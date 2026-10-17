# LOM English Localization + QoL

A complete, reviewed English translation of **Legend of Mortal** (活俠傳, Obb Studio), with layout fixes for English and a
handful of quality-of-life features.

Many thanks to Lash for **[Lash's English Patch](https://github.com/joshfreitas1984/LegendOfMortalOverLlm)**, the original
English patch for the game, which this mod is built on. This mod is meant to be compatible with it, so you can keep both
installed.

![Title screen with the mod installed](docs/screenshots/title.jpg)

**[Download the latest release](https://github.com/DataRogue/LOM-English-Localization-QoL/releases/latest)** ·
[Features](#features) · [Install](#install) · [Updates and forks](#updates-and-forks) ·
[Troubleshooting](#troubleshooting) · [Report a problem](https://github.com/DataRogue/LOM-English-Localization-QoL/issues)

## Features

### Translation

- **The whole game in English.** All 72,524 game-data entries (items, skills, stats, menus and story) and all 52,714
  scene lines were reviewed in their scene, for accuracy and for the wuxia voice of the original.
- **One consistent glossary** for names, sects, titles, ranks and martial arts. 唐門 is always the Tang Clan, and 內力
  always Internal Force.
- **Names restored** where the machine translation had turned them into words.
- **Choices you can trust.** Every choice line was checked against the story branch it leads to.
- **Your pick of text.** Choose between this mod's translation, the text of Lash's English Patch, or no text from this
  mod at all ("UI only"). You can switch in the game.

### Screens that fit English

- **Layout fixes** where English would overflow, overlap or be cut off. They cover the status screens, martial arts,
  upgrades, the fate shop, the HUD, menus, dialogue, settings, the library, save slots, duels and army battles.
- **Translated images** for the title, the menus and other art with Chinese baked in.
- **Horizontal layouts** where the game stacks Chinese text vertically.
- **English numerals** instead of Chinese ones.
- **Dialogue tuned for English:** line spacing and text speed, since English runs about twice as long as the Chinese.
- **Long dialogue logs stay readable**, where the game's own backlog would go blank.

### Quality of life (all optional)

- **Quick save and quick load** with F5 and F9, wherever the game allows saving.
- **Character tooltips.** Point at a name in dialogue or a choice menu to see the character's portrait, title and your
  affinity. A character gets a tooltip only once the story has introduced them, so tooltips never spoil a reveal.

  ![A character tooltip over a line of narration](docs/screenshots/name-tooltip.jpg)
- **Faction tooltips.** Each sect, clan and gang has a short, spoiler-free description.

  ![A faction tooltip in a talk menu](docs/screenshots/faction-tooltip.jpg)
- **Exact affinity values** in Status > Social.
- **Clearer duel gauges.** Poison and paralysis get a mark at every tier (50, 100 and 150). The gauge's tooltip gives
  the exact build-up, what each tier does and how fast the build-up falls.

  ![A duel gauge tooltip](docs/screenshots/duel-gauge-tip.jpg)

### Settings and game updates

- **An in-game Mod Settings window.** Open it with F8, or with the button on the title screen and in the pause menus.
  Each setting has one plain sentence of help, and almost all of them apply immediately.
- **Built to survive game updates.** Each feature checks the game when it starts. If an update breaks one, that feature
  switches itself off and says so, and the rest of the mod keeps working.
- **Works with or without Lash's English Patch.** Installed together, you can switch to its text in Mod Settings, and
  after a game update it can fill in lines this mod has not caught up with.

![The Mod Settings window](docs/screenshots/mod-settings.jpg)

## Install

You need Legend of Mortal on Steam for Windows, with the game language on **Traditional Chinese** (the default); the mod
shows that language in English. It is tested with game version `release_1.0.6000.3` (Steam build 25987745, the 64-bit
update of October 2026).

1. Download `LOM-English-Localization-QoL-<version>-full.zip` from
   [Releases](https://github.com/DataRogue/LOM-English-Localization-QoL/releases/latest).
2. In Steam, right-click Legend of Mortal > **Manage** > **Browse local files**. This opens the folder that holds
   `Mortal.exe`.
3. Extract the zip into that folder, so that `winhttp.dll` and the `BepInEx` folder sit next to `Mortal.exe`.
4. Start the game. The title screen now has a **Mod Settings** button; F8 opens the same window anywhere.

The full package includes [BepInEx](https://github.com/BepInEx/BepInEx) 5.4.23.3, the mod loader: the same version
Lash's English Patch installs. If you already have it, for example because Lash's English Patch is installed with its
installer, use the smaller `-mod-only.zip` instead.

From 1.2.0 the mod runs on BepInEx 5.4.23.3 only. An older setup on BepInEx 6 (this mod's full package up to 1.1.x, or
Lash's English Patch up to its 2026-02 releases) does not load it; see Updating. Version 1.1.x still runs on BepInEx 6.

**The game is 64-bit since its October 2026 update.** BepInEx's loader (`winhttp.dll`) comes in a 32-bit and a 64-bit
build, and the updated game only loads the 64-bit one. From 1.3.0 the full package brings the 64-bit build; up to 1.2.0
it brought the 32-bit one. If no mod loads since that update, see Updating.

**Updating:** delete the folder `BepInEx/plugins/LOM_UI_EN` first (your settings are kept in `BepInEx/config`; this also
removes 1.1.x's `LOM_UI_EN.BepInEx5.dll`, which is no longer used). Then:
- if you installed this mod's full package (any version up to 1.2.0), extract the new `-full.zip`: it brings the
  64-bit loader the updated game needs (and moves a 1.1.x or older install from BepInEx 6 to BepInEx 5.4.23.3);
- if you use Lash's English Patch set up by its installer, run its newest installer once (from 2026-10-16 on it
  installs the 64-bit BepInEx; the patch's in-game updater cannot do this, as no mod loads until then), then extract
  the new `-mod-only.zip`;
- if the game already runs the 64-bit BepInEx 5.4.23.3 (this mod's 1.3.0 full package, or Lash's installer from
  2026-10-16 on), extract the new `-mod-only.zip`;
- if you use Lash's English Patch from before its installer (its 2026-02 releases, BepInEx 6), run Lash's installer
  first, which updates the patch and moves the game to BepInEx 5.4.23.3, then extract the `-mod-only.zip`. Delete
  `Mods/English/StringTable.csv` afterwards: the new patch reads that old file over part of its new text (Mod Settings
  reminds you).

Lash's English Patch now offers its own updates when the game starts; they never touch this mod's folder.

**Uninstalling:** delete `BepInEx/plugins/LOM_UI_EN`. If no other mod uses BepInEx, also delete the `BepInEx` folder,
`winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and `changelog.txt`. Steam's "Verify integrity of game files"
does not remove mods.

**Steam Deck and Linux (Proton):** not tested. BepInEx under Proton usually needs the launch option
`WINEDLLOVERRIDES="winhttp=n,b" %command%`.

## Updates and forks

A note from the maintainer: I may be slow to update this mod, especially after a game update. Two things soften that.

- **The mod keeps working after a game update.** Anything the update breaks switches itself off, and the rest carries
  on. New or changed game text may show in Chinese until the mod catches up. With Lash's English Patch installed, its
  lines fill in wherever it has them.
- **You don't have to wait for me.** You are welcome to fork this repository, update it yourself and share your version.
  You don't need to ask. The code is MIT-licensed and the translation text is free to reuse with credit (see
  [Licence](#licence)). The developer guide's
  [Updating in a fork](docs/DEVELOPMENT.md#updating-in-a-fork) explains how to fix what a game update broke, bring the
  text up to date, test your build and package it. If you keep an updated fork, feel free to
  open an issue here with a link, so players can find it.

## Troubleshooting

- **Nothing changed, and there is no Mod Settings button.** The files are probably one folder too deep. `winhttp.dll`
  must be next to `Mortal.exe`, not inside a `LOM-English-Localization-QoL...` folder. After a start, `BepInEx/LogOutput.log`
  should exist.
- **No Mod Settings button since the game's October 2026 update.** The game is 64-bit now and does not load the
  32-bit loader of this mod's full package up to 1.2.0 (or of Lash's installer before 2026-10-16). The game starts
  without any mod, and `BepInEx/LogOutput.log` is not written again (it keeps its old date). Extract the 1.3.0
  `-full.zip`, or run the newest installer of Lash's English Patch, as in Updating.
- **No Mod Settings button after updating from 1.1.x.** The game still runs BepInEx 6, which does not load 1.2.0: extract
  the `-full.zip` (or run the installer of Lash's English Patch). The first line of `BepInEx/LogOutput.log` names the
  BepInEx version; 1.2.0 needs 5.4.23.3.
- **Some text is still in Chinese.** Check that the game language is Traditional Chinese. After a game update, new or
  changed text stays Chinese until the mod is updated; Mod Settings > Compatibility says what changed.
- **My antivirus flags `winhttp.dll`.** It is BepInEx's loader, Unity Doorstop, and is unmodified from the official
  BepInEx release. Loaders like it are a common false positive.
- **After a game update, a feature is marked as not working.** It switched itself off to keep the game working. The rest
  of the mod is fine. Please report it, attaching the report described below.

## Reporting a problem

Open an [issue](https://github.com/DataRogue/LOM-English-Localization-QoL/issues) and attach:
- `compat_report.txt`, from Mod Settings > Compatibility > **Write a report** (it is written to
  `BepInEx/plugins/LOM_UI_EN`);
- `BepInEx/LogOutput.log`;
- a screenshot, for anything about text or layout.

For a translation error, the Chinese line and where it appears help most.

## How the translation was made

This mod started from Lash's English Patch, a machine translation of the whole game, and went through a series
of passes:
- a terminology glossary built from the game data, with 23 rulings on the contested terms made by the maintainer;
- a sweep that restored personal names the machine translation had turned into words;
- a review of every choice against the script it branches to;
- a proofread of chapters 1 to 3;
- a line-by-line review of everything else, each line with its scene and the code around it for context.

The reviews were done with LLM assistance: each line was reviewed by a model, each rewrite was checked by a second pass,
random samples were audited, and the maintainer ruled on the glossary. It is not a human professional translation. If
something reads wrong, please report it.

## Building from source

The plugin is C# for BepInEx 5.4.23.3 on Unity 2020.3 Mono (the 1.0.x and 1.1.x branches build for BepInEx 6 too).
`src/LOM_UI_EN/build.ps1` builds it with the compiler that comes with Visual Studio 2022 or later. The build is deterministic, so you can check a release DLL against the source. The
translation lives in `workspace/`, and `tools/` holds the maintainer tools that turn it into the plugin folder and
package releases. See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## Credits

- **Lash**, for [Lash's English Patch](https://github.com/joshfreitas1984/LegendOfMortalOverLlm), the original English
  patch this mod grew from. Lines that kept its wording, and the text sizes carried over from its resizer files, are
  Lash's work.
- **[BepInEx](https://github.com/BepInEx/BepInEx)** (LGPL-2.1), the mod loader, bundled in the full package: unmodified
  but for one line of `doorstop_config.ini`, set as the installer of Lash's English Patch sets it.
- **[XUnity.AutoTranslator](https://github.com/bbepis/XUnity.AutoTranslator)** (MIT): its translation-file format and
  lookup are ported in the scene text engine.
- **[Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json)** (MIT), shipped unmodified as Unity's AOT build from
  its `com.unity.nuget.newtonsoft-json` package (Unity Companion License).
- The CSV parser by ideafixxxer, as bundled in [Fungus](https://github.com/snozbot/fungus) (MIT), ported for reading the
  string table exactly as the game does.
- The Korean UI plugin (LegendOfMortal_UI_KR), whose layout approach the first version of this mod built on.

Full notices are in `THIRD_PARTY_NOTICES.txt`, and in `BepInEx/THIRD_PARTY_NOTICES-BepInEx.txt` in the full package.

## Licence

- **Code** (the plugin in `src/` and the tools in `tools/`): [MIT](LICENSE).
- **Translation text** (`workspace/` and the files the release ships in `translation/`, `strings/`, `nametips.tsv` and
  `factiontips.tsv`): you may reuse it in other Legend of Mortal mods and translations, with credit to this project.
  Lines kept from Lash's English Patch remain Lash's work.
- **The game itself:** Legend of Mortal and its text, names and art belong to Obb Studio. The translated images in
  `sprites/` are edited game art. This is an unofficial fan project, not affiliated with or endorsed by Obb Studio.
  Please buy the game.
