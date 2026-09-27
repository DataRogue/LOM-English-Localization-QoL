using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Mortal.Core;
using UnityEngine;

namespace LOM_UI_EN
{
	/// <summary>
	/// English-specific game behaviour. The game only knows Traditional Chinese, Simplified Chinese and Korean; every English key
	/// and source line of this mod is built on Traditional Chinese (language index 0), so English is "index 0 with this mod's
	/// text". When the player picks another language on the title screen the text layers step aside (Active is false) instead of
	/// mixing English table text with Korean or Simplified Lean data.
	///   HorizontalLayouts  SystemSettings.IsChineseLanguage returns false, so the game takes its own horizontal (non-Chinese)
	///                      branches: game-over screen, ending CG captions, the army-battle defeat screen.
	///   StoryLineSpacing   the line spacing of the story dialogs by line length, for English text (replaces the old
	///                      PretendKorean switch, whose only live effect was the Korean ladder on StoryText).
	///   EnglishNumerals    GameStatUtils.ChineseNumber (home level, duel realm) returns digits.
	/// </summary>
	public static class EnglishLanguage
	{
		public const string DefaultLineSpacing = "130:1.1;*:0.9";

		public static ConfigEntry<bool> CfgHorizontalLayouts;
		public static ConfigEntry<string> CfgStoryLineSpacing;
		public static ConfigEntry<bool> CfgEnglishNumerals;

		private static string _ladderSource;
		private static List<KeyValuePair<int, float>> _ladder = new List<KeyValuePair<int, float>>();

		public static string LadderError { get; private set; }

		private static Func<int> _language;
		private static bool _languageResolved;

		/// <summary>
		/// The game runs in its Traditional Chinese slot, the one this mod's English replaces. Reads SystemSettings.Language (the
		/// int, so it never recurses through the patched IsChineseLanguage getter) through a delegate resolved once, so a game
		/// update that renames it cannot break the text hooks that ask: the check then assumes the English slot and is flagged.
		/// </summary>
		public static bool Active
		{
			get
			{
				try
				{
					Func<int> lang = _language;
					if (!_languageResolved)
					{
						// Marked first: if resolving throws (the game type gone), it is not retried on every text lookup.
						_languageResolved = true;
						lang = ResolveLanguage();
					}
					return lang == null || lang() == 0;
				}
				catch (Exception)
				{
					return true;
				}
			}
		}

		/// <summary>Like Active, but false when the language cannot be read (for the harness's srcdump, which must be sure).</summary>
		public static bool ActiveKnown
		{
			get
			{
				try
				{
					Func<int> lang = _languageResolved ? _language : ResolveLanguage();
					return lang != null && lang() == 0;
				}
				catch (Exception)
				{
					return false;
				}
			}
		}

		private static Func<int> ResolveLanguage()
		{
			_languageResolved = true;
			_language = null;
			if (F.Language.SimulateInstall)
			{
				return null;
			}
			try
			{
				_language = ResolveLanguageCore();
			}
			catch (Exception)
			{
				_language = null;
			}
			return _language;
		}

		// The game type is named only here, so a missing SystemSettings fails inside ResolveLanguage's try.
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private static Func<int> ResolveLanguageCore()
		{
			MethodInfo getter = AccessTools.PropertyGetter(typeof(SystemSettings), "Language");
			if (getter != null && getter.IsStatic && getter.ReturnType == typeof(int))
			{
				return (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), getter);
			}
			return null;
		}

		public static void Bind(ConfigFile cfg)
		{
			// First run after 0.3: carry the two [General] switches of the Korean-era LanguagePatches over to [Language].
			Dictionary<ConfigDefinition, string> orphans = Orphans(cfg);
			ConfigDefinition oldHorizontal = new ConfigDefinition("General", "ForceHorizontalLanguageBranches");
			ConfigDefinition oldKorean = new ConfigDefinition("General", "PretendKorean");
			ConfigDefinition newHorizontal = new ConfigDefinition("Language", "HorizontalLayouts");
			ConfigDefinition newSpacing = new ConfigDefinition("Language", "StoryLineSpacing");
			string oldH = null;
			string oldK = null;
			bool migrate = false;
			if (orphans != null)
			{
				orphans.TryGetValue(oldHorizontal, out oldH);
				orphans.TryGetValue(oldKorean, out oldK);
				migrate = (oldH != null || oldK != null) && !orphans.ContainsKey(newHorizontal) && !orphans.ContainsKey(newSpacing);
			}
			CfgHorizontalLayouts = cfg.Bind("Language", "HorizontalLayouts", true, "Use the game's own horizontal (non-Chinese) layouts for the game-over screen, ending CG captions and the army-battle defeat screen, which the game draws vertically for Chinese. Only while the game language is the one this mod translates (Traditional Chinese slot) and the UI fixes are on.");
			CfgStoryLineSpacing = cfg.Bind("Language", "StoryLineSpacing", DefaultLineSpacing, "Line spacing of the story dialogs by line length, as length:spacing steps separated by ';' (a line of at most 'length' characters gets 'spacing'; * = any length). The default keeps short lines at the game's 1.1 and tightens longer ones to 0.9 so they fit the box. Empty = the game's 1.1 for every line.");
			CfgEnglishNumerals = cfg.Bind("Language", "EnglishNumerals", true, "Show levels the game writes as Chinese numerals (home level, duel realm) as digits.");
			if (migrate)
			{
				try
				{
					bool b;
					if (oldH != null && bool.TryParse(oldH.Trim(), out b))
					{
						CfgHorizontalLayouts.Value = b;
					}
					if (oldK != null && bool.TryParse(oldK.Trim(), out b))
					{
						CfgStoryLineSpacing.Value = b ? DefaultLineSpacing : "";
					}
					orphans.Remove(oldHorizontal);
					orphans.Remove(oldKorean);
					cfg.Save();
					Plugin.Log.LogInfo($"config: moved [General] ForceHorizontalLanguageBranches={oldH ?? "-"} / PretendKorean={oldK ?? "-"} to [Language] HorizontalLayouts={CfgHorizontalLayouts.Value} / StoryLineSpacing='{CfgStoryLineSpacing.Value}'");
				}
				catch (Exception ex)
				{
					Plugin.Log.LogWarning("config migration of the language switches failed: " + ex.Message);
				}
			}
			else if (orphans != null && (oldH != null || oldK != null))
			{
				// Already migrated earlier; only the stale lines are left.
				orphans.Remove(oldHorizontal);
				orphans.Remove(oldKorean);
				try
				{
					cfg.Save();
				}
				catch (Exception)
				{
				}
			}
		}

		internal static Dictionary<ConfigDefinition, string> Orphans(ConfigFile cfg)
		{
			try
			{
				PropertyInfo p = typeof(ConfigFile).GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				return p?.GetValue(cfg, null) as Dictionary<ConfigDefinition, string>;
			}
			catch (Exception)
			{
				return null;
			}
		}

		public static void Patch(Harmony h)
		{
			Compat.Setup(F.Language, delegate
			{
				Compat.Require(F.Language, "Mortal.Core", "Mortal.Core.SystemSettings", "Language");
				if (!F.Language.SimulateInstall && ResolveLanguage() == null)
				{
					F.Language.Problems.Add("SystemSettings.Language is not a static int any more");
					F.Language.EssentialMissing = true;
				}
			});
			Compat.Setup(F.Horizontal, delegate
			{
				Compat.Hook(F.Horizontal, h, AccessTools.PropertyGetter(typeof(SystemSettings), "IsChineseLanguage"), "SystemSettings.IsChineseLanguage", new HarmonyMethod(typeof(EnglishLanguage), nameof(IsChinese_Prefix)), essential: true);
			});
			Compat.Setup(F.LineSpacing, delegate
			{
				Compat.Hook(F.LineSpacing, h, AccessTools.Method(typeof(TextLineSpacingPanel), "GetFinalLineSpacing", new Type[1] { typeof(string) }), "TextLineSpacingPanel.GetFinalLineSpacing(string)", new HarmonyMethod(typeof(EnglishLanguage), nameof(GetFinalLineSpacing_Prefix)), essential: true);
			});
			Compat.Setup(F.LanguageCaption, delegate
			{
				Compat.Hook(F.LanguageCaption, h, AccessTools.Method(typeof(SystemSettings), "SetLanguage", new Type[1] { typeof(int) }), "SystemSettings.SetLanguage(int)", null, new HarmonyMethod(typeof(EnglishLanguage), nameof(SetLanguage_Postfix)), essential: true);
			});
			Compat.Setup(F.Numerals, delegate
			{
				Compat.Hook(F.Numerals, h, AccessTools.Method(typeof(GameStatUtils), "ChineseNumber", new Type[1] { typeof(int) }), "GameStatUtils.ChineseNumber(int)", null, new HarmonyMethod(typeof(EnglishLanguage), nameof(ChineseNumber_Postfix)), essential: true);
			});
		}

		private static bool IsChinese_Prefix(ref bool __result)
		{
			if (!F.Horizontal.Live)
			{
				return true;
			}
			try
			{
				F.Horizontal.Probe();
				if (!Plugin.CfgEnabled.Value || !CfgHorizontalLayouts.Value || !Active)
				{
					return true;
				}
				__result = false;
				return false;
			}
			catch (Exception ex)
			{
				F.Horizontal.Fail(ex, "IsChineseLanguage");
				return true;
			}
		}

		// __instance as Component: the patch method must not name game types beyond the target's own, so a game change cannot
		// make this class unloadable for the other hooks. Game parameters by position (__0): a renamed one still binds.
		private static bool GetFinalLineSpacing_Prefix(Component __instance, string __0, ref float __result)
		{
			string text = __0;
			if (!F.LineSpacing.Live)
			{
				return true;
			}
			try
			{
				F.LineSpacing.Probe();
				if (!Plugin.CfgEnabled.Value || !Active || __instance == null || __instance.name != "StoryText")
				{
					return true;
				}
				List<KeyValuePair<int, float>> ladder = Ladder();
				if (ladder.Count == 0)
				{
					return true;
				}
				int length = text?.Length ?? 0;
				foreach (KeyValuePair<int, float> step in ladder)
				{
					if (length <= step.Key)
					{
						__result = step.Value;
						return false;
					}
				}
				return true;
			}
			catch (Exception ex)
			{
				F.LineSpacing.Fail(ex, "GetFinalLineSpacing");
				return true;
			}
		}

		/// <summary>
		/// Dropdown.value shows the new entry's caption before its onValueChanged reaches SetLanguage, i.e. while the old language
		/// is still set, so the caption rule (englishOnly) would judge by the old one. Show the caption again once the language
		/// has changed.
		/// </summary>
		private static void SetLanguage_Postfix()
		{
			if (!F.LanguageCaption.Live)
			{
				return;
			}
			try
			{
				F.LanguageCaption.Probe();
				foreach (UnityEngine.UI.Dropdown d in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.UI.Dropdown>())
				{
					if (d != null && d.gameObject.scene.IsValid() && d.transform.parent != null && d.transform.parent.name == "Language")
					{
						d.RefreshShownValue();
					}
				}
			}
			catch (Exception ex)
			{
				F.LanguageCaption.Fail(ex, "SetLanguage");
			}
		}

		private static void ChineseNumber_Postfix(int __0, ref string __result)
		{
			int number = __0;
			if (!F.Numerals.Live)
			{
				return;
			}
			try
			{
				F.Numerals.Probe();
				if (CfgEnglishNumerals.Value && Active)
				{
					__result = number.ToString(CultureInfo.InvariantCulture);
				}
			}
			catch (Exception ex)
			{
				F.Numerals.Fail(ex, "ChineseNumber");
			}
		}

		/// <summary>Parses [Language] StoryLineSpacing ("130:1.1;*:0.9") once per change; an invalid string counts as empty and is reported.</summary>
		public static List<KeyValuePair<int, float>> Ladder()
		{
			string source = CfgStoryLineSpacing.Value ?? "";
			if (string.Equals(source, _ladderSource, StringComparison.Ordinal))
			{
				return _ladder;
			}
			_ladderSource = source;
			List<KeyValuePair<int, float>> list = new List<KeyValuePair<int, float>>();
			LadderError = null;
			foreach (string part in source.Split(new char[1] { ';' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string[] kv = part.Split(':');
				int max;
				float spacing;
				string k = (kv.Length == 2) ? kv[0].Trim() : null;
				if (k == null || !float.TryParse(kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out spacing) || spacing <= 0f || spacing > 5f)
				{
					LadderError = "bad step '" + part.Trim() + "'";
					list.Clear();
					break;
				}
				if (k == "*")
				{
					max = int.MaxValue;
				}
				else if (!int.TryParse(k, NumberStyles.Integer, CultureInfo.InvariantCulture, out max) || max < 0)
				{
					LadderError = "bad length '" + k + "'";
					list.Clear();
					break;
				}
				list.Add(new KeyValuePair<int, float>(max, spacing));
			}
			if (LadderError != null)
			{
				Plugin.Log.LogWarning("[Language] StoryLineSpacing: " + LadderError + "; using the game's line spacing");
			}
			list.Sort((KeyValuePair<int, float> a, KeyValuePair<int, float> b) => a.Key.CompareTo(b.Key));
			_ladder = list;
			return _ladder;
		}
	}
}
