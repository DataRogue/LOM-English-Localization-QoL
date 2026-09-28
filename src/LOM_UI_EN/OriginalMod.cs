using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Mortal.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LOM_UI_EN
{
	public enum BaseData
	{
		Unknown,
		None,
		Pristine,
		OurEdits,
		Other
	}

	public enum BaseSummary
	{
		Absent,
		Complete,
		Partial
	}

	/// <summary>The base patch's plugin that answers game data lookups from its own table.</summary>
	public enum TablePlugin
	{
		None,
		/// <summary>Binarizer, reading one StringTable.csv (releases up to 2026.02).</summary>
		Binarizer,
		/// <summary>The patch's own FanslationStudio.LegendOfMortal.Plugin, reading the per-file tables (llmkit-upgrade, 2026-09).</summary>
		LlmKit
	}

	/// <summary>
	/// Detects the original English patch (OverLlm EnglishPatch: Binarizer, XUnity AutoTranslator + ResourceRedirector,
	/// LOM_UI_Plugin_KR, Mods/English/StringTable.csv; since its llmkit-upgrade its own plugin FanslationStudio.LegendOfMortal.Plugin
	/// with one table per game source file in Mods/English, see BaseTableSet) by what is running, not by what is listed: in BepInEx
	/// be.692 a plugin whose Awake died is still in the chainloader's list, so each component is checked for its Harmony hooks and
	/// its data. The new plugin is built for BepInEx 5, which be.692 never loads: then its tables are only read by this mod. The data
	/// check compares a few sentinel lines (translation/MANIFEST.json, which keeps only hashes of their English) whose revised and
	/// original English differ, which tells whether the base patch still has its own text (Pristine; Other = a different release)
	/// or holds this mod's edits. Since 0.6 this mod ships its whole reviewed translation; when the base patch is installed its
	/// files (BaseTable, BaseScenePath, BaseResizeDir) are read under this mod's lines (fallback, Original), never written.
	/// Nothing here changes the base patch unless [Compat] SuppressKrPlugin or [Dev] DetachBaseMod is on.
	/// </summary>
	public static class OriginalMod
	{
		public const string BinarizerGuid = "binarizer.plugin.mortal";
		public const string XUnityGuid = "gravydevsupreme.xunity.autotranslator";
		public const string RedirectorGuid = "gravydevsupreme.xunity.resourceredirector";
		public const string KrGuid = "LegendOfMortal_UI_KR";
		public const string XUnityHarmony = "xunity.common.hookinghelper";
		/// <summary>The patch's own plugin since its llmkit-upgrade (BepInEx.PluginInfoProps: GUID and assembly = AssemblyName). Its
		/// GetString prefix is StringTableInjectionPatches.GetString_Prefix, its table the static Dictionary _translations, filled
		/// on the first lookup through the static property Translations.</summary>
		public const string LlmKitGuid = "FanslationStudio.LegendOfMortal.Plugin";
		public const string LlmKitAssembly = "FanslationStudio.LegendOfMortal.Plugin";
		public const string LlmKitDll = LlmKitAssembly + ".dll";
		public const string LlmKitInjection = "StringTableInjectionPatches";
		/// <summary>The FanslationStudio.Plugins pack the newer releases ship next to it (BaseGuards).</summary>
		public const string PackDll = "FanslationStudio.Plugins.dll";

		public sealed class Sentinel
		{
			public string Key;
			/// <summary>Hash (TextHash) of this mod's English and of the base patch's for the key.</summary>
			public string Revised;
			public string Original;
		}

		/// <summary>First 16 hex digits of SHA-1 over the UTF-8 text: what MANIFEST.json keeps instead of the base patch's wording.</summary>
		public static string TextHash(string s)
		{
			if (s == null)
			{
				return null;
			}
			using (System.Security.Cryptography.SHA1 sha = System.Security.Cryptography.SHA1.Create())
			{
				byte[] h = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s));
				return BitConverter.ToString(h, 0, 8).Replace("-", "").ToLowerInvariant();
			}
		}

		/// <summary>
		/// The text has the given TextHash as it is or in Binarizer's form: the hashes of the base patch's lines (MANIFEST.json
		/// sentinels, the revision record) are taken as Binarizer reads its StringTable.csv, which gives every line break inside a
		/// value as CRLF and drops blank lines, while the per-file tables of its newer releases give LF and keep them. A difference
		/// in line breaks alone is not a new line.
		/// </summary>
		public static bool SameText(string s, string hash)
		{
			if (s == null || hash == null)
			{
				return false;
			}
			if (TextHash(s) == hash)
			{
				return true;
			}
			return s.IndexOf('\n') >= 0 && TextHash(BinarizerForm(s)) == hash;
		}

		/// <summary>Line breaks as Binarizer's parser returns them: each run of them one CRLF (it skips empty lines).</summary>
		public static string BinarizerForm(string s)
		{
			return System.Text.RegularExpressions.Regex.Replace(s.Replace("\r\n", "\n"), "\n+", "\r\n");
		}

		public static string EnglishModDir => Path.Combine(Path.Combine(Paths.GameRootPath, "Mods"), "English");

		private static BaseTableSet _baseTable;

		/// <summary>
		/// The base patch's game data table as installed: the llmkit-upgrade per-file tables in Mods/English, else Mods/English/
		/// StringTable.csv or the StringTable.csv of a folder Binarizer loads. Looked up again on every detection.
		/// </summary>
		public static BaseTableSet BaseTable => _baseTable ?? (_baseTable = FindBaseTable());

		private static BaseTableSet FindBaseTable()
		{
			List<string> binarizerDirs = null;
			try
			{
				Type hook = TranslationProfiles.HookMods;
				if (hook != null)
				{
					binarizerDirs = AccessTools.Field(hook, "ModPaths")?.GetValue(null) as List<string>;
				}
			}
			catch (Exception)
			{
			}
			try
			{
				return BaseTableSet.Find(EnglishModDir, binarizerDirs);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("base patch table check failed: " + ex.Message);
				return BaseTableSet.Empty;
			}
		}

		/// <summary>The BepInEx major version running (5 or 6), from the name of the assembly that holds Paths.</summary>
		public static int RunningBepInExMajor => (typeof(Paths).Assembly.GetName().Name == "BepInEx.Core") ? 6 : 5;

		/// <summary>
		/// A plugin DLL built for another BepInEx major version than the one running (BepInEx never loads those): BepInEx 6 plugins
		/// reference the assembly BepInEx.Core, BepInEx 5 ones only BepInEx. Read from the metadata's string heap, without loading.
		/// </summary>
		private static bool ForOtherBepInEx(string dll)
		{
			try
			{
				FileInfo fi = new FileInfo(dll);
				string id = dll + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
				bool other;
				lock (_otherBepInEx)
				{
					if (_otherBepInEx.TryGetValue(id, out other))
					{
						return other;
					}
				}
				byte[] bytes = File.ReadAllBytes(dll);
				bool bie6 = IndexOf(bytes, System.Text.Encoding.ASCII.GetBytes("\0BepInEx.Core\0")) >= 0;
				other = bie6 != (RunningBepInExMajor == 6);
				lock (_otherBepInEx)
				{
					_otherBepInEx[id] = other;
				}
				return other;
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// <summary>ForOtherBepInEx by path, size and write time (detection runs several times at startup).</summary>
		private static readonly Dictionary<string, bool> _otherBepInEx = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

		private static int IndexOf(byte[] hay, byte[] needle)
		{
			for (int i = 0; i + needle.Length <= hay.Length; i++)
			{
				int j = 0;
				while (j < needle.Length && hay[i + j] == needle[j])
				{
					j++;
				}
				if (j == needle.Length)
				{
					return i;
				}
			}
			return -1;
		}

		private static Type _llmKitType;
		private static Dictionary<string, string> _llmKitMap;

		/// <summary>The table of the patch's own plugin (null while it is not hooked). load = fill it now if its first lookup has not
		/// happened yet, the way that lookup would (reading its files once).</summary>
		private static Dictionary<string, string> LlmKitMap(bool load)
		{
			Type t = _llmKitType;
			if (t == null)
			{
				return null;
			}
			Dictionary<string, string> map = AccessTools.Field(t, "_translations")?.GetValue(null) as Dictionary<string, string>;
			if (map == null && load)
			{
				map = AccessTools.Property(t, "Translations")?.GetValue(null, null) as Dictionary<string, string>;
			}
			if (map != null)
			{
				_llmKitMap = map;
			}
			return map;
		}

		private static bool IsLlmKitPatch(MethodInfo m)
		{
			Type t = m?.DeclaringType;
			return t != null && (t.Assembly.GetName().Name == LlmKitAssembly || t.Name == LlmKitInjection);
		}

		/// <summary>The base patch's own line for a key, from the plugin the game data text is handed back to (Original).</summary>
		public static bool TryGetHandBackLine(string key, out string value)
		{
			value = null;
			if (key == null)
			{
				return false;
			}
			switch (Current.HandBackTable)
			{
			case TablePlugin.LlmKit:
			{
				Dictionary<string, string> map = _llmKitMap;
				return map != null && map.TryGetValue(key, out value);
			}
			case TablePlugin.Binarizer:
				return PerfPatches.TryGetBase(key, out value);
			default:
				return false;
			}
		}

		/// <summary>The base patch's XUnity AutoTranslator folder (its translation file and resizers).</summary>
		public static string BaseTextDir => Path.Combine(Path.Combine(Paths.BepInExRootPath, "Translation"), Path.Combine("en", "Text"));

		public static string BaseScenePath => Path.Combine(BaseTextDir, "_AutoGeneratedTranslations.txt");

		public static IEnumerable<string> TableSentinelKeys => _tableSentinels.Select((Sentinel s) => s.Key);

		public static IEnumerable<string> SceneSentinelKeys => _sceneSentinels.Select((Sentinel s) => s.Key);

		/// <summary>The data check on values read straight from the base patch's files (for Original served from them).</summary>
		public static BaseData VoteSample(bool table, Dictionary<string, string> sample, out string detail)
		{
			if (sample == null || sample.Count == 0)
			{
				detail = "no sentinel lines in the file";
				return BaseData.Unknown;
			}
			return Vote(table ? _tableSentinels : _sceneSentinels, delegate(string k, out string v)
			{
				return sample.TryGetValue(k, out v);
			}, out detail);
		}

		public sealed class Report
		{
			public bool BinarizerInstalled;
			public bool BinarizerLoaded;
			public bool BinarizerHooked;
			public int BinarizerEntries = -1;
			public bool BinarizerEnglishFolder;
			public bool AddressablesPatched;
			public BaseData BinarizerData = BaseData.Unknown;
			public bool XUnityInstalled;
			public bool XUnityLoaded;
			public bool XUnityRunning;
			public bool XUnityHooked;
			public BaseData XUnityData = BaseData.Unknown;
			public bool RedirectorLoaded;
			public bool KrInstalled;
			public bool KrLoaded;
			public bool KrHooked;
			public bool KrSuppressed;
			public bool BaseTableFile;
			public bool BaseSceneFile;
			public bool Detached;
			public string BinarizerVote = "";
			public string XUnityVote = "";
			/// <summary>The patch's own plugin (llmkit-upgrade): its DLL on disk, in the chainloader, its GetString prefix, its table.</summary>
			public bool LlmKitInstalled;
			public bool LlmKitLoaded;
			public bool LlmKitHooked;
			public int LlmKitEntries = -1;
			public BaseData LlmKitData = BaseData.Unknown;
			public string LlmKitVote = "";
			/// <summary>Its DLL is built for another BepInEx major version than the one running, which never loads it.</summary>
			public bool LlmKitOtherBepInEx;
			/// <summary>The same for its older plugins, built for BepInEx 6: on BepInEx 5 (the loader of its own plugin) they never run.</summary>
			public bool BinarizerOtherBepInEx;
			public bool XUnityOtherBepInEx;
			public bool KrOtherBepInEx;
			/// <summary>How the table on disk is laid out, and whether an old StringTable.csv sits next to the per-file tables.</summary>
			public BaseTableLayout TableLayout;
			public bool TableLeftover;
			/// <summary>Its plugin also writes its English into Lean's labels (a GetTranslation postfix, release 2026.09.28 on).</summary>
			public bool LlmKitLabelsHooked;
			/// <summary>Its FanslationStudio.Plugins pack (prefab text, code strings, text resizer, UI editor), on disk and loaded.</summary>
			public bool PackInstalled;
			public bool PackLoaded;
			public bool PackOtherBepInEx;

			/// <summary>Any of the base patch's plugins or text files is on disk (this mod reads the text files itself).</summary>
			public bool AnyInstalled => BinarizerInstalled || LlmKitInstalled || PackInstalled || XUnityInstalled || KrInstalled || BaseTableFile || BaseSceneFile;

			/// <summary>The base patch's English is on disk for this mod to lay its revisions over.</summary>
			public bool TextInstalled => BaseTableFile || BaseSceneFile;

			/// <summary>A game data plugin of the base patch has its lookup hook in.</summary>
			public bool TablePluginHooked => BinarizerHooked || LlmKitHooked;

			/// <summary>A game data plugin of the base patch answers lookups from a table of its own.</summary>
			public bool TablePluginRuns => (BinarizerHooked && BinarizerEntries > 0) || (LlmKitHooked && LlmKitEntries > 0);

			/// <summary>The plugin for the table layout on disk runs with it (Binarizer holding an older release's StringTable.csv
			/// next to the per-file tables does not serve the installed text).</summary>
			public bool TablePluginServesLayout => (TableLayout == BaseTableLayout.PerFile) ? (LlmKitHooked && LlmKitEntries > 0) : (BinarizerHooked && BinarizerEntries > 0);

			/// <summary>The per-file tables are installed but no plugin serves them (the patch's own plugin not loaded by this
			/// BepInEx): this mod reads them itself, and they only show through this mod.</summary>
			public bool TablesUnserved => TableLayout == BaseTableLayout.PerFile && !LlmKitHooked;

			public BaseSummary Summary
			{
				get
				{
					if (!AnyInstalled)
					{
						return BaseSummary.Absent;
					}
					// The llmkit-upgrade release no longer updates XUnity's files, and its BepInEx 5 never runs XUnity's BepInEx 6
					// build: without a XUnity that can run, the table alone is the patch.
					bool scene = (XUnityRunning && XUnityHooked && BaseSceneFile) || (TableLayout == BaseTableLayout.PerFile && (!XUnityInstalled || XUnityOtherBepInEx));
					if (TablePluginServesLayout && scene)
					{
						return BaseSummary.Complete;
					}
					return BaseSummary.Partial;
				}
			}

			private static bool OwnText(BaseData d)
			{
				return d == BaseData.Pristine || d == BaseData.Other;
			}

			/// <summary>
			/// The plugin the Original table can be left to: one that runs with its own (non-revised) table read from the layout on
			/// disk (Binarizer the single StringTable.csv, the patch's own plugin the per-file tables; Binarizer holding an old
			/// release's file next to the new tables is not the patch's text any more).
			/// </summary>
			public TablePlugin HandBackTable
			{
				get
				{
					if (Detached)
					{
						return TablePlugin.None;
					}
					if (LlmKitHooked && LlmKitEntries > 0 && OwnText(LlmKitData) && TableLayout == BaseTableLayout.PerFile)
					{
						return TablePlugin.LlmKit;
					}
					if (BinarizerHooked && BinarizerEntries > 0 && OwnText(BinarizerData) && TableLayout != BaseTableLayout.PerFile)
					{
						return TablePlugin.Binarizer;
					}
					return TablePlugin.None;
				}
			}

			/// <summary>A plugin of the base patch runs with its own (non-revised) table, so the Original table can be left to it.</summary>
			public bool TableHandBack => HandBackTable != TablePlugin.None;

			/// <summary>Lines in the table of the plugin the Original table is left to.</summary>
			public int HandBackEntries => (HandBackTable == TablePlugin.LlmKit) ? LlmKitEntries : BinarizerEntries;

			/// <summary>XUnity runs with its own (non-revised) lines, so Original scene text can be left to it.</summary>
			public bool SceneHandBack => !Detached && XUnityRunning && XUnityHooked && (XUnityData == BaseData.Pristine || XUnityData == BaseData.Other);

			public List<string> Diagnoses()
			{
				List<string> d = new List<string>();
				bool perFile = TableLayout == BaseTableLayout.PerFile;
				if (Detached)
				{
					d.Add("detached for testing ([Dev] DetachBaseMod): its hooks were removed at startup");
				}
				if (LlmKitInstalled && !LlmKitHooked && !Detached)
				{
					d.Add("its plugin (" + LlmKitDll + ") is installed but not running" + (LlmKitOtherBepInEx ? (": it is built for another BepInEx version than this game runs (BepInEx " + RunningBepInExMajor + "), which never loads it") : "") + (perFile ? "; this mod reads its tables itself" : ""));
				}
				else if (LlmKitHooked && LlmKitEntries <= 0)
				{
					d.Add("its plugin is running with no tables in Mods/English");
				}
				else if (perFile && !LlmKitInstalled && !Detached)
				{
					d.Add("its tables are in Mods/English but not its plugin (" + LlmKitDll + "); this mod reads them itself");
				}
				if (BinarizerInstalled && !BinarizerHooked)
				{
					// The per-file tables do not need Binarizer: its state only matters while its table is the one installed.
					if (!perFile)
					{
						d.Add(BinarizerOtherBepInEx ? ("Binarizer is installed but not running: it is built for another BepInEx version than this game runs (BepInEx " + RunningBepInExMajor + ")") : (AddressablesPatched ? "Binarizer is installed but not running" : "Binarizer is installed but not running: Unity.Addressables.dll is the stock copy (a Steam update or file check replaces the patched one); this mod does not need it"));
					}
				}
				else if (BinarizerHooked && perFile)
				{
					d.Add((BinarizerEntries > 0) ? "Binarizer still runs, with an older release's table (StringTable.csv); the new release's tables are for its own plugin" : "Binarizer still runs, with no table of its own (the new release's tables are for its own plugin)");
				}
				else if (BinarizerHooked && BinarizerEntries <= 0)
				{
					d.Add("Binarizer is running with no English table (Mods/English/StringTable.csv)");
				}
				if (TableLeftover)
				{
					d.Add("Mods/English still holds the " + BaseTableSet.SingleName + " of an older release next to the new tables: this mod leaves it out" + (LlmKitHooked ? ", but the patch's plugin reads it too, over part of the new text (delete that file)" : ""));
				}
				if (PackInstalled && !PackLoaded && !Detached)
				{
					d.Add("its plugin pack (" + PackDll + ") is installed but not running" + (PackOtherBepInEx ? (": it is built for another BepInEx version than this game runs (BepInEx " + RunningBepInExMajor + "), which never loads it") : ""));
				}
				if (XUnityInstalled && !XUnityRunning)
				{
					d.Add("XUnity AutoTranslator is installed but not running" + (XUnityOtherBepInEx ? (": it is built for another BepInEx version than this game runs (BepInEx " + RunningBepInExMajor + "); this mod serves the scene text itself") : ""));
				}
				if (XUnityRunning && !TablePluginHooked && !perFile && !LlmKitInstalled)
				{
					d.Add("XUnity AutoTranslator runs without Binarizer");
				}
				if (BinarizerHooked && !XUnityRunning && !perFile)
				{
					d.Add("Binarizer runs without XUnity AutoTranslator");
				}
				bool tableEdits = BinarizerData == BaseData.OurEdits || LlmKitData == BaseData.OurEdits;
				if (tableEdits || XUnityData == BaseData.OurEdits)
				{
					d.Add("its files contain this mod's edits (" + (tableEdits ? "string table" : "") + ((tableEdits && XUnityData == BaseData.OurEdits) ? " and " : "") + ((XUnityData == BaseData.OurEdits) ? "scene lines" : "") + "); Original cannot show its own text until the patch is reinstalled");
				}
				if (AnyInstalled && !BaseTableFile)
				{
					d.Add("its game data table (Mods/English) is missing" + (TranslationProfiles.OwnTableComplete ? " (this mod's own table covers the game data text)" : ": only this mod's own game data lines are in English"));
				}
				if (AnyInstalled && !BaseSceneFile && !(perFile && (!XUnityInstalled || XUnityOtherBepInEx)))
				{
					d.Add("its scene text file (BepInEx/Translation/en/Text/_AutoGeneratedTranslations.txt) is missing" + (TranslationProfiles.OwnSceneComplete ? " (this mod's own scene lines cover the scene text)" : ": only this mod's own scene lines are in English"));
				}
				if (XUnityRunning && !XUnityHooked)
				{
					d.Add("XUnity AutoTranslator runs but its text hooks are missing");
				}
				if (KrInstalled && !BinarizerInstalled && !LlmKitInstalled && !XUnityInstalled && !TextInstalled)
				{
					d.Add("only its LOM_UI_Plugin_KR layout plugin is installed (its fixes are also built into this mod)");
				}
				if (d.Count == 0 && Summary == BaseSummary.Partial)
				{
					d.Add("some of its files are present but it is not fully running");
				}
				return d;
			}
		}

		public static ConfigEntry<bool> CfgSuppressKr;
		public static ConfigEntry<bool> CfgDetach;

		private static List<Sentinel> _tableSentinels = new List<Sentinel>();
		private static List<Sentinel> _sceneSentinels = new List<Sentinel>();
		private static int _sceneChecks;
		private static bool _lateDone;
		private static bool _detached;
		private static bool _krSuppressed;
		private static Harmony _neutralizer;
		private static readonly HashSet<MethodInfo> _neutralized = new HashSet<MethodInfo>();

		public static Report Current { get; private set; } = new Report();

		public static string ManifestPath => Path.Combine(TranslationProfiles.TranslationDir, "MANIFEST.json");

		public static string ManifestError { get; private set; }

		/// <summary>MANIFEST.json "standalone" (publish.py, since 0.6): the table rows and scene lines this mod ships and the full
		/// working copy's counts at publish time (TranslationProfiles.OwnTableComplete compares them); -1 when the manifest has
		/// none (an overlay that needs the OverLlm patch's text for most of the game).</summary>
		public static int ManifestTableRows { get; private set; } = -1;
		public static int ManifestTableTotal { get; private set; } = -1;
		public static int ManifestSceneLines { get; private set; } = -1;
		public static int ManifestSceneTotal { get; private set; } = -1;

		/// <summary>Raised when the report changes in a way that can change which engine serves a layer.</summary>
		public static event Action Changed;

		public static void Bind(ConfigFile cfg)
		{
			CfgSuppressKr = cfg.Bind("Compat", "SuppressKrPlugin", false, "Remove the Harmony patches of LOM_UI_Plugin_KR (the Korean layout plugin that comes with " + Plugin.BaseModName + ") at startup. Its layout fixes are built into this mod's rules, so this only drops its per-object SetActive hook. Turning it back off needs a restart.");
			CfgDetach = cfg.Bind("Dev", "DetachBaseMod", false, "Testing only: at startup remove every Harmony patch of " + Plugin.BaseModName + " (Binarizer or its own " + LlmKitAssembly + ", XUnity AutoTranslator, LOM_UI_Plugin_KR) and disable its components, to see this mod running on its own without uninstalling anything. Needs a restart both ways.");
			LoadManifest();
		}

		private static void LoadManifest()
		{
			_tableSentinels = new List<Sentinel>();
			_sceneSentinels = new List<Sentinel>();
			ManifestError = null;
			ManifestTableRows = ManifestTableTotal = ManifestSceneLines = ManifestSceneTotal = -1;
			try
			{
				if (!File.Exists(ManifestPath))
				{
					ManifestError = "translation/MANIFEST.json is missing";
					return;
				}
				JObject m = JObject.Parse(File.ReadAllText(ManifestPath));
				_tableSentinels = ReadSentinels(m["table_sentinels"]);
				_sceneSentinels = ReadSentinels(m["scene_sentinels"]);
				if (m["standalone"] is JObject sa)
				{
					ManifestTableRows = Count(sa["table_rows"]);
					ManifestTableTotal = Count(sa["table_total"]);
					ManifestSceneLines = Count(sa["scene_lines"]);
					ManifestSceneTotal = Count(sa["scene_total"]);
				}
			}
			catch (Exception ex)
			{
				ManifestError = "MANIFEST.json: " + ex.Message;
			}
		}

		private static int Count(JToken t)
		{
			return (t != null && t.Type == JTokenType.Integer) ? (int)t : -1;
		}

		private static List<Sentinel> ReadSentinels(JToken t)
		{
			List<Sentinel> list = new List<Sentinel>();
			if (t is JArray arr)
			{
				foreach (JToken e in arr)
				{
					string k = (string)e["key"];
					// Hashes since 0.5 (no base-patch text in this mod); plain values from 0.4 manifests are hashed here.
					string r = (string)e["revised_sha1"] ?? TextHash((string)e["revised"]);
					string o = (string)e["original_sha1"] ?? TextHash((string)e["original"]);
					if (!string.IsNullOrEmpty(k) && r != null && o != null && r != o)
					{
						list.Add(new Sentinel
						{
							Key = k,
							Revised = r,
							Original = o
						});
					}
				}
			}
			return list;
		}

		/// <summary>Plugin.Awake: the base patch's plugins load before ours (soft dependencies), so their hooks and tables exist.</summary>
		// In its own method: a game without LeanLocalizationResolver fails here (caught by the caller), not the whole detection.
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private static MethodInfo ResolverGetString()
		{
			return AccessTools.Method(typeof(LeanLocalizationResolver), "GetString", new Type[1] { typeof(string) });
		}

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private static MethodInfo LeanGetTranslation()
		{
			return AccessTools.Method(typeof(Lean.Localization.LeanLocalization), "GetTranslation", new Type[1] { typeof(string) });
		}

		public static void DetectEarly()
		{
			Current = Detect(Current);
			Plugin.Log.LogInfo("base patch: " + Describe(Current));
			try
			{
				Loader.WhenAllLoaded(DetectLate);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("chainloader Finished event not reachable: " + ex.Message);
			}
		}

		/// <summary>After every plugin has loaded: suppression and detach run here, then the report is taken again.</summary>
		public static void DetectLate()
		{
			if (_lateDone)
			{
				return;
			}
			_lateDone = true;
			try
			{
				if (CfgDetach.Value)
				{
					Detach();
				}
				else if (CfgSuppressKr.Value)
				{
					SuppressKr();
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogError("base patch detach/suppress failed: " + ex);
			}
			// Current is still the report from before the detach, so the comparison in Update sees what changed.
			Update(Detect(Current));
		}

		/// <summary>sceneLoaded: XUnity loads its cache after the plugins, so its data state is retried on the first few scene loads.</summary>
		public static void OnSceneLoaded()
		{
			if (!_lateDone)
			{
				DetectLate();
			}
			if (Current.XUnityData != BaseData.Unknown || _sceneChecks >= 3)
			{
				return;
			}
			_sceneChecks++;
			Update(Detect(Current));
		}

		private static void Update(Report next)
		{
			Report old = Current;
			Current = next;
			if (old.TableHandBack != next.TableHandBack || old.HandBackTable != next.HandBackTable || old.SceneHandBack != next.SceneHandBack || old.XUnityData != next.XUnityData || old.BinarizerData != next.BinarizerData || old.LlmKitData != next.LlmKitData || old.Detached != next.Detached || old.BinarizerHooked != next.BinarizerHooked || old.LlmKitHooked != next.LlmKitHooked || old.XUnityHooked != next.XUnityHooked || old.XUnityRunning != next.XUnityRunning || old.TableLayout != next.TableLayout || old.BaseTableFile != next.BaseTableFile)
			{
				Plugin.Log.LogInfo("base patch: " + Describe(next));
				try
				{
					Changed?.Invoke();
				}
				catch (Exception ex)
				{
					Plugin.Log.LogError("base patch change handler failed: " + ex);
				}
			}
		}

		public static void Refresh()
		{
			Update(Detect(Current));
		}

		private static Report Detect(Report previous)
		{
			Report r = new Report
			{
				KrSuppressed = _krSuppressed,
				Detached = _detached
			};
			try
			{
				string plugins = Paths.PluginPath;
				string binarizer = FindFilePath(plugins, "FunctionalPlugin_Binarizer.dll");
				r.BinarizerInstalled = binarizer != null;
				r.BinarizerOtherBepInEx = binarizer != null && ForOtherBepInEx(binarizer);
				string llmKit = FindFilePath(plugins, LlmKitDll);
				r.LlmKitInstalled = llmKit != null;
				r.LlmKitOtherBepInEx = llmKit != null && ForOtherBepInEx(llmKit);
				r.XUnityInstalled = Directory.Exists(Path.Combine(plugins, "XUnity.AutoTranslator")) || FindFile(plugins, "XUnity.AutoTranslator.Plugin.Core.dll");
				// XUnity's BepInEx wrapper is what the chainloader loads (Plugin.Core itself references no BepInEx).
				string xunity = r.XUnityInstalled ? FindFilePath(plugins, "XUnity.AutoTranslator.Plugin.BepInEx.dll") : null;
				r.XUnityOtherBepInEx = xunity != null && ForOtherBepInEx(xunity);
				string kr = FindFilePath(plugins, "LOM_UI_Plugin_KR.dll");
				r.KrInstalled = kr != null;
				r.KrOtherBepInEx = kr != null && ForOtherBepInEx(kr);
				string pack = FindFilePath(plugins, PackDll);
				r.PackInstalled = pack != null;
				r.PackOtherBepInEx = pack != null && ForOtherBepInEx(pack);
				BaseTableSet table = FindBaseTable();
				if (_baseTable != null && _baseTable.Signature != table.Signature)
				{
					Plugin.Log.LogInfo("base patch table changed on disk: " + _baseTable.Describe() + " -> " + table.Describe());
				}
				_baseTable = table;
				r.TableLayout = table.Layout;
				r.TableLeftover = table.Leftover != null;
				r.BaseTableFile = table.Exists;
				r.BaseSceneFile = File.Exists(BaseScenePath);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("base patch file check failed: " + ex.Message);
			}
			try
			{
				r.BinarizerLoaded = Loaded(BinarizerGuid);
				r.LlmKitLoaded = Loaded(LlmKitGuid) || LoadedAssembly(LlmKitAssembly);
				r.PackLoaded = LoadedGuidPrefix(BaseGuards.PackGuid + ".");
				r.XUnityLoaded = Loaded(XUnityGuid);
				r.RedirectorLoaded = Loaded(RedirectorGuid);
				r.KrLoaded = Loaded(KrGuid);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("chainloader check failed: " + ex.Message);
			}
			try
			{
				MethodInfo getString = ResolverGetString();
				Patches info = (getString == null) ? null : Harmony.GetPatchInfo(getString);
				r.BinarizerHooked = info != null && info.Prefixes.Any((Patch p) => p.PatchMethod?.DeclaringType?.FullName == "Mortal.HookMods");
				Patch llm = info?.Prefixes.FirstOrDefault((Patch p) => IsLlmKitPatch(p.PatchMethod));
				r.LlmKitHooked = llm != null;
				if (llm != null)
				{
					_llmKitType = llm.PatchMethod.DeclaringType;
				}
				MethodInfo getTranslation = LeanGetTranslation();
				Patches labels = (getTranslation == null) ? null : Harmony.GetPatchInfo(getTranslation);
				r.LlmKitLabelsHooked = labels != null && labels.Postfixes.Any((Patch p) => IsLlmKitPatch(p.PatchMethod));
				r.XUnityHooked = Harmony.HasAnyPatches(XUnityHarmony) && !_detached;
				r.KrHooked = Harmony.HasAnyPatches(KrGuid) && !_krSuppressed;
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("Harmony patch check failed: " + ex.Message);
			}
			try
			{
				Dictionary<string, string> map = r.LlmKitHooked ? LlmKitMap(load: true) : null;
				// Game keys all have a '/': the rest are the text of the lines its reader cut from rows spanning lines.
				r.LlmKitEntries = (map == null) ? (-1) : map.Keys.Count((string k) => k.IndexOf('/') >= 0);
				r.LlmKitData = (map == null || map.Count == 0) ? BaseData.None : Vote(_tableSentinels, delegate(string k, out string v)
				{
					return map.TryGetValue(k, out v);
				}, out r.LlmKitVote);
			}
			catch (Exception ex)
			{
				r.LlmKitData = BaseData.None;
				Plugin.Log.LogWarning("the table check of " + LlmKitAssembly + " failed: " + ex.Message);
			}
			try
			{
				Type hook = TranslationProfiles.HookMods;
				if (hook != null)
				{
					Dictionary<string, string> map = AccessTools.Field(hook, "mapString")?.GetValue(null) as Dictionary<string, string>;
					r.BinarizerEntries = map?.Count ?? -1;
					if (AccessTools.Field(hook, "ModPaths")?.GetValue(null) is List<string> paths)
					{
						r.BinarizerEnglishFolder = paths.Any((string p) => string.Equals(Path.GetFileName(p.TrimEnd('/', '\\')), "English", StringComparison.OrdinalIgnoreCase));
					}
					r.BinarizerData = (map == null || map.Count == 0) ? BaseData.None : Vote(_tableSentinels, delegate(string k, out string v)
					{
						return map.TryGetValue(k, out v);
					}, out r.BinarizerVote);
				}
				else
				{
					r.BinarizerData = BaseData.None;
				}
				Type addressables = TranslationProfiles.FindType("Unity.Addressables", "UnityEngine.AddressableAssets.Addressables");
				r.AddressablesPatched = addressables != null && AccessTools.Field(addressables, "ModPaths") != null;
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("Binarizer table check failed: " + ex.Message);
			}
			try
			{
				Type plugin = TranslationProfiles.FindType("XUnity.AutoTranslator.Plugin.Core", "XUnity.AutoTranslator.Plugin.Core.AutoTranslationPlugin");
				object current = (plugin == null) ? null : AccessTools.Field(plugin, "Current")?.GetValue(null);
				r.XUnityRunning = current is Behaviour b && b != null && b.enabled;
				if (!r.XUnityRunning)
				{
					r.XUnityData = BaseData.None;
				}
				else if (previous.XUnityData != BaseData.Unknown && previous.XUnityData != BaseData.None)
				{
					r.XUnityData = previous.XUnityData;
					r.XUnityVote = previous.XUnityVote;
				}
				else
				{
					r.XUnityData = Vote(_sceneSentinels, XUnityBridge.TryTranslate, out r.XUnityVote);
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("XUnity check failed: " + ex.Message);
			}
			return r;
		}

		private delegate bool Lookup(string key, out string value);

		/// <summary>At least two thirds of the sentinels answering with the original (or the revised) English decide; too few answers = Unknown.</summary>
		private static BaseData Vote(List<Sentinel> sentinels, Lookup lookup, out string detail)
		{
			int n = sentinels.Count;
			if (n == 0)
			{
				detail = "no sentinels";
				return BaseData.Unknown;
			}
			int original = 0;
			int revised = 0;
			int answered = 0;
			foreach (Sentinel s in sentinels)
			{
				string v;
				if (!lookup(s.Key, out v) || v == null)
				{
					continue;
				}
				answered++;
				if (SameText(v, s.Original))
				{
					original++;
				}
				else if (SameText(v, s.Revised))
				{
					revised++;
				}
			}
			detail = $"{original} original, {revised} revised, {answered - original - revised} other of {n}";
			if (answered * 3 < n * 2)
			{
				return BaseData.Unknown;
			}
			if (original * 3 >= n * 2)
			{
				return BaseData.Pristine;
			}
			// A meaningful share of this mod's wording means the files hold (some of) our edits, e.g. a partial restore: never
			// hand Original to them. Other is a base patch whose text is mostly neither, i.e. a different release.
			if (revised * 3 >= n)
			{
				return BaseData.OurEdits;
			}
			return BaseData.Other;
		}

		private static bool Loaded(string guid)
		{
			IDictionary<string, PluginInfo> plugins = Loader.Plugins;
			PluginInfo pi;
			return plugins != null && plugins.TryGetValue(guid, out pi) && pi != null && pi.Instance != null;
		}

		/// <summary>The patch's own plugin (newer releases) is loaded and running: by its GUID or its assembly.</summary>
		internal static bool LlmKitPluginLoaded()
		{
			return Loaded(LlmKitGuid) || LoadedAssembly(LlmKitAssembly);
		}

		/// <summary>A loaded plugin from the named assembly, whatever its GUID (a later build may change it).</summary>
		private static bool LoadedAssembly(string assembly)
		{
			IDictionary<string, PluginInfo> plugins = Loader.Plugins;
			return plugins != null && plugins.Values.Any((PluginInfo pi) => pi?.Instance != null && pi.Instance.GetType().Assembly.GetName().Name == assembly);
		}

		/// <summary>A loaded plugin whose GUID starts with the prefix.</summary>
		private static bool LoadedGuidPrefix(string prefix)
		{
			IDictionary<string, PluginInfo> plugins = Loader.Plugins;
			return plugins != null && plugins.Any((KeyValuePair<string, PluginInfo> kv) => kv.Key != null && kv.Key.StartsWith(prefix, StringComparison.Ordinal) && kv.Value?.Instance != null);
		}

		private static bool FindFile(string root, string name)
		{
			return FindFilePath(root, name) != null;
		}

		/// <summary>The file in root or two folder levels below it, or null.</summary>
		private static string FindFilePath(string root, string name)
		{
			if (!Directory.Exists(root))
			{
				return null;
			}
			string p = Path.Combine(root, name);
			if (File.Exists(p))
			{
				return p;
			}
			foreach (string d in Directory.GetDirectories(root))
			{
				p = Path.Combine(d, name);
				if (File.Exists(p))
				{
					return p;
				}
				try
				{
					foreach (string d2 in Directory.GetDirectories(d))
					{
						p = Path.Combine(d2, name);
						if (File.Exists(p))
						{
							return p;
						}
					}
				}
				catch (Exception)
				{
				}
			}
			return null;
		}

		private static void SuppressKr()
		{
			if (!Harmony.HasAnyPatches(KrGuid))
			{
				return;
			}
			Harmony.UnpatchID(KrGuid);
			int inert = Neutralize((Patch p) => p.owner == KrGuid);
			_krSuppressed = true;
			Plugin.Log.LogInfo("LOM_UI_Plugin_KR: its Harmony patches were removed ([Compat] SuppressKrPlugin)" + ((inert > 0) ? $"; {inert} on native Unity methods (GameObject.SetActive) cannot be removed and were made inert" : ""));
		}

		/// <summary>
		/// Makes the matching patch methods do nothing (a skip prefix on the patch method itself, the way PerfPatches quiets
		/// Binarizer): a void patch returns at once, a bool prefix returns true (run the original). Other return types are left.
		/// </summary>
		private static int Neutralize(Func<Patch, bool> match)
		{
			if (_neutralizer == null)
			{
				_neutralizer = new Harmony("lom.ui.english.neutralize");
			}
			int n = 0;
			foreach (MethodBase m in Harmony.GetAllPatchedMethods().ToList())
			{
				Patches info = Harmony.GetPatchInfo(m);
				if (info == null)
				{
					continue;
				}
				foreach (Patch p in info.Prefixes.Concat(info.Postfixes).Concat(info.Finalizers).ToList())
				{
					MethodInfo pm = p.PatchMethod;
					if (pm == null || !match(p) || _neutralized.Contains(pm))
					{
						continue;
					}
					string skip = (pm.ReturnType == typeof(void)) ? nameof(SkipVoid) : ((pm.ReturnType == typeof(bool)) ? nameof(SkipBool) : null);
					if (skip == null)
					{
						continue;
					}
					try
					{
						_neutralizer.Patch(pm, new HarmonyMethod(typeof(OriginalMod), skip)
						{
							priority = Priority.First
						});
						_neutralized.Add(pm);
						n++;
					}
					catch (Exception ex)
					{
						Plugin.Log.LogWarning("could not neutralize " + pm.DeclaringType?.FullName + "." + pm.Name + ": " + ex.Message);
					}
				}
			}
			return n;
		}

		private static bool SkipVoid()
		{
			return false;
		}

		private static bool SkipBool(ref bool __result)
		{
			__result = true;
			return false;
		}

		/// <summary>Patches of these owners are still registered but made inert (native-method patches that cannot be removed).</summary>
		public static int NeutralizedCount => _neutralized.Count;

		/// <summary>[Dev] DetachBaseMod: simulates an uninstalled base patch without touching its files.</summary>
		private static void Detach()
		{
			int owners = 0;
			HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal)
			{
				XUnityHarmony,
				KrGuid
			};
			foreach (MethodBase m in Harmony.GetAllPatchedMethods().ToList())
			{
				Patches info = Harmony.GetPatchInfo(m);
				if (info == null)
				{
					continue;
				}
				foreach (Patch p in info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers))
				{
					string asm = p.PatchMethod?.DeclaringType?.Assembly.GetName().Name;
					if (asm == "FunctionalPlugin_Binarizer" || asm == LlmKitAssembly || (asm != null && (asm.StartsWith("XUnity.", StringComparison.Ordinal) || asm.StartsWith(BaseGuards.PackGuid, StringComparison.Ordinal))) || asm == "LOM_UI_Plugin_KR")
					{
						ids.Add(p.owner);
					}
				}
			}
			ids.Remove(Plugin.GUID);
			ids.Remove(StringsPlugin.GUID);
			foreach (string id in ids)
			{
				if (Harmony.HasAnyPatches(id))
				{
					Harmony.UnpatchID(id);
					owners++;
				}
			}
			// HarmonyX cannot unpatch native Unity methods (GameObject.SetActive, TextMesh.text): make those patch methods inert.
			int inert = Neutralize((Patch p) => ids.Contains(p.owner));
			if (inert > 0)
			{
				Plugin.Log.LogInfo($"[Dev] DetachBaseMod: {inert} patches on native Unity methods could not be removed and were made inert");
			}
			_krSuppressed = true;
			foreach (string guid in new string[4] { BinarizerGuid, LlmKitGuid, XUnityGuid, RedirectorGuid })
			{
				try
				{
					IDictionary<string, PluginInfo> plugins = Loader.Plugins;
					PluginInfo pi;
					if (plugins != null && plugins.TryGetValue(guid, out pi) && pi?.Instance is Behaviour b && b != null)
					{
						b.enabled = false;
					}
				}
				catch (Exception)
				{
				}
			}
			try
			{
				// The chainloader's XUnity entry is only the BepInEx wrapper; the component that runs is AutoTranslationPlugin.Current.
				Type xua = TranslationProfiles.FindType("XUnity.AutoTranslator.Plugin.Core", "XUnity.AutoTranslator.Plugin.Core.AutoTranslationPlugin");
				if (xua != null && AccessTools.Field(xua, "Current")?.GetValue(null) is Behaviour running && running != null)
				{
					running.enabled = false;
				}
			}
			catch (Exception)
			{
			}
			try
			{
				Type addressables = TranslationProfiles.FindType("Unity.Addressables", "UnityEngine.AddressableAssets.Addressables");
				FieldInfo modPaths = (addressables == null) ? null : AccessTools.Field(addressables, "ModPaths");
				if (modPaths != null && !modPaths.FieldType.IsValueType)
				{
					modPaths.SetValue(null, null);
				}
			}
			catch (Exception)
			{
			}
			_detached = true;
			Plugin.Log.LogWarning($"[Dev] DetachBaseMod: removed the Harmony patches of {owners} base-patch owner(s) and disabled its plugins; restart with the setting off to restore them");
		}

		public static string Describe(Report r)
		{
			string s;
			switch (r.Summary)
			{
			case BaseSummary.Absent:
				s = TranslationProfiles.OwnComplete ? "not installed (optional: this mod's own translation is complete)" : "not installed (only this mod's own lines are in English)";
				break;
			case BaseSummary.Complete:
				s = "installed and running";
				break;
			default:
				s = "partly installed";
				break;
			}
			s += $" (tables on disk: {BaseTable.Describe()}; Binarizer {Flags(r.BinarizerInstalled, r.BinarizerLoaded, r.BinarizerHooked)}, {((r.BinarizerEntries >= 0) ? (r.BinarizerEntries + " keys") : "no table")}, data {r.BinarizerData}{(string.IsNullOrEmpty(r.BinarizerVote) ? "" : (" [" + r.BinarizerVote + "]"))}";
			if (r.LlmKitInstalled || r.LlmKitLoaded || r.LlmKitHooked)
			{
				s += $"; its plugin {Flags(r.LlmKitInstalled, r.LlmKitLoaded, r.LlmKitHooked)}{(r.LlmKitOtherBepInEx ? " (built for another BepInEx)" : "")}, {((r.LlmKitEntries >= 0) ? (r.LlmKitEntries + " keys") : "no table")}, data {r.LlmKitData}{(string.IsNullOrEmpty(r.LlmKitVote) ? "" : (" [" + r.LlmKitVote + "]"))}{(r.LlmKitLabelsHooked ? ", label hook" : "")}";
			}
			if (r.PackInstalled || r.PackLoaded)
			{
				s += $"; its plugin pack {Flags(r.PackInstalled, r.PackLoaded, r.PackLoaded)}{(r.PackOtherBepInEx ? " (built for another BepInEx)" : "")}";
			}
			s += $"; XUnity {Flags(r.XUnityInstalled, r.XUnityRunning, r.XUnityHooked)}, data {r.XUnityData}{(string.IsNullOrEmpty(r.XUnityVote) ? "" : (" [" + r.XUnityVote + "]"))}; KR {Flags(r.KrInstalled, r.KrLoaded, r.KrHooked)}{(r.KrSuppressed ? " suppressed" : "")})";
			List<string> d = r.Diagnoses();
			if (d.Count > 0)
			{
				s += ": " + string.Join("; ", d);
			}
			if (ManifestError != null)
			{
				s += " [" + ManifestError + "]";
			}
			return s;
		}

		private static string Flags(bool installed, bool loaded, bool hooked)
		{
			if (!installed && !loaded)
			{
				return "absent";
			}
			return (installed ? "installed" : "not on disk") + (loaded ? "/loaded" : "") + (hooked ? "/hooked" : "/not hooked");
		}
	}
}
