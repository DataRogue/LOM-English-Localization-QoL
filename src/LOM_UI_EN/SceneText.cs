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
	/// The resizer files (XUnity *.resizer.txt) are applied exactly when XUnity would have applied them: to a component whose text
	/// this layer has just translated, before the text is set (SceneResizeTable). Chinese that has no line is written once per
	/// session to translation/untranslated.txt, which replaces XUnity's machine-translation append as the way gaps show up.
	/// </summary>
	public static class SceneText
	{
		public static ConfigEntry<bool> CfgLogUntranslated;
		public static ConfigEntry<bool> CfgApplyResizers;
		public static ConfigEntry<bool> CfgWideTextOverflow;
		public static ConfigEntry<bool> CfgLetBaseFillGaps;
		public static ConfigEntry<string> CfgSkipPaths;

		private static SceneDictionary _dict;
		private static SceneResizeTable _resize = new SceneResizeTable();
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
		private static string _resizeDir;
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

		public static int ResizeNodes => _resize.Nodes;

		public static string UntranslatedPath => Path.Combine(TranslationProfiles.TranslationDir, "untranslated.txt");

		public static void Bind(ConfigFile cfg)
		{
			CfgLogUntranslated = cfg.Bind("Translation", "LogUntranslated", true, "Write Chinese scene text that has no English line to plugins/LOM_UI_EN/translation/untranslated.txt (once per text per session), so gaps can be translated.");
			CfgApplyResizers = cfg.Bind("Translation", "ApplyResizers", true, "Apply the resizer files (translation/scene/resize/*.resizer.txt, XUnity format) to text this mod translates, the way XUnity AutoTranslator applied them.");
			CfgWideTextOverflow = cfg.Bind("Translation", "WideTextOverflow", true, "XUnity's implicit resize: a translated Text wider than a quarter of the screen, without best fit, may wrap and overflow vertically; a TextMeshPro in Masking overflow switches to Truncate.");
			CfgLetBaseFillGaps = cfg.Bind("Translation", "LetBaseModFillGaps", false, "When the original English patch's XUnity AutoTranslator is running: let it translate scene text this mod has no line for (from its own file or its online machine translation, which it appends to its file). Off = such text stays Chinese and is logged to untranslated.txt.");
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

		/// <summary>Installs a dictionary (null = hand back / off) and the resizers that go with it.</summary>
		public static void Use(SceneDictionary dict, string label, string resizeDir)
		{
			_dict = dict;
			ActiveLabel = label;
			_missPlain.Clear();
			_missRich.Clear();
			_maxLength = (dict != null) ? (2 * Math.Max(dict.MaxKeyLength, 64)) : 0;
			if (dict == null)
			{
				// Handed back or off: put back the font sizes and line spacings the resizers changed, so the base patch (or the
				// game) starts from the component's own values, as XUnity's UnresizeUI does.
				SceneResizeTable.Unresize();
				_resize = new SceneResizeTable();
				_resizeDir = null;
			}
			else if (_resizeDir == null || !string.Equals(_resizeDir, resizeDir ?? "", StringComparison.OrdinalIgnoreCase))
			{
				_resize = SceneResizeTable.Load(resizeDir);
				_resizeDir = resizeDir ?? "";
			}
		}

		/// <summary>Re-read the resizer files on the next Use (Reload translation files).</summary>
		public static void ReloadResizersOnNextUse()
		{
			_resizeDir = null;
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
					if (CfgApplyResizers.Value)
					{
						_resize.Apply(c, CfgWideTextOverflow.Value);
					}
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
				if (CfgApplyResizers.Value)
				{
					_resize.Apply(c, CfgWideTextOverflow.Value);
				}
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
			SceneResizeTable.NoteOriginal(c, id);
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
					SceneResizeTable.Forget(id);
				}
			}
			if (_skip.Count > 20000)
			{
				_skip.Clear();
			}
		}

		/// <summary>
		/// Turned off: with the original patch's XUnity running, undo this layer's resizes and give the texts it translated their
		/// Chinese back, which XUnity then translates as it would without this mod. Without XUnity the English stays on screen
		/// (better than Chinese); new text shows as the game writes it.
		/// </summary>
		private static void OnTurnedOff()
		{
			if (!OriginalMod.Current.XUnityRunning)
			{
				return;
			}
			SceneResizeTable.Unresize();
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
	/// XUnity's UI resizer (UIResizeCache / UIResizeAttachment / DefaultTextComponentManipulator.ResizeUI), for the
	/// *.resizer.txt files the English patch ships. Lines are 'path=Command(args);Command(args)'; a path matches from the scene
	/// root down, and every matching ancestor contributes its commands (a deeper node overrides per command slot). Files load
	/// in reverse name order, later ones overriding. Applied only to components this mod translated, as XUnity did.
	/// </summary>
	public sealed class SceneResizeTable
	{
		private sealed class Node
		{
			public readonly Dictionary<string, Node> Children = new Dictionary<string, Node>(StringComparer.Ordinal);
			public Result Result;
		}

		private sealed class Result
		{
			public Func<int, int?> FontSize;       // ChangeFontSize / ByPercentage / IgnoreFontSize
			public bool HasAutoResize;
			public bool AutoResize;
			public double? MinSize;
			public double? MaxSize;
			public Func<float, float?> LineSpacing;
			public int? HOverflow;
			public int? VOverflow;
			public int? TmpOverflow;
			public int? TmpAlignment;

			public Result Copy()
			{
				return (Result)MemberwiseClone();
			}

			public void MergeInto(Result o)
			{
				if (o == null)
				{
					return;
				}
				if (o.FontSize != null) FontSize = o.FontSize;
				if (o.HasAutoResize) { HasAutoResize = true; AutoResize = o.AutoResize; MinSize = o.MinSize; MaxSize = o.MaxSize; }
				if (o.LineSpacing != null) LineSpacing = o.LineSpacing;
				if (o.HOverflow.HasValue) HOverflow = o.HOverflow;
				if (o.VOverflow.HasValue) VOverflow = o.VOverflow;
				if (o.TmpOverflow.HasValue) TmpOverflow = o.TmpOverflow;
				if (o.TmpAlignment.HasValue) TmpAlignment = o.TmpAlignment;
			}
		}

		private static readonly Regex CommandRegex = new Regex("^\\s*(.+)\\s*\\(([\\s\\S]*)\\)\\s*$", RegexOptions.CultureInvariant);
		private static readonly char[] ArgSplitters = new char[2] { ',', ' ' };
		private readonly Node _root = new Node();
		// Per component, across table reloads and profile switches (a new SceneResizeTable must not shrink a label twice):
		// the size and spacing a component had before any resize (noted when its Chinese source is first recorded, before
		// our prefix or XUnity's postfix resizes it), and the values this mod last wrote, as XUnity's _alteredFontSize.
		private static readonly Dictionary<int, int> _originalFontSize = new Dictionary<int, int>();
		private static readonly Dictionary<int, float> _originalLineSpacing = new Dictionary<int, float>();
		private static readonly Dictionary<int, int> _alteredFontSize = new Dictionary<int, int>();
		private static readonly Dictionary<int, float> _alteredLineSpacing = new Dictionary<int, float>();
		private static readonly Dictionary<int, Component> _resized = new Dictionary<int, Component>();
		private static readonly Stack<Transform> _pathStack = new Stack<Transform>(32);

		public int Nodes { get; private set; }
		public int Commands { get; private set; }
		public int BadCommands { get; private set; }

		/// <summary>Called when a component's Chinese source is recorded: keep its own font size and line spacing once.</summary>
		public static void NoteOriginal(Component c, int id)
		{
			if (_originalFontSize.ContainsKey(id))
			{
				return;
			}
			if (c is Text t)
			{
				_originalFontSize[id] = t.fontSize;
				_originalLineSpacing[id] = t.lineSpacing;
			}
			else if (c is TMP_Text tmp)
			{
				_originalFontSize[id] = (int)tmp.fontSize;
			}
		}

		public static void Forget(int id)
		{
			_originalFontSize.Remove(id);
			_originalLineSpacing.Remove(id);
			_alteredFontSize.Remove(id);
			_alteredLineSpacing.Remove(id);
			_resized.Remove(id);
		}

		/// <summary>XUnity's UnresizeUI for every component this mod resized: the original size and spacing come back where ours still show.</summary>
		public static void Unresize()
		{
			foreach (KeyValuePair<int, Component> kv in _resized)
			{
				try
				{
					int orig;
					int altered;
					float origLs;
					float alteredLs;
					if (kv.Value is Text t && t != null)
					{
						if (_alteredFontSize.TryGetValue(kv.Key, out altered) && t.fontSize == altered && _originalFontSize.TryGetValue(kv.Key, out orig))
						{
							t.fontSize = orig;
						}
						if (_alteredLineSpacing.TryGetValue(kv.Key, out alteredLs) && t.lineSpacing == alteredLs && _originalLineSpacing.TryGetValue(kv.Key, out origLs))
						{
							t.lineSpacing = origLs;
						}
					}
					else if (kv.Value is TMP_Text tmp && tmp != null)
					{
						if (_alteredFontSize.TryGetValue(kv.Key, out altered) && (int)tmp.fontSize == altered && _originalFontSize.TryGetValue(kv.Key, out orig))
						{
							tmp.fontSize = orig;
						}
					}
				}
				catch (Exception)
				{
				}
			}
			_resized.Clear();
			_alteredFontSize.Clear();
			_alteredLineSpacing.Clear();
		}

		/// <summary>The base a size command works from: the component's own size when it is known and ours (or XUnity's) is showing.</summary>
		private static int FontBase(int id, int current)
		{
			int orig;
			int altered;
			if (_originalFontSize.TryGetValue(id, out orig) && (!_alteredFontSize.TryGetValue(id, out altered) || altered != current))
			{
				return orig;
			}
			return current;
		}

		/// <summary>The resizer files of one folder, or of several separated by ';' in that order (a later folder's lines win: the original patch's first, this mod's own after).</summary>
		public static SceneResizeTable Load(string dirs)
		{
			SceneResizeTable t = new SceneResizeTable();
			if (string.IsNullOrEmpty(dirs))
			{
				return t;
			}
			foreach (string dir in dirs.Split(new char[1] { ';' }, StringSplitOptions.RemoveEmptyEntries))
			{
				if (!Directory.Exists(dir))
				{
					continue;
				}
				string[] files = Directory.GetFiles(dir, "*resizer.txt", SearchOption.AllDirectories);
				Array.Sort(files, StringComparer.OrdinalIgnoreCase);
				Array.Reverse(files);
				foreach (string f in files)
				{
					try
					{
						t.LoadFile(f);
						t.FileCount++;
					}
					catch (Exception ex)
					{
						Plugin.Log?.LogWarning("resizer file " + Path.GetFileName(f) + ": " + ex.Message);
					}
				}
			}
			return t;
		}

		public int FileCount { get; private set; }

		private void LoadFile(string path)
		{
			string all;
			using (StreamReader r = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
			{
				all = r.ReadToEnd();
			}
			foreach (string line in all.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
			{
				if (line.StartsWith("#set ", StringComparison.Ordinal) || line.StartsWith("#unset ", StringComparison.Ordinal) || line.StartsWith("#enable ", StringComparison.Ordinal))
				{
					continue;
				}
				string[] kv;
				try
				{
					kv = SceneDictionary.ReadTranslationLineAndDecode(line);
				}
				catch (Exception)
				{
					continue;
				}
				if (kv == null || string.IsNullOrEmpty(kv[0]) || string.IsNullOrEmpty(kv[1]))
				{
					continue;
				}
				Add(kv[0], kv[1]);
			}
		}

		private void Add(string path, string commands)
		{
			Node node = _root;
			foreach (string seg in path.Split(new char[1] { '/' }, StringSplitOptions.RemoveEmptyEntries))
			{
				Node next;
				if (!node.Children.TryGetValue(seg, out next))
				{
					next = new Node();
					node.Children[seg] = next;
					Nodes++;
				}
				node = next;
			}
			foreach (string cmd in commands.Split(new char[1] { ';' }, StringSplitOptions.RemoveEmptyEntries))
			{
				Match m = CommandRegex.Match(cmd);
				if (!m.Success)
				{
					BadCommands++;
					continue;
				}
				try
				{
					string name = m.Groups[1].Value.Trim();
					string[] args = m.Groups[2].Value.Split(ArgSplitters, StringSplitOptions.RemoveEmptyEntries);
					Result r = node.Result ?? (node.Result = new Result());
					Apply(r, name, args);
					Commands++;
				}
				catch (Exception)
				{
					// Invalid arguments (e.g. TMP_Alignment(Middle), which is not a TextAlignmentOptions name): XUnity skips the command too.
					BadCommands++;
				}
			}
		}

		private static void Apply(Result r, string name, string[] args)
		{
			switch (name.ToLowerInvariant())
			{
			case "changefontsize":
			{
				Need(args, 1);
				int size = int.Parse(args[0], CultureInfo.InvariantCulture);
				r.FontSize = (int current) => size;
				break;
			}
			case "changefontsizebypercentage":
			{
				Need(args, 1);
				double perc = double.Parse(args[0], CultureInfo.InvariantCulture);
				r.FontSize = (int current) => (int)(current * perc);
				break;
			}
			case "ignorefontsize":
				r.FontSize = (int current) => null;
				break;
			case "autoresize":
				if (args.Length < 1)
				{
					throw new ArgumentException("AutoResize needs arguments");
				}
				r.HasAutoResize = true;
				r.AutoResize = bool.Parse(args[0]);
				r.MinSize = (args.Length >= 2) ? ParseMinMax(args[1]) : null;
				r.MaxSize = (args.Length >= 3) ? ParseMinMax(args[2]) : null;
				break;
			case "ugui_changelinespacing":
			{
				Need(args, 1);
				float v = float.Parse(args[0], CultureInfo.InvariantCulture);
				r.LineSpacing = (float current) => v;
				break;
			}
			case "ugui_changelinespacingbypercentage":
			{
				Need(args, 1);
				float p = float.Parse(args[0], CultureInfo.InvariantCulture);
				r.LineSpacing = (float current) => current * p;
				break;
			}
			case "ugui_horizontaloverflow":
				Need(args, 1);
				r.HOverflow = (int)Enum.Parse(typeof(HorizontalWrapMode), args[0], ignoreCase: true);
				break;
			case "ugui_verticaloverflow":
				Need(args, 1);
				r.VOverflow = (int)Enum.Parse(typeof(VerticalWrapMode), args[0], ignoreCase: true);
				break;
			case "tmp_overflow":
				Need(args, 1);
				r.TmpOverflow = (int)Enum.Parse(typeof(TextOverflowModes), args[0], ignoreCase: true);
				break;
			case "tmp_alignment":
				Need(args, 1);
				r.TmpAlignment = (int)Enum.Parse(typeof(TextAlignmentOptions), args[0], ignoreCase: true);
				break;
			default:
				throw new ArgumentException("unknown command " + name);
			}
		}

		private static void Need(string[] args, int n)
		{
			if (args.Length != n)
			{
				throw new ArgumentException("wrong argument count");
			}
		}

		private static double? ParseMinMax(string arg)
		{
			if (string.Equals(arg, "keep", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			if (string.Equals(arg, "none", StringComparison.OrdinalIgnoreCase))
			{
				return double.NaN;
			}
			return double.Parse(arg, CultureInfo.InvariantCulture);
		}

		private Result Lookup(Transform t)
		{
			if (_root.Children.Count == 0)
			{
				return null;
			}
			_pathStack.Clear();
			for (Transform x = t; x != null; x = x.parent)
			{
				_pathStack.Push(x);
			}
			Node node = _root;
			Result result = null;
			while (_pathStack.Count > 0)
			{
				Transform seg = _pathStack.Pop();
				Node next;
				if (!node.Children.TryGetValue(seg.name, out next))
				{
					break;
				}
				if (result == null)
				{
					result = next.Result?.Copy();
				}
				else
				{
					result.MergeInto(next.Result);
				}
				node = next;
			}
			_pathStack.Clear();
			return result;
		}

		/// <summary>DefaultTextComponentManipulator.ResizeUI for UGUI Text and TextMeshPro.</summary>
		public void Apply(Component c, bool implicitEffects)
		{
			try
			{
				if (c is Text text)
				{
					ApplyText(text, implicitEffects);
				}
				else if (c is TMP_Text tmp)
				{
					ApplyTmp(tmp, implicitEffects);
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("resize failed on " + PathUtil.GetPath(c.transform) + ": " + ex.Message);
			}
		}

		private void ApplyText(Text t, bool implicitEffects)
		{
			float width = t.rectTransform.rect.width;
			bool wide = width > (float)(Screen.width / 4);
			bool setLineSpacing = false;
			bool setH = false;
			bool setV = false;
			Result r = Lookup(t.transform);
			int id = t.GetInstanceID();
			if (r != null)
			{
				if (r.HasAutoResize)
				{
					double min = r.MinSize ?? 1.0;
					t.resizeTextMinSize = double.IsNaN(min) ? 1 : (int)min;
					if (r.MaxSize.HasValue)
					{
						t.resizeTextMaxSize = double.IsNaN(r.MaxSize.Value) ? 1 : (int)r.MaxSize.Value;
					}
					t.resizeTextForBestFit = r.AutoResize;
				}
				if (r.FontSize != null)
				{
					int current = t.fontSize;
					int altered;
					if (!_alteredFontSize.TryGetValue(id, out altered) || altered != current)
					{
						int? size = r.FontSize(FontBase(id, current));
						if (size.HasValue)
						{
							t.fontSize = size.Value;
							_alteredFontSize[id] = size.Value;
							_resized[id] = t;
						}
					}
				}
				if (r.LineSpacing != null)
				{
					float current = t.lineSpacing;
					float altered;
					if (!_alteredLineSpacing.TryGetValue(id, out altered) || altered != current)
					{
						float orig;
						float? ls = r.LineSpacing(_originalLineSpacing.TryGetValue(id, out orig) ? orig : current);
						if (ls.HasValue)
						{
							setLineSpacing = true;
							t.lineSpacing = ls.Value;
							_alteredLineSpacing[id] = ls.Value;
							_resized[id] = t;
						}
					}
				}
				if (r.HOverflow.HasValue)
				{
					setH = true;
					t.horizontalOverflow = (HorizontalWrapMode)r.HOverflow.Value;
				}
				if (r.VOverflow.HasValue)
				{
					setV = true;
					t.verticalOverflow = (VerticalWrapMode)r.VOverflow.Value;
				}
			}
			if (!implicitEffects || !wide || t.resizeTextForBestFit)
			{
				return;
			}
			if (!setV)
			{
				t.verticalOverflow = VerticalWrapMode.Overflow;
			}
			if (!setH)
			{
				t.horizontalOverflow = HorizontalWrapMode.Wrap;
			}
		}

		private void ApplyTmp(TMP_Text t, bool implicitEffects)
		{
			TextOverflowModes originalOverflow = t.overflowMode;
			bool setOverflow = false;
			Result r = Lookup(t.transform);
			int id = t.GetInstanceID();
			if (r != null)
			{
				if (r.TmpOverflow.HasValue)
				{
					setOverflow = true;
					t.overflowMode = (TextOverflowModes)r.TmpOverflow.Value;
				}
				if (r.TmpAlignment.HasValue)
				{
					t.alignment = (TextAlignmentOptions)r.TmpAlignment.Value;
				}
				if (r.HasAutoResize)
				{
					if (r.MinSize.HasValue)
					{
						t.fontSizeMin = double.IsNaN(r.MinSize.Value) ? 0f : (float)r.MinSize.Value;
					}
					if (r.MaxSize.HasValue)
					{
						t.fontSizeMax = double.IsNaN(r.MaxSize.Value) ? float.MaxValue : (float)r.MaxSize.Value;
					}
					t.enableAutoSizing = r.AutoResize;
				}
				if (r.FontSize != null)
				{
					int current = (int)t.fontSize;
					int altered;
					if (!_alteredFontSize.TryGetValue(id, out altered) || altered != current)
					{
						int? size = r.FontSize(FontBase(id, current));
						if (size.HasValue)
						{
							t.fontSize = size.Value;
							_alteredFontSize[id] = size.Value;
							_resized[id] = t;
						}
					}
				}
			}
			if (implicitEffects && !setOverflow && originalOverflow == TextOverflowModes.Masking)
			{
				t.overflowMode = TextOverflowModes.Truncate;
			}
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
					F.XUnityBridge.NotNeededWhy = "XUnity AutoTranslator (the original patch) is not running";
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
