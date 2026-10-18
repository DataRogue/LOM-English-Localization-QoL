# Changelog

Player-facing changes. The detailed engineering notes for every version are in
[src/LOM_UI_EN/README.md](src/LOM_UI_EN/README.md).

## Unreleased (1.3.0)

Works with the game's October 2026 update (game version `release_1.0.6000.3`), which made the game 64-bit, and with
Lash's English Patch of 2026-10-16 (`EnglishPatch-2026.10.16.07.52`).

- **The game is 64-bit now.** It only loads the 64-bit build of BepInEx's loader (`winhttp.dll`), and the full package
  up to 1.2.0 brought the 32-bit one: since the update the game starts without any mod, Lash's included. The full
  package now brings BepInEx 5.4.23.3 for 64-bit, as Lash's installer does since 2026-10-16. If you installed this
  mod's full package before, extract the new full package; with Lash's English Patch, run its newest installer once
  (its in-game updater cannot do it, as no mod loads until then), then extract the mod-only package.
- **Name tooltips.** A character whose stage portrait cannot be loaded shows their full-length art from Status >
  Social again: the update moved that art out of memory, and the tooltip now loads it like the stage portraits.
- **Story dialogs and the language list keep their layout.** The update moved the speaker's name plate inside the
  dialog box (for its new dialog-size setting) and rebuilt the language dropdown's list; this mod's sizing of names and
  titles, and the "English" entry in the language list, follow them.
- **The update's new story and text are translated.** About 19,400 new lines: the new story chapters (about 16,600
  lines), about 1,500 chronicle entries in Status > Legend, and the new items, effects, duel and battle lines, settings
  and other game text, plus 77 new dice-check headers. Each line was translated from the Chinese and reviewed against
  it, with the same glossary and conventions as the rest of the mod. 686 existing lines whose Chinese the update
  changed were translated again.
- **New settings rows.** Settings > Game > Brave Mode and Settings > Graphics > Dialog Size, new in the update, read in
  English, and the Dialog Size options no longer run under the next checkbox.
- **With Lash's English Patch, this mod's line wins on more labels.** That patch swaps some screen texts by their exact
  Chinese as the game sets them, so a few settings labels showed its wording even where this mod has its own (Dialog
  Size read "Middle" and "Big"). These labels now show this mod's line, as everywhere else.
- **Name tooltips for the new characters.** 19 characters the update introduced get portrait tooltips in dialogue and
  choices (among them the Yu family, Meng Gong, General Zhao Fang and the Tie smiths), and the four Kongtong Guardians,
  whose Chinese names changed, are found again.
- **More names link in dialogue.** About one Story line in eight is written with a Windows line break; no name in such
  a line got a tooltip before. They do now.
- **Characters introduced by a card that unlocks nothing.** Some introduction cards (Xia Lingxi, Meng Xianyao, Meng
  Gong) show the character without adding them to Status > Social, so their names never became tooltips. The mod now
  remembers such a card once it has played (in `BepInEx/config/lom.ui.english.met.txt`, for every save on this PC);
  delete the file to forget them.

## 1.2.0 (2026-10-06)

Works with Lash's English Patch of 2026-10-05 (`EnglishPatch-2026.10.05.09.58`) and its new installer, and moves to
the same BepInEx it installs.

- **BepInEx 5.4.23.3.** The mod now runs on BepInEx 5.4.23.3 only, the version Lash's English Patch installs. The full
  package bundles it, set up the way that patch's installer sets it up, and there is one `LOM_UI_EN.dll` again. When
  updating, delete `BepInEx/plugins/LOM_UI_EN` first (1.1.x's `LOM_UI_EN.BepInEx5.dll` is no longer used). A game still
  on BepInEx 6 needs BepInEx 5.4.23.3 first: from this mod's full package up to 1.1.x, extract the 1.2.0 full package;
  with Lash's patch from its 2026-02 releases, run that patch's installer, then add the mod-only package. 1.1.x still
  runs on BepInEx 6.
- **Lash's text resizer stays held back.** From its 2026-10-04 release on, that patch reworked its text resizer, and
  1.1.x no longer finds it: with 1.1.x its 80% shrink applies on top of this mod's layout. This version holds it back again
  (Mod Settings > Advanced > "Resizer of Lash's English Patch" still lets it run). Its updates, which it now offers
  when the game starts, never touch this mod's folder.
- **The old resizer files are gone.** Up to 1.1.x the mod still sized some text with resizer files from the patch's
  XUnity days (`translation/scene/resize`). Their sizes are now ordinary layout rules, applied to the same texts at the
  same moment, so screens look as they did (checked screen by screen against 1.1.1). The mod no longer reads any
  resizer file, its own or the patch's. Mod Settings > Advanced loses "Text sizes of Lash's English Patch" and "Wide
  text overflow".
- **Mod Settings switches put the screen back exactly.** Switching "Layout fixes" or "Translated images", or
  Compatibility's "Try again", now redraws every text as the screen first drew it. Before, some text could come back
  about 10% larger, or lose its size, until the next screen.
- **Sect tab income.** The facility cards read "+50", "+30": the word "taels" was cut off. It now shows on one line.
- **Book descriptions show in full.** 44 book descriptions showed a fragment ("...but this book was compiled by
  later scholars from the Shanghan Lun and the Jingui Yaolue.") instead of the whole text: an early fix kept only the
  sentences it corrected and replaced the rest with "...", and that fix showed over the full, reviewed description.
  Each now shows the whole description with its corrections. The description boxes in the Bag, under a selected
  martial art and in the Relationships biographies also no longer risk losing their last line.
- **The duel splash reads "Fight Their Mother"**, closer to the original joke (戰你娘親), instead of "Duel Start".

## 1.1.1 (2026-09-28)

A fix for the Attributes screen. Nothing else changed.

- **Attributes tooltips no longer open empty.** On the Attributes screen, the tooltips of Stamina, Blade and Sword,
  Scholarship and the three point counters (Martial Points, Forging, Alchemy) showed an empty box. The mod sizes each
  tooltip to its text, and these short descriptions were still clipped, which removed their only line. All 13
  tooltips now show their text. Tested in game with both builds: BepInEx 6 at 1280x720, 1600x900 and 1920x1080, and
  BepInEx 5 with Lash's English Patch of 2026-09-28, with its text resizer held back and allowed.

## 1.1.0 (2026-09-28)

Works with Lash's English Patch of 2026-09-28 (`EnglishPatch-2026.09.28.13.56`, the first release of its
`llmkit-upgrade` branch) as well as its earlier versions. Tested in game with that release unpacked over an older
install and on a clean install, and with the earlier version 1.0.0 was tested with.

- **Update this mod when you update Lash's English Patch.** Its 2026-09-28 release moves the game to BepInEx 5.
  Version 1.0.0 of this mod is built for BepInEx 6 only, so it stops loading. From 1.1.0 the mod ships
  `LOM_UI_EN.BepInEx5.dll` next to `LOM_UI_EN.dll`, and each BepInEx loads only its own. With that release installed,
  use the `-mod-only.zip`.
- **Lash's new tables.** The new version drops the single `Mods/English/StringTable.csv` for one table per game file
  (`Story_1.csv`, `System_zh-cn.csv` and so on). Its own plugins replace Binarizer and XUnity AutoTranslator. The mod
  reads either layout exactly as that patch's plugin does, under its own lines. The "Lash's" choice in Mod Settings and
  the fallback show that patch's current text.
- **This mod's text stays on screen.** The new version also writes its English into the game's fixed labels, and its
  plugin pack replaces text on the game's screens and in its code. Wherever this mod has its own line, that line
  shows. Everything else shows Lash's text, as before. The mod also keeps its own copy of the game's Chinese text,
  because the new plugin overwrites it, and the mod's checks need the original.
- **Text sizes.** That plugin pack shrinks the game's text (to 80% on most screens). This mod's layout fixes are sized
  for its own text, so the mod holds that resizer back. Mod Settings > Advanced > "Resizer of Lash's English Patch"
  lets it run.
- **BepInEx 5's hidden manager.** On BepInEx 5 with default settings, this game destroys BepInEx's manager object. Every
  plugin's per-frame work stops with it: here the hotkeys, quick save and the text passes. The mod hides that object
  the way BepInEx 6 always does.
- **Mod Settings says what it found.** Each part of that patch is listed in Mod Settings, and a part built for the
  other BepInEx is named as such. Unpacking the new version over an older one leaves the old
  `Mods/English/StringTable.csv` in place. The new plugin then reads it over about 16,000 of its new lines, so Mod
  Settings asks you to delete it.

## 1.0.0 (2026-09-27)

First public release, tested with game version `release_1.0.5000.13` (Steam build 20337760). It was tested in game on a
clean install (BepInEx and this mod only) and alongside Lash's English Patch.

- **Two packages.**
  - A full package that includes BepInEx 6.0.0-be.692, so the mod installs on a game with nothing else.
  - A mod-only package, for updating or for installing next to Lash's English Patch.
- **Newtonsoft.Json ships with the mod.** The mod depends on it, and until now only Lash's English Patch installed it.
- **New name, "LOM English Localization + QoL".** The plugin keeps its folder, `LOM_UI_EN`, so settings carry over.
- **Mod Settings > About** says where to get updates and how to report a problem.
- **The patch this mod grew from is called Lash's English Patch everywhere.** Mod Settings, notices and the README
  used to call it OverLlm. The option on the Which translation buttons reads "Lash's".
- **Text fixes.**
  - The text drawn outside the string table was checked line by line: dice-check headers, dates, credits, labels, item
    tooltips, and the fallback copies of story lines. 392 lines were corrected, for meaning, names, terms, and the game's
    date format.
  - 20 story lines and dice-check headers that read wrongly were fixed. Examples: 我數十聲 now counts to ten, as the
    countdown after it expects. The crowd's 算！ now answers that he counts as a hero. Several cries no longer read as
    word-by-word glosses. Xiaomei and Eldest Senior Brother are spelled as everywhere else.
  - Seven names the text used two ways for are now the same everywhere:
    - 唐門 is always "Tang Clan", and 唐掌門 "Sect Leader Tang";
    - 小師妹 is always "Junior Martial Sister", and 大公子 always "Eldest Young Lord";
    - 第三香 is always "Di San Xiang"; his name was also shown as "Third Fragrance";
    - 小竹 is always "Xiao Zhu", and 金烏上人 always "Venerable Golden Crow" (no more "Jinwu Shangren").
  - The four staff handles in the credits are romanized like the rest.

## Before 1.0 (private builds, 2026-09)

- **0.9:**
  - Poison and paralysis tier marks and exact-value tips on the duel gauges.
  - Mod Settings rewritten for players: one plain sentence per setting, technical settings moved to Advanced.
- **0.8:**
  - Tooltips in choice menus.
  - Faction tooltips with spoiler-free descriptions.
  - Mod Settings pages ordered from closest to the original game to furthest.
- **0.7:** character name tooltips in dialogue, with portrait, title and affinity.
- **0.6:**
  - A line-by-line review of the whole game.
  - The mod carries the complete translation and plays without Lash's English Patch.
- **0.5:** fallback to the line from Lash's English Patch where one of the mod's no longer fits after a game update.
- **0.4:**
  - Its own text engines, so the English no longer depends on the plugins of Lash's English Patch.
  - A compatibility layer that turns off only what a game update breaks.
- **0.3:** the in-game Mod Settings window.
- **0.2:**
  - Faster scene loads.
  - Text reveal speed tuned for English.
  - Exact affinity in Status > Social.
  - Quick save and quick load.
- **0.1:** layout rules, translated images and wording fixes on top of Lash's English Patch.
