using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Lean.Localization;
using Mortal.Core;

namespace LOM_UI_EN
{
	/// <summary>
	/// The game-data text layer: every LocaleResolver.GetString(key) lookup (items, martial arts, stats, names, menus, story lines
	/// through GetStoryText) answered from a StringTable.csv, which is what Binarizer's GetStringRedirect did for the English patch.
	/// Same semantics as Binarizer: key-only and ordinal, the raw value (no LeanTranslation.FormatText), first row wins, a miss
	/// falls through to the game's own (Chinese) Lean data. The prefix runs at Priority.Low, after the Normal-priority prefix of
	/// the English patch's plugin (Binarizer, or since the patch's llmkit-upgrade its own FanslationStudio.LegendOfMortal.Plugin):
	/// HarmonyX runs every prefix and skips the original when any returned false, so our value is the one written whenever both
	/// run, and a key we lack keeps the patch's. PerfPatches additionally skips Binarizer's body while we serve.
	/// </summary>
	public static class TextTable
	{
		public sealed class Table
		{
			public readonly Dictionary<string, string> Map = new Dictionary<string, string>(80000, StringComparer.Ordinal);
			public string Path;
			public string Label;
			public int Rows;
			public int Duplicates;
			public int Repaired;
			public int SkippedNonKey;
			public int Empty;
			public int UnbalancedBraces;
			public int WithCjk;
			public long LoadMs;
			public readonly List<string> Warnings = new List<string>();
			/// <summary>Overlay tables: the original patch's table read underneath (null when not found: only this mod's rows are in):
			/// its StringTable.csv, or for the per-file layout its folder.</summary>
			public string BasePath;
			public BaseTableLayout BaseLayout;
			public int BaseCount;
			/// <summary>Overlay tables: this mod's rows, how many replaced a base row, and how many keys the base lacked.</summary>
			public int OverlayCount;
			public int Replaced;
			public int Added;
			/// <summary>Overlay tables: the base table's own values of the keys asked for (for OriginalMod's data check).</summary>
			public readonly Dictionary<string, string> BaseSample = new Dictionary<string, string>(StringComparer.Ordinal);
			/// <summary>Overlay tables: the original patch's current line for every key this mod's row replaced with different text (what a fallback shows).</summary>
			public readonly Dictionary<string, string> BaseValues = new Dictionary<string, string>(StringComparer.Ordinal);
			/// <summary>Overlay tables: per revised row, hashes of the game's Chinese and of the original patch's line it was written against (StringTable.meta.tsv).</summary>
			public readonly Dictionary<string, RowRecord> Records = new Dictionary<string, RowRecord>(StringComparer.Ordinal);
			public readonly FallbackState Fallback = new FallbackState();

			public int Count => Map.Count;
		}

		/// <summary>What one revised row was written against, as OriginalMod.TextHash values (null = not recorded).</summary>
		public sealed class RowRecord
		{
			public string Source;
			public string Base;
		}

		/// <summary>
		/// Fallback state of one table: decisions, counts and the report list are per table, so a switch never serves the old
		/// table's choice and switching back finds its own counts. ClearDecisions bumps a generation that every state checks.
		/// </summary>
		public sealed class FallbackState
		{
			public readonly Dictionary<string, string> Decided = new Dictionary<string, string>(StringComparer.Ordinal);
			public readonly Dictionary<string, bool> OverrideOk = new Dictionary<string, bool>(StringComparer.Ordinal);
			public readonly List<string> Log = new List<string>();
			public int Changed;
			public int Placeholders;
			public int Stale;
			public int OverridesSkipped;
			public int Generation;
			/// <summary>Null until the records were checked against the game's text; false when they do not belong to this game/language.</summary>
			public bool? RecordsUsable;
			public string RecordsNote;
			/// <summary>From the record's header: the game build it was taken on.</summary>
			public string RecordsGame;
			/// <summary>Keys of the records that carry a game-text hash (the ones the game-update check can use).</summary>
			public readonly List<string> HashedKeys = new List<string>();
			public int NextSampleTick;
		}

		private static int _generation;
		private static readonly List<string> _noLog = new List<string>();

		/// <summary>The served table's fallback state, reset first if ClearDecisions ran since it was last used.</summary>
		private static FallbackState State(Table t)
		{
			FallbackState s = t.Fallback;
			lock (s)
			{
				if (s.Generation != _generation)
				{
					s.Generation = _generation;
					s.Decided.Clear();
					s.OverrideOk.Clear();
					s.Log.Clear();
					s.Changed = 0;
					s.Placeholders = 0;
					s.Stale = 0;
					s.OverridesSkipped = 0;
					s.RecordsUsable = null;
					s.RecordsNote = null;
					s.NextSampleTick = 0;
				}
			}
			return s;
		}

		private static Table _active;

		/// <summary>[Translation] FallBackToOriginal: a revised row that no longer fits the game shows the original patch's line.</summary>
		public static ConfigEntry<bool> CfgFallback;

		public static int FellBackChanged => (_active != null) ? State(_active).Changed : 0;
		public static int FellBackPlaceholders => (_active != null) ? State(_active).Placeholders : 0;
		public static int StaleKept => (_active != null) ? State(_active).Stale : 0;
		public static int OverridesSkipped => (_active != null) ? State(_active).OverridesSkipped : 0;
		/// <summary>Keys of the served table that fell back or are out of date, with the reason, for the report (a copy).</summary>
		public static List<string> FallbackLog
		{
			get
			{
				if (_active == null) return _noLog;
				FallbackState s = State(_active);
				lock (s) { return new List<string>(s.Log); }
			}
		}
		/// <summary>Records of the served revised table that carry a game-text hash.</summary>
		public static int HashedRecords => (_active != null) ? _active.Fallback.HashedKeys.Count : 0;
		private static readonly Regex Placeholder = new Regex("\\{(\\d+)(?::[^{}]*)?\\}|\\{\\$([A-Za-z0-9_]+)\\}", RegexOptions.CultureInvariant);

		public static long Served;
		public static long Missed;

		/// <summary>The table this layer serves, or null while it is handed back / off.</summary>
		public static Table Active => _active;

		/// <summary>This layer answers lookups right now. False while its hook is missing or turned off: the original patch's Binarizer (if installed) then answers on its own.</summary>
		public static bool Serving => _active != null && F.GameText.Live && EnglishLanguage.Active;

		public static bool Hooked { get; private set; }

		/// <summary>Keys the game asked for that neither this table nor the original patch had, while the game's own text was Chinese (logged once each).</summary>
		public static int Gaps;

		[ThreadStatic]
		private static string _missKey;

		private static readonly HashSet<string> _gapKeys = new HashSet<string>(StringComparer.Ordinal);

		public static void Patch(Harmony h)
		{
			Compat.Setup(F.GameText, delegate
			{
				MethodInfo target = AccessTools.Method(typeof(LeanLocalizationResolver), "GetString", new Type[1] { typeof(string) });
				Hooked = Compat.Hook(F.GameText, h, target, "LeanLocalizationResolver.GetString(string)", new HarmonyMethod(typeof(TextTable), nameof(GetString_Prefix))
				{
					priority = Priority.Low
				}, new HarmonyMethod(typeof(TextTable), nameof(GetString_Postfix))
				{
					priority = Priority.Last
				}, essential: true);
			});
			// The fallback reads the game's own text from Lean; by name, so a Lean change costs only the fallback.
			Compat.Setup(F.Fallback, delegate
			{
				F.Fallback.OnTurnedOn = ClearDecisions;
				Compat.Require(F.Fallback, "LeanLocalization", "Lean.Localization.LeanLocalization", "GetTranslation");
				Compat.Require(F.Fallback, "LeanLocalization", "Lean.Localization.LeanLocalization", "CurrentTranslations");
				Compat.Require(F.Fallback, "LeanLocalization", "Lean.Localization.LeanTranslation", "Data");
			});
		}

		public static void Use(Table t)
		{
			// Each table keeps its own decisions and counts, so re-applying or switching back loses nothing.
			_active = t;
		}

		/// <summary>After [Translation] FallBackToOriginal, a wording-fix reload or Try again: every table decides every key again.</summary>
		public static void ClearDecisions()
		{
			System.Threading.Interlocked.Increment(ref _generation);
		}

		/// <summary>
		/// Harness test only: make one revised row of the active table look out of date (game text changed, and optionally the
		/// original patch's line too) or give it a value, then decide it again. Returns what the key serves now. Changes the
		/// loaded table until the next reload.
		/// </summary>
		public static string TestFallback(string key, string mode, string value)
		{
			Table t = _active;
			if (t == null || key == null)
			{
				return "no table";
			}
			FallbackState st = State(t);
			lock (st)
			{
				if (mode == "value")
				{
					t.Map[key] = value ?? "";
				}
				else
				{
					string baseValue;
					t.BaseValues.TryGetValue(key, out baseValue);
					// "stale": the game text differs from the record, the patch line is the one revised against.
					// "newer": the game text differs and the patch line differs too (updated for the new text).
					t.Records[key] = new RowRecord
					{
						Source = "0000000000000000",
						Base = (mode == "newer") ? "0000000000000000" : OriginalMod.TextHash(baseValue)
					};
				}
				st.Decided.Remove(key);
				st.OverrideOk.Remove(key);
				st.RecordsUsable = true;
			}
			if (GameSource(key) == null)
			{
				return "the game's own text for " + key + " cannot be read";
			}
			string served;
			return Resolve(t, key, out served) ? served : "(no row)";
		}

		public static bool TryGet(string key, out string value)
		{
			Table t = _active;
			if (t == null || key == null)
			{
				value = null;
				return false;
			}
			return Resolve(t, key, out value);
		}

		private static bool FallbackOn => CfgFallback != null && CfgFallback.Value && F.Fallback.Live;

		/// <summary>
		/// The value served for a key: this mod's row, unless the fallback mode is on, the original patch has a different line for
		/// the key, and this mod's row no longer fits the game (Decide). Decided once per key and table, once the game's text is known.
		/// </summary>
		private static bool Resolve(Table t, string key, out string value)
		{
			if (!t.Map.TryGetValue(key, out value))
			{
				return false;
			}
			string baseValue;
			if (t.BaseValues.Count == 0 || !FallbackOn || !t.BaseValues.TryGetValue(key, out baseValue))
			{
				return true;
			}
			FallbackState st = State(t);
			int gen;
			lock (st)
			{
				gen = st.Generation;
				string decided;
				if (st.Decided.TryGetValue(key, out decided))
				{
					value = decided;
					return true;
				}
			}
			bool cache;
			int kind;
			string served = Decide(t, key, value, baseValue, out cache, out kind);
			if (cache)
			{
				lock (st)
				{
					// Several threads may decide the same key at once: the first to store it counts it.
					if (st.Generation == gen && !st.Decided.ContainsKey(key))
					{
						st.Decided[key] = served;
						Count(st, key, kind);
					}
				}
			}
			value = served;
			return true;
		}

		private const int KindNone = 0;
		private const int KindPlaceholders = 1;
		private const int KindChanged = 2;
		private const int KindStale = 3;

		/// <summary>Called under the state's lock.</summary>
		private static void Count(FallbackState st, string key, int kind)
		{
			string why;
			switch (kind)
			{
			case KindPlaceholders:
				st.Placeholders++;
				why = "this mod's line uses placeholders the game's text no longer has";
				break;
			case KindChanged:
				st.Changed++;
				why = "game text changed; the original patch's line differs from the one revised (updated for it)";
				break;
			case KindStale:
				st.Stale++;
				why = "game text changed; no newer line from the original patch (this mod's revision kept, needs revising)";
				break;
			default:
				return;
			}
			if (st.Log.Count < 5000)
			{
				st.Log.Add(key + "\t" + why);
			}
		}

		/// <summary>
		/// A revised row falls back to the original patch's line when it uses a format placeholder ({0}, {$var}) the game's text
		/// no longer has (it would break the line), or when the game's own Chinese for the key has changed since the row was
		/// written (a game update) and the patch's line has changed too (updated for the new text). A patch line that only changed
		/// its wording does not replace a revision; a changed game text with no newer patch line keeps the revision and is listed
		/// as out of date. Not decided (and not cached) while the game's text cannot be read.
		/// </summary>
		private static string Decide(Table t, string key, string ours, string baseValue, out bool cache, out int kind)
		{
			cache = false;
			kind = KindNone;
			string source = GameSource(key);
			if (source == null)
			{
				return ours;
			}
			if (!PlaceholdersFit(ours, source) && PlaceholdersFit(baseValue, source))
			{
				cache = true;
				kind = KindPlaceholders;
				return baseValue;
			}
			RowRecord rec;
			if (!t.Records.TryGetValue(key, out rec) || rec.Source == null)
			{
				cache = true;
				return ours;
			}
			bool? usable = RecordsUsable(t);
			if (!usable.HasValue)
			{
				// The record cannot be judged yet (the game's text not filled in): decide again on a later lookup.
				return ours;
			}
			cache = true;
			if (usable.Value && OriginalMod.TextHash(source) != rec.Source)
			{
				if (rec.Base == null || !OriginalMod.SameText(baseValue, rec.Base))
				{
					kind = KindChanged;
					return baseValue;
				}
				kind = KindStale;
			}
			return ours;
		}

		/// <summary>
		/// The revision record is trusted only when it matches this game's text for most rows: a record taken in another language
		/// or garbled would otherwise send every revised row to the original patch. Checked once per table, on a sample.
		/// </summary>
		private static bool? RecordsUsable(Table t)
		{
			FallbackState st = State(t);
			List<string> hashed = t.Fallback.HashedKeys;
			int gen;
			lock (st)
			{
				gen = st.Generation;
				if (st.RecordsUsable.HasValue)
				{
					return st.RecordsUsable;
				}
				if (hashed.Count == 0)
				{
					st.RecordsUsable = false;
					return false;
				}
				if (st.NextSampleTick != 0 && unchecked(Environment.TickCount - st.NextSampleTick) < 0)
				{
					return null;
				}
			}
			// An even sample across every hashed record (a game patch touches chapters, so the first rows are no sample).
			int step = Math.Max(1, hashed.Count / 400);
			int readable = 0;
			int differ = 0;
			for (int i = 0; i < hashed.Count; i += step)
			{
				string s = GameSource(hashed[i]);
				if (s == null)
				{
					continue;
				}
				readable++;
				RowRecord rec;
				if (t.Records.TryGetValue(hashed[i], out rec) && OriginalMod.TextHash(s) != rec.Source)
				{
					differ++;
				}
			}
			int needed = Math.Min(50, Math.Max(1, ((hashed.Count + step - 1) / step) / 2));
			if (readable < needed)
			{
				lock (st)
				{
					// Too early (Lean not filled yet): look again in a few seconds, meanwhile decide nothing from the record.
					if (st.Generation == gen) st.NextSampleTick = unchecked(Environment.TickCount + 3000);
				}
				return null;
			}
			// After a game update most rows still match (a text-only patch keeps the code fingerprint, so the record's game= header
			// cannot tell); a record where most rows differ (70%+) was taken in another language or is garbled.
			bool usable = differ * 10 < readable * 7;
			lock (st)
			{
				if (st.Generation != gen) return null;
				st.RecordsUsable = usable;
				st.RecordsNote = usable ? null : ("the revision record does not match this game's text (" + differ + " of " + readable + " sampled rows differ): it belongs to another game build or language, so only the placeholder check runs");
			}
			if (!usable)
			{
				Plugin.Log?.LogWarning("fallback: " + st.RecordsNote);
			}
			return usable;
		}

		/// <summary>Why the fallback cannot use the revision record, or null.</summary>
		public static string RecordsProblem => (_active != null) ? State(_active).RecordsNote : null;

		/// <summary>
		/// LOM_Strings_EN: a wording fix for a key replaces the served text, so it gets the same protection: with the fallback on it
		/// is skipped when it uses a placeholder the game's text no longer has, or when the key's revision record says the game text
		/// changed since (then the table's decision stands).
		/// </summary>
		public static bool OverrideFits(string key, string text)
		{
			Table t = _active;
			if (t == null || key == null || text == null || !FallbackOn)
			{
				return true;
			}
			FallbackState st = State(t);
			int gen;
			lock (st)
			{
				gen = st.Generation;
				bool ok;
				if (st.OverrideOk.TryGetValue(key, out ok))
				{
					return ok;
				}
			}
			string source = GameSource(key);
			if (source == null)
			{
				return true;
			}
			bool fits = PlaceholdersFit(text, source);
			RowRecord rec;
			if (fits && t.Records.TryGetValue(key, out rec) && rec.Source != null)
			{
				bool? usable = RecordsUsable(t);
				if (!usable.HasValue)
				{
					// Not judged yet: apply the wording fix for now and look again later.
					return true;
				}
				if (usable.Value && OriginalMod.TextHash(source) != rec.Source)
				{
					fits = false;
				}
			}
			lock (st)
			{
				if (st.Generation == gen && !st.OverrideOk.ContainsKey(key))
				{
					st.OverrideOk[key] = fits;
					if (!fits)
					{
						st.OverridesSkipped++;
						if (st.Log.Count < 5000)
						{
							st.Log.Add(key + "\twording fix skipped: it no longer fits the game's text");
						}
					}
				}
			}
			return fits;
		}

		/// <summary>Every {0}-style index and {$var} of the line is one the game's text also has.</summary>
		private static bool PlaceholdersFit(string line, string source)
		{
			if (string.IsNullOrEmpty(line) || line.IndexOf('{') < 0)
			{
				return true;
			}
			HashSet<string> have = new HashSet<string>(StringComparer.Ordinal);
			foreach (Match m in Placeholder.Matches(source ?? ""))
			{
				have.Add(m.Groups[1].Success ? ("#" + m.Groups[1].Value) : ("$" + m.Groups[2].Value));
			}
			foreach (Match m in Placeholder.Matches(line))
			{
				if (!have.Contains(m.Groups[1].Success ? ("#" + m.Groups[1].Value) : ("$" + m.Groups[2].Value)))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>The game's own (Chinese) text for a key, straight from Lean's current translations (no hook runs); null if unknown.</summary>
		public static string GameSource(string key)
		{
			if (key == null || !F.Fallback.Live)
			{
				return null;
			}
			try
			{
				F.Fallback.Probe();
				return GameSourceCore(key);
			}
			catch (Exception ex)
			{
				// A Lean change in a game update turns the fallback off (and says so on the Compatibility page), not the text.
				F.Fallback.Fail(ex, "reading the game's own text");
				return null;
			}
		}

		/// <summary>
		/// The game's own text per key as Lean last built it (TakeSnapshot, after every LeanLocalization.RegisterAndBuild, which
		/// rebuilds all translations on each UpdateTranslations pass before any label reads them). Since the English patch's
		/// llmkit-upgrade its own plugin writes its English into the shared LeanTranslation.Data on every GetTranslation (a postfix;
		/// BaseGuards holds it back while this mod's table serves, but not before and not while Original is handed back to it), so
		/// Data is no longer always the game's text. Null until hooked (the patch plugin not running) or built.
		/// </summary>
		private static Dictionary<string, string> _gameText;

		public static int GameTextCount => _gameText?.Count ?? 0;

		/// <summary>BaseGuards, when the patch plugin's GetTranslation postfix runs: keep the game's text from every Lean rebuild.</summary>
		public static bool HookSnapshot(Feature f, Harmony h)
		{
			return Compat.Hook(f, h, SnapshotTarget(), "LeanLocalization.RegisterAndBuild", null, new HarmonyMethod(typeof(TextTable), nameof(RegisterAndBuild_Postfix)));
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static MethodInfo SnapshotTarget()
		{
			return AccessTools.Method(typeof(LeanLocalization), "RegisterAndBuild");
		}

		private static void RegisterAndBuild_Postfix()
		{
			if (!F.BasePlugins.Live)
			{
				return;
			}
			try
			{
				F.BasePlugins.Probe();
				TakeSnapshot();
			}
			catch (Exception ex)
			{
				F.BasePlugins.Fail(ex, "keeping the game's text");
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void TakeSnapshot()
		{
			Dictionary<string, string> d = new Dictionary<string, string>(LeanLocalization.CurrentTranslations.Count, StringComparer.Ordinal);
			foreach (KeyValuePair<string, LeanTranslation> kv in LeanLocalization.CurrentTranslations)
			{
				string s = (kv.Value != null) ? (kv.Value.Data as string) : null;
				if (s != null)
				{
					d[kv.Key] = s;
				}
			}
			_gameText = d;
		}

		/// <summary>The game's text for a key: the snapshot, else Lean's current data read directly (GetTranslation would run the
		/// patch plugin's postfix, which writes its English into it).</summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static string GameSourceCore(string key)
		{
			Dictionary<string, string> snap = _gameText;
			string s;
			if (snap != null && snap.TryGetValue(key, out s))
			{
				return s;
			}
			LeanTranslation tr;
			return (LeanLocalization.CurrentTranslations.TryGetValue(key, out tr) && tr != null) ? (tr.Data as string) : null;
		}

		/// <summary>The harness's srcdump: every key of Lean's current translations with its text (the game's Chinese), from the
		/// snapshot when there is one.</summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static IEnumerable<KeyValuePair<string, string>> GameSources()
		{
			List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
			Dictionary<string, string> snap = _gameText;
			if (snap != null && snap.Count > 0)
			{
				list.AddRange(snap);
				return list;
			}
			foreach (KeyValuePair<string, LeanTranslation> kv in LeanLocalization.CurrentTranslations)
			{
				string s = (kv.Value != null) ? (kv.Value.Data as string) : null;
				if (s != null)
				{
					list.Add(new KeyValuePair<string, string>(kv.Key, s));
				}
			}
			return list;
		}

		private static bool GetString_Prefix(string __0, ref string __result)
		{
			string key = __0;
			_missKey = null;
			if (!F.GameText.Live)
			{
				return true;
			}
			try
			{
				F.GameText.Probe();
				Table t = _active;
				if (t == null || key == null || !EnglishLanguage.Active)
				{
					return true;
				}
				string value;
				if (Resolve(t, key, out value))
				{
					__result = value;
					Served++;
					return false;
				}
				// A key neither this mod's rows nor the original patch's table has (new game content after an update, say): the original patch's plugin, whose prefix ran
				// before this one, may have answered already; otherwise the game's own text.
				Missed++;
				_missKey = key;
				return true;
			}
			catch (Exception ex)
			{
				F.GameText.Fail(ex, "GetString");
				return true;
			}
		}

		/// <summary>After every other patch: a key nobody translated whose game text is Chinese is a gap worth logging for translation.</summary>
		private static void GetString_Postfix(string __0, string __result)
		{
			string key = __0;
			string miss = _missKey;
			_missKey = null;
			if (miss == null || !ReferenceEquals(miss, key) || string.IsNullOrEmpty(__result))
			{
				return;
			}
			try
			{
				Table t = _active;
				if (t == null || t.BasePath == null)
				{
					// Without the original patch's table every key this mod did not revise would count as a gap.
					return;
				}
				bool add;
				lock (_gapKeys)
				{
					add = SceneDictionary.IsTranslatableChinese(__result) && !StringOverrides.TryGet(key, out _) && _gapKeys.Count < 20000 && _gapKeys.Add(key);
				}
				if (add)
				{
					System.Threading.Interlocked.Increment(ref Gaps);
					SceneText.LogGap("[game data text]", key, __result);
				}
			}
			catch (Exception)
			{
			}
		}

		/// <summary>
		/// Reads a StringTable.csv the way Binarizer does (its parser is Ideafixxxer.CsvParser from the game's Fungus.dll; IdeaCsv is
		/// a copy of it, checked identical on the full tables, so a game update to Fungus.dll cannot stop the table loading), so values
		/// come out identical, CRLF inside multi-line values included. Differences from Binarizer, all logged: a row with more than
		/// two fields is re-joined (the ten {punch=...} story rows that every parser cuts at the comma inside the tag), and the
		/// LeanPhrase dumps that are not string keys (TextFont, Image_*, TextMeshFont_*) are skipped.
		/// </summary>
		public static Table Load(string path, string label)
		{
			Table t = new Table
			{
				Path = path,
				Label = label
			};
			Stopwatch sw = Stopwatch.StartNew();
			string[][] rows = IdeaCsv.Parse(SceneDictionary.ReadShared(path));
			foreach (string[] row in rows)
			{
				t.Rows++;
				if (row == null || row.Length < 2 || string.IsNullOrEmpty(row[0]))
				{
					continue;
				}
				string key = row[0];
				if (key.IndexOf('/') < 0 && (key == "TextFont" || key.StartsWith("Image_", StringComparison.Ordinal) || key.StartsWith("TextMeshFont_", StringComparison.Ordinal)))
				{
					t.SkippedNonKey++;
					continue;
				}
				if (t.Map.ContainsKey(key))
				{
					t.Duplicates++;
					continue;
				}
				string value = row[1];
				if (row.Length > 2)
				{
					value = string.Join(",", row, 1, row.Length - 1);
					t.Repaired++;
					if (t.Warnings.Count < 20)
					{
						t.Warnings.Add("row " + key + " had " + row.Length + " fields; joined");
					}
				}
				if (value.Length == 0)
				{
					t.Empty++;
				}
				else
				{
					if (!BracesBalanced(value))
					{
						t.UnbalancedBraces++;
					}
					if (SceneDictionary.IsTranslatableChinese(value))
					{
						t.WithCjk++;
					}
				}
				t.Map.Add(key, value);
			}
			t.LoadMs = sw.ElapsedMilliseconds;
			return t;
		}

		/// <summary>
		/// The original patch's own table as its plugin serves it: the one StringTable.csv the way Binarizer reads it (Load), or the
		/// llmkit-upgrade per-file tables the way that patch's own plugin reads them (every file in turn, a later row of a key
		/// replacing an earlier one; LlmKitCsv). The same non-key phrases are skipped either way.
		/// </summary>
		public static Table LoadBase(BaseTableSet set, string label)
		{
			if (set == null || !set.Exists)
			{
				return null;
			}
			if (set.Layout != BaseTableLayout.PerFile)
			{
				return Load(set.Files[0], label);
			}
			Table t = new Table
			{
				Path = set.Dir,
				Label = label
			};
			Stopwatch sw = Stopwatch.StartNew();
			foreach (string file in set.Files)
			{
				try
				{
					foreach (KeyValuePair<string, string> kv in LlmKitCsv.Read(file))
					{
						t.Rows++;
						string key = kv.Key;
						if (key.IndexOf('/') < 0 && (key == "TextFont" || key.StartsWith("Image_", StringComparison.Ordinal) || key.StartsWith("TextMeshFont_", StringComparison.Ordinal)))
						{
							t.SkippedNonKey++;
							continue;
						}
						if (t.Map.ContainsKey(key))
						{
							// The game's own sources repeat a few keys (Story/undefine); the plugin keeps the last row.
							t.Duplicates++;
						}
						t.Map[key] = kv.Value;
					}
				}
				catch (Exception ex)
				{
					// One unreadable file costs its rows only (the plugin fails its whole load then).
					if (t.Warnings.Count < 20)
					{
						t.Warnings.Add(System.IO.Path.GetFileName(file) + ": " + ex.Message);
					}
				}
			}
			Lint(t);
			t.LoadMs = sw.ElapsedMilliseconds;
			return t;
		}

		/// <summary>The lint counts over what the table serves now.</summary>
		private static void Lint(Table t)
		{
			t.Empty = 0;
			t.UnbalancedBraces = 0;
			t.WithCjk = 0;
			foreach (string value in t.Map.Values)
			{
				if (value.Length == 0)
				{
					t.Empty++;
				}
				else
				{
					if (!BracesBalanced(value))
					{
						t.UnbalancedBraces++;
					}
					if (SceneDictionary.IsTranslatableChinese(value))
					{
						t.WithCjk++;
					}
				}
			}
		}

		/// <summary>
		/// The original patch's table with this mod's rows laid over it: a key this mod has takes its value, keys the base lacks are
		/// added, everything else is the base's own row. Without the base table, only this mod's rows are in.
		/// </summary>
		public static Table LoadOverlay(BaseTableSet baseSet, string overlayPath, string label, IEnumerable<string> sampleKeys = null)
		{
			Stopwatch sw = Stopwatch.StartNew();
			bool haveBase = baseSet != null && baseSet.Exists;
			bool haveOverlay = !string.IsNullOrEmpty(overlayPath) && File.Exists(overlayPath);
			Table t = (haveBase ? LoadBase(baseSet, label) : null) ?? new Table
			{
				Path = overlayPath,
				Label = label
			};
			t.Label = label;
			if (haveBase)
			{
				t.BasePath = baseSet.Where;
				t.BaseLayout = baseSet.Layout;
				t.BaseCount = t.Map.Count;
				if (sampleKeys != null)
				{
					foreach (string k in sampleKeys)
					{
						string v;
						if (k != null && t.Map.TryGetValue(k, out v))
						{
							t.BaseSample[k] = v;
						}
					}
				}
			}
			if (haveOverlay)
			{
				Table o = Load(overlayPath, label);
				t.Path = overlayPath;
				t.OverlayCount = o.Count;
				t.Rows += o.Rows;
				t.Duplicates += o.Duplicates;
				t.Repaired += o.Repaired;
				t.SkippedNonKey += o.SkippedNonKey;
				foreach (string w in o.Warnings)
				{
					if (t.Warnings.Count < 20)
					{
						t.Warnings.Add(w);
					}
				}
				foreach (KeyValuePair<string, string> kv in o.Map)
				{
					string old;
					if (t.Map.TryGetValue(kv.Key, out old))
					{
						t.Replaced++;
						if (haveBase && !string.Equals(old, kv.Value, StringComparison.Ordinal))
						{
							t.BaseValues[kv.Key] = old;
						}
					}
					else
					{
						t.Added++;
					}
					t.Map[kv.Key] = kv.Value;
				}
				LoadRecords(t, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(overlayPath), System.IO.Path.GetFileNameWithoutExtension(overlayPath) + ".meta.tsv"));
				// The lint counts describe what is served now.
				Lint(t);
			}
			t.LoadMs = sw.ElapsedMilliseconds;
			return t;
		}

		/// <summary>StringTable.meta.tsv: key TAB game-text hash TAB original-line hash ('-' = not recorded), one line per revised row.</summary>
		private static void LoadRecords(Table t, string path)
		{
			if (!File.Exists(path))
			{
				return;
			}
			try
			{
				foreach (string line in File.ReadAllLines(path))
				{
					if (line.Length == 0 || line[0] == '#')
					{
						int g = line.IndexOf("game=", StringComparison.Ordinal);
						if (g >= 0)
						{
							t.Fallback.RecordsGame = line.Substring(g + 5).Trim();
						}
						continue;
					}
					string[] f = line.Split('\t');
					if (f.Length < 3 || f[0].Length == 0)
					{
						continue;
					}
					RowRecord rec = new RowRecord
					{
						Source = (f[1] == "-" || f[1].Length == 0) ? null : f[1],
						Base = (f[2] == "-" || f[2].Length == 0) ? null : f[2]
					};
					t.Records[f[0]] = rec;
					if (rec.Source != null)
					{
						t.Fallback.HashedKeys.Add(f[0]);
					}
				}
			}
			catch (Exception ex)
			{
				t.Warnings.Add(System.IO.Path.GetFileName(path) + ": " + ex.Message);
			}
		}

		private static bool BracesBalanced(string s)
		{
			int depth = 0;
			foreach (char c in s)
			{
				if (c == '{')
				{
					depth++;
				}
				else if (c == '}' && --depth < 0)
				{
					return false;
				}
			}
			return depth == 0;
		}

		public static void LogStats(ManualLogSource log, Table t)
		{
			if (t.OverlayCount > 0 || t.BasePath != null)
			{
				log.LogInfo($"string table '{t.Label}': {t.Count} keys = " + ((t.BasePath != null) ? $"{t.BaseCount} from the original patch ({t.BasePath})" : "no original patch table (not installed)") + $" with this mod's {t.OverlayCount} rows over it ({t.Replaced} replaced, {t.Added} added) in {t.LoadMs} ms");
			}
			log.LogInfo($"string table '{t.Label}': {t.Count} keys from {System.IO.Path.GetFileName(t.Path)} in {t.LoadMs} ms ({t.Duplicates} duplicate rows ignored, {t.Repaired} rows re-joined, {t.SkippedNonKey} non-string phrases skipped; lint: {t.Empty} empty, {t.UnbalancedBraces} with unbalanced braces, {t.WithCjk} still containing Chinese)");
			foreach (string w in t.Warnings)
			{
				log.LogInfo("  " + w);
			}
		}
	}
}
