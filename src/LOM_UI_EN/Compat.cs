using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LOM_UI_EN
{
	public enum Health
	{
		Working,
		Partial,
		NotWorking,
		TurnedOff,
		NotNeeded
	}

	/// <summary>
	/// One feature of the mod as far as the game is concerned: the hooks and game members it depends on, whether they were found,
	/// and whether it keeps working at runtime. A feature whose game code changed turns itself off and says so (Mod Settings >
	/// Compatibility) instead of throwing into the game; every other feature keeps working.
	/// </summary>
	public sealed class Feature
	{
		public readonly string Id;
		public readonly string Name;
		public readonly string Group;
		/// <summary>What the player gets while this feature is not working.</summary>
		public readonly string Fallback;
		/// <summary>Game hooks the feature needs, and how many were installed.</summary>
		public int HooksWanted;
		public int HooksInstalled;
		/// <summary>Game members it reads by name, and how many were found.</summary>
		public int ChecksWanted;
		public int ChecksPassed;
		public int Wanted => HooksWanted + ChecksWanted;
		public int Installed => HooksInstalled + ChecksPassed;
		public readonly List<string> Problems = new List<string>();
		/// <summary>Parts declared by Require(part:) or Part(part:); when every declared part is broken the feature is not working.</summary>
		public readonly HashSet<string> DeclaredParts = new HashSet<string>(StringComparer.Ordinal);
		/// <summary>Undo what the feature changed in the game, run (deferred, outside the game's call) when it turns itself off.</summary>
		public Action OnTurnedOff;
		/// <summary>Redo it after Mod Settings > Compatibility > Try again turned the feature back on.</summary>
		public Action OnTurnedOn;
		/// <summary>Named parts that can fail on their own (quick save per scene); a failed part is listed in Problems too.</summary>
		public readonly Dictionary<string, string> BrokenParts = new Dictionary<string, string>(StringComparer.Ordinal);
		public string NotNeededWhy;
		public bool EssentialMissing;
		public bool Tripped;
		public string TripReason;
		public int Errors;
		public string FirstError;
		internal bool SimulateInstall;
		internal bool SimulateRuntime;
		/// <summary>Errors within 30 s that turn the feature off (a broken screen in the layout rules should not end them all).</summary>
		public int BurstLimit = 20;
		/// <summary>Errors in all that turn the feature off; low for features that run rarely (a hover, a key), so a broken one does not linger.</summary>
		public int TotalLimit = 200;
		private float[] _errorTimes;
		private int _errorIndex;

		public Feature(string id, string name, string group, string fallback)
		{
			Id = id;
			Name = name;
			Group = group;
			Fallback = fallback;
		}

		/// <summary>The feature may act: set up, nothing essential missing, not turned off by runtime errors, not simulated broken.</summary>
		public bool Live => !Tripped && !EssentialMissing && NotNeededWhy == null && (HooksWanted == 0 || HooksInstalled > 0) && !AllPartsBroken;

		private bool AllPartsBroken => DeclaredParts.Count > 0 && DeclaredParts.All((string p) => BrokenParts.ContainsKey(p));

		public Health Health
		{
			get
			{
				if (NotNeededWhy != null)
				{
					return Health.NotNeeded;
				}
				if (Tripped)
				{
					return Health.TurnedOff;
				}
				if (EssentialMissing || (HooksWanted > 0 && HooksInstalled == 0) || AllPartsBroken)
				{
					return Health.NotWorking;
				}
				if (HooksInstalled < HooksWanted || BrokenParts.Count > 0 || Problems.Count > 0 || Errors > 0)
				{
					return Health.Partial;
				}
				return Health.Working;
			}
		}

		public bool PartOk(string part)
		{
			return Live && !BrokenParts.ContainsKey(part);
		}

		/// <summary>One part stopped working at runtime (its game type changed); the rest of the feature carries on.</summary>
		public void BreakPart(string part, string why)
		{
			DeclaredParts.Add(part);
			if (BrokenParts.ContainsKey(part))
			{
				return;
			}
			BrokenParts[part] = why;
			Problems.Add(part + ": " + why);
			Plugin.Log?.LogWarning("[" + Id + "] part '" + part + "' turned off: " + why);
			if (!Live)
			{
				Trip("every part stopped working");
			}
		}

		/// <summary>Called at the top of a hook body inside its try: throws when [Dev] SimulateHookErrors names this feature.</summary>
		public void Probe()
		{
			if (SimulateRuntime)
			{
				throw new SimulatedGameChangeException(Id);
			}
		}

		/// <summary>A hook or tick of this feature threw. Structural errors (a game member gone) turn it off at once; others after 20 within 30 s or 200 in all.</summary>
		public void Fail(Exception ex, string where)
		{
			if (Tripped)
			{
				return;
			}
			Errors++;
			Exception inner = ex;
			while (inner is TargetInvocationException && inner.InnerException != null)
			{
				inner = inner.InnerException;
			}
			string msg = where + ": " + inner.GetType().Name + ": " + inner.Message;
			if (FirstError == null)
			{
				FirstError = msg;
			}
			if (Errors <= 3)
			{
				Plugin.Log?.LogError("[" + Id + "] " + ((Errors == 1) ? inner.ToString() : msg));
			}
			// Not Time.realtimeSinceStartup: Fail can be reached from a lookup on another thread, where Unity's clock throws.
			float now = (float)Compat.Clock.Elapsed.TotalSeconds;
			if (_errorTimes == null || _errorTimes.Length != Math.Max(2, BurstLimit))
			{
				_errorTimes = new float[Math.Max(2, BurstLimit)];
				_errorIndex = 0;
			}
			float oldest = _errorTimes[_errorIndex % _errorTimes.Length];
			_errorTimes[_errorIndex % _errorTimes.Length] = now;
			_errorIndex++;
			if (Compat.IsStructural(inner))
			{
				Trip("the game's code no longer matches (" + msg + ")");
			}
			else if (Errors >= _errorTimes.Length && now - oldest < 30f)
			{
				Trip(Errors + " errors in under 30 seconds, first: " + FirstError);
			}
			else if (Errors >= TotalLimit)
			{
				Trip(Errors + " errors, first: " + FirstError);
			}
		}

		public void Trip(string reason)
		{
			if (Tripped)
			{
				return;
			}
			Tripped = true;
			TripReason = reason;
			Plugin.Log?.LogError("[" + Id + "] " + Name + " turned itself off: " + reason + ". " + Fallback);
			Compat.OnTripped(this);
		}

		/// <summary>Mod Settings "Try again": clears runtime errors (install problems stay).</summary>
		public void Reset()
		{
			Tripped = false;
			TripReason = null;
			Errors = 0;
			FirstError = null;
			_errorIndex = 0;
			if (_errorTimes != null)
			{
				Array.Clear(_errorTimes, 0, _errorTimes.Length);
			}
		}

		public string StatusLine()
		{
			switch (Health)
			{
			case Health.NotNeeded:
				return "Not needed: " + NotNeededWhy + ".";
			case Health.TurnedOff:
				return "Turned itself off: " + TripReason + ". " + Fallback;
			case Health.NotWorking:
				return "Not working with this game version: " + string.Join("; ", Problems.Take(4)) + ". " + Fallback;
			case Health.Partial:
			{
				List<string> parts = new List<string>(Problems.Take(4));
				if (Problems.Count > 4)
				{
					parts.Add($"+{Problems.Count - 4} more");
				}
				if (Errors > 0)
				{
					parts.Add(Errors + ((Errors == 1) ? " error" : " errors") + " this session (first: " + FirstError + ")");
				}
				return "Partly working: " + string.Join("; ", parts) + ".";
			}
			default:
			{
				List<string> counts = new List<string>();
				if (HooksInstalled > 0)
				{
					counts.Add(HooksInstalled + ((HooksInstalled == 1) ? " game hook" : " game hooks"));
				}
				if (ChecksPassed > 0)
				{
					counts.Add(ChecksPassed + ((ChecksPassed == 1) ? " check" : " checks"));
				}
				return (counts.Count > 0) ? ("Working (" + string.Join(", ", counts) + ").") : "Working.";
			}
			}
		}
	}

	/// <summary>
	/// Every feature, in the order Mod Settings > Compatibility lists them. Created on first use (explicit static constructor,
	/// so not before Compat.Bind has read the [Dev] simulation settings in Plugin.Awake, which runs before LOM_Strings_EN).
	/// </summary>
	public static class F
	{
		public static readonly Feature GameText;
		public static readonly Feature SceneText;
		public static readonly Feature XUnityBridge;
		public static readonly Feature Wording;
		public static readonly Feature Fallback;
		public static readonly Feature Detector;
		public static readonly Feature Language;
		public static readonly Feature Layout;
		public static readonly Feature Horizontal;
		public static readonly Feature Numerals;
		public static readonly Feature LanguageCaption;
		public static readonly Feature CombatHover;
		public static readonly Feature AffinityHover;
		public static readonly Feature LineSpacing;
		public static readonly Feature TextSpeed;
		public static readonly Feature StoryLog;
		public static readonly Feature QuickSave;
		public static readonly Feature MenuButtons;
		public static readonly Feature QuietBinarizer;
		public static readonly Feature NameTips;
		public static readonly Feature Buildup;

		static F()
		{
			// Grouped like the Mod Settings pages that hold their switches.
			// The names and fallbacks are shown to players (notices, Mod Settings > Compatibility): plain words, no plugin internals.
			GameText = Compat.Register("gameText", "Game data text", "Translation", "The text of " + Plugin.BaseModName + ", or the game's own, shows instead.");
			SceneText = Compat.Register("sceneText", "Scene text", "Translation", "The text of " + Plugin.BaseModName + ", or the game's own, shows instead.");
			XUnityBridge = Compat.Register("xunityBridge", "Working with " + Plugin.BaseModName, "Translation", Plugin.BaseModName + " may translate some on-screen text again on its own.");
			Fallback = Compat.Register("fallback", "Fall back to " + Plugin.BaseModName, "Translation", "This mod's lines show even where a game update changed them.");
			QuietBinarizer = Compat.Register("quietBinarizer", "Quiet " + Plugin.BaseModName + " lookups", "Translation", Plugin.BaseModName + " logs every text lookup, which is slower.");
			Detector = Compat.Register("detector", "Detecting " + Plugin.BaseModName, "Translation", "This mod may not notice whether " + Plugin.BaseModName + " is installed.");
			Language = Compat.Register("language", "Game language check", "Translation", "This mod assumes the game's language is English.");
			Layout = Compat.Register("layout", "Layout fixes", "Localization", "Screens keep the game's own layout; long English may overflow.");
			Layout.BurstLimit = 100;
			Horizontal = Compat.Register("horizontal", "Horizontal text", "Localization", "Game-over and ending captions stay vertical.");
			LineSpacing = Compat.Register("lineSpacing", "Dialogue line spacing", "Localization", "Dialogue uses the game's line spacing.");
			CombatHover = Compat.Register("combatHover", "Duel card slide", "Localization", "Duel cards slide up as in the game.");
			Wording = Compat.Register("wording", "Wording fixes", "Localization", "Menus keep the translation's own wording.");
			Numerals = Compat.Register("numerals", "English numerals", "Localization", "Levels show as Chinese numerals.");
			LanguageCaption = Compat.Register("languageCaption", "Language menu caption", "Localization", "The language list may not update until you reopen it.");
			TextSpeed = Compat.Register("textSpeed", "Text speed", "Localization", "Dialogue types out at the game's own speed.");
			StoryLog = Compat.Register("storyLog", "Long dialogue logs", "Localization", "A long dialogue log may go blank again.");
			NameTips = Compat.Register("nameTips", "Name tooltips", "Extras", "Names show without tooltips.");
			AffinityHover = Compat.Register("affinityHover", "Exact affinity", "Extras", "Status > Social shows no exact affinity.");
			Buildup = Compat.Register("buildup", "Poison and paralysis gauges", "Extras", "The duel gauges show no marks, and the game's own tip.");
			QuickSave = Compat.Register("quickSave", "Quick save and load", "Extras", "Use the game's own Save and Load menus.");
			MenuButtons = Compat.Register("menuButtons", "Mod Settings buttons", "Mod Settings", "Open Mod Settings with its key (F8) instead.");
			// Features that run rarely (a hover, a key, a menu): a few errors in all already mean they are broken.
			foreach (Feature sparse in new Feature[7] { CombatHover, AffinityHover, TextSpeed, QuickSave, MenuButtons, LanguageCaption, Numerals })
			{
				sparse.TotalLimit = 10;
			}
		}
	}

	public sealed class SimulatedGameChangeException : MissingMemberException
	{
		public SimulatedGameChangeException(string feature)
			: base("simulated game change for '" + feature + "' ([Dev] SimulateHookErrors)")
		{
		}
	}

	/// <summary>
	/// The built-in compatibility layer. Every feature installs its hooks through Hook/Require here, one at a time, so a game
	/// update that renames or removes something disables only what depends on it; hooks wrap their bodies in the feature's
	/// guard, so an exception never reaches the game; and the result is listed in Mod Settings > Compatibility, the log and
	/// plugins/LOM_UI_EN/compat_report.txt. When this mod's text layers are not working, the original OverLlm patch (if
	/// installed) serves the text on its own.
	/// </summary>
	public static class Compat
	{
		public static ConfigEntry<bool> CfgNotices;
		public static ConfigEntry<string> CfgSimulateGameChange;
		public static ConfigEntry<string> CfgSimulateHookErrors;

		private static readonly List<Feature> _features = new List<Feature>();
		private static bool _noticeShown;
		private static readonly List<string> _pendingNotices = new List<string>();
		private static readonly List<string> _recentTrips = new List<string>();
		private static float _recentTripsAt = -100f;

		public static IList<Feature> All => _features;

		public static string GameVersion { get; private set; } = "?";
		public static string Fingerprint { get; private set; } = "?";
		public static string VerifiedVersion { get; private set; }
		public static string VerifiedFingerprint { get; private set; }
		public static string VerifiedOn { get; private set; }

		/// <summary>The game's code differs from the build this mod was last checked against.</summary>
		public static bool GameChanged => VerifiedFingerprint != null && Fingerprint != "?" && Fingerprint != VerifiedFingerprint;

		public static string VerifiedPath => Path.Combine(Plugin.PluginDir, "compat_verified.json");
		public static string ReportPath => Path.Combine(Plugin.PluginDir, "compat_report.txt");

		public static event Action Changed;

		public static void Bind(ConfigFile cfg)
		{
			CfgNotices = cfg.Bind("Compat", "Notices", true, "Show a short on-screen notice when a feature of this mod does not work with the installed game version (once per session), or turns itself off after errors.");
			CfgSimulateGameChange = cfg.Bind("Dev", "SimulateGameChange", "", "Testing only: feature ids (comma-separated, see Mod Settings > Compatibility or compat_report.txt) whose game hooks are treated as missing at startup, to check that the rest of the mod keeps working. Needs a restart.");
			CfgSimulateHookErrors = cfg.Bind("Dev", "SimulateHookErrors", "", "Testing only: feature ids whose hooks throw as if the game had changed, to check that they turn themselves off cleanly. Applies at once.");
			CfgSimulateHookErrors.SettingChanged += delegate
			{
				ApplySimulation(runtimeOnly: true);
			};
		}

		public static Feature Register(string id, string name, string group, string fallback)
		{
			Feature f = _features.FirstOrDefault((Feature x) => x.Id == id);
			if (f != null)
			{
				return f;
			}
			f = new Feature(id, name, group, fallback);
			_features.Add(f);
			ApplySimulation(f);
			return f;
		}

		private static HashSet<string> Ids(string s)
		{
			return new HashSet<string>((s ?? "").Split(new char[3] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select((string x) => x.Trim()), StringComparer.OrdinalIgnoreCase);
		}

		private static void ApplySimulation(Feature f)
		{
			if (CfgSimulateGameChange != null)
			{
				f.SimulateInstall = Ids(CfgSimulateGameChange.Value).Contains(f.Id) || Ids(CfgSimulateGameChange.Value).Contains("all");
			}
			if (CfgSimulateHookErrors != null)
			{
				f.SimulateRuntime = Ids(CfgSimulateHookErrors.Value).Contains(f.Id) || Ids(CfgSimulateHookErrors.Value).Contains("all");
			}
		}

		private static void ApplySimulation(bool runtimeOnly)
		{
			foreach (Feature f in _features)
			{
				bool before = f.SimulateInstall;
				ApplySimulation(f);
				if (runtimeOnly)
				{
					f.SimulateInstall = before;
				}
			}
		}

		/// <summary>A clock any thread may read (Unity's time is main-thread only).</summary>
		public static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

		/// <summary>An error meaning the game's code no longer matches what the mod was built against (not a passing state problem).</summary>
		public static bool IsStructural(Exception ex)
		{
			while (ex is TargetInvocationException && ex.InnerException != null)
			{
				ex = ex.InnerException;
			}
			return ex is MissingMemberException || ex is TypeLoadException || ex is BadImageFormatException || ex is InvalidCastException || ex is SimulatedGameChangeException || (ex is System.IO.FileNotFoundException && ex.Message.Contains("Assembly"));
		}

		/// <summary>Installs one hook of a feature. A missing target or a failed patch is recorded (and rolled back), never thrown.</summary>
		public static bool Hook(Feature f, Harmony h, MethodBase target, string what, HarmonyMethod prefix = null, HarmonyMethod postfix = null, HarmonyMethod finalizer = null, bool essential = false)
		{
			f.HooksWanted++;
			if (f.SimulateInstall)
			{
				f.Problems.Add(what + " not found (simulated)");
				if (essential)
				{
					f.EssentialMissing = true;
				}
				return false;
			}
			if (target == null)
			{
				f.Problems.Add(what + " not found");
				if (essential)
				{
					f.EssentialMissing = true;
				}
				Plugin.Log?.LogWarning("[" + f.Id + "] hook target not found: " + what);
				return false;
			}
			try
			{
				h.Patch(target, prefix: prefix, postfix: postfix, finalizer: finalizer);
				f.HooksInstalled++;
				return true;
			}
			catch (Exception ex)
			{
				// HarmonyX keeps the patches it added to the method's shared PatchInfo when building the wrapper fails; take them
				// out again, or every later patch of that method (by any mod) rebuilds from them and fails too.
				foreach (HarmonyMethod hm in new HarmonyMethod[3] { prefix, postfix, finalizer })
				{
					if (hm?.method != null)
					{
						try
						{
							h.Unpatch(target, hm.method);
						}
						catch (Exception)
						{
						}
					}
				}
				f.Problems.Add(what + ": " + ex.Message);
				if (essential)
				{
					f.EssentialMissing = true;
				}
				Plugin.Log?.LogWarning("[" + f.Id + "] could not hook " + what + ": " + ex.Message);
				return false;
			}
		}

		/// <summary>Checks that a game type has a field, property or method this feature reads by name; records the gap if not.</summary>
		public static bool Require(Feature f, Type type, string member, bool essential = true, string part = null)
		{
			return RequireCore(f, type, (type != null) ? type.Name : "?", member, essential, part);
		}

		/// <summary>
		/// The same, with the type given by name (assembly, full name), so the calling method holds no compile-time reference to it:
		/// a renamed or removed game type then costs only this check (or its part), not the whole setup method.
		/// </summary>
		public static bool Require(Feature f, string assembly, string typeName, string member, bool essential = true, string part = null)
		{
			Type type = TranslationProfiles.FindType(assembly, typeName);
			if (type == null)
			{
				f.ChecksWanted++;
				string what = "type " + typeName + " not found";
				f.Problems.Add(what);
				MarkBroken(f, what, essential, part);
				return false;
			}
			return RequireCore(f, type, type.Name, member, essential, part);
		}

		internal static void MarkBroken(Feature f, string what, bool essential, string part)
		{
			if (part != null)
			{
				f.DeclaredParts.Add(part);
				if (!f.BrokenParts.ContainsKey(part))
				{
					f.BrokenParts[part] = what;
				}
			}
			else if (essential)
			{
				f.EssentialMissing = true;
			}
		}

		/// <summary>Any field, property, method or event of that name on the type or a base type, public or not (no AccessTools warnings, no AmbiguousMatchException for overloads).</summary>
		public static bool HasMember(Type type, string member)
		{
			const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
			for (Type x = type; x != null; x = x.BaseType)
			{
				try
				{
					if (x.GetMember(member, all).Length > 0)
					{
						return true;
					}
				}
				catch (Exception)
				{
					return false;
				}
			}
			return false;
		}

		private static bool RequireCore(Feature f, Type type, string typeName, string member, bool essential, string part)
		{
			f.ChecksWanted++;
			if (part != null)
			{
				f.DeclaredParts.Add(part);
			}
			bool ok = !f.SimulateInstall && type != null && HasMember(type, member);
			if (ok)
			{
				f.ChecksPassed++;
				return true;
			}
			string what = typeName + "." + member + (f.SimulateInstall ? " not found (simulated)" : " not found");
			f.Problems.Add(what);
			MarkBroken(f, what, essential, part);
			return false;
		}

		/// <summary>
		/// One block of a feature's setup in its own method (a lambda compiles to one), so a game type that no longer loads in it costs
		/// only that block: the rest of the feature still installs. isHook counts a failed block as a hook that could not be installed.
		/// </summary>
		public static void Part(Feature f, string what, Action a, bool essential = false, string part = null, bool isHook = true)
		{
			if (part != null)
			{
				f.DeclaredParts.Add(part);
			}
			try
			{
				a();
			}
			catch (Exception ex)
			{
				Exception inner = (ex is TargetInvocationException && ex.InnerException != null) ? ex.InnerException : ex;
				if (isHook)
				{
					f.HooksWanted++;
				}
				string msg = what + ": " + inner.GetType().Name + ": " + inner.Message;
				f.Problems.Add(msg);
				MarkBroken(f, msg, essential, part);
				Plugin.Log?.LogWarning("[" + f.Id + "] " + msg);
			}
		}

		/// <summary>Runs a feature's setup; an exception (e.g. a game type that no longer loads) marks the feature not working.</summary>
		public static void Setup(Feature f, Action a)
		{
			try
			{
				a();
			}
			catch (Exception ex)
			{
				Exception inner = (ex is TargetInvocationException && ex.InnerException != null) ? ex.InnerException : ex;
				f.Problems.Add("setup failed: " + inner.GetType().Name + ": " + inner.Message);
				f.EssentialMissing = true;
				Plugin.Log?.LogError("[" + f.Id + "] setup failed: " + inner);
			}
		}

		/// <summary>Runs a per-frame or event step of a feature, contained.</summary>
		public static void Tick(Feature f, Action a)
		{
			if (!f.Live)
			{
				return;
			}
			try
			{
				f.Probe();
				a();
			}
			catch (Exception ex)
			{
				f.Fail(ex, "update");
			}
		}

		internal static void OnTripped(Feature f)
		{
			// Trip runs inside a hook, i.e. inside a game call: undo and notice run on the next Plugin.Update instead.
			Plugin.Defer(delegate
			{
				TurnedOff(f);
			});
		}

		private static void TurnedOff(Feature f)
		{
			if (!f.Tripped)
			{
				// Turned back on (Try again) before this ran.
				return;
			}
			try
			{
				f.OnTurnedOff?.Invoke();
			}
			catch (Exception ex)
			{
				Plugin.Log?.LogWarning("[" + f.Id + "] undo after turning off failed: " + ex.Message);
			}
			float now = Time.realtimeSinceStartup;
			if (now - _recentTripsAt > 8f)
			{
				_recentTrips.Clear();
			}
			_recentTripsAt = now;
			if (!_recentTrips.Contains(f.Name))
			{
				_recentTrips.Add(f.Name);
			}
			string names = string.Join(", ", _recentTrips.Take(3)) + ((_recentTrips.Count > 3) ? (" and " + (_recentTrips.Count - 3) + " more") : "");
			Notice(names + ((_recentTrips.Count == 1) ? " stopped working and turned itself off." : " stopped working and turned themselves off.") + " The rest of the mod keeps working. See Mod Settings > Compatibility.");
			try
			{
				Changed?.Invoke();
			}
			catch (Exception)
			{
			}
		}

		/// <summary>Try again: turn back on every feature that turned itself off, and let each redo its changes.</summary>
		public static int TryAgain()
		{
			int n = 0;
			foreach (Feature f in _features)
			{
				if (!f.Tripped)
				{
					continue;
				}
				f.Reset();
				n++;
				try
				{
					f.OnTurnedOn?.Invoke();
				}
				catch (Exception ex)
				{
					f.Fail(ex, "turning back on");
				}
			}
			if (n > 0)
			{
				// Lean labels (menu buttons) take their text and sizing from the game data layer: have them set again as well.
				try
				{
					StringOverrides.Refresh();
				}
				catch (Exception)
				{
				}
			}
			RaiseChanged();
			return n;
		}

		public static void RaiseChanged()
		{
			try
			{
				Changed?.Invoke();
			}
			catch (Exception)
			{
			}
		}

		// ---- game fingerprint, verified state, summary, report ------------------------------------------------------

		private static bool _finished;

		/// <summary>Plugin.Awake: summarise once every plugin has loaded (LOM_Strings_EN sets up its hooks after LOM_UI_EN).</summary>
		public static void ScheduleFinish()
		{
			// Whichever comes first: every plugin loaded, or the first scene (in case the event is unreachable in another BepInEx).
			SceneManager.sceneLoaded += delegate
			{
				Finish();
			};
			try
			{
				SubscribeFinished();
			}
			catch (Exception)
			{
			}
		}

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private static void SubscribeFinished()
		{
			BepInEx.Unity.Mono.Bootstrap.UnityChainloader.Instance.Finished += Finish;
		}

		/// <summary>After every feature is set up: fingerprint the game, compare with the last verified build, log a summary.</summary>
		public static void Finish()
		{
			if (_finished)
			{
				return;
			}
			_finished = true;
			try
			{
				GameVersion = Application.version;
				// The game's code files on disk (not the loaded assemblies: which of those are loaded yet depends on when this runs).
				using (SHA1 sha = SHA1.Create())
				{
					foreach (string asm in new string[11] { "Mortal.Core", "Mortal.Story", "Mortal.Combat", "Mortal.Free", "Mortal.Battle", "Fungus", "LeanLocalization", "LeanLocalization.TMP", "Unity.TextMeshPro", "DOTween", "Assembly-CSharp" })
					{
						string file = Path.Combine(BepInEx.Paths.ManagedPath, asm + ".dll");
						byte[] name = Encoding.UTF8.GetBytes(asm + "=");
						sha.TransformBlock(name, 0, name.Length, null, 0);
						byte[] data = File.Exists(file) ? File.ReadAllBytes(file) : Encoding.UTF8.GetBytes("missing");
						sha.TransformBlock(data, 0, data.Length, null, 0);
					}
					sha.TransformFinalBlock(new byte[0], 0, 0);
					Fingerprint = BitConverter.ToString(sha.Hash, 0, 6).Replace("-", "").ToLowerInvariant();
				}
			}
			catch (Exception ex)
			{
				Plugin.Log?.LogWarning("game fingerprint failed: " + ex.Message);
			}
			LoadVerified();
			Plugin.Log?.LogInfo("compatibility: " + Summary() + $" (game {GameVersion}, build {Fingerprint}" + ((VerifiedFingerprint == null) ? ", never verified" : (GameChanged ? (", CHANGED since verified " + VerifiedVersion + " " + VerifiedFingerprint) : ", verified")) + ")");
			foreach (Feature f in _features.Where((Feature x) => x.Health != Health.Working && x.Health != Health.NotNeeded))
			{
				Plugin.Log?.LogWarning("  " + f.Name + " [" + f.Id + "]: " + f.StatusLine());
			}
			WriteReport();
			SceneManager.sceneLoaded += delegate
			{
				ShowStartupNotice();
			};
		}

		private static void LoadVerified()
		{
			try
			{
				if (!File.Exists(VerifiedPath))
				{
					return;
				}
				JObject j = JObject.Parse(File.ReadAllText(VerifiedPath));
				VerifiedVersion = (string)j["game_version"];
				VerifiedFingerprint = (string)j["fingerprint"];
				VerifiedOn = (string)j["verified_on"];
			}
			catch (Exception ex)
			{
				Plugin.Log?.LogWarning("compat_verified.json: " + ex.Message);
			}
		}

		/// <summary>Mod Settings > Compatibility "Mark this game version as working" (for whoever maintains the mod).</summary>
		public static string MarkVerified()
		{
			try
			{
				JObject j = new JObject
				{
					["game_version"] = GameVersion,
					["fingerprint"] = Fingerprint,
					["mod_version"] = Plugin.VERSION,
					["verified_on"] = DateTime.Now.ToString("yyyy-MM-dd"),
					["features"] = new JObject(_features.Select((Feature f) => new JProperty(f.Id, f.Health.ToString())))
				};
				File.WriteAllText(VerifiedPath, j.ToString(), new UTF8Encoding(false));
				LoadVerified();
				RaiseChanged();
				return "This game version (" + GameVersion + ", build " + Fingerprint + ") is now marked as checked with this mod.";
			}
			catch (Exception ex)
			{
				return "Could not write compat_verified.json: " + ex.Message;
			}
		}

		public static int CountBad => _features.Count((Feature f) => f.Health == Health.NotWorking || f.Health == Health.TurnedOff);
		public static int CountPartial => _features.Count((Feature f) => f.Health == Health.Partial);

		public static string Summary()
		{
			int working = _features.Count((Feature f) => f.Health == Health.Working);
			int notNeeded = _features.Count((Feature f) => f.Health == Health.NotNeeded);
			int bad = CountBad;
			int partial = CountPartial;
			if (bad == 0 && partial == 0)
			{
				return $"all {working} features working" + ((notNeeded > 0) ? $" ({notNeeded} not needed)" : "");
			}
			return $"{working} working, {partial} partly, {bad} not working" + ((notNeeded > 0) ? $", {notNeeded} not needed" : "");
		}

		public static string WriteReport()
		{
			try
			{
				StringBuilder sb = new StringBuilder();
				sb.Append("LOM_UI_EN ").Append(Plugin.VERSION).Append(" compatibility report, ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append('\n');
				sb.Append("game ").Append(GameVersion).Append(", build ").Append(Fingerprint).Append(", Unity ").Append(Application.unityVersion).Append('\n');
				sb.Append("last verified: ").Append((VerifiedFingerprint == null) ? "never" : (VerifiedVersion + " build " + VerifiedFingerprint + " on " + VerifiedOn + (GameChanged ? " (the game has changed since)" : " (same build)"))).Append('\n');
				sb.Append("summary: ").Append(Summary()).Append("\n\n");
				try
				{
					// Revised rows the fallback mode replaced, or found out of date, this session: what to revise after a game update.
					List<string> fb;
					lock (TextTable.FallbackLog)
					{
						fb = TextTable.FallbackLog.ToList();
					}
					sb.Append("translation: game data ").Append(TranslationProfiles.TableMode).Append(TranslationProfiles.TableHasBase ? (" over the table of " + Plugin.BaseModName) : (" (table of " + Plugin.BaseModName + " not found)")).Append(", scene ").Append(TranslationProfiles.SceneMode).Append(TranslationProfiles.SceneHasBase ? (" over the scene file of " + Plugin.BaseModName) : (" (scene file of " + Plugin.BaseModName + " not found)")).Append("\n");
					sb.Append("fallback to " + Plugin.BaseModName + ": ").Append((TextTable.CfgFallback != null && TextTable.CfgFallback.Value) ? "on" : "off").Append(", ").Append(TextTable.FellBackChanged).Append(" changed by a game update, ").Append(TextTable.FellBackPlaceholders).Append(" placeholders, ").Append(TextTable.StaleKept).Append(" out of date kept (only keys the game asked for this session)\n");
					foreach (string f in fb.Take(2000))
					{
						sb.Append("  ").Append(f).Append("\n");
					}
					sb.Append('\n');
				}
				catch (Exception)
				{
				}
				try
				{
					List<string> unmatched = Rules.Unmatched().ToList();
					sb.Append("layout rules: ").Append(Rules.Count).Append(" loaded, ").Append(Rules.MatchedCount).Append(" applied to something this session");
					sb.Append(" (rules for screens not visited yet stay unmatched; after a game update, a rule that never matches on its screen points at a change there)\n");
					if (unmatched.Count > 0)
					{
						sb.Append("  not matched yet: ").Append(string.Join(", ", unmatched.Take(400))).Append("\n");
					}
					sb.Append('\n');
				}
				catch (Exception)
				{
				}
				foreach (Feature f in _features)
				{
					sb.Append(f.Id).Append('\t').Append(f.Health).Append('\t').Append(f.Name).Append('\t').Append(f.StatusLine()).Append('\n');
					foreach (string p in f.Problems)
					{
						sb.Append("\t\t").Append(p).Append('\n');
					}
					if (f.FirstError != null)
					{
						sb.Append("\t\tfirst error: ").Append(f.FirstError).Append('\n');
					}
				}
				File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
				return ReportPath;
			}
			catch (Exception ex)
			{
				Plugin.Log?.LogWarning("compat report failed: " + ex.Message);
				return null;
			}
		}

		// ---- notices -------------------------------------------------------------------------------------------------

		private static void Notice(string text)
		{
			if (CfgNotices == null || !CfgNotices.Value)
			{
				return;
			}
			try
			{
				QuickSaveToast.Show(text, 8f);
			}
			catch (Exception)
			{
				_pendingNotices.Add(text);
			}
		}

		private static void ShowStartupNotice()
		{
			if (_noticeShown)
			{
				return;
			}
			_noticeShown = true;
			string missing = BaseTextNotice();
			int bad = CountBad;
			if (bad == 0)
			{
				if (missing != null)
				{
					Notice(missing);
				}
				return;
			}
			string names = string.Join(", ", _features.Where((Feature f) => f.Health == Health.NotWorking || f.Health == Health.TurnedOff).Select((Feature f) => f.Name).Take(3));
			Notice(Plugin.DisplayName + ": " + bad + ((bad == 1) ? " feature does" : " features do") + " not work with this game version (" + names + ((bad > 3) ? ", ..." : "") + "). The rest works. See Mod Settings > Compatibility." + ((missing != null) ? ("\n" + missing) : ""));
		}

		/// <summary>Where this mod's own lines do not carry a layer by themselves (MANIFEST.json "standalone"; a 0.5 overlay never
		/// does) and the OverLlm patch's text for it is not installed, part of the game stays Chinese: the player hears about it once.
		/// Since 0.6 this mod ships the whole reviewed translation, so without the patch there is nothing to say.</summary>
		private static string BaseTextNotice()
		{
			try
			{
				OriginalMod.Report r = OriginalMod.Current;
				// Only for layers that serve this mod's lines (not for UI only / Off / Original).
				bool tableWants = TranslationProfiles.CfgTable != null && TranslationProfiles.CfgTable.Value == TextSource.Revised;
				bool sceneWants = TranslationProfiles.CfgScene != null && TranslationProfiles.CfgScene.Value == TextSource.Revised;
				bool tableShort = tableWants && !r.BaseTableFile && !TranslationProfiles.OwnTableComplete;
				bool sceneShort = sceneWants && !r.BaseSceneFile && !TranslationProfiles.OwnSceneComplete;
				if (!EnglishLanguage.Active || (!tableShort && !sceneShort))
				{
					return null;
				}
				if (!r.BaseTableFile && !r.BaseSceneFile)
				{
					return Plugin.DisplayName + ": without " + Plugin.BaseModName + ", some text shows in Chinese. See Mod Settings > Translation.";
				}
				return Plugin.DisplayName + ": part of " + Plugin.BaseModName + " is missing, so some text shows in Chinese. Reinstall it (see Mod Settings > Translation).";
			}
			catch (Exception)
			{
				return null;
			}
		}
	}
}
