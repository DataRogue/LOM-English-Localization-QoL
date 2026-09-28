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
     package, add BepInEx 6.0.0-be.692 as in the table below.

## Requirements

- The game with BepInEx 6.0.0-be.692 installed (the full release package installs it), or with BepInEx 5 (Lash's
  English Patch installs it from its 2026.09.28 release on; building the BepInEx 6 DLL then needs `-CoreDir`, below).
- Visual Studio 2022 or later, or its Build Tools, with the .NET Framework 4.7.1+ targeting pack.
- Python 3.10 or later.

## Build the plugin

```
powershell -ExecutionPolicy Bypass -File src/LOM_UI_EN/build.ps1 [-Game <game folder>] [-OutDir <folder>]
powershell -ExecutionPolicy Bypass -File src/LOM_UI_EN/build.ps1 -BepInEx5 [-BepInEx5Dir <folder with BepInEx.dll 5.4>]
```

The output is `src/LOM_UI_EN/bin/LOM_UI_EN.dll`, and with `-BepInEx5` `LOM_UI_EN.BepInEx5.dll`: the same source built
for BepInEx 5 (`BIE5`, see `Loader.cs`). That is the loader of Lash's English Patch's own plugins from its 2026.09.28
release on. `-BepInEx5Dir` defaults to the game's `BepInEx/core`, which holds a BepInEx 5 `BepInEx.dll` either way:
5.4.21 next to BepInEx 6 from that patch's older package, 5.4.23.3 alone from its new release. Without BepInEx 6 in
`BepInEx/core`, pass `-CoreDir <a be.692 core folder>` for the BepInEx 6 DLL. Both DLLs ship, and each BepInEx loads
only its own. Copy them into `BepInEx/plugins/LOM_UI_EN/`
while the game is closed, and keep the previous ones in `BepInEx/backup_stock/`. The build is deterministic: the same
source, compiler and references give the same bytes. `release.py` relies on that to prove that each release DLL is the
build of the tagged source.

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
| `BepInEx-Unity.Mono-win-x86-6.0.0-be.692+851521c.zip` | https://builds.bepinex.dev/projects/bepinex_be/692/BepInEx-Unity.Mono-win-x86-6.0.0-be.692%2B851521c.zip | `97720c5f5c70abfb2ae19dba6000529049ae67f053303b3ce2b49e6ad6c0eca6` |
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
   - Run `build.ps1` and `build.ps1 -BepInEx5`, and install both DLLs.
   - Copy `src/LOM_UI_EN/PLAYER_README.txt` over the plugin's `README.txt`.
   - Run `publish.py build`, then `publish.py check`.
3. **Candidate packages.** `python tools/release.py --candidate` writes `release/dist/<version>-candidate/`.
   Besides A, B and C below, the scenarios for the releases of Lash's English Patch from 2026.09.28 on (its
   `llmkit-upgrade`) run between `prep` and `final`:
   - `release_test.py r_setup <its release zip>`, then `r_teardown`, and again with `--clean`. The zip is extracted
     the way a player unzips it. Without `--clean` it goes over the current install and keeps what the older version
     leaves (its `StringTable.csv`, its BepInEx 6 plugins). With `--clean` the BepInEx folder, `Mods` and the loader
     files are held aside first, and this mod's plugin folder and settings go back in. Check:
     - `detect`, `compat`, `invoke static LOM_UI_EN.BaseGuards.Describe`;
     - the 11 screens, compared with B's;
     - Mod Settings > Translation and About, and Which translation set to Lash's and back;
     - a duel;
     - once, the newer plugins' guards failing. First `cfg Dev/SimulateHookErrors=basePlugins`: it turns off with its
       fallback, and About reads "text resizer on". Clear it (`cfg Dev/SimulateHookErrors=`), then `invoke static
       LOM_UI_EN.Compat.TryAgain`. Then `SimulateGameChange = basePlugins` in the cfg and a restart: not working, with
       the patch's labels and text sizes.

     While R is set up, `BepInEx\core` holds BepInEx 5: build with `-CoreDir` (a be.692 core folder), or after
     `r_teardown`.
   - For tables and a plugin built from the branch before a release, `release_test.py l_setup <its Mods/English> <its
     FanslationStudio.LegendOfMortal.Plugin.dll>` (add `--leftover` for an older StringTable.csv left next to the new
     tables), then `l_teardown`;
   - on top of L, `release_test.py l5_setup` / `l5_teardown` puts BepInEx 5 in place of 6. It needs the official
     `BepInEx_win_x86_5.4.*.zip` in `release/vendor`. While it is set up, `BepInEx\core` holds BepInEx 5: build with
     `build.ps1 -CoreDir BepInEx\_lom_release_test\L5_core` (both DLLs).
4. **Test in game** with `tools/ingame/release_test.py`. It sets up and undoes each scenario exactly: every move is
   hash-checked, and the saves and registry key are backed up and restored. Drive the game with `run_batch.ps1` and
   `screens05.ps1`.
   1. `release_test.py prep`
   2. **A, clean install:** `release_test.py a_setup`. This moves aside Lash's English Patch, the core files the official
      BepInEx zip lacks and the patched `Unity.Addressables.dll`, then extracts the `-full` candidate. Launch
      `Mortal.exe` and check:
      - `detect`, `compat`, the log's "loaded: N rules, N sprite replacements";
      - the 11 screens, Mod Settings, a Story scene with name tips, a duel.

      Then `release_test.py a_teardown`, which also checks the extracted plugin folder against the `-mod-only` zip.
   3. **B, alongside Lash's English Patch:** `release_test.py b_setup`, launch, the same checks.
      - Switch Which translation to Lash's and back.
      - Mark as checked (step 5).
      - `cfg Dev/SimulateHookErrors=all`, then `invoke static LOM_UI_EN.Compat.TryAgain`.
   4. **C, a simulated game update:** `SimulateGameChange = all` in the cfg, then restart. Mod Settings must still open,
      and the text must fall back to Lash's English Patch.
   5. `release_test.py b_teardown`, then `release_test.py final`.

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
