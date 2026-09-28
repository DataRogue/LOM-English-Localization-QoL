# LOM_UI_EN source (recovered) - 1.1.0

LOM_UI_EN is the plugin of **LOM English Localization + QoL** (the public name since 1.0.0; the plugin keeps its name
and GUID `lom.ui.english`, so configs carry over).

The original 0.1.0 source was not on this machine, so `LOM_UI_EN.cs` is the ILSpy 9.1 decompilation of the shipped
`LOM_UI_EN.dll` (kept verbatim in `LOM_UI_EN.v010.decompiled.cs.bak`) plus the 0.2.0 changes applied by `patch_v020.py`.
Comments from the original source are lost; behaviour is identical except for the changes below.

Build: `powershell -ExecutionPolicy Bypass -File build.ps1 [-Game <folder>] [-OutDir <folder>]` (Roslyn csc from Visual
Studio 2022, else the newest installed; .NET Framework 4.7.1+ reference assemblies; game and BepInEx DLLs as references;
deterministic since 1.0.0). Output: `bin\LOM_UI_EN.dll`; copy it over `BepInEx\plugins\LOM_UI_EN\LOM_UI_EN.dll`.
Since 1.1.0 also `build.ps1 -BepInEx5` for `bin\LOM_UI_EN.BepInEx5.dll`, the same source for BepInEx 5 (see 1.1.0 below),
which goes next to it.
Release: also copy `PLAYER_README.txt` over `BepInEx\plugins\LOM_UI_EN\README.txt` (publish.py ships
`THIRD_PARTY_NOTICES.txt` and the translation only), then package with `tools/release.py`; the whole checklist is in
`docs/DEVELOPMENT.md`.
The 0.1.0 binary is kept at `BepInEx\backup_stock\LOM_UI_EN.dll.v0.1.0`.

## 0.2.0 changes

Scene load time (the BepInEx log showed `scene 'Story': rule pass ~2,300 ms (7,545 lookups, 145,425 regex tests)` and the
same again for the `Loading1` overlay, about 4.5 s of regex work per story transition):
- `Rules.Match` caches its result per full object path (`_matchCache`); the same 7,500 paths recur on every load, so repeat
  loads cost a dictionary lookup per object instead of ~19 regex tests. Cleared on rule reload (F11).
- Wildcard rules carry `Literals` (the longest wildcard-free run of each pattern); `Rule.Matches` skips the regex when the
  path does not even contain that literal. Cuts the first-pass regex tests by roughly an order of magnitude.
- `OnSceneLoaded` runs `RuleApplier.ApplyToScene(scene)` (the loaded scene's roots and children, inactive included) instead
  of walking every loaded scene, after the very first pass. `[Performance] FullScenePass = true` restores 0.1.0 behaviour.
- `PerfPatches`: the Binarizer string-table hook (`Mortal.HookMods.GetStringRedirect`) calls `Debug.Log` for every string
  lookup (219,225 lines in one session's Player.log, 23,775 during one combat load, each with a stack trace). A Harmony
  prefix on that method does the same dictionary lookup silently. `[Performance] QuietBinarizerStringLog`.
  `NoStackTraceForInfoLogs` also stops Unity capturing stack traces for plain Info logs.

Empty Dialog History (Log button):
- Root cause (Player.log): `ArgumentException: Mesh can not have more than 65000 vertices` thrown from
  `UnityEngine.UI.Text.UpdateGeometry` inside `Mortal.Story.StoryManager.Log`. The backlog is a single legacy `Text`
  holding `NarrativeLog.GetPrettyHistory()` for the whole chapter; English runs ~2x longer than Chinese, so past ~16,000
  glyphs Unity refuses the mesh and the Text draws nothing.
- `NarrativeLogGuard`: a prefix on `Fungus.NarrativeLog.GetPrettyHistory(bool, int)` lowers `maxEntry` so the shown history
  stays under `[NarrativeLog] GlyphBudget` visible characters (13,000 by default, divided by the Outline/Shadow vertex
  multiplier of the log Text). The oldest entries drop off; the recent ones always render.
- `LazyRebuild`: the game rebuilt the whole backlog string and forced two canvas updates on every spoken line even while
  the log was hidden. The prefix on `NarrativeLogMenu.UpdateNarrativeLogText` skips the rebuild while hidden and a prefix
  on `ToggleNarrativeLogView` performs it once when the log opens.

Verification after the next launch (BepInEx console / LogOutput.log):
- `LOM_UI_EN 0.2.0 loaded: ...`, `Binarizer string lookups: per-lookup Debug.Log suppressed`
- `scene 'Story': rule pass N ms (... lookups, ... cached, ... regex tests, ...)` with far fewer regex tests than 145,000
  on the first Story load and a few ms on later ones; `scene 'Loading1'` passes should report a handful of objects.
- Open the Log in a long chapter: it shows the most recent entries; with `VerboseLog = true` the log line
  `narrative log trimmed to the last N of M entries` confirms the guard fired.

## 0.2.1 changes

- `TextSpeedPatch`: a postfix on `Fungus.Writer.Start` multiplies the dialog reveal speed (`writingSpeed`, 60 cps in the
  game's Say dialogs) by `[Story] TextSpeedMultiplier` (default 2.0) and divides the punctuation pause by the same
  factor. English readers were waiting on a per-character crawl tuned for Chinese. Set the multiplier to 1 to restore
  the stock speed; each Writer is adjusted once (tracked by instance id).
- `[BepInDependency("binarizer.plugin.mortal", SoftDependency)]` so the Binarizer hook is in place before `PerfPatches`
  looks for `GetStringRedirect`.

Rules and strings added alongside (BepInEx\plugins\LOM_UI_EN):
- `rules\34_duel_splash_dialog.json`: `duel-splash-horizontal` turns the Combat scene's three 50x500 start-splash strips
  (System/CombatStartText) into 1000x160 horizontal TMP boxes, auto-sized 40-100, so the text no longer stacks one
  letter per line; `story-text-top-left` re-applies UpperLeft to the Character/Narrative/Think StoryText boxes on every
  activation, undoing LOM_UI_Plugin_KR's MiddleLeft so lines start at the top-left corner.
- `strings\10_system.csv`: System/CombatStartText = "Duel Start" (the 1v1 Combat scene is the duel; "Battle" is the
  army-scale Battle.unity encounter).
- `rules\52_army_battle_more.json`: ReadyPanel key-hint columns (best fit 16-25) and the off-screen thrower's shout
  bubble (LeftOutsideDialogue, best fit 22-50). The rest of Battle.unity was already covered by
  `51_army_battle.json` and `60_offline_panels.json`; all of it was written offline from UnityPy scene dumps and is
  still unverified in-game.

Verification after the next launch (set `[Dev] Harness = true` in BepInEx\config\lom.ui.english.cfg or press the
DumpKey F10 in each scene): Combat start splash reads "Duel Start" on one line; story text starts at the top-left;
dialog reveals at twice the stock speed; in an army battle check the pre-battle card, the Tang Clan cut-in (six words
top to bottom), the tilted ally/enemy skill banners, the shout bubble at the map edge and the Victory/Defeat stamp.

## 0.2.2 changes

- `RelationshipHover` + `RelationshipHoverTip`: in Status > Social the game shows a relationship only as a level digit
  (`RelationshipStat.Value / 10`) and a progress bar (`Value % 10`). A postfix on `SocialStatPanel.UpdateInfo` attaches a
  hover component to the Info box (parent of the Level text) and marks the level digit, star and progress bar as
  raycast targets (plus the Back plate, so the hover area is the whole box with no gaps to flicker in); pointer enter/exit
  bubble up from those graphics, and the tip (a 200x40 dark box above the top-right corner of the Info box, using the
  level text's font) shows `Affinity 73 / 100`. Visibility is a CanvasGroup alpha, not SetActive, so the plugin's own
  Text/Image OnEnable hooks run once at creation rather than on every hover; the objects are named AffinityTip /
  AffinityTipText so no path rule touches them. The label comes from
  `PlayerStat/relationship` through the string table, so it follows the localization. Config `[Social]
  RelationshipHover` (on/off) and `RelationshipHoverFormat` (`{0}` value, `{1}` max, `{2}` label).
- Backup of the previous build: `BepInEx\backup_stock\LOM_UI_EN.dll.v0.2.1`.

## 0.2.3 changes

- `QuickSave.cs`: quick save / quick load hotkeys (`[QuickSave] SaveKey` F5, `LoadKey` F9, `Enabled`, `Toast`,
  `SavedText`, `LoadingText`). Both act on the game's current manual slot (`SaveSystem.CurrentSlot`: the slot the run
  was started or last loaded from, and the one the in-game Save button writes to via `SaveSystem.SaveGameData()`).
  Quick load repeats `RecentSaveSlotPanel.OnTitleClick` (SetSlot, StopMusic, LoadGameData, UpdateCheckMissions,
  LoadCurrentScene). The hotkeys are gated by the same state the game's own Save / Load menu items depend on, read
  through Harmony `Traverse` from the scene managers:
  - Story: `StoryManager` menu reachable (`EnableAction`, no log/status/inner panel) and `MenuPanel._saveButton`
    interactable (the Lua `ToggleSaveButton` lock, i.e. the "Manual Save Unlocked/Locked" toasts); Load needs
    `_loadButton`.
  - Free (daily life): `FreeManager` not processing, no status/save panel, hotkeys enabled; Save always, Load if the
    menu has a Load button; with the menu open only Load.
  - Battle: `GameLevelManager` ready, not game over, level allows pausing; Load only (the pause menu has no Save),
    also while paused if no inner panel is up.
  - Combat (duel): `CombatManager.EnableHotKey` and status panel closed; Load only.
  - Title: Load only, while no panel is open.
  - Anything else (Break, GameOver, End, the Loading1 overlay, a load already in flight) is refused.
  A refusal shows the reason as a toast ("Manual save is locked here", "No manual save during a battle", "Close the
  open panel first", ...). The toast is a DontDestroyOnLoad overlay canvas (`QuickSaveToast`) that borrows the font of
  a live game Text and fades on unscaled time so it also works while the game is paused.
- References added to `build.ps1`: Mortal.Story, Mortal.Free, Mortal.Battle, Unity.InputSystem. `SceneController` is
  aliased to `Mortal.Core.SceneController` (Mortal.Battle has one too).
- Backup of the previous build: `BepInEx\backup_stock\LOM_UI_EN.dll.v0.2.2`.

Verification after the next launch: BepInEx log shows `LOM_UI_EN 0.2.3 loaded`; F5 in a story scene after a
"Manual Save Unlocked" toast writes `Save_<slot>.dat` (log `quick save: slot 00N (Story)` and the game's own
`[SaveSystem]Save success`), F5 before the unlock shows "Manual save is locked here"; F9 anywhere the menu offers Load
reloads that slot (`quick load: slot 00N from Story -> Story`). Written offline against the decompiled game code; not
yet exercised in-game.

## 0.3.0 changes

In-game **Mod Settings** window for everything the mod does (`ModMenu.cs`, `ModMenuPages.cs`, `ModMenuEntry.cs`,
`TranslationProfiles.cs`).

- Entry points (`ModMenuEntry`): a "Mod Settings" button cloned from the menu's own Settings button, inserted right after it,
  on the title screen (`TitleManager._systemSettingButton`) and in the pause menus of the story, the daily-life map, duels
  (`MenuPanel._systemSettingButton`) and army battles (`PausePanel._systemSettingButton`). The clone is made under an inactive
  holder and stripped of InputButton, DevelopmentOnly, Lean localization components and copied UIFixMarkers before it wakes;
  its inspector onClick is replaced. On first show it copies the Settings label's final text settings (after XUnity's resizer,
  the font map and the rules ran) and tightens the column's VerticalLayoutGroup spacing until the column clears the bottom of
  the screen. Plus `[ModMenu] OpenKey` (F8) anywhere; `TitleButton` / `PauseMenuButton` turn the buttons off.
- Window (`ModMenu`): own DontDestroyOnLoad overlay canvas (sorting 29000, under the quick-save toast), built from code, objects
  named `MM_*` so no rule matches them, OS font Palatino Linotype (fallbacks Book Antiqua, Georgia, Times New Roman, the game's
  SourceHanSerifTC-Bold). While open: `Time.timeScale = 0` (restored if we set it) and every enabled Input System action is
  disabled. The UI module's point / left click / scroll wheel are the shared `UI/Point`, `UI/Click`, `UI/ScrollWheel` of the
  game's action asset, and CombatManager, FreeManager, GameOverPanel, CgPanel and EndGamePanel subscribe to `UI/Click` themselves
  (next line, next result, load next scene), so the module is pointed at three private actions (a runtime InputActionAsset
  "MM_UIActions"; InputActionReference refuses actions outside an asset) for as long as the window is up. The module
  reference-counts the actions it enabled and disables the shared ones itself on the swap; the swap back happens while ours are
  still enabled, which is what makes it re-enable the shared ones (plus an explicit re-enable of whatever was on before). The
  keyboard device is disabled too (scene code polls `Keyboard.current` directly); gamepads stay on. The window reads its own
  keys through the legacy Input manager: Esc (on key up), F8 or pad B closes it; Esc inside a text field only ends the edit.
  The F5/F9/F11/F12 hotkeys are ignored while it is open. The cursor state is put back only if the control scheme did not change.
- Pages: Translation, Story & Reading, Interface, Quick Save, Menu & Keys, Performance, Developer, About. Every entry of
  lom.ui.english.cfg and lom.strings.english.cfg appears once; controls write the ConfigEntry (BepInEx saves the file) and run
  the code the entry drives, so changes are live. Only `TmpLatinFallbackFont` still needs a restart (marked in the window).
  "Defaults for this page" and About > "Reset everything" (two clicks) restore defaults.
- Live switching refactors: NarrativeLogGuard, the Binarizer quiet-lookup prefix, RelationshipHover and TextSpeedPatch are now
  always installed and check their setting per call; TextSpeedPatch keeps each Writer's own speed so the multiplier can change
  (`ApplyAll`); `PerfPatches.ApplyStackTraceSetting` restores Unity's original Info stack-trace mode when turned off;
  `Plugin.Reapply()` restores every marker and re-applies rules / fonts / images when the layout switches change;
  `Plugin.SyncHarness()` adds the dev harness when it is switched on.
- `UIFixMarker.Restore` now puts back text colour, TMP colour, image sprite / colour / enabled only when a rule (or the sprite
  map) actually set them (`Touched*` flags), and a sprite only while one of ours is still showing, so switching the layout off
  live does not replay a colour or sprite captured mid-fade onto a screen that has moved on. Restored rects of sliding panels
  (the duel's corner buttons) sit at the position captured when the rule first applied, so those reappear the next time the
  game slides them in.
- Harness: `mouseclick PATH` / `mouseclick X Y` queues Input System mouse events (move, press, release) so a click goes through
  the EventSystem input module and every mouse-bound game action, unlike `click`, which calls the handlers directly. It sets
  `InputSystem.settings.backgroundBehavior = IgnoreFocus` so it also works while the game window is in the background.
- AccessTools.TypeByName is avoided for Binarizer / XUnity types (`TranslationProfiles.FindType`): its full-assembly scan hit
  NAudio's ReflectionTypeLoadException and HarmonyX logged it as a warning each time.
- Translation profiles (`TranslationProfiles`, config `[Translation] DataTableText` / `SceneText` = Revised | Original):
  - Data table: Binarizer's `HookMods.mapString` is refilled in place from `plugins/LOM_UI_EN/profiles/original/StringTable.csv`
    (or back from the mod folders), same parser (Ideafixxxer CsvParser from Fungus.dll) and first-file-wins rule as
    `HookMods.AddStringTable`, then `LeanLocalization.UpdateTranslations()`. About 0.3-0.5 s for 72,576 rows.
  - Scene text: XUnity's main file stays ours (the edit tools keep working on it). In Original mode a postfix on
    `TextTranslationCache.GetTranslationFiles` (global cache only) appends `profiles/original/xunity_translations.txt`, which
    loads after the main file so its lines win; base-patch keys we never had keep our text. The switch sets
    `AutoTranslationPlugin._translationReloadRequest` (XUnity's own Alt+R path, which re-translates what is on screen); a
    prefix skips `PruneMainTranslationFile` for our reloads so the main file is never rewritten. At startup an Original
    profile is applied by one such reload (XUnity loads its files before our Awake).
  - The Translation page's preset sets both layers plus LOM_Strings_EN (`Original` = base text with the wording fixes off).
  - Profile files: `StringTable.csv` is byte-identical to the base patch release's table (dated 2026-02-06, md5
    c9e1292a4a8f3e8b27d8bb01ba9563d4); `xunity_translations.txt` is the release's XUnity file plus 113 lines XUnity's Google
    fallback appended, no edits (both from LOM_Localization/backups/*.20260912-2304.bak). The revised table differs on
    16,440 values, the revised XUnity file on 12,916 lines.
- `[BepInDependency]` on XUnity (soft) so its types exist when the hooks are installed.
- Backup of the previous build: `BepInEx\backup_stock\LOM_UI_EN.dll.v0.2.3`.

Verified in-game on 2026-09-22 with the harness (screenshots in that session's scratchpad harness_test_run/): title button and
window, all eight pages, Revised <-> Original both ways (table 287-349 ms, XUnity reload a few seconds; title labels change),
status line after a reload, steppers and page defaults written to the cfg, Layout fixes / Translated images off and on in a duel,
the Mod Settings button in the daily-life map and duel pause menus (column fitted on screen), timeScale restored (1 on the map,
2 in the duel), and with `mouseclick`: real clicks work inside the window, do not reach the duel's `UI/Click` while it is open,
and the game's mouse works again after it closes (control click advances the duel). Not exercised: Esc / F8 / key capture and
typing (the game window was not in the foreground; they use the legacy Input manager like F5/F9/F10), the story and army-battle
pause menus (same MenuPanel prefab as the map / PausePanel with the same injection path).

Log lines (0.3.0): `LOM_UI_EN 0.3.0 loaded`, `mod menu button added: /UI/Layer_1/Buttons/ModSettings` on the title,
`.../MenuButton_ModSettings` once a story/map/duel/battle scene loads, `mod menu opened` / `closed`,
`string table: Original (72576 entries ...)` and `scene text: XUnity translations loaded as Original` after switching.
Launching Mortal.exe directly makes the game quit (SteamAPI.RestartAppIfNecessary); for a test run with arguments put a
`steam_appid.txt` containing 1859910 next to Mortal.exe and delete it afterwards.

## 0.4.0 changes

The English stack runs on its own. The original OverLlm English patch (Binarizer, XUnity AutoTranslator + ResourceRedirector,
LOM_UI_Plugin_KR, Mods/English/StringTable.csv) is optional: when it is installed it is only detected, and in Revised mode this
mod's text replaces it. Korean-release behaviour is replaced by English-specific settings.

Text layers (both read `plugins/LOM_UI_EN/translation/`; the base patch's files are never written):
- `TextTable.cs` (game data text): a prefix on `LeanLocalizationResolver.GetString(string)` at Priority.Low answers from
  `translation/StringTable.csv` with Binarizer's semantics (Ideafixxxer CsvParser from Fungus.dll, first row wins, raw value, a
  miss falls through to the game's Lean data). Rows with more than two fields are re-joined, the 14 LeanPhrase dumps
  (TextFont, Image_*, TextMeshFont_*) skipped, lint logged. While it serves, the PerfPatches prefix skips Binarizer's
  `GetStringRedirect` body. The LOM_Strings_EN Lean postfixes fall back to the table value for a Lean label still showing
  Chinese (`TableFallback`, e.g. Position/Name/Forge).
- `SceneDictionary.cs` + `SceneText.cs` (scene text): prefixes on `Text.text`, `TMP_Text.text`, `TMP_Text.SetText(string,bool)`
  and the OnEnable of Text / TextMeshProUGUI / TextMeshPro translate Chinese source text before it is assigned, with XUnity
  5.3.1's file format and lookup rules (line decoding, trimmed key variants, whitespace re-attach, known-value check, rich-text
  fragments, `r:` regex). Offline parity against a Python port of XUnity: 72,156 keys and 30,018 probes, 0 mismatches.
  `SceneResizeTable` ports XUnity's `*.resizer.txt` handling (path trie, per-slot merge, AutoResize / font size / line spacing /
  overflow / TMP commands, the implicit wide-text overflow and Masking->Truncate), applied only to text this layer translated,
  with per-component original sizes so a switch never resizes twice (restored on hand-back). Each component's Chinese source is
  recorded (and the text this layer wrote, so re-enabling a panel keeps the record) for live switching; XUnity's nested write
  in hand-back mode does not erase it (setter depth counter + finalizer). English with full-width punctuation longer than the
  longest non-Han key takes the non-CJK fast path (the Fungus typewriter). Chinese still on screen 1.5 s after it appeared is
  appended to `translation/untranslated.txt` (scene, path, escaped text); placeholders that the game overwrites are not.
- `XUnityBridge`: registers an `ITranslator.OnTranslating` callback (reflection only) that tells a running XUnity to leave a
  component alone while this mod serves scene text. `[Translation] LetBaseModFillGaps` (off) lets XUnity fill real gaps; this
  mod's own values and keys never go to XUnity's machine translation.
- `TranslationProfiles.cs` (rewritten): per layer `Revised`, `OriginalHandBack` (the base patch runs with its own text),
  `OriginalBuiltIn` (profiles/original), `BaseOnly`, `Off`. The scene lines load on a worker thread in parallel with the table
  (about 0.5 s together). Switching re-sets recorded components and calls `LeanLocalization.UpdateTranslations`; text the game
  sets from code updates when it is redrawn (e.g. the duel's Ultimate card name). The XUnity reload hooks of 0.3 are gone.
- `OriginalMod.cs`: detection by what runs (chainloader entry + Harmony patch info + Binarizer's mapString + XUnity's
  `ITranslator.TryTranslate`), and data state from 24 + 24 sentinel lines in `translation/MANIFEST.json` (Pristine / OurEdits /
  Other). Original hands a layer back only when that part runs with its own (non-revised) text. `[Compat] SuppressKrPlugin`
  unpatches LOM_UI_Plugin_KR; `[Dev] DetachBaseMod` unpatches the whole base patch and disables its plugins (test only).
  HarmonyX cannot unpatch native Unity methods (GameObject.SetActive, set_active, TextMesh.text), so those patch methods are made
  inert with a skip prefix instead.

English instead of Korean (`EnglishLanguage.cs`, `[Language]`):
- `EnglishLanguage.Active` = the game language is index 0 (the Traditional Chinese slot every English key is built on). With
  another language the text layers, the wording fixes and the numerals step aside instead of mixing English into Korean or
  Simplified Chinese.
- `HorizontalLayouts` (was `[General] ForceHorizontalLanguageBranches`): `IsChineseLanguage` returns false, so the game uses its
  horizontal layouts (game-over, ending captions, army-battle defeat).
- `StoryLineSpacing` = `130:1.1;*:0.9` (was `PretendKorean`, whose only live effect was the Korean ladder on StoryText): a prefix
  on `TextLineSpacingPanel.GetFinalLineSpacing` for StoryText. `IsKoreanLanguage` is no longer patched.
- `EnglishNumerals`: `GameStatUtils.ChineseNumber` returns digits (home level "2", duel realm).
- The old keys are migrated on first run through `ConfigFile.OrphanedEntries` (reflection) and removed from the file.
- The KR plugin's layout effects are rules now: `status-home-ledger-plate-left` / `status-home-ledger-left` (10_status_shell),
  `endgame-desc-fit` (60_offline_panels), the Intro fitter in `charintro-intro` (14_status_social), `rect.y 6.9` on
  `story-text-top-left` and `story-continue-arrow` (34_duel_splash_dialog). Rules gained `englishOnly`; the language caption rule
  uses it, the index-0 list item reads "English", and the caption is refreshed after a language change.

Mod Settings: Translation page shows the base patch's state, both layers' engine and counts, "Let the base patch fill gaps",
"Note untranslated text"; Interface has Horizontal text layouts / Story line spacing / English numerals; Performance has
"Remove the KR plugin's hooks"; Developer has Reload translation files, resizer switches, skip paths and Detach; About lists
each base-patch component. Harness commands: `cjkscan [tag]`, `detect`, `textstat`, `profile table|scene|both revised|original`,
`reloadtext`, `patches [owner]`.

Data and tools (`LOM_Localization/tools/`): `migrate_standalone.py` (`plan`, `copy` = M1 backup + M2 copy into translation/ with
MANIFEST sentinels, `restore-base` = M4, `revert`), `lom_paths.py` (all paths; `assert_writable` allows only translation/), and
the maintained edit scripts copied out of the session scratchpads (see tools/README.md). Two {punch} story rows the base patch
had half-lost were fixed by hand in translation/StringTable.csv.

Verified in game on 2026-09-22 (harness dumps compared object by object with a 0.3.0 baseline taken the same evening, route:
title, settings, library + ending, load panel, daily-life map, six Status tabs, a story scene with seven lines, a duel):
- C1 base patch present, Revised: text and layout identical to the baseline except EnglishNumerals ("Two" -> "2") and the ending
  description's nominal font size (22 -> 25; best fit sizes it). No Chinese on screen except a hidden placeholder the baseline
  shows too. Scene text costs 22 ms over a whole duel session (65k text sets).
- C5 base patch plugins moved out (true standalone): identical to C1 except Binarizer's ", mods: English" version tag.
- C3 DetachBaseMod: identical to C1 (the Home ledger included, with the KR hook inert).
- Live switching Revised <-> Original (built-in copy and, after M4, hand-back to Binarizer/XUnity): no Chinese, round trip
  restores everything the game redraws. Game language Korean: the layers are idle and Korean shows unmixed; back to English: all
  English again.
- M4 ran afterwards: Mods/English/StringTable.csv is the release file again and XUnity's file the original plus its own 8
  runtime lines (backup LOM_Localization/backups/restore-20260922-221859, undo with `migrate_standalone.py revert <dir>`);
  Revised text was still identical to the baseline.
- Backup of the previous build: `BepInEx\backup_stock\LOM_UI_EN.dll.v0.3.0`.

## 0.4.1 changes: built-in compatibility layer

Goal: after a game update, as much of the mod as possible keeps working without a new release, whatever breaks switches
itself off and says so, and players can fall back to the original OverLlm patch for text while keeping this mod's UI.

- `Compat.cs`: a `Feature` per game-facing feature (18, listed in `F`), each with the hooks and game members it needs.
  - `Compat.Hook` installs one hook and records a missing target or a failed patch instead of throwing.
  - `Compat.Require` probes a field, property or method a feature reads by name (Traverse). It can mark the whole feature
    or one named part (quick save per scene, Mod Settings buttons per menu).
  - `Compat.Setup` contains a feature's setup; `Compat.Tick` its per-frame work.
  - Health: Working / Partial / NotWorking / TurnedOff / NotNeeded.
- Every hook body: `if (!F.X.Live) return;` then `try { F.X.Probe(); ... } catch (ex) { F.X.Fail(ex, where) }`.
  - Hooks whose bodies name game types keep them in a `[NoInlining]` body method, so a changed game member fails at JIT
    time inside the guard, not in the game's call.
  - `__instance` is typed `Component` where the declaring type is a game class.
  - `Fail` trips the feature at once for structural errors (MissingMemberException, TypeLoadException), otherwise after
    `BurstLimit` errors in 30 s (20; 100 for layout) or 10x that in all.
- `Plugin.Awake` installs each feature separately (one failure no longer stops the rest). LOM_Strings_EN now loads after
  LOM_UI_EN. The summary is logged and `compat_report.txt` written once every plugin has loaded (chainloader Finished).
- Text fallbacks:
  - `TextTable.Serving` / `SceneText.Serving` include `F.*.Live`. A broken layer steps aside, and the original patch's
    Binarizer / XUnity (if installed) serve on their own.
  - `[Translation] FillGapsFromBaseMod` (on) fills keys and lines missing from this mod's data from the original patch:
    its Binarizer table through the PerfPatches prefix, and its XUnity dictionary through `ITranslator.TryTranslate` (no
    machine translation).
  - Keys nobody translated are logged to `translation/untranslated.txt` as `[game data text]` lines.
  - `TextSource.Off` and the "UI only" preset leave all text to the original patch.
- Layout fixes that are not working also stop the scene-load rule pass and Reapply, so a screen is either fixed or left as
  the game draws it. One malformed rule no longer drops its whole file. The report lists rules not matched yet.
- `EnglishLanguage.Active` reads `SystemSettings.Language` through a delegate resolved once. A rename then means
  "assume English" and is flagged, instead of breaking every text hook.
- Game fingerprint: module version ids of the game assemblies, compared with `compat_verified.json`.
  - Mod Settings > Compatibility > "Mark this game version as checked" writes it.
  - After a game update the page says so.
- Mod Settings:
  - A Compatibility page lists every feature, with Try again, Write report, Mark as checked and Notices.
  - Items whose feature is not working show a warning line under their help.
  - Translation page: a 3-way preset (Revised / Original / UI only), Off per layer, "Use the original patch for missing
    lines".
  - Developer page: `[Dev] SimulateGameChange` (ids or `all`, restart) and `SimulateHookErrors` (live).
  - Notices (a toast, 8 s) at the first scene when something does not work, and when a feature trips.
- Harness: `cfg Section/Key=value` (prefix `strings:` for lom.strings.english.cfg), `compat`, `patches [owner]`.
- `plugins/LOM_UI_EN/README.txt`: a player-facing explanation.

## 0.4.2 changes: compatibility hardening (review round)

An adversarial review of 0.4.1's compatibility layer found places where one game change could still take down more than
its own feature, or leave a screen half-fixed. Fixed:

- Isolation below the feature level:
  - `Compat.Require(f, assembly, typeName, member, part:)` looks the type up by name, so the calling method names no game
    type. Quick save, the menu buttons, the language check and the story log use it.
  - `Compat.Part(f, what, action, part:)` runs one hook's setup in its own lambda. A game type that no longer loads costs
    that hook only. Used for the three wording hooks, the Lean TMP font hook and the two story-log parts.
  - Members are found with `Compat.HasMember` (every base type, no AccessTools warnings, no AmbiguousMatchException).
  - Hook bodies that named game members moved to `[NoInlining]` bodies inside the guard: MenuPanel.OnPanelOpen, the Lean
    postfixes (TranslationName), the Mod Settings click sound (SoundManager), OriginalMod's resolver lookup and
    EnglishLanguage's SystemSettings lookup.
  - `__instance` is typed Component, and parameters bind by position (`__0`).
  - The layout rules find MovePanel by name, and the affinity tip holds its data as object.
  - Menu buttons scan title, pause and battle in separate methods, and a structural failure breaks that part only
    (`Feature.BreakPart`). Quick save's per-scene gates do the same.
- Health: hooks and checks are counted separately (`HooksWanted/HooksInstalled`, `ChecksWanted/ChecksPassed`). A
  feature whose declared parts are all broken is Not working. Problems or errors make it Partial. Rarely-run features
  (hovers, quick save, menu buttons, numerals, caption, text speed) turn off after 10 errors in all.
- A failed `Harmony.Patch` is rolled back (`Unpatch`), so it cannot poison later patches of the same method.
- Turning off undoes and Try again redoes (`Feature.OnTurnedOff` / `OnTurnedOn`, deferred to Plugin.Update):
  - layout: every marker is restored, then Reapply;
  - scene text: with XUnity running, resizes are undone and the Chinese handed back for XUnity, then Retranslate;
  - text speed: the game's writer speeds are restored;
  - affinity tip: tips are hidden.
  - Try again also refreshes the Lean labels. Verified in game: after a trip and Try again the Load screen matches the
    pre-test dump exactly.
- Rule engine: `RuleApplier.Process` and the scene/all passes count errors against layout (`F.Layout.Fail`), per object,
  and stop when layout is off.
- Quick save: probe by type name, Fail on errors, no quick save after a failed load until the next scene, MenuPanel
  checks for map and duel.
- StringTable.csv is read by `IdeaCsv`, a copy of Ideafixxxer.CsvParser. A parity test against the Fungus.dll parser
  gave identical output on all three tables (72,576 rows each) and on edge cases.
- The game fingerprint hashes the game's DLL files on disk (the loaded-assembly MVIDs depended on load timing). It now
  includes LeanLocalization.TMP, DOTween and Assembly-CSharp. `compat_verified.json` ships marked for
  release_1.0.5000.13, build fc01f3b44033.
- Finish also runs on the first scene if the chainloader event is not reachable. Notices name at most 3 features.
- The untranslated log flushes even when the scene layer is off. `Setter_Finalizer` returns void.
- Mod Settings texts: the "UI only" toast checks that the patch is running, the Base patch status says when the detector
  could not check, the gap-filling items mention a failed XUnity bridge and the patch's saved machine translations, and
  a failed layer switch reports its error.

## 0.5.0 changes: ships only this mod's own work

Owner's ruling (2026-09-23): "Don't include the original patch or works as part of our patch, instead simply detect if the
other mod is installed." 0.4 shipped OverLlm's text: profiles/original held verbatim copies, the translation tables held
56,134 of 72,576 game-data rows and 39,800 of 52,747 scene lines unchanged from OverLlm, the resizers were copies, and the
MANIFEST held OverLlm wording. Now:

- The plugin folder ships only this mod's revisions.
  - translation/StringTable.csv holds the rows this mod changed or added.
  - translation/scene/*.txt holds the scene lines it changed or added. A line with an empty value removes that OverLlm line.
  - MANIFEST.json keeps only SHA-1 prefixes of the sentinel values (`OriginalMod.TextHash`).
  - profiles/ and translation/scene/resize/ are gone.
- Revised is an overlay of those revisions on the user's own OverLlm install, read in place and never written.
  - `TextTable.LoadOverlay(OriginalMod.BaseTablePath, ...)`: Mods/English/StringTable.csv, or a StringTable.csv in a
    Binarizer mod folder.
  - `SceneDictionary.LoadOverlay(OriginalMod.BaseScenePath, ...)`: BepInEx/Translation/en/Text/_AutoGeneratedTranslations.txt.
    Our values are put in place of the base lines as the file is read. The result is identical to one file with the base
    text edited in place: map, known-value set, non-CJK keys, regexes and 20,012 probe lookups (offline check
    scratchpad parity/OverlayParity.cs, compiled with SceneDictionary.cs).
  - Resizers: OverLlm's own `*.resizer.txt` in its folder, then ours in translation/scene/resize if we ever add any
    (`SceneResizeTable.Load` takes ';'-separated folders).
  - Without OverLlm, only the revised lines are English. Mod Settings says so, and a notice appears at the first scene.
- Original: handed back to OverLlm's running plugins as before. Otherwise it is read from its files
  (`LayerMode.OriginalFromFiles`, which replaces `OriginalBuiltIn`). Without OverLlm, Original is unavailable.
- `[Translation] FillGapsFromBaseMod` is removed. The overlay already contains OverLlm's rows. A running Binarizer or XUnity
  is asked only for a key neither file has.
- Harness: `tabledump NAME` / `scenedump NAME` write the served table/dictionary to BepInEx/cache/LOM_UI_EN/NAME.tsv; the whole harness (cmd.txt, out/) now lives in BepInEx/cache/LOM_UI_EN/harness, outside the plugin folder.
- THIRD_PARTY_NOTICES.txt (XUnity.AutoTranslator MIT for the SceneDictionary/SceneText port; ideafixxxer CsvParser MIT via
  Fungus for IdeaCsv.cs) ships in the plugin folder.
- Maintainer data: see LOM_Localization/tools/README.md (workspace, base_ref, publish.py).
- **Fallback mode** (`[Translation] FallBackToOriginal`, on by default; Mod Settings > Translation > "Fall back to the
  OverLlm patch").
  - Where a revised game-data row no longer fits the game, OverLlm's current line is served instead
    (`TextTable.Resolve`/`Decide`, decided once per key).
  - "No longer fits" means one of two things:
    - The game's own Chinese for the key has changed since the row was revised, and OverLlm's line has changed too. The
      game text is read from Lean's `LeanLocalization.GetTranslation(key).Data`, with no hook involved.
    - The row uses a `{0}`-style index or `{$var}` that the game's text no longer has.
  - What the rows were revised against is recorded in `translation/StringTable.meta.tsv`:
    `key<TAB>TextHash(game Chinese)<TAB>TextHash(OverLlm line)`. publish.py writes it from `base_ref/game_source.tsv`,
    which comes from the harness `srcdump`.
  - An OverLlm rewording alone never replaces a revision. A changed game text with no newer OverLlm line keeps the
    revision and is listed as out of date.
  - The compat report lists every fallback and every out-of-date row.
  - Scene text needs no record: its lines are matched by the game's Chinese, so a changed line naturally finds OverLlm's
    line for the new text.
  - Harness: `tget KEY`, `fallbacks`, and `fbtest KEY stale|newer|value TEXT` (an in-memory test).
- **Wording-fix marker `@table`** (`StringOverrides.TableMarker`).
  - An override whose text is exactly `@table` makes the key's Lean label show the served game-data text: OverLlm's
    installed row or our revision. The wording file then holds none of that text.
  - It replaces 4 overrides that copied OverLlm's wording only to keep Lean labels on the table's wording:
    - System/Menu/CommunityContent
    - CombatAction/weapon
    - CombatSkill/Name/S9202_1
    - Position/Name/Forge
  - For those keys the scene layer has a different line for the same Chinese ("player community", "Thrown Weapons",
    "Ape King's Whirlwind", "Blacksmith's Forge").
  - The cut-over first deleted them as "no-ops". The 0.5 in-game comparison caught the title link turning lowercase.

## 0.6.0 changes: plays in English without the OverLlm patch

Owner ruling 2026-09-24: the mod carries the whole translation, every line reviewed in context (the full review pass,
LOM_Localization/tools/fullpass), so it plays without OverLlm; a reviewed line ships even when it equals OverLlm's
(the review ledger, tools/ledger.py). The OverLlm patch is optional: installed, it fills anything this mod lacks and
serves the fallback and Original.
- MANIFEST.json `standalone` {table_rows, table_total, scene_lines, scene_total} (publish.py, with a ledger): what ships
  against the full working copy. OriginalMod.LoadManifest reads it (ManifestTableRows ... ; -1 without it).
- TranslationProfiles.OwnTableComplete / OwnSceneComplete / OwnComplete: shipped >= CompleteShare (98%) of the total and
  what loaded is at least 0.9 of what shipped; before a layer loads the manifest's word stands, a load that was tried and
  failed counts as nothing.
- With a complete layer and no OverLlm text for it: no startup notice (Compat.BaseTextNotice), Mod Settings says "this
  mod's own translation ... (optional)" instead of the warnings (NoBaseWarning, BaseStatus, the Translation page intro),
  and the log/Describe say "optional: this mod's own translation is complete". A 0.5 manifest (no `standalone`) or a
  share under 98% keeps the old warnings.
- BaseSummary.Complete also needs the patch's scene file (it said "Installed and running" with the file missing).
- Wording: "revised rows/lines" became "rows/lines" where the mod's text now includes reviewed lines; the About page and
  THIRD_PARTY_NOTICES.txt credit OverLlm for the lines that kept its wording and the three resizer copies.
- PLAYER_README.txt is the plugin folder's README.txt (copied by hand at release).

## 0.7.0 changes: character names in dialogue show a portrait tip

Owner request 2026-09-26: in dialogue, the name of a character who has portrait art becomes a hover tooltip showing the
character and the player's affinity with them, where the game has one.
- `NameTips.cs`, Compat feature `nameTips` ("Character names in dialogue", group Story; parts `portraits`, `affinity`,
  `menus` can fail on their own). Settings `[Story] NameTooltips` (on), `NameTooltipsMetOnly` (on) and
  `NameTooltipColor` (`#E8C47C`; empty = no colour), all on Mod Settings > Story & Reading.
- Spoiler gate (`NameTooltipsMetOnly`): a character who has an affinity record is linked only while that record is
  Active. The game activates it only in its character introduction card (the Show coroutine of the intro panel), i.e. at
  the reveal, so a name mentioned earlier shows no face. This matters because several disguises reuse the real art:
  special1_1 "Youth in dark robes" = Liu E's normal.png, special999_1 "White-Clad Youth" = Rui Sheng's, the "???" and
  "Young woman" variants too. Characters without a record are always linked; the list leaves out the ones whose
  disguise shares their art (Guildmaster Jiang / "Merchant", Xin Ru, Crane Hand), Nangong Xiu (an assumed name with Duan
  Zhixiu's portrait), Di Ao (the same face as Tang Jiaojiao) and the Iron Crown Daoist's hidden identities (Patriarch
  Wuxiang, Wu Xiang; only his public name is linked).
- Which characters: `nametips.tsv` in the plugin folder (source `LOM_Localization/workspace/nametips.tsv`, shipped byte for
  byte by publish.py; maintained with `tools/nametips/nametips.py dump|crops|candidates|check`). Columns: id, aliases,
  portrait address override, crop. The file holds no translated text: the name matched is the served `Character/<id>`
  (and the title shown `CharacterTitle/<id>`), so the tips follow the table; aliases are extra forms such as a given name
  the text uses alone ("Xiaomei" 395 times against "Yu Xiaomei" 16). Matching is case-sensitive and whole-word, longest
  form first; a form two listed characters share is dropped (logged). Disguise and placeholder variants (Masked Man =
  brother1_1, Lady Thief, ...) are never listed: the tip would reveal the identity.
- Detection never changes the dialog text. A postfix on `UnityEngine.UI.Text.OnPopulateMesh` handles the story Text of
  every Fungus `SayDialog` (`StoryTextObject`; classified once per Text instance id). It strips rich-text tags (the
  Fungus Writer keeps the unrevealed rest of a line in the string inside a transparent `<color>` tag, so the tag-free text
  is the same for the whole reveal and is matched once per line), maps characters to glyph quads (the quad count decides
  once whether whitespace/tags draw quads in this Unity: logged as "text meshes draw ..."), recolours the name's quads
  (alpha kept, so unrevealed letters stay hidden; a darker shade on dark text) before Shadow/Outline copy them, and keeps
  one rectangle per line of each name. A name is hoverable once all its letters are revealed.
- Hover: `NameTips.Update` (Plugin.Update via Compat.Tick) reads the Input System mouse, tests the rectangles in the
  Text's local space, waits 0.12 s, and shows the tip unless Mod Settings is open, the story is paused
  (`StoryManager.IsStoryPause`: menu, log, status, shops) or the topmost UI raycast hit belongs to another root canvas.
  Nothing is added as a raycast target, so clicks and click-to-continue are untouched.
- The tip (`NameTipView`, own overlay canvas, order 30500, 1920x1080 reference) shows a head-and-shoulders crop of the
  character's "normal" stage portrait (Addressables `StoryCharacterData.PortraitResourceList`, via the game's patched
  `Addressables.LoadAssetAsync`, so Binarizer mod portraits apply; preloaded when a name appears; released on scene
  change), the name and title, and, when the character's `RelationshipStat` is Active (listed in Status > Social), the
  level (Value / 10), the progress bar the Social panel draws and the exact value. Characters without art show no frame.
  When the stage art cannot load, the affinity record's own avatar is used.
- Crop: 640 px of a 1700 px portrait from 40 px above the figure's top, centred on the head (`crop` column: the opaque
  columns of the 260 px band below the figure's top, measured offline).
- Harness: `say [character|narrative|center|think] TEXT` writes a line through a story dialog's Writer, `nametips` prints
  the state (names on screen with screen positions, mesh mode, cover), `namehover NAME|ID` points at a name through the
  Input System, `mousemove X Y|PATH` moves the pointer.
- build.ps1 references OBB.Framework, Unity.Addressables, Unity.ResourceManager and UnityEngine.AudioModule.
- Portrait addresses are the game's own `AddressKey`s: an address can differ from the asset's path in its bundle (the
  Special_833 folder was renamed 南宮秀 -> 段智秀, the address kept the old name), so tools look them up in the catalog's
  key table, never by bundle path: `nametips.py crops` resolves each address to its internal id through
  `aa/catalog.json` (m_KeyDataString / m_BucketDataString / m_EntryDataString) and measures that asset (special834).
- Verified in game 2026-09-26 with the owner's go-ahead (story save 002, harness `say`/`namehover`, screenshots in the
  session scratchpad harness_nametips_20260926): the mesh mode is "glyphs and other whitespace" (Unity 2020.3 draws no
  quad for spaces or rich-text tags), 0 mismatches over about 500 rebuilds; names found in the character, narrative and
  centre dialogs, including an alias (Moling) and a name wrapped onto the next line; tips with portrait, title and
  affinity (Tang Buyi: Level 2, 23 / 100) and without (Shi Ming, narrower); Xiaomei (not introduced in that save) left
  plain and linked with the gate off; a name under the open Rest menu gets no tip (covered); the setting off, the
  colour empty and SimulateHookErrors=nameTips (turned off at once, names plain, Try again restores) all behave; the real
  story runs unchanged. Fixed during the run: the affinity row's labels drew nothing (Truncate with a font whose line,
  about 1.44 em, is taller than the box; now Overflow), the hover colour was nearly white (now 20% lighter than the link
  colour), and a tip without affinity is as wide as its name.

## 0.8.0 changes: tips in choice menus, and faction tips

Owner request 2026-09-26: the character tips in choice menus too, and tips for faction and sect names with a brief,
spoiler-free description, "just so that you know which one is which".
- Choice menus: `NameTips.Classify` also takes a label inside a Button under any Fungus `MenuDialog` (options, talk,
  location and dice menus all derive from it). The game fills an option's visible labels (Normal / Hover / Disable
  copies, or the location card's Type / Title / Desc) from a hidden "Text (Fungus)" that holds the story key, so the
  visible labels are what carry names. A label under a RectMask2D / Mask (the talk menu's scrolling list) only shows a
  tip while the pointer is inside the mask.
- The dice menu's labels are TextMeshPro (`DiceOption` NormalText / HighText): `TMPro_EventManager.TEXT_CHANGED_EVENT`
  (raised after TMP uploads a mesh) runs `BuildTmp`, which matches on `textInfo.characterInfo` (TMP has already parsed the
  rich text), recolours `meshInfo[].colors32` and calls `UpdateVertexData(Colors32)`; rectangles come from each
  character's box, one per `lineNumber`. A TMP label is redrawn with `ForceMeshUpdate` (it only regenerates when asked).
  Compat part `tmp`.
- Factions: `factiontips.tsv` (workspace source, shipped byte for byte by publish.py like nametips.tsv): id, title,
  forms, description. Entries are `TipCharacter`s with `IsFaction` (no portrait, no affinity, never gated). Settings
  `[Story] FactionTooltips` (on) and `FactionTooltipColor` (`#8FC7B0`, jade, so the two kinds read apart); either switch
  alone keeps the feature on. The tip shows the title in the faction colour over a rule and the wrapped description
  (column 340, height from `preferredHeight`), with a border in the same hue.
- Harness: `menu [options|dice|talk] KEY~KEY2~...` shows a story choice menu with those options through the game's own
  option code (dice: KEY|Stat); `menu close` hides it.
- Tools: `nametips.py import-factions PROPOSAL.tsv [--live]` (numeric ids, title not repeated among the forms: publish.py's
  OverLlm scan reads each line as prose, and "kongtong_sect Kongtong Sect Kongtong Sect" is a run OverLlm has); `check`
  validates both files (forms two entries share, long descriptions, Chinese); publish.py REL_FACTIONTIPS, lom_paths
  WS_FACTIONTIPS. Compat feature renamed "Name tooltips (characters and factions)" (id still nameTips).
- The 26 factions (2026-09-26): a Sonnet draft from the glossary's sect_faction terms, then an independent Sonnet review
  of every claim against the text and its earliest chapter, then the owner-side review. Dropped as spoilers: gang
  rankings (the "three great gangs" change in ch6-7), Snow Mountain Sect, the Six Paths, Ye Family, Western Wulin
  Alliance; the bare form "White Shark" (it is the gang's cant, not the gang). Eight descriptions were reworded where
  publish.py's scan found 4-word runs only OverLlm's text has.
- Verified in game 2026-09-26 (owner's go-ahead; screenshots in the session scratchpad harness_factions_20260926): faction
  names jade next to gold character names in the dialog, tips with title and description; the options menu (real story
  keys through the game's own option code) links Tang Clan, Tang Buyi, Qingcheng and Emei, tips show over the buttons,
  and a hovered button (dark text on light) gets the darker shade; the dice menu's TextMeshPro labels are coloured and
  show tips; a dialog name under the open menu is covered (no tip); FactionTooltips off leaves the character names;
  SimulateHookErrors=nameTips turns everything plain (TextMeshPro too) and Try again restores it; 0 mismatches.

## 0.8.1 changes: Mod Settings pages ordered by distance from the original

Owner request 2026-09-26: order the window by "purity from the original": first compatibility with the original mod
and which translation to use, then this mod's own localization features such as the UI fixes, then purely additive
features that do not improve the localization, and so on.
- Pages: Translation, Localization, Extras, Performance, then a second group (a gap and a faint rule in the navigation
  column, `MenuPage.NewGroup`) with Compatibility, Developer and About. Story & Reading, Interface, Quick Save and
  Menu & Keys are gone. Every setting keeps its label and config key, so the .cfg files are unchanged; a setting goes on
  the first page that describes it.
- Translation: Base patch, Which translation, the fallback, Layers, and a new section "Alongside the OverLlm patch":
  Let the base patch fill gaps; Apply resizer files and XUnity's wide-text overflow (from Developer); Quiet base-patch
  lookups and Remove the KR plugin's hooks (from Performance).
- Localization: Layout (layout fixes, translated images, horizontal layouts, story line spacing, duel card slide, Latin
  fallback font), Wording (the wording fixes and punctuation switches from Translation, English numerals) and Dialogue
  (text speed, the log guard and its length). Text speed and the log guard count as localization: the game's reveal
  pace and the 16,000-glyph log limit only become problems because English runs about twice as long as Chinese.
- Extras: name tooltips, exact affinity on hover, quick save. Performance: rescan every scene, rebuild the log only when
  opened (from Story & Reading), no info stack traces, and the two logging switches.
- Developer gains Note untranslated text (from Translation); About gains the Mod Settings key and the title and pause
  menu buttons (from Menu & Keys).
- The wording fixes are still part of the Which translation presets but sit on another page now, so when they are the
  only difference from a preset the status line says so ("Custom mix: Revised with the wording fixes (Localization
  page) turned off.", `PresetStatus`) instead of blaming the layers.
- Reworded help: Which translation, Wording fixes (the preset sets it), Keep long logs visible and Duel card slide (why
  they are localization), Quick save and load (the old page intro).
- Compat features are grouped the same way (Translation, Localization, Extras, Mod Settings), so quietBinarizer moved to
  Translation; ids and names are unchanged.
- Checked offline: all 53 config entries still have exactly one control, before and after; the string literals differ
  only in the page titles, intros, section names and the texts above.

## 0.9.0 changes: poison and paralysis tiers on the duel gauges

Owner request 2026-09-26: make the status effect breakpoints in the duel more pronounced and visible, and give exact values
on hover.
- The breakpoints: each fighter has a poison and a paralysis gauge (`CombatStatUI` `_poisonProgress` / `_paralyzedProgress`,
  `CombatStatBar`, a 128x12 fill in a 136x20 frame under the name). `CombatActionController.ModifyPoisonValue` sets off
  Poisoned Lv1 (D101) at 50, Lv2 (D102) at 100 and Lv3 (D103) at the maximum, 150, which also empties the gauge; paralysis
  is the same with D201-D203. The frame art has one faint 1px notch at 100 and none at 50; the value label under each bar
  has font size 0, so the number never shows; the game's hover tip names only the next tier.
- `BuildupGauges.cs`, Compat feature `buildup` ("Poison and paralysis gauges", group Extras, part `tips`). Settings
  `[Duel] BuildupMarks` and `BuildupTips` (both on), Mod Settings > Extras > Duels.
- Marks: a layer with the fill's own rect (`LOM_BuildupMarks`, last sibling of the fill) holds a 2px mark with a dark 4px
  edge at each threshold / max, 10px taller than the fill so it crosses the frame. A mark lights up (3px, the gauge's hue:
  jade for poison, yellow for paralysis) while its tier or a higher one is in effect, by `ExistEffect` on the fighter's
  controller (`CombatManager.PlayerAction` / `EnemyAction`, matched by `.UI`), not by the fill: the game keeps a tier until
  the gauge empties or a higher tier replaces it, so Lv1 can be in effect with the fill below 50.
- Tip: a postfix on `CombatStatUI.UpdatePoisonTip` / `UpdateParalyzedTip` (the game calls them after every change and when
  the duel starts) keeps the game's text and writes ours with `CombatStatBar.SetTipText`: the build-up and maximum, the fall
  at the start of each round (`Mathf.Min(-resist, -1)` in `RestorePoisonValue`), and each tier with its threshold, state
  ("in effect", "in effect until the gauge empties", "27 more") and the tier effect's own name and description
  (`CombatSkill/Name|Desc/<key>`, the description's leading "Name: " dropped). English only; another game language keeps
  the game's tip. `Plugin.Update` looks again every 0.25 s, since an event can clear a tier effect without a build-up change.
- The tip box needs no placing: the game mirrors the enemy's tip Container (scale -1,-1, its Text flipped back), so the
  enemy's tips open down and to the left from the top of the screen and the player's upward (a first build flipped the
  enemy's pivot and pushed the tip off the top; seen in game and removed). The box (`back_tip_frame_1`) is a light,
  see-through plate under dark text, so the tip's own colours are dark: the tier in effect in bold dark green (poison) or
  dark amber (paralysis), the fall line and replaced tiers in grey. Turned off or `BuildupTips` off: the game's last text
  is put back.
- The thresholds and tier keys are read at startup from the coroutine's IL (`PatchProcessor.GetOriginalInstructions` on
  `AccessTools.EnumeratorMoveNext`: the constant stored into `<value1>` / `<value2>`, the string into `<level1..3>`), so a
  game update that changes them moves the marks, and one that renames them turns the feature off at startup. Log line:
  `duel gauges: poison tiers at 50/100/max (D101,D102,D103), paralysis at 50/100/max (D201,D202,D203)`. Checked offline
  with a console program against the shipped Mortal.Combat.dll.
- Harness: `invoke static LOM_UI_EN.BuildupGauges.TestAdd player|enemy poison|paralysis N` runs the game's own
  ModifyPoisonValue / ModifyParalyzedValue on a fighter; `invoke static LOM_UI_EN.BuildupGauges.Describe` prints each gauge.
- While our tip shows, the box's Image alpha goes from the game's 0.784 to 0.97 (seven lines over the fighter's name and the
  dialogue label were hard to read through it); the game's colour is put back with the game's tip.
- Verified in game 2026-09-26 with the owner's go-ahead (duel autosave, 1600x900, screenshots in session scratchpad
  shots_0_9_0): log line and "all 21 features working"; marks at 50/100 on all four gauges, idle beige, lit green / yellow
  while a tier is in effect; player poison 60 (Lv1) and enemy paralysis 110 (Lv2) tips on both sides, the enemy's opening
  downward; poison dropped to 30 keeps Lv1 and its mark ("in effect until the gauge empties"); paralysis to 150 fires Lv3,
  empties the gauge and unlights both marks; BuildupMarks off hides the marks, BuildupTips off gives the game's tip back;
  SimulateHookErrors=buildup turns the feature off (marks gone, game tips, notice) and Try again restores it. The Mod
  Settings tabs (0.8.1) were checked in the same run: the nav divider, Extras > Duels, Compatibility's groups.

## 0.9.1 changes: Mod Settings for players, not maintainers

Owner request 2026-09-27: less verbose, more utilitarian, for non-technical players.
- Every help text is one plain sentence with no file paths or plugin internals (Binarizer, XUnity, LOM_Strings_EN, rule
  files): 681 words over 66 texts, down from 1,374 over 63 in 0.8.0 (average 21.8 -> 10.3 words, longest 62 -> 22);
  page intros 170 -> 46 words. Status lines are short and only say what a player can act on (counts of rules, images
  and labels are gone).
- Tabs: Translation, Localization, Extras, then Compatibility, Advanced, About. The purity order stays; everything
  technical moved to Advanced (formerly Developer, now also Performance), in the same order: Translation (the two text
  layers, Let OverLlm fill gaps, OverLlm text sizes, Wide text overflow, Quiet OverLlm lookups, the KR plugin, Note
  untranslated text, Never translate, Reload, OverLlm patch details), Localization (Dialogue line spacing, Latin fallback
  font), Extras (the affinity tip and quick save/load message formats), Performance, Logs and testing (with Mark as
  checked and All features, the per-feature list with ids), Now. Translation keeps Which translation, the OverLlm patch
  state and Fall back to OverLlm.
- The options say "OverLlm" where they said "Original" (players read "Original" as the untranslated Chinese); the
  TextSource enum and the .cfg keep Original.
- Compatibility shows the game version, one status line listing only what is not working, Try again, Write a report and
  the notices switch (was every feature on its own row).
- Items carry their Compat feature (`MenuItem.Feature`) instead of a label-to-feature table, so renaming a label no
  longer loses its warning line.
- Feature names and fallbacks (notices, Compatibility, the report) are plain words too ("Working with the OverLlm patch",
  "Long dialogue logs", "Name tooltips", ...); ids are unchanged. The missing-OverLlm notices are one sentence.
- Window subtitle: "Legend of Mortal English · version 0.9.1".
- Lists get one bullet per item (owner: "use bullet points when it makes sense"): the Which translation options, the
  broken features under Compatibility > Status, Advanced > OverLlm patch details and All features (grouped), About > Keys,
  and the three tier lines of the duel gauge tip (`ModMenuPages.Bullet`; the tip's font, SourceHanSerifTC-Bold, has the
  glyph).

## 1.0.0 changes: first public release

Owner decisions 2026-09-27: public name "LOM English Localization + QoL", repository DataRogue/LOM-English-Localization-QoL
(MIT for the code, the translation text reusable with credit), a full package (BepInEx 6.0.0-be.692 bundled unmodified)
and a mod-only package.
- **Newtonsoft.Json ships in the plugin folder.** The plugin references Newtonsoft.Json 13.0.0.0 (fonts.json, rules/,
  spritemap.json and sprite sidecars, MANIFEST.json, compat_verified.json), the game has none, and until now the only copy was the one the OverLlm/KR package
  put in BepInEx/core: on a clean BepInEx install the plugin would not have loaded. The folder now holds Newtonsoft.Json
  13.0.2 as Unity builds it for AOT, `Runtime/AOT/Newtonsoft.Json.dll` of Unity's com.unity.nuget.newtonsoft-json 3.2.2
  (the same file is in 3.2.1, and it is byte for byte the copy OverLlm's package installs, so every earlier in-game test
  ran on it). It is pinned by SHA-256 in publish.py `PINNED_LIBS`, with its notices in THIRD_PARTY_NOTICES.txt (MIT, and
  the Unity Companion License for Unity's build). With OverLlm's copy in core it is never loaded: Mono finds core first
  through Doorstop's `dll_search_path_override`. Without it, BepInEx be.692's `UnityPreloaderRunner.LocalResolve` finds
  the plugin folder's copy.
- **Not the official NuGet build.** The first 1.0.0 candidate shipped `lib/net45` of the official package. The
  clean-install test (OverLlm, its core extras and the patched Addressables moved aside, the full zip extracted) showed
  every rule skipped and spritemap.json and fonts.json failing with "Operation is not supported on this platform". That
  build creates delegates with Reflection.Emit (DynamicMethod), which this game's stripped runtime does not have. The
  result was 0 rules and 0 sprite replacements, while Compatibility said "all 19 features working".
- **Verified in game 2026-09-27** with the owner's go-ahead. The scripted setup and teardown hash-checked every move,
  and restored the saves and the registry.
  - **A, clean install.** Lash's English Patch moved aside (overllm_aside.ps1), together with the 13 core files the
    official be.692 zip lacks (Newtonsoft.Json among them) and the patched Unity.Addressables.dll (the stock one went in).
    The plugin folder and the configs were moved aside and the -full candidate extracted.
    - 348 rules and 24 images loaded.
    - All 19 features worked, with 2 "not needed" (in the new wording).
    - The 11 standard screens and a Story scene showed 0 Chinese. The duel's only Chinese was the two hidden
      "一二三…" bubble placeholders.
    - Mod Settings: the Translation page ("Revised | Lash's | UI only", "Not installed. It's optional…", and "Can't
      switch: Lash's English Patch isn't installed." on the Lash's button), Advanced (the longest renamed labels
      wrap) and About (name, BepInEx version, "Updates and problems").
    - Name tips with portraits under the stock Addressables (`portrait=Ready` in the Story scene); in the Free scene a
      portrait waits for the story's character config, as designed. Faction tips worked in the talk menu.
    - The extracted plugin folder equals the -mod-only zip, 73 of 73 files.
  - **B, alongside Lash's English Patch.**
    - Detected as installed and running with pristine data; all 21 features worked.
    - The 11 screens showed 0 Chinese.
    - The Lash's option hands the text back to the patch's plugins (title "player community") and Revised restores it.
    - Marked as checked for 1.0.0.
    - `SimulateHookErrors=all` turned 11 features off while 10 kept working; Try again brought back all 21. A Status
      panel opened while the features were off kept its placeholder until reopened.
  - **C, `SimulateGameChange=all`.** 1 feature worked and 20 did not. The text fell back to Lash's English Patch (0
    Chinese on the title). Mod Settings opened, and Compatibility listed every feature in the new wording.
- `Plugin.CheckLayoutData`: the layout feature declares data parts `rules`, `images` and `fonts`. A part whose data could
  not be read at startup (`Rules.Unreadable` with no rule loaded, `SpriteStore.MapError`, `FontMap.LoadError`) is marked
  broken (`Compat.MarkBroken`, listed in Problems and logged). One broken part makes the feature partly working, all
  three make it not working. So a failure like the one above now shows up in Compatibility, the report and the startup
  notice.
- **Rebuilt before publishing (owner's call, 2026-09-27 evening).** The 1.0.0 draft had not been published, so its zips
  were rebuilt from the text as of the scene-line audit and the name rulings (8acc4ae to 52e72d3), and the v1.0.0 tag
  moved to that commit.
  - Only text changed; the plugin source, and so the DLL, is identical to the tested build. The in-game mark of
    2026-09-27 therefore still stands.
  - The first build's zips are kept in `release/dist/1.0.0-first-build-826f802/`.
- `Plugin.DisplayName` / `Plugin.ProjectUrl`: the Mod Settings subtitle reads "LOM English Localization + QoL · version
  1.0.0", About > Version names the mod, and About has "Updates and problems" (the project page and the report to attach).
  The on-screen notices start with the display name instead of "LOM_UI_EN:".
- **"Lash's English Patch"** (owner, 2026-09-27): everything a player reads calls the OverLlm patch by that name:
  - where: Mod Settings, the notices, the compat report, Compat feature names and fallbacks, `NotNeededWhy`, the `.cfg`
    descriptions, PLAYER_README and THIRD_PARTY_NOTICES;
  - how: `Plugin.BaseModName`, and `Plugin.BaseModShortName` "Lash's" on the three-way Which translation and layer buttons.
    Those buttons are 124 px with about 108 px of text at 22 px and no wrap, which the full name overflows; the help text
    under them spells it out;
  - unchanged: code identifiers, comments, log lines, the TextSource enum and cfg keys (`Original`).
  - `OriginalProblem` keeps the words "not installed", which `ShortProblem` keys on. The status lines now say what the
    player can act on ("Can't switch: Lash's English Patch isn't installed.", "Showing this mod's text: ...").
- build.ps1: `-Game` / `LOM_GAME`, compiler found through vswhere (VS 2022 first, the compiler of every tested build),
  reference assemblies 4.7.1 to 4.8.1, `-deterministic` and `-pathmap`, so a rebuild of the tagged source reproduces the
  released DLL byte for byte (tools/release.py checks it).
- PLAYER_README.txt: the new name, both packages, updating (delete the folder first: a file a newer version drops would
  otherwise stay and load), uninstalling, reporting a problem, Newtonsoft.Json.dll.
- Tools: publish.py check skips `RUNTIME_FILES` (translation/untranslated.txt, compat_report.txt: the game writes them and
  the missing-text log quotes whatever the screen showed, OverLlm's lines included; they never ship). New
  tools/release.py packages a release (gates: check passes, rebuild equals the installed DLL, README current, the
  in-game mark is this version's, every file accounted for). lom_paths.GAME honours `LOM_GAME`.

## 1.1.0 changes: the OverLlm patch's llmkit-upgrade and its release 2026.09.28

The OverLlm patch's branch `llmkit-upgrade` (joshfreitas1984/LegendOfMortalOverLlm; d94a13b of 2026-09-18, then ad14c3d)
became its release `EnglishPatch-2026.09.28.13.56`. What an install holds changes:
- `Mods/English/StringTable.csv` gives way to one CSV per game source file (44: `BattleSkill_zh-cn.csv` ...
  `Story_1.csv` ... `Work_zh-cn.csv`), written by LlmKit's `CsvGameDataWorkflow.PackageAsync`. The keys are the same key
  space (72,560 of the old table's 72,562; `Library/Award/30001` and `30019` are gone).
- Its own plugin `FanslationStudio.LegendOfMortal.Plugin` ("Legend of Mortal English Patch", GUID = assembly name,
  BepInEx 5) replaces Binarizer. `StringTableInjectionPatches` is a Normal-priority `GetString` prefix over a
  `Dictionary<string,string> _translations`, filled on the first lookup by `LoadTranslations`: every `*.csv` of
  `Mods/English` (`Directory.GetFiles` order), `\n` turned into a line break, a later row replacing an earlier one.
  - The release reads each file whole (`File.ReadAllText`, `CsvUtility.ParseFile`: a quoted value may span lines). The
    branch at d94a13b read line by line (`File.ReadAllLines`, `CsvUtility.ParseRow`) and cut each of the 3,353 values
    that span lines at its first break.
  - New in the release: `GetTranslation_Postfix`, a `LeanLocalization.GetTranslation` postfix. It writes the English
    into the returned `LeanTranslation.Data`, the object Lean shares with every static label of that key.
  - `StringTableDumpPatches` (a `LeanLanguageCSV.Compile` postfix) writes the ChineseSimplified sources to
    `BepInEx/plugins/raw/*.csv` on every compile.
- A plugin pack, `FanslationStudio.Plugins.dll` (its services in the Costura-embedded `FanslationStudio.Plugins.Shared`),
  holds five plugins with GUIDs `FanslationStudio.Plugins.<name>`, set in `FanslationStudio.Plugins.UIEditor.cfg`:
  - `PrefabTextReplacer`: an exact Chinese-to-English dictionary (`Mods/English/*prefabText*.yaml`, 111 texts) put on
    texts as assets load, on `OnEnable` and through the text setters;
  - `DynamicStringPatcher`: IL transpilers that swap Chinese literals in the game's code (`dynamicStrings.txt.yaml`,
    121 strings grouped by `GroupedDynamicStringContracts`; the log says "61 successful");
  - `TextResizer`: `BepInEx/resizers/*.yaml`, among them a global `/*` rule with `fontPercentage 0.8`, applied on
    `OnEnable` and every text change (`ApplyResizing`, `ApplyResizingToLegacyText`);
  - `UIEditor`, an in-game editor for its layouts, sprites and resizers (Alt with the mouse buttons, Alt+1,
    PageUp/PageDown and the brackets; this mod's keys are F5, F8 to F12), and `DebugPlugin`.

  The pack runs its per-frame work from `Canvas.willRenderCanvases`, since the game destroys BepInEx 5's manager
  object.
- The zip installs BepInEx 5.4.23.3 with Doorstop 4.4.0 (`winhttp.dll`, `doorstop_config.ini`) and a `BepInEx.cfg` with
  `HideManagerGameObject = false`. It ships no Binarizer, no XUnity AutoTranslator (the pack takes over prefab and code
  text) and no Newtonsoft.Json. Unpacked over an older install, it leaves that install's files in place: BepInEx 6's
  core files and plugins, which BepInEx 5 ignores, and the old `StringTable.csv`, which the new plugin reads as one
  more table.

What this version does about it:
- **BaseTables.cs** (no Unity or BepInEx types): `BaseTableSet.Find` tells the layouts apart. It picks per-file when
  `Mods/English` holds a CSV named like a game source file (`Story_N.csv`, `*_zh-cn|zh-tw|kr.csv`); then every other
  `*.csv` belongs to it, in the plugin's order. A `StringTable.csv` next to them is an older release's leftover, which
  this mod leaves out. Otherwise the single table is used, including one in a folder Binarizer loads.
  - `LlmKitCsv.ParseFile` is the release's `CsvUtility.ParseFile`, statement for statement. `LlmKitCsv.Read` reads a
    file as `File.ReadAllText` does (UTF-8, a byte order mark honoured), with shared read access. So the table read is
    the plugin's own, leftover aside.
  - Before the release, `LlmKitCsv` ported the branch's `ParseRow`. It joined a line that left a quote open with the
    following ones (up to 64 lines) to read whole what that plugin cut. The release made the difference moot.
- `TextTable.LoadBase` (either layout) and `LoadOverlay(BaseTableSet, ...)`. `OriginalMod.BaseTable` replaces
  `BaseTablePath`; detection refreshes it and logs a change on disk.
- **OriginalMod** detects the new plugin: `LlmKitInstalled/Loaded/Hooked` (a `GetString` prefix from its assembly or
  a `StringTableInjectionPatches` type), `LlmKitLabelsHooked` (its `GetTranslation` postfix), `LlmKitEntries` and
  `LlmKitData`. Its table is read through `_translations`, and the `Translations` getter loads it when no lookup has
  yet. `PackInstalled/Loaded/OtherBepInEx` do the same for the pack (`FanslationStudio.Plugins.dll`, a loaded GUID
  that starts with `FanslationStudio.Plugins`).
  - `HandBackTable` picks the plugin Original is left to: its own plugin for the per-file layout, Binarizer for the
    single file. Binarizer holding a leftover `StringTable.csv` next to the new tables does not count.
  - `TryGetHandBackLine` serves the wording fixes in hand-back mode (it was `PerfPatches.TryGetBase`, Binarizer only).
  - `Summary` counts only a plugin that serves the layout on disk (`TablePluginServesLayout`).
  - Per plugin, `*OtherBepInEx` says whether its DLL is built for the other BepInEx major, found by searching the
    metadata strings for `\0BepInEx.Core\0` and cached per file. Such a plugin is expected not to run, so XUnity's
    BepInEx 6 build on a BepInEx 5 install is not "partly installed".
  - `TableLeftover`: a `StringTable.csv` next to the per-file tables. The new plugin reads it too, after the `Story_N`
    tables and before `System_*`, and a later row wins. On the release that serves 16,236 keys from the old table.
    The Translation status and a diagnosis ask the player to delete it.
  - New diagnoses, and `DetachBaseMod` removes the new plugin's and the pack's patches too.
  - `SameText`/`BinarizerForm`: MANIFEST sentinel and revision-record hashes are taken from Binarizer's parse, which
    returns every line break inside a value as CRLF and drops empty lines. The per-file tables give LF and keep them,
    so the vote and the fallback's "newer line" test compare either form.
- **BaseGuards.cs** keeps this mod's text and layout in charge next to the release's plugins. Each guard patches the
  other plugin's own method, found by name (as `PerfPatches` does with Binarizer), so a change on their side costs only
  that guard. Together they are the Compat feature `basePlugins` ("Newer plugins of Lash's English Patch", Translation
  group; 22 features now), not needed when neither the new plugin nor the pack runs. They install at Awake for the
  plugins loaded by then (soft dependencies on all four GUIDs load those first), the rest once every plugin has
  loaded (`Loader.WhenAllLoaded`).
  - **Labels.** A `Priority.First` prefix on `GetTranslation_Postfix` skips it for the keys this mod's table serves.
    The shared `Data` then keeps the game's Chinese, which this mod's scene lines, wording fixes and table fallback
    work from. Without it, the patch's English would sit in the label before this mod's layers see the key's text.
  - **Prefab and code text.** A postfix on `PrefabTextReplacerService.LoadReplacements` and a prefix on
    `StringPatcherService.GroupedDynamicStringContracts` drop the entries this mod's scene lines cover as they load, so
    this mod's line shows there. `Covers` asks the scene dictionary as the scene pass would (`Translate(text, true,
    ...)`, plus a variant with ASCII commas for code literals, which the pack's dumper writes with fullwidth ones).
    The rest stays the patch's: placeholder texts, battle shouts, the duel history log, effect tooltips. On the release,
    63 of the 111 prefab texts are this mod's (63 of the 74 Traditional ones, none of the 37 Simplified), and 8 of the
    121 code strings. The pack decides on its first frame, so switching the text layers later keeps that until a
    restart.
  - **Resizer.** `ApplyResizing` and `ApplyResizingToLegacyText` are skipped unless `[Translation]
    LetBaseModResizeText` is on (Mod Settings > Advanced > "Resizer of Lash's English Patch") or the mod is off. This
    mod's layout rules size its own text, and the pack's global 80% would shrink every label on top of them (the title
    buttons went from 27 to 24 px with it on). Turning it off again takes a restart, as nothing undoes a resize.
  - With `[Dev] DetachBaseMod` the pack's three `EnsurePatched` methods are skipped.
  - Every guard body calls `F.BasePlugins.Probe()` inside its `try`, as the other features' hooks do, so `[Dev]
    SimulateHookErrors = basePlugins` (or `all`) turns the feature off at runtime and Try again brings it back.
  - `BaseGuards.Describe` (harness `invoke static`) prints the counts; About lists the pack with them. About's lines follow
    the live state (`LabelsHolding`, `ResizerHolding`). While the feature is off, the mod is off or Original is handed
    back, the labels line drops "its labels give way to this mod's text" and the resizer reads "on".
- **The game's text.** The labels guard keeps the patch's English out of the keys this mod serves. The keys it lacks,
  and every key while Original is handed back, still get that English written into Lean's data. So `TextTable` copies
  `LeanLocalization.CurrentTranslations` after every `RegisterAndBuild` (a postfix, while `basePlugins` is live), which
  rebuilds all of them before any label reads them. `GameSource` and `GameSources` read that copy first. The fallback's
  game-text test, `srcdump` and `tget`'s "game:" still see the game's Chinese.
  - The copy is hooked whenever the patch's own plugin is loaded (`OriginalMod.LlmKitPluginLoaded`), not only when its
    `GetTranslation_Postfix` is found by name. If a later build renames that postfix, the labels guard is missing and
    the feature is partly working ("... not found, so its labels may show over this mod's text"), but the fallback
    still judges rows by the game's text.
  - Without the copy, its English in Lean's data reads as a changed game text. With `FallBackToOriginal` on (the
    default), the wording fixes and table rows of those keys then stand down (`OverrideFits`, `Decide`), and the
    labels keep the patch's English. Seen with `[Dev] SimulateGameChange = basePlugins`, where every hook of the
    feature is missing: the title read "Departure | Environment settings | Scripture Hall | Retiring to the mountains",
    while `tget` still served "Set Out". A copy of the release's plugin with the postfix renamed (Mono.Cecil, its
    attributes kept, so its `PatchAll` still applies it) left every title and story text as this mod's.
- **Coexisting prefixes.** HarmonyX 2.10 runs every prefix and ANDs their results (`HarmonyManipulator.WritePrefixes`).
  So our Low-priority `GetString` prefix still writes `__result` after the patch plugin's Normal-priority one returned
  false, and a key we lack keeps the patch's line. `[BepInDependency]` on the new GUID and the pack's (soft) makes
  their hooks exist at our Awake.
- **BepInEx 5 build.** BepInEx 6 loads only assemblies that reference `BepInEx.Core` and whose plugin derives from
  `BepInEx.Unity.Mono.BaseUnityPlugin`. BepInEx 5 (`Chainloader.HasBepinPlugins`, 5.4.21) loads only those that
  reference `BepInEx`. So the new plugin never runs on be.692, and on the release's BepInEx 5 our be.692 build does not
  either: 1.0.0 stops loading once the release is installed. The game's `BepInEx/core` held a complete BepInEx 5.4.21
  from the OverLlm/KR package all along, so switching is a `doorstop_config.ini` change.
  - `build.ps1 -BepInEx5` defines `BIE5`, references `BepInEx.dll` 5.4 and writes `LOM_UI_EN.BepInEx5.dll`.
    `Loader.cs` wraps the two differences: the plugin list (`UnityChainloader.Instance.Plugins` / `Chainloader.PluginInfos`)
    and "after every plugin loaded" (be.692's `Finished` event; on 5, `Plugin.Start`, which Unity runs after every
    plugin's Awake of the chainloader pass).
  - Under `BIE5` the file also defines stand-ins for the explicit `BepInEx*LogInterpolatedStringHandler` blocks the
    decompiled source uses. BepInEx 5's `LogInfo(object)` logs their `ToString()`.
  - Both DLLs ship; each BepInEx loads only its own. publish.py accepts both as plugin DLLs, and release.py ships both,
    rebuilding and comparing each.
  - **The manager object.** be.692 creates `BepInEx_Manager` with `hideFlags = HideAndDontSave` unconditionally
    (`UnityChainloader`). BepInEx 5 does so only with `[Chainloader] HideManagerGameObject`, false by default and in
    the release's own `BepInEx.cfg`. This game destroys the visible object. The plugins' Awake had run, but no Update
    or coroutine ever did: the harness said "ready" and never read a batch, and F8, quick save, `Plugin.Update`'s
    deferred work and the Compat ticks would be dead too. With the setting on, all ran. So the BIE5 build's
    `Plugin.Awake` sets `HideAndDontSave` on its game object (the manager) when it is not set, which is be.692's
    state, and logs it.
  - `build.ps1 -CoreDir` references another BepInEx 6 core folder (for building while `BepInEx\core` holds BepInEx 5).
    `LOM_BEPINEX5` names the BepInEx 5 reference folder for release.py. The BIE5 DLL records the BepInEx.dll version it
    was built against (5.4.21), and loads on 5.4.23.5 and on the release's 5.4.23.3.
- **Mod Settings.** Translation's status names a leftover `StringTable.csv` ("Installed, but a file of its older
  version is still in Mods/English (StringTable.csv). Delete that file."). About lists the new plugin ("its labels give
  way to this mod's text") and the pack (prefab and code counts, the resizer's state). Advanced has the resizer switch.
- Offline checks (session scratchpad, not shipped):
  - The branch's `Files/Converted/*.yaml` packaged with a port of `PackageAsync` gave 72,598 rows and no raw fallbacks.
    Against the branch plugin's own `CsvUtility.cs` compiled in: 69,208 keys equal, and the 3,352 joined ones whole
    where that plugin cut them.
  - On the release's tables, `LlmKitCsv` against the release plugin's `CsvUtility` (decompiled, compiled in): the same
    72,560 keys and values, 3,254 of them over several lines.
  - This mod's 72,524 rows are served as they are over the old base and the new one, with 0 Chinese left. The MANIFEST
    vote on the new tables is Pristine (24/24): about 77% of the "retranslation" is the old text, carried over by the
    branch's `LegacyDataMigration`.
  - The old `StringTable.csv` left next to the release's tables makes its plugin serve 16,236 keys from it.
  - The filters on the release's pack files: 63 of 74 Traditional prefab texts and 0 of 37 Simplified ones, 8 of 121
    code strings (the battle shouts stay the patch's).
  - The detector's conclusions were checked for the current release, the new one on BepInEx 6 (with and without
    leftovers) and on BepInEx 5, a table holding our edits, and detach. So were the dialect's edge cases and the BepInEx
    version test on real DLLs (the patch's plugin compiled from the branch against BepInEx 5.4.21; ours; Binarizer). Each
    was run against both builds.
- Tools: `tools/lashtables.py` reads either layout the plugin's way (`parse_file`; `crlf=True` gives Binarizer's form,
  for comparing with BASE_REF). `publish.py check` compares with the installed per-file tables. `publish.py rebase`
  takes a per-file release, an unpacked zip included: its tables are stored in BASE_REF as one `StringTable.csv` with
  the same map. A release without `_AutoGeneratedTranslations.txt` keeps BASE_REF's own. BASE_REF still holds the
  2026-02-06 release. A dry run onto the release keeps all 72,524 rows:
  - 16,168 conflicts (the patch changed a row we revised; ours kept), 4,323 of them rows the review kept as the patch's
    line;
  - 294 rows now equal to the patch's, 2 rows taken over, 2 removed;
  - 33 wording overrides that no longer change anything;
  - no scene change.

  `ingame/overllm_aside.ps1` also moves the new plugins and `BepInEx/resizers` aside.
  - `ingame/release_test.py` adds three scenarios:
    - `l_setup TABLES PLUGIN [--leftover]` / `l_teardown`: the branch's tables and plugin in place of `Mods/English`.
    - `l5_setup` / `l5_teardown`: the official `BepInEx_win_x86_5.4.*.zip` from `release/vendor` in place of be.692's
      loader, with `BepInEx.cfg` and the chainloader cache held. The teardown also moves out the configs of plugins
      only BepInEx 5 loaded (the first L5 run left `FanslationStudio.LegendOfMortal.Plugin.cfg` behind).
    - `r_setup RELEASE_ZIP [--clean]` / `r_teardown`: a release zip of the patch extracted as a player's unzip does.
      By default it goes over the current install, leftovers and all. With `--clean`, the BepInEx folder, `Mods` and
      the loader's root files are held aside first, and this mod's plugin folder and settings go back in. The
      teardown moves the release's install to the run folder and checks every file against the hashes taken before.

    Every move is hash-checked. A dry run against a fake game folder (`LOM_GAME`, `LOM_RELEASE_TEST_DIR`,
    `LOM_VENDOR_DIR`) left it byte-identical. `set_harness` now keeps the cfg's CRLF; it used to rewrite it with LF.
    `prep` moves a finished run's `state.json` into that run's folder.
- **Verified in game 2026-09-27** with the owner's go-ahead; saves and registry were backed up and restored, and every
  move was verified.
  - **B, the current release of the OverLlm patch.**
    - Detected as installed and running (one table, Binarizer 72,576 keys, Pristine).
    - All 21 features worked, and the 11 screens showed 0 Chinese.
    - Mod Settings reads as in 1.0.0.
  - **L, the branch's tables (packaged from Files/Converted) and its plugin (compiled from the branch) on be.692.**
    - The detector said "partly installed", with its plugin "built for another BepInEx".
    - The served table was 72,560 keys, 72,524 of them ours, with 3,353 multi-line rows read whole and 0 Chinese.
    - All 21 features worked.
    - Mod Settings: "Installed. Its newer version's plugin doesn't run with this game's BepInEx, so its text shows
      through this mod."
    - Lash's gave `OriginalFromFiles`: StartGame "Departure" and the whole multi-line intro. Revised went back.
    - The 11 screens showed 0 Chinese. Their text equalled B's except the load panel's recent-save slot.
  - **L5, the same on BepInEx 5.4.23.5.**
    - 3 plugins loaded (the patch's, LOM_UI_EN from the BepInEx5 DLL, LOM_Strings_EN); every be.692 plugin was left
      out.
    - After the manager-object fix: installed and running, its plugin hooked, 72,560 keys, Pristine. 19 features
      worked and 2 were not needed (the XUnity bridge, quiet Binarizer).
    - Advanced lists each BepInEx 6 plugin as "built for BepInEx 6, which this game doesn't run".
    - Lash's gave `OriginalHandBack` to the patch's own plugin. With the wording fixes off too, the title read its
      "Environment settings / Scripture Hall / Retiring to the mountains". The start button is scene text.
    - The 11 screens showed 0 Chinese. They equalled B's except the version line (no Binarizer, so no ", mods:
      English") and the recent-save slot.
- **Verified in game 2026-09-28 (night)** with the owner's go-ahead, with that night's build (`LOM_UI_EN.dll`
  02C2C32E..., `LOM_UI_EN.BepInEx5.dll` AD57A274..., SHA-256; superseded by the ladder run below), the same safeguards
  and `EnglishPatch-2026.09.28.13.56.zip`.
  - **R over the current install** (as a player unpacks a new version over the old one):
    - BepInEx 5.4.23.3 loaded 8 plugins: the patch's, its pack's five, LOM_UI_EN from the BepInEx5 DLL and
      LOM_Strings_EN. The manager fix logged.
    - Detected as installed and running: 44 tables plus the old `StringTable.csv` left out, its plugin hooked with the
      label hook (72,562 keys, its reading of the leftover included), Pristine, the pack hooked; Binarizer, XUnity and
      the KR plugin installed but not running. 20 features worked and 2 were not needed (the XUnity bridge, quiet
      Binarizer).
    - `BaseGuards`: labels held back (129 at the title), the snapshot hooked, 888 resizes held, prefab texts 63 of 111
      and code strings 8 of 121 this mod's; the pack's patcher reported "61 successful".
    - The 11 screens showed 0 Chinese and equalled B's except the version line and the animated WhiteRing.
    - The Translation status asked to delete `StringTable.csv`; About listed the pack.
    - Lash's (with the wording fixes off) gave the title "Departure | Environment settings | Scripture Hall | Retiring
      to the mountains" from the patch's own plugins, and Revised went back to "Set Out | Settings | Records |
      Retire". `tget` still read the game's Chinese after the hand-back (the snapshot).
    - The resizer switch on took the title buttons from 27 to 24 px. A duel showed only the game's hidden `一二三`
      placeholders.
  - **R clean** (the release alone, then this mod's mod-only files): the same 8 plugins; installed and running, 44
    tables, 72,560 keys, Pristine; 20 features worked and 2 were not needed; the same guard counts. Translation:
    "Installed."; About as above; "This mod's 72,524 lines, Lash's English Patch for the rest (72,560 in all)". The 11
    screens (but the load panel's save times), Lash's and back, and the duel equalled R over. Lash's story screen
    showed the release's own lines ("Masked Doctor", "Heart Connection").
  - **B again**, the owner's setup (be.692 and the 2026-02 release): 21 features worked and `basePlugins` was not
    needed. The 11 screens equalled the 2026-09-27 B run's text and layout exactly, and Lash's and back worked through
    Binarizer.
  - The harness's `invoke static` logs a HarmonyX `GetTypesFromAssembly` warning per call on B: `AccessTools.TypeByName`
    walks every assembly on a miss, and the NAudio a be.692 plugin loads throws. It only affects the harness.
- **The release ladder, 2026-09-28 09:50-11:34** (the owner: "Rerun our full test ladder"), docs/DEVELOPMENT.md steps 2
  to 5 in run `_release_test/run_20260928-095045`. The first pass found the three gaps below. Their fixes gave the final
  build (`LOM_UI_EN.dll` 27B93D69..., `LOM_UI_EN.BepInEx5.dll` 4C2E0C9B..., SHA-256), and the whole ladder was run
  again on it.
  - Offline:
    - the scratchpad harnesses passed on both builds: the branch reader 44 checks, and the release reader with the
      guards' filters 32;
    - `lashtables.py` matched the C# reader on the branch tables and on the release (0 differences, round trip 0);
    - `publish.py check` passed;
    - `release.py --candidate` rebuilt both DLLs byte for byte.
  - A, the `-full` candidate on a clean game:
    - 348 rules, 19 features working and 3 not needed;
    - 0 Chinese on the 11 screens and the six Mod Settings pages;
    - a name tip with its portrait (stock Addressables) for a line written with `say`;
    - the duel's hidden `一二三` placeholders only;
    - the extracted folder equal to the `-mod-only` zip;
    - the screens equal to B's but the version line.
  - R clean and R over, with the release zip:
    - the checks of the night run, with the same results;
    - `SimulateHookErrors = basePlugins` turned the feature off ("text sizes"; in L5 "labels"), and Try again brought
      it back;
    - `SimulateGameChange = basePlugins` gave "not working" with the patch's labels and sizes;
    - the renamed-postfix plugin gave "partly working" with this mod's text kept.
  - L with and without the old `StringTable.csv`, and L5, with the release's tables and plugin:
    - as on 2026-09-27, 21 working (1 not needed) on be.692 and 20 (2) on BepInEx 5.4.23.5;
    - the leftover left out;
    - Lash's `OriginalFromFiles` / `OriginalHandBack` and back;
    - the screens equal to those of 2026-09-27.
  - B:
    - all checks, 21 working;
    - `SimulateHookErrors = all` turned 16 features off with their fallbacks (one Chinese numeral on the Status screen,
      as the numerals fallback says), and Try again brought all 16 back;
    - B logs the say dialogs' hidden placeholder texts to untranslated.txt through the XUnity bridge (unchanged since
      1.0.0; never on screen).
  - C, `SimulateGameChange = all`:
    - 20 not working with their fallbacks;
    - Mod Settings opened with 0 Chinese, and the text was Lash's;
    - the one Chinese text left was the hidden `[內容]` placeholder of the time-progress result panel.
  - Then Mark as checked on B: `compat_verified.json` records 1.1.0, 2026-09-28, 21 working and `basePlugins` not
    needed. `release.py --candidate` passes every gate.
  - The gaps found:
    - the guards never called `Probe`, so the ladder's hook-error step could not reach them;
    - About's pack and label lines ignored whether the guards were live;
    - the game-text copy depended on finding the label postfix by name (see "The game's text").
  - Tool fixes:
    - `release_test.py`: repeated L / L5 teardowns in one run go to `L-2_*`, `L5-2_*` (the second L refused before);
    - `set_harness` accepts a harness already on (`r_setup` after `b_setup` stopped after moving the files, before
      saving its record; that record was rebuilt from `R3_before`, and the teardown verified 181 files);
    - `l_teardown` removes the empty holding folder.
    - While R is set up, `BepInEx\core` holds BepInEx 5: build then with `-CoreDir` (as in L5), or after `r_teardown`.
