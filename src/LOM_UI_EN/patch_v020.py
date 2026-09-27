# One-shot source patch: LOM_UI_EN 0.1.0 (decompiled) -> 0.2.0
#  - path-keyed rule match cache + literal prefilter for wildcard rules (scene loads no longer re-run 145k regex tests)
#  - scene-scoped rule pass after the first scene (the Loading1 overlay no longer re-walks the whole hierarchy)
#  - narrative log guard: trims the backlog text to a vertex-safe length (fixes the empty Dialog History)
#  - lazy narrative log: the backlog text is rebuilt when opened, not on every spoken line
#  - Binarizer quiet: replaces the per-lookup Debug.Log in the string-table hook; no stack traces for Info logs
import io, re, sys
p = sys.argv[1]
s = io.open(p, encoding='utf-8').read()
def rep(old, new, count=1):
    global s
    if old not in s: raise SystemExit('ANCHOR MISSING: ' + old[:100])
    s = s.replace(old, new, count)

# ---- Rule: literal prefilter
rep("""		[JsonIgnore]
		public Regex[] Regexes;
""", """		[JsonIgnore]
		public Regex[] Regexes;

		[JsonIgnore]
		public string[] Literals;
""")
rep("""			for (int i = 0; i < regexes.Length; i++)
			{
				if (regexes[i].IsMatch(path))
				{
					return true;
				}
			}
			return false;
		}

		public override string ToString()""", """			string[] literals = Literals;
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

		public override string ToString()""")

# ---- Rules: cache + literals
rep("""		private static List<Rule> _wildcardTail = new List<Rule>();

		public static long MatchCalls;

		public static long RegexTests;
""", """		private static List<Rule> _wildcardTail = new List<Rule>();

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
""")
rep("""								rule.Regexes = list2.Select(PatternToRegex).ToArray();""",
    """								rule.Regexes = list2.Select(PatternToRegex).ToArray();
								rule.Literals = list2.Select(LongestLiteral).ToArray();""")
rep("""			_rules = list;
			BuildIndex(list);
			Version++;
		}
""", """			_rules = list;
			BuildIndex(list);
			_matchCache.Clear();
			Version++;
		}
""")
rep("""			MatchCalls++;
			int num = path.LastIndexOf('/');
			string key = ((num >= 0) ? path.Substring(num + 1) : path);
			List<Rule> list = null;
			if (_byTail.TryGetValue(key, out var value))
			{
				RegexTests += value.Count;
				for (int i = 0; i < value.Count; i++)""", """			MatchCalls++;
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
				for (int i = 0; i < value.Count; i++)""")
rep("""			RegexTests += _wildcardTail.Count;
			for (int j = 0; j < _wildcardTail.Count; j++)""", """			for (int j = 0; j < _wildcardTail.Count; j++)""")
rep("""			if (list != null && list.Count > 1)
			{
				list.Sort((Rule a, Rule b) => a.Index.CompareTo(b.Index));
			}
			return list;
		}
	}
	public static class SpriteStore""", """			if (list != null && list.Count > 1)
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
	public static class SpriteStore""")

# ---- RuleApplier: scene-scoped pass
rep("""		public static void ApplyToAll(bool includeInactive)
		{""", """		/// <summary>Rule pass over one scene only (its root objects and all their children, inactive included).</summary>
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
					RectTransform rt = rects[i];
					if (rt != null && rt.gameObject.hideFlags == HideFlags.None)
					{
						ProcessSingle(rt.gameObject, fromTextChange: false);
						num++;
					}
				}
			}
			return num;
		}

		public static void ApplyToAll(bool includeInactive)
		{""")

# ---- Plugin: config, version, scene pass, patches
rep("""		public const string VERSION = "0.1.0";""", """		public const string VERSION = "0.2.0";""")
rep("""					bepInExInfoLogInterpolatedStringHandler.AppendFormatted("0.1.0");""", """					bepInExInfoLogInterpolatedStringHandler.AppendFormatted("0.2.0");""")
rep("""		public static ConfigEntry<KeyCode> CfgReloadKey;
""", """		public static ConfigEntry<KeyCode> CfgReloadKey;

		public static ConfigEntry<bool> CfgFullScenePass;

		public static ConfigEntry<bool> CfgLogGuard;

		public static ConfigEntry<int> CfgLogGlyphBudget;

		public static ConfigEntry<bool> CfgLazyLog;

		public static ConfigEntry<bool> CfgQuietBinarizer;

		public static ConfigEntry<bool> CfgNoInfoStackTraces;

		private static bool _firstPassDone;
""")
rep("""			CfgVerbose = base.Config.Bind("General", "VerboseLog", defaultValue: false, "Log every rule application.");""",
    """			CfgVerbose = base.Config.Bind("General", "VerboseLog", defaultValue: false, "Log every rule application.");
			CfgFullScenePass = base.Config.Bind("Performance", "FullScenePass", defaultValue: false, "Run the rule pass over every loaded scene on each scene load (0.1.0 behaviour). Off = only the scene that just loaded; objects from earlier scenes keep their applied rules and the OnEnable hooks still cover anything re-activated.");
			CfgQuietBinarizer = base.Config.Bind("Performance", "QuietBinarizerStringLog", defaultValue: true, "The Binarizer string-table hook writes a Debug.Log line for every string lookup (hundreds of thousands per session, each with a stack trace). Replace it with a silent lookup of the same table.");
			CfgNoInfoStackTraces = base.Config.Bind("Performance", "NoStackTraceForInfoLogs", defaultValue: true, "Stop Unity from capturing a stack trace for plain Debug.Log (Info) messages; warnings and errors keep theirs.");
			CfgLogGuard = base.Config.Bind("NarrativeLog", "GuardMeshLimit", defaultValue: true, "The story backlog (Log button) is one Unity Text; past ~16,000 glyphs Unity throws 'Mesh can not have more than 65000 vertices' and the log shows empty. Trim the shown history to the most recent entries that fit.");
			CfgLogGlyphBudget = base.Config.Bind("NarrativeLog", "GlyphBudget", 13000, "Maximum visible characters of backlog text (before dividing by the Outline/Shadow multiplier of the log Text). 16,250 is the hard limit without effects.");
			CfgLazyLog = base.Config.Bind("NarrativeLog", "LazyRebuild", defaultValue: true, "Rebuild the backlog text when the log is opened instead of after every spoken line (the game rebuilds and re-lays-out the whole history on each line even while the log is hidden).");""")
rep("""				LanguagePatches.Patch(_harmony);""", """				LanguagePatches.Patch(_harmony);
				PerfPatches.Patch(_harmony);
				NarrativeLogGuard.Patch(_harmony);""")
rep("""			Defer(delegate
			{
				Stopwatch stopwatch = Stopwatch.StartNew();
				RuleApplier.ApplyToAll(includeInactive: true);
				stopwatch.Stop();
				Log.LogInfo($"scene '{scene.name}': rule pass {stopwatch.ElapsedMilliseconds} ms ({Rules.MatchCalls - callsAtStart} lookups, {Rules.RegexTests - testsAtStart} regex tests, {RuleApplier.Applications - appsAtStart} rules applied)");
			});""", """			long hitsAtStart = Rules.CacheHits;
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
			});""")

# ---- new classes appended inside the namespace (before the final closing brace)
new_classes = '''
	/// <summary>Performance patches for other layers of the English stack.</summary>
	public static class PerfPatches
	{
		private static Dictionary<string, string> _map;

		public static long Redirects;

		public static void Patch(Harmony h)
		{
			if (Plugin.CfgNoInfoStackTraces.Value)
			{
				try
				{
					Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
				}
				catch (Exception ex)
				{
					Plugin.Log.LogWarning("could not change the Info stack-trace setting: " + ex.Message);
				}
			}
			if (!Plugin.CfgQuietBinarizer.Value)
			{
				return;
			}
			try
			{
				Type type = AccessTools.TypeByName("Mortal.HookMods");
				if (type == null)
				{
					Plugin.Log.LogInfo("Binarizer HookMods not loaded; nothing to quiet");
					return;
				}
				FieldInfo field = AccessTools.Field(type, "mapString");
				MethodInfo method = AccessTools.Method(type, "GetStringRedirect");
				if (field == null || method == null)
				{
					Plugin.Log.LogWarning("Binarizer GetStringRedirect/mapString not found; the per-lookup log stays");
					return;
				}
				_map = field.GetValue(null) as Dictionary<string, string>;
				if (_map == null)
				{
					Plugin.Log.LogWarning("Binarizer mapString is not a Dictionary<string,string>; the per-lookup log stays");
					return;
				}
				h.Patch(method, new HarmonyMethod(typeof(PerfPatches), "GetStringRedirect_Prefix")
				{
					priority = Priority.First
				});
				Plugin.Log.LogInfo("Binarizer string lookups: per-lookup Debug.Log suppressed");
			}
			catch (Exception ex2)
			{
				Plugin.Log.LogWarning("Binarizer quiet patch failed: " + ex2.Message);
			}
		}

		// Replaces Mortal.HookMods.GetStringRedirect(ref LeanLocalizationResolver, ref string result, string key): same lookup, no Debug.Log.
		private static bool GetStringRedirect_Prefix(ref string __1, string __2, ref bool __result)
		{
			string value;
			if (__2 != null && _map.TryGetValue(__2, out value))
			{
				__1 = value;
				__result = false;
				Redirects++;
				return false;
			}
			__result = true;
			return false;
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
			try
			{
				if (Plugin.CfgLogGuard.Value)
				{
					MethodInfo methodInfo = AccessTools.Method(typeof(NarrativeLog), "GetPrettyHistory", new Type[2] { typeof(bool), typeof(int) });
					if (methodInfo == null)
					{
						Plugin.Log.LogWarning("NarrativeLog.GetPrettyHistory not found; mesh guard off");
					}
					else
					{
						h.Patch(methodInfo, new HarmonyMethod(typeof(NarrativeLogGuard), "GetPrettyHistory_Prefix"));
					}
				}
				if (Plugin.CfgLazyLog.Value)
				{
					_update = AccessTools.Method(typeof(NarrativeLogMenu), "UpdateNarrativeLogText");
					MethodInfo toggle = AccessTools.Method(typeof(NarrativeLogMenu), "ToggleNarrativeLogView");
					if (_update == null || toggle == null)
					{
						Plugin.Log.LogWarning("NarrativeLogMenu methods not found; lazy rebuild off");
					}
					else
					{
						h.Patch(_update, new HarmonyMethod(typeof(NarrativeLogGuard), "Update_Prefix"));
						h.Patch(toggle, new HarmonyMethod(typeof(NarrativeLogGuard), "Toggle_Prefix"));
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("narrative log patches failed: " + ex.Message);
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
				else if (c != '\\n' && c != '\\r')
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

		private static void GetPrettyHistory_Prefix(NarrativeLog __instance, ref int maxEntry)
		{
			try
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
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("narrative log guard failed: " + ex.Message);
			}
		}

		private static bool IsVisible(NarrativeLogMenu menu)
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
		private static bool Update_Prefix(NarrativeLogMenu __instance)
		{
			if (IsVisible(__instance))
			{
				_dirty = false;
				return true;
			}
			_dirty = true;
			return false;
		}

		private static void Toggle_Prefix(NarrativeLogMenu __instance)
		{
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
				Plugin.Log.LogWarning("narrative log rebuild on open failed: " + ex.Message);
			}
		}

		private static bool _dirtyGuard;
	}
'''
# Update_Prefix must let the forced rebuild through even while the group is still hidden (Toggle runs before the fade-in)
new_classes = new_classes.replace('''		private static bool Update_Prefix(NarrativeLogMenu __instance)
		{
			if (IsVisible(__instance))''', '''		private static bool Update_Prefix(NarrativeLogMenu __instance)
		{
			if (_dirtyGuard || IsVisible(__instance))''')
idx = s.rstrip().rfind('}')
s = s[:idx] + new_classes + '}\n'
# soft dependency on Binarizer so it is loaded (and its HookMods type resolvable) before us
rep('''	[BepInPlugin("lom.ui.english", "LOM_UI_EN", "0.1.0")]''', '''	[BepInPlugin("lom.ui.english", "LOM_UI_EN", "0.2.0")]
	[BepInDependency("binarizer.plugin.mortal", BepInDependency.DependencyFlags.SoftDependency)]''')
io.open(p, 'w', encoding='utf-8', newline='\n').write(s)
print('patched', len(s))
