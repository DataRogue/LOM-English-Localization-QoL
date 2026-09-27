using System;
using BepInEx.Configuration;
using HarmonyLib;
using Mortal.Battle;
using Mortal.Combat;
using Mortal.Core;
using Mortal.Free;
using Mortal.Story;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SceneController = Mortal.Core.SceneController;

namespace LOM_UI_EN
{
	/// <summary>
	/// Quick save / quick load hotkeys. Both use the game's current manual slot (SaveSystem.CurrentSlot: the slot the game
	/// was started or last loaded from, and the one the in-game Save button writes to), and they are allowed exactly when the
	/// game's own Save / Load menu items are reachable and enabled in the current scene:
	///   Story      - menu reachable (no log/status/inner panel, hotkeys enabled); Save only while the Lua script has unlocked
	///                manual saving (MenuPanel.ToggleSaveButton), Load whenever the menu has a Load button.
	///   Free       - not processing an action, no status/save panel open; Save always (the map's Save button), Load if the menu has one.
	///   Battle     - pause reachable (ready, not game over, level allows pausing); Load only (the pause menu has no Save).
	///   Combat     - hotkeys enabled, status panel closed; Load only (the duel menu has no Save).
	///   Title      - Load only, while no panel is open.
	/// </summary>
	public static class QuickSave
	{
		public static ConfigEntry<bool> CfgEnabled;
		public static ConfigEntry<KeyCode> CfgSaveKey;
		public static ConfigEntry<KeyCode> CfgLoadKey;
		public static ConfigEntry<bool> CfgToast;
		public static ConfigEntry<string> CfgSavedText;
		public static ConfigEntry<string> CfgLoadingText;

		private const string LoadingScene = "Loading1";
		private static bool _loadInFlight;
		private static float _loadStartedAt;
		/// <summary>A quick load threw part-way: the game state may be half loaded, so no quick save until the next scene loads.</summary>
		private static bool _loadFailed;
		private static bool _hooked;

		private struct Gate
		{
			public bool Save;
			public bool Load;
			public string SaveWhy;
			public string LoadWhy;
			public string Slot;

			public static Gate None(string why)
			{
				return new Gate { SaveWhy = why, LoadWhy = why };
			}
		}

		public static void Bind(ConfigFile cfg)
		{
			CfgEnabled = cfg.Bind("QuickSave", "Enabled", true, "Quick save / quick load hotkeys. Both use the current manual save slot (the one the in-game Save button writes to) and only work when the game's own Save / Load menu items would be available: story scenes while manual saving is unlocked, the daily-life map, loading from the battle/duel pause menu and the title screen.");
			CfgSaveKey = cfg.Bind("QuickSave", "SaveKey", KeyCode.F5, "Hotkey: save to the current manual slot.");
			CfgLoadKey = cfg.Bind("QuickSave", "LoadKey", KeyCode.F9, "Hotkey: reload the current manual slot (no confirmation; unsaved progress is lost).");
			CfgToast = cfg.Bind("QuickSave", "Toast", true, "Show a short on-screen message when a quick save / load happens or is refused.");
			CfgSavedText = cfg.Bind("QuickSave", "SavedText", "Quick saved: {0}", "Message after a quick save. {0} = slot label (System/SaveSlotText, e.g. 'Legend 003'), {1} = in-game date.");
			CfgLoadingText = cfg.Bind("QuickSave", "LoadingText", "Loading {0}", "Message when a quick load starts. {0} = slot label, {1} = in-game date of the save.");
			if (!_hooked)
			{
				_hooked = true;
				SceneManager.sceneLoaded += OnSceneLoaded;
			}
		}

		private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			if (scene.name != LoadingScene)
			{
				_loadInFlight = false;
				_loadFailed = false;
			}
		}

		/// <summary>
		/// Plugin.Awake: every private field the scene gates read by name, per scene. A missing one (a game update renamed it) turns
		/// quick save off in that scene only, because a gate that reads a default instead of the real state could save mid-panel.
		/// </summary>
		public static void Probe()
		{
			// Types by name (assembly, full name): this method names no game type, so a type a game update removed or renamed
			// costs the checks of its own part, not the whole feature.
			const string Core = "Mortal.Core";
			Req("Mortal.Story", "Mortal.Story.StoryManager", "story", "_panelOpen", "_menuOpen", "_logOpen", "_statusOpen", "_menuPanelController", "EnableAction", "Instance");
			Req(Core, "Mortal.Core.MenuPanel", "story", "_continueButton", "_saveButton", "_loadButton");
			Req("Mortal.Free", "Mortal.Free.FreeManager", "map", "_menuOpen", "_statOpen", "_saveOpen", "_menuPanel", "IsProcessing", "EnableHotKey", "Instance");
			Req(Core, "Mortal.Core.MenuPanel", "map", "_continueButton", "_loadButton");
			Req("Mortal.Battle", "Mortal.Battle.GameLevelManager", "battle", "_levelData", "_pausePanel", "IsGameOver", "IsReady", "EnableHotKey", "IsPause", "Instance");
			Req("Mortal.Battle", "Mortal.Battle.BattleLevel", "battle", "DisablePause");
			Req("Mortal.Battle", "Mortal.Battle.PausePanel", "battle", "_loadButton", "_continueButton");
			Req("Mortal.Combat", "Mortal.Combat.CombatUIManager", "duel", "_menuButtonToggle", "_statusButtonToggle", "_menuPanel", "Instance");
			Req("Mortal.Combat", "Mortal.Combat.CombatManager", "duel", "EnableHotKey", "Instance");
			Req(Core, "Mortal.Core.MenuPanel", "duel", "_continueButton", "_loadButton");
			Req(Core, "Mortal.Core.TitleManager", "title", "_startButton", "Instance");
			// Needed everywhere: without these the feature is off.
			Req(Core, "Mortal.Core.SaveSystem", null, "CurrentSlot", "SetSlot", "SaveGameData", "LoadGameData", "GetSaveData", "Instance");
			Req(Core, "Mortal.Core.SceneController", null, "IsPrepare", "IsLoading", "CurrentScene", "LoadCurrentScene", "Instance");
		}

		private static void Req(string assembly, string type, string part, params string[] members)
		{
			foreach (string m in members)
			{
				Compat.Require(F.QuickSave, assembly, type, m, essential: part == null, part: part);
			}
		}

		/// <summary>Called from Plugin.Update every frame.</summary>
		public static void Update()
		{
			if (CfgEnabled == null || !CfgEnabled.Value || ModMenu.IsOpen)
			{
				return;
			}
			if (_loadInFlight && Time.realtimeSinceStartup - _loadStartedAt > 30f)
			{
				_loadInFlight = false;
			}
			if (Input.GetKeyDown(CfgSaveKey.Value))
			{
				TrySave();
			}
			if (Input.GetKeyDown(CfgLoadKey.Value))
			{
				TryLoad();
			}
		}

		public static void TrySave()
		{
			try
			{
				Gate g = Check();
				if (!g.Save)
				{
					Refuse("save", g.SaveWhy);
					return;
				}
				SaveSystem ss = SaveSystem.Instance;
				ss.SaveGameData();
				GameSave data = ss.GetSaveData(g.Slot);
				if (data == null)
				{
					Plugin.Log.LogWarning("quick save: slot " + g.Slot + " has no data after SaveGameData (see [SaveSystem] lines above)");
					Toast("Quick save failed");
					return;
				}
				Plugin.Log.LogInfo("quick save: slot " + g.Slot + " (" + SceneController.Instance.CurrentScene + ")");
				Toast(Format(CfgSavedText.Value, "Quick saved: {0}", g.Slot, data));
			}
			catch (Exception ex)
			{
				// Counted against the feature: repeated failures (a changed save system) turn quick save off with a notice.
				F.QuickSave.Fail(ex, "quick save");
				Toast("Quick save failed");
			}
		}

		public static void TryLoad()
		{
			try
			{
				Gate g = Check();
				if (!g.Load)
				{
					Refuse("load", g.LoadWhy);
					return;
				}
				SaveSystem ss = SaveSystem.Instance;
				GameSave data = ss.GetSaveData(g.Slot);
				if (data == null)
				{
					Refuse("load", "No save in slot " + g.Slot);
					return;
				}
				// Same sequence as the game's RecentSaveSlotPanel / LoadSlotPanel.OnTitleClick.
				_loadInFlight = true;
				_loadStartedAt = Time.realtimeSinceStartup;
				Plugin.Log.LogInfo("quick load: slot " + g.Slot + " from " + SceneController.Instance.CurrentScene + " -> " + data.CurrentScene);
				Toast(Format(CfgLoadingText.Value, "Loading {0}", g.Slot, data));
				ss.SetSlot(g.Slot);
				if (SoundManager.Instance != null)
				{
					SoundManager.Instance.StopMusic();
				}
				ss.LoadGameData();
				MissionManagerData.Instance.UpdateCheckMissions();
				SceneController.Instance.LoadCurrentScene();
			}
			catch (Exception ex)
			{
				_loadInFlight = false;
				_loadFailed = true;
				F.QuickSave.Fail(ex, "quick load");
				Toast("Quick load failed");
			}
		}

		private static void Refuse(string what, string why)
		{
			if (Plugin.CfgVerbose.Value)
			{
				Plugin.Log.LogInfo("quick " + what + " refused: " + why);
			}
			Toast(why);
		}

		// ---- gating -------------------------------------------------------------------------------------------------

		private static Gate Check()
		{
			SaveSystem ss = SaveSystem.Instance;
			if (ss == null)
			{
				return Gate.None("Save system not ready");
			}
			SceneController sc = SceneController.Instance;
			if (sc == null || sc.IsPrepare || sc.IsLoading || _loadInFlight || SceneManager.GetSceneByName(LoadingScene).isLoaded)
			{
				return Gate.None("Loading");
			}
			if (string.IsNullOrEmpty(ss.CurrentSlot))
			{
				return Gate.None("No save slot selected");
			}
			Gate g;
			string part;
			switch (sc.CurrentScene)
			{
			case "Story":
				part = "story";
				break;
			case "Free":
				part = "map";
				break;
			case "Battle":
				part = "battle";
				break;
			case "Combat":
				part = "duel";
				break;
			case "Title":
				part = "title";
				break;
			default:
				part = null;
				break;
			}
			if (part != null && !F.QuickSave.PartOk(part))
			{
				g = Gate.None(NotHere);
			}
			else
			{
				try
				{
					switch (part)
					{
					case "story":
						g = CheckStory();
						break;
					case "map":
						g = CheckFree();
						break;
					case "battle":
						g = CheckBattle();
						break;
					case "duel":
						g = CheckCombat();
						break;
					case "title":
						g = CheckTitle();
						break;
					default:
						g = Gate.None("Not available in this scene");
						break;
					}
				}
				catch (Exception ex) when (part != null && Compat.IsStructural(ex))
				{
					// This scene's gate reads a game type or member that changed: only this scene loses quick save.
					F.QuickSave.BreakPart(part, ex.GetType().Name + ": " + ex.Message);
					g = Gate.None(NotHere);
				}
			}
			if (_loadFailed && g.Save)
			{
				g.Save = false;
				g.SaveWhy = "The last quick load failed; save again after the next scene loads";
			}
			g.Slot = ss.CurrentSlot;
			return g;
		}

		private const string NotHere = "Quick save does not work here with this game version (see Mod Settings > Compatibility)";

		private static Gate CheckStory()
		{
			StoryManager sm = StoryManager.Instance;
			if (sm == null)
			{
				return Gate.None("Story not ready");
			}
			Traverse t = Traverse.Create(sm);
			bool panelOpen = t.Field("_panelOpen").GetValue<bool>();
			bool menuOpen = t.Field("_menuOpen").GetValue<bool>();
			bool logOpen = t.Field("_logOpen").GetValue<bool>();
			bool statusOpen = t.Field("_statusOpen").GetValue<bool>();
			MenuPanel mp = t.Field("_menuPanelController").GetValue<MenuPanel>();
			// StoryManager.MenuActionChanged: the menu opens only under these conditions (EnableAction is cleared while an inner panel is up).
			if (!sm.EnableAction || logOpen || statusOpen || (panelOpen && !menuOpen))
			{
				return Gate.None("Close the open panel first");
			}
			if (menuOpen && !ButtonUsable(mp, "_continueButton"))
			{
				return Gate.None("Close the open panel first");
			}
			Gate g = default(Gate);
			g.Save = ButtonUsable(mp, "_saveButton");
			g.SaveWhy = (g.Save ? null : "Manual save is locked here");
			g.Load = ButtonUsable(mp, "_loadButton");
			g.LoadWhy = (g.Load ? null : "Load is not available here");
			return g;
		}

		private static Gate CheckFree()
		{
			FreeManager fm = FreeManager.Instance;
			if (fm == null)
			{
				return Gate.None("Map not ready");
			}
			Traverse t = Traverse.Create(fm);
			bool menuOpen = t.Field("_menuOpen").GetValue<bool>();
			bool statOpen = t.Field("_statOpen").GetValue<bool>();
			bool saveOpen = t.Field("_saveOpen").GetValue<bool>();
			CommonPanel menuPanel = t.Field("_menuPanel").GetValue<CommonPanel>();
			MenuPanel mp = ((menuPanel != null) ? menuPanel.GetComponentInChildren<MenuPanel>(includeInactive: true) : null);
			// FreeManager.SaveActionPerformed / MenuActionChanged: nothing while an action plays out or another panel is up.
			if (fm.IsProcessing || statOpen || saveOpen)
			{
				return Gate.None("Close the open panel first");
			}
			if (!menuOpen && !fm.EnableHotKey)
			{
				return Gate.None("Close the open panel first");
			}
			Gate g = default(Gate);
			if (menuOpen)
			{
				if (!ButtonUsable(mp, "_continueButton"))
				{
					return Gate.None("Close the open panel first");
				}
				g.Save = false;
				g.SaveWhy = "Close the menu first";
			}
			else
			{
				g.Save = true;
			}
			g.Load = ButtonUsable(mp, "_loadButton");
			g.LoadWhy = (g.Load ? null : "Load is not available here");
			return g;
		}

		private static Gate CheckBattle()
		{
			GameLevelManager glm = GameLevelManager.Instance;
			if (glm == null)
			{
				return Gate.None("Battle not ready");
			}
			Traverse t = Traverse.Create(glm);
			BattleLevel level = t.Field("_levelData").GetValue<BattleLevel>();
			// GameLevelManager.MenuActionChange: CanControl() && !_levelData.DisablePause
			if (glm.IsGameOver || !glm.IsReady || !glm.EnableHotKey || level == null || level.DisablePause)
			{
				return Gate.None("Not available right now");
			}
			Gate g = default(Gate);
			g.Save = false;
			g.SaveWhy = "No manual save during a battle";
			if (glm.IsPause)
			{
				PausePanel pp = t.Field("_pausePanel").GetValue<PausePanel>();
				g.Load = ButtonUsable(pp, "_loadButton") && ButtonUsable(pp, "_continueButton");
			}
			else
			{
				g.Load = true;
			}
			g.LoadWhy = (g.Load ? null : "Close the open panel first");
			return g;
		}

		private static Gate CheckCombat()
		{
			CombatManager cm = CombatManager.Instance;
			CombatUIManager ui = CombatUIManager.Instance;
			if (cm == null || ui == null)
			{
				return Gate.None("Duel not ready");
			}
			Traverse t = Traverse.Create(ui);
			Toggle menuToggle = t.Field("_menuButtonToggle").GetValue<Toggle>();
			Toggle statusToggle = t.Field("_statusButtonToggle").GetValue<Toggle>();
			CommonPanel menuPanel = t.Field("_menuPanel").GetValue<CommonPanel>();
			MenuPanel mp = ((menuPanel != null) ? menuPanel.GetComponentInChildren<MenuPanel>(includeInactive: true) : null);
			bool menuOpen = menuToggle != null && menuToggle.isOn;
			// CombatUIManager.MenuActionChanged: EnableHotKey && !_statusButtonToggle.isOn
			if (!cm.EnableHotKey || (statusToggle != null && statusToggle.isOn))
			{
				return Gate.None("Not available right now");
			}
			if (menuOpen && !ButtonUsable(mp, "_continueButton"))
			{
				return Gate.None("Close the open panel first");
			}
			Gate g = default(Gate);
			g.Save = ButtonUsable(mp, "_saveButton");
			g.SaveWhy = (g.Save ? null : "No manual save during a duel");
			g.Load = ButtonUsable(mp, "_loadButton");
			g.LoadWhy = (g.Load ? null : "Load is not available here");
			return g;
		}

		private static Gate CheckTitle()
		{
			TitleManager tm = TitleManager.Instance;
			if (tm == null)
			{
				return Gate.None("Title not ready");
			}
			Button start = Traverse.Create(tm).Field("_startButton").GetValue<Button>();
			if (start == null || !start.interactable)
			{
				return Gate.None("Close the open panel first");
			}
			Gate g = default(Gate);
			g.Save = false;
			g.SaveWhy = "Nothing to save on the title screen";
			g.Load = true;
			return g;
		}

		private static bool ButtonUsable(object owner, string field)
		{
			if (owner == null)
			{
				return false;
			}
			Button b = Traverse.Create(owner).Field(field).GetValue<Button>();
			return b != null && b.interactable;
		}

		// ---- messages -----------------------------------------------------------------------------------------------

		private static string Format(string format, string fallback, string slot, GameSave data)
		{
			string label = SlotLabel(slot);
			string date = GameDate(data);
			try
			{
				return string.Format(string.IsNullOrEmpty(format) ? fallback : format, label, date);
			}
			catch (FormatException)
			{
				return string.Format(fallback, label, date);
			}
		}

		private static string SlotLabel(string slot)
		{
			try
			{
				string format = LocalizationManager.Instance.LocaleResolver.GetString("System/SaveSlotText");
				if (!string.IsNullOrEmpty(format) && format.Contains("{0}"))
				{
					return string.Format(format, slot);
				}
			}
			catch (Exception)
			{
			}
			return "Slot " + slot;
		}

		private static string GameDate(GameSave data)
		{
			if (data == null)
			{
				return "";
			}
			try
			{
				ILocaleResolver r = LocalizationManager.Instance.LocaleResolver;
				GameTime gt = new GameTime(data.CurrentYear, data.CurrentMonth, data.CurrentStage);
				string format = r.GetString("System/GameTimeText");
				return string.Format(format, r.GetString(gt.YearText), r.GetString(gt.MonthText), r.GetString(gt.StageText));
			}
			catch (Exception)
			{
				return "";
			}
		}

		private static void Toast(string text)
		{
			if (CfgToast.Value && !string.IsNullOrEmpty(text))
			{
				QuickSaveToast.Show(text);
			}
		}
	}

	/// <summary>A small top-centre message on its own overlay canvas that survives scene loads; fades on unscaled time so it works while the game is paused.</summary>
	public class QuickSaveToast : MonoBehaviour
	{
		private static QuickSaveToast _instance;
		private CanvasGroup _group;
		private Text _text;
		private float _hideAt;
		private const float HoldSeconds = 2f;
		private const float FadeSeconds = 0.6f;

		public static void Show(string text)
		{
			Show(text, HoldSeconds);
		}

		public static void Show(string text, float seconds)
		{
			try
			{
				if (_instance == null || _instance._text == null || _instance._group == null)
				{
					// Not made yet, or a build that failed half-way (dropped, so it is not left behind with each retry).
					if (_instance != null)
					{
						UnityEngine.Object.Destroy(_instance.gameObject);
						_instance = null;
					}
					_instance = Create();
				}
				_instance.Display(text, seconds);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("quick save toast failed: " + ex.Message);
			}
		}

		private static QuickSaveToast Create()
		{
			GameObject root = new GameObject("QuickSaveToast");
			UnityEngine.Object.DontDestroyOnLoad(root);
			Canvas canvas = root.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 30000;
			CanvasScaler scaler = root.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920f, 1080f);
			scaler.matchWidthOrHeight = 0.5f;
			QuickSaveToast toast = root.AddComponent<QuickSaveToast>();
			// Set before the parts are built: a notice raised while building (a hook on Text) finds this one, not a second toast.
			_instance = toast;

			GameObject box = new GameObject("QuickSaveToastBox", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
			RectTransform brt = box.GetComponent<RectTransform>();
			brt.SetParent(root.transform, worldPositionStays: false);
			brt.anchorMin = new Vector2(0.5f, 1f);
			brt.anchorMax = new Vector2(0.5f, 1f);
			brt.pivot = new Vector2(0.5f, 1f);
			brt.anchoredPosition = new Vector2(0f, -40f);
			brt.sizeDelta = new Vector2(640f, 56f);
			Image bg = box.GetComponent<Image>();
			bg.color = new Color(0.05f, 0.04f, 0.03f, 0.82f);
			bg.raycastTarget = false;
			toast._group = box.GetComponent<CanvasGroup>();
			toast._group.alpha = 0f;
			toast._group.interactable = false;
			toast._group.blocksRaycasts = false;

			GameObject tg = new GameObject("QuickSaveToastText", typeof(RectTransform), typeof(Text));
			RectTransform trt = tg.GetComponent<RectTransform>();
			trt.SetParent(brt, worldPositionStays: false);
			trt.anchorMin = Vector2.zero;
			trt.anchorMax = Vector2.one;
			trt.offsetMin = new Vector2(16f, 4f);
			trt.offsetMax = new Vector2(-16f, -4f);
			toast._text = tg.GetComponent<Text>();
			toast._text.fontSize = 28;
			toast._text.resizeTextForBestFit = true;
			toast._text.resizeTextMinSize = 16;
			toast._text.resizeTextMaxSize = 28;
			toast._text.alignment = TextAnchor.MiddleCenter;
			toast._text.horizontalOverflow = HorizontalWrapMode.Wrap;
			toast._text.verticalOverflow = VerticalWrapMode.Truncate;
			toast._text.color = new Color(0.96f, 0.92f, 0.82f, 1f);
			toast._text.raycastTarget = false;
			return toast;
		}

		private void Display(string text, float seconds)
		{
			Font f = PickFont();
			if (f != null)
			{
				_text.font = f;
			}
			_text.text = text;
			_group.alpha = 1f;
			_hideAt = Time.realtimeSinceStartup + seconds;
		}

		/// <summary>Borrow the font of a live game Text (the game's serif face) so the toast matches the UI; scene fonts can be unloaded between scenes, so pick again on every show.</summary>
		private static Font PickFont()
		{
			try
			{
				Text[] all = Resources.FindObjectsOfTypeAll<Text>();
				Font fallback = null;
				foreach (Text t in all)
				{
					if (t == null || t.font == null || !t.gameObject.scene.IsValid() || t.name.StartsWith("QuickSaveToast"))
					{
						continue;
					}
					if (t.isActiveAndEnabled && t.font.dynamic)
					{
						return t.font;
					}
					if (fallback == null)
					{
						fallback = t.font;
					}
				}
				if (fallback != null)
				{
					return fallback;
				}
			}
			catch (Exception)
			{
			}
			try
			{
				return Resources.GetBuiltinResource<Font>("Arial.ttf");
			}
			catch (Exception)
			{
				return null;
			}
		}

		private void Update()
		{
			if (_group == null || _group.alpha <= 0f)
			{
				return;
			}
			float now = Time.realtimeSinceStartup;
			if (now < _hideAt)
			{
				return;
			}
			float a = 1f - (now - _hideAt) / FadeSeconds;
			_group.alpha = Mathf.Clamp01(a);
		}
	}
}
