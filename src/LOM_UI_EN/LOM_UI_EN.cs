using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
#if !BIE5
using BepInEx.Unity.Mono;
#endif
using DG.Tweening;
using Fungus;
using HarmonyLib;
using Lean.Localization;
using Microsoft.CodeAnalysis;
using MoonSharp.Interpreter;
using Mortal.Combat;
using Mortal.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[assembly: TargetFramework(".NETFramework,Version=v4.7.2", FrameworkDisplayName = ".NET Framework 4.7.2")]
namespace LOM_UI_EN
{
	public class DevHarness : MonoBehaviour
	{
		private string _dir;

		private string _outDir;

		private string _cmdFile;

		private bool _busy;

		private StringBuilder _log = new StringBuilder();

		private static readonly Vector3[] _corners = new Vector3[4];

		private static readonly HashSet<string> _skip = new HashSet<string> { "RectTransform", "Transform", "CanvasRenderer", "UIFixMarker" };

		private void Awake()
		{
			// Outside the plugin folder (BepInEx/cache/LOM_UI_EN/harness): its dumps and logs hold rendered text of the original
			// patch and must never be packaged with this mod.
			_dir = Path.Combine(Path.Combine(BepInEx.Paths.CachePath, "LOM_UI_EN"), "harness");
			_outDir = Path.Combine(_dir, "out");
			_cmdFile = Path.Combine(_dir, "cmd.txt");
			Directory.CreateDirectory(_outDir);
			Log("harness ready");
		}

		private void Update()
		{
			if (!_busy && Plugin.CfgHarness.Value && File.Exists(_cmdFile))
			{
				string[] lines;
				try
				{
					lines = File.ReadAllLines(_cmdFile);
					File.Delete(_cmdFile);
				}
				catch (IOException)
				{
					return;
				}
				StartCoroutine(RunBatch(lines));
			}
		}

		public void HotkeyDump()
		{
			if (_dir == null)
			{
				Awake();
			}
			string text = "hotkey_" + DateTime.Now.ToString("HHmmss");
			StartCoroutine(RunBatch(new string[3]
			{
				"batch " + text,
				"dump " + text,
				"screenshot " + text
			}));
		}

		private void Log(string s)
		{
			string text = DateTime.Now.ToString("HH:mm:ss.fff") + " " + s;
			Plugin.Log.LogInfo("[harness] " + s);
			try
			{
				File.AppendAllText(Path.Combine(_outDir, "log.txt"), text + "\n");
			}
			catch
			{
			}
		}

		private IEnumerator RunBatch(string[] lines)
		{
			_busy = true;
			string batch = "batch";
			foreach (string text in lines)
			{
				string line = text.Trim();
				if (line.Length == 0 || line.StartsWith("#"))
				{
					continue;
				}
				int num = line.IndexOf(' ');
				string text2 = ((num < 0) ? line : line.Substring(0, num)).ToLowerInvariant();
				string text3 = ((num < 0) ? "" : line.Substring(num + 1).Trim());
				IEnumerator co = null;
				try
				{
					switch (text2)
					{
					case "batch":
						batch = text3;
						Log("batch " + batch);
						break;
					case "wait":
						co = WaitCmd(text3);
						break;
					case "screenshot":
						co = ScreenshotCmd(text3);
						break;
					case "dump":
						DumpCmd(text3);
						break;
					case "find":
						FindCmd(text3);
						break;
					case "click":
						ClickCmd(text3);
						break;
					case "mouseclick":
						co = MouseClickCmd(text3);
						break;
					case "mousemove":
						co = MouseMoveCmd(text3);
						break;
					case "say":
						SayCmd(text3);
						break;
					case "menu":
						MenuCmd(text3);
						break;
					case "nametips":
						Log(NameTips.Describe());
						break;
					case "namehover":
						co = NameHoverCmd(text3);
						break;
					case "hover":
						PointerCmd(text3, enter: true);
						break;
					case "unhover":
						PointerCmd(text3, enter: false);
						break;
					case "active":
						ActiveCmd(text3);
						break;
					case "show":
						ShowCmd(text3);
						break;
					case "settext":
						SetTextCmd(text3);
						break;
					case "loadsave":
						LoadSaveCmd(text3);
						break;
					case "title":
						SceneController.Instance.LoadTitle();
						Log("title");
						break;
					case "lang":
						SystemSettings.SetLanguage(int.Parse(text3));
						Log("lang " + text3);
						break;
					case "fix":
						FixCmd(text3);
						break;
					case "sprites":
						Plugin.CfgSpriteReplace.Value = text3.Trim() == "1";
						Log("sprites " + text3);
						break;
					case "reload":
						Plugin.Reload();
						Log("reload");
						break;
					case "reset":
						RuleApplier.ResetAllMarkers();
						Log("reset");
						break;
					case "scan":
						RuleApplier.ApplyToAll(includeInactive: true);
						Log("scan");
						break;
					case "res":
					{
						string[] array = text3.Split(' ');
						Screen.SetResolution(int.Parse(array[0]), int.Parse(array[1]), fullscreen: false);
						Log("res " + text3);
						break;
					}
					case "info":
						InfoCmd();
						break;
					case "invoke":
						co = InvokeCmd(text3);
						break;
					case "trace":
						StringsPlugin.CfgTrace.Value = text3.Trim() == "1";
						Log("trace " + text3);
						break;
					case "strings":
						StringsPlugin.CfgEnabled.Value = text3.Trim() == "1";
						StringOverrides.Load();
						StringOverrides.Refresh();
						Log($"strings {text3} ({StringOverrides.Count} overrides)");
						break;
					case "lua":
						LuaCmd(text3);
						break;
					case "cjkscan":
						CjkScanCmd(text3);
						break;
					case "detect":
						OriginalMod.Refresh();
						Log("detect: " + OriginalMod.Describe(OriginalMod.Current));
						Log($"detect: table {TranslationProfiles.TableMode} ({TranslationProfiles.TableEntries} keys, served {TextTable.Served}, missed {TextTable.Missed}), scene {TranslationProfiles.SceneMode} ({TranslationProfiles.SceneEntries} lines), XUnity bridge {(XUnityBridge.Registered ? "registered" : "not registered")}, language {SystemSettings.Language} (english={EnglishLanguage.Active})");
						break;
					case "cfg":
					{
						// cfg Section/Key=value: set an entry of lom.ui.english.cfg (or lom.strings.english.cfg with a strings: prefix) live.
						string spec = text3;
						ConfigFile file = Plugin.Instance.Config;
						if (spec.StartsWith("strings:", StringComparison.OrdinalIgnoreCase))
						{
							file = StringsPlugin.Instance.Config;
							spec = spec.Substring(8);
						}
						int eq = spec.IndexOf('=');
						int slash = spec.IndexOf('/');
						string section = spec.Substring(0, slash).Trim();
						string keyName = spec.Substring(slash + 1, eq - slash - 1).Trim();
						string raw = spec.Substring(eq + 1).Trim();
						ConfigEntryBase entry = file.Keys.Where((ConfigDefinition d) => d.Section == section && d.Key == keyName).Select((ConfigDefinition d) => file[d]).FirstOrDefault();
						if (entry == null)
						{
							Log("ERR cfg: no entry " + section + "/" + keyName);
							break;
						}
						entry.BoxedValue = TomlTypeConverter.ConvertToValue(raw, entry.SettingType);
						Log("cfg " + section + "/" + keyName + " = " + entry.BoxedValue);
						break;
					}
					case "compat":
						Log("compat: " + Compat.Summary());
						foreach (Feature cf in Compat.All)
						{
							Log("  " + cf.Id + " " + cf.Health + ": " + cf.StatusLine());
						}
						break;
					case "patches":
					{
						// patches [owner substring]: every patched method with its owners and patch classes, to out/patches.txt
						StringBuilder pb = new StringBuilder();
						Dictionary<string, int> perOwner = new Dictionary<string, int>();
						foreach (MethodBase m in Harmony.GetAllPatchedMethods().ToList())
						{
							Patches pi = Harmony.GetPatchInfo(m);
							if (pi == null)
							{
								continue;
							}
							foreach (Patch p in pi.Prefixes.Concat(pi.Postfixes).Concat(pi.Transpilers).Concat(pi.Finalizers))
							{
								perOwner[p.owner] = (perOwner.TryGetValue(p.owner, out int n0) ? n0 : 0) + 1;
								if (text3.Length == 0 || p.owner.IndexOf(text3, StringComparison.OrdinalIgnoreCase) >= 0)
								{
									pb.Append(p.owner).Append('\t').Append(m.DeclaringType?.FullName).Append('.').Append(m.Name).Append('\t').Append(p.PatchMethod?.DeclaringType?.Assembly.GetName().Name).Append(':').Append(p.PatchMethod?.DeclaringType?.FullName).Append('.').Append(p.PatchMethod?.Name).Append('\n');
								}
							}
						}
						File.WriteAllText(Path.Combine(_outDir, "patches.txt"), pb.ToString(), new UTF8Encoding(false));
						Log("patches: " + string.Join(", ", perOwner.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + "=" + kv.Value)));
						break;
					}
					case "textstat":
						Log(SceneText.StatsLine() + $"; table served {TextTable.Served}, missed {TextTable.Missed}");
						break;
					case "tabledump":
						// tabledump NAME: the served game data table (after the overlay) to BepInEx/cache/LOM_UI_EN/NAME.tsv, for offline comparison.
						if (TextTable.Active != null)
						{
							DumpPairs(string.IsNullOrEmpty(text3) ? "tabledump" : text3, TextTable.Active.Map);
						}
						else
						{
							Log("tabledump: no table is served (" + TranslationProfiles.TableMode + ")");
						}
						break;
					case "srcdump":
						// srcdump NAME: the game's own (Chinese) text of every key, for tools/publish.py's revision record. Only in the
						// language slot this mod translates: a dump in another language would mark every row as changed.
						if (!EnglishLanguage.ActiveKnown)
						{
							Log("ERR srcdump: the game's language is not known to be the one this mod translates (Traditional Chinese slot); switch it first");
							break;
						}
						try
						{
							DumpPairs(string.IsNullOrEmpty(text3) ? "srcdump" : text3, TextTable.GameSources());
						}
						catch (Exception ex)
						{
							Log("ERR srcdump: " + ex.Message);
						}
						break;
					case "tget":
					{
						// tget KEY: what the game data layer serves for a key now, the original patch's line, and the game's own text.
						string v;
						bool ok = TextTable.TryGet(text3.Trim(), out v);
						string b = null;
						TextTable.Active?.BaseValues.TryGetValue(text3.Trim(), out b);
						Log("tget " + text3.Trim() + ": " + (ok ? ("served '" + v + "'") : "not in the table") + " | original patch line: " + ((b != null) ? ("'" + b + "'") : "(same or none)") + " | game: '" + (TextTable.GameSource(text3.Trim()) ?? "(unknown)") + "'");
						break;
					}
					case "fbtest":
					{
						// fbtest KEY stale|newer  |  fbtest KEY value TEXT : in-memory fallback test on the active table.
						string[] fa = text3.Split(new char[1] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
						if (fa.Length < 2)
						{
							Log("ERR fbtest KEY stale|newer|value TEXT");
							break;
						}
						Log("fbtest " + fa[0] + " " + fa[1] + ": now serves '" + TextTable.TestFallback(fa[0], fa[1], (fa.Length > 2) ? fa[2] : null) + "'");
						break;
					}
					case "fallbacks":
						Log($"fallbacks: {TextTable.FellBackChanged} changed by an update, {TextTable.FellBackPlaceholders} placeholders, {TextTable.StaleKept} out of date kept; records {(TranslationProfiles.TableHasRecords ? "loaded" : "missing")}");
						foreach (string f in TextTable.FallbackLog.Take(50))
						{
							Log("  " + f);
						}
						break;
					case "scenedump":
						if (SceneText.Active != null)
						{
							DumpPairs(string.IsNullOrEmpty(text3) ? "scenedump" : text3, SceneText.Active.Pairs());
						}
						else
						{
							Log("scenedump: no scene dictionary is served (" + TranslationProfiles.SceneMode + ")");
						}
						break;
					case "profile":
					{
						string[] pa = text3.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
						TextSource src = (TextSource)Enum.Parse(typeof(TextSource), pa[1], ignoreCase: true);
						if (pa[0].Equals("table", StringComparison.OrdinalIgnoreCase))
						{
							TranslationProfiles.SetTable(src);
						}
						else if (pa[0].Equals("scene", StringComparison.OrdinalIgnoreCase))
						{
							TranslationProfiles.SetScene(src);
						}
						else
						{
							TranslationProfiles.SetTable(src);
							TranslationProfiles.SetScene(src);
						}
						Log($"profile {text3}: table {TranslationProfiles.TableMode}, scene {TranslationProfiles.SceneMode}");
						break;
					}
					case "reloadtext":
						Log(TranslationProfiles.ReloadFiles());
						break;
					case "quit":
						Log("quit");
						Application.Quit();
						break;
					default:
						Log("ERR unknown command: " + line);
						break;
					}
				}
				catch (Exception ex)
				{
					Log("ERR " + line + ": " + ex.GetType().Name + ": " + ex.Message);
				}
				if (co != null)
				{
					while (true)
					{
						bool flag;
						try
						{
							flag = co.MoveNext();
						}
						catch (Exception ex2)
						{
							Log("ERR " + line + ": " + ex2.GetType().Name + ": " + ex2.Message);
							break;
						}
						if (!flag)
						{
							break;
						}
						yield return co.Current;
					}
				}
				yield return null;
			}
			try
			{
				File.WriteAllText(Path.Combine(_outDir, batch + ".done"), DateTime.Now.ToString("o"));
			}
			catch
			{
			}
			Log("done " + batch);
			_busy = false;
		}

		private IEnumerator WaitCmd(string arg)
		{
			float time = float.Parse(arg, CultureInfo.InvariantCulture);
			yield return new WaitForSecondsRealtime(time);
		}

		private IEnumerator ScreenshotCmd(string name)
		{
			yield return new WaitForEndOfFrame();
			string path = Path.Combine(_outDir, name + ".png");
			try
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
			catch
			{
			}
			ScreenCapture.CaptureScreenshot(path, 1);
			float t = 0f;
			while (!File.Exists(path) && t < 5f)
			{
				t += Time.unscaledDeltaTime;
				yield return null;
			}
			yield return null;
			Log((File.Exists(path) ? "screenshot " : "ERR screenshot missing ") + path);
		}

		private void InfoCmd()
		{
			Log($"info scene={SceneManager.GetActiveScene().name} screen={Screen.width}x{Screen.height} fullscreen={Screen.fullScreen} lang={SystemSettings.Language}({LeanLocalization.CurrentLanguage}) isChinese={SystemSettings.IsChineseLanguage} isKorean={SystemSettings.IsKoreanLanguage} rules={Rules.Count} applications={RuleApplier.Applications} spriteReplacements={SpriteStore.Replacements} time={Time.realtimeSinceStartup:F1} timeScale={Time.timeScale}");
		}

		private void FixCmd(string arg)
		{
			if (!(arg.Trim() == "1"))
			{
				RuleApplier.ResetAllMarkers();
				Plugin.CfgEnabled.Value = false;
			}
			else
			{
				Plugin.CfgEnabled.Value = true;
				RuleApplier.ApplyToAll(includeInactive: true);
			}
			Log("fix " + arg);
		}

		private void LoadSaveCmd(string slot)
		{
			slot = slot.Trim();
			SaveSystem.Instance.SetSlot(slot);
			SoundManager.Instance.StopMusic();
			SaveSystem.Instance.LoadGameData();
			SaveSystem.Instance.SaveUniverseData();
			MissionManagerData.Instance.UpdateCheckMissions();
			SceneController.Instance.LoadCurrentScene();
			Log("loadsave " + slot);
		}

		/// <summary>Harness dumps: backslash, tab, CR and LF escaped, one key TAB value per line.</summary>
		private static string DumpEscape(string s)
		{
			if (s == null)
			{
				return "";
			}
			StringBuilder b = new StringBuilder(s.Length + 8);
			foreach (char c in s)
			{
				if (c == (char)92)
				{
					b.Append((char)92).Append((char)92);
				}
				else if (c == (char)10)
				{
					b.Append((char)92).Append('n');
				}
				else if (c == (char)13)
				{
					b.Append((char)92).Append('r');
				}
				else if (c == (char)9)
				{
					b.Append((char)92).Append('t');
				}
				else
				{
					b.Append(c);
				}
			}
			return b.ToString();
		}

		private void DumpPairs(string name, IEnumerable<KeyValuePair<string, string>> pairs)
		{
			// Outside the plugin folder (BepInEx/cache): these hold the original patch's and the game's text, never to be packaged.
			string dir = Path.Combine(BepInEx.Paths.CachePath, "LOM_UI_EN");
			Directory.CreateDirectory(dir);
			string path = Path.Combine(dir, name + ".tsv");
			int n = 0;
			using (StreamWriter w = new StreamWriter(path, false, new UTF8Encoding(false)))
			{
				foreach (KeyValuePair<string, string> kv in pairs)
				{
					w.Write(DumpEscape(kv.Key) + "\t" + DumpEscape(kv.Value) + "\n");
					n++;
				}
			}
			Log(name + ": " + n + " pairs -> " + path);
		}

		private IEnumerator InvokeCmd(string arg)
		{
			string[] array = arg.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (array.Length < 2)
			{
				Log("ERR invoke needs PATH Type.Method");
				yield break;
			}
			object obj = null;
			string text2;
			Type type;
			string[] array2;
			if (array[0] == "static")
			{
				string obj2 = array[1];
				int num = obj2.LastIndexOf('.');
				string text = obj2.Substring(0, num);
				text2 = obj2.Substring(num + 1);
				type = AccessTools.TypeByName(text);
				if (type == null)
				{
					Log("ERR type not found: " + text);
					yield break;
				}
				array2 = array.Skip(2).ToArray();
			}
			else
			{
				Transform transform = Find(array[0]);
				if (transform == null)
				{
					yield break;
				}
				string text3 = array[1];
				int num2 = text3.LastIndexOf('.');
				string text4 = ((num2 < 0) ? null : text3.Substring(0, num2));
				text2 = ((num2 < 0) ? text3 : text3.Substring(num2 + 1));
				Component component = null;
				Component[] components = transform.GetComponents<Component>();
				foreach (Component component2 in components)
				{
					if (!(component2 == null) && (text4 == null || component2.GetType().Name == text4 || component2.GetType().FullName == text4))
					{
						component = component2;
						if (text4 != null)
						{
							break;
						}
					}
				}
				if (component == null)
				{
					Log("ERR component not found: " + text4);
					yield break;
				}
				obj = component;
				type = component.GetType();
				array2 = array.Skip(2).ToArray();
			}
			BindingFlags bindingAttr = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
			MethodInfo methodInfo = null;
			MethodInfo[] methods = type.GetMethods(bindingAttr);
			foreach (MethodInfo methodInfo2 in methods)
			{
				if (methodInfo2.Name == text2 && methodInfo2.GetParameters().Length == array2.Length)
				{
					methodInfo = methodInfo2;
					break;
				}
			}
			if (methodInfo == null)
			{
				methods = type.GetMethods(bindingAttr);
				foreach (MethodInfo methodInfo3 in methods)
				{
					if (methodInfo3.Name == text2 && methodInfo3.GetParameters().Length >= array2.Length && methodInfo3.GetParameters().Skip(array2.Length).All((ParameterInfo p) => p.IsOptional))
					{
						methodInfo = methodInfo3;
						break;
					}
				}
			}
			if (methodInfo == null)
			{
				Log($"ERR method not found: {type.Name}.{text2}/{array2.Length}");
				yield break;
			}
			ParameterInfo[] parameters = methodInfo.GetParameters();
			object[] array3 = new object[parameters.Length];
			for (int num3 = 0; num3 < parameters.Length; num3++)
			{
				if (num3 >= array2.Length)
				{
					array3[num3] = Type.Missing;
					continue;
				}
				Type parameterType = parameters[num3].ParameterType;
				string text5 = array2[num3];
				try
				{
					if (parameterType == typeof(string))
					{
						array3[num3] = text5;
					}
					else if (parameterType == typeof(int))
					{
						array3[num3] = int.Parse(text5);
					}
					else if (parameterType == typeof(float))
					{
						array3[num3] = float.Parse(text5, CultureInfo.InvariantCulture);
					}
					else if (parameterType == typeof(bool))
					{
						array3[num3] = text5 == "1" || text5.Equals("true", StringComparison.OrdinalIgnoreCase);
					}
					else if (parameterType.IsEnum)
					{
						array3[num3] = Enum.Parse(parameterType, text5, ignoreCase: true);
					}
					else
					{
						array3[num3] = Convert.ChangeType(text5, parameterType, CultureInfo.InvariantCulture);
					}
				}
				catch (Exception ex)
				{
					Log($"ERR arg {num3} ({parameterType.Name}) '{text5}': {ex.Message}");
					yield break;
				}
			}
			object obj3;
			try
			{
				obj3 = methodInfo.Invoke(methodInfo.IsStatic ? null : obj, array3);
			}
			catch (Exception ex2)
			{
				Log("ERR invoke " + type.Name + "." + text2 + ": " + (ex2.InnerException ?? ex2).GetType().Name + ": " + (ex2.InnerException ?? ex2).Message);
				yield break;
			}
			if (obj3 is IEnumerator enumerator)
			{
				MonoBehaviour monoBehaviour = obj as MonoBehaviour;
				Log("invoke " + type.Name + "." + text2 + " -> coroutine (started)");
				if (monoBehaviour != null && monoBehaviour.isActiveAndEnabled)
				{
					monoBehaviour.StartCoroutine(enumerator);
				}
				else
				{
					StartCoroutine(Guarded(enumerator, type.Name + "." + text2));
				}
			}
			else
			{
				Log(string.Format("invoke {0}.{1} -> {2}", type.Name, text2, obj3 ?? "void"));
			}
		}

		private IEnumerator Guarded(IEnumerator en, string label)
		{
			while (true)
			{
				bool flag;
				try
				{
					flag = en.MoveNext();
				}
				catch (Exception ex)
				{
					Log("ERR coroutine " + label + ": " + ex.GetType().Name + ": " + ex.Message);
					yield break;
				}
				if (!flag)
				{
					break;
				}
				yield return en.Current;
			}
			Log("coroutine " + label + " finished");
		}

		/// <summary>cjkscan [tag]: every active Text/TMP whose text still contains Han or Hangul, as path TAB text, to out/cjkscan_tag.txt.</summary>
		private void CjkScanCmd(string tag)
		{
			if (string.IsNullOrEmpty(tag))
			{
				tag = DateTime.Now.ToString("HHmmss");
			}
			StringBuilder sb = new StringBuilder();
			int n = 0;
			foreach (Component c in Resources.FindObjectsOfTypeAll<Text>().Cast<Component>().Concat(Resources.FindObjectsOfTypeAll<TMP_Text>()))
			{
				if (c == null || !c.gameObject.activeInHierarchy || !c.gameObject.scene.IsValid())
				{
					continue;
				}
				string s = (c is Text t) ? t.text : ((TMP_Text)c).text;
				if (string.IsNullOrEmpty(s) || !HasHanOrHangul(s))
				{
					continue;
				}
				n++;
				sb.Append(PathUtil.GetPath(c.transform)).Append('\t').Append(SceneText.Escape(s)).Append('\n');
			}
			File.WriteAllText(Path.Combine(_outDir, "cjkscan_" + tag + ".txt"), sb.ToString(), new UTF8Encoding(false));
			Log($"cjkscan {tag}: {n} components with Chinese or Korean text");
		}

		private static bool HasHanOrHangul(string s)
		{
			foreach (char c in s)
			{
				if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF) || (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xAC00 && c <= 0xD7AF) || (c >= 0x1100 && c <= 0x11FF) || (c >= 0x3130 && c <= 0x318F))
				{
					return true;
				}
			}
			return false;
		}

		private void LuaCmd(string code)
		{
			object obj = null;
			string[] array = new string[5] { "Mortal.Core.LuaManager", "Mortal.Story.LuaManager", "Mortal.LuaManager", "LuaManager", "Fungus.LuaManager" };
			for (int i = 0; i < array.Length; i++)
			{
				Type type = AccessTools.TypeByName(array[i]);
				if (!(type == null))
				{
					PropertyInfo propertyInfo = AccessTools.Property(type, "Instance");
					obj = ((propertyInfo != null) ? propertyInfo.GetValue(null, null) : null);
					if (obj != null)
					{
						break;
					}
				}
			}
			if (obj == null)
			{
				Log("ERR LuaManager not found");
				return;
			}
			LuaEnvironment value = Traverse.Create(obj).Field("_luaEnvironment").GetValue<LuaEnvironment>();
			if (value == null)
			{
				Log("ERR no lua environment");
				return;
			}
			string ret = null;
			value.DoLuaString(code, "harness", runAsCoroutine: false, delegate(DynValue r)
			{
				ret = r.ToString();
			});
			Log("lua -> " + ret);
		}

		private Transform Find(string path)
		{
			Transform obj = PathUtil.FindByPath(path);
			if (obj == null)
			{
				Log("ERR not found: " + path);
			}
			return obj;
		}

		private void ActiveCmd(string arg)
		{
			int num = arg.LastIndexOf(' ');
			string text = arg.Substring(0, num).Trim();
			bool flag = arg.Substring(num + 1).Trim() == "1";
			Transform transform = Find(text);
			if (!(transform == null))
			{
				transform.gameObject.SetActive(flag);
				Log($"active {text} {flag}");
			}
		}

		private void ShowCmd(string arg)
		{
			int num = arg.LastIndexOf(' ');
			string text = arg.Substring(0, num).Trim();
			bool flag = arg.Substring(num + 1).Trim() == "1";
			Transform transform = Find(text);
			if (!(transform == null))
			{
				CommonPanel component = transform.GetComponent<CommonPanel>();
				if (component == null)
				{
					Log("ERR no CommonPanel on " + text);
					return;
				}
				component.Show(flag);
				Log($"show {text} {flag}");
			}
		}

		private void SetTextCmd(string arg)
		{
			int num = arg.IndexOf('|');
			string text;
			string text2;
			if (num >= 0)
			{
				text = arg.Substring(0, num).Trim();
				text2 = arg.Substring(num + 1);
			}
			else
			{
				int num2 = arg.IndexOf(' ');
				text = ((num2 < 0) ? arg : arg.Substring(0, num2));
				text2 = ((num2 < 0) ? "" : arg.Substring(num2 + 1));
			}
			text2 = text2.Replace("\\n", "\n");
			Transform transform = Find(text);
			if (!(transform == null))
			{
				Text component = transform.GetComponent<Text>();
				if (component != null)
				{
					component.text = text2;
				}
				TMP_Text component2 = transform.GetComponent<TMP_Text>();
				if (component2 != null)
				{
					component2.text = text2;
				}
				Log("settext " + text);
			}
		}

		private void ClickCmd(string path)
		{
			Transform transform = Find(path);
			if (!(transform == null))
			{
				GameObject gameObject = transform.gameObject;
				EventSystem current = EventSystem.current;
				PointerEventData pointerEventData = new PointerEventData(current)
				{
					button = PointerEventData.InputButton.Left,
					clickCount = 1
				};
				RectTransform rectTransform = transform as RectTransform;
				if (rectTransform != null)
				{
					Canvas componentInParent = transform.GetComponentInParent<Canvas>();
					Camera cam = ((componentInParent != null && componentInParent.renderMode != RenderMode.ScreenSpaceOverlay) ? componentInParent.worldCamera : null);
					pointerEventData.position = RectTransformUtility.WorldToScreenPoint(cam, rectTransform.TransformPoint(rectTransform.rect.center));
					pointerEventData.pressPosition = pointerEventData.position;
				}
				bool flag = false;
				Button component = gameObject.GetComponent<Button>();
				ExecuteEvents.Execute(gameObject, pointerEventData, ExecuteEvents.pointerEnterHandler);
				ExecuteEvents.Execute(gameObject, pointerEventData, ExecuteEvents.pointerDownHandler);
				ExecuteEvents.Execute(gameObject, pointerEventData, ExecuteEvents.pointerUpHandler);
				GameObject gameObject2 = ExecuteEvents.ExecuteHierarchy(gameObject, pointerEventData, ExecuteEvents.pointerClickHandler);
				flag = gameObject2 != null;
				if (!flag && component != null)
				{
					component.onClick.Invoke();
					flag = true;
				}
				if (!flag)
				{
					flag = ExecuteEvents.ExecuteHierarchy(gameObject, new BaseEventData(current), ExecuteEvents.submitHandler) != null;
				}
				Log((flag ? "click " : "ERR click not handled ") + path + ((gameObject2 != null) ? (" -> " + gameObject2.name) : ""));
			}
		}

		/// <summary>
		/// 'mouseclick PATH' or 'mouseclick X Y' (screen pixels, origin bottom-left): a click through the real input path. Mouse
		/// state events are queued into the Input System (move, press, release, two frames apart), so they reach the EventSystem's
		/// input module and every game action bound to the mouse, unlike 'click', which calls the pointer handlers directly.
		/// While the game window is unfocused the Input System would drop them, so background behaviour is set to IgnoreFocus.
		/// </summary>
		private IEnumerator MouseClickCmd(string arg)
		{
			Vector2 pos;
			string[] parts = arg.Split(' ');
			if (parts.Length == 2 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
			{
				pos = new Vector2(x, y);
			}
			else
			{
				RectTransform rt = Find(arg) as RectTransform;
				if (rt == null)
				{
					yield break;
				}
				Canvas canvas = rt.GetComponentInParent<Canvas>();
				Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;
				pos = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(rt.rect.center));
			}
			UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
			if (mouse == null)
			{
				Log("ERR mouseclick: no mouse device");
				yield break;
			}
			UnityEngine.InputSystem.InputSettings settings = UnityEngine.InputSystem.InputSystem.settings;
			if (settings.backgroundBehavior != UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus)
			{
				settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
				Log("input background behaviour -> IgnoreFocus");
			}
			if (!mouse.enabled)
			{
				UnityEngine.InputSystem.InputSystem.EnableDevice(mouse);
			}
			QueueMouse(mouse, pos, down: false);
			yield return null;
			yield return null;
			QueueMouse(mouse, pos, down: true);
			yield return null;
			yield return null;
			QueueMouse(mouse, pos, down: false);
			yield return null;
			yield return null;
			Log($"mouseclick {arg} at {pos.x:F0},{pos.y:F0}");
		}

		/// <summary>'mousemove X Y' (screen pixels, origin bottom-left) or 'mousemove PATH': moves the pointer through the Input System, no button.</summary>
		private IEnumerator MouseMoveCmd(string arg)
		{
			Vector2 pos;
			string[] parts = arg.Split(' ');
			if (parts.Length == 2 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
			{
				pos = new Vector2(x, y);
			}
			else
			{
				RectTransform rt = Find(arg) as RectTransform;
				if (rt == null)
				{
					yield break;
				}
				Canvas canvas = rt.GetComponentInParent<Canvas>();
				Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;
				pos = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(rt.rect.center));
			}
			foreach (object step in MoveMouse(pos))
			{
				yield return step;
			}
			Log($"mousemove {arg} at {pos.x:F0},{pos.y:F0}");
		}

		private IEnumerable<object> MoveMouse(Vector2 pos)
		{
			UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
			if (mouse == null)
			{
				Log("ERR mousemove: no mouse device");
				yield break;
			}
			UnityEngine.InputSystem.InputSettings settings = UnityEngine.InputSystem.InputSystem.settings;
			if (settings.backgroundBehavior != UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus)
			{
				settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
				Log("input background behaviour -> IgnoreFocus");
			}
			if (!mouse.enabled)
			{
				UnityEngine.InputSystem.InputSystem.EnableDevice(mouse);
			}
			QueueMouse(mouse, pos, down: false);
			yield return null;
			yield return null;
		}

		/// <summary>
		/// 'menu [options|dice|talk] KEY~KEY2~...': shows one of the story's choice menus with those options, added the way the
		/// scripts' choose() adds them (the game's option code turns each story key into its text; the dice menu takes KEY|Stat).
		/// 'menu close' hides the menus again.
		/// </summary>
		private void MenuCmd(string arg)
		{
			Mortal.Story.MenuDialogPlaceholder ph = Resources.FindObjectsOfTypeAll<Mortal.Story.MenuDialogPlaceholder>().FirstOrDefault((Mortal.Story.MenuDialogPlaceholder p) => p != null && p.gameObject.scene.IsValid());
			if (ph == null)
			{
				Log("ERR menu: no menu dialogs here (story scene only)");
				return;
			}
			string which = "options";
			string rest = arg.Trim();
			int sp = rest.IndexOf(' ');
			string first = (sp > 0 ? rest.Substring(0, sp) : rest).ToLowerInvariant();
			if (first == "close")
			{
				foreach (string f in new string[3] { "_options", "_dice", "_talk" })
				{
					Traverse.Create(ph).Field(f).GetValue<Fungus.MenuDialog>()?.SetActive(state: false);
				}
				Log("menu close");
				return;
			}
			if (sp > 0 && (first == "options" || first == "dice" || first == "talk"))
			{
				which = first;
				rest = rest.Substring(sp + 1);
			}
			Fungus.MenuDialog md = Traverse.Create(ph).Field((which == "dice") ? "_dice" : ((which == "talk") ? "_talk" : "_options")).GetValue<Fungus.MenuDialog>();
			if (md == null)
			{
				Log("ERR menu: no " + which + " menu");
				return;
			}
			md.SetActive(state: true);
			Fungus.MenuDialog.ActiveMenuDialog = md;
			md.Clear();
			int n = 0;
			foreach (string option in rest.Split('~'))
			{
				if (option.Trim().Length > 0 && md.AddOption(option.Trim(), interactable: true, luaEnv: null, callBack: null))
				{
					n++;
				}
			}
			Log("menu " + which + " (" + PathUtil.GetPath(md.transform) + "): " + n + " options");
		}

		/// <summary>'namehover NAME|ID': points at the first dialog name of that character (see 'nametips'), through the Input System.</summary>
		private IEnumerator NameHoverCmd(string arg)
		{
			if (!NameTips.FindOnScreen(arg.Trim(), out Vector2 pos))
			{
				Log("ERR namehover: no name '" + arg.Trim() + "' in a dialog on screen");
				yield break;
			}
			foreach (object step in MoveMouse(pos))
			{
				yield return step;
			}
			Log($"namehover {arg.Trim()} at {pos.x:F0},{pos.y:F0}");
		}

		/// <summary>'say [character|narrative|center|think] TEXT': writes a line in one of the story's dialogs through its Fungus Writer (\n = new line).</summary>
		private void SayCmd(string arg)
		{
			string which = "character";
			string text = arg;
			int sp = arg.IndexOf(' ');
			if (sp > 0)
			{
				string w = arg.Substring(0, sp).ToLowerInvariant();
				if (w == "character" || w == "narrative" || w == "center" || w == "think")
				{
					which = w;
					text = arg.Substring(sp + 1);
				}
			}
			Mortal.Story.SayDialogPlaceholder ph = Resources.FindObjectsOfTypeAll<Mortal.Story.SayDialogPlaceholder>().FirstOrDefault((Mortal.Story.SayDialogPlaceholder p) => p != null && p.gameObject.scene.IsValid());
			SayDialog sd = (ph == null) ? SayDialog.GetSayDialog() : ((which == "narrative") ? ph.narrative : ((which == "center") ? ph.center : ((which == "think") ? ph.think : ph.character)));
			if (sd == null)
			{
				Log("ERR say: no say dialog");
				return;
			}
			sd.SetActive(state: true);
			SayDialog.ActiveSayDialog = sd;
			sd.Say(text.Replace("\\n", "\n"), clearPrevious: true, waitForInput: true, fadeWhenDone: false, stopVoiceover: true, waitForVO: false, voiceOverClip: null, onComplete: null);
			Log("say " + which + " (" + PathUtil.GetPath(sd.transform) + "): " + text);
		}

		private static void QueueMouse(UnityEngine.InputSystem.Mouse mouse, Vector2 pos, bool down)
		{
			UnityEngine.InputSystem.LowLevel.MouseState state = new UnityEngine.InputSystem.LowLevel.MouseState
			{
				position = pos
			};
			if (down)
			{
				state = state.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left);
			}
			UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, state);
		}

		private void PointerCmd(string path, bool enter)
		{
			Transform transform = Find(path);
			if (!(transform == null))
			{
				PointerEventData pointerEventData = new PointerEventData(EventSystem.current);
				RectTransform rectTransform = transform as RectTransform;
				if (rectTransform != null)
				{
					pointerEventData.position = RectTransformUtility.WorldToScreenPoint(null, rectTransform.TransformPoint(rectTransform.rect.center));
				}
				GameObject gameObject = (enter ? ExecuteEvents.ExecuteHierarchy(transform.gameObject, pointerEventData, ExecuteEvents.pointerEnterHandler) : ExecuteEvents.ExecuteHierarchy(transform.gameObject, pointerEventData, ExecuteEvents.pointerExitHandler));
				Log((enter ? "hover " : "unhover ") + path + ((gameObject != null) ? (" -> " + gameObject.name) : " (no handler)"));
			}
		}

		private void FindCmd(string sub)
		{
			StringBuilder stringBuilder = new StringBuilder();
			int num = 0;
			foreach (Transform item in PathUtil.AllSceneTransforms())
			{
				string path = PathUtil.GetPath(item);
				if (path.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					stringBuilder.Append(item.gameObject.activeInHierarchy ? "A " : "- ").Append(path).Append("  [")
						.Append(ComponentList(item.gameObject))
						.Append("]\n");
					num++;
				}
			}
			File.WriteAllText(Path.Combine(_outDir, "find.txt"), stringBuilder.ToString(), Encoding.UTF8);
			Log($"find '{sub}': {num} matches -> find.txt");
		}

		private void DumpCmd(string arg)
		{
			string[] array = arg.Split(' ');
			string text = array[0];
			bool all = array.Contains("all");
			string text2 = ((array.Length > 1 && array[1] != "all") ? array[1] : null);
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append($"# scene={SceneManager.GetActiveScene().name} screen={Screen.width}x{Screen.height} lang={SystemSettings.Language} isChinese={SystemSettings.IsChineseLanguage} time={Time.realtimeSinceStartup:F1}\n");
			stringBuilder.Append("# columns: path [components] rect=x,y-x2,y2(WxH) (screen px, origin top-left) | text/image details | flags\n");
			List<Transform> list = new List<Transform>();
			if (text2 != null)
			{
				Transform transform = Find(text2);
				if (transform != null)
				{
					list.Add(transform);
				}
			}
			else
			{
				list = PathUtil.GetAllRoots();
			}
			foreach (Transform item in list.OrderBy((Transform t) => t.name))
			{
				DumpTransform(item, stringBuilder, 0, all);
			}
			string text3 = Path.Combine(_outDir, text + ".txt");
			File.WriteAllText(text3, stringBuilder.ToString(), Encoding.UTF8);
			Log("dump -> " + text3);
		}

		private void DumpTransform(Transform t, StringBuilder sb, int depth, bool all)
		{
			GameObject gameObject = t.gameObject;
			if (!all && !gameObject.activeInHierarchy)
			{
				return;
			}
			RectTransform rectTransform = t as RectTransform;
			if (rectTransform != null || depth == 0)
			{
				sb.Append(' ', depth).Append(gameObject.activeSelf ? "" : "(inactive) ").Append(t.name);
				sb.Append(" [").Append(ComponentList(gameObject)).Append(']');
				if (rectTransform != null)
				{
					Canvas componentInParent = t.GetComponentInParent<Canvas>();
					Camera cam = ((componentInParent != null && componentInParent.renderMode != RenderMode.ScreenSpaceOverlay) ? componentInParent.worldCamera : null);
					rectTransform.GetWorldCorners(_corners);
					Vector2 vector = RectTransformUtility.WorldToScreenPoint(cam, _corners[0]);
					Vector2 vector2 = RectTransformUtility.WorldToScreenPoint(cam, _corners[2]);
					float num = Mathf.Min(vector.x, vector2.x);
					float num2 = Mathf.Max(vector.x, vector2.x);
					float num3 = (float)Screen.height - Mathf.Max(vector.y, vector2.y);
					float num4 = (float)Screen.height - Mathf.Min(vector.y, vector2.y);
					sb.Append($" rect={num:F0},{num3:F0}-{num2:F0},{num4:F0}({num2 - num:F0}x{num4 - num3:F0})");
					sb.Append($" local=sz{rectTransform.sizeDelta.x:F0}x{rectTransform.sizeDelta.y:F0} ap{rectTransform.anchoredPosition.x:F0},{rectTransform.anchoredPosition.y:F0} an{rectTransform.anchorMin.x:F2},{rectTransform.anchorMin.y:F2}-{rectTransform.anchorMax.x:F2},{rectTransform.anchorMax.y:F2}");
					float z = rectTransform.localRotation.eulerAngles.z;
					if (Mathf.Abs(z) > 0.5f && Mathf.Abs(z - 360f) > 0.5f)
					{
						sb.Append($" rot={z:F0}");
					}
					if (rectTransform.localScale != Vector3.one)
					{
						sb.Append($" scale={rectTransform.localScale.x:F2},{rectTransform.localScale.y:F2}");
					}
					CanvasGroup component = gameObject.GetComponent<CanvasGroup>();
					if (component != null && component.alpha < 0.99f)
					{
						sb.Append($" alpha={component.alpha:F2}");
					}
				}
				Text component2 = gameObject.GetComponent<Text>();
				if (component2 != null)
				{
					float num5 = ((rectTransform != null) ? rectTransform.rect.width : 0f);
					float num6 = ((rectTransform != null) ? rectTransform.rect.height : 0f);
					float num7 = 0f;
					float num8 = 0f;
					try
					{
						num7 = component2.preferredWidth;
						num8 = component2.preferredHeight;
					}
					catch
					{
					}
					sb.Append($" | T:\"{Esc(component2.text)}\" fs={component2.fontSize}");
					if (component2.resizeTextForBestFit)
					{
						sb.Append($" bf={component2.resizeTextMinSize}-{component2.resizeTextMaxSize}");
					}
					sb.Append(string.Format(" al={0} h={1} v={2} ls={3:F2} font={4} pref={5:F0}x{6:F0}", component2.alignment, component2.horizontalOverflow, component2.verticalOverflow, component2.lineSpacing, (component2.font != null) ? component2.font.name : "null", num7, num8));
					LeanLocalizedText component3 = gameObject.GetComponent<LeanLocalizedText>();
					if (component3 != null)
					{
						sb.Append(" key=").Append(component3.TranslationName);
					}
					if (!string.IsNullOrEmpty(component2.text))
					{
						if (component2.horizontalOverflow == HorizontalWrapMode.Overflow && num7 > num5 + 1f)
						{
							sb.Append(" !H-OVERFLOW");
						}
						if (component2.horizontalOverflow == HorizontalWrapMode.Wrap && component2.verticalOverflow == VerticalWrapMode.Truncate && num8 > num6 + 1f && !component2.resizeTextForBestFit)
						{
							sb.Append(" !V-CLIPPED");
						}
						if (component2.horizontalOverflow == HorizontalWrapMode.Wrap && component2.verticalOverflow == VerticalWrapMode.Overflow && num8 > num6 + 1f)
						{
							sb.Append(" !V-OVERFLOW");
						}
						if (num5 > 0f && num5 < 40f && component2.horizontalOverflow == HorizontalWrapMode.Wrap && component2.text.Length > 1)
						{
							sb.Append(" !NARROW-VERTICAL");
						}
					}
				}
				TMP_Text component4 = gameObject.GetComponent<TMP_Text>();
				if (component4 != null)
				{
					float num9 = ((rectTransform != null) ? rectTransform.rect.width : 0f);
					if (rectTransform != null)
					{
						_ = rectTransform.rect.height;
					}
					float num10 = 0f;
					float num11 = 0f;
					int num12 = 0;
					bool flag = false;
					try
					{
						num10 = component4.preferredWidth;
						num11 = component4.preferredHeight;
						num12 = ((component4.textInfo != null) ? component4.textInfo.lineCount : 0);
						flag = component4.isTextOverflowing;
					}
					catch
					{
					}
					sb.Append($" | TMP:\"{Esc(component4.text)}\" fs={component4.fontSize:F1}");
					if (component4.enableAutoSizing)
					{
						sb.Append($" auto={component4.fontSizeMin:F0}-{component4.fontSizeMax:F0}");
					}
					sb.Append(string.Format(" al={0} ov={1} wrap={2} font={3} pref={4:F0}x{5:F0} lines={6}", component4.alignment, component4.overflowMode, component4.enableWordWrapping, (component4.font != null) ? component4.font.name : "null", num10, num11, num12));
					LeanLocalizedTextMeshProUGUI component5 = gameObject.GetComponent<LeanLocalizedTextMeshProUGUI>();
					if (component5 != null)
					{
						sb.Append(" key=").Append(component5.TranslationName);
					}
					if (flag)
					{
						sb.Append(" !TMP-OVERFLOW");
					}
					if (num9 > 0f && num9 < 40f && component4.enableWordWrapping && !string.IsNullOrEmpty(component4.text) && component4.text.Length > 1)
					{
						sb.Append(" !NARROW-VERTICAL");
					}
				}
				Image component6 = gameObject.GetComponent<Image>();
				if (component6 != null)
				{
					sb.Append(string.Format(" | I:{0} type={1}", (component6.sprite != null) ? component6.sprite.name : "null", component6.type));
					if (SpriteStore.IsOurs(component6.sprite))
					{
						sb.Append(" (replaced)");
					}
					if (!component6.enabled)
					{
						sb.Append(" (disabled)");
					}
				}
				LayoutGroup component7 = gameObject.GetComponent<LayoutGroup>();
				if (component7 != null)
				{
					sb.Append(" | ").Append(component7.GetType().Name);
					if (component7 is HorizontalOrVerticalLayoutGroup horizontalOrVerticalLayoutGroup)
					{
						sb.Append($"(sp={horizontalOrVerticalLayoutGroup.spacing},pad={horizontalOrVerticalLayoutGroup.padding.left}/{horizontalOrVerticalLayoutGroup.padding.right}/{horizontalOrVerticalLayoutGroup.padding.top}/{horizontalOrVerticalLayoutGroup.padding.bottom},ctrl={B(horizontalOrVerticalLayoutGroup.childControlWidth)}{B(horizontalOrVerticalLayoutGroup.childControlHeight)},exp={B(horizontalOrVerticalLayoutGroup.childForceExpandWidth)}{B(horizontalOrVerticalLayoutGroup.childForceExpandHeight)},al={horizontalOrVerticalLayoutGroup.childAlignment})");
					}
					if (component7 is GridLayoutGroup gridLayoutGroup)
					{
						sb.Append($"(cell={gridLayoutGroup.cellSize.x}x{gridLayoutGroup.cellSize.y},sp={gridLayoutGroup.spacing.x},{gridLayoutGroup.spacing.y},{gridLayoutGroup.constraint}:{gridLayoutGroup.constraintCount},axis={gridLayoutGroup.startAxis})");
					}
				}
				ContentSizeFitter component8 = gameObject.GetComponent<ContentSizeFitter>();
				if (component8 != null)
				{
					sb.Append($" | CSF(h={component8.horizontalFit},v={component8.verticalFit})");
				}
				LayoutElement component9 = gameObject.GetComponent<LayoutElement>();
				if (component9 != null)
				{
					sb.Append(string.Format(" | LE(min={0}x{1},pref={2}x{3},flex={4}x{5}{6})", component9.minWidth, component9.minHeight, component9.preferredWidth, component9.preferredHeight, component9.flexibleWidth, component9.flexibleHeight, component9.ignoreLayout ? ",ignore" : ""));
				}
				UIFixMarker component10 = gameObject.GetComponent<UIFixMarker>();
				if (component10 != null && component10.AppliedOnce.Count > 0)
				{
					sb.Append(" | fixed:").Append(string.Join(",", component10.AppliedOnce.Select((int i) => (i >= Rules.Count) ? i.ToString() : Rules.All[i].ToString()).ToArray()));
				}
				sb.Append('\n');
			}
			for (int num13 = 0; num13 < t.childCount; num13++)
			{
				DumpTransform(t.GetChild(num13), sb, depth + 1, all);
			}
		}

		private static string B(bool b)
		{
			if (!b)
			{
				return "0";
			}
			return "1";
		}

		private static string Esc(string s)
		{
			if (s == null)
			{
				return "";
			}
			s = s.Replace("\r", "").Replace("\n", "\\n").Replace("\"", "'");
			if (s.Length <= 70)
			{
				return s;
			}
			return s.Substring(0, 70) + "…";
		}

		public static string ComponentList(GameObject go)
		{
			Component[] components = go.GetComponents<Component>();
			List<string> list = new List<string>(components.Length);
			Component[] array = components;
			foreach (Component component in array)
			{
				if (component == null)
				{
					list.Add("<missing>");
					continue;
				}
				string item = component.GetType().Name;
				if (!_skip.Contains(item))
				{
					list.Add(item);
				}
			}
			return string.Join(",", list.ToArray());
		}
	}
	public static class FontMap
	{
		private class Config
		{
			public Dictionary<string, string[]> map = new Dictionary<string, string[]>();

			public Dictionary<string, float> sizeScale = new Dictionary<string, float>();

			public bool enabled = true;
		}

		private static Config _cfg = new Config();

		private static readonly Dictionary<string, Font> _resolved = new Dictionary<string, Font>();

		private static readonly HashSet<int> _ourFonts = new HashSet<int>();

		public static int Applications;

		public static int Count => _cfg.map.Count;

		/// <summary>Why fonts.json could not be read at the last load (null when it was read or is absent).</summary>
		public static string LoadError;

		public static void Load(string dir)
		{
			string path = Path.Combine(dir, "fonts.json");
			Config cfg = new Config();
			LoadError = null;
			if (File.Exists(path))
			{
				try
				{
					cfg = JsonConvert.DeserializeObject<Config>(File.ReadAllText(path)) ?? new Config();
				}
				catch (Exception ex)
				{
					LoadError = ex.Message;
					Plugin.Log.LogError("fonts.json parse failed: " + ex.Message);
				}
			}
			_cfg = cfg;
			_resolved.Clear();
		}

		public static bool IsOurs(Font f)
		{
			if (f != null)
			{
				return _ourFonts.Contains(f.GetInstanceID());
			}
			return false;
		}

		private static Font Resolve(string gameFont)
		{
			if (_resolved.TryGetValue(gameFont, out var value) && value != null)
			{
				return value;
			}
			if (!_cfg.map.TryGetValue(gameFont, out var value2) || value2 == null)
			{
				return null;
			}
			HashSet<string> hashSet = new HashSet<string>(Font.GetOSInstalledFontNames(), StringComparer.OrdinalIgnoreCase);
			string[] array = value2;
			bool isEnabled;
			foreach (string text in array)
			{
				if (string.IsNullOrEmpty(text) || !hashSet.Contains(text))
				{
					continue;
				}
				value = RuleApplier.GetOsFont(text);
				if (value != null)
				{
					_resolved[gameFont] = value;
					_ourFonts.Add(value.GetInstanceID());
					ManualLogSource log = Plugin.Log;
					BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(14, 2, out isEnabled);
					if (isEnabled)
					{
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral("Font map: ");
						bepInExInfoLogInterpolatedStringHandler.AppendFormatted(gameFont);
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral(" -> ");
						bepInExInfoLogInterpolatedStringHandler.AppendFormatted(text);
					}
					log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
					return value;
				}
			}
			_resolved[gameFont] = null;
			ManualLogSource log2 = Plugin.Log;
			BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(40, 2, out isEnabled);
			if (isEnabled)
			{
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral("Font map: no installed candidate for ");
				bepInExWarningLogInterpolatedStringHandler.AppendFormatted(gameFont);
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral(" (");
				bepInExWarningLogInterpolatedStringHandler.AppendFormatted(string.Join(", ", value2));
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral(")");
			}
			log2.LogWarning(bepInExWarningLogInterpolatedStringHandler);
			return null;
		}

		public static bool Apply(Text t)
		{
			if (!_cfg.enabled || !Plugin.CfgEnabled.Value || t == null || t.font == null)
			{
				return false;
			}
			Font font = t.font;
			if (_ourFonts.Contains(font.GetInstanceID()))
			{
				return false;
			}
			Font font2 = Resolve(font.name);
			if (font2 == null)
			{
				return false;
			}
			UIFixMarker uIFixMarker = t.GetComponent<UIFixMarker>();
			if (uIFixMarker == null)
			{
				uIFixMarker = t.gameObject.AddComponent<UIFixMarker>();
				uIFixMarker.hideFlags = HideFlags.HideAndDontSave;
			}
			uIFixMarker.CaptureText(t);
			t.font = font2;
			if (!uIFixMarker.FontScaleApplied && _cfg.sizeScale != null && _cfg.sizeScale.TryGetValue(font.name, out var value) && Math.Abs(value - 1f) > 0.001f)
			{
				t.fontSize = Mathf.Max(1, Mathf.RoundToInt((float)uIFixMarker.OFontSize * value));
				uIFixMarker.FontScaleApplied = true;
			}
			Applications++;
			return true;
		}
	}
	public static class Hooks
	{
		[ThreadStatic]
		private static bool _inFontSetter;

		[ThreadStatic]
		private static bool _inTextSetter;

		[ThreadStatic]
		private static bool _inSpriteSetter;

		public static void Patch(Harmony h)
		{
			Compat.Setup(F.Layout, delegate
			{
				// Turned off at runtime: put every fixed screen back the way the game drew it, not half-fixed.
				F.Layout.OnTurnedOff = RuleApplier.ResetAllMarkers;
				F.Layout.OnTurnedOn = Plugin.Reapply;
				PatchPostfix(h, typeof(Text), "OnEnable", "OnEnable_Component");
				PatchPostfix(h, typeof(TextMeshProUGUI), "OnEnable", "OnEnable_Component");
				PatchPostfix(h, typeof(Image), "OnEnable", "OnEnable_Component");
				PatchPostfix(h, typeof(LayoutGroup), "OnEnable", "OnEnable_Component");
				PatchPostfix(h, typeof(ContentSizeFitter), "OnEnable", "OnEnable_Component");
				PatchPostfix(h, typeof(LayoutElement), "OnEnable", "OnEnable_Component");
				Compat.Hook(F.Layout, h, AccessTools.PropertySetter(typeof(Text), "text"), "Text.text setter", null, new HarmonyMethod(typeof(Hooks), "Text_set_text_Postfix")
				{
					priority = 0
				});
				Compat.Hook(F.Layout, h, AccessTools.PropertySetter(typeof(TMP_Text), "text"), "TMP_Text.text setter", null, new HarmonyMethod(typeof(Hooks), "TMP_set_text_Postfix")
				{
					priority = 0
				});
				Compat.Hook(F.Layout, h, AccessTools.PropertySetter(typeof(Image), "sprite"), "Image.sprite setter", null, new HarmonyMethod(typeof(Hooks), "Image_set_sprite_Postfix")
				{
					priority = 0
				});
				// Its own block: the Lean TMP type is in another assembly, and without it only this hook is lost.
				Compat.Part(F.Layout, "LeanLocalizedTextMeshProUGUIFont.UpdateTranslation", delegate
				{
					Compat.Hook(F.Layout, h, AccessTools.Method(typeof(LeanLocalizedTextMeshProUGUIFont), "UpdateTranslation"), "LeanLocalizedTextMeshProUGUIFont.UpdateTranslation", null, new HarmonyMethod(typeof(Hooks), "LeanTmpFont_Postfix")
					{
						priority = 0
					});
				});
				Compat.Hook(F.Layout, h, AccessTools.PropertySetter(typeof(Text), "font"), "Text.font setter", null, new HarmonyMethod(typeof(Hooks), "Text_set_font_Postfix")
				{
					priority = 0
				});
			});
			Compat.Setup(F.CombatHover, delegate
			{
				Type button = typeof(CombatActionButton);
				Compat.Require(F.CombatHover, button, "_movePanel");
				Compat.Require(F.CombatHover, button, "_focusDuration");
				Compat.Require(F.CombatHover, button, "_moveEase");
				Compat.Require(F.CombatHover, typeof(MovePanel), "OriginAnchoredPosition");
				Compat.Require(F.CombatHover, typeof(MovePanel), "LocalMoveTo");
				Compat.Hook(F.CombatHover, h, AccessTools.Method(button, "FocusPanel"), "CombatActionButton.FocusPanel", new HarmonyMethod(typeof(Hooks), "CombatFocusPanel_Prefix"), essential: true);
			});
		}

		// The hook only names Component; the game types live in the body method, so a changed game type fails there, inside the guard.
		private static bool CombatFocusPanel_Prefix(Component __instance)
		{
			if (!F.CombatHover.Live)
			{
				return true;
			}
			try
			{
				F.CombatHover.Probe();
				return CombatFocusPanelBody(__instance);
			}
			catch (Exception ex)
			{
				F.CombatHover.Fail(ex, "FocusPanel");
				return true;
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool CombatFocusPanelBody(Component __instance)
		{
			if (!Plugin.CfgEnabled.Value)
			{
				return true;
			}
			float value = Plugin.CfgCombatHoverOffsetX.Value;
			if (Mathf.Approximately(value, 0f) || !(__instance is MonoBehaviour owner))
			{
				return true;
			}
			Traverse traverse = Traverse.Create(__instance);
			MovePanel value2 = traverse.Field("_movePanel").GetValue<MovePanel>();
			if (value2 == null)
			{
				return true;
			}
			Vector3 originAnchoredPosition = value2.OriginAnchoredPosition;
			originAnchoredPosition.x -= value;
			float value3 = traverse.Field("_focusDuration").GetValue<float>();
			Ease value4 = traverse.Field("_moveEase").GetValue<Ease>();
			owner.StartCoroutine(value2.LocalMoveTo(originAnchoredPosition, value3, value4));
			return false;
		}

		private static void LeanTmpFont_Postfix(Component __instance)
		{
			if (!F.Layout.Live)
			{
				return;
			}
			try
			{
				F.Layout.Probe();
				if (!(__instance == null))
				{
					UIFixMarker component = __instance.GetComponent<UIFixMarker>();
					if (component != null && component.HasOnTextChangeRule && !component.Reentrancy)
					{
						RuleApplier.Process(__instance.gameObject, fromTextChange: true);
					}
				}
			}
			catch (Exception ex)
			{
				F.Layout.Fail(ex, "Lean TMP font");
			}
		}

		private static void Text_set_font_Postfix(Text __instance)
		{
			if (!F.Layout.Live)
			{
				return;
			}
			if (_inFontSetter)
			{
				return;
			}
			try
			{
				F.Layout.Probe();
				_inFontSetter = true;
				FontMap.Apply(__instance);
			}
			catch (Exception ex)
			{
				F.Layout.Fail(ex, "Text.font");
			}
			finally
			{
				_inFontSetter = false;
			}
		}

		private static void PatchPostfix(Harmony h, Type type, string method, string postfixName)
		{
			Compat.Hook(F.Layout, h, AccessTools.Method(type, method), type.Name + "." + method, null, new HarmonyMethod(typeof(Hooks), postfixName)
			{
				priority = 0
			});
		}

		private static void OnEnable_Component(Component __instance)
		{
			if (!F.Layout.Live)
			{
				return;
			}
			try
			{
				F.Layout.Probe();
				if (!(__instance == null))
				{
					GameObject gameObject = __instance.gameObject;
					if (__instance is Image img)
					{
						SpriteStore.TryReplace(img);
					}
					if (__instance is Text t)
					{
						FontMap.Apply(t);
					}
					RuleApplier.Process(gameObject);
				}
			}
			catch (Exception ex)
			{
				F.Layout.Fail(ex, "OnEnable");
			}
		}

		private static void Text_set_text_Postfix(Text __instance)
		{
			if (!F.Layout.Live)
			{
				return;
			}
			try
			{
				F.Layout.Probe();
				if (__instance == null)
				{
					return;
				}
				if (!_inTextSetter)
				{
					string text = __instance.text;
					string text2 = StringOverrides.PostProcessDynamic(text);
					if ((object)text2 != text)
					{
						try
						{
							_inTextSetter = true;
							__instance.text = text2;
						}
						finally
						{
							_inTextSetter = false;
						}
					}
				}
				UIFixMarker component = __instance.GetComponent<UIFixMarker>();
				if (component != null && component.HasOnTextChangeRule && !component.Reentrancy)
				{
					RuleApplier.Process(__instance.gameObject, fromTextChange: true);
				}
			}
			catch (Exception ex)
			{
				F.Layout.Fail(ex, "Text.text");
			}
		}

		private static void TMP_set_text_Postfix(TMP_Text __instance)
		{
			if (!F.Layout.Live)
			{
				return;
			}
			try
			{
				F.Layout.Probe();
				if (!(__instance == null))
				{
					UIFixMarker component = __instance.GetComponent<UIFixMarker>();
					if (component != null && component.HasOnTextChangeRule && !component.Reentrancy)
					{
						RuleApplier.Process(__instance.gameObject, fromTextChange: true);
					}
				}
			}
			catch (Exception ex)
			{
				F.Layout.Fail(ex, "TMP_Text.text");
			}
		}

		private static void Image_set_sprite_Postfix(Image __instance)
		{
			if (!F.Layout.Live)
			{
				return;
			}
			if (_inSpriteSetter)
			{
				return;
			}
			try
			{
				F.Layout.Probe();
				_inSpriteSetter = true;
				SpriteStore.TryReplace(__instance);
			}
			catch (Exception ex)
			{
				F.Layout.Fail(ex, "Image.sprite");
			}
			finally
			{
				_inSpriteSetter = false;
			}
		}
	}
	public static class PathUtil
	{
		[ThreadStatic]
		private static List<string> _parts;

		public static string GetPath(Transform t)
		{
			if (t == null)
			{
				return "";
			}
			if (_parts == null)
			{
				_parts = new List<string>(24);
			}
			_parts.Clear();
			Transform transform = t;
			int num = 0;
			while (transform != null && num++ < 128)
			{
				_parts.Add(transform.name);
				transform = transform.parent;
			}
			StringBuilder stringBuilder = new StringBuilder(_parts.Count * 12);
			for (int num2 = _parts.Count - 1; num2 >= 0; num2--)
			{
				stringBuilder.Append('/');
				stringBuilder.Append(_parts[num2]);
			}
			return stringBuilder.ToString();
		}

		public static string GetPath(GameObject go)
		{
			if (!(go == null))
			{
				return GetPath(go.transform);
			}
			return "";
		}

		public static Transform FindByPath(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				return null;
			}
			path = path.Trim();
			if (!path.StartsWith("/"))
			{
				path = "/" + path;
			}
			string[] array = path.Substring(1).Split('/');
			foreach (Transform allRoot in GetAllRoots())
			{
				if (allRoot.name != array[0])
				{
					continue;
				}
				Transform transform = allRoot;
				bool flag = true;
				for (int i = 1; i < array.Length; i++)
				{
					Transform transform2 = FindChildExact(transform, array[i]);
					if (transform2 == null)
					{
						flag = false;
						break;
					}
					transform = transform2;
				}
				if (flag)
				{
					return transform;
				}
			}
			return null;
		}

		private static Transform FindChildExact(Transform parent, string name)
		{
			int num = 0;
			if (name.EndsWith("]"))
			{
				int num2 = name.LastIndexOf('[');
				if (num2 > 0 && int.TryParse(name.Substring(num2 + 1, name.Length - num2 - 2), out var result))
				{
					num = result;
					name = name.Substring(0, num2);
				}
			}
			int num3 = 0;
			for (int i = 0; i < parent.childCount; i++)
			{
				Transform child = parent.GetChild(i);
				if (child.name == name)
				{
					if (num3 == num)
					{
						return child;
					}
					num3++;
				}
			}
			return null;
		}

		public static List<Transform> GetAllRoots()
		{
			List<Transform> list = new List<Transform>();
			HashSet<int> hashSet = new HashSet<int>();
			for (int i = 0; i < SceneManager.sceneCount; i++)
			{
				Scene sceneAt = SceneManager.GetSceneAt(i);
				if (!sceneAt.isLoaded)
				{
					continue;
				}
				GameObject[] rootGameObjects = sceneAt.GetRootGameObjects();
				foreach (GameObject gameObject in rootGameObjects)
				{
					if (hashSet.Add(gameObject.GetInstanceID()))
					{
						list.Add(gameObject.transform);
					}
				}
			}
			Transform[] array = Resources.FindObjectsOfTypeAll<Transform>();
			foreach (Transform transform in array)
			{
				if (!(transform == null) && !(transform.parent != null))
				{
					GameObject gameObject2 = transform.gameObject;
					if (gameObject2.scene.IsValid() && gameObject2.scene.isLoaded && gameObject2.hideFlags == HideFlags.None && hashSet.Add(gameObject2.GetInstanceID()))
					{
						list.Add(transform);
					}
				}
			}
			return list;
		}

		public static IEnumerable<Transform> AllSceneTransforms()
		{
			Transform[] array = Resources.FindObjectsOfTypeAll<Transform>();
			foreach (Transform transform in array)
			{
				if (!(transform == null))
				{
					GameObject gameObject = transform.gameObject;
					if (gameObject.scene.IsValid() && gameObject.scene.isLoaded && gameObject.hideFlags == HideFlags.None)
					{
						yield return transform;
					}
				}
			}
		}
	}
	[BepInPlugin("lom.ui.english", "LOM_UI_EN", VERSION)]
	// Soft dependencies only: none of these is needed, but when the original English patch is installed its plugins load first,
	// so its hooks and tables exist when OriginalMod looks for them.
	[BepInDependency("binarizer.plugin.mortal", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency(OriginalMod.LlmKitGuid, BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency(BaseGuards.PackGuid + ".TextResizer", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency(BaseGuards.PackGuid + ".PrefabTextReplacer", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency(BaseGuards.PackGuid + ".DynamicStringPatcher", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency("gravydevsupreme.xunity.autotranslator", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency("gravydevsupreme.xunity.resourceredirector", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency("LegendOfMortal_UI_KR", BepInDependency.DependencyFlags.SoftDependency)]
	public class Plugin : BaseUnityPlugin
	{
		public const string GUID = "lom.ui.english";

		public const string NAME = "LOM_UI_EN";

		public const string VERSION = "1.1.0";

		/// <summary>The mod's public name (the plugin keeps NAME and GUID, so configs and logs stay the same).</summary>
		public const string DisplayName = "LOM English Localization + QoL";

		/// <summary>Where players get updates and report problems.</summary>
		public const string ProjectUrl = "github.com/DataRogue/LOM-English-Localization-QoL";

		/// <summary>What players call the English patch this mod grew from (github.com/joshfreitas1984/LegendOfMortalOverLlm; the
		/// code, the configs' enum values and the tools call it OverLlm, Original or the base patch). Owner's naming, 2026-09-27.</summary>
		public const string BaseModName = "Lash's English Patch";

		/// <summary>BaseModName where a button only has room for one word (the Which translation choices).</summary>
		public const string BaseModShortName = "Lash's";

		public static ConfigEntry<bool> CfgEnabled;

		public static ConfigEntry<bool> CfgSpriteReplace;

		public static ConfigEntry<float> CfgCombatHoverOffsetX;

		public static ConfigEntry<string> CfgTmpFallbackFont;

		public static ConfigEntry<bool> CfgHarness;

		public static ConfigEntry<bool> CfgVerbose;

		public static ConfigEntry<KeyCode> CfgDumpKey;

		public static ConfigEntry<KeyCode> CfgReloadKey;

		public static ConfigEntry<bool> CfgFullScenePass;

		public static ConfigEntry<bool> CfgLogGuard;

		public static ConfigEntry<int> CfgLogGlyphBudget;

		public static ConfigEntry<bool> CfgLazyLog;

		public static ConfigEntry<bool> CfgQuietBinarizer;

		public static ConfigEntry<bool> CfgNoInfoStackTraces;

		public static ConfigEntry<float> CfgTextSpeed;
		public static ConfigEntry<bool> CfgRelationshipHover;
		public static ConfigEntry<string> CfgRelationshipHoverFormat;

		private static bool _firstPassDone;

		private static readonly Queue<Action> _deferred = new Queue<Action>();

		private Harmony _harmony;

		private static bool _tmpFallbackDone;

		private static int _tmpFallbackAttempts;

		public static Plugin Instance { get; private set; }

		public static ManualLogSource Log { get; private set; }

		public static string PluginDir { get; private set; }

		private void Awake()
		{
			Instance = this;
			Log = base.Logger;
#if BIE5
			// BepInEx 6 always creates its manager object hidden (HideAndDontSave); BepInEx 5 only with [Chainloader]
			// HideManagerGameObject, off by default. This game then destroys the visible object, and every plugin's Update and
			// coroutines stop with it (found in the 1.1.0 test: the harness never ran, nor would F8, quick save or the deferred
			// text passes). Hide it as BepInEx 6 does, before the first scene loads.
			if ((base.gameObject.hideFlags & HideFlags.HideAndDontSave) != HideFlags.HideAndDontSave)
			{
				base.gameObject.hideFlags = HideFlags.HideAndDontSave;
				Log.LogInfo("BepInEx 5: the BepInEx manager object is now hidden from the game, as BepInEx 6 creates it, so the mod's per-frame work keeps running");
			}
#endif
			PluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			Compat.Bind(base.Config);
			CfgEnabled = base.Config.Bind("General", "Enabled", defaultValue: true, "Master switch for the UI fix rules.");
			CfgSpriteReplace = base.Config.Bind("General", "SpriteReplacement", defaultValue: true, "Replace baked-text sprites listed in sprites/spritemap.json.");
			CfgCombatHoverOffsetX = base.Config.Bind("General", "CombatHoverOffsetX", 60f, "How far a combat action card slides when hovered, in pixels. The game moves it 100px UP, which suited the original horizontal row of narrow cards; the rules turn those into a vertical stack down the right edge, where sliding up makes a card overlap the one above it. A positive value here slides the card LEFT instead, out of the stack towards the battlefield. Set to 0 to keep the game's own upward slide.");
			CfgTmpFallbackFont = base.Config.Bind("General", "TmpLatinFallbackFont", "SourceHanSerifTC-Bold", "Font registered as a global TextMeshPro fallback so TMP labels whose atlas has no Latin glyphs render properly. Name of one of the game's Font assets (recommended: SourceHanSerifTC-Bold, whose Latin glyphs are Source Serif) or of an existing TMP font asset. Empty = off.");
			CfgVerbose = base.Config.Bind("General", "VerboseLog", defaultValue: false, "Log every rule application.");
			CfgFullScenePass = base.Config.Bind("Performance", "FullScenePass", defaultValue: false, "Run the rule pass over every loaded scene on each scene load (0.1.0 behaviour). Off = only the scene that just loaded; objects from earlier scenes keep their applied rules and the OnEnable hooks still cover anything re-activated.");
			CfgQuietBinarizer = base.Config.Bind("Performance", "QuietBinarizerStringLog", defaultValue: true, "The Binarizer string-table hook writes a Debug.Log line for every string lookup (hundreds of thousands per session, each with a stack trace). Replace it with a silent lookup of the same table.");
			CfgNoInfoStackTraces = base.Config.Bind("Performance", "NoStackTraceForInfoLogs", defaultValue: true, "Stop Unity from capturing a stack trace for plain Debug.Log (Info) messages; warnings and errors keep theirs.");
			CfgLogGuard = base.Config.Bind("NarrativeLog", "GuardMeshLimit", defaultValue: true, "The story backlog (Log button) is one Unity Text; past ~16,000 glyphs Unity throws 'Mesh can not have more than 65000 vertices' and the log shows empty. Trim the shown history to the most recent entries that fit.");
			CfgLogGlyphBudget = base.Config.Bind("NarrativeLog", "GlyphBudget", 13000, "Maximum visible characters of backlog text (before dividing by the Outline/Shadow multiplier of the log Text). 16,250 is the hard limit without effects.");
			CfgRelationshipHover = base.Config.Bind("Social", "RelationshipHover", defaultValue: true, "Status > Social: hovering the affinity level digit, star or progress bar shows the exact value (0-100) that the game only draws as a level and a bar.");
			CfgRelationshipHoverFormat = base.Config.Bind("Social", "RelationshipHoverFormat", "{2} {0} / {1}", "Text of the hover tip. {0} = current value, {1} = maximum (100), {2} = the localized name of the stat (PlayerStat/relationship, 'Affinity').");
			CfgTextSpeed = base.Config.Bind("Story", "TextSpeedMultiplier", 2f, "Multiplies the typewriter reveal speed of every Fungus Writer (the four story dialogs ship at 60 characters per second, tuned for Chinese; English needs roughly twice that to read at the same pace). 1 = the game's own speed. Applied once per Writer when it starts; text tags that set their own speed are scaled the same way.");
			CfgLazyLog = base.Config.Bind("NarrativeLog", "LazyRebuild", defaultValue: true, "Rebuild the backlog text when the log is opened instead of after every spoken line (the game rebuilds and re-lays-out the whole history on each line even while the log is hidden).");
			CfgHarness = base.Config.Bind("Dev", "Harness", defaultValue: false, "Enable the file-driven dev harness (reads BepInEx/cache/LOM_UI_EN/harness/cmd.txt, writes screenshots and hierarchy dumps to harness/out). Off for normal play.");
			CfgDumpKey = base.Config.Bind("Dev", "DumpKey", KeyCode.F10, "Hotkey: dump active UI hierarchy + screenshot to BepInEx/cache/LOM_UI_EN/harness/out.");
			CfgReloadKey = base.Config.Bind("Dev", "ReloadKey", KeyCode.F11, "Hotkey: reload rules and sprites and re-apply to the live UI.");
			QuickSave.Bind(base.Config);
			NameTips.Bind(base.Config);
			BuildupGauges.Bind(base.Config);
			TranslationProfiles.Bind(base.Config);
			SceneText.Bind(base.Config);
			BaseGuards.Bind(base.Config);
			EnglishLanguage.Bind(base.Config);
			OriginalMod.Bind(base.Config);
			ModMenu.Bind(base.Config);
			Compat.Setup(F.Detector, OriginalMod.DetectEarly);
			try
			{
				Rules.Load(Path.Combine(PluginDir, "rules"));
				SpriteStore.Load(Path.Combine(PluginDir, "sprites"));
				FontMap.Load(PluginDir);
			}
			catch (Exception ex)
			{
				Log.LogError("Failed to load rules/sprites/fonts: " + ex);
			}
			try
			{
				// One feature at a time: whatever a game update breaks turns off on its own (Compat), the rest installs as usual.
				_harmony = new Harmony("lom.ui.english");
				Harmony h = _harmony;
				Compat.Setup(F.Layout, () => Hooks.Patch(h));
				CheckLayoutData();
				Compat.Setup(F.GameText, () => TextTable.Patch(h));
				Compat.Setup(F.SceneText, () => SceneText.Patch(h));
				Compat.Setup(F.Language, () => EnglishLanguage.Patch(h));
				Compat.Setup(F.QuietBinarizer, () => PerfPatches.Patch(h));
				Compat.Setup(F.BasePlugins, () => BaseGuards.Install(h));
				Compat.Setup(F.XUnityBridge, XUnityBridge.TryRegister);
				Compat.Setup(F.StoryLog, () => NarrativeLogGuard.Patch(h));
				Compat.Setup(F.TextSpeed, () => TextSpeedPatch.Patch(h));
				Compat.Setup(F.AffinityHover, () => RelationshipHover.Patch(h));
				Compat.Setup(F.Buildup, () => BuildupGauges.Patch(h));
				Compat.Setup(F.QuickSave, QuickSave.Probe);
				Compat.Setup(F.NameTips, () => NameTips.Patch(h));
				Compat.Setup(F.MenuButtons, () => ModMenu.Patch(h));
				ManualLogSource log = Log;
				bool isEnabled;
				BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(48, 5, out isEnabled);
				if (isEnabled)
				{
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted("LOM_UI_EN");
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(" ");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(VERSION);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(" loaded: ");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Rules.Count);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(" rules, ");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(SpriteStore.Count);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(" sprite replacements. Harness=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(CfgHarness.Value ? "on" : "off");
				}
				log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
			}
			catch (Exception ex2)
			{
				Log.LogError("Failed to apply Harmony patches: " + ex2);
			}
			SceneManager.sceneLoaded += OnSceneLoaded;
			SyncHarness();
			TranslationProfiles.ApplyStartup();
			base.gameObject.AddComponent<ModMenu>();
			Compat.ScheduleFinish();
		}

		/// <summary>Adds the dev harness when [Dev] Harness is on (it idles again when the setting goes off).</summary>
		public static void SyncHarness()
		{
			if (!CfgHarness.Value || Instance == null)
			{
				return;
			}
			Application.runInBackground = true;
			if (Instance.GetComponent<DevHarness>() == null)
			{
				Instance.gameObject.AddComponent<DevHarness>();
				Log.LogInfo("Plugin dir: " + PluginDir + " runInBackground=" + Application.runInBackground);
			}
		}

		/// <summary>
		/// The layout feature's data as loaded at startup: rules/*.json, sprites/spritemap.json, fonts.json. A part none of whose data
		/// could be read shows in Compatibility as not working, as a missing game member would, instead of the feature calling
		/// itself working with nothing to apply. (1.0.0 found this: the official Newtonsoft.Json build could read none of it in
		/// this game's stripped runtime, while every hook installed and the report said "working".)
		/// </summary>
		private static void CheckLayoutData()
		{
			Feature f = F.Layout;
			f.DeclaredParts.Add("rules");
			f.DeclaredParts.Add("images");
			f.DeclaredParts.Add("fonts");
			if (Rules.Files > 0 && Rules.Count == 0 && Rules.Unreadable > 0)
			{
				LayoutDataUnreadable(f, "rules", "no layout rule could be read (" + Rules.FirstUnreadable + ")");
			}
			if (SpriteStore.MapError != null)
			{
				LayoutDataUnreadable(f, "images", "spritemap.json could not be read (" + SpriteStore.MapError + ")");
			}
			if (FontMap.LoadError != null)
			{
				LayoutDataUnreadable(f, "fonts", "fonts.json could not be read (" + FontMap.LoadError + ")");
			}
		}

		private static void LayoutDataUnreadable(Feature f, string part, string what)
		{
			f.Problems.Add(what);
			Compat.MarkBroken(f, what, essential: false, part);
			Log.LogError("[" + f.Id + "] " + what);
		}

		/// <summary>Undo every applied rule, font substitution and image swap, then apply them again under the current settings (used when the layout switches change).</summary>
		public static void Reapply()
		{
			try
			{
				RuleApplier.ResetAllMarkers();
				if (!CfgEnabled.Value || !F.Layout.Live)
				{
					return;
				}
				RuleApplier.ApplyToAll(includeInactive: true);
				foreach (Text text in Resources.FindObjectsOfTypeAll<Text>())
				{
					if (text != null && text.gameObject.scene.IsValid())
					{
						FontMap.Apply(text);
					}
				}
				foreach (Image image in Resources.FindObjectsOfTypeAll<Image>())
				{
					if (image != null && image.gameObject.scene.IsValid())
					{
						SpriteStore.TryReplace(image);
					}
				}
			}
			catch (Exception ex)
			{
				Log.LogError("Re-apply failed: " + ex);
			}
		}

		private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			if (CfgVerbose.Value)
			{
				Log.LogInfo("Scene loaded: " + scene.name);
			}
			Compat.Tick(F.Detector, OriginalMod.OnSceneLoaded);
			if (!F.Layout.Live)
			{
				return;
			}
			EnsureTmpLatinFallback();
			long callsAtStart = Rules.MatchCalls;
			long testsAtStart = Rules.RegexTests;
			int appsAtStart = RuleApplier.Applications;
			long hitsAtStart = Rules.CacheHits;
			Defer(delegate
			{
				Stopwatch stopwatch = Stopwatch.StartNew();
				int visited;
				if (CfgFullScenePass.Value || !_firstPassDone)
				{
					RuleApplier.ApplyToAll(includeInactive: true);
					visited = -1;
				}
				else
				{
					visited = RuleApplier.ApplyToScene(scene);
				}
				_firstPassDone = true;
				stopwatch.Stop();
				Log.LogInfo($"scene '{scene.name}': rule pass {stopwatch.ElapsedMilliseconds} ms ({Rules.MatchCalls - callsAtStart} lookups, {Rules.CacheHits - hitsAtStart} cached, {Rules.RegexTests - testsAtStart} regex tests, {RuleApplier.Applications - appsAtStart} rules applied{(visited >= 0 ? $", {visited} objects in scene" : ", all scenes")})");
			});
		}

		private static void EnsureTmpLatinFallback()
		{
			if (_tmpFallbackDone || !CfgEnabled.Value)
			{
				return;
			}
			string value = CfgTmpFallbackFont.Value;
			if (string.IsNullOrEmpty(value))
			{
				_tmpFallbackDone = true;
				return;
			}
			try
			{
				TMP_FontAsset tmpFont = RuleApplier.GetTmpFont(value);
				if (tmpFont == null)
				{
					if (++_tmpFallbackAttempts >= 8)
					{
						Log.LogWarning("TMP Latin fallback: giving up on " + value);
						_tmpFallbackDone = true;
					}
					return;
				}
				List<TMP_FontAsset> list = TMP_Settings.fallbackFontAssets;
				if (list == null)
				{
					list = new List<TMP_FontAsset>();
					FieldInfo fieldInfo = AccessTools.Field(typeof(TMP_Settings), "m_fallbackFontAssets");
					if (fieldInfo == null)
					{
						Log.LogWarning("TMP_Settings.m_fallbackFontAssets not found");
						_tmpFallbackDone = true;
						return;
					}
					fieldInfo.SetValue(TMP_Settings.instance, list);
				}
				if (!list.Contains(tmpFont))
				{
					list.Insert(0, tmpFont);
				}
				_tmpFallbackDone = true;
				Log.LogInfo("TMP global Latin fallback font: " + value);
			}
			catch (Exception ex)
			{
				Log.LogWarning("TMP fallback setup failed: " + ex.Message);
				_tmpFallbackDone = true;
			}
		}

		public static void Defer(Action a)
		{
			lock (_deferred)
			{
				_deferred.Enqueue(a);
			}
		}

#if BIE5
		/// <summary>BepInEx 5 has no chainloader Finished event: Start comes after every plugin's Awake (Loader.WhenAllLoaded).</summary>
		private void Start()
		{
			Loader.AllLoaded();
		}
#endif

		private void Update()
		{
			if (_deferred.Count > 0)
			{
				Action[] array;
				lock (_deferred)
				{
					array = _deferred.ToArray();
					_deferred.Clear();
				}
				Action[] array2 = array;
				foreach (Action action in array2)
				{
					try
					{
						action();
					}
					catch (Exception ex)
					{
						Log.LogError("Deferred action failed: " + ex);
					}
				}
			}
			Compat.Tick(F.SceneText, SceneText.Tick);
			try
			{
				SceneText.FlushLog();
			}
			catch (Exception)
			{
			}
			if (ModMenu.IsCapturingKey)
			{
				return;
			}
			if (!ModMenu.IsOpen && Input.GetKeyDown(CfgReloadKey.Value))
			{
				Reload();
			}
			if (Input.GetKeyDown(CfgDumpKey.Value))
			{
				(GetComponent<DevHarness>() ?? base.gameObject.AddComponent<DevHarness>()).HotkeyDump();
			}
			Compat.Tick(F.QuickSave, QuickSave.Update);
			Compat.Tick(F.NameTips, NameTips.Update);
			Compat.Tick(F.Buildup, BuildupGauges.Update);
		}

		public static void Reload()
		{
			try
			{
				Rules.Load(Path.Combine(PluginDir, "rules"));
				SpriteStore.Load(Path.Combine(PluginDir, "sprites"));
				FontMap.Load(PluginDir);
				StringOverrides.Load();
				StringOverrides.Refresh();
				NameTips.Reload();
				RuleApplier.ResetAllMarkers();
				RuleApplier.ApplyToAll(includeInactive: true);
				Text[] array = Resources.FindObjectsOfTypeAll<Text>();
				foreach (Text text in array)
				{
					if (text != null && text.gameObject.scene.IsValid())
					{
						FontMap.Apply(text);
					}
				}
				ManualLogSource log = Log;
				bool isEnabled;
				BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(51, 3, out isEnabled);
				if (isEnabled)
				{
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("Reloaded: ");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Rules.Count);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(" rules, ");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(SpriteStore.Count);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(" sprite replacements, ");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(FontMap.Count);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(" font maps.");
				}
				log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
			}
			catch (Exception ex)
			{
				Log.LogError("Reload failed: " + ex);
			}
		}
	}
	public class UIFixMarker : MonoBehaviour
	{
		public int RulesVersion = -1;

		public string Path;

		public HashSet<int> AppliedOnce = new HashSet<int>();

		public bool HasOnTextChangeRule;

		public List<Rule> Matches;

		public bool MatchesResolved;

		public Transform PathParent;

		public Transform PathRoot;

		public bool Reentrancy;

		public bool FontScaleApplied;

		public bool HasRectOriginal;

		public Vector2 OAnchorMin;

		public Vector2 OAnchorMax;

		public Vector2 OAnchoredPos;

		public Vector2 OSizeDelta;

		public Vector2 OPivot;

		public Quaternion ORotation;

		public Vector3 OScale;

		public bool HasTextOriginal;

		public int OFontSize;

		public bool OBestFit;

		public int OMin;

		public int OMax;

		public TextAnchor OAlign;

		public HorizontalWrapMode OHWrap;

		public VerticalWrapMode OVWrap;

		public float OLineSpacing;

		public Font OFont;

		public FontStyle OFontStyle;

		public Color OColor;

		public bool HasTmpOriginal;

		public float OTmpSize;

		public bool OTmpAuto;

		public float OTmpMin;

		public float OTmpMax;

		public TextAlignmentOptions OTmpAlign;

		public TextOverflowModes OTmpOverflow;

		public bool OTmpWrap;

		public Vector4 OTmpMargin;

		public float OTmpChar;

		public float OTmpWord;

		public float OTmpLine;

		public float OTmpPara;

		public Color OTmpColor;

		public TMP_FontAsset OTmpFont;

		public TMP_Style OTmpStyle;

		public bool HasImageOriginal;

		public Sprite OSprite;

		public Color OImageColor;

		public bool OPreserveAspect;

		public Image.Type OImageType;

		public bool OImageEnabled;

		public bool HasActiveOriginal;

		public bool OActive;

		// Which of the state-like properties (the ones the game itself also changes: hover colours, fades, sprite swaps) a rule or
		// the sprite map actually set. Restore puts only these back, so switching the layout off in the Mod Settings window does
		// not replay colours, sprites or enabled flags captured at some earlier moment onto a screen that has moved on.
		public bool TouchedTextColor;

		public bool TouchedTmpColor;

		public bool TouchedSprite;

		public bool TouchedImageColor;

		public bool TouchedImageEnabled;

		public void SetPath(string path, Transform t)
		{
			Path = path;
			PathParent = t.parent;
			PathRoot = t.root;
		}

		public bool HasMoved(Transform t)
		{
			if (Path != null)
			{
				if (!(PathParent != t.parent))
				{
					return PathRoot != t.root;
				}
				return true;
			}
			return false;
		}

		public void CaptureRect(RectTransform rt)
		{
			if (!HasRectOriginal && !(rt == null))
			{
				HasRectOriginal = true;
				OAnchorMin = rt.anchorMin;
				OAnchorMax = rt.anchorMax;
				OAnchoredPos = rt.anchoredPosition;
				OSizeDelta = rt.sizeDelta;
				OPivot = rt.pivot;
				ORotation = rt.localRotation;
				OScale = rt.localScale;
			}
		}

		public void CaptureText(Text t)
		{
			if (!HasTextOriginal && !(t == null))
			{
				HasTextOriginal = true;
				OFontSize = t.fontSize;
				OBestFit = t.resizeTextForBestFit;
				OMin = t.resizeTextMinSize;
				OMax = t.resizeTextMaxSize;
				OAlign = t.alignment;
				OHWrap = t.horizontalOverflow;
				OVWrap = t.verticalOverflow;
				OLineSpacing = t.lineSpacing;
				OFont = t.font;
				OFontStyle = t.fontStyle;
				OColor = t.color;
			}
		}

		public void CaptureTmp(TMP_Text t)
		{
			if (!HasTmpOriginal && !(t == null))
			{
				HasTmpOriginal = true;
				OTmpSize = t.fontSize;
				OTmpAuto = t.enableAutoSizing;
				OTmpMin = t.fontSizeMin;
				OTmpMax = t.fontSizeMax;
				OTmpAlign = t.alignment;
				OTmpOverflow = t.overflowMode;
				OTmpWrap = t.enableWordWrapping;
				OTmpMargin = t.margin;
				OTmpChar = t.characterSpacing;
				OTmpWord = t.wordSpacing;
				OTmpLine = t.lineSpacing;
				OTmpPara = t.paragraphSpacing;
				OTmpColor = t.color;
				OTmpFont = t.font;
				OTmpStyle = t.textStyle;
			}
		}

		public void CaptureImage(Image img)
		{
			if (!HasImageOriginal && !(img == null))
			{
				HasImageOriginal = true;
				OSprite = img.sprite;
				OImageColor = img.color;
				OPreserveAspect = img.preserveAspect;
				OImageType = img.type;
				OImageEnabled = img.enabled;
			}
		}

		public void Restore()
		{
			try
			{
				RectTransform component = GetComponent<RectTransform>();
				if (HasRectOriginal && component != null)
				{
					component.anchorMin = OAnchorMin;
					component.anchorMax = OAnchorMax;
					component.anchoredPosition = OAnchoredPos;
					component.sizeDelta = OSizeDelta;
					component.pivot = OPivot;
					component.localRotation = ORotation;
					component.localScale = OScale;
				}
				Text component2 = GetComponent<Text>();
				if (HasTextOriginal && component2 != null)
				{
					component2.fontSize = OFontSize;
					component2.resizeTextForBestFit = OBestFit;
					component2.resizeTextMinSize = OMin;
					component2.resizeTextMaxSize = OMax;
					component2.alignment = OAlign;
					component2.horizontalOverflow = OHWrap;
					component2.verticalOverflow = OVWrap;
					component2.lineSpacing = OLineSpacing;
					if (OFont != null)
					{
						component2.font = OFont;
					}
					component2.fontStyle = OFontStyle;
					if (TouchedTextColor)
					{
						component2.color = OColor;
					}
				}
				TMP_Text component3 = GetComponent<TMP_Text>();
				if (HasTmpOriginal && component3 != null)
				{
					component3.fontSize = OTmpSize;
					component3.enableAutoSizing = OTmpAuto;
					component3.fontSizeMin = OTmpMin;
					component3.fontSizeMax = OTmpMax;
					component3.alignment = OTmpAlign;
					component3.overflowMode = OTmpOverflow;
					component3.enableWordWrapping = OTmpWrap;
					component3.margin = OTmpMargin;
					component3.characterSpacing = OTmpChar;
					component3.wordSpacing = OTmpWord;
					component3.lineSpacing = OTmpLine;
					component3.paragraphSpacing = OTmpPara;
					if (TouchedTmpColor)
					{
						component3.color = OTmpColor;
					}
					if (OTmpFont != null)
					{
						component3.font = OTmpFont;
					}
					if (OTmpStyle != null)
					{
						component3.textStyle = OTmpStyle;
					}
				}
				Image component4 = GetComponent<Image>();
				if (HasImageOriginal && component4 != null)
				{
					// Only take the sprite back while one of ours (or a rule's 'none') is still showing; if the game has swapped the
					// sprite since, its choice stands.
					if (TouchedSprite && (component4.sprite == null || SpriteStore.IsOurs(component4.sprite)))
					{
						component4.sprite = OSprite;
					}
					if (TouchedImageColor)
					{
						component4.color = OImageColor;
					}
					component4.preserveAspect = OPreserveAspect;
					component4.type = OImageType;
					if (TouchedImageEnabled)
					{
						component4.enabled = OImageEnabled;
					}
				}
				if (HasActiveOriginal)
				{
					base.gameObject.SetActive(OActive);
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("Restore failed for " + Path + ": " + ex.Message);
			}
			AppliedOnce.Clear();
			Matches = null;
			MatchesResolved = false;
			FontScaleApplied = false;
			RulesVersion = -1;
			TouchedTextColor = false;
			TouchedTmpColor = false;
			TouchedSprite = false;
			TouchedImageColor = false;
			TouchedImageEnabled = false;
		}
	}
	public static class RuleApplier
	{
		private static readonly Dictionary<string, Font> _osFonts = new Dictionary<string, Font>();

		public static int Applications;

		private static readonly Dictionary<string, TMP_FontAsset> _tmpFonts = new Dictionary<string, TMP_FontAsset>();

		private static readonly HashSet<string> _tmpFontUnbuildable = new HashSet<string>(StringComparer.Ordinal);

		public static void Process(GameObject go, bool fromTextChange = false)
		{
			if (!Plugin.CfgEnabled.Value || go == null || !F.Layout.Live)
			{
				return;
			}
			try
			{
				ProcessSingle(go, fromTextChange);
				if (!fromTextChange)
				{
					Transform parent = go.transform.parent;
					int num = 0;
					while (parent != null && num++ < 64)
					{
						ProcessSingle(parent.gameObject, fromTextChange: false, ancestorsOnly: true);
						parent = parent.parent;
					}
				}
			}
			catch (Exception t)
			{
				// Counted against the layout feature: a screen the game changed costs its own rules; many failures turn layout off.
				F.Layout.Fail(t, "rules on " + SafePath(go));
			}
		}

		private static string SafePath(GameObject go)
		{
			try
			{
				return PathUtil.GetPath(go);
			}
			catch (Exception)
			{
				return "?";
			}
		}

		private static void ProcessSingle(GameObject go, bool fromTextChange, bool ancestorsOnly = false)
		{
			UIFixMarker uIFixMarker = go.GetComponent<UIFixMarker>();
			if (uIFixMarker != null && uIFixMarker.RulesVersion == Rules.Version && uIFixMarker.HasMoved(go.transform))
			{
				uIFixMarker.RulesVersion = -1;
				uIFixMarker.Matches = null;
				uIFixMarker.MatchesResolved = false;
			}
			if (ancestorsOnly && uIFixMarker != null && uIFixMarker.RulesVersion == Rules.Version)
			{
				return;
			}
			int num;
			string path;
			if (uIFixMarker != null)
			{
				num = ((uIFixMarker.RulesVersion == Rules.Version) ? 1 : 0);
				if (num != 0 && uIFixMarker.Path != null)
				{
					path = uIFixMarker.Path;
					goto IL_008c;
				}
			}
			else
			{
				num = 0;
			}
			path = PathUtil.GetPath(go);
			goto IL_008c;
			IL_008c:
			string text = path;
			List<Rule> list;
			if (num != 0 && uIFixMarker.MatchesResolved)
			{
				list = uIFixMarker.Matches;
			}
			else
			{
				list = Rules.Match(text);
				if (uIFixMarker != null)
				{
					uIFixMarker.Matches = list;
					uIFixMarker.MatchesResolved = true;
				}
			}
			if (list == null)
			{
				if (uIFixMarker != null)
				{
					uIFixMarker.RulesVersion = Rules.Version;
					uIFixMarker.SetPath(text, go.transform);
					uIFixMarker.Matches = null;
					uIFixMarker.MatchesResolved = true;
				}
				else if (ancestorsOnly)
				{
					UIFixMarker uIFixMarker2 = go.AddComponent<UIFixMarker>();
					uIFixMarker2.hideFlags = HideFlags.HideAndDontSave;
					uIFixMarker2.RulesVersion = Rules.Version;
					uIFixMarker2.SetPath(text, go.transform);
					uIFixMarker2.Matches = null;
					uIFixMarker2.MatchesResolved = true;
				}
				return;
			}
			if (uIFixMarker == null)
			{
				uIFixMarker = go.AddComponent<UIFixMarker>();
				uIFixMarker.hideFlags = HideFlags.HideAndDontSave;
			}
			if (uIFixMarker.Reentrancy)
			{
				return;
			}
			uIFixMarker.Reentrancy = true;
			try
			{
				if (uIFixMarker.RulesVersion != Rules.Version)
				{
					uIFixMarker.AppliedOnce.Clear();
					uIFixMarker.RulesVersion = Rules.Version;
					uIFixMarker.SetPath(text, go.transform);
					uIFixMarker.HasOnTextChangeRule = false;
				}
				bool touchedLayout = false;
				foreach (Rule item in list)
				{
					if (item.onTextChange)
					{
						uIFixMarker.HasOnTextChangeRule = true;
					}
					if ((fromTextChange && !item.onTextChange) || (!fromTextChange && !item.always && !item.onTextChange && uIFixMarker.AppliedOnce.Contains(item.Index)))
					{
						continue;
					}
					if (item.englishOnly && !EnglishLanguage.Active)
					{
						continue;
					}
					Apply(go, uIFixMarker, item, ref touchedLayout);
					uIFixMarker.AppliedOnce.Add(item.Index);
					Applications++;
					if (Plugin.CfgVerbose.Value)
					{
						ManualLogSource log = Plugin.Log;
						bool isEnabled;
						BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(14, 2, out isEnabled);
						if (isEnabled)
						{
							bepInExInfoLogInterpolatedStringHandler.AppendLiteral("Applied [");
							bepInExInfoLogInterpolatedStringHandler.AppendFormatted(item);
							bepInExInfoLogInterpolatedStringHandler.AppendLiteral("] to ");
							bepInExInfoLogInterpolatedStringHandler.AppendFormatted(text);
						}
						log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
					}
				}
				if (touchedLayout)
				{
					RectTransform rectTransform = go.transform as RectTransform;
					if (rectTransform != null)
					{
						LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
					}
					RectTransform rectTransform2 = go.transform.parent as RectTransform;
					if (rectTransform2 != null)
					{
						LayoutRebuilder.MarkLayoutForRebuild(rectTransform2);
					}
				}
			}
			finally
			{
				uIFixMarker.Reentrancy = false;
			}
		}

		/// <summary>Rule pass over one scene only (its root objects and all their children, inactive included).</summary>
		public static int ApplyToScene(Scene scene)
		{
			if (!Plugin.CfgEnabled.Value || !scene.IsValid() || !scene.isLoaded)
			{
				return 0;
			}
			int num = 0;
			GameObject[] roots = scene.GetRootGameObjects();
			for (int r = 0; r < roots.Length; r++)
			{
				if (roots[r] == null)
				{
					continue;
				}
				RectTransform[] rects = roots[r].GetComponentsInChildren<RectTransform>(true);
				for (int i = 0; i < rects.Length; i++)
				{
					if (!F.Layout.Live)
					{
						return num;
					}
					RectTransform rt = rects[i];
					if (rt != null && rt.gameObject.hideFlags == HideFlags.None)
					{
						try
						{
							ProcessSingle(rt.gameObject, fromTextChange: false);
						}
						catch (Exception ex)
						{
							F.Layout.Fail(ex, "rules on " + SafePath(rt.gameObject));
						}
						num++;
					}
				}
			}
			return num;
		}

		public static void ApplyToAll(bool includeInactive)
		{
			if (!Plugin.CfgEnabled.Value)
			{
				return;
			}
			int num = 0;
			foreach (Transform item in PathUtil.AllSceneTransforms())
			{
				if (!F.Layout.Live)
				{
					break;
				}
				if (item is RectTransform && (includeInactive || item.gameObject.activeInHierarchy))
				{
					try
					{
						ProcessSingle(item.gameObject, fromTextChange: false);
					}
					catch (Exception ex)
					{
						F.Layout.Fail(ex, "rules on " + SafePath(item.gameObject));
					}
					num++;
				}
			}
			if (Plugin.CfgVerbose.Value)
			{
				ManualLogSource log = Plugin.Log;
				bool isEnabled;
				BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(35, 1, out isEnabled);
				if (isEnabled)
				{
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("ApplyToAll visited ");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(num);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(" rect transforms");
				}
				log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
			}
		}

		public static void ResetAllMarkers()
		{
			UIFixMarker[] array = Resources.FindObjectsOfTypeAll<UIFixMarker>();
			foreach (UIFixMarker uIFixMarker in array)
			{
				if (!(uIFixMarker == null))
				{
					uIFixMarker.Restore();
				}
			}
		}

		private static void Apply(GameObject go, UIFixMarker m, Rule r, ref bool touchedLayout)
		{
			Rules.MarkMatched(r);
			if (r.active.HasValue)
			{
				if (!m.HasActiveOriginal)
				{
					m.HasActiveOriginal = true;
					m.OActive = go.activeSelf;
				}
				if (go.activeSelf != r.active.Value)
				{
					go.SetActive(r.active.Value);
				}
			}
			if (r.rect != null)
			{
				RectTransform rectTransform = go.transform as RectTransform;
				if (rectTransform != null)
				{
					m.CaptureRect(rectTransform);
					ApplyRect(rectTransform, m, r.rect);
					touchedLayout = true;
				}
			}
			if (r.text != null)
			{
				Text component = go.GetComponent<Text>();
				if (component != null)
				{
					m.CaptureText(component);
					ApplyText(component, m, r.text);
					touchedLayout = true;
				}
			}
			if (r.tmp != null)
			{
				TMP_Text component2 = go.GetComponent<TMP_Text>();
				if (component2 != null)
				{
					m.CaptureTmp(component2);
					ApplyTmp(component2, m, r.tmp);
					touchedLayout = true;
				}
			}
			if (r.layout != null)
			{
				ApplyLayout(go, r.layout);
				touchedLayout = true;
			}
			if (r.grid != null)
			{
				ApplyGrid(go, r.grid);
				touchedLayout = true;
			}
			if (r.fitter != null)
			{
				ApplyFitter(go, r.fitter);
				touchedLayout = true;
			}
			if (r.layoutElement != null)
			{
				ApplyLayoutElement(go, r.layoutElement);
				touchedLayout = true;
			}
			if (r.image != null)
			{
				Image component3 = go.GetComponent<Image>();
				if (component3 != null)
				{
					m.CaptureImage(component3);
					ApplyImage(component3, m, r.image);
				}
			}
			if (r.movePanel != null)
			{
				ApplyMovePanel(go, r.movePanel);
			}
			if (r.rebuildLayout == false)
			{
				touchedLayout = false;
			}
		}

		private static void ApplyRect(RectTransform rt, UIFixMarker m, RectRule rr)
		{
			if (rr.anchorMin != null)
			{
				rt.anchorMin = V2(rr.anchorMin);
			}
			if (rr.anchorMax != null)
			{
				rt.anchorMax = V2(rr.anchorMax);
			}
			if (rr.pivot != null)
			{
				rt.pivot = V2(rr.pivot);
			}
			if (rr.offsetMin != null)
			{
				rt.offsetMin = V2(rr.offsetMin);
			}
			if (rr.offsetMax != null)
			{
				rt.offsetMax = V2(rr.offsetMax);
			}
			if (rr.anchoredPos != null)
			{
				rt.anchoredPosition = V2(rr.anchoredPos);
			}
			if (rr.sizeDelta != null)
			{
				rt.sizeDelta = V2(rr.sizeDelta);
			}
			Vector2 anchoredPosition = rt.anchoredPosition;
			Vector2 sizeDelta = rt.sizeDelta;
			if (rr.x.HasValue)
			{
				anchoredPosition.x = rr.x.Value;
			}
			if (rr.y.HasValue)
			{
				anchoredPosition.y = rr.y.Value;
			}
			if (rr.width.HasValue)
			{
				sizeDelta.x = rr.width.Value;
			}
			if (rr.height.HasValue)
			{
				sizeDelta.y = rr.height.Value;
			}
			if (rr.dx.HasValue)
			{
				anchoredPosition.x = m.OAnchoredPos.x + rr.dx.Value;
			}
			if (rr.dy.HasValue)
			{
				anchoredPosition.y = m.OAnchoredPos.y + rr.dy.Value;
			}
			if (rr.dw.HasValue)
			{
				sizeDelta.x = m.OSizeDelta.x + rr.dw.Value;
			}
			if (rr.dh.HasValue)
			{
				sizeDelta.y = m.OSizeDelta.y + rr.dh.Value;
			}
			rt.anchoredPosition = anchoredPosition;
			rt.sizeDelta = sizeDelta;
			if (rr.scale != null)
			{
				rt.localScale = new Vector3(rr.scale[0], (rr.scale.Length > 1) ? rr.scale[1] : rr.scale[0], 1f);
			}
			if (rr.rotationZ.HasValue)
			{
				rt.localRotation = Quaternion.Euler(0f, 0f, rr.rotationZ.Value);
			}
			SyncMovePanel(rt);
		}

		private static Type _movePanelType;
		private static bool _movePanelResolved;

		/// <summary>The game's MovePanel, by name: no compile-time reference, so a rename only loses the slide-position sync.</summary>
		private static Component GetMovePanel(GameObject go)
		{
			if (!_movePanelResolved)
			{
				_movePanelResolved = true;
				_movePanelType = TranslationProfiles.FindType("Mortal.Core", "Mortal.Core.MovePanel");
				if (_movePanelType == null)
				{
					Plugin.Log.LogWarning("Mortal.Core.MovePanel not found: layout rules that move sliding panels no longer adjust their slide positions");
				}
			}
			return (_movePanelType != null) ? go.GetComponent(_movePanelType) : null;
		}

		private static void SyncMovePanel(RectTransform rt)
		{
			Component component = GetMovePanel(rt.gameObject);
			if (component == null)
			{
				return;
			}
			try
			{
				Traverse traverse = Traverse.Create(component);
				Traverse traverse2 = traverse.Field("_originAnchoredPosition");
				Traverse traverse3 = traverse.Field("_standbyAhcoredPosition");
				Vector3 value = traverse2.GetValue<Vector3>();
				Vector3 vector = traverse3.GetValue<Vector3>() - value;
				Vector3 vector2 = rt.anchoredPosition;
				traverse2.SetValue(vector2);
				traverse3.SetValue(vector2 + vector);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("MovePanel sync failed on " + rt.name + ": " + ex.Message);
			}
		}

		private static void ApplyMovePanel(GameObject go, MovePanelRule mr)
		{
			if (mr.standbyOffset == null || mr.standbyOffset.Length < 2)
			{
				return;
			}
			Component component = GetMovePanel(go);
			if (component == null)
			{
				return;
			}
			try
			{
				Vector3 vector = new Vector3(mr.standbyOffset[0], mr.standbyOffset[1], 0f);
				Traverse traverse = Traverse.Create(component);
				traverse.Field("_standbyOffset").SetValue(vector);
				Vector3 value = traverse.Field("_originAnchoredPosition").GetValue<Vector3>();
				traverse.Field("_standbyAhcoredPosition").SetValue(value + vector);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("MovePanel offset failed on " + go.name + ": " + ex.Message);
			}
		}

		private static void ApplyText(Text t, UIFixMarker m, TextRule tr)
		{
			if (tr.outline.HasValue)
			{
				Outline component = t.GetComponent<Outline>();
				if (component != null && component.enabled != tr.outline.Value)
				{
					component.enabled = tr.outline.Value;
				}
			}
			if (tr.fontSize.HasValue)
			{
				t.fontSize = Mathf.RoundToInt(tr.fontSize.Value);
			}
			if (tr.fontScale.HasValue)
			{
				t.fontSize = Mathf.Max(1, Mathf.RoundToInt((float)m.OFontSize * tr.fontScale.Value));
			}
			if (tr.bestFit.HasValue)
			{
				t.resizeTextForBestFit = tr.bestFit.Value;
			}
			if (tr.minSize.HasValue)
			{
				t.resizeTextMinSize = tr.minSize.Value;
			}
			if (tr.maxSize.HasValue)
			{
				t.resizeTextMaxSize = tr.maxSize.Value;
			}
			if (!string.IsNullOrEmpty(tr.alignment) && TryEnum<TextAnchor>(tr.alignment, out var value))
			{
				t.alignment = value;
			}
			if (!string.IsNullOrEmpty(tr.hOverflow) && TryEnum<HorizontalWrapMode>(tr.hOverflow, out var value2))
			{
				t.horizontalOverflow = value2;
			}
			if (!string.IsNullOrEmpty(tr.vOverflow) && TryEnum<VerticalWrapMode>(tr.vOverflow, out var value3))
			{
				t.verticalOverflow = value3;
			}
			if (tr.lineSpacing.HasValue)
			{
				t.lineSpacing = tr.lineSpacing.Value;
			}
			if (tr.richText.HasValue)
			{
				t.supportRichText = tr.richText.Value;
			}
			if (!string.IsNullOrEmpty(tr.fontStyle) && TryEnum<FontStyle>(tr.fontStyle, out var value4))
			{
				t.fontStyle = value4;
			}
			if (tr.raycastTarget.HasValue)
			{
				t.raycastTarget = tr.raycastTarget.Value;
			}
			if (!string.IsNullOrEmpty(tr.color) && TryColor(tr.color, out var c))
			{
				t.color = c;
				m.TouchedTextColor = true;
			}
			if (!string.IsNullOrEmpty(tr.font))
			{
				if (tr.font.Equals("default", StringComparison.OrdinalIgnoreCase))
				{
					if (m.OFont != null)
					{
						t.font = m.OFont;
					}
				}
				else
				{
					Font osFont = GetOsFont(tr.font);
					if (osFont != null)
					{
						t.font = osFont;
					}
				}
			}
			if (tr.text != null)
			{
				t.text = tr.text;
			}
		}

		private static void ApplyTmp(TMP_Text t, UIFixMarker m, TmpRule tr)
		{
			if (tr.fontSize.HasValue)
			{
				t.fontSize = tr.fontSize.Value;
			}
			if (tr.fontScale.HasValue)
			{
				t.fontSize = Mathf.Max(1f, m.OTmpSize * tr.fontScale.Value);
			}
			if (tr.autoSize.HasValue)
			{
				t.enableAutoSizing = tr.autoSize.Value;
			}
			if (tr.minSize.HasValue)
			{
				t.fontSizeMin = tr.minSize.Value;
			}
			if (tr.maxSize.HasValue)
			{
				t.fontSizeMax = tr.maxSize.Value;
			}
			if (!string.IsNullOrEmpty(tr.alignment) && TryEnum<TextAlignmentOptions>(tr.alignment, out var value))
			{
				t.alignment = value;
			}
			if (!string.IsNullOrEmpty(tr.overflow) && TryEnum<TextOverflowModes>(tr.overflow, out var value2))
			{
				t.overflowMode = value2;
			}
			if (tr.wordWrap.HasValue)
			{
				t.enableWordWrapping = tr.wordWrap.Value;
			}
			if (tr.margin != null && tr.margin.Length == 4)
			{
				t.margin = new Vector4(tr.margin[0], tr.margin[1], tr.margin[2], tr.margin[3]);
			}
			if (tr.charSpacing.HasValue)
			{
				t.characterSpacing = tr.charSpacing.Value;
			}
			if (tr.wordSpacing.HasValue)
			{
				t.wordSpacing = tr.wordSpacing.Value;
			}
			if (tr.lineSpacing.HasValue)
			{
				t.lineSpacing = tr.lineSpacing.Value;
			}
			if (tr.paragraphSpacing.HasValue)
			{
				t.paragraphSpacing = tr.paragraphSpacing.Value;
			}
			if (tr.richText.HasValue)
			{
				t.richText = tr.richText.Value;
			}
			if (tr.raycastTarget.HasValue)
			{
				t.raycastTarget = tr.raycastTarget.Value;
			}
			if (!string.IsNullOrEmpty(tr.color) && TryColor(tr.color, out var c))
			{
				t.color = c;
				m.TouchedTmpColor = true;
			}
			if (!string.IsNullOrEmpty(tr.font))
			{
				if (tr.font.Equals("default", StringComparison.OrdinalIgnoreCase))
				{
					if (m.OTmpFont != null)
					{
						t.font = m.OTmpFont;
					}
				}
				else
				{
					TMP_FontAsset tmpFont = GetTmpFont(tr.font);
					if (tmpFont != null)
					{
						t.font = tmpFont;
					}
				}
			}
			if (string.IsNullOrEmpty(tr.style))
			{
				return;
			}
			if (tr.style.Equals("default", StringComparison.OrdinalIgnoreCase))
			{
				if (m.OTmpStyle != null)
				{
					t.textStyle = m.OTmpStyle;
				}
			}
			else if (tr.style.Equals("normal", StringComparison.OrdinalIgnoreCase) || tr.style.Equals("none", StringComparison.OrdinalIgnoreCase))
			{
				t.textStyle = TMP_Style.NormalStyle;
			}
			else
			{
				TMP_StyleSheet defaultStyleSheet = TMP_Settings.defaultStyleSheet;
				TMP_Style tMP_Style = ((defaultStyleSheet != null) ? defaultStyleSheet.GetStyle(tr.style) : null);
				if (tMP_Style != null)
				{
					t.textStyle = tMP_Style;
				}
				else
				{
					Plugin.Log.LogWarning("TMP style not found: " + tr.style);
				}
			}
			t.SetAllDirty();
		}

		public static TMP_FontAsset GetTmpFont(string fontName)
		{
			if (_tmpFonts.TryGetValue(fontName, out var value) && value != null)
			{
				return value;
			}
			if (_tmpFontUnbuildable.Contains(fontName))
			{
				return null;
			}
			bool isEnabled;
			try
			{
				TMP_FontAsset[] array = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
				foreach (TMP_FontAsset tMP_FontAsset in array)
				{
					if (tMP_FontAsset != null && tMP_FontAsset.name == fontName)
					{
						_tmpFonts[fontName] = tMP_FontAsset;
						return tMP_FontAsset;
					}
				}
				MethodInfo methodInfo = AccessTools.Method(typeof(TMP_FontAsset), "CreateFontAsset", new Type[1] { typeof(Font) });
				if (methodInfo == null)
				{
					Plugin.Log.LogWarning("TMP_FontAsset.CreateFontAsset(Font) not available in this TMP version");
					return null;
				}
				Font font = null;
				Font[] array2 = Resources.FindObjectsOfTypeAll<Font>();
				foreach (Font font2 in array2)
				{
					if (font2 != null && font2.name == fontName && !_osFonts.ContainsValue(font2))
					{
						font = font2;
						break;
					}
				}
				string text = "game font";
				if (font == null)
				{
					font = GetOsFont(fontName);
					text = "OS font";
				}
				if (font == null)
				{
					Plugin.Log.LogWarning("TMP font: no font named " + fontName);
					return null;
				}
				value = methodInfo.Invoke(null, new object[1] { font }) as TMP_FontAsset;
				if (value != null)
				{
					value.name = "LOM_UI_EN " + fontName + " SDF";
					value.hideFlags = HideFlags.HideAndDontSave;
					_tmpFonts[fontName] = value;
					ManualLogSource log = Plugin.Log;
					BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(24, 2, out isEnabled);
					if (isEnabled)
					{
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral("TMP font created from ");
						bepInExInfoLogInterpolatedStringHandler.AppendFormatted(text);
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral(": ");
						bepInExInfoLogInterpolatedStringHandler.AppendFormatted(fontName);
					}
					log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
				}
				else
				{
					if (text == "OS font")
					{
						_tmpFontUnbuildable.Add(fontName);
					}
					ManualLogSource log2 = Plugin.Log;
					BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(114, 2, out isEnabled);
					if (isEnabled)
					{
						bepInExWarningLogInterpolatedStringHandler.AppendLiteral("TMP font: CreateFontAsset returned null for ");
						bepInExWarningLogInterpolatedStringHandler.AppendFormatted(fontName);
						bepInExWarningLogInterpolatedStringHandler.AppendLiteral(" (");
						bepInExWarningLogInterpolatedStringHandler.AppendFormatted(text);
						bepInExWarningLogInterpolatedStringHandler.AppendLiteral("; OS fonts have no font data for TMP 3.0) - not retried this session");
					}
					log2.LogWarning(bepInExWarningLogInterpolatedStringHandler);
				}
			}
			catch (Exception ex)
			{
				ManualLogSource log3 = Plugin.Log;
				BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(20, 2, out isEnabled);
				if (isEnabled)
				{
					bepInExWarningLogInterpolatedStringHandler.AppendLiteral("TMP font '");
					bepInExWarningLogInterpolatedStringHandler.AppendFormatted(fontName);
					bepInExWarningLogInterpolatedStringHandler.AppendLiteral("' failed: ");
					bepInExWarningLogInterpolatedStringHandler.AppendFormatted(ex.Message);
				}
				log3.LogWarning(bepInExWarningLogInterpolatedStringHandler);
			}
			return value;
		}

		private static void ApplyLayout(GameObject go, LayoutRule lr)
		{
			HorizontalOrVerticalLayoutGroup horizontalOrVerticalLayoutGroup = go.GetComponent<HorizontalOrVerticalLayoutGroup>();
			if (!string.IsNullOrEmpty(lr.type))
			{
				string text = lr.type.Trim().ToLowerInvariant();
				if (text == "none")
				{
					if (horizontalOrVerticalLayoutGroup != null)
					{
						HorizontalOrVerticalLayoutGroup e = horizontalOrVerticalLayoutGroup;
						Plugin.Defer(delegate
						{
							if (e != null)
							{
								UnityEngine.Object.Destroy(e);
							}
						});
						horizontalOrVerticalLayoutGroup = null;
					}
				}
				else
				{
					bool flag = text.StartsWith("h");
					bool flag2 = horizontalOrVerticalLayoutGroup is HorizontalLayoutGroup;
					if (horizontalOrVerticalLayoutGroup == null || flag2 != flag)
					{
						HorizontalOrVerticalLayoutGroup old = horizontalOrVerticalLayoutGroup;
						HorizontalOrVerticalLayoutGroup horizontalOrVerticalLayoutGroup2 = (flag ? ((HorizontalOrVerticalLayoutGroup)go.AddComponent<HorizontalLayoutGroup>()) : ((HorizontalOrVerticalLayoutGroup)go.AddComponent<VerticalLayoutGroup>()));
						if (old != null)
						{
							horizontalOrVerticalLayoutGroup2.padding = new RectOffset(old.padding.left, old.padding.right, old.padding.top, old.padding.bottom);
							horizontalOrVerticalLayoutGroup2.spacing = old.spacing;
							horizontalOrVerticalLayoutGroup2.childAlignment = old.childAlignment;
							horizontalOrVerticalLayoutGroup2.childControlWidth = old.childControlWidth;
							horizontalOrVerticalLayoutGroup2.childControlHeight = old.childControlHeight;
							horizontalOrVerticalLayoutGroup2.childForceExpandWidth = old.childForceExpandWidth;
							horizontalOrVerticalLayoutGroup2.childForceExpandHeight = old.childForceExpandHeight;
							horizontalOrVerticalLayoutGroup2.childScaleWidth = old.childScaleWidth;
							horizontalOrVerticalLayoutGroup2.childScaleHeight = old.childScaleHeight;
							horizontalOrVerticalLayoutGroup2.reverseArrangement = old.reverseArrangement;
							old.enabled = false;
							Plugin.Defer(delegate
							{
								if (old != null)
								{
									UnityEngine.Object.Destroy(old);
								}
							});
						}
						horizontalOrVerticalLayoutGroup = horizontalOrVerticalLayoutGroup2;
					}
				}
			}
			if (!(horizontalOrVerticalLayoutGroup == null))
			{
				if (lr.spacing.HasValue)
				{
					horizontalOrVerticalLayoutGroup.spacing = lr.spacing.Value;
				}
				if (lr.padding != null && lr.padding.Length == 4)
				{
					horizontalOrVerticalLayoutGroup.padding = new RectOffset((int)lr.padding[0], (int)lr.padding[1], (int)lr.padding[2], (int)lr.padding[3]);
				}
				if (!string.IsNullOrEmpty(lr.childAlignment) && TryEnum<TextAnchor>(lr.childAlignment, out var value))
				{
					horizontalOrVerticalLayoutGroup.childAlignment = value;
				}
				if (lr.childControlWidth.HasValue)
				{
					horizontalOrVerticalLayoutGroup.childControlWidth = lr.childControlWidth.Value;
				}
				if (lr.childControlHeight.HasValue)
				{
					horizontalOrVerticalLayoutGroup.childControlHeight = lr.childControlHeight.Value;
				}
				if (lr.childForceExpandWidth.HasValue)
				{
					horizontalOrVerticalLayoutGroup.childForceExpandWidth = lr.childForceExpandWidth.Value;
				}
				if (lr.childForceExpandHeight.HasValue)
				{
					horizontalOrVerticalLayoutGroup.childForceExpandHeight = lr.childForceExpandHeight.Value;
				}
				if (lr.childScaleWidth.HasValue)
				{
					horizontalOrVerticalLayoutGroup.childScaleWidth = lr.childScaleWidth.Value;
				}
				if (lr.childScaleHeight.HasValue)
				{
					horizontalOrVerticalLayoutGroup.childScaleHeight = lr.childScaleHeight.Value;
				}
				if (lr.reverseArrangement.HasValue)
				{
					horizontalOrVerticalLayoutGroup.reverseArrangement = lr.reverseArrangement.Value;
				}
				if (lr.enabled.HasValue)
				{
					horizontalOrVerticalLayoutGroup.enabled = lr.enabled.Value;
				}
			}
		}

		private static void ApplyGrid(GameObject go, GridRule gr)
		{
			GridLayoutGroup component = go.GetComponent<GridLayoutGroup>();
			if (!(component == null))
			{
				if (gr.cellSize != null)
				{
					component.cellSize = V2(gr.cellSize);
				}
				if (gr.spacing != null)
				{
					component.spacing = V2(gr.spacing);
				}
				if (!string.IsNullOrEmpty(gr.startCorner) && TryEnum<GridLayoutGroup.Corner>(gr.startCorner, out var value))
				{
					component.startCorner = value;
				}
				if (!string.IsNullOrEmpty(gr.startAxis) && TryEnum<GridLayoutGroup.Axis>(gr.startAxis, out var value2))
				{
					component.startAxis = value2;
				}
				if (!string.IsNullOrEmpty(gr.childAlignment) && TryEnum<TextAnchor>(gr.childAlignment, out var value3))
				{
					component.childAlignment = value3;
				}
				if (!string.IsNullOrEmpty(gr.constraint) && TryEnum<GridLayoutGroup.Constraint>(gr.constraint, out var value4))
				{
					component.constraint = value4;
				}
				if (gr.constraintCount.HasValue)
				{
					component.constraintCount = gr.constraintCount.Value;
				}
				if (gr.padding != null && gr.padding.Length == 4)
				{
					component.padding = new RectOffset((int)gr.padding[0], (int)gr.padding[1], (int)gr.padding[2], (int)gr.padding[3]);
				}
				if (gr.enabled.HasValue)
				{
					component.enabled = gr.enabled.Value;
				}
			}
		}

		private static void ApplyFitter(GameObject go, FitterRule fr)
		{
			ContentSizeFitter contentSizeFitter = go.GetComponent<ContentSizeFitter>();
			if (fr.remove == true)
			{
				if (!(contentSizeFitter != null))
				{
					return;
				}
				ContentSizeFitter ff = contentSizeFitter;
				Plugin.Defer(delegate
				{
					if (ff != null)
					{
						UnityEngine.Object.Destroy(ff);
					}
				});
				return;
			}
			if (contentSizeFitter == null)
			{
				contentSizeFitter = go.AddComponent<ContentSizeFitter>();
			}
			if (!string.IsNullOrEmpty(fr.horizontal) && TryEnum<ContentSizeFitter.FitMode>(fr.horizontal, out var value))
			{
				contentSizeFitter.horizontalFit = value;
			}
			if (!string.IsNullOrEmpty(fr.vertical) && TryEnum<ContentSizeFitter.FitMode>(fr.vertical, out var value2))
			{
				contentSizeFitter.verticalFit = value2;
			}
			if (fr.enabled.HasValue)
			{
				contentSizeFitter.enabled = fr.enabled.Value;
			}
		}

		private static void ApplyLayoutElement(GameObject go, LayoutElementRule le)
		{
			LayoutElement layoutElement = go.GetComponent<LayoutElement>();
			if (layoutElement == null)
			{
				if (le.add != true)
				{
					return;
				}
				layoutElement = go.AddComponent<LayoutElement>();
			}
			if (le.ignoreLayout.HasValue)
			{
				layoutElement.ignoreLayout = le.ignoreLayout.Value;
			}
			if (le.minWidth.HasValue)
			{
				layoutElement.minWidth = le.minWidth.Value;
			}
			if (le.minHeight.HasValue)
			{
				layoutElement.minHeight = le.minHeight.Value;
			}
			if (le.preferredWidth.HasValue)
			{
				layoutElement.preferredWidth = le.preferredWidth.Value;
			}
			if (le.preferredHeight.HasValue)
			{
				layoutElement.preferredHeight = le.preferredHeight.Value;
			}
			if (le.flexibleWidth.HasValue)
			{
				layoutElement.flexibleWidth = le.flexibleWidth.Value;
			}
			if (le.flexibleHeight.HasValue)
			{
				layoutElement.flexibleHeight = le.flexibleHeight.Value;
			}
			if (le.enabled.HasValue)
			{
				layoutElement.enabled = le.enabled.Value;
			}
		}

		private static void ApplyImage(Image img, UIFixMarker m, ImageRule ir)
		{
			if (!string.IsNullOrEmpty(ir.sprite))
			{
				m.TouchedSprite = true;
				if (ir.sprite.Equals("none", StringComparison.OrdinalIgnoreCase))
				{
					img.sprite = null;
				}
				else if (ir.sprite.Equals("default", StringComparison.OrdinalIgnoreCase))
				{
					img.sprite = m.OSprite;
				}
				else
				{
					Sprite file = SpriteStore.GetFile(ir.sprite, img.sprite);
					if (file != null)
					{
						img.sprite = file;
					}
					else
					{
						Plugin.Log.LogWarning("Sprite file not found: " + ir.sprite);
					}
				}
			}
			if (!string.IsNullOrEmpty(ir.color) && TryColor(ir.color, out var c))
			{
				img.color = c;
				m.TouchedImageColor = true;
			}
			if (ir.preserveAspect.HasValue)
			{
				img.preserveAspect = ir.preserveAspect.Value;
			}
			if (!string.IsNullOrEmpty(ir.type) && TryEnum<Image.Type>(ir.type, out var value))
			{
				img.type = value;
			}
			if (ir.fillCenter.HasValue)
			{
				img.fillCenter = ir.fillCenter.Value;
			}
			if (ir.raycastTarget.HasValue)
			{
				img.raycastTarget = ir.raycastTarget.Value;
			}
			if (ir.nativeSize == true)
			{
				img.SetNativeSize();
			}
			if (ir.enabled.HasValue)
			{
				img.enabled = ir.enabled.Value;
				m.TouchedImageEnabled = true;
			}
		}

		public static Font GetOsFont(string name)
		{
			if (_osFonts.TryGetValue(name, out var value) && value != null)
			{
				return value;
			}
			try
			{
				value = Font.CreateDynamicFontFromOSFont(name, 24);
				if (value != null)
				{
					value.hideFlags = HideFlags.HideAndDontSave;
					_osFonts[name] = value;
				}
			}
			catch (Exception ex)
			{
				ManualLogSource log = Plugin.Log;
				bool isEnabled;
				BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(19, 2, out isEnabled);
				if (isEnabled)
				{
					bepInExWarningLogInterpolatedStringHandler.AppendLiteral("OS font '");
					bepInExWarningLogInterpolatedStringHandler.AppendFormatted(name);
					bepInExWarningLogInterpolatedStringHandler.AppendLiteral("' failed: ");
					bepInExWarningLogInterpolatedStringHandler.AppendFormatted(ex.Message);
				}
				log.LogWarning(bepInExWarningLogInterpolatedStringHandler);
			}
			return value;
		}

		public static Vector2 V2(float[] a)
		{
			return new Vector2(a[0], (a.Length > 1) ? a[1] : a[0]);
		}

		public static bool TryEnum<T>(string s, out T value) where T : struct
		{
			try
			{
				value = (T)Enum.Parse(typeof(T), s.Trim(), ignoreCase: true);
				return true;
			}
			catch
			{
				value = default(T);
				ManualLogSource log = Plugin.Log;
				bool isEnabled;
				BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(17, 2, out isEnabled);
				if (isEnabled)
				{
					bepInExWarningLogInterpolatedStringHandler.AppendLiteral("Unknown ");
					bepInExWarningLogInterpolatedStringHandler.AppendFormatted(typeof(T).Name);
					bepInExWarningLogInterpolatedStringHandler.AppendLiteral(" value '");
					bepInExWarningLogInterpolatedStringHandler.AppendFormatted(s);
					bepInExWarningLogInterpolatedStringHandler.AppendLiteral("'");
				}
				log.LogWarning(bepInExWarningLogInterpolatedStringHandler);
				return false;
			}
		}

		public static bool TryColor(string s, out Color c)
		{
			if (ColorUtility.TryParseHtmlString(s, out c))
			{
				return true;
			}
			string[] array = s.Split(',');
			if (array.Length >= 3)
			{
				try
				{
					c = new Color(float.Parse(array[0], CultureInfo.InvariantCulture), float.Parse(array[1], CultureInfo.InvariantCulture), float.Parse(array[2], CultureInfo.InvariantCulture), (array.Length > 3) ? float.Parse(array[3], CultureInfo.InvariantCulture) : 1f);
					return true;
				}
				catch
				{
				}
			}
			return false;
		}
	}
	public class RectRule
	{
		public float[] anchorMin;

		public float[] anchorMax;

		public float[] anchoredPos;

		public float[] sizeDelta;

		public float[] pivot;

		public float[] offsetMin;

		public float[] offsetMax;

		public float[] scale;

		public float? rotationZ;

		public float? x;

		public float? y;

		public float? width;

		public float? height;

		public float? dx;

		public float? dy;

		public float? dw;

		public float? dh;
	}
	public class TextRule
	{
		public float? fontSize;

		public float? fontScale;

		public bool? bestFit;

		public int? minSize;

		public int? maxSize;

		public string alignment;

		public string hOverflow;

		public string vOverflow;

		public float? lineSpacing;

		public bool? richText;

		public string fontStyle;

		public string font;

		public string color;

		public bool? raycastTarget;

		public string text;

		public bool? outline;
	}
	public class TmpRule
	{
		public float? fontSize;

		public float? fontScale;

		public bool? autoSize;

		public float? minSize;

		public float? maxSize;

		public string alignment;

		public string overflow;

		public bool? wordWrap;

		public float[] margin;

		public float? charSpacing;

		public float? wordSpacing;

		public float? lineSpacing;

		public float? paragraphSpacing;

		public string color;

		public bool? richText;

		public bool? raycastTarget;

		public string font;

		public string style;
	}
	public class LayoutRule
	{
		public string type;

		public float? spacing;

		public float[] padding;

		public string childAlignment;

		public bool? childControlWidth;

		public bool? childControlHeight;

		public bool? childForceExpandWidth;

		public bool? childForceExpandHeight;

		public bool? childScaleWidth;

		public bool? childScaleHeight;

		public bool? reverseArrangement;

		public bool? enabled;
	}
	public class GridRule
	{
		public float[] cellSize;

		public float[] spacing;

		public string startCorner;

		public string startAxis;

		public string childAlignment;

		public string constraint;

		public int? constraintCount;

		public float[] padding;

		public bool? enabled;
	}
	public class FitterRule
	{
		public string horizontal;

		public string vertical;

		public bool? enabled;

		public bool? remove;
	}
	public class LayoutElementRule
	{
		public bool? ignoreLayout;

		public float? minWidth;

		public float? minHeight;

		public float? preferredWidth;

		public float? preferredHeight;

		public float? flexibleWidth;

		public float? flexibleHeight;

		public bool? add;

		public bool? enabled;
	}
	public class MovePanelRule
	{
		public float[] standbyOffset;
	}
	public class ImageRule
	{
		public string sprite;

		public string color;

		public bool? preserveAspect;

		public bool? nativeSize;

		public bool? enabled;

		public string type;

		public bool? raycastTarget;

		public bool? fillCenter;
	}
	public class Rule
	{
		public string id;

		public string path;

		public string[] paths;

		public string comment;

		public bool always;

		public bool onTextChange;

		/// <summary>Apply only while the game runs the language this mod's English replaces (EnglishLanguage.Active).</summary>
		public bool englishOnly;

		public bool? active;

		public bool? disabled;

		public RectRule rect;

		public TextRule text;

		public TmpRule tmp;

		public LayoutRule layout;

		public GridRule grid;

		public FitterRule fitter;

		public LayoutElementRule layoutElement;

		public ImageRule image;

		public MovePanelRule movePanel;

		public bool? rebuildLayout;

		public bool? destroyChildrenLayout;

		[JsonIgnore]
		public Regex[] Regexes;

		[JsonIgnore]
		public string[] Literals;

		[JsonIgnore]
		public string Source;

		[JsonIgnore]
		public int Index;

		public bool Matches(string path)
		{
			if (disabled == true)
			{
				return false;
			}
			Regex[] regexes = Regexes;
			if (regexes == null)
			{
				return false;
			}
			string[] literals = Literals;
			for (int i = 0; i < regexes.Length; i++)
			{
				if (literals != null && i < literals.Length && literals[i] != null && path.IndexOf(literals[i], StringComparison.Ordinal) < 0)
				{
					continue;
				}
				Rules.RegexTests++;
				if (regexes[i].IsMatch(path))
				{
					return true;
				}
			}
			return false;
		}

		public override string ToString()
		{
			string obj = id;
			if (obj == null)
			{
				obj = path;
				if (obj == null)
				{
					if (paths == null)
					{
						return "?";
					}
					obj = string.Join("|", paths);
				}
			}
			return obj;
		}
	}
	public static class Rules
	{
		private static List<Rule> _rules = new List<Rule>();

		/// <summary>Rules that applied to something this session: after a game update, the ones that never do point at changed screens.</summary>
		private static readonly HashSet<int> _matched = new HashSet<int>();

		public static void MarkMatched(Rule r)
		{
			_matched.Add(r.Index);
		}

		public static int MatchedCount => _matched.Count;

		public static IEnumerable<string> Unmatched()
		{
			return _rules.Where((Rule r) => !_matched.Contains(r.Index)).Select((Rule r) => r.Source + ":" + (r.id ?? ("#" + r.Index)));
		}

		private static Dictionary<string, List<Rule>> _byTail = new Dictionary<string, List<Rule>>(StringComparer.Ordinal);

		private static List<Rule> _wildcardTail = new List<Rule>();

		private static readonly Dictionary<string, List<Rule>> _matchCache = new Dictionary<string, List<Rule>>(StringComparer.Ordinal);

		public static long MatchCalls;

		public static long RegexTests;

		public static long CacheHits;

		public static int CacheSize => _matchCache.Count;

		/// <summary>Longest wildcard-free run of a pattern (with the leading '/'), used to skip the regex for paths that cannot match.</summary>
		public static string LongestLiteral(string pattern)
		{
			pattern = pattern.Trim();
			if (pattern.StartsWith("**"))
			{
				pattern = pattern.Substring(2);
			}
			string best = null;
			string[] segments = pattern.Split('*');
			for (int i = 0; i < segments.Length; i++)
			{
				if (segments[i].Length >= 3 && (best == null || segments[i].Length > best.Length))
				{
					best = segments[i];
				}
			}
			return best;
		}

		public static IReadOnlyList<Rule> All => _rules;

		public static int Count => _rules.Count;

		public static int Version { get; private set; }

		private static string TailOf(string pattern)
		{
			string text = pattern.Trim().TrimEnd('/');
			int num = text.LastIndexOf('/');
			string text2 = ((num >= 0) ? text.Substring(num + 1) : text);
			if (text2.IndexOf('*') < 0)
			{
				return text2;
			}
			return null;
		}

		private static void BuildIndex(List<Rule> list)
		{
			Dictionary<string, List<Rule>> dictionary = new Dictionary<string, List<Rule>>(StringComparer.Ordinal);
			List<Rule> list2 = new List<Rule>();
			foreach (Rule item in list)
			{
				List<string> list3 = new List<string>();
				if (!string.IsNullOrEmpty(item.path))
				{
					list3.Add(item.path);
				}
				if (item.paths != null)
				{
					string[] paths = item.paths;
					foreach (string text in paths)
					{
						if (!string.IsNullOrEmpty(text))
						{
							list3.Add(text);
						}
					}
				}
				bool flag = false;
				HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
				foreach (string item2 in list3)
				{
					string text2 = TailOf(item2);
					if (text2 == null)
					{
						flag = true;
					}
					else
					{
						hashSet.Add(text2);
					}
				}
				if (flag)
				{
					list2.Add(item);
				}
				foreach (string item3 in hashSet)
				{
					if (!dictionary.TryGetValue(item3, out var value))
					{
						value = (dictionary[item3] = new List<Rule>());
					}
					value.Add(item);
				}
			}
			_byTail = dictionary;
			_wildcardTail = list2;
		}

		public static Regex PatternToRegex(string pattern)
		{
			pattern = pattern.Trim();
			bool flag = pattern.StartsWith("/");
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append(flag ? "^" : "^(?:.*/)?");
			int num = 0;
			while (num < pattern.Length)
			{
				char c = pattern[num];
				if (c == '*')
				{
					if (num + 1 < pattern.Length && pattern[num + 1] == '*')
					{
						stringBuilder.Append(".*");
						num += 2;
					}
					else
					{
						stringBuilder.Append("[^/]*");
						num++;
					}
				}
				else
				{
					stringBuilder.Append(Regex.Escape(c.ToString()));
					num++;
				}
			}
			stringBuilder.Append("$");
			return new Regex(stringBuilder.ToString(), RegexOptions.Compiled | RegexOptions.CultureInvariant);
		}

		/// <summary>At the last load: rule files found, rules and files that could not be read, and the first reason.</summary>
		public static int Files;

		public static int Unreadable;

		public static string FirstUnreadable;

		private static void NoteUnreadable(string file, string why)
		{
			Unreadable++;
			FirstUnreadable = FirstUnreadable ?? (file + ": " + why);
		}

		public static void Load(string dir)
		{
			List<Rule> list = new List<Rule>();
			Files = 0;
			Unreadable = 0;
			FirstUnreadable = null;
			if (Directory.Exists(dir))
			{
				string[] array = Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories).OrderBy((string f) => f, StringComparer.OrdinalIgnoreCase).ToArray();
				Files = array.Length;
				foreach (string text in array)
				{
					bool isEnabled;
					try
					{
						JToken jToken = JToken.Parse(File.ReadAllText(text));
						JArray jArray = jToken as JArray;
						if (jArray == null && jToken is JObject jObject && jObject["rules"] is JArray jArray2)
						{
							jArray = jArray2;
						}
						if (jArray == null)
						{
							Plugin.Log.LogWarning("Rule file is not an array: " + text);
							continue;
						}
						int num2 = 0;
						foreach (JToken item in jArray)
						{
							Rule rule;
							try
							{
								rule = item.ToObject<Rule>();
							}
							catch (Exception exRule)
							{
								// One malformed rule (a wrong type after an edit) is skipped; the rest of its file still loads.
								Plugin.Log.LogWarning("Rule skipped in " + Path.GetFileName(text) + ": " + exRule.Message);
								NoteUnreadable(Path.GetFileName(text), exRule.Message);
								continue;
							}
							if (rule == null)
							{
								continue;
							}
							List<string> list2 = new List<string>();
							if (!string.IsNullOrEmpty(rule.path))
							{
								list2.Add(rule.path);
							}
							if (rule.paths != null)
							{
								list2.AddRange(rule.paths.Where((string p) => !string.IsNullOrEmpty(p)));
							}
							if (list2.Count == 0)
							{
								ManualLogSource log = Plugin.Log;
								BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(23, 2, out isEnabled);
								if (isEnabled)
								{
									bepInExWarningLogInterpolatedStringHandler.AppendLiteral("Rule without path in ");
									bepInExWarningLogInterpolatedStringHandler.AppendFormatted(Path.GetFileName(text));
									bepInExWarningLogInterpolatedStringHandler.AppendLiteral(": ");
									bepInExWarningLogInterpolatedStringHandler.AppendFormatted(rule.comment);
								}
								log.LogWarning(bepInExWarningLogInterpolatedStringHandler);
							}
							else
							{
								rule.Regexes = list2.Select(PatternToRegex).ToArray();
								rule.Literals = list2.Select(LongestLiteral).ToArray();
								rule.Source = Path.GetFileName(text);
								rule.Index = list.Count;
								list.Add(rule);
								num2++;
							}
						}
						ManualLogSource log2 = Plugin.Log;
						BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(19, 2, out isEnabled);
						if (isEnabled)
						{
							bepInExInfoLogInterpolatedStringHandler.AppendLiteral("Loaded ");
							bepInExInfoLogInterpolatedStringHandler.AppendFormatted(num2);
							bepInExInfoLogInterpolatedStringHandler.AppendLiteral(" rules from ");
							bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Path.GetFileName(text));
						}
						log2.LogInfo(bepInExInfoLogInterpolatedStringHandler);
					}
					catch (Exception ex)
					{
						ManualLogSource log3 = Plugin.Log;
						BepInExErrorLogInterpolatedStringHandler bepInExErrorLogInterpolatedStringHandler = new BepInExErrorLogInterpolatedStringHandler(28, 2, out isEnabled);
						if (isEnabled)
						{
							bepInExErrorLogInterpolatedStringHandler.AppendLiteral("Failed to parse rule file ");
							bepInExErrorLogInterpolatedStringHandler.AppendFormatted(text);
							bepInExErrorLogInterpolatedStringHandler.AppendLiteral(": ");
							bepInExErrorLogInterpolatedStringHandler.AppendFormatted(ex.Message);
						}
						log3.LogError(bepInExErrorLogInterpolatedStringHandler);
						NoteUnreadable(Path.GetFileName(text), ex.Message);
					}
				}
			}
			else
			{
				Plugin.Log.LogWarning("Rules directory missing: " + dir);
			}
			_rules = list;
			_matched.Clear();
			BuildIndex(list);
			_matchCache.Clear();
			Version++;
		}

		public static List<Rule> Match(string path)
		{
			if (path == null)
			{
				return null;
			}
			MatchCalls++;
			if (_matchCache.TryGetValue(path, out var cached))
			{
				CacheHits++;
				return cached;
			}
			int num = path.LastIndexOf('/');
			string key = ((num >= 0) ? path.Substring(num + 1) : path);
			List<Rule> list = null;
			if (_byTail.TryGetValue(key, out var value))
			{
				for (int i = 0; i < value.Count; i++)
				{
					if (value[i].Matches(path))
					{
						if (list == null)
						{
							list = new List<Rule>(2);
						}
						list.Add(value[i]);
					}
				}
			}
			for (int j = 0; j < _wildcardTail.Count; j++)
			{
				Rule rule = _wildcardTail[j];
				if (rule.Matches(path))
				{
					if (list == null)
					{
						list = new List<Rule>(2);
					}
					if (!list.Contains(rule))
					{
						list.Add(rule);
					}
				}
			}
			if (list != null && list.Count > 1)
			{
				list.Sort((Rule a, Rule b) => a.Index.CompareTo(b.Index));
			}
			if (_matchCache.Count < 250000)
			{
				_matchCache[path] = list;
			}
			return list;
		}
	}
	public static class SpriteStore
	{
		private class SidecarMeta
		{
			public float[] border;

			public float[] pivot;

			public float? pixelsPerUnit;

			public bool keepOriginalGeometry = true;
		}

		private static string _dir;

		private static Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.Ordinal);

		private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

		private static readonly HashSet<int> _ourSprites = new HashSet<int>();

		public static int Replacements;

		public static int Count => _map.Count;

		public static void Load(string dir)
		{
			_dir = dir;
			foreach (Sprite value in _cache.Values)
			{
				if (value != null)
				{
					if (value.texture != null)
					{
						UnityEngine.Object.Destroy(value.texture);
					}
					UnityEngine.Object.Destroy(value);
				}
			}
			_cache.Clear();
			_ourSprites.Clear();
			Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
			string path = Path.Combine(dir, "spritemap.json");
			MapError = null;
			if (File.Exists(path))
			{
				try
				{
					Dictionary<string, string> dictionary2 = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
					if (dictionary2 != null)
					{
						foreach (KeyValuePair<string, string> item in dictionary2)
						{
							if (!string.IsNullOrEmpty(item.Key) && !string.IsNullOrEmpty(item.Value))
							{
								dictionary[item.Key] = item.Value;
							}
						}
					}
				}
				catch (Exception ex)
				{
					MapError = ex.Message;
					Plugin.Log.LogError("spritemap.json parse failed: " + ex.Message);
				}
			}
			_map = dictionary;
		}

		/// <summary>Why spritemap.json could not be read at the last load (null when it was read or is absent).</summary>
		public static string MapError;

		public static bool IsOurs(Sprite s)
		{
			if (s != null)
			{
				return _ourSprites.Contains(s.GetInstanceID());
			}
			return false;
		}

		public static void TryReplace(Image img)
		{
			if (!Plugin.CfgEnabled.Value || !Plugin.CfgSpriteReplace.Value || img == null)
			{
				return;
			}
			Sprite sprite = img.sprite;
			if (sprite == null || _map.Count == 0 || _ourSprites.Contains(sprite.GetInstanceID()) || !_map.TryGetValue(sprite.name, out var value))
			{
				return;
			}
			Sprite file = GetFile(value, sprite);
			if (file == null)
			{
				return;
			}
			UIFixMarker uIFixMarker = img.GetComponent<UIFixMarker>();
			if (uIFixMarker == null)
			{
				uIFixMarker = img.gameObject.AddComponent<UIFixMarker>();
				uIFixMarker.hideFlags = HideFlags.HideAndDontSave;
			}
			uIFixMarker.CaptureImage(img);
			uIFixMarker.TouchedSprite = true;
			img.sprite = file;
			Replacements++;
			if (Plugin.CfgVerbose.Value)
			{
				ManualLogSource log = Plugin.Log;
				bool isEnabled;
				BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(15, 3, out isEnabled);
				if (isEnabled)
				{
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("Sprite ");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(sprite.name);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(" -> ");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(value);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(" on ");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(PathUtil.GetPath(img.gameObject));
				}
				log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
			}
		}

		public static Sprite GetFile(string file, Sprite original)
		{
			if (string.IsNullOrEmpty(_dir))
			{
				return null;
			}
			if (!file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
			{
				file += ".png";
			}
			string key = file + "|" + ((original != null) ? original.name : "");
			if (_cache.TryGetValue(key, out var value) && value != null)
			{
				return value;
			}
			string text = Path.Combine(_dir, file);
			if (!File.Exists(text))
			{
				return null;
			}
			try
			{
				byte[] data = File.ReadAllBytes(text);
				Texture2D texture2D = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
				if (!texture2D.LoadImage(data))
				{
					UnityEngine.Object.Destroy(texture2D);
					return null;
				}
				texture2D.name = Path.GetFileNameWithoutExtension(file);
				texture2D.filterMode = FilterMode.Bilinear;
				texture2D.wrapMode = TextureWrapMode.Clamp;
				texture2D.hideFlags = HideFlags.HideAndDontSave;
				SidecarMeta sidecarMeta = null;
				string text2 = text + ".json";
				if (File.Exists(text2))
				{
					try
					{
						sidecarMeta = JsonConvert.DeserializeObject<SidecarMeta>(File.ReadAllText(text2));
					}
					catch (Exception ex)
					{
						Plugin.Log.LogWarning("sidecar parse failed " + text2 + ": " + ex.Message);
					}
				}
				Vector2 vector = new Vector2(0.5f, 0.5f);
				float pixelsPerUnit = 100f;
				Vector4 border = Vector4.zero;
				if (original != null && (sidecarMeta == null || sidecarMeta.keepOriginalGeometry))
				{
					Rect rect = original.rect;
					vector = ((rect.width > 0f && rect.height > 0f) ? new Vector2(original.pivot.x / rect.width, original.pivot.y / rect.height) : vector);
					pixelsPerUnit = original.pixelsPerUnit;
					Vector4 border2 = original.border;
					float num = ((rect.width > 0f) ? ((float)texture2D.width / rect.width) : 1f);
					float num2 = ((rect.height > 0f) ? ((float)texture2D.height / rect.height) : 1f);
					border = new Vector4(border2.x * num, border2.y * num2, border2.z * num, border2.w * num2);
				}
				if (sidecarMeta != null)
				{
					if (sidecarMeta.pivot != null && sidecarMeta.pivot.Length == 2)
					{
						vector = new Vector2(sidecarMeta.pivot[0], sidecarMeta.pivot[1]);
					}
					if (sidecarMeta.pixelsPerUnit.HasValue)
					{
						pixelsPerUnit = sidecarMeta.pixelsPerUnit.Value;
					}
					if (sidecarMeta.border != null && sidecarMeta.border.Length == 4)
					{
						border = new Vector4(sidecarMeta.border[0], sidecarMeta.border[1], sidecarMeta.border[2], sidecarMeta.border[3]);
					}
				}
				Sprite sprite = Sprite.Create(texture2D, new Rect(0f, 0f, texture2D.width, texture2D.height), vector, pixelsPerUnit, 0u, SpriteMeshType.FullRect, border);
				sprite.name = ((original != null) ? original.name : texture2D.name);
				sprite.hideFlags = HideFlags.HideAndDontSave;
				_cache[key] = sprite;
				_ourSprites.Add(sprite.GetInstanceID());
				return sprite;
			}
			catch (Exception ex2)
			{
				Plugin.Log.LogError("Sprite load failed " + text + ": " + ex2.Message);
				return null;
			}
		}
	}
	[BepInPlugin("lom.strings.english", "LOM_Strings_EN", "0.1.0")]
	// After LOM_UI_EN, whose Awake binds the compatibility settings this plugin's hooks report to.
	[BepInDependency("lom.ui.english", BepInDependency.DependencyFlags.SoftDependency)]
	public class StringsPlugin : BaseUnityPlugin
	{
		public const string GUID = "lom.strings.english";

		public const string NAME = "LOM_Strings_EN";

		public const string VERSION = "0.1.0";

		public static ConfigEntry<bool> CfgEnabled;

		public static ConfigEntry<bool> CfgNormalize;

		public static ConfigEntry<bool> CfgNormalizeStory;

		public static ConfigEntry<bool> CfgLog;

		public static ConfigEntry<bool> CfgTrace;

		public static ConfigEntry<KeyCode> CfgReloadKey;

		public static StringsPlugin Instance { get; private set; }

		public static ManualLogSource SLog { get; private set; }

		private void Awake()
		{
			Instance = this;
			SLog = base.Logger;
			CfgEnabled = base.Config.Bind("General", "Enabled", defaultValue: true, "Apply the UI string overrides in strings/*.csv (independent of the LOM_UI_EN layout fixes).");
			CfgNormalize = base.Config.Bind("General", "NormalizePunctuation", defaultValue: true, "Tidy punctuation and spacing of localized UI strings: full-width punctuation to ASCII, a space after commas/colons/sentence ends, no doubled spaces. Never touches rich-text tags or {0} placeholders.");
			CfgNormalizeStory = base.Config.Bind("General", "NormalizeStoryText", defaultValue: false, "Also normalize story/dialogue text (keys starting with Story/). Off by default: story prose is left exactly as the translation writes it.");
			CfgLog = base.Config.Bind("Dev", "LogOverrides", defaultValue: false, "Log every applied override.");
			CfgTrace = base.Config.Bind("Dev", "TraceKeys", defaultValue: false, "Append every looked-up key and its final text to BepInEx/cache/LOM_UI_EN/harness/out/strings_trace.txt (for finding which key a screen uses).");
			CfgReloadKey = base.Config.Bind("Dev", "ReloadKey", KeyCode.F12, "Hotkey: reload strings/*.csv.");
			try
			{
				StringOverrides.Load();
			}
			catch (Exception ex0)
			{
				SLog.LogError("could not read strings/*.csv: " + ex0);
			}
			try
			{
				Harmony harmony = new Harmony("lom.strings.english");
				// Each hook in its own block (a lambda is its own method): a game type that no longer loads costs only its hook.
				Compat.Setup(F.Wording, delegate
				{
					Compat.Part(F.Wording, "LeanLocalizationResolver.GetString(string)", delegate
					{
						Compat.Hook(F.Wording, harmony, AccessTools.Method(typeof(LeanLocalizationResolver), "GetString", new Type[1] { typeof(string) }), "LeanLocalizationResolver.GetString(string)", null, new HarmonyMethod(typeof(StringOverrides), "GetString_Postfix")
						{
							priority = 0
						});
					});
					Compat.Part(F.Wording, "LeanLocalizedText.UpdateTranslation", delegate
					{
						Compat.Hook(F.Wording, harmony, AccessTools.Method(typeof(LeanLocalizedText), "UpdateTranslation"), "LeanLocalizedText.UpdateTranslation", null, new HarmonyMethod(typeof(StringOverrides), "LeanText_Postfix")
						{
							priority = 0
						});
					});
					Compat.Part(F.Wording, "LeanLocalizedTextMeshProUGUI.UpdateTranslation", delegate
					{
						Compat.Hook(F.Wording, harmony, AccessTools.Method(typeof(LeanLocalizedTextMeshProUGUI), "UpdateTranslation"), "LeanLocalizedTextMeshProUGUI.UpdateTranslation", null, new HarmonyMethod(typeof(StringOverrides), "LeanTmp_Postfix")
						{
							priority = 0
						});
					});
				});
				ManualLogSource sLog = SLog;
				bool isEnabled;
				BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(31, 3, out isEnabled);
				if (isEnabled)
				{
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted("LOM_Strings_EN");
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(" loaded: ");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(StringOverrides.Count);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(" overrides, normalize=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(CfgNormalize.Value ? "on" : "off");
				}
				sLog.LogInfo(bepInExInfoLogInterpolatedStringHandler);
			}
			catch (Exception ex)
			{
				SLog.LogError("Harmony patch failed: " + ex);
			}
		}

		private void Update()
		{
			if (!ModMenu.IsOpen && Input.GetKeyDown(CfgReloadKey.Value))
			{
				StringOverrides.Load();
				StringOverrides.Refresh();
				ManualLogSource sLog = SLog;
				bool isEnabled;
				BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(26, 1, out isEnabled);
				if (isEnabled)
				{
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("Reloaded ");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(StringOverrides.Count);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(" string overrides");
				}
				sLog.LogInfo(bepInExInfoLogInterpolatedStringHandler);
			}
		}
	}
	public static class StringOverrides
	{
		private static Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.Ordinal);

		private static Dictionary<string, string> _normalized = new Dictionary<string, string>(StringComparer.Ordinal);

		private const int NormCacheCap = 8192;

		private static Dictionary<string, string> _normCache = new Dictionary<string, string>(StringComparer.Ordinal);

		private static readonly HashSet<string> _traced = new HashSet<string>();

		public static int Applied;

		private static readonly Regex DatePattern = new Regex("^\\s*(January|February|March|April|May|June|July|August|September|October|November|December)\\s*[‧·・•]\\s*(Early|Mid|Late)(\\s+Month)?\\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

		private static readonly Regex ScaffoldingPrefix = new Regex("^\\s*(?:Instructions?|Translations?|Outputs?|System|Prompt|Assistant)\\s*:[^\\n]{0,120}\\r?\\n+(?=\\S)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

		private static readonly Regex TagSplit = new Regex("(<[^<>]+>|\\{[^{}]+\\})", RegexOptions.Compiled);

		private static readonly Regex MultiSpace = new Regex("[ \\t]{2,}", RegexOptions.Compiled);

		private static readonly Regex SpaceBeforePunct = new Regex(" +([,.;:!?%)])", RegexOptions.Compiled);

		private static readonly Regex SpaceAfterOpen = new Regex("\\( +", RegexOptions.Compiled);

		private static readonly Regex MissingSpaceAfter = new Regex("([,;:!?])(?=[A-Za-z(\\[\"'])", RegexOptions.Compiled);

		private static readonly Regex MissingSpaceAfterPeriod = new Regex("(?<=[a-z\\)])\\.(?=[A-Z])", RegexOptions.Compiled);

		private static readonly Regex SpaceAroundNewline = new Regex(" *\\n *", RegexOptions.Compiled);

		private static readonly (string from, string to)[] FullWidth = new(string, string)[25]
		{
			("，", ", "),
			("。", ". "),
			("！", "! "),
			("？", "? "),
			("：", ": "),
			("；", "; "),
			("、", ", "),
			("（", " ("),
			("）", ") "),
			("「", "\""),
			("」", "\""),
			("『", "'"),
			("』", "'"),
			("【", "["),
			("】", "]"),
			("～", "~"),
			("－", "-"),
			("％", "%"),
			("\u3000", " "),
			("‧", "·"),
			("．", "."),
			("＋", "+"),
			("／", "/"),
			("‑", "-"),
			("‐", "-")
		};

		public static int Count => _map.Count;

		public static string Dir => Path.Combine(Plugin.PluginDir ?? Path.GetDirectoryName(typeof(StringOverrides).Assembly.Location), "strings");

		public static string NormalizeCached(string s)
		{
			if (string.IsNullOrEmpty(s))
			{
				return s;
			}
			if (_normCache.TryGetValue(s, out var value))
			{
				return value;
			}
			string text = Normalize(s);
			if (_normCache.Count >= 8192)
			{
				_normCache.Clear();
			}
			_normCache[s] = text;
			return text;
		}

		public static void Load()
		{
			Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
			string dir = Dir;
			if (Directory.Exists(dir))
			{
				string[] files = Directory.GetFiles(dir, "*.csv", SearchOption.TopDirectoryOnly);
				Array.Sort(files, StringComparer.OrdinalIgnoreCase);
				string[] array = files;
				foreach (string path in array)
				{
					int num = 0;
					foreach (List<string> item in ParseCsv(File.ReadAllText(path)))
					{
						if (item.Count >= 2)
						{
							string text = item[0].Trim();
							if (text.Length != 0 && !text.StartsWith("#") && !(text == "key"))
							{
								dictionary[text] = item[1].Replace("\\n", "\n");
								num++;
							}
						}
					}
					ManualLogSource sLog = StringsPlugin.SLog;
					if (sLog != null)
					{
						bool isEnabled;
						BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(30, 2, out isEnabled);
						if (isEnabled)
						{
							bepInExInfoLogInterpolatedStringHandler.AppendLiteral("Loaded ");
							bepInExInfoLogInterpolatedStringHandler.AppendFormatted(num);
							bepInExInfoLogInterpolatedStringHandler.AppendLiteral(" string overrides from ");
							bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Path.GetFileName(path));
						}
						sLog.LogInfo(bepInExInfoLogInterpolatedStringHandler);
					}
				}
			}
			_map = dictionary;
			Dictionary<string, string> dictionary2 = new Dictionary<string, string>(dictionary.Count, StringComparer.Ordinal);
			foreach (KeyValuePair<string, string> item2 in dictionary)
			{
				dictionary2[item2.Key] = Normalize(item2.Value);
			}
			_normalized = dictionary2;
			_normCache = new Dictionary<string, string>(StringComparer.Ordinal);
			// The fallback checked the old wording fixes against the game's text; check the new ones afresh.
			TextTable.ClearDecisions();
		}

		/// <summary>
		/// An override whose text is exactly this shows the game data layer's served text for its key on the key's Lean label
		/// (where the scene layer would otherwise show a different line for the same Chinese). The text then comes from the
		/// served table (the original patch's installed row or this mod's revision), so the wording file holds none of it.
		/// </summary>
		public const string TableMarker = "@table";

		private static bool TryFinal(string key, out string text)
		{
			if (!((StringsPlugin.CfgNormalize != null && StringsPlugin.CfgNormalize.Value) ? _normalized : _map).TryGetValue(key, out text))
			{
				return false;
			}
			if (text == TableMarker)
			{
				string served;
				if (!TextTable.TryGet(key, out served) || string.IsNullOrEmpty(served) || SceneDictionary.IsTranslatableChinese(served))
				{
					text = null;
					return false;
				}
				text = (StringsPlugin.CfgNormalize != null && StringsPlugin.CfgNormalize.Value && (StringsPlugin.CfgNormalizeStory.Value || !key.StartsWith("Story/", StringComparison.Ordinal))) ? NormalizeCached(served) : served;
				return true;
			}
			// A wording fix that no longer fits the game's text (a game update) is skipped, like a revised row (fallback mode).
			if (!TextTable.OverrideFits(key, text))
			{
				text = null;
				return false;
			}
			return true;
		}

		public static bool TryGet(string key, out string text)
		{
			return _map.TryGetValue(key, out text);
		}

		public static void Refresh()
		{
			try
			{
				LeanLocalization.UpdateTranslations();
			}
			catch (Exception ex)
			{
				StringsPlugin.SLog?.LogWarning("LeanLocalization refresh failed: " + ex.Message);
			}
		}

		public static void GetString_Postfix(string __0, ref string __result)
		{
			string key = __0;
			if (!F.Wording.Live)
			{
				return;
			}
			try
			{
				F.Wording.Probe();
				if (key == null)
				{
					return;
				}
				if (StringsPlugin.CfgEnabled != null && StringsPlugin.CfgEnabled.Value && EnglishLanguage.Active)
				{
					if (TryFinal(key, out var text))
					{
						__result = text;
						Applied++;
						if (StringsPlugin.CfgLog.Value)
						{
							ManualLogSource sLog = StringsPlugin.SLog;
							bool isEnabled;
							BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(13, 2, out isEnabled);
							if (isEnabled)
							{
								bepInExInfoLogInterpolatedStringHandler.AppendLiteral("override ");
								bepInExInfoLogInterpolatedStringHandler.AppendFormatted(key);
								bepInExInfoLogInterpolatedStringHandler.AppendLiteral(" -> ");
								bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Short(text));
							}
							sLog.LogInfo(bepInExInfoLogInterpolatedStringHandler);
						}
					}
					else if (StringsPlugin.CfgNormalize.Value && __result != null && (StringsPlugin.CfgNormalizeStory.Value || !key.StartsWith("Story/", StringComparison.Ordinal)))
					{
						__result = NormalizeCached(__result);
					}
				}
				if (!StringsPlugin.CfgTrace.Value || __result == null || !_traced.Add(key))
				{
					return;
				}
				try
				{
					string path = Path.Combine(BepInEx.Paths.CachePath, "LOM_UI_EN", "harness", "out", "strings_trace.txt");
					Directory.CreateDirectory(Path.GetDirectoryName(path));
					File.AppendAllText(path, key + "\t" + __result.Replace("\n", "\\n") + "\n", Encoding.UTF8);
				}
				catch
				{
				}
			}
			catch (Exception ex)
			{
				F.Wording.Fail(ex, "GetString");
			}
		}

		// The Lean label's key comes from a NoInlining helper: a renamed TranslationName fails there, inside the try.
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static string LeanTextName(Component c)
		{
			return ((LeanLocalizedText)c).TranslationName;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static string LeanTmpName(Component c)
		{
			return ((LeanLocalizedTextMeshProUGUI)c).TranslationName;
		}

		public static void LeanText_Postfix(Component __instance)
		{
			if (!F.Wording.Live)
			{
				return;
			}
			try
			{
				F.Wording.Probe();
				if (__instance == null || StringsPlugin.CfgEnabled == null)
				{
					return;
				}
				string translationName = LeanTextName(__instance);
				if (string.IsNullOrEmpty(translationName))
				{
					return;
				}
				Text component = __instance.GetComponent<Text>();
				if (component == null || !EnglishLanguage.Active)
				{
					return;
				}
				string text;
				string before = component.text;
				if (!(StringsPlugin.CfgEnabled.Value && TryFinal(translationName, out text)) && !TableFallback(translationName, component, before, out text))
				{
					return;
				}
				if (!(before != text))
				{
					return;
				}
				// The scene layer found no line for the Chinese Lean text and noted it as untranslated; it is covered here.
				SceneText.Unlog(before);
				component.text = text;
				Applied++;
				if (StringsPlugin.CfgLog.Value)
				{
					ManualLogSource sLog = StringsPlugin.SLog;
					bool isEnabled;
					BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(19, 2, out isEnabled);
					if (isEnabled)
					{
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral("override(lean) ");
						bepInExInfoLogInterpolatedStringHandler.AppendFormatted(translationName);
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral(" -> ");
						bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Short(text));
					}
					sLog.LogInfo(bepInExInfoLogInterpolatedStringHandler);
				}
			}
			catch (Exception ex)
			{
				F.Wording.Fail(ex, "LeanLocalizedText");
			}
		}

		public static void LeanTmp_Postfix(Component __instance)
		{
			if (!F.Wording.Live)
			{
				return;
			}
			try
			{
				F.Wording.Probe();
				if (__instance == null || StringsPlugin.CfgEnabled == null)
				{
					return;
				}
				string translationName = LeanTmpName(__instance);
				if (string.IsNullOrEmpty(translationName))
				{
					return;
				}
				TMP_Text component = __instance.GetComponent<TMP_Text>();
				if (component == null || !EnglishLanguage.Active)
				{
					return;
				}
				string text;
				string before = component.text;
				if (!(StringsPlugin.CfgEnabled.Value && TryFinal(translationName, out text)) && !TableFallback(translationName, component, before, out text))
				{
					return;
				}
				if (!(before != text))
				{
					return;
				}
				// The scene layer found no line for the Chinese Lean text and noted it as untranslated; it is covered here.
				SceneText.Unlog(before);
				component.text = text;
				Applied++;
				if (StringsPlugin.CfgLog.Value)
				{
					ManualLogSource sLog = StringsPlugin.SLog;
					bool isEnabled;
					BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(23, 2, out isEnabled);
					if (isEnabled)
					{
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral("override(lean-tmp) ");
						bepInExInfoLogInterpolatedStringHandler.AppendFormatted(translationName);
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral(" -> ");
						bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Short(text));
					}
					sLog.LogInfo(bepInExInfoLogInterpolatedStringHandler);
				}
			}
			catch (Exception ex)
			{
				F.Wording.Fail(ex, "LeanLocalizedTextMeshProUGUI");
			}
		}

		/// <summary>
		/// A Lean scene label reads the game's own (Chinese) Lean data, which the scene-text layer translates by its text. When it has
		/// no line for it, the label is still Chinese; the string table then usually has the label's key (Position/Name/Forge and
		/// others), so its value is shown instead, tidied like any other table value.
		/// </summary>
		private static bool TableFallback(string key, Component component, string current, out string text)
		{
			text = null;
			if (string.IsNullOrEmpty(current) || !SceneDictionary.IsTranslatableChinese(current) || SceneText.IsSkipped(component))
			{
				return false;
			}
			string value = null;
			bool found;
			if (TextTable.Serving)
			{
				found = TextTable.TryGet(key, out value);
			}
			else
			{
				// Game data text handed back to the base patch: its plugin's table (Binarizer's or its own) has the same keys.
				found = TranslationProfiles.TableMode == LayerMode.OriginalHandBack && OriginalMod.TryGetHandBackLine(key, out value);
			}
			if (!found || string.IsNullOrEmpty(value) || SceneDictionary.IsTranslatableChinese(value))
			{
				return false;
			}
			text = (StringsPlugin.CfgEnabled.Value && StringsPlugin.CfgNormalize.Value && (StringsPlugin.CfgNormalizeStory.Value || !key.StartsWith("Story/", StringComparison.Ordinal))) ? NormalizeCached(value) : value;
			return true;
		}

		private static string Short(string s)
		{
			if (s != null)
			{
				return ((s.Length > 60) ? (s.Substring(0, 60) + "…") : s).Replace("\n", "\\n");
			}
			return "";
		}

		public static string PostProcessDynamic(string s)
		{
			if (string.IsNullOrEmpty(s) || StringsPlugin.CfgEnabled == null || !StringsPlugin.CfgEnabled.Value || !StringsPlugin.CfgNormalize.Value || !EnglishLanguage.Active)
			{
				return s;
			}
			if (s.IndexOf(':') > 0 && s.IndexOf('\n') > 0)
			{
				string text = ScaffoldingPrefix.Replace(s, "");
				if ((object)text != s && text.Length != s.Length)
				{
					if (StringsPlugin.CfgLog.Value)
					{
						StringsPlugin.SLog.LogInfo("stripped translation scaffolding: " + Short(s));
					}
					return text;
				}
			}
			if (s.Length > 40)
			{
				return s;
			}
			Match match = DatePattern.Match(s);
			if (match.Success)
			{
				return match.Groups[2].Value + " " + match.Groups[1].Value;
			}
			return s;
		}

		public static string Normalize(string s)
		{
			if (string.IsNullOrEmpty(s))
			{
				return s;
			}
			bool flag = false;
			for (int i = 0; i < s.Length; i++)
			{
				if (flag)
				{
					break;
				}
				if (s[i] > '\u007f')
				{
					flag = true;
				}
			}
			string[] array = TagSplit.Split(s);
			StringBuilder stringBuilder = new StringBuilder(s.Length + 8);
			for (int j = 0; j < array.Length; j++)
			{
				string text = array[j];
				if (text.Length == 0)
				{
					continue;
				}
				if (j % 2 == 1)
				{
					stringBuilder.Append(text);
					continue;
				}
				if (flag)
				{
					(string, string)[] fullWidth = FullWidth;
					for (int k = 0; k < fullWidth.Length; k++)
					{
						var (text2, newValue) = fullWidth[k];
						if (text.IndexOf(text2, StringComparison.Ordinal) >= 0)
						{
							text = text.Replace(text2, newValue);
						}
					}
				}
				text = MissingSpaceAfter.Replace(text, "$1 ");
				text = MissingSpaceAfterPeriod.Replace(text, ". ");
				text = SpaceBeforePunct.Replace(text, "$1");
				text = SpaceAfterOpen.Replace(text, "(");
				stringBuilder.Append(text);
			}
			string input = MultiSpace.Replace(stringBuilder.ToString(), " ");
			input = SpaceAroundNewline.Replace(input, "\n");
			return input.Trim();
		}

		private static List<List<string>> ParseCsv(string text)
		{
			List<List<string>> list = new List<List<string>>();
			List<string> list2 = new List<string>();
			StringBuilder stringBuilder = new StringBuilder();
			bool flag = false;
			for (int i = 0; i < text.Length; i++)
			{
				char c = text[i];
				if (flag)
				{
					if (c == '"')
					{
						if (i + 1 < text.Length && text[i + 1] == '"')
						{
							stringBuilder.Append('"');
							i++;
						}
						else
						{
							flag = false;
						}
					}
					else
					{
						stringBuilder.Append(c);
					}
					continue;
				}
				switch (c)
				{
				case '"':
					flag = true;
					break;
				case ',':
					list2.Add(stringBuilder.ToString());
					stringBuilder.Length = 0;
					break;
				case '\n':
					list2.Add(stringBuilder.ToString());
					stringBuilder.Length = 0;
					if (list2.Count > 1 || list2[0].Length > 0)
					{
						list.Add(list2);
					}
					list2 = new List<string>();
					break;
				default:
					stringBuilder.Append(c);
					break;
				case '\r':
					break;
				}
			}
			if (stringBuilder.Length > 0 || list2.Count > 0)
			{
				list2.Add(stringBuilder.ToString());
				list.Add(list2);
			}
			if (text.Length > 0 && text[0] == '\ufeff' && list.Count > 0 && list[0].Count > 0)
			{
				list[0][0] = list[0][0].TrimStart('\ufeff');
			}
			return list;
		}
	}

	/// <summary>Performance patches for other layers of the English stack.</summary>
	public static class PerfPatches
	{
		private static Dictionary<string, string> _map;

		private static StackTraceLogType? _originalLogTrace;

		public static long Redirects;

		/// <summary>True once the Binarizer lookup prefix is installed; [Performance] QuietBinarizerStringLog then switches it live.</summary>
		public static bool Hooked { get; private set; }

		/// <summary>A key in the base patch's own Binarizer table (HookMods.mapString), when that is loaded.</summary>
		public static bool TryGetBase(string key, out string value)
		{
			value = null;
			return _map != null && key != null && _map.TryGetValue(key, out value);
		}

		public static void ApplyStackTraceSetting()
		{
			try
			{
				if (!_originalLogTrace.HasValue)
				{
					_originalLogTrace = Application.GetStackTraceLogType(LogType.Log);
				}
				Application.SetStackTraceLogType(LogType.Log, Plugin.CfgNoInfoStackTraces.Value ? StackTraceLogType.None : _originalLogTrace.Value);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("could not change the Info stack-trace setting: " + ex.Message);
			}
		}

		/// <summary>Game data keys neither this mod nor the original patch's table file had that the running Binarizer still supplied.</summary>
		public static long BaseFills;

		public static void Patch(Harmony h)
		{
			ApplyStackTraceSetting();
			Type type = TranslationProfiles.HookMods;
			if (type == null)
			{
				// Not loaded: not installed, or (on BepInEx 5) built for BepInEx 6.
				F.QuietBinarizer.NotNeededWhy = "the Binarizer of " + Plugin.BaseModName + " is not running";
				return;
			}
			FieldInfo field = AccessTools.Field(type, "mapString");
			if (!Compat.Require(F.QuietBinarizer, type, "mapString"))
			{
				return;
			}
			_map = field?.GetValue(null) as Dictionary<string, string>;
			if (_map == null)
			{
				F.QuietBinarizer.Problems.Add("HookMods.mapString is not a Dictionary<string,string> any more");
				F.QuietBinarizer.EssentialMissing = true;
				return;
			}
			Hooked = Compat.Hook(F.QuietBinarizer, h, AccessTools.Method(type, "GetStringRedirect"), "Mortal.HookMods.GetStringRedirect (Binarizer)", new HarmonyMethod(typeof(PerfPatches), "GetStringRedirect_Prefix")
			{
				priority = Priority.First
			}, essential: true);
			if (Hooked)
			{
				Plugin.Log.LogInfo("Binarizer string lookups: per-lookup Debug.Log " + (Plugin.CfgQuietBinarizer.Value ? "suppressed" : "kept (QuietBinarizerStringLog = false)"));
			}
		}

		// Replaces Mortal.HookMods.GetStringRedirect(ref LeanLocalizationResolver, ref string result, string key): nothing while this mod's
		// table serves; otherwise (Original handed back to Binarizer) the same lookup without the Debug.Log.
		private static bool GetStringRedirect_Prefix(ref string __1, string __2, ref bool __result)
		{
			if (!F.QuietBinarizer.Live)
			{
				return true;
			}
			try
			{
				F.QuietBinarizer.Probe();
				bool serving = TextTable.Serving;
				// The revised table already holds the original patch's own rows (read from its file), so Binarizer is asked only for a
				// key neither has (its table may come from another folder or release).
				if (serving && (__2 == null || TextTable.TryGet(__2, out _)))
				{
					// This mod's table answers (TextTable's GetString prefix): Binarizer's body is skipped, with no lookup, no log line
					// and no throw on a null key.
					__result = true;
					return false;
				}
				if (!serving && !Plugin.CfgQuietBinarizer.Value)
				{
					return true;
				}
				// Handed back to Binarizer, or a key the served table lacks: the same lookup, without the log line.
				string value;
				if (__2 != null && _map.TryGetValue(__2, out value))
				{
					__1 = value;
					__result = false;
					Redirects++;
					if (serving)
					{
						BaseFills++;
					}
					return false;
				}
				__result = true;
				return false;
			}
			catch (Exception ex)
			{
				F.QuietBinarizer.Fail(ex, "GetStringRedirect");
				return true;
			}
		}
	}

	/// <summary>Keeps the Fungus narrative log (story backlog) under Unity's 65,000-vertex Text limit and rebuilds it lazily.</summary>
	public static class NarrativeLogGuard
	{
		private static int _multiplier;

		private static bool _dirty;

		private static MethodInfo _update;

		public static int LastShown;

		public static void Patch(Harmony h)
		{
			// Both patches are installed regardless of the settings and check them on every call, so the Mod Settings window can switch them live.
			// Two parts, each in its own block: a game change to one Fungus type costs only its part.
			Compat.Setup(F.StoryLog, delegate
			{
				Compat.Require(F.StoryLog, "Fungus", "Fungus.NarrativeLog", "history", essential: false, part: "log guard");
				Compat.Require(F.StoryLog, "Fungus", "Fungus.NarrativeData", "entries", essential: false, part: "log guard");
				Compat.Require(F.StoryLog, "Fungus", "Fungus.NarrativeLogMenu", "narrativeLogMenuGroup", essential: false, part: "lazy rebuild");
				Compat.Part(F.StoryLog, "Fungus.NarrativeLog.GetPrettyHistory(bool, int)", delegate
				{
					Compat.Hook(F.StoryLog, h, AccessTools.Method(typeof(NarrativeLog), "GetPrettyHistory", new Type[2] { typeof(bool), typeof(int) }), "Fungus.NarrativeLog.GetPrettyHistory(bool, int)", new HarmonyMethod(typeof(NarrativeLogGuard), "GetPrettyHistory_Prefix"));
				}, part: "log guard");
				Compat.Part(F.StoryLog, "Fungus.NarrativeLogMenu hooks", delegate
				{
					PatchMenu(h);
				}, part: "lazy rebuild");
			});
		}

		private static void PatchMenu(Harmony h)
		{
			{
				_update = AccessTools.Method(typeof(NarrativeLogMenu), "UpdateNarrativeLogText");
				MethodInfo toggle = AccessTools.Method(typeof(NarrativeLogMenu), "ToggleNarrativeLogView");
				// A rebuild skipped while hidden must be done on open: the open hook goes in first, and the skip only if it did.
				if (_update != null && toggle != null && Compat.Hook(F.StoryLog, h, toggle, "Fungus.NarrativeLogMenu.ToggleNarrativeLogView", new HarmonyMethod(typeof(NarrativeLogGuard), "Toggle_Prefix")))
				{
					if (!Compat.Hook(F.StoryLog, h, _update, "Fungus.NarrativeLogMenu.UpdateNarrativeLogText", new HarmonyMethod(typeof(NarrativeLogGuard), "Update_Prefix")))
					{
						_update = null;
						Compat.MarkBroken(F.StoryLog, "UpdateNarrativeLogText could not be hooked", essential: false, part: "lazy rebuild");
					}
				}
				else
				{
					Compat.Hook(F.StoryLog, h, null, "Fungus.NarrativeLogMenu.UpdateNarrativeLogText / ToggleNarrativeLogView");
					_update = null;
					Compat.MarkBroken(F.StoryLog, "NarrativeLogMenu hooks not installed", essential: false, part: "lazy rebuild");
				}
			}
		}

		private static int VisibleLength(string s)
		{
			if (string.IsNullOrEmpty(s))
			{
				return 0;
			}
			int num = 0;
			bool inTag = false;
			for (int i = 0; i < s.Length; i++)
			{
				char c = s[i];
				if (inTag)
				{
					if (c == '>')
					{
						inTag = false;
					}
					continue;
				}
				if (c == '<')
				{
					inTag = true;
				}
				else if (c != '\n' && c != '\r')
				{
					num++;
				}
			}
			return num;
		}

		private static Text FindLogText(NarrativeLogMenu menu)
		{
			if (menu == null)
			{
				return null;
			}
			TextAdapter adapter = Traverse.Create(menu).Field("narLogViewtextAdapter").GetValue<TextAdapter>();
			Text text = ((adapter != null) ? Traverse.Create(adapter).Field("textUI").GetValue<Text>() : null);
			if (text == null)
			{
				ScrollRect view = Traverse.Create(menu).Field("narrativeLogView").GetValue<ScrollRect>();
				if (view != null)
				{
					text = view.GetComponentInChildren<Text>(true);
				}
			}
			return text;
		}

		private static int Multiplier()
		{
			if (_multiplier > 0)
			{
				return _multiplier;
			}
			try
			{
				NarrativeLogMenu menu = UnityEngine.Object.FindObjectOfType<NarrativeLogMenu>();
				if (menu == null)
				{
					NarrativeLogMenu[] all = Resources.FindObjectsOfTypeAll<NarrativeLogMenu>();
					if (all.Length > 0)
					{
						menu = all[0];
					}
				}
				Text text = FindLogText(menu);
				if (text == null)
				{
					return 1;
				}
				int mult = 1;
				BaseMeshEffect[] effects = text.GetComponents<BaseMeshEffect>();
				for (int i = 0; i < effects.Length; i++)
				{
					if (effects[i] is Outline)
					{
						mult += 4;
					}
					else if (effects[i] is Shadow)
					{
						mult++;
					}
				}
				_multiplier = mult;
				if (Plugin.CfgVerbose.Value)
				{
					Plugin.Log.LogInfo($"narrative log text: vertex multiplier {mult} ({effects.Length} mesh effects)");
				}
				return mult;
			}
			catch
			{
				return 1;
			}
		}

		private static void GetPrettyHistory_Prefix(Component __instance, ref int __0)
		{
			if (!F.StoryLog.PartOk("log guard") || !Plugin.CfgLogGuard.Value)
			{
				return;
			}
			try
			{
				F.StoryLog.Probe();
				GetPrettyHistoryBody(__instance, ref __0);
			}
			catch (Exception ex)
			{
				F.StoryLog.Fail(ex, "GetPrettyHistory");
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void GetPrettyHistoryBody(Component __instance, ref int maxEntry)
		{
			{
				NarrativeData data = Traverse.Create(__instance).Field("history").GetValue<NarrativeData>();
				List<NarrativeLogEntry> entries = ((data != null) ? Traverse.Create(data).Field("entries").GetValue<List<NarrativeLogEntry>>() : null);
				if (entries == null || entries.Count == 0)
				{
					return;
				}
				int budget = Math.Max(500, Plugin.CfgLogGlyphBudget.Value / Multiplier());
				int count = 0;
				int glyphs = 0;
				for (int i = entries.Count - 1; i >= 0 && count < maxEntry; i--)
				{
					NarrativeLogEntry entry = entries[i];
					int g = VisibleLength(entry.text) + VisibleLength(entry.name) + 2;
					if (glyphs + g > budget && count > 0)
					{
						break;
					}
					glyphs += g;
					count++;
				}
				LastShown = count;
				if (count < maxEntry)
				{
					if (Plugin.CfgVerbose.Value)
					{
						Plugin.Log.LogInfo($"narrative log trimmed to the last {count} of {entries.Count} entries ({glyphs} glyphs, budget {budget})");
					}
					maxEntry = Math.Max(1, count);
				}
			}
		}

		private static bool IsVisible(Component menu)
		{
			try
			{
				CanvasGroup group = Traverse.Create(menu).Field("narrativeLogMenuGroup").GetValue<CanvasGroup>();
				if (group != null)
				{
					return group.gameObject.activeInHierarchy && group.alpha > 0.001f;
				}
				ScrollRect view = Traverse.Create(menu).Field("narrativeLogView").GetValue<ScrollRect>();
				return view == null || view.gameObject.activeInHierarchy;
			}
			catch
			{
				return true;
			}
		}

		// While the backlog is hidden, skip the per-line rebuild and remember to do it on open.
		private static bool Update_Prefix(Component __instance)
		{
			if (!F.StoryLog.PartOk("lazy rebuild") || _update == null)
			{
				return true;
			}
			try
			{
				F.StoryLog.Probe();
				if (_dirtyGuard || !Plugin.CfgLazyLog.Value || IsVisible(__instance))
				{
					_dirty = false;
					return true;
				}
				_dirty = true;
				return false;
			}
			catch (Exception ex)
			{
				_dirty = false;
				F.StoryLog.Fail(ex, "UpdateNarrativeLogText");
				return true;
			}
		}

		private static void Toggle_Prefix(Component __instance)
		{
			// Runs even when the feature has turned off since, so a rebuild skipped while hidden is still done on open.
			if (!_dirty || _update == null)
			{
				return;
			}
			try
			{
				_dirty = false;
				bool prev = _dirtyGuard;
				_dirtyGuard = true;
				try
				{
					_update.Invoke(__instance, null);
				}
				finally
				{
					_dirtyGuard = prev;
				}
			}
			catch (Exception ex)
			{
				F.StoryLog.Fail(ex, "ToggleNarrativeLogView");
			}
		}

		private static bool _dirtyGuard;
	}

	/// <summary>Scales the Fungus Writer reveal speed for English readers.</summary>
	public static class TextSpeedPatch
	{
		private class Scaled
		{
			public Component Writer;
			public float Speed;
			public float Pause;
		}

		// The game's own speed of every Writer seen so far, captured at its Start, so the multiplier can change at any time.
		private static readonly Dictionary<int, Scaled> _writers = new Dictionary<int, Scaled>();

		public static void Patch(Harmony h)
		{
			Compat.Setup(F.TextSpeed, delegate
			{
				Compat.Require(F.TextSpeed, typeof(Writer), "writingSpeed");
				Compat.Require(F.TextSpeed, typeof(Writer), "punctuationPause");
				F.TextSpeed.OnTurnedOff = RestoreAll;
				F.TextSpeed.OnTurnedOn = ApplyAll;
				if (Compat.Hook(F.TextSpeed, h, AccessTools.Method(typeof(Writer), "Start"), "Fungus.Writer.Start", null, new HarmonyMethod(typeof(TextSpeedPatch), "Start_Postfix"), essential: true))
				{
					Plugin.Log.LogInfo($"story text speed x{Factor()}");
				}
			});
		}

		private static float Factor()
		{
			float v = Plugin.CfgTextSpeed.Value;
			return (v > 0f) ? v : 1f;
		}

		private static void Start_Postfix(Component __instance)
		{
			if (!F.TextSpeed.Live)
			{
				return;
			}
			try
			{
				F.TextSpeed.Probe();
				if (__instance == null || _writers.ContainsKey(__instance.GetInstanceID()))
				{
					return;
				}
				if (_writers.Count > 64)
				{
					Prune();
				}
				Traverse t = Traverse.Create(__instance);
				Scaled s = new Scaled
				{
					Writer = __instance,
					Speed = t.Field("writingSpeed").GetValue<float>(),
					Pause = t.Field("punctuationPause").GetValue<float>()
				};
				_writers[__instance.GetInstanceID()] = s;
				Apply(s);
				if (Plugin.CfgVerbose.Value)
				{
					Plugin.Log.LogInfo($"writer {__instance.gameObject.name}: speed {s.Speed} -> {s.Speed * Factor()}");
				}
			}
			catch (Exception ex)
			{
				F.TextSpeed.Fail(ex, "Writer.Start");
			}
		}

		private static void Apply(Scaled s)
		{
			float factor = Factor();
			Traverse t = Traverse.Create(s.Writer);
			if (s.Speed > 0f)
			{
				t.Field("writingSpeed").SetValue(s.Speed * factor);
			}
			if (s.Pause > 0f)
			{
				t.Field("punctuationPause").SetValue(s.Pause / factor);
			}
		}

		/// <summary>Turned off: every Writer seen goes back to the game's own speed.</summary>
		private static void RestoreAll()
		{
			Prune();
			foreach (Scaled s in _writers.Values)
			{
				try
				{
					Traverse t = Traverse.Create(s.Writer);
					if (s.Speed > 0f)
					{
						t.Field("writingSpeed").SetValue(s.Speed);
					}
					if (s.Pause > 0f)
					{
						t.Field("punctuationPause").SetValue(s.Pause);
					}
				}
				catch (Exception)
				{
				}
			}
		}

		/// <summary>Re-scale every live Writer after [Story] TextSpeedMultiplier changed.</summary>
		public static void ApplyAll()
		{
			if (!F.TextSpeed.Live)
			{
				return;
			}
			Prune();
			foreach (Scaled s in _writers.Values)
			{
				try
				{
					Apply(s);
				}
				catch (Exception ex)
				{
					Plugin.Log.LogWarning("text speed update failed: " + ex.Message);
				}
			}
		}

		private static void Prune()
		{
			List<int> dead = null;
			foreach (KeyValuePair<int, Scaled> kv in _writers)
			{
				if (kv.Value.Writer == null)
				{
					(dead ?? (dead = new List<int>())).Add(kv.Key);
				}
			}
			if (dead != null)
			{
				foreach (int id in dead)
				{
					_writers.Remove(id);
				}
			}
		}
	}
	/// <summary>Status > Social: hovering the affinity level digit, star or progress bar shows the exact 0-100 value the game only draws as a level and a bar.</summary>
	public static class RelationshipHover
	{
		private static bool _warned;
		public static void Patch(Harmony h)
		{
			Compat.Setup(F.AffinityHover, delegate
			{
				Compat.Require(F.AffinityHover, typeof(SocialStatPanel), "_currentData");
				Compat.Require(F.AffinityHover, typeof(SocialStatPanel), "_levelText");
				Compat.Require(F.AffinityHover, typeof(SocialStatPanel), "_progressBar", essential: false);
				Compat.Require(F.AffinityHover, typeof(RelationshipStat), "Value");
				F.AffinityHover.OnTurnedOff = HideAll;
				if (Compat.Hook(F.AffinityHover, h, AccessTools.Method(typeof(SocialStatPanel), "UpdateInfo"), "SocialStatPanel.UpdateInfo", null, new HarmonyMethod(typeof(RelationshipHover), "UpdateInfo_Postfix"), essential: true))
				{
					Plugin.Log.LogInfo("relationship hover value " + (Plugin.CfgRelationshipHover.Value ? "enabled" : "installed (off)"));
				}
			});
		}
		// SocialStatPanel.UpdateInfo() runs whenever a character is selected: it writes Value / 10 into _levelText and (Value % 10) / 10 into _progressBar.
		private static void UpdateInfo_Postfix(Component __instance)
		{
			if (!F.AffinityHover.Live || !Plugin.CfgRelationshipHover.Value)
			{
				return;
			}
			try
			{
				F.AffinityHover.Probe();
				UpdateInfoBody(__instance);
			}
			catch (Exception ex)
			{
				F.AffinityHover.Fail(ex, "SocialStatPanel.UpdateInfo");
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void UpdateInfoBody(Component __instance)
		{
			{
				Traverse t = Traverse.Create(__instance);
				RelationshipStat data = t.Field("_currentData").GetValue<RelationshipStat>();
				Text level = t.Field("_levelText").GetValue<Text>();
				Image bar = t.Field("_progressBar").GetValue<Image>();
				if (data == null || level == null || level.transform.parent == null)
				{
					if (!_warned)
					{
						_warned = true;
						Plugin.Log.LogWarning($"relationship hover: SocialStatPanel fields not found (data {data != null}, level {level != null}); the game's field names may have changed");
					}
					return;
				}
				GameObject info = level.transform.parent.gameObject;   // the 400x100 'Info' box that holds Level, Star, Name, Title and Progress
				RelationshipHoverTip tip = info.GetComponent<RelationshipHoverTip>() ?? info.AddComponent<RelationshipHoverTip>();
				tip.Setup(level, bar, data);
			}
		}
		/// <summary>[Social] RelationshipHover changed: hide any tip that is up; the next hover or panel update follows the setting.</summary>
		public static void OnToggle()
		{
			if (!Plugin.CfgRelationshipHover.Value)
			{
				HideAll();
			}
		}

		private static void HideAll()
		{
			foreach (RelationshipHoverTip tip in Resources.FindObjectsOfTypeAll<RelationshipHoverTip>())
			{
				if (tip != null)
				{
					tip.Hide();
				}
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int ReadValue(object data)
		{
			return ((RelationshipStat)data).Value;
		}
		public static string Label()
		{
			try
			{
				string s = LocalizationManager.Instance.LocaleResolver.GetString("PlayerStat/relationship");
				if (!string.IsNullOrEmpty(s) && !Regex.IsMatch(s, "[一-鿿]"))
				{
					return s.Trim();
				}
			}
			catch (Exception)
			{
			}
			return "Affinity";
		}
	}
	/// <summary>Lives on the Social panel's Info box; pointer enter/exit bubble up from its child graphics (Back, Level, Star, Progress).</summary>
	public class RelationshipHoverTip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
	{
		// object, not RelationshipStat: this class then loads (and hides its tip) even if the game renames that type.
		private object _data;
		private Text _level;
		private GameObject _tip;
		private Text _tipText;
		private CanvasGroup _group;
		public void Setup(Text level, Image bar, object data)
		{
			_data = data;
			_level = level;
			EnsureTip();
			// The whole Info box is the hover area: its Back plate plus the level digit, star and bar. Marking all of them keeps the
			// raycast continuous, so sliding from the digit to the bar never leaves the box (an uncovered gap would fire exit/enter and flicker).
			MarkTarget(level);
			if (bar != null)
			{
				MarkTarget(bar);
				MarkTarget(bar.transform.parent != null ? bar.transform.parent.GetComponent<Graphic>() : null);
			}
			foreach (string child in new string[2] { "Star", "Back" })
			{
				Transform c = transform.Find(child);
				if (c != null)
				{
					MarkTarget(c.GetComponent<Graphic>());
				}
			}
			Refresh();
		}
		private static void MarkTarget(Graphic g)
		{
			if (g != null)
			{
				g.raycastTarget = true;
			}
		}
		private void EnsureTip()
		{
			if (_tip != null)
			{
				return;
			}
			// Distinct names keep the plugin's own path rules away from these objects, and visibility is a CanvasGroup alpha rather than
			// SetActive so the Text/Image OnEnable hooks (font map + rule pass) run once at creation instead of on every hover.
			_tip = new GameObject("AffinityTip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
			_group = _tip.GetComponent<CanvasGroup>();
			_group.alpha = 0f;
			_group.blocksRaycasts = false;
			_group.interactable = false;
			RectTransform rt = _tip.GetComponent<RectTransform>();
			rt.SetParent(transform, worldPositionStays: false);
			rt.anchorMin = new Vector2(1f, 1f);
			rt.anchorMax = new Vector2(1f, 1f);
			rt.pivot = new Vector2(1f, 0f);
			rt.anchoredPosition = new Vector2(-36f, 4f);     // just above the top-right corner of the Info box, over the level digit
			rt.sizeDelta = new Vector2(200f, 40f);
			Image bg = _tip.GetComponent<Image>();
			bg.color = new Color(0.08f, 0.06f, 0.05f, 0.92f);
			bg.raycastTarget = false;
			GameObject tg = new GameObject("AffinityTipText", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
			RectTransform trt = tg.GetComponent<RectTransform>();
			trt.SetParent(rt, worldPositionStays: false);
			trt.anchorMin = Vector2.zero;
			trt.anchorMax = Vector2.one;
			trt.offsetMin = new Vector2(8f, 2f);
			trt.offsetMax = new Vector2(-8f, -2f);
			_tipText = tg.GetComponent<Text>();
			_tipText.font = _level.font;
			_tipText.fontSize = 22;
			_tipText.resizeTextForBestFit = true;
			_tipText.resizeTextMinSize = 14;
			_tipText.resizeTextMaxSize = 22;
			_tipText.alignment = TextAnchor.MiddleCenter;
			_tipText.horizontalOverflow = HorizontalWrapMode.Wrap;
			_tipText.verticalOverflow = VerticalWrapMode.Truncate;
			_tipText.color = new Color(0.96f, 0.92f, 0.82f, 1f);
			_tipText.raycastTarget = false;
		}
		private void Show(bool on)
		{
			if (_group != null)
			{
				_group.alpha = (on ? 1f : 0f);
			}
		}
		public void Refresh()
		{
			if (_tipText == null || _data == null)
			{
				return;
			}
			string format = Plugin.CfgRelationshipHoverFormat.Value;
			if (string.IsNullOrEmpty(format))
			{
				format = "{2} {0} / {1}";
			}
			int value = RelationshipHover.ReadValue(_data);
			try
			{
				_tipText.text = string.Format(format, value, 100, RelationshipHover.Label());
			}
			catch (FormatException)
			{
				_tipText.text = $"{RelationshipHover.Label()} {value} / 100";
			}
		}
		public void OnPointerEnter(PointerEventData eventData)
		{
			if (!Plugin.CfgRelationshipHover.Value || !F.AffinityHover.Live)
			{
				return;
			}
			try
			{
				F.AffinityHover.Probe();
				Refresh();
				Show(on: true);
			}
			catch (Exception ex)
			{
				F.AffinityHover.Fail(ex, "affinity tip");
			}
		}
		public void Hide()
		{
			Show(on: false);
		}
		public void OnPointerExit(PointerEventData eventData)
		{
			Show(on: false);
		}
		private void OnDisable()
		{
			Show(on: false);
		}
	}
}
