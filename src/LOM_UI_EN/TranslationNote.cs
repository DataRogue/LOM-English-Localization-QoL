using System;
using System.IO;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LOM_UI_EN
{
	/// <summary>
	/// Which translation a screen shows, at a glance, so a screenshot in a bug report tells whose text it is (asked for by Lash,
	/// so that neither of us debugs the other's script by mistake):
	///  - a small note in the bottom-left corner of every screen names the translation in use: this mod's, Lash's English Patch
	///    (with its release from BepInEx/release-manifest.json), or a mix. It reads what actually serves each text layer
	///    (TranslationProfiles.TableMode / SceneMode and whether this mod's text features are working), not only the settings.
	///    Its own overlay canvas has no raycaster, so it never takes a click.
	///  - the first time the title screen comes up with both this mod and Lash's English Patch installed, a question in the Mod
	///    Settings window asks which translation to use (ModMenu.OpenTranslationPrompt). The answer, or any choice made in Mod
	///    Settings > Translation, sets [Translation] TranslationAsked; "Ask me later" or Esc asks again at the next start.
	/// Feature translationNote: everything here names only this mod's own types and the scene name, so a game update can only
	/// stop it through runtime errors; then the note and the question stay away and Mod Settings still switches.
	/// </summary>
	public static class TranslationNote
	{
		public static ConfigEntry<bool> CfgShow;
		public static ConfigEntry<bool> CfgAsked;

		/// <summary>Seconds after the title scene starts before the question shows: the title's intro fade ignores clicks.</summary>
		private const float PromptDelay = 4f;
		private const int FontSize = 15;

		private static GameObject _root;
		private static RectTransform _rt;
		private static Text _text;
		private static string _shown;
		private static float _nextRefresh;
		private static string _scene = "";
		private static float _sceneAt;
		private static bool _promptedThisSession;
		private static string _lashVersion;
		private static bool _lashVersionRead;

		private enum Src
		{
			Ours,
			Lash,
			Game
		}

		public static void Bind(ConfigFile cfg)
		{
			CfgShow = cfg.Bind("Translation", "ShowTranslationNote", true, "A small note in the bottom-left corner of the screen names the translation in use (this mod's, " + Plugin.BaseModName + ", or a mix), so a screenshot shows whose text it is.");
			CfgAsked = cfg.Bind("Translation", "TranslationAsked", false, "Set once the title-screen question about which translation to use (asked when " + Plugin.BaseModName + " is installed too) has been answered, or a translation was chosen in Mod Settings. Off = ask again at the next title screen.");
			CfgShow.SettingChanged += delegate
			{
				Invalidate();
			};
		}

		/// <summary>Plugin.Awake (Compat.Setup): when the feature turns itself off, the note and the question go away with it.</summary>
		public static void Setup()
		{
			F.TranslationNote.OnTurnedOff = Hide;
			F.TranslationNote.OnTurnedOn = Invalidate;
		}

		/// <summary>The feature turned off: no stale note left on screen, no question open.</summary>
		private static void Hide()
		{
			try
			{
				if (_root != null)
				{
					_root.SetActive(false);
				}
				_shown = null;
				if (ModMenu.IsPromptOpen)
				{
					ModMenu.Close();
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("translation note: could not hide: " + ex.Message);
			}
		}

		/// <summary>Redraw the note at the next frame (a translation switch, a detection change, a setting).</summary>
		public static void Invalidate()
		{
			_shown = null;
			_nextRefresh = 0f;
			_lashVersionRead = false;
		}

		/// <summary>Plugin.Update, every frame (Compat.Tick): cheap until the half-second refresh.</summary>
		public static void Tick()
		{
			float now = Time.realtimeSinceStartup;
			string active = SceneManager.GetActiveScene().name ?? "";
			if (active != _scene)
			{
				_scene = active;
				_sceneAt = now;
				_nextRefresh = 0f;
			}
			if (now < _nextRefresh)
			{
				return;
			}
			_nextRefresh = now + 0.5f;
			Refresh();
			// Lash's patch offers its updates in a window of its own at start; the question waits for it, and steps aside (to come
			// back once it is gone) when it opens on top.
			bool lashWindow = LashWindowOpen();
			if (lashWindow && ModMenu.IsPromptOpen)
			{
				ModMenu.Close();
				_promptedThisSession = false;
				Plugin.Log.LogInfo("translation question: closed for the update window of " + Plugin.BaseModName + "; asked again when it is gone");
			}
			if (!lashWindow)
			{
				MaybeAsk(now);
			}
		}

		/// <summary>The update window of Lash's English Patch is up (its UiEditor's UpdatePrompt, the root object FSEditorUpdatePrompt,
		/// offered at start when a newer release is out).</summary>
		private static bool LashWindowOpen()
		{
			if (!OriginalMod.Current.PackLoaded)
			{
				return false;
			}
			GameObject w = GameObject.Find("/FSEditorUpdatePrompt");
			return w != null && w.activeInHierarchy;
		}

		// ---- the corner note ---------------------------------------------------------------------------------------------

		private static void Refresh()
		{
			string text = (CfgShow.Value && EnglishLanguage.Active) ? Describe() : null;
			if (text == null)
			{
				if (_root != null && _root.activeSelf)
				{
					_root.SetActive(false);
				}
				return;
			}
			if (_root == null)
			{
				Build();
			}
			if (!_root.activeSelf)
			{
				_root.SetActive(true);
			}
			Vector2 pos = Offset(_scene);
			if (_rt.anchoredPosition != pos)
			{
				_rt.anchoredPosition = pos;
			}
			if (text != _shown)
			{
				_text.text = text;
				_shown = text;
			}
		}

		/// <summary>
		/// Where the note sits, bottom-left, in 1920x1080 units. The title screen draws the game's version (AppVersion, 20,12, 30 high)
		/// in that corner, so the note goes just above it there; the duel's fighter gauges fill the corner, so it moves right of them.
		/// </summary>
		private static Vector2 Offset(string scene)
		{
			switch (scene)
			{
			case "Title":
				return new Vector2(12f, 44f);
			case "Combat":
				return new Vector2(392f, 4f);
			default:
				return new Vector2(12f, 4f);
			}
		}

		private static void Build()
		{
			_root = new GameObject("LOM_TranslationNote");
			_root.layer = 5;
			UnityEngine.Object.DontDestroyOnLoad(_root);
			Canvas canvas = _root.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			// Over the game's screens, under this mod's own windows and tips (Mod Settings 29000, quick save 30000, name tips 30500).
			canvas.sortingOrder = 28000;
			CanvasScaler scaler = _root.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920f, 1080f);
			scaler.matchWidthOrHeight = 0.5f;
			// A faint dark plate as wide as the text keeps it readable on the title's light parchment as on the dark scenes.
			_rt = MMUi.NewRect("LOM_TranslationNote_Plate", _root.transform);
			_rt.anchorMin = _rt.anchorMax = _rt.pivot = Vector2.zero;
			MMUi.AddImage(_rt, new Color(0.04f, 0.03f, 0.02f, 0.42f));
			HorizontalLayoutGroup row = _rt.gameObject.AddComponent<HorizontalLayoutGroup>();
			row.padding = new RectOffset(8, 8, 2, 3);
			row.childControlWidth = true;
			row.childControlHeight = true;
			row.childForceExpandWidth = false;
			row.childForceExpandHeight = false;
			ContentSizeFitter fit = _rt.gameObject.AddComponent<ContentSizeFitter>();
			fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
			fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			RectTransform textRt = MMUi.NewRect("LOM_TranslationNote_Text", _rt);
			_text = MMUi.AddText(textRt, "", FontSize, new Color(0.93f, 0.9f, 0.84f, 0.88f), TextAnchor.MiddleLeft, wrap: false);
			Shadow shadow = textRt.gameObject.AddComponent<Shadow>();
			shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
			shadow.effectDistance = new Vector2(1f, -1f);
		}

		/// <summary>The note's text, from what serves each text layer right now (also the harness: invoke static ...Describe).</summary>
		public static string Describe()
		{
			OriginalMod.Report r = OriginalMod.Current;
			bool lashRunning = r.TablePluginHooked || r.XUnityRunning || r.LlmKitLoaded || r.PackLoaded;
			Src table = Source(TranslationProfiles.TableMode, F.GameText.Live, lashRunning);
			Src scene = Source(TranslationProfiles.SceneMode, F.SceneText.Live, lashRunning);
			string ours = "LOM English Localization " + Plugin.VERSION;
			string lash = Plugin.BaseModName + (LashVersion() is string v ? " " + v : "");
			if (table == scene)
			{
				switch (table)
				{
				case Src.Ours:
					return "Translation: " + ours + (r.AnyInstalled ? ("  ·  " + lash + " installed") : "");
				case Src.Lash:
					return "Translation: " + lash + "  ·  layout by LOM " + Plugin.VERSION;
				default:
					return "Translation: none, the game's own text  ·  LOM " + Plugin.VERSION;
				}
			}
			return "Translation: " + Name(table, ours, lash) + " (game data), " + Name(scene, ours, lash) + " (screen text)";
		}

		private static string Name(Src s, string ours, string lash)
		{
			switch (s)
			{
			case Src.Ours:
				return ours;
			case Src.Lash:
				return lash;
			default:
				return "the game's own text";
			}
		}

		/// <summary>Who serves a text layer: this mod while its layer is Revised and its feature works; Lash's patch when the layer is
		/// handed to it (or read from its files), or when this mod adds nothing and its plugins run; otherwise the game itself.</summary>
		private static Src Source(LayerMode m, bool live, bool lashRunning)
		{
			switch (m)
			{
			case LayerMode.Revised:
				return live ? Src.Ours : (lashRunning ? Src.Lash : Src.Game);
			case LayerMode.OriginalHandBack:
			case LayerMode.OriginalFromFiles:
			case LayerMode.BaseOnly:
				return Src.Lash;
			default:
				return lashRunning ? Src.Lash : Src.Game;
			}
		}

		/// <summary>The release of Lash's English Patch its installer recorded (BepInEx/release-manifest.json "version"), or null.</summary>
		private static string LashVersion()
		{
			if (_lashVersionRead)
			{
				return _lashVersion;
			}
			_lashVersionRead = true;
			_lashVersion = null;
			try
			{
				string path = Path.Combine(BepInEx.Paths.BepInExRootPath, "release-manifest.json");
				if (File.Exists(path))
				{
					string json = File.ReadAllText(path);
					Match m = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]{1,40})\"");
					if (m.Success && json.IndexOf("LegendOfMortalOverLlm", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						_lashVersion = m.Groups[1].Value;
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("translation note: could not read the release of " + Plugin.BaseModName + ": " + ex.Message);
			}
			return _lashVersion;
		}

		// ---- the title-screen question -----------------------------------------------------------------------------------

		/// <summary>Asks once per session, on the title screen after its intro, while the question is unanswered and both this mod and a
		/// working Lash's English Patch are there. A choice made earlier in Mod Settings counts as the answer.</summary>
		private static void MaybeAsk(float now)
		{
			if (CfgAsked.Value || _promptedThisSession || ModMenu.IsOpen || _scene != "Title" || now - _sceneAt < PromptDelay || !EnglishLanguage.Active)
			{
				return;
			}
			if (!OriginalMod.Current.AnyInstalled || (TranslationProfiles.OriginalProblem(table: true) != null && TranslationProfiles.OriginalProblem(table: false) != null))
			{
				return;
			}
			if (ModMenuPages.PresetIndex() != 0)
			{
				CfgAsked.Value = true;
				Plugin.Log.LogInfo("translation question: not asked, a translation was chosen in Mod Settings before");
				return;
			}
			_promptedThisSession = true;
			Plugin.Log.LogInfo("translation question: " + Plugin.BaseModName + " is installed too; asking which translation to use");
			ModMenu.OpenTranslationPrompt();
		}

		/// <summary>The question's answer (0 = this mod's translation, 1 = Lash's English Patch), also the harness's way to answer it:
		/// invoke static LOM_UI_EN.TranslationNote.Answer 0. Returns what changed, or why nothing did.</summary>
		public static string Answer(int preset)
		{
			string msg = ModMenuPages.ApplyPreset(preset);
			Plugin.Log.LogInfo("translation question answered: " + msg);
			Invalidate();
			return msg;
		}

		/// <summary>The harness: ask again (as on a first start with both installed) and open the question now.</summary>
		public static void AskAgain()
		{
			CfgAsked.Value = false;
			_promptedThisSession = true;
			ModMenu.OpenTranslationPrompt();
		}
	}
}
