# Development

This repository is the maintainer's `LOM_Localization` folder, which lives inside the game folder:
`<game>/LOM_Localization`. The tools assume that place. Set `LOM_GAME` if the game is somewhere other than the default
Steam folder.

Lash's English Patch is the English patch this mod grew from
([joshfreitas1984/LegendOfMortalOverLlm](https://github.com/joshfreitas1984/LegendOfMortalOverLlm)). Players and these
docs call it that. The code, the tools and the engineering notes call it OverLlm, Original or the base patch.

## What is where

| Path | Holds |
|---|---|
| `src/LOM_UI_EN/` | The plugin: C# source, `build.ps1`, `PLAYER_README.txt` (ships as the plugin's `README.txt`), `THIRD_PARTY_NOTICES.txt`, and `README.md` (engineering notes for every version) |
| `workspace/` | The full working translation: the source of truth that `tools/publish.py` turns into the plugin folder (see `workspace/README.txt`) |
| `tools/` | Maintainer tools: `publish.py` (build and check the shipped text), `release.py` (package a release), `ledger.py` (the review ledger), the apply tools of each review pass, the in-game test scripts in `tools/ingame/`. See [tools/README.md](../tools/README.md) |
| `glossary/` | The binding conventions, the maintainer's rulings, and the terminology table |
| `release/vendor/licenses/` | Licence texts of the BepInEx components bundled in the full package |

These stay out of the repository (see `.gitignore`):
- `base_ref/`: the release of Lash's English Patch that the workspace is compared with, and the game's own Chinese text (`game_source.tsv`,
  from the harness `srcdump`).
- `gamedata/`: the game's Lua scripts.
- `backups/`, and the working files of the review passes.

`publish.py` and `release.py` need `base_ref/`, and it cannot be rebuilt from a fresh clone. `workspace/base_ref.lock`
pins its exact files, and its scene file also holds 113 lines XUnity appended on the maintainer's machine. With
`base_ref/` in place, `python tools/publish.py rebase <release dir>` moves to a newer release of Lash's English Patch, and
`python tools/publish.py source <srcdump.tsv>` records the game's current text. Without it, use the fork path below.

Lash's English Patch changes its layout with its 2026.09.28 release, the first from its `llmkit-upgrade` branch. The
single `Mods/English/StringTable.csv` becomes one table per game file. Its own BepInEx 5 plugins replace Binarizer and
XUnity, and the release installs BepInEx 5.4.23.3. The plugin reads either layout, and `tools/lashtables.py` reads both
the same way for the tools. `rebase` accepts a release in the new layout (an unpacked release zip): it keeps BASE_REF's
XUnity file when the release has none. `publish.py rebase <dir> --dry-run` shows what a rebase would change. BASE_REF
still holds the 2026-02-06 release. A dry run onto `EnglishPatch-2026.09.28.13.56` keeps every row, with these results:
- 16,168 conflicts, 4,323 of them rows the review kept as the patch's line;
- 294 rows that now equal the patch's, so `--allow-converged` is needed;
- 2 rows taken over and 2 removed;
- 33 wording overrides that no longer change anything;
- no scene change.

From its 2026.09.29 release on, Lash's English Patch is installed by its own installer (`Installer-win-x64.exe`, a
GitHub pre-release named `installer`; source in the sibling repo `FanslationStudio.LlmKit`, `FanslationStudio.Installer.*`).
The installer puts BepInEx 5.4.23.3 in place (the official `BepInEx_win_x86_5.4.23.3.zip`, pinned by SHA-256, with
`dll_search_path_override = "BepInEx\core"` written into `doorstop_config.ini` and a marker
`BepInEx/installer-bepinex.sha256`), then the release zip's files as its `release-manifest.json` lists them, and keeps
that manifest in `BepInEx/release-manifest.json`. The zip itself no longer holds BepInEx. An update removes only the
files the previous manifest listed, so it never touches this mod's folder. The pack's `UiEditor` plugin checks GitHub
at every start and offers a newer release in a small window ("Update available"), then hands over to the installer and
quits the game. Since 1.2.0 this mod runs on the same BepInEx 5.4.23.3, and its full package bundles that zip with the
same `doorstop_config.ini` line (`tools/release.py`).

## Updating in a fork

Forks are welcome (see the README). Without `base_ref/`, the maintainer pipeline (`publish.py`, `release.py`) will not
run, but you don't need it to keep the mod working:

1. **Get a working plugin folder.** Extract the latest `-mod-only.zip` from Releases into the game folder. Since 0.6 its
   `translation/` files hold the whole text, not just a patch over Lash's.
2. **Fix what a game update broke.**
   - Mod Settings > Compatibility (and its report) names each feature that switched itself off, and the game member it
     could not find.
   - The features live in `src/LOM_UI_EN/`; `Compat.cs` lists them.
   - Rebuild with `build.ps1` and copy `bin/LOM_UI_EN.dll` into the plugin folder.
3. **Bring the text up to date.**
   - Turn on Mod Settings > Advanced > *Note untranslated text*. Chinese the mod has no line for is then written to
     `translation/untranslated.txt`.
   - Add scene lines to `translation/scene/scene_text.txt` (`Chinese=English`, one per line, escapes as in the file).
   - Add or change game-data rows in `translation/StringTable.csv` (`key,"English"`; the keys are the game's).
   - Edit the plugin folder's files, not `workspace/`. The workspace tables hold asset rows (fonts, images) that must
     never be copied into the plugin.
4. **Test and package.**
   - Play-test the screens you touched. Mod Settings > Advanced > *Mark as checked* records the game version you
     tested.
   - Zip `BepInEx/plugins/LOM_UI_EN` (leave out `translation/untranslated.txt` and `compat_report.txt`). For a full
     package, add BepInEx 5.4.23.3 as in the table below, with `dll_search_path_override = "BepInEx\core"` in its
     `doorstop_config.ini`.

## Requirements

- The game. Its installed BepInEx does not matter for building; to run the build, BepInEx 5.4.23.3 (the full release
  package or the installer of Lash's English Patch installs it).
- `release/vendor/BepInEx_win_x86_5.4.23.3.zip` (see the table below): the build references its core files.
- Unity's AOT `Newtonsoft.Json.dll` in `release/vendor/unity-newtonsoft/Runtime-AOT/` (see the table below), which the
  build references too; without it, the build falls back to the copy in the game's `BepInEx/plugins/LOM_UI_EN/`.
- Visual Studio 2022 or later, or its Build Tools, with the .NET Framework 4.7.1+ targeting pack.
- Python 3.10 or later.

## Build the plugin

```
powershell -ExecutionPolicy Bypass -File src/LOM_UI_EN/build.ps1 [-Game <game folder>] [-OutDir <folder>] [-BepInExCore <folder>]
```

The output is `src/LOM_UI_EN/bin/LOM_UI_EN.dll`, for BepInEx 5.4.23.3, the version Lash's English Patch installs. Since
1.2.0 there is one DLL; the 1.0.x and 1.1.x branches also build the BepInEx 6 (be.692) one. The build references
BepInEx's `BepInEx.dll` and `0Harmony.dll` from `release/vendor/BepInEx_win_x86_5.4.23.3.zip` (checked against its
SHA-256 and extracted once under `%TEMP%`), or from `-BepInExCore` / `LOM_BEPINEX_CORE`; whatever the game folder has
installed does not matter. Copy the DLL into `BepInEx/plugins/LOM_UI_EN/` while the game is closed, and keep the
previous one in `BepInEx/backup_stock/`. The build is deterministic: the same source, compiler and references give the
same bytes. `release.py` relies on that to prove that each release DLL is the build of the tagged source.

Text sizes are layout rules (`workspace/rules`). Up to 1.1.x the plugin also applied XUnity-format resizer files
(`translation/scene/resize/*.resizer.txt`, Lash's from the patch's XUnity days); 1.2.0 retired them, and
`rules/05_text_sizes.json` carries their sizes as `translatedOnly` rules, which the scene text layer applies when it
translates a text (where the resizer applied) and the normal rule passes skip. `publish.py check` fails on a resizer file
in the workspace. The harness `textfit [PATH-PREFIX]` lists, per visible text, whether Unity drew all of it.

## Change the translation

Edit the files in `workspace/`, or run one of the apply tools. Then:

```
python tools/publish.py build
python tools/publish.py check
```

`build` writes the plugin folder's `translation/`, `strings/`, `rules/`, `nametips.tsv`, `factiontips.tsv` and
`THIRD_PARTY_NOTICES.txt`. `check` verifies that folder against the workspace and scans it for text that only
Lash's English Patch has.

## Third-party files (release/vendor)

`release.py` checks each of these against its pinned SHA-256 before using it. Download them into `release/vendor/`:

| File | From | SHA-256 |
|---|---|---|
| `BepInEx_win_x86_5.4.23.3.zip` | https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.3/BepInEx_win_x86_5.4.23.3.zip | `275bc94fa582ba0ddeb5dca1241fd657d5f101563c3a8dd3a4a6c27ca28e2376` (the pin of Lash's installer too) |
| `unity-newtonsoft/com.unity.nuget.newtonsoft-json-3.2.2.tgz` | https://download.packages.unity.com/com.unity.nuget.newtonsoft-json/-/com.unity.nuget.newtonsoft-json-3.2.2.tgz | `5db19a6ec4478ab974922d155aa32b952bc34abadaa78afc7973cf049b62db41` |

The plugin folder ships `package/Runtime/AOT/Newtonsoft.Json.dll` from Unity's package (SHA-256
`a56146202232958f46bd6a28b5a7da166aea123ee0d646735a46e5c341dfbf1f`, pinned in `publish.py` `PINNED_LIBS`; extracted to
`release/vendor/unity-newtonsoft/Runtime-AOT/`). The game has no Newtonsoft.Json of its own. Lash's English Patch up
to its 2026-02 releases puts this same file in `BepInEx/core`, and when that copy is present it loads first. Its
2026.09.28 release has none, so the plugin folder's copy loads.

It has to be the AOT build. The official NuGet build generates code at run time (Reflection.Emit), and this game's
stripped runtime refuses that with "Operation is not supported on this platform". The 1.0.0 clean-install test caught
it: none of the layout rules, the image map or the font map could be read, while every hook installed. Since then the
plugin marks those data parts as not working in Compatibility (`Plugin.CheckLayoutData`).

## Release checklist

1. **Version and notes.** Set `VERSION` in `src/LOM_UI_EN/LOM_UI_EN.cs`, then add the version to `CHANGELOG.md` (for
   players) and to `src/LOM_UI_EN/README.md` (engineering notes).
2. **Build and install.**
   - Run `build.ps1` and install the DLL (a 1.1.x install also has `LOM_UI_EN.BepInEx5.dll`: move it to
     `BepInEx/backup_stock/`, `publish.py check` fails while it is there).
   - Copy `src/LOM_UI_EN/PLAYER_README.txt` over the plugin's `README.txt`.
   - Run `publish.py build`, then `publish.py check`.
3. **Candidate packages.** `python tools/release.py --candidate` writes `release/dist/<version>-candidate/`.
4. **Test in game** with `tools/ingame/release_test.py`. It sets up and undoes each scenario exactly: every move is
   hash-checked, and the saves and registry key are backed up and restored. Drive the game with `run_batch.ps1` and
   `screens05.ps1`. The mod runs on BepInEx 5.4.23.3 only, so every scenario installs it.
   1. `release_test.py prep`
   2. **A, clean install:** `release_test.py a_setup`. This holds the whole install aside (BepInEx, `Mods`, the loader's
      root files) and the patched `Unity.Addressables.dll`, then extracts the `-full` candidate onto the bare game.
      Launch `Mortal.exe` and check:
      - `detect`, `compat`, the log's "loaded: N rules, N sprite replacements";
      - the 11 screens, Mod Settings, a Story scene with name tips, a duel.

      Then `release_test.py a_teardown`, which also checks the extracted plugin folder against the `-mod-only` zip.
   3. **R, with Lash's English Patch:** `release_test.py r_setup <its release zip> --clean --installer` installs it as its
      installer does (BepInEx 5.4.23.3, the manifest's files, `BepInEx/release-manifest.json`) next to this mod's
      plugin folder and settings; without `--installer` the zip is unpacked as a player would (its releases up to
      2026.09.28 carry BepInEx 5 themselves); without `--clean` it goes over the current install. Check:
      - `detect`, `compat`, `invoke static LOM_UI_EN.BaseGuards.Describe` (the resizer held back, its hooks found);
      - the 11 screens, compared with A's;
      - Mod Settings > Translation and About, and Which translation set to Lash's and back;
      - a duel;
      - Mark as checked (step 5);
      - `cfg Dev/SimulateHookErrors=all`, then `invoke static LOM_UI_EN.Compat.TryAgain`;
      - once, the newer plugins' guards failing: `cfg Dev/SimulateHookErrors=basePlugins` turns them off with their
        fallback (About reads "text resizer on"); clear it and Try again.

      Then `release_test.py r_teardown`. With `--clean` the mark went into the test install's plugin folder: copy
      `compat_verified.json` from the run folder's `R<n>_after/BepInEx/plugins/LOM_UI_EN/` into the plugin folder.
   4. **C, a simulated game update:** on R, `SimulateGameChange = all` in the cfg, then restart. Mod Settings must still
      open, and the text must fall back to Lash's English Patch.
   5. **B, the current install as it is** (the maintainer's own): `release_test.py b_setup` / `b_teardown` only switch
      the harness.
   6. `release_test.py final`.

   Harness tips:
   - Slots 001 and 002 may hold the maintainer's own play.
   - The Story autosave (`click /UI/Layer_2/LoadGamePanel/Container/AutoSave/GameAutoSaveSlot_Story/Slot`) opens a Story
     scene, and the Battle one opens a duel.
   - At 1600x900 the Mod Settings tabs are at `mouseclick 330 y`: Translation 697, Localization 642, Extras 589,
     Compatibility 519, Advanced 466, About 412.
5. **Mark as checked.** Use Mod Settings > Advanced > Mark as checked, or the harness
   `invoke static LOM_UI_EN.Compat.MarkVerified`. This writes `compat_verified.json` for this version and game build.
6. **Package.** `python tools/release.py` refuses unless all of these hold:
   - `publish.py check` passes;
   - a fresh build equals the installed DLL;
   - the README is current;
   - the mark is this version's;
   - every file in the plugin folder is accounted for.

   It writes the `-full` and `-mod-only` zips, `SHA256SUMS.txt` and `FILES.txt` into `release/dist/<version>/`.
7. **Publish.**
   - Commit, and tag the commit `v<version>`.
   - `gh release create v<version> release/dist/<version>/*.zip release/dist/<version>/SHA256SUMS.txt`, with the
     CHANGELOG entry as the notes.
