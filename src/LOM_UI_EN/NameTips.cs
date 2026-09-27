using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LOM_UI_EN
{
	/// <summary>
	/// Names in dialogue and choice menus: the name of a character who has portrait art is coloured, and pointing at it shows a
	/// tip with the character's portrait (a head-and-shoulders crop of their stage art), their title and, once the game lists
	/// them in Status > Social, the player's affinity with them (level, progress and the exact 0-100 value). The names of sects,
	/// clans and other factions work the same way, with a short spoiler-free description (factiontips.tsv) in their tip.
	///
	/// Which characters, and which extra name forms ("Xiaomei" for Yu Xiaomei), come from plugins/LOM_UI_EN/nametips.tsv. The
	/// full name itself is the served Character/&lt;id&gt; text, so a renamed translation needs no change here, and the file holds
	/// no translated text of its own.
	///
	/// The dialog text is never changed. A postfix on Text.OnPopulateMesh finds the names in the dialog Text's string with the
	/// rich-text tags stripped (so the Fungus Writer's hidden-text colour tag, which keeps the unrevealed rest of the line in the
	/// string, does not matter), recolours those glyph quads in the mesh before Shadow/Outline copy them, and keeps their
	/// rectangles. The hover test runs every frame against the mouse position; nothing is added as a raycast target, so clicks
	/// and the game's click-to-continue work as before.
	/// </summary>
	public static class NameTips
	{
		public static ConfigEntry<bool> CfgEnabled;
		public static ConfigEntry<string> CfgColor;
		public static ConfigEntry<bool> CfgMetOnly;
		public static ConfigEntry<bool> CfgFactions;
		public static ConfigEntry<string> CfgFactionColor;

		/// <summary>Seconds the pointer rests on a name before the tip shows.</summary>
		private const float Dwell = 0.12f;
		/// <summary>Crop window of the stage art in pixels of a 1700 px tall portrait (the game's size): its height, and the gap left above the top of the figure.</summary>
		internal const float CropHeight = 640f;
		internal const float CropMargin = 40f;
		internal const float PortraitRefHeight = 1700f;
		private const string DataFile = "nametips.tsv";
		private const string FactionFile = "factiontips.tsv";

		internal sealed class TipCharacter
		{
			public string Id;
			public string[] Aliases = new string[0];
			/// <summary>Portrait address from the data file (a character whose game data points at a missing image).</summary>
			public string PortraitOverride;
			public float CropX = 0.5f;
			public float CropTop = 0.22f;
			public bool HasCrop;
			/// <summary>Served Character/&lt;id&gt; and CharacterTitle/&lt;id&gt; text when the matcher was built.</summary>
			public string Name;
			public string Title;
			/// <summary>Portrait addresses to try in order ("normal" first); null until read from the game's character config.</summary>
			public List<string> PortraitKeys;
			public int KeyIndex;
			public PortraitState State;
			public Sprite Portrait;
			/// <summary>RelationshipStat, typed object so this class loads even if the game renames that type.</summary>
			public object Relationship;
			public bool RelationshipResolved;
			/// <summary>A sect, clan or other faction from factiontips.tsv: Name is its title, Aliases the forms matched, Description the tip text; no portrait or affinity.</summary>
			public bool IsFaction;
			public string Description;
		}

		internal enum PortraitState
		{
			Idle,
			Loading,
			Ready,
			Failed
		}

		/// <summary>One occurrence of a name in a dialog text: its range in the tag-free text and its rectangles (text-local, one per line).</summary>
		internal sealed class Link
		{
			public TipCharacter Char;
			public int Start;
			public int Length;
			public bool Revealed;
			public readonly List<Rect> Rects = new List<Rect>(2);
		}

		/// <summary>A text the tips work on: a SayDialog's story text, or a label of a choice menu's option (UGUI Text, or TextMeshPro in the dice menu).</summary>
		internal sealed class Tracked
		{
			/// <summary>The UGUI Text; null for a TextMeshPro label (Tmp).</summary>
			public Text Text;
			public TMPro.TMP_Text Tmp;
			/// <summary>Whichever of the two it is, for what both share (rect, canvas, alpha, redraw).</summary>
			public Graphic G;
			public bool Choice;
			/// <summary>RectMask2D / Mask areas above the text (a scrolling option list): a name outside them is hidden, so it has no tip.</summary>
			public RectTransform[] Clips = new RectTransform[0];
			public byte[] Cat = new byte[512];
			public int[] PlainToOrig = new int[512];
			public int[] QuadOf = new int[512];
			public readonly StringBuilder Plain = new StringBuilder(512);
			public string LastPlain;
			public int LastStamp = -1;
			public readonly List<KeyValuePair<int, KeyValuePair<int, TipCharacter>>> Matches = new List<KeyValuePair<int, KeyValuePair<int, TipCharacter>>>();
			public readonly List<Link> Links = new List<Link>();
			public int LinkCount;
			/// <summary>Start (in the tag-free text) of the hovered name, drawn in the hover colour; -1 = none.</summary>
			public int HoverStart = -1;

			public void Ensure(int n)
			{
				if (Cat.Length < n)
				{
					int size = Math.Max(n, Cat.Length * 2);
					Cat = new byte[size];
					PlainToOrig = new int[size];
					QuadOf = new int[size];
				}
			}
		}

		// Character categories of the text string: a quad is drawn for glyphs (and <quad> tags); whether whitespace and tag
		// characters get quads depends on the Unity version, which the first matching count decides (see PickMode).
		private const byte CatTag = 0;
		private const byte CatSpace = 1;
		private const byte CatOtherSpace = 2;
		private const byte CatGlyph = 3;
		private const byte CatQuadTag = 4;

		private static readonly string[] ModeNames = new string[4] { "glyphs and other whitespace", "glyphs only", "glyphs and whitespace", "every character" };
		private static readonly int[] _counts = new int[4];
		/// <summary>When the story character config (story scene assets) was last looked for in vain; looked for again after 2 s.</summary>
		private static float _keysMissedAt = -100f;
		/// <summary>When the player stats were last looked for in vain (their getter logs an error each time).</summary>
		private static float _statsMissedAt = -100f;
		private static string _lastCover;

		private static readonly List<TipCharacter> _chars = new List<TipCharacter>();
		private static readonly Dictionary<string, TipCharacter> _byId = new Dictionary<string, TipCharacter>(StringComparer.Ordinal);
		private static Dictionary<string, TipCharacter> _byMatch = new Dictionary<string, TipCharacter>(StringComparer.Ordinal);
		private static Regex _regex;
		private static bool _matcherDirty = true;
		private static int _matcherStamp;
		private static bool _loaded;
		private static string _loadError;

		private static readonly Dictionary<int, Tracked> _tracked = new Dictionary<int, Tracked>();
		private static readonly HashSet<int> _notDialog = new HashSet<int>();
		private static readonly HashSet<TipCharacter> _portraitWanted = new HashSet<TipCharacter>();

		/// <summary>Set by Update: tips are on, the feature works and the game is in English. The mesh hook reads only this.</summary>
		private static volatile bool _on;
		private static int _mode = -1;
		private static bool _modeLogged;
		private static bool _tint = true;
		private static Color32 _linkLight;
		private static Color32 _linkDark;
		private static Color32 _hoverLight;
		private static Color32 _hoverDark;
		private static bool _facTint = true;
		private static Color32 _facLight;
		private static Color32 _facDark;
		private static Color32 _facHoverLight;
		private static Color32 _facHoverDark;
		/// <summary>The font of the last UGUI text a name was found in (a TextMeshPro label has no UGUI font to lend the tip).</summary>
		private static Font _lastFont;
		private static bool _menusBroken;

		private static Tracked _hoverText;
		private static int _hoverStart = -1;
		private static float _hoverSince;
		private static NameTipView _view;
		private static readonly List<RaycastResult> _raycast = new List<RaycastResult>();
		private static PointerEventData _pointer;
		private static EventSystem _pointerSystem;

		public static long Rebuilds;
		public static long Mismatches;
		public static long LinksFound;
		private static string _lastMismatch;

		public static void Bind(ConfigFile cfg)
		{
			CfgEnabled = cfg.Bind("Story", "NameTooltips", true, "Dialogue and choice menus: names of characters who have portrait art are coloured, and pointing at one shows the character's portrait and title and, once the game lists them in Status > Social, your affinity with them. The characters, and the extra name forms matched (given names such as 'Xiaomei'), are listed in plugins/LOM_UI_EN/nametips.tsv.");
			CfgColor = cfg.Bind("Story", "NameTooltipColor", "#E8C47C", "Colour of those names in dialogue, as #RRGGBB (on a light dialog box a darker shade of it is used). Empty = the text's own colour; the names can still be pointed at.");
			CfgMetOnly = cfg.Bind("Story", "NameTooltipsMetOnly", true, "Characters who have an entry in Status > Social become tooltips only once the game has introduced them (the card that unlocks their entry), so a name mentioned before a reveal shows no face. Off = from the start.");
			CfgMetOnly.SettingChanged += delegate
			{
				DirtyAll();
			};
			CfgFactions = cfg.Bind("Story", "FactionTooltips", true, "Dialogue and choice menus: names of sects, clans, families and other factions are coloured, and pointing at one shows a short spoiler-free description of who they are. The factions and the name forms matched are listed in plugins/LOM_UI_EN/factiontips.tsv.");
			CfgFactionColor = cfg.Bind("Story", "FactionTooltipColor", "#8FC7B0", "Colour of faction names, as #RRGGBB (on a light box a darker shade of it is used). Empty = the text's own colour; the names can still be pointed at.");
			CfgFactions.SettingChanged += delegate
			{
				OnSettingChanged();
			};
			CfgFactionColor.SettingChanged += delegate
			{
				ParseColor();
				DirtyAll();
			};
			ParseColor();
			CfgColor.SettingChanged += delegate
			{
				ParseColor();
				DirtyAll();
			};
			CfgEnabled.SettingChanged += delegate
			{
				OnSettingChanged();
			};
		}

		public static void Patch(Harmony h)
		{
			F.NameTips.OnTurnedOff = TurnOff;
			F.NameTips.OnTurnedOn = DirtyAll;
			// Names only (assembly, type): this method names no game type, so a renamed one costs its own check, not the setup.
			Compat.Require(F.NameTips, "Fungus", "Fungus.SayDialog", "StoryTextObject");
			Compat.Require(F.NameTips, "Mortal.Core", "Mortal.Core.StoryCharacterConfig", "List", essential: false, part: "portraits");
			Compat.Require(F.NameTips, "Mortal.Core", "Mortal.Core.StoryCharacterData", "PortraitResourceList", essential: false, part: "portraits");
			Compat.Require(F.NameTips, "Mortal.Core", "Mortal.Core.StoryCharacterData", "Id", essential: false, part: "portraits");
			Compat.Require(F.NameTips, "Mortal.Core", "Mortal.Core.StoryCharaterImageItem", "AddressKey", essential: false, part: "portraits");
			Compat.Require(F.NameTips, "Mortal.Core", "Mortal.Core.StoryCharaterImageItem", "Mapping", essential: false, part: "portraits");
			Compat.Require(F.NameTips, "Mortal.Core", "Mortal.Core.PlayerStatManagerData", "Relationships", essential: false, part: "affinity");
			Compat.Require(F.NameTips, "Mortal.Core", "Mortal.Core.RelationshipStat", "Value", essential: false, part: "affinity");
			Compat.Require(F.NameTips, "Mortal.Core", "Mortal.Core.RelationshipStat", "Active", essential: false, part: "affinity");
			Compat.Require(F.NameTips, "Mortal.Core", "Mortal.Core.RelationshipStat", "Type", essential: false, part: "affinity");
			Compat.Require(F.NameTips, "Mortal.Story", "Mortal.Story.StoryManager", "IsStoryPause", essential: false, part: "menus");
			Load();
			if (!_loaded)
			{
				F.NameTips.Problems.Add(_loadError);
				F.NameTips.EssentialMissing = true;
			}
			if (Compat.Hook(F.NameTips, h, AccessTools.Method(typeof(Text), "OnPopulateMesh", new Type[1] { typeof(VertexHelper) }), "Text.OnPopulateMesh", null, new HarmonyMethod(typeof(NameTips), nameof(OnPopulateMesh_Postfix)), essential: true))
			{
				Plugin.Log.LogInfo($"name tips: {_chars.Count((TipCharacter c) => !c.IsFaction)} characters from {DataFile}, {_chars.Count((TipCharacter c) => c.IsFaction)} factions from {FactionFile}" + (CfgEnabled.Value || CfgFactions.Value ? "" : " (off)"));
			}
			// The dice menu's option labels are TextMeshPro: they are coloured when TMP reports a rebuilt text.
			Compat.Part(F.NameTips, "TextMeshPro text event", SubscribeTmp, part: "tmp", isHook: false);
			SceneManager.sceneLoaded += OnSceneLoaded;
		}

		// ---- data --------------------------------------------------------------------------------------------------------

		/// <summary>Reads nametips.tsv and factiontips.tsv (either may be missing: its kind of name then stays plain).</summary>
		public static void Load()
		{
			_chars.Clear();
			_byId.Clear();
			_loaded = false;
			_loadError = null;
			List<string> missing = new List<string>();
			LoadCharacters(Path.Combine(Plugin.PluginDir, DataFile), missing);
			LoadFactions(Path.Combine(Plugin.PluginDir, FactionFile), missing);
			if (missing.Count > 0)
			{
				Plugin.Log.LogWarning("name tips: " + string.Join(" and ", missing.ToArray()) + " not found in the plugin folder; those names stay plain");
			}
			_loaded = _chars.Count > 0;
			if (!_loaded)
			{
				_loadError = (missing.Count > 0 ? string.Join(" and ", missing.ToArray()) + " not found in the plugin folder" : "no characters or factions listed");
			}
			MarkMatcherDirty();
		}

		/// <summary>
		/// nametips.tsv: id TAB aliases (comma-separated) TAB portrait address override TAB crop (head centre x, top of the figure
		/// y, both 0-1 of the portrait). Lines starting with '#' are comments; empty trailing columns may be left out.
		/// </summary>
		private static void LoadCharacters(string path, List<string> missing)
		{
			if (!File.Exists(path))
			{
				missing.Add(DataFile);
				return;
			}
			int lineNo = 0;
			foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
			{
				lineNo++;
				string line = raw.TrimEnd((char)13);
				if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#"))
				{
					continue;
				}
				string[] cols = line.Split((char)9);
				string id = cols[0].Trim();
				if (id.Length == 0 || _byId.ContainsKey(id))
				{
					Plugin.Log.LogWarning($"name tips: {DataFile} line {lineNo}: " + (id.Length == 0 ? "no id" : "id '" + id + "' listed twice") + ", skipped");
					continue;
				}
				TipCharacter c = new TipCharacter { Id = id };
				if (cols.Length > 1)
				{
					c.Aliases = cols[1].Split(',').Select((string a) => a.Trim()).Where((string a) => a.Length > 0).ToArray();
				}
				if (cols.Length > 2 && cols[2].Trim().Length > 0)
				{
					c.PortraitOverride = cols[2].Trim();
				}
				if (cols.Length > 3 && cols[3].Trim().Length > 0)
				{
					string[] xy = cols[3].Split(',');
					if (xy.Length == 2 && float.TryParse(xy[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) && float.TryParse(xy[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
					{
						c.CropX = Mathf.Clamp01(x);
						c.CropTop = Mathf.Clamp01(y);
						c.HasCrop = true;
					}
					else
					{
						Plugin.Log.LogWarning($"name tips: {DataFile} line {lineNo}: crop '{cols[3]}' not understood (want x,y), default used");
					}
				}
				_chars.Add(c);
				_byId[id] = c;
			}
		}

		/// <summary>
		/// factiontips.tsv: id TAB title TAB forms (comma-separated, matched like the character names) TAB description (the tip's
		/// text). Lines starting with '#' are comments.
		/// </summary>
		private static void LoadFactions(string path, List<string> missing)
		{
			if (!File.Exists(path))
			{
				missing.Add(FactionFile);
				return;
			}
			int lineNo = 0;
			foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
			{
				lineNo++;
				string line = raw.TrimEnd((char)13);
				if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#"))
				{
					continue;
				}
				string[] cols = line.Split((char)9);
				string id = "faction:" + cols[0].Trim();
				if (cols.Length < 4 || cols[0].Trim().Length == 0 || _byId.ContainsKey(id))
				{
					Plugin.Log.LogWarning($"name tips: {FactionFile} line {lineNo}: " + (cols.Length < 4 ? "want id, title, forms and description" : "no id or listed twice") + ", skipped");
					continue;
				}
				TipCharacter c = new TipCharacter
				{
					Id = id,
					IsFaction = true,
					Name = cols[1].Trim(),
					Aliases = cols[2].Split(',').Select((string a) => a.Trim()).Where((string a) => a.Length > 0).ToArray(),
					Description = cols[3].Trim(),
					State = PortraitState.Failed,
					PortraitKeys = new List<string>(),
					RelationshipResolved = true
				};
				_chars.Add(c);
				_byId[id] = c;
			}
		}

		/// <summary>F11 reload: read the data file again and redo every dialog text.</summary>
		public static void Reload()
		{
			try
			{
				ReleasePortraits();
				Load();
				DirtyAll();
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("name tips reload failed: " + ex.Message);
			}
		}

		private static void MarkMatcherDirty()
		{
			_matcherDirty = true;
		}

		/// <summary>The served name and title of every listed character, and one regex over all their name forms, longest first.</summary>
		private static void EnsureMatcher()
		{
			if (!_matcherDirty)
			{
				return;
			}
			_matcherDirty = false;
			Dictionary<string, TipCharacter> map = new Dictionary<string, TipCharacter>(StringComparer.Ordinal);
			HashSet<string> clash = new HashSet<string>(StringComparer.Ordinal);
			foreach (TipCharacter c in _chars)
			{
				List<string> forms = new List<string>();
				if (c.IsFaction)
				{
					if (!CfgFactions.Value)
					{
						continue;
					}
					if (Usable(c.Name))
					{
						forms.Add(c.Name);
					}
					forms.AddRange(c.Aliases.Where(Usable));
				}
				else
				{
					if (!CfgEnabled.Value)
					{
						continue;
					}
					c.Name = Served("Character/" + c.Id);
					c.Title = Served("CharacterTitle/" + c.Id);
					if (!Usable(c.Title))
					{
						c.Title = null;
					}
					if (Usable(c.Name))
					{
						forms.Add(c.Name);
					}
					else
					{
						c.Name = null;
					}
					forms.AddRange(c.Aliases.Where(Usable));
				}
				foreach (string f in forms.Distinct(StringComparer.Ordinal))
				{
					if (map.TryGetValue(f, out TipCharacter other) && other != c)
					{
						clash.Add(f);
					}
					else
					{
						map[f] = c;
					}
				}
			}
			foreach (string f in clash)
			{
				map.Remove(f);
				Plugin.Log.LogWarning("name tips: '" + f + "' is a name of more than one listed character or faction; it is not linked");
			}
			_byMatch = map;
			if (map.Count == 0)
			{
				_regex = null;
			}
			else
			{
				string alternation = string.Join("|", map.Keys.OrderByDescending((string k) => k.Length).ThenBy((string k) => k, StringComparer.Ordinal).Select(Regex.Escape).ToArray());
				// A name is a whole word: no letter or digit right before or after it ("Xiaomei's" matches, "Lanzhou" does not match "Lan").
				_regex = new Regex(@"(?<![\p{L}\p{N}])(?:" + alternation + @")(?![\p{L}\p{N}])", RegexOptions.CultureInvariant);
			}
			_matcherStamp++;
		}

		/// <summary>English text fit to match: not empty, no Chinese, no placeholder ("???", "{title}").</summary>
		private static bool Usable(string s)
		{
			if (string.IsNullOrEmpty(s) || s.Trim().Length < 2)
			{
				return false;
			}
			bool letter = false;
			foreach (char ch in s)
			{
				if ((ch >= 0x3000 && ch <= 0x9FFF) || (ch >= 0xF900 && ch <= 0xFAFF) || (ch >= 0xFF00 && ch <= 0xFFEF) || ch == '?' || ch == '{' || ch == '}' || ch == '(' || ch == (char)10)
				{
					return false;
				}
				if (char.IsLetter(ch))
				{
					letter = true;
				}
			}
			return letter;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static string Served(string key)
		{
			try
			{
				string s = Mortal.Core.LocalizationManager.Instance.LocaleResolver.GetString(key);
				return (s ?? "").Trim();
			}
			catch (Exception)
			{
				return "";
			}
		}

		// ---- settings --------------------------------------------------------------------------------------------------------

		private static void ParseColor()
		{
			ParseColor(CfgColor, new Color(0.91f, 0.77f, 0.49f), out _tint, out _linkLight, out _linkDark, out _hoverLight, out _hoverDark);
			if (CfgFactionColor != null)
			{
				ParseColor(CfgFactionColor, new Color(0.56f, 0.78f, 0.69f), out _facTint, out _facLight, out _facDark, out _facHoverLight, out _facHoverDark);
			}
		}

		private static void ParseColor(ConfigEntry<string> entry, Color fallback, out bool tint, out Color32 light, out Color32 dark, out Color32 hoverLight, out Color32 hoverDark)
		{
			string s = (entry.Value ?? "").Trim();
			tint = s.Length > 0;
			if (!s.StartsWith("#"))
			{
				s = "#" + s;
			}
			if (!tint || !ColorUtility.TryParseHtmlString(s, out Color c))
			{
				if (tint)
				{
					Plugin.Log.LogWarning("name tips: " + entry.Definition.Key + " '" + entry.Value + "' is not a colour (#RRGGBB); the default is used");
				}
				c = fallback;
			}
			light = Opaque(c);
			hoverLight = Opaque(Color.Lerp(c, Color.white, 0.2f));
			// On a light box (dark text) the same hue, darkened, so the name keeps its contrast.
			dark = Opaque(new Color(c.r * 0.5f, c.g * 0.42f, c.b * 0.34f));
			hoverDark = Opaque(new Color(c.r * 0.62f, c.g * 0.52f, c.b * 0.42f));
		}

		private static Color32 Opaque(Color c)
		{
			Color32 k = c;
			k.a = 255;
			return k;
		}

		public static void OnSettingChanged()
		{
			MarkMatcherDirty();
			if (!CfgEnabled.Value && !CfgFactions.Value)
			{
				TurnOff();
				return;
			}
			DirtyAll();
		}

		/// <summary>Tips off (setting, language, or the feature turned itself off): hide the tip, forget every name and redraw the texts plain.</summary>
		private static void TurnOff()
		{
			_on = false;
			Hide();
			foreach (Tracked t in _tracked.Values)
			{
				t.LinkCount = 0;
			}
			DirtyAll();
		}

		/// <summary>Rebuild the mesh of every dialog text seen, so a colour or on/off change shows at once.</summary>
		private static void DirtyAll()
		{
			foreach (Tracked t in _tracked.Values)
			{
				if (t.G != null)
				{
					t.HoverStart = -1;
					Redraw(t);
				}
			}
		}

		/// <summary>Rebuild a text's mesh (and so its names). TextMeshPro regenerates only when asked outright.</summary>
		private static void Redraw(Tracked t)
		{
			if (t.G == null)
			{
				return;
			}
			if (t.Tmp != null)
			{
				RedrawTmp(t.Tmp);
			}
			else
			{
				t.G.SetVerticesDirty();
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void RedrawTmp(TMPro.TMP_Text tmp)
		{
			if (tmp.isActiveAndEnabled)
			{
				tmp.ForceMeshUpdate();
			}
		}

		private static void DirtyDialogs()
		{
			try
			{
				DirtyDialogsBody();
			}
			catch (Exception ex)
			{
				F.NameTips.Fail(ex, "dialog refresh");
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void DirtyDialogsBody()
		{
			foreach (Fungus.SayDialog sd in Resources.FindObjectsOfTypeAll<Fungus.SayDialog>())
			{
				if (sd != null && sd.gameObject.scene.IsValid() && sd.StoryTextObject != null)
				{
					sd.StoryTextObject.SetVerticesDirty();
				}
			}
			// A choice menu that is up: its option labels (UGUI and TextMeshPro).
			foreach (Fungus.MenuDialog md in Resources.FindObjectsOfTypeAll<Fungus.MenuDialog>())
			{
				if (md == null || !md.gameObject.scene.IsValid() || !md.isActiveAndEnabled)
				{
					continue;
				}
				foreach (Button b in md.GetComponentsInChildren<Button>())
				{
					foreach (Graphic g in b.GetComponentsInChildren<Graphic>())
					{
						if (g is TMPro.TMP_Text tmp)
						{
							tmp.ForceMeshUpdate();
						}
						else if (g is Text)
						{
							g.SetVerticesDirty();
						}
					}
				}
			}
		}

		private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			try
			{
				_notDialog.Clear();
				PruneTracked();
				MarkMatcherDirty();
				if (mode == LoadSceneMode.Single)
				{
					Hide();
					ReleasePortraits();
				}
			}
			catch (Exception ex)
			{
				F.NameTips.Fail(ex, "scene loaded");
			}
		}

		private static void PruneTracked()
		{
			List<int> dead = null;
			foreach (KeyValuePair<int, Tracked> kv in _tracked)
			{
				if (kv.Value.G == null)
				{
					(dead ?? (dead = new List<int>())).Add(kv.Key);
				}
			}
			if (dead != null)
			{
				foreach (int id in dead)
				{
					_tracked.Remove(id);
				}
			}
			if (_hoverText != null && _hoverText.G == null)
			{
				_hoverText = null;
			}
		}

		// ---- mesh hook ---------------------------------------------------------------------------------------------------

		// Runs for every UGUI Text rebuild in the game: when tips are off it only reads one bool.
		private static void OnPopulateMesh_Postfix(Text __instance, VertexHelper __0)
		{
			if (!_on || __instance == null || __0 == null)
			{
				return;
			}
			try
			{
				F.NameTips.Probe();
				Populate(__instance, __0);
			}
			catch (Exception ex)
			{
				F.NameTips.Fail(ex, "Text.OnPopulateMesh");
			}
		}

		private static void Populate(Text text, VertexHelper vh)
		{
			int id = text.GetInstanceID();
			if (!_tracked.TryGetValue(id, out Tracked t))
			{
				if (_notDialog.Contains(id))
				{
					return;
				}
				int kind = Classify(text);
				if (kind == 0)
				{
					_notDialog.Add(id);
					return;
				}
				t = NewTracked(text, null, kind == 2);
				_tracked[id] = t;
			}
			Build(t, vh);
		}

		/// <summary>
		/// 0 = not a text the tips work on; 1 = the story text of a Fungus SayDialog (the four story dialogs, and any other
		/// SayDialog the game shows); 2 = a label inside an option button of a Fungus MenuDialog (every choice, dice, location and
		/// talk menu derives from it).
		/// </summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static int Classify(Graphic g)
		{
			Text text = g as Text;
			if (text != null)
			{
				foreach (Fungus.SayDialog sd in g.GetComponentsInParent<Fungus.SayDialog>(true))
				{
					if (sd != null && sd.StoryTextObject == text)
					{
						return 1;
					}
				}
			}
			Button button = g.GetComponentInParent<Button>();
			if (button != null && button.GetComponentInParent<Fungus.MenuDialog>() != null)
			{
				return 2;
			}
			return 0;
		}

		private static Tracked NewTracked(Text text, TMPro.TMP_Text tmp, bool choice)
		{
			Graphic g = (text != null) ? (Graphic)text : tmp;
			List<RectTransform> clips = new List<RectTransform>();
			foreach (RectMask2D m in g.GetComponentsInParent<RectMask2D>(true))
			{
				clips.Add(m.rectTransform);
			}
			foreach (Mask m in g.GetComponentsInParent<Mask>(true))
			{
				clips.Add(m.rectTransform);
			}
			return new Tracked { Text = text, Tmp = tmp, G = g, Choice = choice, Clips = clips.ToArray() };
		}

		private static void Build(Tracked t, VertexHelper vh)
		{
			Rebuilds++;
			t.LinkCount = 0;
			string s = t.Text.text;
			int quads = vh.currentVertCount / 4;
			if (string.IsNullOrEmpty(s) || quads == 0)
			{
				return;
			}
			EnsureMatcher();
			if (_regex == null)
			{
				return;
			}
			int n = s.Length;
			t.Ensure(n + 1);
			_lastFont = t.Text.font;
			bool rich = t.Text.supportRichText;
			StringBuilder plain = t.Plain;
			plain.Length = 0;
			int glyphs = 0, spaces = 0, otherSpaces = 0, tagChars = 0, quadTags = 0;
			int i = 0;
			while (i < n)
			{
				char ch = s[i];
				if (rich && ch == '<' && TagAt(s, i, out int end, out bool quad))
				{
					for (int k = i; k <= end; k++)
					{
						t.Cat[k] = CatTag;
					}
					tagChars += end - i + 1;
					if (quad)
					{
						t.Cat[i] = CatQuadTag;
						quadTags++;
						tagChars--;
					}
					i = end + 1;
					continue;
				}
				byte cat;
				if (ch == ' ' || ch == (char)10 || ch == (char)9 || ch == (char)13)
				{
					cat = CatSpace;
					spaces++;
				}
				else if (char.IsWhiteSpace(ch))
				{
					cat = CatOtherSpace;
					otherSpaces++;
				}
				else
				{
					cat = CatGlyph;
					glyphs++;
				}
				t.Cat[i] = cat;
				t.PlainToOrig[plain.Length] = i;
				plain.Append(ch);
				i++;
			}
			int mode = PickMode(quads, n, glyphs, spaces, otherSpaces, quadTags);
			if (mode < 0)
			{
				Mismatches++;
				_lastMismatch = $"{quads} quads for '{Clip(s)}' ({glyphs} glyphs, {spaces} spaces, {otherSpaces} other whitespace, {tagChars} tag characters)";
				if (Mismatches <= 3)
				{
					Plugin.Log.LogWarning("name tips: cannot map the text to its mesh: " + _lastMismatch);
				}
				return;
			}
			// quad index of each character of s under the chosen mode (-1 = draws nothing)
			int q = 0;
			for (int k = 0; k < n; k++)
			{
				byte cat = t.Cat[k];
				bool draws = mode == 3 || cat == CatGlyph || cat == CatQuadTag || (cat == CatOtherSpace && mode != 1) || (cat == CatSpace && mode == 2);
				t.QuadOf[k] = (draws && q < quads) ? q : -1;
				if (draws)
				{
					q++;
				}
			}
			// names in the tag-free text; unchanged while the Writer reveals a line, so matched once per line
			if (t.LastPlain == null || t.LastStamp != _matcherStamp || !SameText(plain, t.LastPlain))
			{
				t.LastPlain = plain.ToString();
				t.LastStamp = _matcherStamp;
				t.Matches.Clear();
				foreach (Match m in _regex.Matches(t.LastPlain))
				{
					if (_byMatch.TryGetValue(m.Value, out TipCharacter c))
					{
						t.Matches.Add(new KeyValuePair<int, KeyValuePair<int, TipCharacter>>(m.Index, new KeyValuePair<int, TipCharacter>(m.Length, c)));
					}
				}
			}
			if (t.Matches.Count == 0)
			{
				return;
			}
			UIVertex v = default(UIVertex);
			foreach (KeyValuePair<int, KeyValuePair<int, TipCharacter>> match in t.Matches)
			{
				int start = match.Key;
				int length = match.Value.Key;
				if (t.LinkCount == t.Links.Count)
				{
					t.Links.Add(new Link());
				}
				Link link = t.Links[t.LinkCount];
				if (!Linkable(match.Value.Value))
				{
					continue;
				}
				link.Char = match.Value.Value;
				link.Start = start;
				link.Length = length;
				link.Rects.Clear();
				bool revealed = true;
				bool any = false;
				bool hovered = t.HoverStart == start;
				bool faction = link.Char.IsFaction;
				bool tint = faction ? _facTint : _tint;
				Rect line = default(Rect);
				bool hasLine = false;
				for (int p = start; p < start + length; p++)
				{
					int quad = t.QuadOf[t.PlainToOrig[p]];
					if (quad < 0)
					{
						continue;
					}
					float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
					for (int k = 0; k < 4; k++)
					{
						vh.PopulateUIVertex(ref v, quad * 4 + k);
						Vector3 pos = v.position;
						minX = Mathf.Min(minX, pos.x);
						maxX = Mathf.Max(maxX, pos.x);
						minY = Mathf.Min(minY, pos.y);
						maxY = Mathf.Max(maxY, pos.y);
						Color32 col = v.color;
						if (col.a < 24)
						{
							revealed = false;
						}
						if (tint)
						{
							Color32 target = TintFor(faction, hovered, col);
							v.color = new Color32(target.r, target.g, target.b, col.a);
							vh.SetUIVertex(v, quad * 4 + k);
						}
					}
					if (maxX <= minX || maxY <= minY)
					{
						continue;
					}
					Rect glyph = Rect.MinMaxRect(minX, minY, maxX, maxY);
					any = true;
					if (!hasLine)
					{
						line = glyph;
						hasLine = true;
						continue;
					}
					float h = Mathf.Max(glyph.height, line.height);
					// a new line of the dialog: the glyph sits lower, or starts left of the line so far (wrapped)
					if (Mathf.Abs(glyph.center.y - line.center.y) > h * 0.6f || glyph.xMin < line.xMin - 1f)
					{
						link.Rects.Add(line);
						line = glyph;
					}
					else
					{
						line = Rect.MinMaxRect(Mathf.Min(line.xMin, glyph.xMin), Mathf.Min(line.yMin, glyph.yMin), Mathf.Max(line.xMax, glyph.xMax), Mathf.Max(line.yMax, glyph.yMax));
					}
				}
				if (hasLine)
				{
					link.Rects.Add(line);
				}
				if (!any)
				{
					continue;
				}
				link.Revealed = revealed;
				t.LinkCount++;
				LinksFound++;
				if (!faction && link.Char.State == PortraitState.Idle)
				{
					_portraitWanted.Add(link.Char);
				}
			}
		}

		/// <summary>The link colour for a name's letter over this text colour: the setting's colour on light text, a darker shade on dark text (a light box).</summary>
		private static Color32 TintFor(bool faction, bool hovered, Color32 col)
		{
			bool light = col.r * 299 + col.g * 587 + col.b * 114 >= 115000;
			if (faction)
			{
				return light ? (hovered ? _facHoverLight : _facLight) : (hovered ? _facHoverDark : _facDark);
			}
			return light ? (hovered ? _hoverLight : _linkLight) : (hovered ? _hoverDark : _linkDark);
		}

		// ---- TextMeshPro labels (the dice menu's options) ------------------------------------------------------------------

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void SubscribeTmp()
		{
			TMPro.TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTmpTextChanged);
		}

		// TMP raises this after it has generated and uploaded a text's mesh; recolouring then is how TMP text effects work.
		private static void OnTmpTextChanged(UnityEngine.Object obj)
		{
			if (!_on || obj == null)
			{
				return;
			}
			try
			{
				F.NameTips.Probe();
				TmpChanged(obj);
			}
			catch (Exception ex)
			{
				F.NameTips.Fail(ex, "TMP text changed");
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void TmpChanged(UnityEngine.Object obj)
		{
			TMPro.TMP_Text tmp = obj as TMPro.TMP_Text;
			// An inactive label is judged when it is shown (its parents' lookups need it active), and until then has no tip anyway.
			if (tmp == null || !tmp.isActiveAndEnabled)
			{
				return;
			}
			int id = tmp.GetInstanceID();
			if (!_tracked.TryGetValue(id, out Tracked t))
			{
				if (_notDialog.Contains(id))
				{
					return;
				}
				if (Classify(tmp) != 2)
				{
					_notDialog.Add(id);
					return;
				}
				t = NewTracked(null, tmp, choice: true);
				_tracked[id] = t;
			}
			BuildTmp(t, tmp);
		}

		/// <summary>Build for a TextMeshPro label: TMP has already parsed the rich text into characterInfo (one entry per character, with its line, quad and colour slot).</summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void BuildTmp(Tracked t, TMPro.TMP_Text tmp)
		{
			Rebuilds++;
			t.LinkCount = 0;
			TMPro.TMP_TextInfo info = tmp.textInfo;
			if (info == null || info.characterCount == 0 || info.characterInfo == null)
			{
				return;
			}
			EnsureMatcher();
			if (_regex == null)
			{
				return;
			}
			int n = Math.Min(info.characterCount, info.characterInfo.Length);
			StringBuilder plain = t.Plain;
			plain.Length = 0;
			for (int i = 0; i < n; i++)
			{
				plain.Append(info.characterInfo[i].character);
			}
			if (t.LastPlain == null || t.LastStamp != _matcherStamp || !SameText(plain, t.LastPlain))
			{
				t.LastPlain = plain.ToString();
				t.LastStamp = _matcherStamp;
				t.Matches.Clear();
				foreach (Match m in _regex.Matches(t.LastPlain))
				{
					if (_byMatch.TryGetValue(m.Value, out TipCharacter c))
					{
						t.Matches.Add(new KeyValuePair<int, KeyValuePair<int, TipCharacter>>(m.Index, new KeyValuePair<int, TipCharacter>(m.Length, c)));
					}
				}
			}
			bool recoloured = false;
			foreach (KeyValuePair<int, KeyValuePair<int, TipCharacter>> match in t.Matches)
			{
				int start = match.Key;
				int length = match.Value.Key;
				if (!Linkable(match.Value.Value))
				{
					continue;
				}
				if (t.LinkCount == t.Links.Count)
				{
					t.Links.Add(new Link());
				}
				Link link = t.Links[t.LinkCount];
				link.Char = match.Value.Value;
				link.Start = start;
				link.Length = length;
				link.Rects.Clear();
				bool faction = link.Char.IsFaction;
				bool tint = faction ? _facTint : _tint;
				bool hovered = t.HoverStart == start;
				bool revealed = true;
				bool any = false;
				int lineNo = -1;
				Rect line = default(Rect);
				for (int p = start; p < start + length && p < n; p++)
				{
					TMPro.TMP_CharacterInfo ci = info.characterInfo[p];
					if (!ci.isVisible)
					{
						continue;
					}
					if (p >= tmp.maxVisibleCharacters)
					{
						revealed = false;
					}
					int mi = ci.materialReferenceIndex;
					int vi = ci.vertexIndex;
					if (info.meshInfo == null || mi < 0 || mi >= info.meshInfo.Length)
					{
						continue;
					}
					Color32[] cols = info.meshInfo[mi].colors32;
					if (cols == null || vi < 0 || vi + 3 >= cols.Length)
					{
						continue;
					}
					if (cols[vi].a < 24)
					{
						revealed = false;
					}
					if (tint)
					{
						for (int k = 0; k < 4; k++)
						{
							Color32 col = cols[vi + k];
							Color32 target = TintFor(faction, hovered, col);
							cols[vi + k] = new Color32(target.r, target.g, target.b, col.a);
						}
						recoloured = true;
					}
					Rect glyph = Rect.MinMaxRect(ci.bottomLeft.x, Mathf.Min(ci.descender, ci.bottomLeft.y), ci.topRight.x, Mathf.Max(ci.ascender, ci.topRight.y));
					any = true;
					if (ci.lineNumber != lineNo)
					{
						if (lineNo >= 0)
						{
							link.Rects.Add(line);
						}
						line = glyph;
						lineNo = ci.lineNumber;
					}
					else
					{
						line = Rect.MinMaxRect(Mathf.Min(line.xMin, glyph.xMin), Mathf.Min(line.yMin, glyph.yMin), Mathf.Max(line.xMax, glyph.xMax), Mathf.Max(line.yMax, glyph.yMax));
					}
				}
				if (lineNo >= 0)
				{
					link.Rects.Add(line);
				}
				if (!any)
				{
					continue;
				}
				link.Revealed = revealed;
				t.LinkCount++;
				LinksFound++;
				if (!faction && link.Char.State == PortraitState.Idle)
				{
					_portraitWanted.Add(link.Char);
				}
			}
			if (recoloured)
			{
				tmp.UpdateVertexData(TMPro.TMP_VertexDataUpdateFlags.Colors32);
			}
		}

		/// <summary>
		/// Which characters draw a quad: Unity 2019.1+ draws none for whitespace and rich-text tags, older versions one per
		/// character. The first mode whose count equals the mesh's quads is used from then on; a text cut short by its box
		/// (fewer quads) keeps the mode already known.
		/// </summary>
		private static int PickMode(int quads, int n, int glyphs, int spaces, int otherSpaces, int quadTags)
		{
			int[] counts = _counts;
			counts[0] = glyphs + otherSpaces + quadTags;
			counts[1] = glyphs + quadTags;
			counts[2] = glyphs + otherSpaces + spaces + quadTags;
			counts[3] = n;
			if (_mode >= 0 && counts[_mode] == quads)
			{
				return _mode;
			}
			for (int m = 0; m < counts.Length; m++)
			{
				if (counts[m] == quads)
				{
					if (m != _mode)
					{
						_mode = m;
						if (!_modeLogged || Plugin.CfgVerbose.Value)
						{
							_modeLogged = true;
							Plugin.Log.LogInfo("name tips: text meshes draw " + ModeNames[m]);
						}
					}
					return m;
				}
			}
			if (_mode >= 0 && quads < counts[_mode])
			{
				return _mode;
			}
			return -1;
		}

		/// <summary>
		/// A rich-text tag of UGUI Text at s[i] ('&lt;'): b, i, size=, color=, material= (and their closing tags) or quad.
		/// Anything else in angle brackets is drawn as text, as Unity does.
		/// </summary>
		private static bool TagAt(string s, int i, out int end, out bool quad)
		{
			end = -1;
			quad = false;
			int close = s.IndexOf('>', i + 1);
			if (close < 0)
			{
				return false;
			}
			int p = i + 1;
			int len = close - p;
			if (len <= 0 || s.IndexOf('<', p, len) >= 0)
			{
				return false;
			}
			bool tag = Is(s, p, len, "b") || Is(s, p, len, "i") || Is(s, p, len, "/b") || Is(s, p, len, "/i") || Is(s, p, len, "/size") || Is(s, p, len, "/color") || Is(s, p, len, "/material") || Starts(s, p, len, "size=") || Starts(s, p, len, "color=") || Starts(s, p, len, "material=");
			if (!tag && len >= 4 && string.CompareOrdinal(s, p, "quad", 0, 4) == 0 && (len == 4 || s[p + 4] == ' ' || s[p + 4] == '/'))
			{
				tag = true;
				quad = true;
			}
			if (tag)
			{
				end = close;
			}
			return tag;
		}

		private static bool Is(string s, int p, int len, string lit)
		{
			return len == lit.Length && string.CompareOrdinal(s, p, lit, 0, lit.Length) == 0;
		}

		private static bool Starts(string s, int p, int len, string lit)
		{
			return len > lit.Length && string.CompareOrdinal(s, p, lit, 0, lit.Length) == 0;
		}

		private static bool SameText(StringBuilder sb, string s)
		{
			if (sb.Length != s.Length)
			{
				return false;
			}
			for (int i = 0; i < s.Length; i++)
			{
				if (sb[i] != s[i])
				{
					return false;
				}
			}
			return true;
		}

		private static string Clip(string s)
		{
			s = s.Replace((char)10, ' ');
			return s.Length > 80 ? s.Substring(0, 80) + "..." : s;
		}

		// ---- hover -------------------------------------------------------------------------------------------------------

		/// <summary>Plugin.Update, through Compat.Tick: turns the mesh hook on or off, loads wanted portraits, and runs the hover test.</summary>
		public static void Update()
		{
			bool on = (CfgEnabled.Value || CfgFactions.Value) && _loaded && EnglishLanguage.Active;
			if (on != _on)
			{
				if (!on)
				{
					TurnOff();
				}
				else
				{
					_on = true;
					DirtyAll();
					// A line already on screen was built while the tips were off: rebuild it so its names show now.
					DirtyDialogs();
				}
			}
			if (!on)
			{
				return;
			}
			if (_portraitWanted.Count > 0)
			{
				TipCharacter[] wanted = _portraitWanted.ToArray();
				_portraitWanted.Clear();
				foreach (TipCharacter c in wanted)
				{
					RequestPortrait(c);
				}
			}
			Tracked hitText = null;
			Link hit = null;
			Rect hitRect = default(Rect);
			if (TryMouse(out Vector2 mouse) && !ModMenu.IsOpen && !StoryPaused())
			{
				foreach (Tracked t in _tracked.Values)
				{
					Graphic text = t.G;
					if (text == null || t.LinkCount == 0 || !text.isActiveAndEnabled || text.canvasRenderer.GetInheritedAlpha() < 0.5f)
					{
						continue;
					}
					Camera cam = CameraOf(text);
					if (!Unclipped(t, mouse, cam) || !RectTransformUtility.ScreenPointToLocalPointInRectangle(text.rectTransform, mouse, cam, out Vector2 local))
					{
						continue;
					}
					for (int li = 0; li < t.LinkCount && hit == null; li++)
					{
						Link link = t.Links[li];
						if (!link.Revealed)
						{
							continue;
						}
						foreach (Rect r in link.Rects)
						{
							Rect pad = Rect.MinMaxRect(r.xMin - 3f, r.yMin - 4f, r.xMax + 3f, r.yMax + 4f);
							if (pad.Contains(local))
							{
								hit = link;
								hitText = t;
								hitRect = r;
								break;
							}
						}
					}
					if (hit != null)
					{
						break;
					}
				}
				if (hit != null && Covered(hitText.G, mouse))
				{
					hit = null;
					hitText = null;
				}
			}
			int start = hit?.Start ?? -1;
			if (hitText != _hoverText || start != _hoverStart)
			{
				if (_hoverText != null && _hoverText.G != null)
				{
					_hoverText.HoverStart = -1;
					Redraw(_hoverText);
				}
				_hoverText = hitText;
				_hoverStart = start;
				_hoverSince = Time.unscaledTime;
				if (hitText != null)
				{
					hitText.HoverStart = start;
					Redraw(hitText);
				}
			}
			if (hit == null)
			{
				Hide();
				return;
			}
			if (Time.unscaledTime - _hoverSince < Dwell)
			{
				return;
			}
			Show(hit, hitText, hitRect);
		}

		private static Camera CameraOf(Graphic g)
		{
			Canvas c = g.canvas;
			if (c == null)
			{
				return null;
			}
			c = c.rootCanvas;
			return (c.renderMode == RenderMode.ScreenSpaceOverlay) ? null : c.worldCamera;
		}

		/// <summary>The pointer is inside every mask above the text (a name scrolled out of an option list is hidden).</summary>
		private static bool Unclipped(Tracked t, Vector2 mouse, Camera cam)
		{
			foreach (RectTransform clip in t.Clips)
			{
				if (clip != null && clip.gameObject.activeInHierarchy && !RectTransformUtility.RectangleContainsScreenPoint(clip, mouse, cam))
				{
					return false;
				}
			}
			return true;
		}

		private static bool TryMouse(out Vector2 p)
		{
			p = default(Vector2);
			try
			{
				if (!MouseFromInputSystem(out p))
				{
					return false;
				}
			}
			catch (Exception)
			{
				try
				{
					p = Input.mousePosition;
				}
				catch (Exception)
				{
					return false;
				}
			}
			return p.x >= 0f && p.y >= 0f && p.x <= Screen.width && p.y <= Screen.height;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool MouseFromInputSystem(out Vector2 p)
		{
			UnityEngine.InputSystem.Mouse m = UnityEngine.InputSystem.Mouse.current;
			if (m == null)
			{
				p = default(Vector2);
				return false;
			}
			p = m.position.ReadValue();
			return true;
		}

		/// <summary>A story panel is up (menu, log, status, shop...): the game pauses the story for those, and the tip steps aside.</summary>
		private static bool StoryPaused()
		{
			if (_menusBroken)
			{
				return false;
			}
			try
			{
				return StoryPausedBody();
			}
			catch (Exception ex)
			{
				if (Compat.IsStructural(ex))
				{
					_menusBroken = true;
					F.NameTips.BreakPart("menus", ex.GetType().Name + ": " + ex.Message);
					return false;
				}
				throw;
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool StoryPausedBody()
		{
			Mortal.Story.StoryManager sm = Mortal.Story.StoryManager.Instance;
			return sm != null && sm.IsStoryPause;
		}

		/// <summary>Something of another canvas is drawn over the name at the pointer (a panel opened on top of the dialog).</summary>
		private static bool Covered(Graphic text, Vector2 mouse)
		{
			EventSystem es = EventSystem.current;
			if (es == null)
			{
				return false;
			}
			if (_pointer == null || _pointerSystem != es)
			{
				_pointer = new PointerEventData(es);
				_pointerSystem = es;
			}
			_pointer.Reset();
			_pointer.position = mouse;
			_raycast.Clear();
			es.RaycastAll(_pointer, _raycast);
			if (_raycast.Count == 0 || _raycast[0].gameObject == null)
			{
				return false;
			}
			Canvas top = _raycast[0].gameObject.GetComponentInParent<Canvas>();
			Canvas own = text.canvas;
			if (top == null || own == null)
			{
				return false;
			}
			if (top.rootCanvas == own.rootCanvas)
			{
				return false;
			}
			_lastCover = PathUtil.GetPath(_raycast[0].gameObject.transform) + " (canvas order " + top.rootCanvas.sortingOrder + " over " + own.rootCanvas.sortingOrder + ")";
			return true;
		}

		private static void Show(Link link, Tracked t, Rect local)
		{
			TipCharacter c = link.Char;
			if (_view == null)
			{
				_view = NameTipView.Create();
			}
			Camera cam = CameraOf(t.G);
			RectTransform rt = t.G.rectTransform;
			Vector2 top = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(new Vector3(local.center.x, local.yMax, 0f)));
			Vector2 bottom = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(new Vector3(local.center.x, local.yMin, 0f)));
			Font font = (t.Text != null) ? t.Text.font : _lastFont;
			if (c.IsFaction)
			{
				_view.ShowFaction(c, font, _facLight, top, bottom);
				return;
			}
			if (c.State == PortraitState.Idle)
			{
				RequestPortrait(c);
			}
			bool affinity = ReadAffinity(c, out int value);
			_view.Show(c, font, c.State == PortraitState.Ready ? c.Portrait : null, c.State == PortraitState.Loading, affinity, value, top, bottom);
		}

		private static void Hide()
		{
			if (_view != null)
			{
				_view.Hide();
			}
		}

		// ---- affinity ----------------------------------------------------------------------------------------------------

		/// <summary>The player's affinity with the character, when the game has one for them and lists them in Status > Social.</summary>
		private static bool ReadAffinity(TipCharacter c, out int value)
		{
			value = 0;
			if (!F.NameTips.PartOk("affinity"))
			{
				return false;
			}
			try
			{
				return ReadAffinityBody(c, out value);
			}
			catch (Exception ex)
			{
				if (Compat.IsStructural(ex))
				{
					F.NameTips.BreakPart("affinity", ex.GetType().Name + ": " + ex.Message);
					return false;
				}
				throw;
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool ReadAffinityBody(TipCharacter c, out int value)
		{
			value = 0;
			if (!ResolveRelationship(c))
			{
				return false;
			}
			Mortal.Core.RelationshipStat r = c.Relationship as Mortal.Core.RelationshipStat;
			if (r == null || !r.Active)
			{
				return false;
			}
			value = Mathf.Clamp(r.Value, 0, 100);
			return true;
		}

		/// <summary>Finds the character's affinity record (RelationshipStat whose type's string value is the id) once. False while the player stats are not loaded.</summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool ResolveRelationship(TipCharacter c)
		{
			if (c.RelationshipResolved)
			{
				return true;
			}
			// PlayerStatManagerData.Instance logs an error each time it finds nothing: ask at most every 2 s.
			if (Time.unscaledTime - _statsMissedAt < 2f)
			{
				return false;
			}
			Mortal.Core.PlayerStatManagerData ps = Mortal.Core.PlayerStatManagerData.Instance;
			if (ps == null || ps.Relationships == null || ps.Relationships.List == null)
			{
				_statsMissedAt = Time.unscaledTime;
				return false;
			}
			c.RelationshipResolved = true;
			foreach (Mortal.Core.RelationshipStat stat in ps.Relationships.List)
			{
				if (stat != null && OBB.Framework.Extensions.EnumExtensions.GetStringValue(stat.Type) == c.Id)
				{
					c.Relationship = stat;
					break;
				}
			}
			return true;
		}

		/// <summary>
		/// [Story] NameTooltipsMetOnly: a character who has an affinity record becomes a link only once the game has introduced
		/// them (its introduction card unlocks their Status > Social entry), so a name mentioned before a reveal (the youth in dark
		/// robes, the white-clad youth) shows no face. Characters without a record are always linked.
		/// </summary>
		private static bool Linkable(TipCharacter c)
		{
			if (c.IsFaction)
			{
				return true;
			}
			if (!CfgMetOnly.Value || !F.NameTips.PartOk("affinity"))
			{
				return true;
			}
			try
			{
				return LinkableBody(c);
			}
			catch (Exception ex)
			{
				if (Compat.IsStructural(ex))
				{
					F.NameTips.BreakPart("affinity", ex.GetType().Name + ": " + ex.Message);
					return true;
				}
				throw;
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool LinkableBody(TipCharacter c)
		{
			if (!ResolveRelationship(c))
			{
				return false;
			}
			Mortal.Core.RelationshipStat r = c.Relationship as Mortal.Core.RelationshipStat;
			return r == null || r.Active;
		}

		/// <summary>The character's own full-length art from its affinity record (already in memory), used when its stage art cannot be loaded.</summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static Sprite AffinityAvatar(TipCharacter c)
		{
			ReadAffinityBody(c, out int _);
			return (c.Relationship as Mortal.Core.RelationshipStat)?.Avatar;
		}

		// ---- portraits ---------------------------------------------------------------------------------------------------

		private static void RequestPortrait(TipCharacter c)
		{
			if (c.State != PortraitState.Idle)
			{
				return;
			}
			if (!F.NameTips.PartOk("portraits"))
			{
				c.State = PortraitState.Failed;
				return;
			}
			try
			{
				if (c.PortraitKeys == null)
				{
					if (c.PortraitOverride != null)
					{
						c.PortraitKeys = new List<string> { c.PortraitOverride };
					}
					else if (Time.unscaledTime - _keysMissedAt < 2f)
					{
						return;
					}
					else if (!PortraitLoader.ResolveKeys(_byId))
					{
						// The character config is part of the story scene's assets: not loaded here yet, ask again later.
						_keysMissedAt = Time.unscaledTime;
						return;
					}
					if (c.PortraitKeys == null)
					{
						c.PortraitKeys = new List<string>();
					}
				}
				PortraitLoader.LoadNext(c);
			}
			catch (Exception ex)
			{
				c.State = PortraitState.Failed;
				if (Compat.IsStructural(ex))
				{
					F.NameTips.BreakPart("portraits", ex.GetType().Name + ": " + ex.Message);
					return;
				}
				throw;
			}
		}

		/// <summary>Called by the loader when a portrait arrived or every address failed.</summary>
		internal static void PortraitDone(TipCharacter c)
		{
			if (c.State == PortraitState.Failed)
			{
				try
				{
					Sprite avatar = F.NameTips.PartOk("affinity") ? AffinityAvatar(c) : null;
					if (avatar != null)
					{
						c.Portrait = avatar;
						c.State = PortraitState.Ready;
					}
				}
				catch (Exception)
				{
				}
			}
			if (_view != null)
			{
				_view.PortraitArrived(c);
			}
		}

		private static void ReleasePortraits()
		{
			try
			{
				PortraitLoader.ReleaseAll(_chars);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("name tips: releasing portraits failed: " + ex.Message);
			}
		}

		// ---- harness -----------------------------------------------------------------------------------------------------

		/// <summary>State for the harness 'nametips' command: settings, data, mesh mode, every dialog text's names with screen rectangles.</summary>
		public static string Describe()
		{
			StringBuilder sb = new StringBuilder();
			EnsureMatcherSafe();
			sb.Append($"name tips: {(_on ? "on" : "off")} (characters {CfgEnabled.Value}, factions {CfgFactions.Value}, feature {F.NameTips.Health}, english {EnglishLanguage.Active}, data {(_loaded ? _chars.Count((TipCharacter c) => !c.IsFaction) + " characters, " + _chars.Count((TipCharacter c) => c.IsFaction) + " factions" : _loadError)}); ");
			sb.Append($"{_byMatch.Count} name forms; mesh mode {(_mode >= 0 ? ModeNames[_mode] : "not known yet")}; rebuilds {Rebuilds}, names found {LinksFound}, mismatches {Mismatches}");
			if (_lastMismatch != null)
			{
				sb.Append(" (last: " + _lastMismatch + ")");
			}
			sb.Append('\n');
			foreach (Tracked t in _tracked.Values)
			{
				if (t.G == null || (t.Choice && !t.G.isActiveAndEnabled))
				{
					continue;
				}
				sb.Append($"  {(t.Choice ? "choice" : "text")}{(t.Tmp != null ? " (TMP)" : "")} {PathUtil.GetPath(t.G.transform)} active={t.G.isActiveAndEnabled} alpha={t.G.canvasRenderer.GetInheritedAlpha():F2} names={t.LinkCount}\n");
				for (int i = 0; i < t.LinkCount; i++)
				{
					Link l = t.Links[i];
					string word = (t.LastPlain != null && l.Start + l.Length <= t.LastPlain.Length) ? t.LastPlain.Substring(l.Start, l.Length) : "?";
					sb.Append($"    '{word}' -> {l.Char.Id} ({l.Char.Name}) revealed={l.Revealed} portrait={l.Char.State}");
					foreach (Rect r in l.Rects)
					{
						Vector2 center = ScreenCenter(t, r);
						sb.Append($" screen=({center.x:F0},{center.y:F0})");
					}
					sb.Append('\n');
				}
			}
			sb.Append("  hover: " + (_hoverText != null && _hoverStart >= 0 ? "at " + _hoverStart : "none") + ", tip " + (_view != null && _view.Visible ? "shown for " + _view.ShownId : "hidden"));
			if (_lastCover != null)
			{
				sb.Append("; a name was last covered by " + _lastCover);
			}
			return sb.ToString();
		}

		private static void EnsureMatcherSafe()
		{
			try
			{
				EnsureMatcher();
			}
			catch (Exception)
			{
			}
		}

		private static Vector2 ScreenCenter(Tracked t, Rect r)
		{
			return RectTransformUtility.WorldToScreenPoint(CameraOf(t.G), t.G.rectTransform.TransformPoint(new Vector3(r.center.x, r.center.y, 0f)));
		}

		/// <summary>Screen position (pixels, origin bottom-left) of the first visible name that is this character id, name or alias.</summary>
		public static bool FindOnScreen(string what, out Vector2 pos)
		{
			pos = default(Vector2);
			foreach (Tracked t in _tracked.Values)
			{
				if (t.G == null || !t.G.isActiveAndEnabled || t.G.canvasRenderer.GetInheritedAlpha() < 0.5f)
				{
					continue;
				}
				for (int i = 0; i < t.LinkCount; i++)
				{
					Link l = t.Links[i];
					string word = (t.LastPlain != null && l.Start + l.Length <= t.LastPlain.Length) ? t.LastPlain.Substring(l.Start, l.Length) : "";
					if (l.Rects.Count > 0 && (string.Equals(l.Char.Id, what, StringComparison.OrdinalIgnoreCase) || string.Equals(word, what, StringComparison.OrdinalIgnoreCase) || string.Equals(l.Char.Name, what, StringComparison.OrdinalIgnoreCase)))
					{
						pos = ScreenCenter(t, l.Rects[0]);
						return true;
					}
				}
			}
			return false;
		}
	}

	/// <summary>Addressables access for the tip portraits, apart so that NameTips loads even if that API changes.</summary>
	internal static class PortraitLoader
	{
		private static readonly Dictionary<NameTips.TipCharacter, UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<Sprite>> _handles = new Dictionary<NameTips.TipCharacter, UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<Sprite>>();

		/// <summary>Portrait addresses of every listed character from the game's story character config ("normal" first). False while that config is not loaded.</summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static bool ResolveKeys(Dictionary<string, NameTips.TipCharacter> byId)
		{
			Mortal.Core.StoryCharacterConfig config = null;
			foreach (Mortal.Core.StoryCharacterConfig x in Resources.FindObjectsOfTypeAll<Mortal.Core.StoryCharacterConfig>())
			{
				if (x != null && x.List != null && x.List.Count > 0)
				{
					config = x;
					break;
				}
			}
			if (config == null)
			{
				return false;
			}
			foreach (Mortal.Core.StoryCharacterData d in config.List)
			{
				if (d == null)
				{
					continue;
				}
				string id;
				try
				{
					id = d.Id;
				}
				catch (NullReferenceException)
				{
					continue;
				}
				if (id == null || !byId.TryGetValue(id, out NameTips.TipCharacter c) || c.PortraitKeys != null)
				{
					continue;
				}
				List<string> keys = new List<string>();
				if (d.PortraitResourceList != null)
				{
					foreach (Mortal.Core.StoryCharaterImageItem item in d.PortraitResourceList)
					{
						if (item == null || string.IsNullOrEmpty(item.AddressKey))
						{
							continue;
						}
						bool normal = item.Mapping != null && item.Mapping.Value == "normal";
						if (normal)
						{
							keys.Insert(0, item.AddressKey);
						}
						else if (keys.Count < 3)
						{
							keys.Add(item.AddressKey);
						}
					}
				}
				c.PortraitKeys = keys;
			}
			// Listed characters the config does not have: no portrait (only name, title and affinity).
			foreach (NameTips.TipCharacter c in byId.Values)
			{
				if (c.PortraitKeys == null)
				{
					c.PortraitKeys = new List<string>();
				}
			}
			return true;
		}

		/// <summary>Starts loading the character's next portrait address that exists; none left = Failed.</summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void LoadNext(NameTips.TipCharacter c)
		{
			while (c.KeyIndex < c.PortraitKeys.Count)
			{
				string key = c.PortraitKeys[c.KeyIndex++];
				if (!Exists(key))
				{
					continue;
				}
				UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<Sprite> handle = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<Sprite>(key);
				_handles[c] = handle;
				c.State = NameTips.PortraitState.Loading;
				handle.Completed += delegate (UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<Sprite> op)
				{
					try
					{
						Completed(c, op);
					}
					catch (Exception ex)
					{
						F.NameTips.Fail(ex, "portrait loaded");
					}
				};
				return;
			}
			c.State = NameTips.PortraitState.Failed;
			NameTips.PortraitDone(c);
		}

		private static void Completed(NameTips.TipCharacter c, UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<Sprite> op)
		{
			// Released meanwhile (a scene change): this answer belongs to no one.
			if (!_handles.TryGetValue(c, out UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<Sprite> mine) || !mine.Equals(op))
			{
				return;
			}
			if (op.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded && op.Result != null)
			{
				c.Portrait = op.Result;
				c.State = NameTips.PortraitState.Ready;
				NameTips.PortraitDone(c);
				return;
			}
			_handles.Remove(c);
			if (op.IsValid())
			{
				UnityEngine.AddressableAssets.Addressables.Release(op);
			}
			LoadNext(c);
		}

		/// <summary>The address is in a loaded catalog (asking for one that is not logs an exception).</summary>
		private static bool Exists(string key)
		{
			foreach (UnityEngine.AddressableAssets.ResourceLocators.IResourceLocator locator in UnityEngine.AddressableAssets.Addressables.ResourceLocators)
			{
				if (locator.Locate(key, typeof(Sprite), out IList<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation> found) && found != null && found.Count > 0)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Scene change: give the portraits back (the next tip loads them again) so no bundle stays pinned by the tips.</summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void ReleaseAll(List<NameTips.TipCharacter> chars)
		{
			foreach (KeyValuePair<NameTips.TipCharacter, UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<Sprite>> kv in _handles)
			{
				try
				{
					if (kv.Value.IsValid())
					{
						UnityEngine.AddressableAssets.Addressables.Release(kv.Value);
					}
				}
				catch (Exception)
				{
				}
			}
			_handles.Clear();
			foreach (NameTips.TipCharacter c in chars)
			{
				if (c.IsFaction)
				{
					continue;
				}
				c.Portrait = null;
				c.State = NameTips.PortraitState.Idle;
				c.KeyIndex = 0;
			}
		}
	}

	/// <summary>The tip itself: a panel on its own overlay canvas with the portrait crop, name, title and the affinity row.</summary>
	internal class NameTipView : MonoBehaviour
	{
		// Layout in reference pixels (1920x1080, like the game's canvases).
		private const float Pad = 12f;
		private const float FrameW = 118f;
		private const float FrameH = 144f;
		private const float Gap = 14f;
		private const float ColumnW = 262f;
		private const float Border = 2f;
		/// <summary>Width of a faction tip's description column.</summary>
		private const float DescW = 340f;
		private static readonly Color NameColor = new Color(0.98f, 0.92f, 0.78f);
		private static readonly Color BorderGold = new Color(0.66f, 0.53f, 0.33f, 0.95f);

		private RectTransform _canvasRect;
		private RectTransform _panel;
		private CanvasGroup _group;
		private RectTransform _frame;
		private Image _portrait;
		private RectTransform _portraitRect;
		private Text _name;
		private Text _title;
		private RectTransform _rule;
		private Text _affinityLabel;
		private Text _level;
		private RectTransform _barBack;
		private RectTransform _barFill;
		private Text _value;
		private Text _desc;
		private Image _border;
		private Image _ruleImage;
		private Font _font;
		private NameTips.TipCharacter _shown;
		private int _shownKey = -1;
		private Vector2 _anchorTop;
		private Vector2 _anchorBottom;

		public bool Visible => _group != null && _group.alpha > 0f;

		public string ShownId => _shown?.Id;

		public static NameTipView Create()
		{
			GameObject root = new GameObject("LOM_NameTip");
			DontDestroyOnLoad(root);
			Canvas canvas = root.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 30500;
			CanvasScaler scaler = root.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920f, 1080f);
			scaler.matchWidthOrHeight = 0.5f;
			NameTipView view = root.AddComponent<NameTipView>();
			view._canvasRect = root.GetComponent<RectTransform>();
			view.Build();
			return view;
		}

		private void Build()
		{
			// Visibility is the CanvasGroup's alpha, not SetActive, so the Text/Image OnEnable hooks (font map, rule pass) run once.
			_panel = NewRect("LOM_NameTipPanel", _canvasRect);
			_panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
			_group = _panel.gameObject.AddComponent<CanvasGroup>();
			_group.alpha = 0f;
			_group.blocksRaycasts = false;
			_group.interactable = false;
			Image border = _panel.gameObject.AddComponent<Image>();
			border.color = BorderGold;
			border.raycastTarget = false;
			_border = border;
			RectTransform back = NewRect("LOM_NameTipBack", _panel);
			Stretch(back, Border);
			Image backImage = back.gameObject.AddComponent<Image>();
			backImage.color = new Color(0.075f, 0.06f, 0.05f, 0.96f);
			backImage.raycastTarget = false;

			_frame = NewRect("LOM_NameTipFrame", _panel);
			TopLeft(_frame, Pad, Pad, FrameW, FrameH);
			Image frameBack = _frame.gameObject.AddComponent<Image>();
			frameBack.color = new Color(0.17f, 0.14f, 0.11f, 1f);
			frameBack.raycastTarget = false;
			_frame.gameObject.AddComponent<RectMask2D>();
			_portraitRect = NewRect("LOM_NameTipPortrait", _frame);
			_portraitRect.anchorMin = _portraitRect.anchorMax = new Vector2(0f, 1f);
			_portraitRect.pivot = new Vector2(0f, 1f);
			_portrait = _portraitRect.gameObject.AddComponent<Image>();
			_portrait.raycastTarget = false;
			_portrait.preserveAspect = false;

			_name = NewText("LOM_NameTipName", 30, FontStyle.Bold, new Color(0.98f, 0.92f, 0.78f), TextAnchor.UpperLeft);
			_name.resizeTextForBestFit = true;
			_name.resizeTextMinSize = 18;
			_name.resizeTextMaxSize = 30;
			_title = NewText("LOM_NameTipTitle", 20, FontStyle.Italic, new Color(0.8f, 0.72f, 0.6f), TextAnchor.UpperLeft);
			_title.resizeTextForBestFit = true;
			_title.resizeTextMinSize = 14;
			_title.resizeTextMaxSize = 20;
			_rule = NewRect("LOM_NameTipRule", _panel);
			Image ruleImage = _rule.gameObject.AddComponent<Image>();
			ruleImage.color = new Color(0.66f, 0.53f, 0.33f, 0.5f);
			ruleImage.raycastTarget = false;
			_ruleImage = ruleImage;
			_affinityLabel = NewText("LOM_NameTipAffinity", 21, FontStyle.Normal, new Color(0.8f, 0.72f, 0.6f), TextAnchor.MiddleLeft);
			_level = NewText("LOM_NameTipLevel", 22, FontStyle.Bold, new Color(0.93f, 0.78f, 0.47f), TextAnchor.MiddleRight);
			_barBack = NewRect("LOM_NameTipBar", _panel);
			Image barBackImage = _barBack.gameObject.AddComponent<Image>();
			barBackImage.color = new Color(0.25f, 0.2f, 0.16f, 1f);
			barBackImage.raycastTarget = false;
			_barFill = NewRect("LOM_NameTipBarFill", _barBack);
			_barFill.anchorMin = new Vector2(0f, 0f);
			_barFill.anchorMax = new Vector2(0f, 1f);
			_barFill.pivot = new Vector2(0f, 0.5f);
			Image fillImage = _barFill.gameObject.AddComponent<Image>();
			fillImage.color = new Color(0.86f, 0.66f, 0.35f, 1f);
			fillImage.raycastTarget = false;
			_value = NewText("LOM_NameTipValue", 18, FontStyle.Normal, new Color(0.8f, 0.72f, 0.6f), TextAnchor.MiddleRight);
			// One-line labels: a font whose line is taller than the box (Source Han Serif, about 1.44 em) would otherwise draw
			// nothing at all under Truncate. The name and title keep Truncate, which best fit needs to shrink them.
			foreach (Text label in new Text[3] { _affinityLabel, _level, _value })
			{
				label.verticalOverflow = VerticalWrapMode.Overflow;
				label.horizontalOverflow = HorizontalWrapMode.Overflow;
			}
			_desc = NewText("LOM_NameTipDesc", 19, FontStyle.Normal, new Color(0.88f, 0.84f, 0.76f), TextAnchor.UpperLeft);
			_desc.verticalOverflow = VerticalWrapMode.Overflow;
			_desc.lineSpacing = 0.95f;
			_desc.enabled = false;
		}

		private static RectTransform NewRect(string name, Transform parent)
		{
			GameObject go = new GameObject(name, typeof(RectTransform));
			RectTransform rt = go.GetComponent<RectTransform>();
			rt.SetParent(parent, worldPositionStays: false);
			return rt;
		}

		private Text NewText(string name, int size, FontStyle style, Color color, TextAnchor align)
		{
			RectTransform rt = NewRect(name, _panel);
			Text t = rt.gameObject.AddComponent<Text>();
			t.fontSize = size;
			t.fontStyle = style;
			t.color = color;
			t.alignment = align;
			t.supportRichText = false;
			t.raycastTarget = false;
			t.horizontalOverflow = HorizontalWrapMode.Wrap;
			t.verticalOverflow = VerticalWrapMode.Truncate;
			t.lineSpacing = 1f;
			return t;
		}

		private static void Stretch(RectTransform rt, float inset)
		{
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.offsetMin = new Vector2(inset, inset);
			rt.offsetMax = new Vector2(0f - inset, 0f - inset);
		}

		/// <summary>Place a child by its top-left corner inside the panel (y down from the panel's top).</summary>
		private static void TopLeft(RectTransform rt, float x, float y, float w, float h)
		{
			rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
			rt.pivot = new Vector2(0f, 1f);
			rt.anchoredPosition = new Vector2(x, 0f - y);
			rt.sizeDelta = new Vector2(w, h);
		}

		private void SetFont(Font font)
		{
			if (font == null)
			{
				try
				{
					font = Resources.GetBuiltinResource<Font>("Arial.ttf");
				}
				catch (Exception)
				{
					return;
				}
			}
			if (font == _font)
			{
				return;
			}
			_font = font;
			foreach (Text t in new Text[6] { _name, _title, _affinityLabel, _level, _value, _desc })
			{
				if (t != null)
				{
					t.font = font;
				}
			}
		}

		public void Show(NameTips.TipCharacter c, Font font, Sprite portrait, bool portraitPending, bool affinity, int value, Vector2 anchorTop, Vector2 anchorBottom)
		{
			// Called every frame while a name is pointed at: redo nothing when nothing changed.
			int key = (affinity ? (value + 1) : 0) * 4 + (portrait != null ? 2 : 0) + (portraitPending ? 1 : 0);
			if (Visible && _shown == c && key == _shownKey && font == _font && anchorTop == _anchorTop && anchorBottom == _anchorBottom && (portrait == null || portrait == _portrait.sprite))
			{
				return;
			}
			_shownKey = key;
			SetFont(font);
			_shown = c;
			_anchorTop = anchorTop;
			_anchorBottom = anchorBottom;
			_desc.enabled = false;
			_name.color = NameColor;
			_border.color = BorderGold;
			_ruleImage.color = new Color(BorderGold.r, BorderGold.g, BorderGold.b, 0.5f);
			bool framed = portrait != null || portraitPending;
			_frame.gameObject.SetActive(framed);
			if (portrait != null)
			{
				SetPortrait(c, portrait);
			}
			else
			{
				_portrait.enabled = false;
			}
			float x0 = framed ? (Pad + FrameW + Gap) : (Pad + 4f);
			string name = c.Name ?? (c.Aliases.Length > 0 ? c.Aliases[0] : c.Id);
			_name.text = name;
			_title.text = c.Title ?? "";
			bool hasTitle = !string.IsNullOrEmpty(c.Title);
			_title.enabled = hasTitle;
			// Without the affinity row the column only needs the name and title (measured at their largest size).
			float column = ColumnW;
			if (!affinity)
			{
				float need = Mathf.Max(_name.preferredWidth, hasTitle ? _title.preferredWidth : 0f);
				column = Mathf.Clamp(Mathf.Ceil(need) + 8f, 120f, ColumnW);
			}
			float width = x0 + column + Pad;
			float textHeight = 38f + (hasTitle ? 30f : 0f) + (affinity ? 72f : 0f);
			float height = Mathf.Max(framed ? (FrameH + Pad * 2f) : 0f, textHeight + Pad * 2f + 4f);
			float y = affinity ? (Pad + 2f) : Mathf.Max(Pad + 2f, (height - textHeight) / 2f);
			TopLeft((RectTransform)_name.transform, x0, y, column, 38f);
			y += 38f;
			if (hasTitle)
			{
				TopLeft((RectTransform)_title.transform, x0, y, column, 28f);
				y += 30f;
			}
			_rule.gameObject.SetActive(affinity);
			_affinityLabel.enabled = affinity;
			_level.enabled = affinity;
			_barBack.gameObject.SetActive(affinity);
			_value.enabled = affinity;
			if (affinity)
			{
				y += 6f;
				TopLeft(_rule, x0, y, ColumnW, 1f);
				y += 8f;
				TopLeft((RectTransform)_affinityLabel.transform, x0, y, ColumnW * 0.6f, 28f);
				TopLeft((RectTransform)_level.transform, x0 + ColumnW * 0.4f, y, ColumnW * 0.6f, 28f);
				_affinityLabel.text = RelationshipHover.Label();
				_level.text = "Level " + (value / 10).ToString(CultureInfo.InvariantCulture);
				y += 32f;
				float barW = ColumnW - 74f;
				TopLeft(_barBack, x0, y + 8f, barW, 9f);
				float progress = (value >= 100) ? 1f : ((value % 10) / 10f);
				_barFill.anchoredPosition = Vector2.zero;
				_barFill.sizeDelta = new Vector2(barW * progress, 0f);
				TopLeft((RectTransform)_value.transform, x0 + barW, y - 2f, 74f, 26f);
				_value.text = value.ToString(CultureInfo.InvariantCulture) + " / 100";
			}
			_panel.sizeDelta = new Vector2(width, height);
			Place();
			_group.alpha = 1f;
		}

		/// <summary>A faction's tip: its name in the link colour over a rule, and its description below.</summary>
		public void ShowFaction(NameTips.TipCharacter c, Font font, Color32 accent, Vector2 anchorTop, Vector2 anchorBottom)
		{
			if (Visible && _shown == c && _shownKey == -2 && font == _font && anchorTop == _anchorTop && anchorBottom == _anchorBottom)
			{
				return;
			}
			_shownKey = -2;
			SetFont(font);
			_shown = c;
			_anchorTop = anchorTop;
			_anchorBottom = anchorBottom;
			_frame.gameObject.SetActive(false);
			_title.enabled = false;
			_affinityLabel.enabled = false;
			_level.enabled = false;
			_value.enabled = false;
			_barBack.gameObject.SetActive(false);
			Color edge = accent;
			_border.color = new Color(edge.r * 0.78f, edge.g * 0.78f, edge.b * 0.78f, 0.95f);
			_ruleImage.color = new Color(edge.r, edge.g, edge.b, 0.5f);
			_name.color = Color.Lerp(edge, Color.white, 0.25f);
			_name.text = c.Name ?? (c.Aliases.Length > 0 ? c.Aliases[0] : "");
			float x0 = Pad + 4f;
			float y = Pad + 2f;
			TopLeft((RectTransform)_name.transform, x0, y, DescW, 38f);
			y += 40f;
			_rule.gameObject.SetActive(true);
			TopLeft(_rule, x0, y, DescW, 1f);
			y += 9f;
			_desc.enabled = true;
			_desc.text = c.Description ?? "";
			// Measured at the column's width: set that first, then ask for the height its lines need.
			TopLeft((RectTransform)_desc.transform, x0, y, DescW, 400f);
			float h = Mathf.Ceil(_desc.preferredHeight) + 4f;
			TopLeft((RectTransform)_desc.transform, x0, y, DescW, h);
			y += h;
			_panel.sizeDelta = new Vector2(x0 + DescW + Pad, y + Pad);
			Place();
			_group.alpha = 1f;
		}

		/// <summary>The crop window (head and shoulders) of the stage art fills the frame.</summary>
		private void SetPortrait(NameTips.TipCharacter c, Sprite sprite)
		{
			float w = sprite.rect.width;
			float h = sprite.rect.height;
			if (w <= 0f || h <= 0f)
			{
				_portrait.enabled = false;
				return;
			}
			float unit = h / NameTips.PortraitRefHeight;
			float cropH = NameTips.CropHeight * unit;
			float cropW = cropH * (FrameW / FrameH);
			float scale = FrameH / cropH;
			float left = c.CropX * w - cropW / 2f;
			float top = Mathf.Max(0f, c.CropTop * h - NameTips.CropMargin * unit);
			_portrait.sprite = sprite;
			_portrait.enabled = true;
			_portraitRect.sizeDelta = new Vector2(w * scale, h * scale);
			_portraitRect.anchoredPosition = new Vector2((0f - left) * scale, top * scale);
		}

		/// <summary>Above the name, centred on it; below when there is no room above; always inside the screen.</summary>
		private void Place()
		{
			Rect canvas = _canvasRect.rect;
			Vector2 size = _panel.sizeDelta;
			RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, _anchorTop, null, out Vector2 top);
			RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, _anchorBottom, null, out Vector2 bottom);
			float margin = 8f;
			float x = Mathf.Clamp(top.x, canvas.xMin + size.x / 2f + margin, canvas.xMax - size.x / 2f - margin);
			float y;
			if (top.y + 10f + size.y <= canvas.yMax - margin)
			{
				_panel.pivot = new Vector2(0.5f, 0f);
				y = top.y + 10f;
			}
			else
			{
				_panel.pivot = new Vector2(0.5f, 1f);
				y = Mathf.Max(bottom.y - 10f, canvas.yMin + size.y + margin);
			}
			_panel.anchoredPosition = new Vector2(x, y);
		}

		public void PortraitArrived(NameTips.TipCharacter c)
		{
			if (_shown != c || !Visible)
			{
				return;
			}
			bool framed = _frame.gameObject.activeSelf;
			if (c.State == NameTips.PortraitState.Ready && c.Portrait != null && framed)
			{
				SetPortrait(c, c.Portrait);
			}
			else if (c.State != NameTips.PortraitState.Loading)
			{
				// Layout changes (the frame comes or goes): the next frame's hover test shows the tip again with it.
				Hide();
			}
		}

		public void Hide()
		{
			if (_group != null)
			{
				_group.alpha = 0f;
			}
			_shown = null;
		}

		private void OnDestroy()
		{
			_shown = null;
		}
	}
}
