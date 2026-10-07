using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LOM_UI_EN
{
	/// <summary>
	/// Scene text: Chinese written into a UGUI Text or TextMeshPro component (serialized labels, Lean scene labels still on the
	/// zh-TW Lean data, text the game builds in code) becomes English through a SceneDictionary, which is what XUnity
	/// AutoTranslator did for the English patch. The translation happens in PREFIXES on the text setters (and on OnEnable for
	/// serialized text), so the component is assigned English straight away: XUnity's postfixes, if XUnity is installed, only
	/// ever see English, the game's own "text unchanged" checks see the final string, and our layout rules run afterwards as
	/// before. When SceneText hands the layer back (Original with a running base patch) the prefixes only record the source.
	///
	/// Text sizes are the layout rules' alone: since 1.2.0 this layer reads no XUnity resizer files. Their sizes became
	/// rules/05_text_sizes.json, whose translatedOnly rules this layer has applied (RuleApplier.ApplyOnTranslation) when it
	/// translates a text, before the text is set, where the resizer applied. Chinese that has no line is written once per
	/// session to translation/untranslated.txt, which replaces XUnity's machine-translation append as the way gaps show up.
	/// </summary>
	public static class SceneText
	{
		public static ConfigEntry<bool> CfgLogUntranslated;
		public static ConfigEntry<bool> CfgLetBaseFillGaps;
		public static ConfigEntry<string> CfgSkipPaths;

		private static SceneDictionary _dict;
		private static bool _inside;
		private static readonly Dictionary<int, Component> _recordComponent = new Dictionary<int, Component>();
		private static readonly Dictionary<int, string> _recordSource = new Dictionary<int, string>();
		private static readonly Dictionary<int, string> _recordResult = new Dictionary<int, string>();
		private static readonly Dictionary<int, bool> _skip = new Dictionary<int, bool>();
		private static readonly HashSet<string> _missPlain = new HashSet<string>(StringComparer.Ordinal);
		private static readonly HashSet<string> _missRich = new HashSet<string>(StringComparer.Ordinal);
		private static readonly HashSet<string> _logged = new HashSet<string>(StringComparer.Ordinal);
		private static readonly List<string> _logBuffer = new List<string>();

		/// <summary>A sighting waiting to be confirmed: only Chinese that is still on screen a moment later is a gap (prefab placeholders are overwritten at once).</summary>
		private struct PendingGap
		{
			public Component Component;
			public string Value;
			public string Scene;
			public float Due;
		}

		private static readonly List<PendingGap> _pending = new List<PendingGap>();
		private static float _nextFlush;
		private static Regex[] _skipPatterns = new Regex[0];
		private static string _skipPatternsSource;
		private static int _maxLength;
		private static int _setDepth;
		private static Component _outerChinese;

		public static long Calls;
		public static long Candidates;
		public static long Hits;
		public static long Misses;
		public static long Ticks;
		public static int SessionUntranslated;
		/// <summary>Scene lines neither this mod nor the original patch's file had that the running XUnity AutoTranslator's dictionary supplied.</summary>
		public static int BaseFills;
		private static readonly Dictionary<string, string> _baseFill = new Dictionary<string, string>(StringComparer.Ordinal);

		/// <summary>The dictionary this layer serves, or null while it is handed back / off.</summary>
		public static SceneDictionary Active => _dict;

		public static string ActiveLabel { get; private set; } = "off";

		/// <summary>This layer translates right now. False while its hooks are missing or turned off: the original patch's XUnity (if installed) then translates on its own.</summary>
		public static bool Serving => _dict != null && F.SceneText.Live && EnglishLanguage.Active;

		public static string UntranslatedPath => Path.Combine(TranslationProfiles.TranslationDir, "untranslated.txt");

		public static void Bind(ConfigFile cfg)
		{
			CfgLogUntranslated = cfg.Bind("Translation", "LogUntranslated", true, "Write Chinese scene text that has no English line to plugins/LOM_UI_EN/translation/untranslated.txt (once per text per session), so gaps can be translated.");
			CfgLetBaseFillGaps = cfg.Bind("Translation", "LetBaseModFillGaps", false, "When the XUnity AutoTranslator of " + Plugin.BaseModName + " is running: let it translate scene text this mod has no line for (from its own file or its online machine translation, which it appends to its file). Off = such text stays Chinese and is logged to untranslated.txt.");
			CfgSkipPaths = cfg.Bind("Translation", "SceneTextSkipPaths", "", "Object paths (glob, ';'-separated, '*' and '**' like the layout rules) whose text this mod never translates.");
		}

		public static void Patch(Harmony h)
		{
			Compat.Setup(F.SceneText, delegate
			{
				F.SceneText.OnTurnedOff = OnTurnedOff;
				F.SceneText.OnTurnedOn = delegate
				{
					Retranslate();
				};
				PatchPrefix(h, AccessTools.PropertySetter(typeof(Text), "text"), "Text.text setter", nameof(TextSet_Prefix), setter: true, essential: true);
				PatchPrefix(h, AccessTools.PropertySetter(typeof(TMP_Text), "text"), "TMP_Text.text setter", nameof(TmpSet_Prefix), setter: true, essential: false);
				PatchPrefix(h, AccessTools.Method(typeof(TMP_Text), "SetText", new Type[2] { typeof(string), typeof(bool) }), "TMP_Text.SetText(string, bool)", nameof(TmpSetText_Prefix), setter: true, essential: false);
				PatchPrefix(h, AccessTools.Method(typeof(Text), "OnEnable"), "Text.OnEnable", nameof(TextOnEnable_Prefix), setter: false, essential: false);
				PatchPrefix(h, AccessTools.Method(typeof(TextMeshProUGUI), "OnEnable"), "TextMeshProUGUI.OnEnable", nameof(TmpOnEnable_Prefix), setter: false, essential: false);
				PatchPrefix(h, AccessTools.Method(typeof(TextMeshPro), "OnEnable"), "TextMeshPro.OnEnable", nameof(TmpOnEnable_Prefix), setter: false, essential: false);
				SceneManager.sceneLoaded += delegate
				{
					try
					{
						PurgeDestroyed();
					}
					catch (Exception ex)
					{
						F.SceneText.Fail(ex, "scene load");
					}
				};
			});
		}

		private static void PatchPrefix(Harmony h, MethodInfo target, string what, string prefix, bool setter, bool essential)
		{
			Compat.Hook(F.SceneText, h, target, what, new HarmonyMethod(typeof(SceneText), prefix)
			{
				priority = Priority.First
			}, null, setter ? new HarmonyMethod(typeof(SceneText), nameof(Setter_Finalizer)) : null, essential);
		}

		/// <summary>Every hook body runs through here: an exception (a game change, a bug) is counted against the feature and never reaches the game.</summary>
		private static void Guarded(Component c, ref string value, bool richText)
		{
			try
			{
				F.SceneText.Probe();
				Handle(c, ref value, richText);
			}
			catch (Exception ex)
			{
				F.SceneText.Fail(ex, "text set");
			}
		}

		/// <summary>Installs a dictionary (null = hand back / off).</summary>
		public static void Use(SceneDictionary dict, string label)
		{
			_dict = dict;
			ActiveLabel = label;
			_missPlain.Clear();
			_missRich.Clear();
			_maxLength = (dict != null) ? (2 * Math.Max(dict.MaxKeyLength, 64)) : 0;
		}

		// ---- hooks ----------------------------------------------------------------------------------------------

		private static void TextSet_Prefix(Text __instance, ref string value)
		{
			_setDepth++;
			if (!_inside && F.SceneText.Live && __instance != null)
			{
				Guarded(__instance, ref value, __instance.supportRichText);
			}
		}

		private static void TmpSet_Prefix(TMP_Text __instance, ref string value)
		{
			_setDepth++;
			if (!_inside && F.SceneText.Live && __instance != null)
			{
				Guarded(__instance, ref value, __instance.richText);
			}
		}

		private static void TmpSetText_Prefix(TMP_Text __instance, ref string sourceText)
		{
			_setDepth++;
			if (!_inside && F.SceneText.Live && __instance != null)
			{
				Guarded(__instance, ref sourceText, __instance.richText);
			}
		}

		/// <summary>
		/// Runs after every other patch of the text setters (XUnity's postfix included). The depth counter tells a nested set
		/// apart: XUnity translates a component inside the postfix of the game's own set, by setting the text again.
		/// </summary>
		private static void Setter_Finalizer()
		{
			if (--_setDepth <= 0)
			{
				_setDepth = 0;
				_outerChinese = null;
			}
		}

		private static void TextOnEnable_Prefix(Text __instance)
		{
			if (_inside || __instance == null || !F.SceneText.Live)
			{
				return;
			}
			try
			{
				F.SceneText.Probe();
				string text = __instance.text;
				string before = text;
				Handle(__instance, ref text, __instance.supportRichText);
				if ((object)text != before)
				{
					SetQuietly(__instance, text);
				}
			}
			catch (Exception ex)
			{
				F.SceneText.Fail(ex, "OnEnable");
			}
		}

		private static void TmpOnEnable_Prefix(TMP_Text __instance)
		{
			if (_inside || __instance == null || !F.SceneText.Live)
			{
				return;
			}
			try
			{
				F.SceneText.Probe();
				string text = __instance.text;
				string before = text;
				Handle(__instance, ref text, __instance.richText);
				if ((object)text != before)
				{
					SetQuietly(__instance, text);
				}
			}
			catch (Exception ex)
			{
				F.SceneText.Fail(ex, "OnEnable");
			}
		}

		private static void SetQuietly(Component c, string text)
		{
			_inside = true;
			try
			{
				if (c is Text t)
				{
					t.text = text;
				}
				else if (c is TMP_Text tmp)
				{
					tmp.text = text;
				}
			}
			finally
			{
				_inside = false;
			}
		}

		/// <summary>The hot path: runs for every text assignment, so it leaves as early as it can.</summary>
		private static void Handle(Component c, ref string value, bool richText)
		{
			Calls++;
			if (c == null)
			{
				return;
			}
			if (string.IsNullOrEmpty(value))
			{
				// Cleared: what the component showed is gone, and so is its record (a switch must not bring it back).
				if (_recordSource.Count > 0)
				{
					Forget(c);
				}
				return;
			}
			if (!EnglishLanguage.Active)
			{
				// Another game language: its text is not ours to record (a later switch must not write old Chinese back).
				if (_recordSource.Count > 0)
				{
					Forget(c);
				}
				return;
			}
			if (_recordResult.Count > 0)
			{
				string ours;
				if (_recordResult.TryGetValue(c.GetInstanceID(), out ours) && string.Equals(ours, value, StringComparison.Ordinal))
				{
					// The component shows what this layer wrote (OnEnable after a re-activation, or the same English set again):
					// keep its record for live switching and do nothing else.
					return;
				}
			}
			SceneDictionary dict = _dict;
			bool cjk = SceneDictionary.ContainsCjk(value);
			if (cjk && (dict == null || value.Length > dict.MaxNonHanKeyLength + 16) && !SceneDictionary.IsTranslatableChinese(value))
			{
				// CJK-range characters but no Chinese one: English with full-width punctuation (about 1,500 story lines, which the
				// Fungus typewriter sets once per glyph). No key that long lacks Chinese, and XUnity would not translate it either.
				cjk = false;
			}
			if (_setDepth == 1)
			{
				_outerChinese = cjk ? c : null;
			}
			if (!cjk)
			{
				ForgetUnlessNested(c);
				if (dict == null || !dict.MayMatchNonCjk(value))
				{
					return;
				}
			}
			if (dict == null)
			{
				// Handed back: remember the source so a later switch to our text can redo it.
				Remember(c, value);
				return;
			}
			if (value.Length > _maxLength || Skip(c) || dict.IsKnownValue(value))
			{
				// Too long for any key, a component this layer leaves alone, or already a translation (our own English that
				// contains full-width punctuation, say): nothing to translate and nothing to record.
				ForgetUnlessNested(c);
				return;
			}
			long t0 = Stopwatch.GetTimestamp();
			Candidates++;
			HashSet<string> misses = richText ? _missRich : _missPlain;
			if (!misses.Contains(value))
			{
				string result;
				if (dict.Translate(value, richText, out result))
				{
					Hits++;
					Remember(c, value);
					_recordResult[c.GetInstanceID()] = result;
					RuleApplier.ApplyOnTranslation(c.gameObject);
					value = result;
					Ticks += Stopwatch.GetTimestamp() - t0;
					return;
				}
				if (misses.Count > 8192)
				{
					misses.Clear();
				}
				misses.Add(value);
			}
			if (SceneDictionary.IsTranslatableChinese(value) && FillFromBase(value, out string filled))
			{
				// A line this mod lacks (new game text after an update, say), from the original patch's own XUnity dictionary.
				BaseFills++;
				Remember(c, value);
				_recordResult[c.GetInstanceID()] = filled;
				RuleApplier.ApplyOnTranslation(c.gameObject);
				value = filled;
				Ticks += Stopwatch.GetTimestamp() - t0;
				return;
			}
			Misses++;
			if (SceneDictionary.IsTranslatableChinese(value))
			{
				Remember(c, value);
				if (CfgLogUntranslated.Value)
				{
					LogUntranslated(c, value);
				}
			}
			else
			{
				ForgetUnlessNested(c);
			}
			Ticks += Stopwatch.GetTimestamp() - t0;
		}

		/// <summary>
		/// A line neither this mod nor the original patch's file has, that the running XUnity AutoTranslator still knows (added to its
		/// dictionary this session): its cached translation. This asks for no machine translation.
		/// </summary>
		private static bool FillFromBase(string value, out string filled)
		{
			filled = null;
			if (!F.XUnityBridge.Live || !XUnityBridge.Registered || (_dict != null && _dict.IsRemoved(value)))
			{
				return false;
			}
			if (_baseFill.TryGetValue(value, out filled))
			{
				return filled != null;
			}
			string en;
			if (XUnityBridge.TryTranslate(value, out en) && !string.IsNullOrEmpty(en) && !string.Equals(en, value, StringComparison.Ordinal) && !SceneDictionary.IsTranslatableChinese(en))
			{
				filled = en;
			}
			if (_baseFill.Count > 8192)
			{
				_baseFill.Clear();
			}
			_baseFill[value] = filled;
			return filled != null;
		}

		/// <summary>
		/// Text that is not a Chinese source replaces the component's record, unless it is the base patch's XUnity translating the
		/// Chinese being set right now (a nested set on the same component), which a later switch must still be able to redo.
		/// </summary>
		private static void ForgetUnlessNested(Component c)
		{
			if (_recordSource.Count > 0 && !(_setDepth > 1 && (object)c == _outerChinese))
			{
				Forget(c);
			}
		}

		private static void Remember(Component c, string source)
		{
			int id = c.GetInstanceID();
			_recordComponent[id] = c;
			_recordSource[id] = source;
			_recordResult.Remove(id);
		}

		private static void Forget(Component c)
		{
			int id = c.GetInstanceID();
			if (_recordSource.Remove(id))
			{
				_recordComponent.Remove(id);
			}
			_recordResult.Remove(id);
		}

		private static void PurgeDestroyed()
		{
			List<int> dead = null;
			foreach (KeyValuePair<int, Component> kv in _recordComponent)
			{
				if (kv.Value == null)
				{
					(dead ?? (dead = new List<int>())).Add(kv.Key);
				}
			}
			if (dead != null)
			{
				foreach (int id in dead)
				{
					_recordComponent.Remove(id);
					_recordSource.Remove(id);
					_recordResult.Remove(id);
					_skip.Remove(id);
				}
			}
			if (_skip.Count > 20000)
			{
				_skip.Clear();
			}
		}

		/// <summary>
		/// Turned off: with the original patch's XUnity running, give the texts this layer translated their Chinese back, which
		/// XUnity then translates as it would without this mod. Without XUnity the English stays on screen (better than
		/// Chinese); new text shows as the game writes it.
		/// </summary>
		private static void OnTurnedOff()
		{
			if (!OriginalMod.Current.XUnityRunning)
			{
				return;
			}
			Retranslate(force: true);
		}

		/// <summary>After a profile switch: give every recorded component its Chinese source again, which the prefix translates (or passes on) under the new settings.</summary>
		public static int Retranslate(bool force = false)
		{
			PurgeDestroyed();
			// Not while this layer is off (it could not translate them again): the texts would drop back to Chinese.
			if (!EnglishLanguage.Active || (!force && !F.SceneText.Live))
			{
				return 0;
			}
			int n = 0;
			foreach (KeyValuePair<int, Component> kv in _recordComponent.ToList())
			{
				string source;
				if (kv.Value == null || !_recordSource.TryGetValue(kv.Key, out source))
				{
					continue;
				}
				try
				{
					if (kv.Value is Text t)
					{
						t.text = source;
						n++;
					}
					else if (kv.Value is TMP_Text tmp)
					{
						tmp.text = source;
						n++;
					}
				}
				catch (Exception)
				{
				}
			}
			return n;
		}

		public static int Recorded => _recordSource.Count;

		/// <summary>After [Translation] SceneTextSkipPaths changes: decide every component again.</summary>
		public static void ClearSkipCache()
		{
			_skip.Clear();
			_missPlain.Clear();
			_missRich.Clear();
		}

		/// <summary>XUnity's ShouldIgnoreTextComponent (an input field's own text, except its placeholder) plus our window and the configured skip paths.</summary>
		private static bool Skip(Component c)
		{
			int id = c.GetInstanceID();
			bool skip;
			if (_skip.TryGetValue(id, out skip))
			{
				return skip;
			}
			skip = false;
			try
			{
				Transform root = c.transform.root;
				if (root != null && root.name == "LOM_ModMenu")
				{
					skip = true;
				}
				else
				{
					InputField input = c.GetComponentInParent<InputField>();
					if (input != null)
					{
						skip = input.placeholder != c;
					}
					else
					{
						TMP_InputField tmpInput = c.GetComponentInParent<TMP_InputField>();
						if (tmpInput != null)
						{
							skip = tmpInput.placeholder != c;
						}
					}
					if (!skip && CfgSkipPaths.Value.Length > 0)
					{
						skip = MatchesSkipPath(PathUtil.GetPath(c.transform));
					}
				}
			}
			catch (Exception)
			{
			}
			_skip[id] = skip;
			return skip;
		}

		private static bool MatchesSkipPath(string path)
		{
			string source = CfgSkipPaths.Value;
			if (!string.Equals(source, _skipPatternsSource, StringComparison.Ordinal))
			{
				_skipPatternsSource = source;
				_skipPatterns = source.Split(new char[1] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(delegate(string g)
				{
					string rx = "^" + Regex.Escape(g.Trim()).Replace("\\*\\*", ".*").Replace("\\*", "[^/]*") + "$";
					return new Regex(rx, RegexOptions.CultureInvariant);
				}).ToArray();
			}
			foreach (Regex rx in _skipPatterns)
			{
				if (rx.IsMatch(path))
				{
					return true;
				}
			}
			return false;
		}

		// ---- untranslated log -------------------------------------------------------------------------------------

		/// <summary>A gap found by another layer (a game data key nobody translated): scene column, then key, then the game's text.</summary>
		public static void LogGap(string scene, string path, string value)
		{
			if (CfgLogUntranslated == null || !CfgLogUntranslated.Value)
			{
				return;
			}
			// Game data lookups may come from other threads than the scene text layer's.
			lock (_logBuffer)
			{
				if (_logBuffer.Count > 5000)
				{
					return;
				}
				SessionUntranslated++;
				_logBuffer.Add(scene + "\t" + path + "\t" + Escape(value));
			}
		}

		/// <summary>The Lean label postfix replaced this Chinese with an override or table text: it is not a gap after all.</summary>
		public static void Unlog(string value)
		{
			if (string.IsNullOrEmpty(value) || !_logged.Remove(value))
			{
				return;
			}
			for (int i = _pending.Count - 1; i >= 0; i--)
			{
				if (string.Equals(_pending[i].Value, value, StringComparison.Ordinal))
				{
					_pending.RemoveAt(i);
					return;
				}
			}
			string tail = "\t" + Escape(value);
			for (int i = _logBuffer.Count - 1; i >= 0; i--)
			{
				if (_logBuffer[i].EndsWith(tail, StringComparison.Ordinal))
				{
					_logBuffer.RemoveAt(i);
					SessionUntranslated--;
					break;
				}
			}
		}

		/// <summary>Whether this component's text is left alone (skip paths, input fields, the Mod Settings window).</summary>
		public static bool IsSkipped(Component c)
		{
			return c != null && Skip(c);
		}

		private static void LogUntranslated(Component c, string value)
		{
			if (_logged.Count > 50000 || _pending.Count > 2000 || !_logged.Add(value))
			{
				return;
			}
			try
			{
				_pending.Add(new PendingGap
				{
					Component = c,
					Value = value,
					Scene = SceneManager.GetActiveScene().name,
					Due = Time.realtimeSinceStartup + 1.5f
				});
			}
			catch (Exception)
			{
				_logged.Remove(value);
			}
		}

		/// <summary>Confirms pending sightings whose component still shows the same Chinese, active, 1.5 s later.</summary>
		private static void ConfirmPending()
		{
			float now = Time.realtimeSinceStartup;
			for (int i = _pending.Count - 1; i >= 0; i--)
			{
				PendingGap g = _pending[i];
				if (g.Due > now)
				{
					continue;
				}
				_pending.RemoveAt(i);
				bool shown = false;
				try
				{
					Component c = g.Component;
					if (c != null && c.gameObject.activeInHierarchy)
					{
						string current = (c is Text t) ? t.text : ((c is TMP_Text tmp) ? tmp.text : null);
						shown = string.Equals(current, g.Value, StringComparison.Ordinal);
					}
					if (shown)
					{
						SessionUntranslated++;
						_logBuffer.Add(g.Scene + "\t" + PathUtil.GetPath(c.transform) + "\t" + Escape(g.Value));
					}
				}
				catch (Exception)
				{
				}
				if (!shown)
				{
					// Gone or replaced: may be logged when it really shows.
					_logged.Remove(g.Value);
				}
			}
		}

		public static string Escape(string s)
		{
			return s.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t").Replace("=", "\\=");
		}

		/// <summary>Plugin.Update: flushes the untranslated log every few seconds and keeps the XUnity callback registered.</summary>
		public static void Tick()
		{
			if (!XUnityBridge.Registered && Time.frameCount % 120 == 0)
			{
				XUnityBridge.TryRegister();
			}
			if (_pending.Count > 0 && Time.frameCount % 15 == 0)
			{
				ConfirmPending();
			}
		}

		/// <summary>Plugin.Update, whatever state the scene text layer is in: the game data layer logs its gaps here too.</summary>
		public static void FlushLog()
		{
			if (_logBuffer.Count == 0 || Time.realtimeSinceStartup < _nextFlush)
			{
				return;
			}
			_nextFlush = Time.realtimeSinceStartup + 3f;
			string chunk;
			lock (_logBuffer)
			{
				chunk = string.Join("\n", _logBuffer) + "\n";
				_logBuffer.Clear();
			}
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(UntranslatedPath));
				File.AppendAllText(UntranslatedPath, chunk, new UTF8Encoding(false));
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("untranslated log write failed: " + ex.Message);
			}
		}

		public static string StatsLine()
		{
			double ms = Ticks * 1000.0 / Stopwatch.Frequency;
			return $"scene text {ActiveLabel}: {Calls} text sets, {Candidates} candidates, {Hits} translated, {Misses} without a line, {ms:F1} ms total, {Recorded} recorded, {SessionUntranslated} logged as untranslated";
		}
	}

	/// <summary>
	/// Talks to a running XUnity AutoTranslator (the original English patch) through its public ITranslator interface, by
	/// reflection only: registers an OnTranslating callback that tells XUnity to leave a component alone while our scene
	/// layer serves it (our English is already in the component; without this XUnity would also send Chinese gaps to Google
	/// and append the results to the base patch's file), and offers TryTranslate for the detector's data check.
	/// </summary>
	public static class XUnityBridge
	{
		private static object _translator;
		private static Type _itranslator;
		private static PropertyInfo _ctxOriginalText;
		private static PropertyInfo _ctxComponent;
		private static MethodInfo _ctxIgnore;
		private static MethodInfo _tryTranslate;
		private static int _attempts;

		public static bool Registered { get; private set; }

		public static bool Available => Translator() != null;

		private static object Translator()
		{
			if (_translator != null)
			{
				return _translator;
			}
			try
			{
				Type at = TranslationProfiles.FindType("XUnity.AutoTranslator.Plugin.Core", "XUnity.AutoTranslator.Plugin.Core.AutoTranslator");
				_itranslator = TranslationProfiles.FindType("XUnity.AutoTranslator.Plugin.Core", "XUnity.AutoTranslator.Plugin.Core.ITranslator");
				if (at == null || _itranslator == null)
				{
					return null;
				}
				_translator = AccessTools.Property(at, "Default")?.GetValue(null, null);
			}
			catch (Exception)
			{
				_translator = null;
			}
			return _translator;
		}

		public static void TryRegister()
		{
			if (Registered || _attempts > 20)
			{
				return;
			}
			_attempts++;
			try
			{
				object tr = Translator();
				if (tr == null)
				{
					F.XUnityBridge.NotNeededWhy = "the XUnity AutoTranslator of " + Plugin.BaseModName + " is not running";
					return;
				}
				F.XUnityBridge.NotNeededWhy = null;
				F.XUnityBridge.HooksWanted = 1;
				if (F.XUnityBridge.SimulateInstall)
				{
					F.XUnityBridge.Problems.Add("ITranslator.RegisterOnTranslatingCallback not found (simulated)");
					F.XUnityBridge.EssentialMissing = true;
					_attempts = 99;
					return;
				}
				Type ctx = TranslationProfiles.FindType("XUnity.AutoTranslator.Plugin.Core", "XUnity.AutoTranslator.Plugin.Core.ComponentTranslationContext");
				MethodInfo register = _itranslator.GetMethod("RegisterOnTranslatingCallback");
				if (ctx == null || register == null)
				{
					Plugin.Log.LogWarning("XUnity bridge: ITranslator.RegisterOnTranslatingCallback not found (other XUnity version?)");
					F.XUnityBridge.Problems.Add("ITranslator.RegisterOnTranslatingCallback not found (another XUnity version?)");
					F.XUnityBridge.EssentialMissing = true;
					_attempts = 99;
					return;
				}
				_ctxOriginalText = ctx.GetProperty("OriginalText");
				_ctxComponent = ctx.GetProperty("Component");
				_ctxIgnore = ctx.GetMethod("IgnoreComponent");
				Type actionType = typeof(Action<>).MakeGenericType(ctx);
				Delegate callback = Delegate.CreateDelegate(actionType, typeof(XUnityBridge).GetMethod(nameof(OnTranslating), BindingFlags.NonPublic | BindingFlags.Static));
				register.Invoke(tr, new object[1] { callback });
				Registered = true;
				F.XUnityBridge.HooksInstalled = 1;
				Plugin.Log.LogInfo("XUnity bridge: OnTranslating callback registered (XUnity leaves components alone while this mod serves scene text)");
			}
			catch (Exception ex)
			{
				_attempts = 99;
				Plugin.Log.LogWarning("XUnity bridge failed: " + ex.Message);
				F.XUnityBridge.Problems.Add("registration failed: " + ex.Message);
				F.XUnityBridge.EssentialMissing = true;
			}
		}

		private static void OnTranslating(object context)
		{
			if (!F.XUnityBridge.Live)
			{
				return;
			}
			try
			{
				F.XUnityBridge.Probe();
				if (!SceneText.Serving)
				{
					return;
				}
				if (SceneText.CfgLetBaseFillGaps.Value && !(_ctxComponent?.GetValue(context, null) is Component comp && SceneText.IsSkipped(comp)))
				{
					string original = _ctxOriginalText?.GetValue(context, null) as string;
					SceneDictionary d = SceneText.Active;
					// A gap is Chinese this layer has no line for; its own output (some revised lines still contain Chinese)
					// must never go to XUnity's machine translation.
					if (original != null && SceneDictionary.IsTranslatableChinese(original) && (d == null || (!d.IsKnownValue(original) && !d.ContainsKey(original))))
					{
						return;
					}
				}
				_ctxIgnore?.Invoke(context, null);
			}
			catch (Exception ex)
			{
				F.XUnityBridge.Fail(ex, "OnTranslating");
			}
		}

		/// <summary>ITranslator.TryTranslate(zh, out en): XUnity's own dictionary (for detecting which data the base patch has).</summary>
		public static bool TryTranslate(string text, out string result)
		{
			result = null;
			try
			{
				object tr = Translator();
				if (tr == null)
				{
					return false;
				}
				if (_tryTranslate == null)
				{
					_tryTranslate = _itranslator.GetMethods().FirstOrDefault((MethodInfo m) => m.Name == "TryTranslate" && m.GetParameters().Length == 2);
				}
				if (_tryTranslate == null)
				{
					return false;
				}
				object[] args = new object[2] { text, null };
				bool ok = (bool)_tryTranslate.Invoke(tr, args);
				result = args[1] as string;
				return ok;
			}
			catch (Exception)
			{
				return false;
			}
		}
	}
}
