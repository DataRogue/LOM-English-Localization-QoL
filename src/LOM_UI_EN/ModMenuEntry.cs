using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mortal.Battle;
using Mortal.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LOM_UI_EN
{
	/// <summary>
	/// Puts a "Mod Settings" button into the game's own menus by cloning the menu's Settings button, so it has the game's art,
	/// font, hover animation and sounds:
	///   Title       TitleManager._systemSettingButton  (/UI/Layer_1/Buttons/Setting, a TitleOptionButton)
	///   Story/Free  MenuPanel._systemSettingButton     (/[UI]/TopPanel/MenuPanel/Buttons/MenuButton_Setting, a MenuToggleButton)
	///   Duel        MenuPanel._systemSettingButton     (/[UI]/TopPanel/MenuPanel/Container/MenuButton_Setting)
	///   Battle      PausePanel._systemSettingButton    (/[UI]/TopPanel/PausePanel/Border/MenuButton_Setting)
	/// The clone is made under an inactive holder so none of its scripts wake up before it is cleaned: the Button's inspector
	/// listeners (they open the game's settings) are replaced, and InputButton (its keyboard shortcut), DevelopmentOnly,
	/// the Lean localization components (they would put 'Settings' back) and copied UIFixMarkers are removed. The button goes in
	/// right after Settings; explicit keyboard navigation is re-linked through it. Pause menus stack their buttons in a vertical
	/// layout that already reaches the bottom of the screen, so once a column is visible its spacing is tightened until it fits.
	/// </summary>
	public static class ModMenuEntry
	{
		private class Entry
		{
			public GameObject Button;
			public Button Source;
			public RectTransform Container;
			public VerticalLayoutGroup Layout;
			public float OriginalSpacing;
			public bool Fitted;
			public float MatchAgainAt;
			public Navigation SourceNav;
			public Selectable Below;
			public Navigation BelowNav;
		}

		public const string Label = "Mod Settings";
		private const string TitleCloneName = "ModSettings";
		private const string PauseCloneName = "MenuButton_ModSettings";
		private static readonly List<Entry> _entries = new List<Entry>();
		private static float _nextScan;
		private static bool _warned;
		/// <summary>Settings buttons a clone failed for: not retried every half second (the failure is reported once).</summary>
		private static readonly HashSet<int> _failedSources = new HashSet<int>();

		public static void Patch(Harmony h)
		{
			Compat.Setup(F.MenuButtons, delegate
			{
				// Each menu the button goes into is its own part: a renamed field or type there only loses that menu's button.
				// Types by name, so a missing one does not stop this method.
				Compat.Require(F.MenuButtons, "Mortal.Core", "Mortal.Core.TitleManager", "_systemSettingButton", part: "title");
				Compat.Require(F.MenuButtons, "Mortal.Core", "Mortal.Core.TitleManager", "Instance", part: "title");
				Compat.Require(F.MenuButtons, "Mortal.Core", "Mortal.Core.MenuPanel", "_systemSettingButton", part: "pause");
				Compat.Require(F.MenuButtons, "Mortal.Battle", "Mortal.Battle.PausePanel", "_systemSettingButton", part: "battle");
				SceneManager.sceneLoaded += delegate
				{
					_nextScan = 0f;
					_failedSources.Clear();
				};
				Compat.Part(F.MenuButtons, "MenuPanel.OnPanelOpen", delegate
				{
					Compat.Hook(F.MenuButtons, h, AccessTools.Method(typeof(MenuPanel), "OnPanelOpen"), "MenuPanel.OnPanelOpen", null, new HarmonyMethod(typeof(ModMenuEntry), nameof(MenuPanelOpen_Postfix)));
				});
			});
		}

		/// <summary>MenuPanel.OnPanelOpen re-inits the hover state of its own buttons only; do ours too.</summary>
		private static void MenuPanelOpen_Postfix(Component __instance)
		{
			if (!F.MenuButtons.Live)
			{
				return;
			}
			try
			{
				F.MenuButtons.Probe();
				MenuPanelOpenBody(__instance);
			}
			catch (Exception ex)
			{
				F.MenuButtons.Fail(ex, "MenuPanel.OnPanelOpen");
			}
		}

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private static void MenuPanelOpenBody(Component panel)
		{
			foreach (Entry e in _entries)
			{
				if (e.Button != null && e.Button.transform.IsChildOf(panel.transform))
				{
					e.Button.GetComponent<MenuToggleButton>()?.Init();
				}
			}
		}

		/// <summary>ModMenu.Update, every frame: look for new menus twice a second, fit visible columns.</summary>
		public static void Tick()
		{
			for (int i = _entries.Count - 1; i >= 0; i--)
			{
				Entry e = _entries[i];
				if (e.Button == null || e.Container == null)
				{
					_entries.RemoveAt(i);
				}
				else if (!e.Fitted && e.Container.gameObject.activeInHierarchy)
				{
					e.Fitted = true;
					e.MatchAgainAt = Time.realtimeSinceStartup + 1.5f;
					MatchTexts(e);
					Fit(e);
				}
				else if (e.MatchAgainAt > 0f && Time.realtimeSinceStartup > e.MatchAgainAt)
				{
					// XUnity can resize the original label a frame or two after it appears; copy once more.
					e.MatchAgainAt = 0f;
					MatchTexts(e);
				}
			}
			if (Time.realtimeSinceStartup < _nextScan)
			{
				return;
			}
			_nextScan = Time.realtimeSinceStartup + 0.5f;
			Scan();
		}

		public static void OnConfigChanged()
		{
			for (int i = _entries.Count - 1; i >= 0; i--)
			{
				Entry e = _entries[i];
				bool title = e.Button != null && e.Button.name == TitleCloneName;
				bool wanted = title ? ModMenu.CfgTitleButton.Value : ModMenu.CfgPauseButton.Value;
				if (!wanted)
				{
					Remove(e);
					_entries.RemoveAt(i);
				}
			}
			_nextScan = 0f;
		}

		private static void Scan()
		{
			// One method per menu type: a type the game no longer has fails only its own part (at JIT time, caught here).
			if (ModMenu.CfgTitleButton.Value)
			{
				ScanPart("title", ScanTitle);
			}
			if (ModMenu.CfgPauseButton.Value)
			{
				ScanPart("pause", ScanPause);
				ScanPart("battle", ScanBattle);
			}
		}

		private static void ScanPart(string part, Action scan)
		{
			if (!F.MenuButtons.PartOk(part))
			{
				return;
			}
			try
			{
				scan();
			}
			catch (Exception ex)
			{
				if (Compat.IsStructural(ex))
				{
					F.MenuButtons.BreakPart(part, ex.GetType().Name + ": " + ex.Message);
				}
				else
				{
					F.MenuButtons.Fail(ex, "adding the button (" + part + ")");
				}
			}
		}

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private static void ScanTitle()
		{
			TitleManager tm = TitleManager.Instance;
			if (tm != null && tm.gameObject.scene.IsValid())
			{
				TryInject(tm, TitleCloneName, title: true);
			}
		}

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private static void ScanPause()
		{
			foreach (MenuPanel mp in Resources.FindObjectsOfTypeAll<MenuPanel>())
			{
				if (mp != null && mp.gameObject.scene.IsValid() && mp.gameObject.scene.isLoaded)
				{
					TryInject(mp, PauseCloneName, title: false);
				}
			}
		}

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private static void ScanBattle()
		{
			foreach (PausePanel pp in Resources.FindObjectsOfTypeAll<PausePanel>())
			{
				if (pp != null && pp.gameObject.scene.IsValid() && pp.gameObject.scene.isLoaded)
				{
					TryInject(pp, PauseCloneName, title: false);
				}
			}
		}

		private static void TryInject(Component owner, string cloneName, bool title)
		{
			Traverse t = Traverse.Create(owner);
			Button src = t.Field("_systemSettingButton").GetValue<Button>();
			if (src == null)
			{
				return;
			}
			Transform container = src.transform.parent;
			if (container == null || container.Find(cloneName) != null || _failedSources.Contains(src.GetInstanceID()))
			{
				return;
			}
			GameObject clone = Clone(src, cloneName, title);
			if (clone == null)
			{
				_failedSources.Add(src.GetInstanceID());
				return;
			}
			Entry e = new Entry
			{
				Button = clone,
				Source = src,
				Container = container as RectTransform,
				Layout = container.GetComponent<VerticalLayoutGroup>()
			};
			e.OriginalSpacing = (e.Layout != null) ? e.Layout.spacing : 0f;
			Link(e, clone.GetComponent<Button>());
			_entries.Add(e);
			Plugin.Log.LogInfo("mod menu button added: " + PathUtil.GetPath(clone));
		}

		private static GameObject Clone(Button src, string cloneName, bool title)
		{
			GameObject holder = new GameObject("MM_CloneHolder");
			holder.SetActive(false);
			try
			{
				GameObject clone = UnityEngine.Object.Instantiate(src.gameObject, holder.transform, worldPositionStays: false);
				clone.name = cloneName;
				foreach (Component c in clone.GetComponentsInChildren<Component>(includeInactive: true))
				{
					if (c == null)
					{
						continue;
					}
					string n = c.GetType().Name;
					if (c is UIFixMarker || n == "InputButton" || n == "DevelopmentOnly" || n.StartsWith("LeanLocalized", StringComparison.Ordinal))
					{
						UnityEngine.Object.DestroyImmediate(c);
					}
				}
				foreach (Text text in clone.GetComponentsInChildren<Text>(includeInactive: true))
				{
					text.text = Label;
					if (title)
					{
						// The title labels sit in a centred 150px box meant for four Chinese characters; the base patch's XUnity
						// resizer lets its siblings overflow sideways on one line, so do the same (MatchTexts copies the rest later).
						text.horizontalOverflow = HorizontalWrapMode.Overflow;
					}
				}
				Button b = clone.GetComponent<Button>();
				b.onClick = new Button.ButtonClickedEvent();
				b.onClick.AddListener(delegate
				{
					ModMenu.Open(clone);
				});
				b.interactable = true;
				CaptureSound(clone);
				clone.transform.SetParent(src.transform.parent, worldPositionStays: false);
				clone.transform.SetSiblingIndex(src.transform.GetSiblingIndex() + 1);
				return clone;
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("mod menu button could not be added next to " + PathUtil.GetPath(src.gameObject) + ": " + ex.Message);
				F.MenuButtons.Fail(ex, "cloning " + src.name);
				return null;
			}
			finally
			{
				UnityEngine.Object.Destroy(holder);
			}
		}

		private static void CaptureSound(GameObject clone)
		{
			try
			{
				// By type name, so this method names no game type (a changed one then only costs the click sound).
				Component c = null;
				foreach (Component x in clone.GetComponents<Component>())
				{
					string n = (x != null) ? x.GetType().Name : null;
					if (n == "MenuToggleButton" || n == "TitleOptionButton")
					{
						c = x;
						break;
					}
				}
				if (c == null)
				{
					return;
				}
				object data = Traverse.Create(c).Field("_pressSoundData").GetValue() ?? Traverse.Create(c).Field("_pressSound").GetValue();
				if (data != null)
				{
					Traverse d = Traverse.Create(data);
					ModMenu.SetClickSound(d.Property("Key").GetValue<string>() ?? d.Field("Key").GetValue<string>() ?? d.Field("_key").GetValue<string>());
				}
			}
			catch (Exception)
			{
			}
		}

		private static void Link(Entry e, Button clone)
		{
			Navigation n = e.Source.navigation;
			e.SourceNav = n;
			if (n.mode != Navigation.Mode.Explicit || clone == null)
			{
				return;
			}
			Selectable below = n.selectOnDown;
			Navigation cn = n;
			cn.selectOnUp = e.Source;
			cn.selectOnDown = below;
			clone.navigation = cn;
			n.selectOnDown = clone;
			e.Source.navigation = n;
			if (below != null && below.navigation.mode == Navigation.Mode.Explicit && below.navigation.selectOnUp == e.Source)
			{
				e.Below = below;
				e.BelowNav = below.navigation;
				Navigation bn = below.navigation;
				bn.selectOnUp = clone;
				below.navigation = bn;
			}
		}

		private static void Remove(Entry e)
		{
			try
			{
				if (e.Source != null)
				{
					e.Source.navigation = e.SourceNav;
				}
				if (e.Below != null)
				{
					e.Below.navigation = e.BelowNav;
				}
				if (e.Layout != null)
				{
					e.Layout.spacing = e.OriginalSpacing;
				}
				if (e.Button != null)
				{
					if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == e.Button)
					{
						EventSystem.current.SetSelectedGameObject(null);
					}
					e.Button.SetActive(false);
					UnityEngine.Object.Destroy(e.Button);
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("mod menu button removal failed: " + ex.Message);
			}
		}

		/// <summary>
		/// The first time the menu shows, give each label of the clone the final text settings of the matching label on the Settings
		/// button (font, size, best fit, alignment, wrapping). By then XUnity's resizers, the font map and the layout rules have all run
		/// on the original, so the clone reads exactly like its neighbours whichever of them touched it. Colours stay with the button
		/// scripts, which drive normal / hover states.
		/// </summary>
		private static void MatchTexts(Entry e)
		{
			try
			{
				if (e.Source == null)
				{
					return;
				}
				Transform root = e.Button.transform;
				foreach (Text t in e.Button.GetComponentsInChildren<Text>(includeInactive: true))
				{
					string rel = RelativePath(t.transform, root);
					Transform other = (rel.Length == 0) ? e.Source.transform : e.Source.transform.Find(rel);
					Text s = (other != null) ? other.GetComponent<Text>() : null;
					if (s == null)
					{
						continue;
					}
					if (s.font != null)
					{
						t.font = s.font;
					}
					t.fontSize = s.fontSize;
					t.fontStyle = s.fontStyle;
					t.resizeTextForBestFit = s.resizeTextForBestFit;
					t.resizeTextMinSize = s.resizeTextMinSize;
					t.resizeTextMaxSize = s.resizeTextMaxSize;
					t.alignment = s.alignment;
					// Always one line: the neighbour may wrap (no resizer from the original patch lets it overflow), and
					// "Mod Settings" wrapped in a box sized for four Chinese characters would show only "Mod".
					t.horizontalOverflow = HorizontalWrapMode.Overflow;
					t.verticalOverflow = s.verticalOverflow;
					t.lineSpacing = s.lineSpacing;
					t.rectTransform.sizeDelta = s.rectTransform.sizeDelta;
					t.rectTransform.anchoredPosition = s.rectTransform.anchoredPosition;
					if (t.text != Label)
					{
						t.text = Label;
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("mod menu button label match failed: " + ex.Message);
			}
		}

		private static string RelativePath(Transform t, Transform root)
		{
			string path = "";
			while (t != null && t != root)
			{
				path = (path.Length == 0) ? t.name : (t.name + "/" + path);
				t = t.parent;
			}
			return path;
		}

		/// <summary>Tighten the column's spacing until its lowest button clears the bottom of the screen by 24 units.</summary>
		private static void Fit(Entry e)
		{
			try
			{
				if (e.Layout == null)
				{
					return;
				}
				Canvas canvas = e.Container.GetComponentInParent<Canvas>();
				RectTransform screen = (canvas != null) ? (canvas.rootCanvas.transform as RectTransform) : null;
				if (screen == null)
				{
					return;
				}
				Vector3[] corners = new Vector3[4];
				for (int pass = 0; pass < 8; pass++)
				{
					LayoutRebuilder.ForceRebuildLayoutImmediate(e.Container);
					float lowest = float.MaxValue;
					int active = 0;
					for (int i = 0; i < e.Container.childCount; i++)
					{
						RectTransform child = e.Container.GetChild(i) as RectTransform;
						if (child == null || !child.gameObject.activeInHierarchy)
						{
							continue;
						}
						active++;
						child.GetWorldCorners(corners);
						lowest = Mathf.Min(lowest, screen.InverseTransformPoint(corners[0]).y);
					}
					float limit = screen.rect.yMin + 24f;
					if (active < 2 || lowest >= limit || e.Layout.spacing <= 2f)
					{
						break;
					}
					float over = limit - lowest;
					e.Layout.spacing = Mathf.Max(2f, e.Layout.spacing - Mathf.Ceil(over / (active - 1)) - 0.5f);
				}
				if (!Mathf.Approximately(e.Layout.spacing, e.OriginalSpacing) && Plugin.CfgVerbose.Value)
				{
					Plugin.Log.LogInfo($"mod menu: {PathUtil.GetPath(e.Container)} spacing {e.OriginalSpacing} -> {e.Layout.spacing} to keep the column on screen");
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("mod menu column fit failed: " + ex.Message);
			}
		}
	}
}
