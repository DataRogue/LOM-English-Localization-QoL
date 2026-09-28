using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using BepInEx.Configuration;
using HarmonyLib;

namespace LOM_UI_EN
{
	/// <summary>
	/// Keeps this mod's text and layout in charge next to the plugins of the English patch's newer releases (2026.09.28 on,
	/// BepInEx 5), which reach text this mod also serves:
	///  - Its own FanslationStudio.LegendOfMortal.Plugin writes its English into Lean's shared LeanTranslation.Data for every static
	///    label (a GetTranslation postfix). While this mod's table serves, that postfix is skipped for the keys the table has: the
	///    labels keep the game's Chinese for this mod's scene lines, wording fixes and table fallback, and TextTable.GameSource keeps
	///    reading the game's text (TextTable also snapshots that text at every Lean rebuild, for what the postfix changed before).
	///  - Its FanslationStudio.Plugins pack:
	///    - PrefabTextReplacer swaps prefab texts by their exact Chinese and DynamicStringPatcher the Chinese literals in the game's
	///      code. The entries this mod's scene lines cover are dropped as they load, so this mod's line shows there; the rest
	///      (placeholder texts, the duel history log, battle shouts, effect tooltips) stay the patch's, as its table fills the rows
	///      this mod lacks.
	///    - TextResizer applies the patch's resizers (among them a global 80% font size) on every text change. This mod's layout
	///      rules size its own text, so it is held back unless [Translation] LetBaseModResizeText.
	/// Every guard patches the other plugin's own method, found by name (as PerfPatches does with Binarizer), so a change on their
	/// side costs only that guard. The pack's decisions are taken when it loads (its first frame): switching the text layers later
	/// keeps them until a restart.
	/// </summary>
	public static class BaseGuards
	{
		public const string ResizerType = "FanslationStudio.Plugins.TextResizer.TextResizerService";
		public const string PrefabType = "FanslationStudio.Plugins.PrefabText.PrefabTextReplacerService";
		public const string CodeType = "FanslationStudio.Plugins.DynamicStrings.StringPatcherService";
		/// <summary>The pack's plugin GUIDs start with this (its assembly is FanslationStudio.Plugins, its services live in the
		/// embedded FanslationStudio.Plugins.Shared).</summary>
		public const string PackGuid = "FanslationStudio.Plugins";

		public static ConfigEntry<bool> CfgLetResize;

		public static bool LabelsGuarded { get; private set; }
		public static bool SnapshotHooked { get; private set; }
		public static bool PrefabGuarded { get; private set; }
		public static bool CodeGuarded { get; private set; }
		public static bool ResizerGuarded { get; private set; }

		public static long LabelsHeld;
		public static long ResizesHeld;

		/// <summary>The patch plugin's labels are held back right now: hooked, the feature live, this mod's table serving.</summary>
		public static bool LabelsHolding => LabelsGuarded && F.BasePlugins.Live && TextTable.Serving;

		/// <summary>The pack's resizer is held back right now: hooked, the feature live, not let through, this mod on.</summary>
		public static bool ResizerHolding => ResizerGuarded && F.BasePlugins.Live && CfgLetResize != null && !CfgLetResize.Value && Plugin.CfgEnabled.Value;
		/// <summary>Prefab texts and code strings the pack loaded, and how many of them this mod's scene lines took over.</summary>
		public static int PrefabLoaded;
		public static int PrefabTaken;
		public static int CodeLoaded;
		public static int CodeTaken;

		private static Harmony _h;
		private static FieldInfo _replacements;
		private static PropertyInfo _raw;
		private static readonly HashSet<string> _installed = new HashSet<string>(StringComparer.Ordinal);

		public static void Bind(ConfigFile cfg)
		{
			CfgLetResize = cfg.Bind("Translation", "LetBaseModResizeText", false, "With a newer " + Plugin.BaseModName + " (its FanslationStudio.Plugins pack): let its text resizer change text sizes too (its BepInEx/resizers, among them a global 80% font size). Off = this mod's layout rules size the text, as with every other version of the patch. Turning it off again undoes nothing it already resized until a restart.");
		}

		/// <summary>Plugin.Awake: the plugins found by then (soft dependencies load them first); the rest when every plugin has loaded.</summary>
		public static void Install(Harmony h)
		{
			_h = h;
			TryInstall(late: false);
			Loader.WhenAllLoaded(delegate
			{
				Compat.Setup(F.BasePlugins, delegate
				{
					TryInstall(late: true);
				});
			});
		}

		private static void TryInstall(bool late)
		{
			bool any = false;
			Type labels = LabelsType();
			if (labels != null || OriginalMod.LlmKitPluginLoaded())
			{
				any = true;
				// Whatever its label hook is called in a later build, the plugin may write its English into Lean's data: keep the
				// game's text from every rebuild while it runs.
				Once("snapshot", delegate
				{
					SnapshotHooked = TextTable.HookSnapshot(F.BasePlugins, _h);
				});
				if (labels != null)
				{
					Once("labels", delegate
					{
						LabelsGuarded = Compat.Hook(F.BasePlugins, _h, AccessTools.Method(labels, "GetTranslation_Postfix"), labels.Name + ".GetTranslation_Postfix (" + OriginalMod.LlmKitAssembly + ")", new HarmonyMethod(typeof(BaseGuards), nameof(Labels_Prefix))
						{
							priority = Priority.First
						});
					});
				}
				else if (late)
				{
					// Its plugin runs but its label hook is not where this mod looks: its labels may show over this mod's text.
					Once("labels not found", delegate
					{
						F.BasePlugins.Problems.Add(OriginalMod.LlmKitInjection + ".GetTranslation_Postfix of " + OriginalMod.LlmKitAssembly + " not found, so its labels may show over this mod's text");
					});
				}
			}
			Type resizer = FindType(ResizerType);
			if (resizer != null)
			{
				any = true;
				Once("resizer", delegate
				{
					HarmonyMethod hold = new HarmonyMethod(typeof(BaseGuards), nameof(Resize_Prefix))
					{
						priority = Priority.First
					};
					bool a = Compat.Hook(F.BasePlugins, _h, AccessTools.Method(resizer, "ApplyResizing"), "TextResizerService.ApplyResizing", hold);
					bool b = Compat.Hook(F.BasePlugins, _h, AccessTools.Method(resizer, "ApplyResizingToLegacyText"), "TextResizerService.ApplyResizingToLegacyText", hold);
					ResizerGuarded = a && b;
				});
			}
			Type prefab = FindType(PrefabType);
			if (prefab != null)
			{
				any = true;
				Once("prefab", delegate
				{
					_replacements = AccessTools.Field(prefab, "_replacements");
					if (_replacements == null || _replacements.FieldType != typeof(Dictionary<string, string>))
					{
						F.BasePlugins.Problems.Add("PrefabTextReplacerService._replacements is not a Dictionary<string,string> any more");
						return;
					}
					PrefabGuarded = Compat.Hook(F.BasePlugins, _h, AccessTools.Method(prefab, "LoadReplacements"), "PrefabTextReplacerService.LoadReplacements", null, new HarmonyMethod(typeof(BaseGuards), nameof(PrefabLoaded_Postfix)));
				});
			}
			Type code = FindType(CodeType);
			if (code != null)
			{
				any = true;
				Once("code", delegate
				{
					CodeGuarded = Compat.Hook(F.BasePlugins, _h, AccessTools.Method(code, "GroupedDynamicStringContracts"), "StringPatcherService.GroupedDynamicStringContracts", new HarmonyMethod(typeof(BaseGuards), nameof(Code_Prefix))
					{
						priority = Priority.First
					});
				});
			}
			if (late && OriginalMod.Current.Detached)
			{
				// [Dev] DetachBaseMod removed the pack's patches before it applies them (on its first frame): keep them out.
				foreach (string name in new string[3] { ResizerType, PrefabType, CodeType })
				{
					Type t = FindType(name);
					if (t != null)
					{
						Once("detach " + t.Name, delegate
						{
							Compat.Hook(F.BasePlugins, _h, AccessTools.Method(t, "EnsurePatched"), t.Name + ".EnsurePatched (detached)", new HarmonyMethod(typeof(BaseGuards), nameof(Skip_Prefix))
							{
								priority = Priority.First
							});
						});
					}
				}
			}
			if (late && !any && _installed.Count == 0)
			{
				F.BasePlugins.NotNeededWhy = "the newer plugins of " + Plugin.BaseModName + " are not running";
			}
			if (any && !late)
			{
				Plugin.Log.LogInfo("newer plugins of the base patch: " + Describe());
			}
		}

		private static void Once(string what, Action a)
		{
			if (_installed.Add(what))
			{
				Compat.Part(F.BasePlugins, what, a);
			}
		}

		/// <summary>The type holding the patch plugin's GetTranslation postfix (by its GetString prefix, else by name).</summary>
		private static Type LabelsType()
		{
			foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
			{
				try
				{
					if (a.GetName().Name != OriginalMod.LlmKitAssembly)
					{
						continue;
					}
					foreach (Type t in a.GetTypes())
					{
						if (t.Name == OriginalMod.LlmKitInjection && AccessTools.Method(t, "GetTranslation_Postfix") != null)
						{
							return t;
						}
					}
				}
				catch (Exception)
				{
				}
			}
			return null;
		}

		/// <summary>A type by full name from whichever loaded assembly holds it (the pack embeds its shared assembly).</summary>
		internal static Type FindType(string fullName)
		{
			foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
			{
				try
				{
					string n = a.GetName().Name;
					if (n == null || !n.StartsWith(PackGuid, StringComparison.Ordinal))
					{
						continue;
					}
					Type t = a.GetType(fullName, throwOnError: false);
					if (t != null)
					{
						return t;
					}
				}
				catch (Exception)
				{
				}
			}
			return null;
		}

		// ---- the guards ------------------------------------------------------------------------------------------------------

		/// <summary>Skips the patch plugin's GetTranslation postfix (its first argument is the key) for a key this mod's table serves.</summary>
		private static bool Labels_Prefix(string __0)
		{
			if (!F.BasePlugins.Live)
			{
				return true;
			}
			try
			{
				F.BasePlugins.Probe();
				if (__0 != null && TextTable.Serving)
				{
					TextTable.Table t = TextTable.Active;
					if (t != null && t.Map.ContainsKey(__0))
					{
						Interlocked.Increment(ref LabelsHeld);
						return false;
					}
				}
			}
			catch (Exception ex)
			{
				F.BasePlugins.Fail(ex, "labels");
			}
			return true;
		}

		private static bool Skip_Prefix()
		{
			return false;
		}

		/// <summary>Holds the pack's text resizer back while this mod's layout rules are on.</summary>
		private static bool Resize_Prefix()
		{
			if (!F.BasePlugins.Live || CfgLetResize == null || CfgLetResize.Value || !Plugin.CfgEnabled.Value)
			{
				return true;
			}
			try
			{
				F.BasePlugins.Probe();
			}
			catch (Exception ex)
			{
				F.BasePlugins.Fail(ex, "text sizes");
				return true;
			}
			Interlocked.Increment(ref ResizesHeld);
			return false;
		}

		/// <summary>After each prefab text file loads: the texts this mod's scene lines cover are left to this mod.</summary>
		private static void PrefabLoaded_Postfix(object __instance)
		{
			if (!F.BasePlugins.Live)
			{
				return;
			}
			try
			{
				F.BasePlugins.Probe();
				Dictionary<string, string> map = _replacements?.GetValue(__instance) as Dictionary<string, string>;
				SceneDictionary d = ServedScene();
				if (map == null || d == null)
				{
					return;
				}
				int taken = FilterReplacements(map, d);
				PrefabTaken += taken;
				PrefabLoaded = map.Count + PrefabTaken;
			}
			catch (Exception ex)
			{
				F.BasePlugins.Fail(ex, "prefab text");
			}
		}

		/// <summary>Before the pack groups the code strings it will patch: the ones this mod's scene lines cover are left out.</summary>
		private static void Code_Prefix(object __0)
		{
			if (!F.BasePlugins.Live)
			{
				return;
			}
			try
			{
				F.BasePlugins.Probe();
				IList list = __0 as IList;
				SceneDictionary d = ServedScene();
				if (list == null || d == null)
				{
					return;
				}
				int before = list.Count;
				int taken = FilterContracts(list, d);
				CodeLoaded += before;
				CodeTaken += taken;
			}
			catch (Exception ex)
			{
				F.BasePlugins.Fail(ex, "code strings");
			}
		}

		/// <summary>This mod's scene dictionary while it serves the scene text (not in Off or when handed back).</summary>
		private static SceneDictionary ServedScene()
		{
			return SceneText.Serving ? SceneText.Active : null;
		}

		// ---- the filters (no Unity types: tested outside the game) ------------------------------------------------------------

		/// <summary>Takes the texts the dictionary translates out of a prefab replacement map (keys are the exact Chinese); returns how many.</summary>
		public static int FilterReplacements(Dictionary<string, string> map, SceneDictionary d)
		{
			List<string> drop = new List<string>();
			foreach (KeyValuePair<string, string> kv in map)
			{
				if (Covers(d, kv.Key))
				{
					drop.Add(kv.Key);
				}
			}
			foreach (string k in drop)
			{
				map.Remove(k);
			}
			return drop.Count;
		}

		/// <summary>Takes the code-string contracts (objects with a Raw string) whose literal the dictionary translates out of the list.</summary>
		public static int FilterContracts(IList list, SceneDictionary d)
		{
			int taken = 0;
			for (int i = list.Count - 1; i >= 0; i--)
			{
				object c = list[i];
				if (c == null)
				{
					continue;
				}
				if (_raw == null || _raw.DeclaringType != c.GetType())
				{
					_raw = c.GetType().GetProperty("Raw", BindingFlags.Public | BindingFlags.Instance);
				}
				string raw = _raw?.GetValue(c, null) as string;
				if (raw != null && CoversLiteral(d, raw))
				{
					list.RemoveAt(i);
					taken++;
				}
			}
			return taken;
		}

		/// <summary>This mod's scene layer would translate the text (SceneDictionary.Translate, as SceneText does for a drawn text).</summary>
		public static bool Covers(SceneDictionary d, string text)
		{
			string result;
			return !string.IsNullOrEmpty(text) && d.Translate(text, supportsRichText: true, out result) && result != null && result != text;
		}

		/// <summary>A code literal as the game draws it: the pack's dumper writes its commas as fullwidth ones and drops CRs.</summary>
		public static bool CoversLiteral(SceneDictionary d, string raw)
		{
			string lit = raw.Replace("\\n", "\n");
			return Covers(d, lit) || (lit.IndexOf('，') >= 0 && Covers(d, lit.Replace('，', ',')));
		}

		public static string Describe()
		{
			List<string> parts = new List<string>();
			if (LabelsGuarded)
			{
				parts.Add("its plugin's labels held back where this mod's table serves (" + LabelsHeld + " so far)" + (SnapshotHooked ? ", the game's text kept from each Lean rebuild" : ""));
			}
			if (ResizerGuarded)
			{
				parts.Add("text resizer " + ((CfgLetResize != null && CfgLetResize.Value) ? "allowed" : ("held back (" + ResizesHeld + " resizes)")));
			}
			if (PrefabGuarded)
			{
				parts.Add("prefab texts: " + PrefabTaken + " of " + PrefabLoaded + " this mod's");
			}
			if (CodeGuarded)
			{
				parts.Add("code strings: " + CodeTaken + " of " + CodeLoaded + " this mod's");
			}
			return (parts.Count > 0) ? string.Join("; ", parts) : "none found";
		}
	}
}
