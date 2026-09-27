using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using Mortal.Combat;
using Mortal.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LOM_UI_EN
{
	/// <summary>
	/// The duel's poison and paralysis gauges (CombatStatUI: a 128x12 fill in a 136x20 frame under each fighter's name). Build-up
	/// sets off a tier effect at two thresholds (50 and 100) and at the gauge's maximum (150, which also empties it); the frame art
	/// marks only 100, with a faint 1px notch, and the game's hover tip names only the next tier. This puts a mark at each
	/// threshold that lights up while that tier is in effect, and rewrites the hover tip with the exact build-up, every tier with
	/// what it does and how far off it is, and the fall at the start of each round (the fighter's resistance, at least 1).
	///
	/// The game keeps the thresholds and the tier effect keys as locals of CombatActionController.ModifyPoisonValue /
	/// ModifyParalyzedValue (hoisted into the coroutine as value1, value2, level1..level3); they are read from that IL, so the marks
	/// follow a game update that changes them, and one that restructures the method turns the feature off instead of marking the
	/// wrong places. A tier stays in effect below its threshold until the gauge empties (the game only removes it at 0 or when a
	/// higher tier replaces it), which is why the marks follow the tier effects, not the fill.
	/// </summary>
	public static class BuildupGauges
	{
		public static ConfigEntry<bool> CfgMarks;
		public static ConfigEntry<bool> CfgTips;

		private sealed class Tiers
		{
			/// <summary>The first two thresholds; the third tier fires at the gauge's maximum.</summary>
			public readonly int[] At = new int[2] { -1, -1 };
			public readonly string[] Keys = new string[3];
		}

		// Game objects are held as Component: this class then loads (and turns off cleanly) even if a game type is renamed.
		private sealed class Gauge
		{
			public Component Ui;
			public Component Bar;
			public Component Controller;
			public bool Poison;
			public RectTransform Fill;
			public bool FromRight;
			public Text TipText;
			public Image TipBox;
			public Color TipBoxColor;
			public RectTransform Marks;
			public readonly Image[] Edges = new Image[2];
			public readonly Image[] Cores = new Image[2];
			public string GameTip;
			public string OurTip;
			public string Signature;
			public int Value;
			public int Max;
			public int Drain;
			public int Active;
		}

		private static readonly Color Edge = new Color(0f, 0f, 0f, 0.85f);
		private static readonly Color Idle = new Color(0.96f, 0.91f, 0.8f, 0.95f);
		private static readonly Color PoisonHue = new Color(0.45f, 1f, 0.74f, 1f);
		private static readonly Color ParalysisHue = new Color(1f, 0.89f, 0.33f, 1f);
		// The tip box (back_tip_frame_1) is a light, see-through plate under dark text, so the tip's own colours are dark ones.
		private const string PoisonHex = "#0F6B3A";
		private const string ParalysisHex = "#7A4E00";
		private const string MutedHex = "#585858";

		private static Tiers _poison;
		private static Tiers _paralysis;
		private static readonly List<Gauge> _gauges = new List<Gauge>();
		private static float _nextCheck;

		public static void Bind(ConfigFile cfg)
		{
			CfgMarks = cfg.Bind("Duel", "BuildupMarks", true, "Duels: mark each poison and paralysis tier on the fighters' gauges (the game's thresholds, 50 and 100 of 150 in this version); a mark lights up while its tier is in effect.");
			CfgTips = cfg.Bind("Duel", "BuildupTips", true, "Duels: pointing at a poison or paralysis gauge shows the exact build-up, each tier with what it does and how far off it is, and how much the build-up falls at the start of each round. English only; with another game language the game's own tip stays.");
		}

		public static void Patch(Harmony h)
		{
			F.Buildup.OnTurnedOff = TurnOff;
			F.Buildup.OnTurnedOn = RefreshAll;
			Compat.Require(F.Buildup, "Mortal.Combat", "Mortal.Combat.CombatStatUI", "_poisonProgress");
			Compat.Require(F.Buildup, "Mortal.Combat", "Mortal.Combat.CombatStatUI", "_paralyzedProgress");
			Compat.Require(F.Buildup, "Mortal.Combat", "Mortal.Combat.CombatStatBar", "_normalImage");
			Compat.Require(F.Buildup, "Mortal.Combat", "Mortal.Combat.CombatStatBar", "_tipText", essential: false, part: "tips");
			Compat.Require(F.Buildup, "Mortal.Combat", "Mortal.Combat.CombatStatBar", "SetTipText", essential: false, part: "tips");
			Compat.Require(F.Buildup, "Mortal.Combat", "Mortal.Combat.CombatStat", "CurrentPoisonValue");
			Compat.Require(F.Buildup, "Mortal.Combat", "Mortal.Combat.CombatStat", "CurrentParalyzedValue");
			Compat.Require(F.Buildup, "Mortal.Combat", "Mortal.Combat.CombatStat", "PoisonResist", essential: false, part: "tips");
			Compat.Require(F.Buildup, "Mortal.Combat", "Mortal.Combat.CombatStat", "ParalyzedResist", essential: false, part: "tips");
			Compat.Require(F.Buildup, "Mortal.Combat", "Mortal.Combat.CombatStatController", "MaxPoisonValue");
			Compat.Require(F.Buildup, "Mortal.Combat", "Mortal.Combat.CombatStatController", "MaxParalyzedValue");
			Compat.Require(F.Buildup, "Mortal.Combat", "Mortal.Combat.CombatActionController", "ExistEffect");
			Compat.Require(F.Buildup, "Mortal.Combat", "Mortal.Combat.CombatManager", "PlayerAction");
			Compat.Require(F.Buildup, "Mortal.Combat", "Mortal.Combat.CombatManager", "EnemyAction");
			Compat.Part(F.Buildup, "poison tiers in CombatActionController.ModifyPoisonValue", delegate
			{
				_poison = ReadTiers("ModifyPoisonValue");
			}, essential: true, isHook: false);
			Compat.Part(F.Buildup, "paralysis tiers in CombatActionController.ModifyParalyzedValue", delegate
			{
				_paralysis = ReadTiers("ModifyParalyzedValue");
			}, essential: true, isHook: false);
			if (_poison == null || _paralysis == null)
			{
				return;
			}
			Compat.Part(F.Buildup, "CombatStatUI tip hooks", delegate
			{
				Type ui = typeof(CombatStatUI);
				Compat.Hook(F.Buildup, h, AccessTools.Method(ui, "UpdatePoisonTip"), "CombatStatUI.UpdatePoisonTip", null, new HarmonyMethod(typeof(BuildupGauges), nameof(UpdatePoisonTip_Postfix)), essential: true);
				Compat.Hook(F.Buildup, h, AccessTools.Method(ui, "UpdateParalyzedTip"), "CombatStatUI.UpdateParalyzedTip", null, new HarmonyMethod(typeof(BuildupGauges), nameof(UpdateParalyzedTip_Postfix)), essential: true);
			}, essential: true);
			Plugin.Log.LogInfo($"duel gauges: poison tiers at {_poison.At[0]}/{_poison.At[1]}/max ({string.Join(",", _poison.Keys)}), paralysis at {_paralysis.At[0]}/{_paralysis.At[1]}/max ({string.Join(",", _paralysis.Keys)})");
		}

		/// <summary>The thresholds and tier keys the coroutine stores first thing: ldc.i4 50 / stfld &lt;value1&gt;, ..., ldstr "D101" / stfld &lt;level1&gt;, ...</summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static Tiers ReadTiers(string method)
		{
			MethodInfo m = AccessTools.Method(typeof(CombatActionController), method, new Type[1] { typeof(int) });
			if (m == null)
			{
				throw new MissingMethodException("CombatActionController", method);
			}
			MethodInfo moveNext = AccessTools.EnumeratorMoveNext(m);
			if (moveNext == null)
			{
				throw new MissingMethodException("CombatActionController", method + " (coroutine body)");
			}
			Tiers t = new Tiers();
			List<CodeInstruction> il = PatchProcessor.GetOriginalInstructions(moveNext);
			for (int i = 1; i < il.Count; i++)
			{
				if (il[i].opcode != OpCodes.Stfld || !(il[i].operand is FieldInfo f))
				{
					continue;
				}
				CodeInstruction prev = il[i - 1];
				for (int k = 0; k < 2; k++)
				{
					if (f.Name.StartsWith("<value" + (k + 1) + ">", StringComparison.Ordinal))
					{
						t.At[k] = IntConstant(prev);
					}
				}
				for (int k = 0; k < 3; k++)
				{
					if (f.Name.StartsWith("<level" + (k + 1) + ">", StringComparison.Ordinal) && prev.opcode == OpCodes.Ldstr)
					{
						t.Keys[k] = prev.operand as string;
					}
				}
			}
			if (t.At[0] <= 0 || t.At[1] <= t.At[0] || Array.IndexOf(t.Keys, null) >= 0)
			{
				throw new MissingMemberException("CombatActionController", method + " tier thresholds (found " + t.At[0] + ", " + t.At[1] + ", " + string.Join(",", t.Keys) + ")");
			}
			return t;
		}

		private static int IntConstant(CodeInstruction ci)
		{
			OpCode op = ci.opcode;
			if (op == OpCodes.Ldc_I4 || op == OpCodes.Ldc_I4_S)
			{
				return Convert.ToInt32(ci.operand, CultureInfo.InvariantCulture);
			}
			OpCode[] shortForms = new OpCode[9] { OpCodes.Ldc_I4_0, OpCodes.Ldc_I4_1, OpCodes.Ldc_I4_2, OpCodes.Ldc_I4_3, OpCodes.Ldc_I4_4, OpCodes.Ldc_I4_5, OpCodes.Ldc_I4_6, OpCodes.Ldc_I4_7, OpCodes.Ldc_I4_8 };
			int n = Array.IndexOf(shortForms, op);
			return (n >= 0) ? n : int.MinValue;
		}

		// ---- hooks: the game refreshes a gauge's tip after every change of its build-up (and once when the duel starts) ------

		private static void UpdatePoisonTip_Postfix(Component __instance)
		{
			Hooked(__instance, poison: true);
		}

		private static void UpdateParalyzedTip_Postfix(Component __instance)
		{
			Hooked(__instance, poison: false);
		}

		private static void Hooked(Component ui, bool poison)
		{
			if (!F.Buildup.Live || ui == null)
			{
				return;
			}
			try
			{
				F.Buildup.Probe();
				Gauge g = Track(ui, poison);
				if (g != null)
				{
					// The game has just written its own tip: keep it for when ours is off, then apply ours over it.
					g.GameTip = (g.TipText != null) ? g.TipText.text : null;
					g.Signature = null;
					Refresh(g);
				}
			}
			catch (Exception ex)
			{
				F.Buildup.Fail(ex, poison ? "CombatStatUI.UpdatePoisonTip" : "CombatStatUI.UpdateParalyzedTip");
			}
		}

		/// <summary>Plugin.Update: the tier effects can also end without a build-up change (an event clears them), so look again a few times a second.</summary>
		public static void Update()
		{
			if (_gauges.Count == 0 || Time.unscaledTime < _nextCheck)
			{
				return;
			}
			_nextCheck = Time.unscaledTime + 0.25f;
			for (int i = _gauges.Count - 1; i >= 0; i--)
			{
				Gauge g = _gauges[i];
				if (g.Ui == null || g.Bar == null || g.Fill == null)
				{
					_gauges.RemoveAt(i);
					continue;
				}
				Refresh(g);
			}
		}

		private static Gauge Track(Component ui, bool poison)
		{
			foreach (Gauge x in _gauges)
			{
				if (x.Ui == ui && x.Poison == poison)
				{
					return x;
				}
			}
			Gauge g = NewGauge(ui, poison);
			if (g != null)
			{
				_gauges.Add(g);
			}
			return g;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static Gauge NewGauge(Component ui, bool poison)
		{
			Traverse t = Traverse.Create(ui);
			Component bar = t.Field(poison ? "_poisonProgress" : "_paralyzedProgress").GetValue<CombatStatBar>();
			Image fill = (bar != null) ? Traverse.Create(bar).Field("_normalImage").GetValue<Image>() : null;
			if (fill == null)
			{
				return null;
			}
			Gauge g = new Gauge
			{
				Ui = ui,
				Bar = bar,
				Poison = poison,
				Fill = fill.rectTransform,
				FromRight = fill.type == Image.Type.Filled && fill.fillMethod == Image.FillMethod.Horizontal && fill.fillOrigin == (int)Image.OriginHorizontal.Right
			};
			// The tip box needs no placing: the game mirrors the enemy's (scale -1,-1, text flipped back), so the enemy's tips
			// already open downward from the top of the screen and the player's upward from the bottom.
			if (F.Buildup.PartOk("tips"))
			{
				g.TipText = Traverse.Create(bar).Field("_tipText").GetValue<Text>();
				g.TipBox = (g.TipText != null && g.TipText.transform.parent != null) ? g.TipText.transform.parent.GetComponent<Image>() : null;
				if (g.TipBox != null)
				{
					g.TipBoxColor = g.TipBox.color;
				}
			}
			return g;
		}

		// ---- refresh --------------------------------------------------------------------------------------------------------

		private static void Refresh(Gauge g)
		{
			if (g.Controller == null)
			{
				g.Controller = FindController(g.Ui);
				if (g.Controller == null)
				{
					return;
				}
			}
			ReadState(g);
			bool marks = CfgMarks.Value;
			bool tips = CfgTips.Value && EnglishLanguage.Active && g.TipText != null && F.Buildup.PartOk("tips");
			string sig = g.Value + "/" + g.Max + "/" + g.Drain + "/" + g.Active + "/" + marks + "/" + tips;
			if (sig == g.Signature)
			{
				return;
			}
			g.Signature = sig;
			UpdateMarks(g, marks);
			UpdateTip(g, tips);
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static Component FindController(Component ui)
		{
			CombatManager cm = CombatManager.Instance;
			if (cm == null)
			{
				return null;
			}
			foreach (CombatActionController c in new CombatActionController[2] { cm.PlayerAction, cm.EnemyAction })
			{
				if (c != null && ReferenceEquals(c.UI, ui))
				{
					return c;
				}
			}
			return null;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void ReadState(Gauge g)
		{
			CombatActionController c = (CombatActionController)g.Controller;
			CombatStatController s = c.Stat;
			CombatStat d = s.Data;
			Tiers t = g.Poison ? _poison : _paralysis;
			g.Value = g.Poison ? d.CurrentPoisonValue : d.CurrentParalyzedValue;
			g.Max = g.Poison ? s.MaxPoisonValue : s.MaxParalyzedValue;
			g.Drain = F.Buildup.PartOk("tips") ? ReadDrain(d, g.Poison) : 0;
			int active = 0;
			for (int k = 0; k < 3; k++)
			{
				if (c.ExistEffect(t.Keys[k]))
				{
					active |= 1 << k;
				}
			}
			g.Active = active;
		}

		/// <summary>CombatActionController.RestorePoisonValue: ModifyPoisonValue(Mathf.Min(-PoisonResist, -1)) at the start of every round.
		/// Its own method, so a renamed resistance field costs only the tips part.</summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static int ReadDrain(object data, bool poison)
		{
			CombatStat d = (CombatStat)data;
			return Math.Max(poison ? d.PoisonResist : d.ParalyzedResist, 1);
		}

		// ---- marks ----------------------------------------------------------------------------------------------------------

		private static void UpdateMarks(Gauge g, bool on)
		{
			if (!on || g.Max <= 0)
			{
				if (g.Marks != null)
				{
					g.Marks.gameObject.SetActive(false);
				}
				return;
			}
			if (g.Marks == null)
			{
				BuildMarks(g);
			}
			g.Marks.gameObject.SetActive(true);
			Tiers t = g.Poison ? _poison : _paralysis;
			// A mark is lit while its tier or a higher one of the gauge is in effect (the third tier empties the gauge).
			int tier = ((g.Active & 2) != 0) ? 2 : (((g.Active & 1) != 0) ? 1 : 0);
			float height = g.Fill.rect.height;
			for (int k = 0; k < 2; k++)
			{
				float x = (float)t.At[k] / g.Max;
				bool show = x > 0f && x < 1f;
				g.Edges[k].gameObject.SetActive(show);
				g.Cores[k].gameObject.SetActive(show);
				if (!show)
				{
					continue;
				}
				if (g.FromRight)
				{
					x = 1f - x;
				}
				bool lit = tier > k;
				Place(g.Edges[k].rectTransform, x, lit ? 5f : 4f, height + (lit ? 14f : 12f));
				Place(g.Cores[k].rectTransform, x, lit ? 3f : 2f, height + (lit ? 12f : 10f));
				g.Edges[k].color = Edge;
				g.Cores[k].color = lit ? (g.Poison ? PoisonHue : ParalysisHue) : Idle;
			}
		}

		/// <summary>A layer over the fill with the fill's own rect, so a mark at x = threshold / max sits where the fill reaches that value.</summary>
		private static void BuildMarks(Gauge g)
		{
			RectTransform fill = g.Fill;
			// Named apart from the game's objects so no layout rule matches them.
			GameObject go = new GameObject("LOM_BuildupMarks", typeof(RectTransform));
			RectTransform rt = (RectTransform)go.transform;
			rt.SetParent(fill.parent, worldPositionStays: false);
			rt.anchorMin = fill.anchorMin;
			rt.anchorMax = fill.anchorMax;
			rt.pivot = fill.pivot;
			rt.anchoredPosition = fill.anchoredPosition;
			rt.sizeDelta = fill.sizeDelta;
			rt.localRotation = fill.localRotation;
			rt.localScale = fill.localScale;
			rt.SetAsLastSibling();
			for (int k = 0; k < 2; k++)
			{
				g.Edges[k] = NewMark(rt, "LOM_BuildupMarkEdge" + (k + 1));
				g.Cores[k] = NewMark(rt, "LOM_BuildupMark" + (k + 1));
			}
			g.Marks = rt;
		}

		private static Image NewMark(RectTransform parent, string name)
		{
			GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			go.transform.SetParent(parent, worldPositionStays: false);
			Image image = go.GetComponent<Image>();
			image.raycastTarget = false;
			return image;
		}

		private static void Place(RectTransform rt, float x, float width, float height)
		{
			rt.anchorMin = new Vector2(x, 0.5f);
			rt.anchorMax = new Vector2(x, 0.5f);
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.anchoredPosition = Vector2.zero;
			rt.sizeDelta = new Vector2(width, height);
		}

		// ---- tip --------------------------------------------------------------------------------------------------------------

		private static void UpdateTip(Gauge g, bool on)
		{
			if (g.TipText == null)
			{
				return;
			}
			if (!on)
			{
				if (g.OurTip != null)
				{
					// Put the game's own tip back (its text as the game last wrote it).
					SetTip(g, g.GameTip ?? "");
					g.OurTip = null;
				}
				if (g.TipBox != null)
				{
					g.TipBox.color = g.TipBoxColor;
				}
				return;
			}
			g.OurTip = TipText(g);
			SetTip(g, g.OurTip);
			// The game's box is 78% white over a see-through frame, fine for its two lines; seven lines over the fighter's name
			// and the dialogue label need a nearly solid plate.
			if (g.TipBox != null)
			{
				g.TipBox.color = new Color(g.TipBoxColor.r, g.TipBoxColor.g, g.TipBoxColor.b, Mathf.Max(g.TipBoxColor.a, 0.97f));
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void SetTip(Gauge g, string text)
		{
			((CombatStatBar)g.Bar).SetTipText(text);
		}

		/// <summary>
		/// Poison buildup: 60 / 150
		/// Falls by 29 at the start of each round.
		/// • At 50, Poison Lv1 (in effect): Lose 5% HP each round.        (bold, in the gauge's dark hue)
		/// • At 100, Poison Lv2 (40 more): Lose 10% HP and 3% Qi each round.
		/// • At 150, Poison Lv3 (90 more, then the gauge empties): Lose 50% HP at the end of the round.
		/// One bullet per tier, since the lines wrap. Otherwise ASCII: the tip's font is whatever the font map gives it
		/// (SourceHanSerifTC-Bold, which has the bullet).
		/// </summary>
		private static string TipText(Gauge g)
		{
			Tiers t = g.Poison ? _poison : _paralysis;
			string hue = g.Poison ? PoisonHex : ParalysisHex;
			StringBuilder sb = new StringBuilder(400);
			sb.Append("<b>").Append(g.Poison ? "Poison buildup: " : "Paralysis buildup: ").Append(g.Value).Append(" / ").Append(g.Max).Append("</b>");
			sb.Append("\n<color=").Append(MutedHex).Append(">Falls by ").Append(g.Drain).Append(" at the start of each round.</color>");
			for (int k = 0; k < 3; k++)
			{
				int at = (k < 2) ? t.At[k] : g.Max;
				bool active = (g.Active & (1 << k)) != 0;
				string state;
				if (active)
				{
					state = (g.Value >= at || k == 2) ? "in effect" : "in effect until the gauge empties";
				}
				else if (g.Value < at)
				{
					state = (at - g.Value) + " more" + ((k == 2) ? ", then the gauge empties" : "");
				}
				else
				{
					// Passed and replaced by the tier above it.
					state = null;
				}
				string name = Localized("CombatSkill/Name/" + t.Keys[k]);
				string body = Body(Localized("CombatSkill/Desc/" + t.Keys[k]));
				string color = active ? hue : ((state == null) ? MutedHex : null);
				sb.Append('\n');
				if (color != null)
				{
					sb.Append("<color=").Append(color).Append('>');
				}
				if (active)
				{
					sb.Append("<b>");
				}
				sb.Append("• At ").Append(at).Append(", ").Append(name ?? ("tier " + (k + 1)));
				if (state != null)
				{
					sb.Append(" (").Append(state).Append(')');
				}
				if (body != null)
				{
					sb.Append(": ").Append(body);
				}
				if (active)
				{
					sb.Append("</b>");
				}
				if (color != null)
				{
					sb.Append("</color>");
				}
			}
			return sb.ToString();
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static string Localized(string key)
		{
			string s = LocalizationManager.Instance.LocaleResolver.GetString(key);
			return (string.IsNullOrEmpty(s) || s == key) ? null : s.Trim();
		}

		/// <summary>The effect descriptions open with their own name ("Poisoned Lv2: Each round loses ..."); the tip names the tier itself.</summary>
		private static string Body(string desc)
		{
			if (desc == null)
			{
				return null;
			}
			int colon = desc.IndexOf(": ", StringComparison.Ordinal);
			if (colon > 0 && colon <= 32 && colon + 2 < desc.Length)
			{
				desc = desc.Substring(colon + 2).Trim();
			}
			return (desc.Length > 0) ? (char.ToUpperInvariant(desc[0]) + desc.Substring(1)) : null;
		}

		// ---- settings, turning off, harness ---------------------------------------------------------------------------------

		/// <summary>Mod Settings changed a [Duel] switch: redraw every gauge on screen.</summary>
		public static void OnSettingChanged()
		{
			RefreshAll();
		}

		private static void RefreshAll()
		{
			foreach (Gauge g in _gauges)
			{
				if (g.Ui != null && g.Fill != null)
				{
					g.Signature = null;
					if (F.Buildup.Live)
					{
						Refresh(g);
					}
				}
			}
		}

		/// <summary>The feature turned itself off: remove the marks and give every tip back to the game.</summary>
		private static void TurnOff()
		{
			foreach (Gauge g in _gauges)
			{
				try
				{
					if (g.Marks != null)
					{
						UnityEngine.Object.Destroy(g.Marks.gameObject);
						g.Marks = null;
					}
					if (g.Ui != null && g.TipText != null)
					{
						UpdateTip(g, on: false);
					}
					g.Signature = null;
				}
				catch (Exception ex)
				{
					Plugin.Log.LogWarning("duel gauges: undo failed: " + ex.Message);
				}
			}
		}

		/// <summary>Harness: `invoke static LOM_UI_EN.BuildupGauges.Describe`.</summary>
		public static string Describe()
		{
			StringBuilder sb = new StringBuilder();
			sb.Append("duel gauges (feature ").Append(F.Buildup.Health).Append(", marks ").Append(CfgMarks.Value).Append(", tips ").Append(CfgTips.Value).Append(", english ").Append(EnglishLanguage.Active).Append("): ");
			foreach (Gauge g in _gauges)
			{
				if (g.Ui == null)
				{
					continue;
				}
				sb.Append(g.Ui.transform.parent != null ? g.Ui.transform.parent.name : g.Ui.name).Append(g.Poison ? " poison " : " paralysis ").Append(g.Value).Append('/').Append(g.Max).Append(" drain ").Append(g.Drain).Append(" active ").Append(g.Active).Append(g.OurTip != null ? " tip" : "").Append(g.Marks != null && g.Marks.gameObject.activeSelf ? " marked" : "").Append("; ");
			}
			return sb.ToString();
		}

		/// <summary>Harness: `invoke static LOM_UI_EN.BuildupGauges.TestAdd player poison 60` runs the game's own ModifyPoisonValue on a fighter.</summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static IEnumerator TestAdd(string side, string kind, int amount)
		{
			CombatManager cm = CombatManager.Instance;
			CombatActionController c = (cm == null) ? null : (side.StartsWith("e", StringComparison.OrdinalIgnoreCase) ? cm.EnemyAction : cm.PlayerAction);
			if (c == null)
			{
				throw new InvalidOperationException("no duel running");
			}
			return kind.StartsWith("para", StringComparison.OrdinalIgnoreCase) ? c.ModifyParalyzedValue(amount) : c.ModifyPoisonValue(amount);
		}
	}
}
