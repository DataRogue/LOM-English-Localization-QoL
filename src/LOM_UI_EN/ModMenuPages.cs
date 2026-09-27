using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace LOM_UI_EN
{
	public class MenuPage
	{
		public string Title;
		public string Intro;
		/// <summary>Starts a new group in the navigation column: a gap and a faint rule above its entry.</summary>
		public bool NewGroup;
		public List<MenuItem> Items = new List<MenuItem>();
	}

	/// <summary>One row of a page: label, help text (plus an optional live status line) and a control on the right.</summary>
	public abstract class MenuItem
	{
		public const float ControlWidth = 380f;
		public string Label;
		public string Help;
		public Func<string> Status;
		public bool Restart;
		public Func<bool> IsEnabled;
		public Func<bool> Reset;
		/// <summary>The Compat feature behind the setting: while it is not working, the row says so under its help text.</summary>
		public Feature Feature;
		protected RectTransform Row;
		protected Text DescText;
		protected CanvasGroup Group;

		protected virtual bool HasControl => true;

		public virtual void BuildRow(RectTransform parent, bool alt)
		{
			Row = MMUi.NewRect("MM_Row", parent);
			MMUi.AddImage(Row, alt ? MMUi.RowAlt : MMUi.Clear);
			HorizontalLayoutGroup h = Row.gameObject.AddComponent<HorizontalLayoutGroup>();
			h.padding = new RectOffset(20, 14, 16, 16);
			h.spacing = 30f;
			h.childAlignment = TextAnchor.MiddleLeft;
			h.childControlWidth = true;
			h.childControlHeight = true;
			h.childForceExpandWidth = false;
			h.childForceExpandHeight = false;
			Group = Row.gameObject.AddComponent<CanvasGroup>();
			RectTransform col = MMUi.NewRect("MM_TextCol", Row);
			VerticalLayoutGroup v = col.gameObject.AddComponent<VerticalLayoutGroup>();
			v.spacing = 10f;
			v.childControlWidth = true;
			v.childControlHeight = true;
			v.childForceExpandWidth = true;
			v.childForceExpandHeight = false;
			LayoutElement le = col.gameObject.AddComponent<LayoutElement>();
			le.minWidth = 260f;
			le.preferredWidth = 260f;
			le.flexibleWidth = 1f;
			if (!string.IsNullOrEmpty(Label))
			{
				MMUi.AddText(MMUi.NewRect("MM_Label", col), Label, 26, MMUi.TextMain, TextAnchor.UpperLeft);
			}
			DescText = MMUi.AddText(MMUi.NewRect("MM_Desc", col), "", 19, MMUi.Muted, TextAnchor.UpperLeft);
			if (HasControl)
			{
				RectTransform ctl = MMUi.NewRect("MM_Control", Row);
				LayoutElement cle = ctl.gameObject.AddComponent<LayoutElement>();
				cle.minWidth = ControlWidth;
				cle.preferredWidth = ControlWidth;
				cle.minHeight = 50f;
				cle.preferredHeight = 50f;
				BuildControl(ctl);
			}
		}

		protected virtual void BuildControl(RectTransform host)
		{
		}

		public virtual void Refresh()
		{
			if (DescText != null)
			{
				string d = Description();
				DescText.text = d;
				bool show = d.Length > 0;
				if (DescText.gameObject.activeSelf != show)
				{
					DescText.gameObject.SetActive(show);
				}
			}
			bool enabled = IsEnabled == null || IsEnabled();
			if (Group != null)
			{
				Group.alpha = enabled ? 1f : 0.42f;
				Group.blocksRaycasts = enabled;
			}
			RefreshControl(enabled);
		}

		protected virtual void RefreshControl(bool enabled)
		{
		}

		private string Description()
		{
			string s = Help ?? "";
			string st = null;
			try
			{
				st = Status?.Invoke();
			}
			catch (Exception ex)
			{
				st = "(status unavailable: " + ex.Message + ")";
			}
			if (!string.IsNullOrEmpty(st))
			{
				s = (s.Length > 0) ? (s + "\n" + st) : st;
			}
			if (Restart)
			{
				s += ((s.Length > 0) ? " " : "") + "<color=" + MMUi.WarnHex + ">Needs a game restart.</color>";
			}
			return s;
		}

		public bool ResetToDefault()
		{
			try
			{
				return Reset != null && Reset();
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("mod menu: reset of '" + Label + "' failed: " + ex.Message);
				return false;
			}
		}
	}

	public class SectionItem : MenuItem
	{
		protected override bool HasControl => false;

		public SectionItem(string title)
		{
			Label = title;
		}

		public override void BuildRow(RectTransform parent, bool alt)
		{
			Row = MMUi.NewRect("MM_Section", parent);
			LayoutElement le = Row.gameObject.AddComponent<LayoutElement>();
			le.minHeight = 66f;
			le.preferredHeight = 66f;
			RectTransform t = MMUi.NewRect("MM_SectionTitle", Row);
			MMUi.Stretch(t, 20f, 0f, 20f, 12f);
			MMUi.AddText(t, Label.ToUpperInvariant(), 20, MMUi.Gold, TextAnchor.LowerLeft, wrap: false);
			RectTransform line = MMUi.NewRect("MM_SectionRule", Row);
			line.anchorMin = new Vector2(0f, 0f);
			line.anchorMax = new Vector2(1f, 0f);
			line.pivot = new Vector2(0.5f, 0f);
			line.sizeDelta = new Vector2(-24f, 1f);
			line.anchoredPosition = new Vector2(0f, 4f);
			MMUi.AddImage(line, MMUi.FrameFaint);
		}

		public override void Refresh()
		{
		}
	}

	public class InfoItem : MenuItem
	{
		protected override bool HasControl => false;
	}

	/// <summary>Segmented buttons (also used for On / Off). get returns the selected index, or -1 for none (e.g. a custom translation mix).</summary>
	public class ChoiceItem : MenuItem
	{
		private readonly string[] _options;
		private readonly Func<int> _get;
		private readonly Func<int, string> _set;
		private MMButton[] _segs;

		public ChoiceItem(string label, string help, string[] options, Func<int> get, Func<int, string> set)
		{
			Label = label;
			Help = help;
			_options = options;
			_get = get;
			_set = set;
		}

		protected override void BuildControl(RectTransform host)
		{
			int n = _options.Length;
			const float gap = 4f;
			float w = (ControlWidth - gap * (n - 1)) / n;
			_segs = new MMButton[n];
			for (int i = 0; i < n; i++)
			{
				int index = i;
				RectTransform s = MMUi.NewRect("MM_Segment", host);
				MMUi.Place(s, new Vector2(0f, 0.5f), new Vector2(i * (w + gap), 0f), new Vector2(w, 48f));
				_segs[i] = MMUi.MakeButton(s, _options[i], 22, delegate
				{
					if (_get() == index)
					{
						return;
					}
					ModMenu.PlayClick();
					string msg = _set(index);
					ModMenu.Changed(msg);
				}).Framed(MMUi.FrameSoft);
			}
		}

		protected override void RefreshControl(bool enabled)
		{
			if (_segs == null)
			{
				return;
			}
			int cur = _get();
			for (int i = 0; i < _segs.Length; i++)
			{
				_segs[i].Selected = i == cur;
				_segs[i].Interactable = enabled;
				_segs[i].Apply();
			}
		}
	}

	public class NumberItem : MenuItem
	{
		private readonly float _min;
		private readonly float _max;
		private readonly float _step;
		private readonly Func<float> _get;
		private readonly Action<float> _set;
		private readonly Func<float, string> _format;
		private MMButton _minus;
		private MMButton _plus;
		private Text _value;

		public NumberItem(string label, string help, float min, float max, float step, Func<float> get, Action<float> set, Func<float, string> format)
		{
			Label = label;
			Help = help;
			_min = min;
			_max = max;
			_step = step;
			_get = get;
			_set = set;
			_format = format;
		}

		protected override void BuildControl(RectTransform host)
		{
			RectTransform m = MMUi.NewRect("MM_Minus", host);
			MMUi.Place(m, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(56f, 48f));
			_minus = MMUi.MakeButton(m, "−", 30, delegate
			{
				Step(-1);
			}).Framed(MMUi.FrameSoft);
			RectTransform box = MMUi.NewRect("MM_Value", host);
			MMUi.Place(box, new Vector2(0f, 0.5f), new Vector2(64f, 0f), new Vector2(ControlWidth - 128f, 48f));
			MMUi.AddImage(box, MMUi.ControlBg);
			MMUi.AddFrame(box, MMUi.FrameFaint, 1f, 0f);
			RectTransform vt = MMUi.NewRect("MM_ValueText", box);
			MMUi.Stretch(vt);
			_value = MMUi.AddText(vt, "", 23, MMUi.TextMain, TextAnchor.MiddleCenter, wrap: false);
			RectTransform p = MMUi.NewRect("MM_Plus", host);
			MMUi.Place(p, new Vector2(0f, 0.5f), new Vector2(ControlWidth - 56f, 0f), new Vector2(56f, 48f));
			_plus = MMUi.MakeButton(p, "+", 30, delegate
			{
				Step(1);
			}).Framed(MMUi.FrameSoft);
		}

		private void Step(int dir)
		{
			float cur = _get();
			float next = Mathf.Clamp(Mathf.Round((cur + dir * _step) / _step) * _step, _min, _max);
			if (Mathf.Approximately(next, cur))
			{
				return;
			}
			ModMenu.PlayClick();
			_set(next);
			ModMenu.Changed(Label + ": " + _format(next));
		}

		protected override void RefreshControl(bool enabled)
		{
			if (_value == null)
			{
				return;
			}
			float cur = _get();
			_value.text = _format(cur);
			_minus.Interactable = enabled && cur > _min + _step * 0.01f;
			_plus.Interactable = enabled && cur < _max - _step * 0.01f;
			_minus.Apply();
			_plus.Apply();
		}
	}

	public class KeyItem : MenuItem
	{
		private readonly ConfigEntry<KeyCode> _entry;
		private readonly Action _after;
		private MMButton _button;

		public KeyItem(ConfigEntry<KeyCode> entry, string label, string help, Action after = null)
		{
			_entry = entry;
			_after = after;
			Label = label;
			Help = help;
			Reset = delegate
			{
				KeyCode def = (KeyCode)entry.DefaultValue;
				if (entry.Value == def)
				{
					return false;
				}
				entry.Value = def;
				after?.Invoke();
				return true;
			};
		}

		protected override void BuildControl(RectTransform host)
		{
			RectTransform b = MMUi.NewRect("MM_KeyButton", host);
			MMUi.Place(b, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(ControlWidth, 48f));
			_button = MMUi.MakeButton(b, "", 23, delegate
			{
				ModMenu.PlayClick();
				ModMenu.CaptureKey(this);
			}).Framed(MMUi.FrameSoft);
		}

		public void Set(KeyCode key)
		{
			_entry.Value = key;
			_after?.Invoke();
			ModMenu.Changed(Label + ": " + KeyName(key));
		}

		protected override void RefreshControl(bool enabled)
		{
			if (_button == null)
			{
				return;
			}
			bool capturing = ModMenu.IsCapturing(this);
			_button.Label.text = capturing ? "Press a key…" : KeyName(_entry.Value);
			_button.TextNormal = capturing ? MMUi.Gold : MMUi.TextMain;
			_button.Interactable = enabled;
			_button.Apply();
		}

		public static string KeyName(KeyCode k)
		{
			if (k == KeyCode.None)
			{
				return "None";
			}
			if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9)
			{
				return ((int)(k - KeyCode.Alpha0)).ToString(CultureInfo.InvariantCulture);
			}
			if (k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9)
			{
				return "Num " + (int)(k - KeyCode.Keypad0);
			}
			string s = k.ToString();
			System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length + 4);
			for (int i = 0; i < s.Length; i++)
			{
				if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1]))
				{
					sb.Append(' ');
				}
				sb.Append(s[i]);
			}
			return sb.ToString();
		}
	}

	public class TextItem : MenuItem
	{
		private readonly ConfigEntry<string> _entry;
		private readonly Action _after;
		private InputField _field;

		public TextItem(ConfigEntry<string> entry, string label, string help, Action after = null)
		{
			_entry = entry;
			_after = after;
			Label = label;
			Help = help;
			Reset = delegate
			{
				string def = (string)entry.DefaultValue;
				if (entry.Value == def)
				{
					return false;
				}
				entry.Value = def;
				after?.Invoke();
				return true;
			};
		}

		protected override void BuildControl(RectTransform host)
		{
			RectTransform f = MMUi.NewRect("MM_Input", host);
			MMUi.Place(f, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(ControlWidth, 48f));
			Image bg = MMUi.AddImage(f, MMUi.ControlBg, raycast: true);
			MMUi.AddFrame(f, MMUi.FrameSoft, 1f, 0f);
			RectTransform tr = MMUi.NewRect("MM_InputText", f);
			MMUi.Stretch(tr, 14f, 7f, 14f, 7f);
			Text text = MMUi.AddText(tr, "", 22, MMUi.TextMain, TextAnchor.MiddleLeft);
			text.supportRichText = false;
			text.verticalOverflow = VerticalWrapMode.Truncate;
			RectTransform pr = MMUi.NewRect("MM_InputPlaceholder", f);
			MMUi.Stretch(pr, 14f, 7f, 14f, 7f);
			Text ph = MMUi.AddText(pr, "(empty)", 22, MMUi.Muted, TextAnchor.MiddleLeft);
			ph.fontStyle = FontStyle.Italic;
			ph.verticalOverflow = VerticalWrapMode.Truncate;
			_field = f.gameObject.AddComponent<InputField>();
			_field.textComponent = text;
			_field.placeholder = ph;
			_field.targetGraphic = bg;
			_field.lineType = InputField.LineType.SingleLine;
			_field.caretColor = MMUi.Gold;
			_field.selectionColor = new Color(0.74f, 0.6f, 0.37f, 0.35f);
			_field.navigation = new Navigation { mode = Navigation.Mode.None };
			_field.text = _entry.Value ?? "";
			_field.onEndEdit.AddListener(delegate(string v)
			{
				if (_entry.Value != v)
				{
					_entry.Value = v;
					_after?.Invoke();
					ModMenu.Changed(Label + " saved.");
				}
			});
		}

		protected override void RefreshControl(bool enabled)
		{
			if (_field == null)
			{
				return;
			}
			if (!_field.isFocused && _field.text != (_entry.Value ?? ""))
			{
				_field.text = _entry.Value ?? "";
			}
			_field.interactable = enabled;
		}
	}

	public class ActionItem : MenuItem
	{
		private readonly string _button;
		private readonly string _confirm;
		private readonly Func<string> _run;
		private MMButton _b;
		private float _armedUntil;

		public ActionItem(string label, string help, string button, Func<string> run, string confirm = null)
		{
			Label = label;
			Help = help;
			_button = button;
			_run = run;
			_confirm = confirm;
		}

		protected override void BuildControl(RectTransform host)
		{
			RectTransform r = MMUi.NewRect("MM_Action", host);
			MMUi.Place(r, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(ControlWidth, 48f));
			_b = MMUi.MakeButton(r, _button, 22, Click).Framed(MMUi.Frame);
		}

		private void Click()
		{
			ModMenu.PlayClick();
			if (_confirm != null && Time.realtimeSinceStartup > _armedUntil)
			{
				_armedUntil = Time.realtimeSinceStartup + 4f;
				_b.Label.text = _confirm;
				ModMenu.Status("Click again within 4 seconds to confirm.");
				return;
			}
			_armedUntil = 0f;
			string msg = _run();
			if (msg != null)
			{
				ModMenu.Changed(msg);
			}
		}

		protected override void RefreshControl(bool enabled)
		{
			if (_b == null)
			{
				return;
			}
			if (Time.realtimeSinceStartup > _armedUntil)
			{
				_b.Label.text = _button;
			}
			_b.Interactable = enabled;
			_b.Apply();
		}
	}

	/// <summary>The pages of the Mod Settings window. Every config entry of LOM_UI_EN and LOM_Strings_EN appears exactly once.</summary>
	public static class ModMenuPages
	{
		private static ChoiceItem OnOff(ConfigEntry<bool> e, string label, string help, Action after = null)
		{
			return new ChoiceItem(label, help, new string[2] { "On", "Off" }, () => e.Value ? 0 : 1, delegate(int i)
			{
				bool v = i == 0;
				if (e.Value != v)
				{
					e.Value = v;
					after?.Invoke();
				}
				return label + ": " + (v ? "on" : "off") + ".";
			})
			{
				Reset = ResetOf(e, after)
			};
		}

		private static Func<bool> ResetOf<T>(ConfigEntry<T> e, Action after)
		{
			return delegate
			{
				T def = (T)e.DefaultValue;
				if (EqualityComparer<T>.Default.Equals(e.Value, def))
				{
					return false;
				}
				e.Value = def;
				after?.Invoke();
				return true;
			};
		}

		private static NumberItem Number(ConfigEntry<float> e, string label, string help, float min, float max, float step, Func<float, string> fmt, Action after = null)
		{
			return new NumberItem(label, help, min, max, step, () => e.Value, delegate(float v)
			{
				e.Value = v;
				after?.Invoke();
			}, fmt)
			{
				Reset = ResetOf(e, after)
			};
		}

		private static NumberItem Number(ConfigEntry<int> e, string label, string help, int min, int max, int step, Func<float, string> fmt, Action after = null)
		{
			return new NumberItem(label, help, min, max, step, () => e.Value, delegate(float v)
			{
				e.Value = Mathf.RoundToInt(v);
				after?.Invoke();
			}, fmt)
			{
				Reset = ResetOf(e, after)
			};
		}

		// ---- translation ------------------------------------------------------------------------------------------

		private static bool OverridesOn => StringsPlugin.CfgEnabled != null && StringsPlugin.CfgEnabled.Value;

		private static int PresetIndex()
		{
			TextSource t = TranslationProfiles.CfgTable.Value;
			TextSource s = TranslationProfiles.CfgScene.Value;
			if (t == TextSource.Revised && s == TextSource.Revised && OverridesOn)
			{
				return 0;
			}
			if (t == TextSource.Original && s == TextSource.Original && !OverridesOn)
			{
				return 1;
			}
			if (t == TextSource.Off && s == TextSource.Off && !OverridesOn)
			{
				return 2;
			}
			return -1;
		}

		/// <summary>Under Which translation: whether it applies at all, and why no preset is selected (the layers are set on Advanced,
		/// or only the wording fixes on Localization differ).</summary>
		private static string PresetStatus()
		{
			if (!EnglishLanguage.Active)
			{
				return Warn("Not in use: the game's language isn't English (Settings > Language).");
			}
			string s = (F.GameText.Live && F.SceneText.Live) ? null : Warn("Part of the text isn't working with this game version (see Compatibility).");
			if (PresetIndex() < 0)
			{
				string mix = (TranslationProfiles.CfgTable.Value != TranslationProfiles.CfgScene.Value) ? "Custom mix, set on the Advanced page." : ("Custom mix: wording fixes are " + (OverridesOn ? "on" : "off") + " (Localization page).");
				s = (s == null) ? Warn(mix) : (s + "\n" + Warn(mix));
			}
			return s;
		}

		/// <summary>Lists in help and status lines: one bullet per item (the window font, Palatino Linotype, has it).</summary>
		private const string Bullet = "• ";

		private const string BaseName = Plugin.BaseModName;

		private const string BaseShort = Plugin.BaseModShortName;

		private static string Warn(string s)
		{
			return "<color=" + MMUi.WarnHex + ">" + s + "</color>";
		}

		/// <summary>TranslationProfiles.OriginalProblem names files; a player only needs to know what to do about it.</summary>
		private static string ShortProblem(string problem)
		{
			return problem.Contains("not installed") ? (BaseName + " isn't installed") : (BaseName + " needs reinstalling");
		}

		private static string ModeName(LayerMode m)
		{
			switch (m)
			{
			case LayerMode.Revised:
				return "Revised";
			case LayerMode.OriginalHandBack:
			case LayerMode.OriginalFromFiles:
				return BaseName;
			case LayerMode.BaseOnly:
				return BaseName + " only";
			default:
				return "off";
			}
		}

		private static string ApplyPreset(int i)
		{
			if (i == 0)
			{
				TranslationProfiles.SetTable(TextSource.Revised);
				TranslationProfiles.SetScene(TextSource.Revised);
				StringsPlugin.CfgEnabled.Value = true;
				StringOverrides.Refresh();
				return "Revised translation on.";
			}
			if (i == 2)
			{
				TranslationProfiles.SetTable(TextSource.Off);
				TranslationProfiles.SetScene(TextSource.Off);
				StringsPlugin.CfgEnabled.Value = false;
				StringOverrides.Refresh();
				OriginalMod.Report r = OriginalMod.Current;
				if (r.BinarizerHooked || r.XUnityRunning)
				{
					return "UI only: " + BaseName + " provides the text.";
				}
				return r.AnyInstalled ? ("UI only: " + BaseName + " isn't running, so the text shows in Chinese.") : ("UI only: without " + BaseName + " the text shows in Chinese.");
			}
			string tableProblem = TranslationProfiles.OriginalProblem(table: true);
			string sceneProblem = TranslationProfiles.OriginalProblem(table: false);
			if (tableProblem != null && sceneProblem != null)
			{
				// Nothing could show the patch's text, so nothing changes (the wording fixes stay as they are).
				return "Can't switch: " + ShortProblem(tableProblem) + ".";
			}
			TranslationProfiles.SetTable(TextSource.Original);
			TranslationProfiles.SetScene(TextSource.Original);
			StringsPlugin.CfgEnabled.Value = false;
			StringOverrides.Refresh();
			string msg = BaseName + " translation on.";
			if (tableProblem != null || sceneProblem != null)
			{
				msg += " Some text stays Revised: " + ShortProblem(tableProblem ?? sceneProblem) + ".";
			}
			return msg;
		}

		private static ChoiceItem SourceChoice(ConfigEntry<TextSource> e, bool table, string label, string help, Action<TextSource> apply, Func<string> status)
		{
			return new ChoiceItem(label, help, new string[3] { "Revised", BaseShort, "Off" }, () => (int)e.Value, delegate(int i)
			{
				TextSource src = (TextSource)i;
				string problem = TranslationProfiles.OriginalProblem(table);
				if (src == TextSource.Original && problem != null)
				{
					return "Not available here: " + ShortProblem(problem) + ".";
				}
				apply(src);
				return label + ": " + ModeName(table ? TranslationProfiles.TableMode : TranslationProfiles.SceneMode) + ".";
			})
			{
				Status = status,
				Reset = delegate
				{
					if (e.Value == TextSource.Revised)
					{
						return false;
					}
					apply(TextSource.Revised);
					return true;
				}
			};
		}

		private static string N(long n)
		{
			return n.ToString("N0", CultureInfo.InvariantCulture);
		}

		private static string BepInExVersion()
		{
			try
			{
				// Paths.BepInExVersion is a SemanticVersioning.Version; read it untyped so the build needs no extra reference.
				return typeof(BepInEx.Paths).GetProperty("BepInExVersion")?.GetValue(null, null)?.ToString() ?? "?";
			}
			catch (Exception)
			{
				return "?";
			}
		}

		/// <summary>Why a layer reads the OverLlm patch's file itself rather than handing the layer to that patch's plugin.</summary>
		private static string FromFilesReason(bool table)
		{
			OriginalMod.Report r = OriginalMod.Current;
			if (r.Detached)
			{
				return "its plugins are detached for testing";
			}
			bool running = table ? r.BinarizerHooked : (r.XUnityRunning && r.XUnityHooked);
			return running ? "its plugin's text couldn't be identified" : "its plugin isn't running";
		}

		/// <summary>A layer served by this mod's own lines alone because the OverLlm patch's text is not installed: plain when those
		/// lines carry the layer by themselves (TranslationProfiles.OwnTableComplete), a warning otherwise.</summary>
		private static string NoBaseWarning(int own, bool complete)
		{
			return complete ? ("This mod's " + N(own) + " lines.") : Warn("Only this mod's " + N(own) + " lines; the rest shows in Chinese without " + BaseName + ".");
		}

		private static string BaseStatus()
		{
			OriginalMod.Report r = OriginalMod.Current;
			if (!F.Detector.Live)
			{
				return Warn("Couldn't be checked with this game version.");
			}
			if (r.Detached)
			{
				return Warn("Detached for testing (Advanced page).");
			}
			switch (r.Summary)
			{
			case BaseSummary.Absent:
				return TranslationProfiles.OwnComplete ? "Not installed. It's optional: this mod has its own full translation." : Warn("Not installed, so some text shows in Chinese. Get it at github.com/joshfreitas1984/LegendOfMortalOverLlm.");
			case BaseSummary.Complete:
				return (r.BinarizerData == BaseData.OurEdits || r.XUnityData == BaseData.OurEdits) ? Warn("Installed, but its files hold edits from an older version of this mod. Reinstall it to use its translation.") : "Installed.";
			default:
				return Warn("Partly installed. Reinstall it to use its translation.");
			}
		}

		private static string FallbackStatus()
		{
			if (!OriginalMod.Current.BaseTableFile)
			{
				return "Needs " + BaseName + ".";
			}
			if (TranslationProfiles.TableMode != LayerMode.Revised)
			{
				return "Only with Revised.";
			}
			int n = TextTable.FellBackChanged + TextTable.FellBackPlaceholders;
			string s = (n > 0) ? (N(n) + " lines used " + BaseName + " this session.") : null;
			string problem = TextTable.RecordsProblem ?? (TranslationProfiles.TableHasRecords ? null : "the revision record has no game-text hashes, so game updates aren't noticed");
			if (problem != null)
			{
				s = ((s == null) ? "" : (s + " ")) + Warn(char.ToUpperInvariant(problem[0]) + problem.Substring(1) + ".");
			}
			return s;
		}

		private static string TableStatus()
		{
			if (!EnglishLanguage.Active)
			{
				return Warn("Not in use: the game's language isn't English.");
			}
			if (!F.GameText.Live)
			{
				return Warn("Not working with this game version; " + (OriginalMod.Current.BinarizerHooked ? (BaseName + " provides this text.") : "the game's own text shows."));
			}
			if (TranslationProfiles.CfgTable.Value == TextSource.Off)
			{
				return (TranslationProfiles.TableMode == LayerMode.BaseOnly) ? ("Off: " + BaseName + " provides this text.") : Warn("Off: the game's own (Chinese) text shows.");
			}
			string s;
			switch (TranslationProfiles.TableMode)
			{
			case LayerMode.Revised:
				s = TranslationProfiles.TableHasBase ? ("This mod's " + N(TranslationProfiles.OwnTableRows) + " lines, " + BaseName + " for the rest (" + N(TranslationProfiles.TableEntries) + " in all).") : NoBaseWarning(TranslationProfiles.OwnTableRows, TranslationProfiles.OwnTableComplete);
				if (TextTable.Gaps > 0)
				{
					s += " " + N(TextTable.Gaps) + " with no English this session.";
				}
				if (TranslationProfiles.TableError != null)
				{
					s += " " + Warn(TranslationProfiles.TableError + ".");
				}
				break;
			case LayerMode.OriginalHandBack:
				s = "The plugin of " + BaseName + " (" + N(OriginalMod.Current.BinarizerEntries) + " lines).";
				break;
			case LayerMode.OriginalFromFiles:
				s = "The file of " + BaseName + ", " + N(TranslationProfiles.TableEntries) + " lines (" + FromFilesReason(table: true) + ").";
				break;
			case LayerMode.BaseOnly:
				s = Warn("This mod's lines couldn't be read (" + TranslationProfiles.TableError + "); those of " + BaseName + " are used.");
				break;
			default:
				s = Warn("This mod's lines couldn't be read (" + TranslationProfiles.TableError + "); this text shows in Chinese.");
				break;
			}
			if (TranslationProfiles.TableOriginalProblem != null)
			{
				s += " " + Warn("Showing this mod's text: " + ShortProblem(TranslationProfiles.TableOriginalProblem) + ".");
			}
			return s;
		}

		private static string SceneStatus()
		{
			if (!EnglishLanguage.Active)
			{
				return Warn("Not in use: the game's language isn't English.");
			}
			if (!F.SceneText.Live)
			{
				return Warn("Not working with this game version; " + (OriginalMod.Current.XUnityRunning ? (BaseName + " translates on its own.") : "the game's own text shows."));
			}
			if (TranslationProfiles.CfgScene.Value == TextSource.Off)
			{
				return (TranslationProfiles.SceneMode == LayerMode.BaseOnly) ? ("Off: " + BaseName + " translates on its own.") : Warn("Off: the game's own (Chinese) text shows.");
			}
			string session = " This session: " + N(SceneText.Hits) + " shown, " + N(SceneText.SessionUntranslated) + " without a line" + ((SceneText.BaseFills > 0) ? (", " + N(SceneText.BaseFills) + " from " + BaseName) : "") + ".";
			string s;
			switch (TranslationProfiles.SceneMode)
			{
			case LayerMode.Revised:
				s = (TranslationProfiles.SceneHasBase ? ("This mod's " + N(TranslationProfiles.OwnSceneLines) + " lines, " + BaseName + " for the rest (" + N(TranslationProfiles.SceneEntries) + " in all).") : NoBaseWarning(TranslationProfiles.OwnSceneLines, TranslationProfiles.OwnSceneComplete)) + session;
				if (TranslationProfiles.SceneError != null)
				{
					s += " " + Warn(TranslationProfiles.SceneError + ".");
				}
				break;
			case LayerMode.OriginalHandBack:
				s = "The plugin of " + BaseName + ".";
				break;
			case LayerMode.OriginalFromFiles:
				s = "The file of " + BaseName + ", " + N(TranslationProfiles.SceneEntries) + " lines (" + FromFilesReason(table: false) + ")." + session;
				break;
			case LayerMode.BaseOnly:
				s = Warn("This mod's lines couldn't be read (" + TranslationProfiles.SceneError + "); " + BaseName + " translates on its own.");
				break;
			default:
				s = Warn("This mod's lines couldn't be read (" + TranslationProfiles.SceneError + "); this text shows in Chinese.");
				break;
			}
			if (TranslationProfiles.SceneOriginalProblem != null)
			{
				s += " " + Warn("Showing this mod's text: " + ShortProblem(TranslationProfiles.SceneOriginalProblem) + ".");
			}
			return s;
		}

		// ---- compatibility ------------------------------------------------------------------------------------------

		/// <summary>The summary line, then one line per feature that is not fully working (none when all is well).</summary>
		private static string CompatStatus()
		{
			string s = Compat.Summary();
			s = char.ToUpperInvariant(s[0]) + s.Substring(1) + ".";
			if (Compat.CountBad == 0)
			{
				return s;
			}
			StringBuilder sb = new StringBuilder(Warn(s));
			foreach (Feature f in Compat.All)
			{
				if (f.Health != Health.Working && f.Health != Health.NotNeeded)
				{
					sb.Append('\n').Append(Warn(Bullet + f.Name + ": " + f.StatusLine()));
				}
			}
			return sb.ToString();
		}

		/// <summary>Advanced: every feature with its group, hooks and id (the ids are what the Simulate settings take).</summary>
		private static string AllParts()
		{
			StringBuilder sb = new StringBuilder();
			string group = null;
			foreach (Feature f in Compat.All)
			{
				if (f.Group != group)
				{
					group = f.Group;
					sb.Append((sb.Length > 0) ? "\n" : "").Append(group).Append(':');
				}
				string line = Bullet + f.Name + " [" + f.Id + "]: " + f.StatusLine();
				sb.Append('\n').Append((f.Health == Health.Working || f.Health == Health.NotNeeded) ? line : Warn(line));
			}
			return sb.ToString();
		}

		private static MenuPage CompatibilityPage()
		{
			MenuPage page = new MenuPage
			{
				Title = "Compatibility",
				Intro = "If a game update breaks part of this mod, that part switches itself off and is listed here. The rest keeps working."
			};
			page.Items.Add(new InfoItem
			{
				Label = "Game version",
				Status = delegate
				{
					if (Compat.VerifiedFingerprint == null)
					{
						return "Not yet checked with this mod.";
					}
					if (Compat.GameChanged)
					{
						return Warn("The game has been updated since this mod was checked (" + Compat.VerifiedOn + ").");
					}
					return "Checked with this mod on " + Compat.VerifiedOn + ".";
				}
			});
			page.Items.Add(new InfoItem
			{
				Label = "Status",
				Status = CompatStatus
			});
			page.Items.Add(new SectionItem("If something stopped working"));
			page.Items.Add(new ActionItem("Try again", "Turns back on whatever switched itself off this session.", "Try again", delegate
			{
				int n = Compat.TryAgain();
				return (n == 0) ? "Nothing had switched itself off." : (n + " feature(s) turned back on.");
			}));
			page.Items.Add(new ActionItem("Write a report", "Saves compat_report.txt in the mod's folder, to send to the mod's maintainer.", "Write report", delegate
			{
				string path = Compat.WriteReport();
				return (path != null) ? ("Saved " + path) : "Couldn't write the report (see the BepInEx log).";
			}));
			page.Items.Add(OnOff(Compat.CfgNotices, "On-screen notices", "A short message when part of this mod stops working."));
			return page;
		}

		/// <summary>Items tied to a feature that is not fully working say so under their own help text.</summary>
		private static void TieToFeatures(List<MenuPage> pages)
		{
			foreach (MenuPage page in pages)
			{
				foreach (MenuItem item in page.Items)
				{
					Feature feature = item.Feature;
					if (feature == null)
					{
						continue;
					}
					Func<string> inner = item.Status;
					item.Status = delegate
					{
						string s = inner?.Invoke();
						if (feature.Health == Health.Working || feature.Health == Health.NotNeeded)
						{
							return s;
						}
						string w = Warn(feature.StatusLine());
						return string.IsNullOrEmpty(s) ? w : (s + "\n" + w);
					};
				}
			}
		}

		private static string PartState(bool installed, bool running, string runningText)
		{
			return running ? runningText : (installed ? "installed, not running" : "not installed");
		}

		/// <summary>Advanced: the OverLlm patch's parts as detected.</summary>
		private static string AboutBase()
		{
			OriginalMod.Report r = OriginalMod.Current;
			string s = Bullet + "Game data (Binarizer): " + PartState(r.BinarizerInstalled, r.BinarizerHooked, N(Math.Max(r.BinarizerEntries, 0)) + " lines" + ((r.BinarizerData == BaseData.OurEdits) ? ", holding this mod's old edits" : "")) + ".\n" + Bullet + "Scene text (XUnity AutoTranslator): " + PartState(r.XUnityInstalled, r.XUnityRunning && r.XUnityHooked, "running" + ((r.XUnityData == BaseData.OurEdits) ? ", its file holding this mod's old edits" : "")) + ".\n" + Bullet + "Korean layout plugin: " + (r.KrSuppressed ? "hooks removed" : PartState(r.KrInstalled, r.KrHooked, "running")) + ".\n" + Bullet + "Its text files: game data " + (r.BaseTableFile ? "found" : "not found") + ", scene " + (r.BaseSceneFile ? "found" : "not found") + ". This mod only reads them.";
			if (r.Detached)
			{
				s += "\n" + Warn("Detached for testing.");
			}
			return s;
		}

		private static string Hotkeys()
		{
			return Bullet + "Mod Settings: " + KeyItem.KeyName(ModMenu.CfgOpenKey.Value) + "\n" + Bullet + "Quick save: " + KeyItem.KeyName(QuickSave.CfgSaveKey.Value) + "\n" + Bullet + "Quick load: " + KeyItem.KeyName(QuickSave.CfgLoadKey.Value);
		}

		/// <summary>
		/// The pages. The first group runs from what stays closest to the original to what departs furthest from it: Translation
		/// (which English, and the OverLlm patch this mod grew from), Localization (this mod's own fixes that fit the game to English)
		/// and Extras (additions neither the game nor that patch has). Compatibility, Advanced and About follow as a separate group.
		/// Written for players: one plain sentence per setting. Anything technical (the text layers, the OverLlm patch's plugins,
		/// formats, performance, logs, testing) is on Advanced, in the same order. A setting goes on the first page that fits.
		/// </summary>
		public static List<MenuPage> Create()
		{
			List<MenuPage> pages = new List<MenuPage>();

			MenuPage tr = new MenuPage
			{
				Title = "Translation",
				Intro = "Which English the game shows."
			};
			tr.Items.Add(new ChoiceItem("Which translation", "• Revised: this mod's reviewed translation.\n• " + BaseShort + ": the translation of " + BaseName + ".\n• UI only: no text from this mod; its fixes and extras stay.", new string[3] { "Revised", BaseShort, "UI only" }, PresetIndex, ApplyPreset)
			{
				Status = PresetStatus,
				Reset = delegate
				{
					if (PresetIndex() == 0)
					{
						return false;
					}
					ApplyPreset(0);
					return true;
				}
			});
			tr.Items.Add(new InfoItem
			{
				Label = BaseName,
				Help = "The fan translation this mod grew from.",
				Status = BaseStatus,
				Feature = F.Detector
			});
			tr.Items.Add(OnOff(TextTable.CfgFallback, "Fall back to " + BaseName, "When a game update changes a line this mod translated, show the newer line from " + BaseName + ".", delegate
			{
				TextTable.ClearDecisions();
				StringOverrides.Refresh();
			}));
			tr.Items[tr.Items.Count - 1].IsEnabled = () => OriginalMod.Current.BaseTableFile;
			tr.Items[tr.Items.Count - 1].Status = FallbackStatus;
			tr.Items[tr.Items.Count - 1].Feature = F.Fallback;
			pages.Add(tr);

			MenuPage loc = new MenuPage
			{
				Title = "Localization",
				Intro = "This mod's fixes for playing in English."
			};
			loc.Items.Add(new SectionItem("Screens"));
			loc.Items.Add(OnOff(Plugin.CfgEnabled, "Layout fixes", "Fits English text into screens made for Chinese.", Plugin.Reapply));
			loc.Items[loc.Items.Count - 1].Feature = F.Layout;
			loc.Items.Add(OnOff(Plugin.CfgSpriteReplace, "Translated images", "Replaces pictures that have Chinese writing in them.", Plugin.Reapply));
			loc.Items[loc.Items.Count - 1].IsEnabled = () => Plugin.CfgEnabled.Value;
			loc.Items[loc.Items.Count - 1].Feature = F.Layout;
			loc.Items.Add(OnOff(EnglishLanguage.CfgHorizontalLayouts, "Horizontal text", "Game-over and ending captions run left to right instead of in vertical columns."));
			loc.Items[loc.Items.Count - 1].IsEnabled = () => Plugin.CfgEnabled.Value;
			loc.Items[loc.Items.Count - 1].Feature = F.Horizontal;
			loc.Items.Add(Number(Plugin.CfgCombatHoverOffsetX, "Duel card slide", "How far a duel card moves left when you point at it. 0 = the game's own upward slide.", 0f, 200f, 10f, v => v.ToString("0", CultureInfo.InvariantCulture) + " px"));
			loc.Items[loc.Items.Count - 1].IsEnabled = () => Plugin.CfgEnabled.Value;
			loc.Items[loc.Items.Count - 1].Feature = F.CombatHover;
			loc.Items.Add(new SectionItem("Wording"));
			loc.Items.Add(OnOff(StringsPlugin.CfgEnabled, "Wording fixes", "Clearer, shorter menu labels. Which translation also switches these.", StringOverrides.Refresh));
			loc.Items[loc.Items.Count - 1].Feature = F.Wording;
			loc.Items.Add(OnOff(StringsPlugin.CfgNormalize, "Tidy punctuation", "Turns Chinese punctuation in menus into English punctuation.", StringOverrides.Refresh));
			loc.Items[loc.Items.Count - 1].IsEnabled = () => OverridesOn;
			loc.Items.Add(OnOff(StringsPlugin.CfgNormalizeStory, "Tidy dialogue punctuation", "Does the same in dialogue.", StringOverrides.Refresh));
			loc.Items[loc.Items.Count - 1].IsEnabled = () => OverridesOn && StringsPlugin.CfgNormalize.Value;
			loc.Items.Add(OnOff(EnglishLanguage.CfgEnglishNumerals, "English numerals", "Shows levels as numbers instead of Chinese numerals."));
			loc.Items[loc.Items.Count - 1].Feature = F.Numerals;
			loc.Items.Add(new SectionItem("Dialogue"));
			loc.Items.Add(Number(Plugin.CfgTextSpeed, "Text speed", "How fast dialogue types out. 2× suits English.", 0.5f, 6f, 0.25f, v => v.ToString("0.##", CultureInfo.InvariantCulture) + "×", TextSpeedPatch.ApplyAll));
			loc.Items[loc.Items.Count - 1].Feature = F.TextSpeed;
			loc.Items.Add(OnOff(Plugin.CfgLogGuard, "Keep long logs visible", "Stops the dialogue log from going blank in long chapters."));
			loc.Items[loc.Items.Count - 1].Feature = F.StoryLog;
			loc.Items.Add(Number(Plugin.CfgLogGlyphBudget, "Log length", "How much of the log is kept. Lower it if the log still goes blank.", 2000, 16000, 500, v => v.ToString("N0", CultureInfo.InvariantCulture) + " characters"));
			loc.Items[loc.Items.Count - 1].IsEnabled = () => Plugin.CfgLogGuard.Value;
			pages.Add(loc);

			MenuPage extras = new MenuPage
			{
				Title = "Extras",
				Intro = "Additions the game doesn't have."
			};
			extras.Items.Add(new SectionItem("Name tooltips"));
			extras.Items.Add(OnOff(NameTips.CfgEnabled, "Character tooltips", "Point at a name in dialogue to see the character's portrait, title and your affinity.", NameTips.OnSettingChanged));
			extras.Items[extras.Items.Count - 1].Feature = F.NameTips;
			extras.Items.Add(OnOff(NameTips.CfgMetOnly, "Only after introductions", "A character gets a tooltip once the story has introduced them, so nothing is spoiled."));
			extras.Items[extras.Items.Count - 1].IsEnabled = () => NameTips.CfgEnabled.Value;
			extras.Items.Add(new TextItem(NameTips.CfgColor, "Character name colour", "A colour code such as #E8C47C. Empty = no colour."));
			extras.Items[extras.Items.Count - 1].IsEnabled = () => NameTips.CfgEnabled.Value;
			extras.Items.Add(OnOff(NameTips.CfgFactions, "Faction tooltips", "Point at a sect or clan name to see who they are.", NameTips.OnSettingChanged));
			extras.Items[extras.Items.Count - 1].Feature = F.NameTips;
			extras.Items.Add(new TextItem(NameTips.CfgFactionColor, "Faction name colour", "A colour code such as #8FC7B0. Empty = no colour."));
			extras.Items[extras.Items.Count - 1].IsEnabled = () => NameTips.CfgFactions.Value;
			extras.Items.Add(new SectionItem("Relationships"));
			extras.Items.Add(OnOff(Plugin.CfgRelationshipHover, "Exact affinity", "Point at a level in Status > Social to see the exact affinity out of 100.", RelationshipHover.OnToggle));
			extras.Items[extras.Items.Count - 1].Feature = F.AffinityHover;
			extras.Items.Add(new SectionItem("Duels"));
			extras.Items.Add(OnOff(BuildupGauges.CfgMarks, "Poison and paralysis marks", "Marks where each level of poison and paralysis starts on the duel gauges.", BuildupGauges.OnSettingChanged));
			extras.Items[extras.Items.Count - 1].Feature = F.Buildup;
			extras.Items.Add(OnOff(BuildupGauges.CfgTips, "Gauge details", "Point at a poison or paralysis gauge to see the exact numbers.", BuildupGauges.OnSettingChanged));
			extras.Items[extras.Items.Count - 1].Feature = F.Buildup;
			extras.Items.Add(new SectionItem("Quick save"));
			extras.Items.Add(OnOff(QuickSave.CfgEnabled, "Quick save and load", "Save to and load your current save slot with a key, wherever the game allows saving."));
			extras.Items[extras.Items.Count - 1].Feature = F.QuickSave;
			extras.Items.Add(new KeyItem(QuickSave.CfgSaveKey, "Quick save key", "Click, then press a key. Esc cancels, Delete clears."));
			extras.Items[extras.Items.Count - 1].IsEnabled = () => QuickSave.CfgEnabled.Value;
			extras.Items.Add(new KeyItem(QuickSave.CfgLoadKey, "Quick load key", "Loads without asking: unsaved progress is lost."));
			extras.Items[extras.Items.Count - 1].IsEnabled = () => QuickSave.CfgEnabled.Value;
			extras.Items.Add(OnOff(QuickSave.CfgToast, "On-screen messages", "A short message when you quick save or load."));
			extras.Items[extras.Items.Count - 1].IsEnabled = () => QuickSave.CfgEnabled.Value;
			pages.Add(extras);

			MenuPage compat = CompatibilityPage();
			compat.NewGroup = true;
			pages.Add(compat);

			MenuPage adv = new MenuPage
			{
				Title = "Advanced",
				Intro = "Technical settings. Most players never need these."
			};
			adv.Items.Add(new SectionItem("Translation"));
			adv.Items.Add(SourceChoice(TranslationProfiles.CfgTable, table: true, "Game data text", "Items, skills, names, menus and story.", TranslationProfiles.SetTable, TableStatus));
			adv.Items[adv.Items.Count - 1].Feature = F.GameText;
			adv.Items.Add(SourceChoice(TranslationProfiles.CfgScene, table: false, "Scene text", "Buttons, captions and battle lines drawn on screen.", TranslationProfiles.SetScene, SceneStatus));
			adv.Items[adv.Items.Count - 1].Feature = F.SceneText;
			adv.Items.Add(OnOff(SceneText.CfgLetBaseFillGaps, "Let " + BaseName + " fill gaps", BaseName + " machine-translates on-screen text that has no English line."));
			adv.Items[adv.Items.Count - 1].IsEnabled = () => OriginalMod.Current.XUnityRunning && XUnityBridge.Registered;
			adv.Items[adv.Items.Count - 1].Status = delegate
			{
				if (!OriginalMod.Current.XUnityRunning)
				{
					return BaseName + " isn't running.";
				}
				return XUnityBridge.Registered ? null : Warn("Couldn't connect to the scene translator of " + BaseName + " (see Compatibility).");
			};
			adv.Items[adv.Items.Count - 1].Feature = F.XUnityBridge;
			adv.Items.Add(OnOff(SceneText.CfgApplyResizers, "Text sizes of " + BaseName, "Uses the text-size adjustments of " + BaseName + "."));
			adv.Items[adv.Items.Count - 1].Status = () => N(SceneText.ResizeNodes) + " adjustments loaded.";
			adv.Items.Add(OnOff(SceneText.CfgWideTextOverflow, "Wide text overflow", "Very wide text may wrap past its box, as with " + BaseName + "."));
			adv.Items.Add(OnOff(Plugin.CfgQuietBinarizer, "Quiet " + BaseName + " lookups", "Stops " + BaseName + " logging every text lookup."));
			adv.Items[adv.Items.Count - 1].IsEnabled = () => PerfPatches.Hooked;
			adv.Items[adv.Items.Count - 1].Status = () => PerfPatches.Hooked ? ("Matters only when showing " + BaseName + ".") : (BaseName + " isn't running.");
			adv.Items[adv.Items.Count - 1].Feature = F.QuietBinarizer;
			adv.Items.Add(OnOff(OriginalMod.CfgSuppressKr, "Remove the KR plugin's hooks", "Turns off the Korean layout plugin that comes with " + BaseName + "; this mod makes the same fixes."));
			adv.Items[adv.Items.Count - 1].Restart = true;
			adv.Items[adv.Items.Count - 1].Status = () => OriginalMod.Current.KrSuppressed ? "Removed at startup." : (OriginalMod.Current.KrHooked ? "Running." : "Not running.");
			adv.Items.Add(OnOff(SceneText.CfgLogUntranslated, "Note untranslated text", "Lists Chinese text that has no English line in translation/untranslated.txt."));
			adv.Items.Add(new TextItem(SceneText.CfgSkipPaths, "Never translate", "Object paths to leave alone, separated by ';'.", SceneText.ClearSkipCache));
			adv.Items.Add(new ActionItem("Reload translation files", "Re-reads the translation files and redraws the text.", "Reload", TranslationProfiles.ReloadFiles));
			adv.Items.Add(new InfoItem
			{
				Label = BaseName + " details",
				Status = AboutBase
			});
			adv.Items.Add(new SectionItem("Localization"));
			adv.Items.Add(new TextItem(EnglishLanguage.CfgStoryLineSpacing, "Dialogue line spacing", "Spacing by line length, as length:spacing steps; * = longer. Empty = the game's.")
			{
				Status = () => (EnglishLanguage.Ladder() != null && EnglishLanguage.LadderError != null) ? Warn("Not understood (" + EnglishLanguage.LadderError + "); using the game's spacing.") : null
			});
			adv.Items[adv.Items.Count - 1].IsEnabled = () => Plugin.CfgEnabled.Value;
			adv.Items[adv.Items.Count - 1].Feature = F.LineSpacing;
			adv.Items.Add(new TextItem(Plugin.CfgTmpFallbackFont, "Latin fallback font", "Game font that fills in Latin letters where a label's font has none. Empty = off.")
			{
				Restart = true
			});
			adv.Items.Add(new SectionItem("Extras"));
			adv.Items.Add(new TextItem(Plugin.CfgRelationshipHoverFormat, "Affinity tip text", "{0} = value, {1} = maximum, {2} = the stat's name."));
			adv.Items[adv.Items.Count - 1].IsEnabled = () => Plugin.CfgRelationshipHover.Value;
			adv.Items.Add(new TextItem(QuickSave.CfgSavedText, "Quick save message", "{0} = slot, {1} = in-game date."));
			adv.Items[adv.Items.Count - 1].IsEnabled = () => QuickSave.CfgEnabled.Value && QuickSave.CfgToast.Value;
			adv.Items.Add(new TextItem(QuickSave.CfgLoadingText, "Quick load message", "{0} = slot, {1} = in-game date of the save."));
			adv.Items[adv.Items.Count - 1].IsEnabled = () => QuickSave.CfgEnabled.Value && QuickSave.CfgToast.Value;
			adv.Items.Add(new SectionItem("Performance"));
			adv.Items.Add(OnOff(Plugin.CfgLazyLog, "Update the log only when opened", "The game rebuilds the whole log after every line; this waits until you open it."));
			adv.Items[adv.Items.Count - 1].Feature = F.StoryLog;
			adv.Items.Add(OnOff(Plugin.CfgFullScenePass, "Rescan every scene", "Re-applies the layout fixes to every open scene on each load. Slower."));
			adv.Items.Add(OnOff(Plugin.CfgNoInfoStackTraces, "No stack traces on info logs", "Makes ordinary log messages cheaper.", PerfPatches.ApplyStackTraceSetting));
			adv.Items.Add(new SectionItem("Logs and testing"));
			adv.Items.Add(OnOff(Plugin.CfgVerbose, "Detailed layout log", "Logs every layout fix applied, with timings."));
			adv.Items.Add(OnOff(StringsPlugin.CfgLog, "Log wording fixes", "Logs every wording fix applied."));
			adv.Items.Add(OnOff(StringsPlugin.CfgTrace, "Trace string keys", "Writes every text key the game looks up to harness/out/strings_trace.txt."));
			adv.Items.Add(OnOff(Plugin.CfgHarness, "Dev harness", "Runs test commands from BepInEx/cache/LOM_UI_EN/harness/cmd.txt.", Plugin.SyncHarness));
			adv.Items.Add(new KeyItem(Plugin.CfgDumpKey, "Screen dump key", "Saves the screen's layout and a screenshot to harness/out."));
			adv.Items.Add(new KeyItem(Plugin.CfgReloadKey, "Reload layout rules key", "Re-reads the layout fixes, images and fonts."));
			adv.Items.Add(new KeyItem(StringsPlugin.CfgReloadKey, "Reload wording fixes key", "Re-reads the wording fixes."));
			adv.Items.Add(OnOff(OriginalMod.CfgDetach, "Detach " + BaseName, "Testing only: runs this mod without the plugins of " + BaseName + "."));
			adv.Items[adv.Items.Count - 1].Restart = true;
			adv.Items.Add(new TextItem(Compat.CfgSimulateGameChange, "Simulate a game change", "Feature ids (see All features; 'all' for every one) to treat as broken at startup.")
			{
				Restart = true
			});
			adv.Items.Add(new TextItem(Compat.CfgSimulateHookErrors, "Simulate hook errors", "Feature ids whose hooks fail from now on. Clear it, then Compatibility > Try again."));
			adv.Items.Add(new ActionItem("Mark this game version as checked", "For the maintainer: records this game build as checked with this mod.", "Mark as checked", Compat.MarkVerified, "Click again to mark"));
			adv.Items.Add(new InfoItem
			{
				Label = "All features",
				Status = AllParts
			});
			adv.Items.Add(new SectionItem("Now"));
			adv.Items.Add(new ActionItem("Reload layout rules", "Same as the reload key.", "Reload rules", delegate
			{
				Plugin.Reload();
				return "Reloaded " + Rules.Count + " rules, " + SpriteStore.Count + " images, " + FontMap.Count + " font maps.";
			}));
			adv.Items.Add(new ActionItem("Reload wording fixes", "Same as the wording reload key.", "Reload wording", delegate
			{
				StringOverrides.Load();
				StringOverrides.Refresh();
				return "Reloaded " + StringOverrides.Count.ToString("N0", CultureInfo.InvariantCulture) + " wording fixes.";
			}));
			adv.Items.Add(new ActionItem("Dump this screen", "Closes this window and dumps the screen under it to harness/out.", "Close and dump", delegate
			{
				ModMenu.Close();
				(Plugin.Instance.GetComponent<DevHarness>() ?? Plugin.Instance.gameObject.AddComponent<DevHarness>()).HotkeyDump();
				return "Dumped.";
			}));
			pages.Add(adv);

			MenuPage about = new MenuPage
			{
				Title = "About"
			};
			about.Items.Add(new InfoItem
			{
				Label = "Version",
				Status = () => Plugin.DisplayName + " " + Plugin.VERSION + " on BepInEx " + BepInExVersion() + "."
			});
			about.Items.Add(new InfoItem
			{
				Label = "Updates and problems",
				Help = "New versions are at " + Plugin.ProjectUrl + ". To report a problem, attach the report from Compatibility > Write a report."
			});
			about.Items.Add(new SectionItem("This window"));
			about.Items.Add(new KeyItem(ModMenu.CfgOpenKey, "Mod Settings key", "Opens or closes this window anywhere. The game pauses while it is open."));
			about.Items.Add(OnOff(ModMenu.CfgTitleButton, "Title screen button", "A Mod Settings button on the title screen.", ModMenuEntry.OnConfigChanged));
			about.Items[about.Items.Count - 1].Feature = F.MenuButtons;
			about.Items.Add(OnOff(ModMenu.CfgPauseButton, "Pause menu button", "A Mod Settings button in the pause menus.", ModMenuEntry.OnConfigChanged));
			about.Items[about.Items.Count - 1].Feature = F.MenuButtons;
			about.Items.Add(new InfoItem
			{
				Label = "Keys",
				Status = Hotkeys
			});
			about.Items.Add(new SectionItem("Settings"));
			about.Items.Add(new InfoItem
			{
				Label = "Settings file",
				Help = "Saved as you go, in BepInEx/config/lom.ui.english.cfg and lom.strings.english.cfg."
			});
			about.Items.Add(new ActionItem("Reset everything", "Puts every setting on every page back to its default.", "Reset everything", delegate
			{
				ModMenu.ResetEverything();
				return null;
			}, "Click again to reset"));
			pages.Add(about);
			TieToFeatures(pages);
			return pages;
		}
	}
}
