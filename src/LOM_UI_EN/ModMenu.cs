using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using Mortal.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace LOM_UI_EN
{
	/// <summary>
	/// In-game settings window for everything this mod does. It opens from a "Mod Settings" button that ModMenuEntry adds to the
	/// title screen and to the pause menus, or from [ModMenu] OpenKey anywhere. The window is its own DontDestroyOnLoad overlay
	/// canvas built from code (no asset bundle); every control writes straight to the BepInEx config entry, which saves the .cfg
	/// file, and then runs the same code the entry drives so the change shows at once.
	///
	/// While it is open the game is paused (timeScale 0, restored on close) and every enabled Input System action except the UI
	/// module's point / click / scroll is disabled, so Escape, Space, the arrow keys and the game's own hotkeys cannot reach the
	/// menus underneath; the window reads its own keys through the legacy Input manager (the player runs with both input systems).
	/// </summary>
	public class ModMenu : MonoBehaviour
	{
		public static ConfigEntry<KeyCode> CfgOpenKey;
		public static ConfigEntry<bool> CfgTitleButton;
		public static ConfigEntry<bool> CfgPauseButton;

		private static ModMenu _instance;
		private static string _clickSound;

		private GameObject _canvasRoot;
		private CanvasGroup _group;
		private RectTransform _content;
		private ScrollRect _scroll;
		private Text _status;
		private readonly List<MMButton> _nav = new List<MMButton>();
		private List<MenuPage> _pages;
		private int _page;
		private bool _open;
		private GameObject _returnFocus;
		private float _savedTimeScale = 1f;
		private bool _pausedByUs;
		private bool _savedCursorVisible;
		private CursorLockMode _savedCursorLock;
		private readonly List<InputAction> _blocked = new List<InputAction>();
		private readonly List<InputDevice> _blockedDevices = new List<InputDevice>();
		private InputSystemUIInputModule _uiModule;
		private InputActionReference _savedPoint;
		private InputActionReference _savedClick;
		private InputActionReference _savedScroll;
		private InputAction _ownPoint;
		private InputAction _ownClick;
		private InputAction _ownScroll;
		private InputActionReference _ownPointRef;
		private InputActionReference _ownClickRef;
		private InputActionReference _ownScrollRef;
		private bool _pointerSwapped;
		private bool _ownActionsFailed;
		private bool _savedPointOn;
		private bool _savedClickOn;
		private bool _savedScrollOn;
		private InputActionAsset _ownAsset;
		private string _savedScheme;
		private KeyItem _capture;
		private int _captureFrame;
		private bool _swallowEscUp;
		private bool _typingLastFrame;
		private float _fade;
		private float _statusUntil;

		public static bool IsOpen => _instance != null && _instance._open;
		public static bool IsCapturingKey => _instance != null && _instance._capture != null;

		public static void Bind(ConfigFile cfg)
		{
			CfgOpenKey = cfg.Bind("ModMenu", "OpenKey", KeyCode.F8, "Hotkey: open or close the Mod Settings window anywhere in the game (it pauses the game while open). None = only the menu buttons.");
			CfgTitleButton = cfg.Bind("ModMenu", "TitleButton", true, "Add a 'Mod Settings' button under Settings on the title screen.");
			CfgPauseButton = cfg.Bind("ModMenu", "PauseMenuButton", true, "Add a 'Mod Settings' button to the pause menus (story, daily-life map, duels, army battles).");
		}

		public static void Patch(Harmony h)
		{
			ModMenuEntry.Patch(h);
		}

		private void Awake()
		{
			_instance = this;
		}

		// ---- open / close -----------------------------------------------------------------------------------------

		public static void Open(GameObject returnFocus)
		{
			if (_instance == null)
			{
				return;
			}
			_instance.DoOpen(returnFocus);
		}

		public static void Close()
		{
			if (_instance != null)
			{
				_instance.DoClose();
			}
		}

		public static void Toggle()
		{
			if (IsOpen)
			{
				Close();
			}
			else
			{
				Open(null);
			}
		}

		private void DoOpen(GameObject returnFocus)
		{
			if (_open)
			{
				return;
			}
			try
			{
				if (_canvasRoot == null)
				{
					Build();
				}
				_returnFocus = returnFocus;
				_open = true;
				_capture = null;
				_canvasRoot.SetActive(true);
				_fade = 0f;
				_group.alpha = 0f;
				ShowPage(_page);
				SetStatus("Changes are saved as you make them.", 0f);
				BlockGameInput();
				_savedTimeScale = Time.timeScale;
				_pausedByUs = Time.timeScale > 0f;
				if (_pausedByUs)
				{
					Time.timeScale = 0f;
				}
				_savedCursorVisible = Cursor.visible;
				_savedCursorLock = Cursor.lockState;
				_savedScheme = ControlScheme();
				Cursor.visible = true;
				Cursor.lockState = CursorLockMode.None;
				if (EventSystem.current != null)
				{
					EventSystem.current.SetSelectedGameObject(null);
				}
				Plugin.Log.LogInfo("mod menu opened");
			}
			catch (Exception ex)
			{
				Plugin.Log.LogError("mod menu failed to open: " + ex);
				_open = false;
				RestoreGameInput();
				if (_pausedByUs && Time.timeScale == 0f)
				{
					Time.timeScale = _savedTimeScale;
				}
				_pausedByUs = false;
				// A half-built window would fail the same way on every later open; drop it so the next open builds it afresh.
				if (_canvasRoot != null)
				{
					UnityEngine.Object.Destroy(_canvasRoot);
				}
				_canvasRoot = null;
				_nav.Clear();
				_pages = null;
			}
		}

		private void DoClose()
		{
			if (!_open)
			{
				return;
			}
			_open = false;
			_capture = null;
			if (EventSystem.current != null)
			{
				EventSystem.current.SetSelectedGameObject(null);
			}
			if (_canvasRoot != null)
			{
				_canvasRoot.SetActive(false);
			}
			RestoreGameInput();
			if (_pausedByUs && Time.timeScale == 0f)
			{
				Time.timeScale = _savedTimeScale;
			}
			_pausedByUs = false;
			// If the player switched between mouse and pad while the window was open, PlayerInputController has already set the
			// cursor for the new scheme; putting the old state back would hide the cursor from a mouse user.
			if (ControlScheme() == _savedScheme)
			{
				Cursor.visible = _savedCursorVisible;
				Cursor.lockState = _savedCursorLock;
			}
			if (_returnFocus != null && _returnFocus.activeInHierarchy && EventSystem.current != null)
			{
				EventSystem.current.SetSelectedGameObject(_returnFocus);
			}
			_returnFocus = null;
			Plugin.Log.LogInfo("mod menu closed");
		}

		// ---- input ------------------------------------------------------------------------------------------------

		/// <summary>
		/// Every game action goes off while the window is open, including the UI module's own point / click / scroll: those are the
		/// shared "UI/Click" etc. of the game's action asset, and CombatManager, FreeManager, GameOverPanel, CgPanel and EndGamePanel
		/// subscribe to UI/Click themselves (next line, next result, load next scene), so a click on this window would reach them.
		/// The UI module gets three private actions instead for as long as the window is up.
		/// </summary>
		private void BlockGameInput()
		{
			try
			{
				HashSet<InputAction> keep = new HashSet<InputAction>();
				if (!SwapPointer() && EventSystem.current != null && EventSystem.current.currentInputModule is InputSystemUIInputModule ui)
				{
					// Fallback if the swap failed: leave the module's shared pointer actions on so the window stays clickable
					// (clicks can then reach the game's UI/Click listeners, but a window you cannot click is worse).
					Keep(keep, ui.point);
					Keep(keep, ui.leftClick);
					Keep(keep, ui.scrollWheel);
				}
				foreach (InputAction a in InputSystem.ListEnabledActions())
				{
					if (a != null && a != _ownPoint && a != _ownClick && a != _ownScroll && !keep.Contains(a))
					{
						a.Disable();
						_blocked.Add(a);
					}
				}
				// Some scene code polls Keyboard.current directly (battle Space / 1-4 / QWE, F11), which no action covers. A disabled
				// device keeps Keyboard.current but stops updating, so those polls read 'not pressed'; the legacy Input manager this
				// window reads (and the IMGUI event queue text fields type from) is fed separately and keeps working. Gamepads stay
				// on: game code only reads Gamepad.current to pick button icons, and their actions are already off.
				if (_blockedDevices.Count == 0)
				{
					foreach (InputDevice d in InputSystem.devices)
					{
						if (d != null && d.enabled && d is Keyboard)
						{
							InputSystem.DisableDevice(d);
							_blockedDevices.Add(d);
						}
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("mod menu: could not pause the game's input: " + ex.Message);
			}
		}

		private static void Keep(HashSet<InputAction> keep, InputActionReference r)
		{
			if (r != null && r.action != null)
			{
				keep.Add(r.action);
			}
		}

		/// <summary>Builds the window's own pointer actions once. InputActionReference only accepts actions that belong to an asset.</summary>
		private bool EnsureOwnActions()
		{
			if (_ownPointRef != null && _ownClickRef != null && _ownScrollRef != null)
			{
				return true;
			}
			if (_ownActionsFailed)
			{
				return false;
			}
			try
			{
				InputActionAsset asset = ScriptableObject.CreateInstance<InputActionAsset>();
				asset.name = "MM_UIActions";
				InputActionMap map = new InputActionMap("MM_UI");
				InputAction point = map.AddAction("Point", InputActionType.PassThrough, "<Mouse>/position", null, null, null, "Vector2");
				InputAction click = map.AddAction("Click", InputActionType.PassThrough, "<Mouse>/leftButton", null, null, null, "Button");
				InputAction scroll = map.AddAction("ScrollWheel", InputActionType.PassThrough, "<Mouse>/scroll", null, null, null, "Vector2");
				asset.AddActionMap(map);
				InputActionReference pointRef = InputActionReference.Create(point);
				InputActionReference clickRef = InputActionReference.Create(click);
				InputActionReference scrollRef = InputActionReference.Create(scroll);
				// Nothing else references these ScriptableObjects; keep Resources.UnloadUnusedAssets (run on scene loads) off them.
				asset.hideFlags = HideFlags.HideAndDontSave;
				pointRef.hideFlags = HideFlags.HideAndDontSave;
				clickRef.hideFlags = HideFlags.HideAndDontSave;
				scrollRef.hideFlags = HideFlags.HideAndDontSave;
				_ownAsset = asset;
				_ownPoint = point;
				_ownClick = click;
				_ownScroll = scroll;
				_ownPointRef = pointRef;
				_ownClickRef = clickRef;
				_ownScrollRef = scrollRef;
				return true;
			}
			catch (Exception ex)
			{
				_ownActionsFailed = true;
				Plugin.Log.LogWarning("mod menu: own pointer actions unavailable, using the game's (clicks may reach the game): " + ex.Message);
				return false;
			}
		}

		/// <summary>Points the UI module at the window's own actions. False when that is not possible (no module yet, or the actions could not be built).</summary>
		private bool SwapPointer()
		{
			InputSystemUIInputModule ui = (EventSystem.current != null) ? (EventSystem.current.currentInputModule as InputSystemUIInputModule) : null;
			if (_pointerSwapped && _uiModule == ui)
			{
				return true;
			}
			if (_pointerSwapped)
			{
				// A scene load replaced the EventSystem while the window was open: hand the old module back, take the new one.
				RestorePointer();
			}
			if (ui == null || !EnsureOwnActions())
			{
				return false;
			}
			_uiModule = ui;
			_savedPoint = ui.point;
			_savedClick = ui.leftClick;
			_savedScroll = ui.scrollWheel;
			_savedPointOn = _savedPoint != null && _savedPoint.action != null && _savedPoint.action.enabled;
			_savedClickOn = _savedClick != null && _savedClick.action != null && _savedClick.action.enabled;
			_savedScrollOn = _savedScroll != null && _savedScroll.action != null && _savedScroll.action.enabled;
			// The module reference-counts the actions it enabled and switches the shared UI/Point, UI/Click, UI/ScrollWheel off
			// itself when they are swapped out; RestorePointer relies on that to get them back.
			ui.point = _ownPointRef;
			ui.leftClick = _ownClickRef;
			ui.scrollWheel = _ownScrollRef;
			_ownPoint.Enable();
			_ownClick.Enable();
			_ownScroll.Enable();
			_pointerSwapped = true;
			return true;
		}

		private void RestorePointer()
		{
			if (!_pointerSwapped)
			{
				return;
			}
			_pointerSwapped = false;
			try
			{
				// Swap back while our actions are still enabled: InputSystemUIInputModule.SwapAction only re-enables the incoming
				// action when the outgoing one was enabled, so disabling ours first would leave the game's UI/Click off after the
				// window closes (mouse dead in every menu).
				if (_uiModule != null)
				{
					_uiModule.point = _savedPoint;
					_uiModule.leftClick = _savedClick;
					_uiModule.scrollWheel = _savedScroll;
				}
				_ownPoint.Disable();
				_ownClick.Disable();
				_ownScroll.Disable();
				// Belt and braces: whatever was on before the swap is on again.
				EnsureOn(_savedPoint, _savedPointOn);
				EnsureOn(_savedClick, _savedClickOn);
				EnsureOn(_savedScroll, _savedScrollOn);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("mod menu: could not hand the pointer back to the game: " + ex.Message);
			}
			_uiModule = null;
			_savedPoint = null;
			_savedClick = null;
			_savedScroll = null;
		}

		private static void EnsureOn(InputActionReference r, bool wasOn)
		{
			if (wasOn && r != null && r.action != null && !r.action.enabled)
			{
				r.action.Enable();
			}
		}

		private static string ControlScheme()
		{
			try
			{
				return (PlayerInput.all.Count > 0) ? PlayerInput.all[0].currentControlScheme : null;
			}
			catch (Exception)
			{
				return null;
			}
		}

		private void RestoreGameInput()
		{
			RestorePointer();
			foreach (InputDevice d in _blockedDevices)
			{
				try
				{
					if (d != null && d.added && !d.enabled)
					{
						InputSystem.EnableDevice(d);
					}
				}
				catch (Exception)
				{
				}
			}
			_blockedDevices.Clear();
			foreach (InputAction a in _blocked)
			{
				try
				{
					a?.Enable();
				}
				catch (Exception)
				{
				}
			}
			_blocked.Clear();
		}

		private static bool InputFieldFocused()
		{
			GameObject sel = (EventSystem.current != null) ? EventSystem.current.currentSelectedGameObject : null;
			if (sel == null)
			{
				return false;
			}
			InputField f = sel.GetComponent<InputField>();
			if (f != null && f.isFocused)
			{
				return true;
			}
			TMPro.TMP_InputField t = sel.GetComponent<TMPro.TMP_InputField>();
			return t != null && t.isFocused;
		}

		private void Update()
		{
			try
			{
				Compat.Tick(F.MenuButtons, ModMenuEntry.Tick);
				if (!_open)
				{
					KeyCode key = CfgOpenKey.Value;
					if (key != KeyCode.None && Input.GetKeyDown(key) && !InputFieldFocused())
					{
						DoOpen(null);
					}
					return;
				}
				if (_fade < 1f)
				{
					_fade = Mathf.MoveTowards(_fade, 1f, Time.unscaledDeltaTime * 9f);
					_group.alpha = _fade;
				}
				if (Time.frameCount % 15 == 0)
				{
					BlockGameInput();
				}
				if (_capture != null)
				{
					UpdateCapture();
					return;
				}
				// Esc in a text field only ends the edit. The field may already have dropped focus this frame (the EventSystem can
				// update before us), so last frame's focus counts too.
				bool typing = InputFieldFocused();
				bool typedLastFrame = _typingLastFrame;
				_typingLastFrame = typing;
				if (Input.GetKeyDown(KeyCode.Escape))
				{
					_swallowEscUp = typing || typedLastFrame;
				}
				if (Input.GetKeyUp(KeyCode.Escape))
				{
					if (_swallowEscUp)
					{
						_swallowEscUp = false;
					}
					else
					{
						DoClose();
						return;
					}
				}
				if (CfgOpenKey.Value != KeyCode.None && Input.GetKeyDown(CfgOpenKey.Value) && !typing)
				{
					DoClose();
					return;
				}
				// Pad players reach the window through the Mod Settings button; B (joystick button 1) takes them back out.
				if (Input.GetKeyDown(KeyCode.JoystickButton1))
				{
					DoClose();
					return;
				}
				if (_status != null && _statusUntil > 0f && Time.realtimeSinceStartup > _statusUntil)
				{
					_statusUntil = 0f;
					_status.text = "Changes are saved as you make them.";
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogError("mod menu update failed: " + ex);
			}
		}

		internal void BeginCapture(KeyItem item)
		{
			_capture = item;
			_captureFrame = Time.frameCount;
			item.Refresh();
			SetStatus("Press a key for " + item.Label + ". Esc cancels, Delete clears.", 0f);
		}

		private void UpdateCapture()
		{
			if (Time.frameCount <= _captureFrame)
			{
				return;
			}
			KeyItem item = _capture;
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				_capture = null;
				_swallowEscUp = true;
				item.Refresh();
				SetStatus("Key unchanged.");
				return;
			}
			KeyCode got = (KeyCode)(-1);
			if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace))
			{
				got = KeyCode.None;
			}
			else
			{
				for (int k = 8; k < 323; k++)
				{
					KeyCode kc = (KeyCode)k;
					if (Input.GetKeyDown(kc))
					{
						got = kc;
						break;
					}
				}
			}
			if ((int)got < 0)
			{
				return;
			}
			_capture = null;
			item.Set(got);
		}

		// ---- pages --------------------------------------------------------------------------------------------------

		private void ShowPage(int index)
		{
			if (_pages == null || _pages.Count == 0)
			{
				return;
			}
			_page = Mathf.Clamp(index, 0, _pages.Count - 1);
			for (int i = 0; i < _nav.Count; i++)
			{
				_nav[i].Selected = i == _page;
				_nav[i].Apply();
			}
			for (int i = _content.childCount - 1; i >= 0; i--)
			{
				GameObject child = _content.GetChild(i).gameObject;
				child.SetActive(false);
				UnityEngine.Object.Destroy(child);
			}
			MenuPage page = _pages[_page];
			MMUi.AddText(MMUi.NewRect("MM_PageTitle", _content), page.Title, 38, MMUi.Gold, TextAnchor.UpperLeft);
			if (!string.IsNullOrEmpty(page.Intro))
			{
				MMUi.Spacer(_content, 12f);
				MMUi.AddText(MMUi.NewRect("MM_PageIntro", _content), page.Intro, 21, MMUi.Muted, TextAnchor.UpperLeft);
			}
			MMUi.Spacer(_content, 14f);
			int n = 0;
			foreach (MenuItem item in page.Items)
			{
				if (item is SectionItem)
				{
					item.BuildRow(_content, alt: false);
					n = 0;
					continue;
				}
				item.BuildRow(_content, n++ % 2 == 1);
			}
			MMUi.Spacer(_content, 24f);
			RefreshPage();
			LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
			_scroll.verticalNormalizedPosition = 1f;
		}

		private void RefreshPage()
		{
			if (_pages == null || !_open)
			{
				return;
			}
			foreach (MenuItem item in _pages[_page].Items)
			{
				try
				{
					item.Refresh();
				}
				catch (Exception ex)
				{
					Plugin.Log.LogWarning("mod menu: refresh of '" + item.Label + "' failed: " + ex.Message);
				}
			}
		}

		/// <summary>Called by every control after it changed a value: redraw the page (one change can enable or disable other rows).</summary>
		internal static void Changed(string message = null)
		{
			if (_instance == null)
			{
				return;
			}
			_instance.RefreshPage();
			_instance.SetStatus(message ?? "Saved.");
		}

		internal static void Status(string message)
		{
			_instance?.SetStatus(message);
		}

		private void SetStatus(string text, float seconds = 5f)
		{
			if (_status == null)
			{
				return;
			}
			_status.text = text;
			_statusUntil = (seconds > 0f) ? (Time.realtimeSinceStartup + seconds) : 0f;
		}

		internal static void CaptureKey(KeyItem item)
		{
			_instance?.BeginCapture(item);
		}

		internal static bool IsCapturing(KeyItem item)
		{
			return _instance != null && _instance._capture == item;
		}

		internal static void SetClickSound(string key)
		{
			if (!string.IsNullOrEmpty(key))
			{
				_clickSound = key;
			}
		}

		private static bool _clickBroken;

		internal static void PlayClick()
		{
			if (_clickSound == null || _clickBroken)
			{
				return;
			}
			try
			{
				PlayClickBody();
			}
			catch (Exception ex)
			{
				// A changed SoundManager costs the click sound only (and is not retried every click).
				_clickBroken = true;
				Plugin.Log.LogWarning("Mod Settings click sound off: " + ex.GetType().Name + ": " + ex.Message);
			}
		}

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private static void PlayClickBody()
		{
			if (SoundManager.Instance != null)
			{
				SoundManager.Instance.PlaySound(_clickSound);
			}
		}

		private void ResetPage()
		{
			int n = 0;
			foreach (MenuItem item in _pages[_page].Items)
			{
				if (item.ResetToDefault())
				{
					n++;
				}
			}
			Changed((n > 0) ? $"{_pages[_page].Title}: {n} setting(s) back to default." : "Nothing to reset on this page.");
		}

		internal static void ResetEverything()
		{
			if (_instance == null)
			{
				return;
			}
			int n = 0;
			foreach (MenuPage page in _instance._pages)
			{
				foreach (MenuItem item in page.Items)
				{
					if (item.ResetToDefault())
					{
						n++;
					}
				}
			}
			Changed($"All settings back to default ({n} changed).");
		}

		// ---- construction -----------------------------------------------------------------------------------------

		private void Build()
		{
			_pages = ModMenuPages.Create();
			_canvasRoot = new GameObject("LOM_ModMenu");
			_canvasRoot.layer = 5;
			UnityEngine.Object.DontDestroyOnLoad(_canvasRoot);
			Canvas canvas = _canvasRoot.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 29000;
			CanvasScaler scaler = _canvasRoot.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920f, 1080f);
			scaler.matchWidthOrHeight = 0.5f;
			_canvasRoot.AddComponent<GraphicRaycaster>();
			_group = _canvasRoot.AddComponent<CanvasGroup>();
			RectTransform root = (RectTransform)_canvasRoot.transform;

			RectTransform dim = MMUi.NewRect("MM_Dim", root);
			MMUi.Stretch(dim);
			MMUi.AddImage(dim, MMUi.Dim, raycast: true);

			RectTransform window = MMUi.NewRect("MM_Window", root);
			window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
			window.pivot = new Vector2(0.5f, 0.5f);
			window.sizeDelta = new Vector2(1440f, 900f);
			MMUi.AddImage(window, MMUi.WindowBg, raycast: true);
			MMUi.AddFrame(window, MMUi.Frame, 2f, 0f);
			MMUi.AddFrame(window, MMUi.FrameFaint, 1f, 9f);

			// header
			RectTransform header = MMUi.NewRect("MM_Header", window);
			MMUi.AnchorTop(header, 104f, 0f);
			RectTransform titleRt = MMUi.NewRect("MM_Title", header);
			MMUi.Stretch(titleRt, 44f, 18f, 300f, 38f);
			MMUi.AddText(titleRt, "Mod Settings", 46, MMUi.Gold, TextAnchor.UpperLeft, wrap: false);
			RectTransform subRt = MMUi.NewRect("MM_Subtitle", header);
			MMUi.Stretch(subRt, 46f, 70f, 120f, 8f);
			MMUi.AddText(subRt, Plugin.DisplayName + "  ·  version " + Plugin.VERSION, 20, MMUi.Muted, TextAnchor.UpperLeft, wrap: false);
			RectTransform close = MMUi.NewRect("MM_CloseX", header);
			close.anchorMin = close.anchorMax = new Vector2(1f, 1f);
			close.pivot = new Vector2(1f, 1f);
			close.sizeDelta = new Vector2(64f, 64f);
			close.anchoredPosition = new Vector2(-22f, -20f);
			MMButton x = MMUi.MakeButton(close, "×", 44, DoClose);
			x.SetColors(MMUi.Clear, MMUi.HoverFaint, MMUi.PressFaint, MMUi.TextMain, MMUi.Gold);
			RectTransform rule = MMUi.NewRect("MM_HeaderRule", window);
			MMUi.AnchorTop(rule, 1f, 104f);
			MMUi.AddImage(rule, MMUi.FrameFaint);

			// footer
			RectTransform footer = MMUi.NewRect("MM_Footer", window);
			footer.anchorMin = new Vector2(0f, 0f);
			footer.anchorMax = new Vector2(1f, 0f);
			footer.pivot = new Vector2(0.5f, 0f);
			footer.sizeDelta = new Vector2(0f, 88f);
			footer.anchoredPosition = Vector2.zero;
			RectTransform footRule = MMUi.NewRect("MM_FooterRule", footer);
			MMUi.AnchorTop(footRule, 1f, 0f);
			MMUi.AddImage(footRule, MMUi.FrameFaint);
			RectTransform statusRt = MMUi.NewRect("MM_Status", footer);
			MMUi.Stretch(statusRt, 40f, 10f, 560f, 10f);
			_status = MMUi.AddText(statusRt, "", 20, MMUi.Muted, TextAnchor.MiddleLeft);
			RectTransform closeBtn = MMUi.NewRect("MM_CloseButton", footer);
			MMUi.Place(closeBtn, new Vector2(1f, 0.5f), new Vector2(-32f, 0f), new Vector2(170f, 54f));
			MMUi.MakeButton(closeBtn, "Close", 24, DoClose).Framed(MMUi.Frame);
			RectTransform defaults = MMUi.NewRect("MM_DefaultsButton", footer);
			MMUi.Place(defaults, new Vector2(1f, 0.5f), new Vector2(-218f, 0f), new Vector2(300f, 54f));
			MMUi.MakeButton(defaults, "Defaults for this page", 22, ResetPage).Framed(MMUi.FrameSoft);

			// body: navigation column + scrolling page
			RectTransform body = MMUi.NewRect("MM_Body", window);
			MMUi.Stretch(body, 2f, 105f, 2f, 89f);
			RectTransform nav = MMUi.NewRect("MM_Nav", body);
			nav.anchorMin = new Vector2(0f, 0f);
			nav.anchorMax = new Vector2(0f, 1f);
			nav.pivot = new Vector2(0f, 0.5f);
			nav.sizeDelta = new Vector2(310f, 0f);
			nav.anchoredPosition = Vector2.zero;
			MMUi.AddImage(nav, MMUi.NavBg);
			RectTransform navRule = MMUi.NewRect("MM_NavRule", nav);
			navRule.anchorMin = new Vector2(1f, 0f);
			navRule.anchorMax = new Vector2(1f, 1f);
			navRule.pivot = new Vector2(1f, 0.5f);
			navRule.sizeDelta = new Vector2(1f, 0f);
			MMUi.AddImage(navRule, MMUi.FrameFaint);
			float navY = 18f;
			for (int i = 0; i < _pages.Count; i++)
			{
				int index = i;
				if (i > 0 && _pages[i].NewGroup)
				{
					RectTransform groupRule = MMUi.NewRect("MM_NavGroupRule", nav);
					groupRule.anchorMin = new Vector2(0f, 1f);
					groupRule.anchorMax = new Vector2(1f, 1f);
					groupRule.pivot = new Vector2(0.5f, 1f);
					groupRule.sizeDelta = new Vector2(-48f, 1f);
					groupRule.anchoredPosition = new Vector2(-0.5f, -navY - 9f);
					MMUi.AddImage(groupRule, MMUi.FrameFaint);
					navY += 20f;
				}
				RectTransform entry = MMUi.NewRect("MM_NavEntry", nav);
				entry.anchorMin = new Vector2(0f, 1f);
				entry.anchorMax = new Vector2(1f, 1f);
				entry.pivot = new Vector2(0.5f, 1f);
				entry.sizeDelta = new Vector2(-1f, 62f);
				entry.anchoredPosition = new Vector2(-0.5f, -navY);
				navY += 64f;
				MMButton b = MMUi.MakeButton(entry, _pages[i].Title, 25, delegate
				{
					PlayClick();
					ShowPage(index);
				});
				b.Label.alignment = TextAnchor.MiddleLeft;
				RectTransform lrt = b.Label.rectTransform;
				lrt.offsetMin = new Vector2(34f, 0f);
				lrt.offsetMax = new Vector2(-12f, 0f);
				b.SetColors(MMUi.Clear, MMUi.HoverFaint, MMUi.PressFaint, MMUi.TextMain, MMUi.TextMain);
				b.SetSelectedColors(MMUi.NavSelected, MMUi.Gold);
				RectTransform bar = MMUi.NewRect("MM_NavBar", entry);
				bar.anchorMin = new Vector2(0f, 0f);
				bar.anchorMax = new Vector2(0f, 1f);
				bar.pivot = new Vector2(0f, 0.5f);
				bar.sizeDelta = new Vector2(4f, -16f);
				bar.anchoredPosition = new Vector2(12f, 0f);
				b.SelectedMarker = MMUi.AddImage(bar, MMUi.Gold).gameObject;
				_nav.Add(b);
			}

			RectTransform scrollRt = MMUi.NewRect("MM_Scroll", body);
			MMUi.Stretch(scrollRt, 311f, 0f, 0f, 0f);
			_scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
			RectTransform viewport = MMUi.NewRect("MM_Viewport", scrollRt);
			MMUi.Stretch(viewport, 0f, 0f, 22f, 0f);
			MMUi.AddImage(viewport, MMUi.Clear, raycast: true);
			viewport.gameObject.AddComponent<RectMask2D>();
			_content = MMUi.NewRect("MM_Content", viewport);
			_content.anchorMin = new Vector2(0f, 1f);
			_content.anchorMax = new Vector2(1f, 1f);
			_content.pivot = new Vector2(0.5f, 1f);
			_content.sizeDelta = Vector2.zero;
			_content.anchoredPosition = Vector2.zero;
			VerticalLayoutGroup vlg = _content.gameObject.AddComponent<VerticalLayoutGroup>();
			vlg.padding = new RectOffset(40, 36, 30, 20);
			vlg.spacing = 2f;
			vlg.childControlWidth = true;
			vlg.childControlHeight = true;
			vlg.childForceExpandWidth = true;
			vlg.childForceExpandHeight = false;
			ContentSizeFitter fit = _content.gameObject.AddComponent<ContentSizeFitter>();
			fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
			_scroll.viewport = viewport;
			_scroll.content = _content;
			_scroll.horizontal = false;
			_scroll.vertical = true;
			_scroll.movementType = ScrollRect.MovementType.Clamped;
			_scroll.scrollSensitivity = 42f;
			_scroll.inertia = true;
			_scroll.decelerationRate = 0.1f;
			RectTransform sbRt = MMUi.NewRect("MM_Scrollbar", scrollRt);
			sbRt.anchorMin = new Vector2(1f, 0f);
			sbRt.anchorMax = new Vector2(1f, 1f);
			sbRt.pivot = new Vector2(1f, 0.5f);
			sbRt.sizeDelta = new Vector2(8f, -28f);
			sbRt.anchoredPosition = new Vector2(-8f, 0f);
			Image track = MMUi.AddImage(sbRt, MMUi.Track, raycast: true);
			RectTransform slide = MMUi.NewRect("MM_SlideArea", sbRt);
			MMUi.Stretch(slide);
			RectTransform handle = MMUi.NewRect("MM_Handle", slide);
			MMUi.Stretch(handle);
			Image handleImg = MMUi.AddImage(handle, MMUi.FrameSoft, raycast: true);
			Scrollbar sb = sbRt.gameObject.AddComponent<Scrollbar>();
			sb.handleRect = handle;
			sb.targetGraphic = handleImg;
			sb.direction = Scrollbar.Direction.BottomToTop;
			sb.navigation = new Navigation { mode = Navigation.Mode.None };
			track.raycastTarget = true;
			_scroll.verticalScrollbar = sb;
			_scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
			_canvasRoot.SetActive(false);
		}
	}

	/// <summary>A plain pointer button: our own colours for normal / hover / pressed / selected / disabled, no Selectable navigation.</summary>
	public class MMButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
	{
		public Image Bg;
		public Text Label;
		public Action OnClick;
		public GameObject SelectedMarker;
		public bool Selected;
		public bool Interactable = true;
		public Color Normal = MMUi.ControlBg;
		public Color Hover = MMUi.ControlHover;
		public Color Pressed = MMUi.ControlPress;
		public Color TextNormal = MMUi.TextMain;
		public Color TextHover = MMUi.TextMain;
		public Color SelectedBg = MMUi.GoldFill;
		public Color SelectedText = MMUi.OnGold;
		private bool _hover;
		private bool _down;

		public MMButton SetColors(Color normal, Color hover, Color pressed, Color text, Color textHover)
		{
			Normal = normal;
			Hover = hover;
			Pressed = pressed;
			TextNormal = text;
			TextHover = textHover;
			Apply();
			return this;
		}

		public MMButton SetSelectedColors(Color bg, Color text)
		{
			SelectedBg = bg;
			SelectedText = text;
			Apply();
			return this;
		}

		public MMButton Framed(Color c)
		{
			MMUi.AddFrame((RectTransform)transform, c, 1f, 0f);
			return this;
		}

		public void Apply()
		{
			Color bg = Selected ? SelectedBg : (_down ? Pressed : (_hover ? Hover : Normal));
			Color tx = Selected ? SelectedText : (_hover ? TextHover : TextNormal);
			if (!Interactable)
			{
				bg = Selected ? SelectedBg : Normal;
				tx = new Color(tx.r, tx.g, tx.b, tx.a * 0.45f);
			}
			if (Bg != null)
			{
				Bg.color = bg;
			}
			if (Label != null)
			{
				Label.color = tx;
			}
			if (SelectedMarker != null && SelectedMarker.activeSelf != Selected)
			{
				SelectedMarker.SetActive(Selected);
			}
		}

		public void OnPointerEnter(PointerEventData e)
		{
			_hover = Interactable;
			Apply();
		}

		public void OnPointerExit(PointerEventData e)
		{
			_hover = false;
			_down = false;
			Apply();
		}

		public void OnPointerDown(PointerEventData e)
		{
			if (e.button == PointerEventData.InputButton.Left && Interactable)
			{
				_down = true;
				Apply();
			}
		}

		public void OnPointerUp(PointerEventData e)
		{
			_down = false;
			Apply();
		}

		public void OnPointerClick(PointerEventData e)
		{
			if (e.button != PointerEventData.InputButton.Left || !Interactable)
			{
				return;
			}
			try
			{
				OnClick?.Invoke();
			}
			catch (Exception ex)
			{
				Plugin.Log.LogError("mod menu action failed: " + ex);
				ModMenu.Status("That did not work: " + ex.Message);
			}
		}

		private void OnDisable()
		{
			_hover = false;
			_down = false;
		}
	}

	/// <summary>Palette and uGUI construction helpers for the Mod Settings window (all objects are named MM_* so no layout rule matches them).</summary>
	public static class MMUi
	{
		public static readonly Color Dim = new Color(0.02f, 0.015f, 0.01f, 0.78f);
		public static readonly Color WindowBg = new Color(0.082f, 0.068f, 0.058f, 0.985f);
		public static readonly Color NavBg = new Color(0.058f, 0.048f, 0.041f, 1f);
		public static readonly Color NavSelected = new Color(0.74f, 0.6f, 0.37f, 0.14f);
		public static readonly Color Frame = new Color(0.74f, 0.6f, 0.37f, 0.8f);
		public static readonly Color FrameSoft = new Color(0.74f, 0.6f, 0.37f, 0.42f);
		public static readonly Color FrameFaint = new Color(0.74f, 0.6f, 0.37f, 0.2f);
		public static readonly Color Track = new Color(1f, 1f, 1f, 0.05f);
		public static readonly Color Clear = new Color(0f, 0f, 0f, 0f);
		public static readonly Color HoverFaint = new Color(1f, 0.95f, 0.85f, 0.06f);
		public static readonly Color PressFaint = new Color(1f, 0.95f, 0.85f, 0.11f);
		public static readonly Color RowAlt = new Color(1f, 0.95f, 0.85f, 0.022f);
		public static readonly Color TextMain = new Color(0.92f, 0.89f, 0.83f, 1f);
		public static readonly Color Muted = new Color(0.66f, 0.62f, 0.55f, 1f);
		public static readonly Color Gold = new Color(0.9f, 0.78f, 0.55f, 1f);
		public static readonly Color GoldFill = new Color(0.69f, 0.53f, 0.3f, 1f);
		public static readonly Color OnGold = new Color(0.1f, 0.075f, 0.055f, 1f);
		public static readonly Color ControlBg = new Color(0.135f, 0.115f, 0.098f, 1f);
		public static readonly Color ControlHover = new Color(0.2f, 0.17f, 0.14f, 1f);
		public static readonly Color ControlPress = new Color(0.25f, 0.21f, 0.17f, 1f);
		public const string WarnHex = "#DE9A66";

		private static Font _font;

		public static Font Font
		{
			get
			{
				if (_font == null)
				{
					_font = PickFont();
				}
				return _font;
			}
		}

		private static Font PickFont()
		{
			try
			{
				HashSet<string> installed = new HashSet<string>(Font.GetOSInstalledFontNames(), StringComparer.OrdinalIgnoreCase);
				foreach (string name in new string[4] { "Palatino Linotype", "Book Antiqua", "Georgia", "Times New Roman" })
				{
					if (installed.Contains(name))
					{
						Font f = RuleApplier.GetOsFont(name);
						if (f != null)
						{
							return f;
						}
					}
				}
				foreach (Font f2 in Resources.FindObjectsOfTypeAll<Font>())
				{
					if (f2 != null && f2.name == "SourceHanSerifTC-Bold")
					{
						return f2;
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("mod menu font lookup failed: " + ex.Message);
			}
			return Resources.GetBuiltinResource<Font>("Arial.ttf");
		}

		public static RectTransform NewRect(string name, Transform parent)
		{
			GameObject go = new GameObject(name, typeof(RectTransform));
			go.layer = 5;
			RectTransform rt = (RectTransform)go.transform;
			rt.SetParent(parent, worldPositionStays: false);
			return rt;
		}

		public static void Stretch(RectTransform rt, float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
		{
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.offsetMin = new Vector2(left, bottom);
			rt.offsetMax = new Vector2(0f - right, 0f - top);
		}

		/// <summary>Full-width strip of the given height whose top edge sits 'fromTop' below the parent's top.</summary>
		public static void AnchorTop(RectTransform rt, float height, float fromTop)
		{
			rt.anchorMin = new Vector2(0f, 1f);
			rt.anchorMax = new Vector2(1f, 1f);
			rt.pivot = new Vector2(0.5f, 1f);
			rt.sizeDelta = new Vector2(0f, height);
			rt.anchoredPosition = new Vector2(0f, 0f - fromTop);
		}

		public static void Place(RectTransform rt, Vector2 anchorPivot, Vector2 pos, Vector2 size)
		{
			rt.anchorMin = rt.anchorMax = anchorPivot;
			rt.pivot = anchorPivot;
			rt.anchoredPosition = pos;
			rt.sizeDelta = size;
		}

		public static Image AddImage(RectTransform rt, Color c, bool raycast = false)
		{
			Image img = rt.gameObject.AddComponent<Image>();
			img.color = c;
			img.raycastTarget = raycast;
			return img;
		}

		public static Text AddText(RectTransform rt, string s, int size, Color c, TextAnchor align, bool wrap = true)
		{
			Text t = rt.gameObject.AddComponent<Text>();
			t.font = Font;
			t.fontSize = size;
			t.color = c;
			t.alignment = align;
			t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
			t.verticalOverflow = VerticalWrapMode.Overflow;
			t.supportRichText = true;
			t.raycastTarget = false;
			t.lineSpacing = 1.05f;
			t.text = s ?? "";
			return t;
		}

		/// <summary>Four 'thickness' lines along the edges of rt, 'inset' pixels inside it.</summary>
		public static void AddFrame(RectTransform rt, Color c, float thickness, float inset)
		{
			for (int i = 0; i < 4; i++)
			{
				RectTransform e = NewRect("MM_FrameEdge", rt);
				switch (i)
				{
				case 0:
					e.anchorMin = new Vector2(0f, 1f);
					e.anchorMax = new Vector2(1f, 1f);
					e.pivot = new Vector2(0.5f, 1f);
					e.sizeDelta = new Vector2(-2f * inset, thickness);
					e.anchoredPosition = new Vector2(0f, 0f - inset);
					break;
				case 1:
					e.anchorMin = new Vector2(0f, 0f);
					e.anchorMax = new Vector2(1f, 0f);
					e.pivot = new Vector2(0.5f, 0f);
					e.sizeDelta = new Vector2(-2f * inset, thickness);
					e.anchoredPosition = new Vector2(0f, inset);
					break;
				case 2:
					e.anchorMin = new Vector2(0f, 0f);
					e.anchorMax = new Vector2(0f, 1f);
					e.pivot = new Vector2(0f, 0.5f);
					e.sizeDelta = new Vector2(thickness, -2f * inset);
					e.anchoredPosition = new Vector2(inset, 0f);
					break;
				default:
					e.anchorMin = new Vector2(1f, 0f);
					e.anchorMax = new Vector2(1f, 1f);
					e.pivot = new Vector2(1f, 0.5f);
					e.sizeDelta = new Vector2(thickness, -2f * inset);
					e.anchoredPosition = new Vector2(0f - inset, 0f);
					break;
				}
				AddImage(e, c);
			}
		}

		public static MMButton MakeButton(RectTransform rt, string label, int size, Action onClick)
		{
			MMButton b = rt.gameObject.AddComponent<MMButton>();
			b.Bg = AddImage(rt, MMUi.ControlBg, raycast: true);
			RectTransform lr = NewRect("MM_ButtonLabel", rt);
			Stretch(lr, 8f, 0f, 8f, 0f);
			b.Label = AddText(lr, label, size, TextMain, TextAnchor.MiddleCenter, wrap: false);
			b.OnClick = onClick;
			b.Apply();
			return b;
		}

		public static void Spacer(RectTransform parent, float height)
		{
			RectTransform s = NewRect("MM_Spacer", parent);
			LayoutElement le = s.gameObject.AddComponent<LayoutElement>();
			le.minHeight = height;
			le.preferredHeight = height;
		}
	}
}
