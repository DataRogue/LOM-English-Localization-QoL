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

`publish.py` needs `base_ref/`. Rebuild it from a release of Lash's English Patch with `python tools/publish.py rebase <release dir>`,
then `python tools/publish.py source <srcdump.tsv>`.

## Requirements

- The game with BepInEx 6.0.0-be.692 installed (the full release package installs it).
- Visual Studio 2022 or later, or its Build Tools, with the .NET Framework 4.7.1+ targeting pack.
- Python 3.10 or later.

## Build the plugin

```
powershell -ExecutionPolicy Bypass -File src/LOM_UI_EN/build.ps1 [-Game <game folder>] [-OutDir <folder>]
```

The output is `src/LOM_UI_EN/bin/LOM_UI_EN.dll`. Copy it into `BepInEx/plugins/LOM_UI_EN/` while the game is closed,
and keep the previous one in `BepInEx/backup_stock/`. The build is deterministic: the same source, compiler and
references give the same bytes. `release.py` relies on that to prove that a release DLL is the build of the tagged
source.

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
`release/vendor/unity-newtonsoft/Runtime-AOT/`). The game has no Newtonsoft.Json of its own. Lash's English Patch puts
this same file in `BepInEx/core`, and when that copy is present it loads first.

It has to be the AOT build. The official NuGet build generates code at run time (Reflection.Emit), and this game's
stripped runtime refuses that with "Operation is not supported on this platform". The 1.0.0 clean-install test caught
it: none of the layout rules, the image map or the font map could be read, while every hook installed. Since then the
plugin marks those data parts as not working in Compatibility (`Plugin.CheckLayoutData`).

## Release checklist

1. **Version and notes.** Set `VERSION` in `src/LOM_UI_EN/LOM_UI_EN.cs`, then add the version to `CHANGELOG.md` (for
   players) and to `src/LOM_UI_EN/README.md` (engineering notes).
2. **Build and install.**
   - Run `build.ps1` and install the DLL.
   - Copy `src/LOM_UI_EN/PLAYER_README.txt` over the plugin's `README.txt`.
   - Run `publish.py build`, then `publish.py check`.
3. **Candidate packages.** `python tools/release.py --candidate` writes `release/dist/<version>-candidate/`.
4. **Test in game** with the scripts in `tools/ingame/`, backing up the saves and the registry key first:
   - **A clean install:**
     - move Lash's English Patch aside with `overllm_aside.ps1 out`;
     - put back the stock `Mortal_Data/Managed/Unity.Addressables.dll`;
     - move the plugin folder aside;
     - extract the `-full` candidate zip.
   - **Alongside Lash's English Patch:** the `-mod-only` candidate zip.
   - **Both ways:**
     - the 11 standard screens (`screens05.ps1`), a duel and an army battle;
     - `[Dev] SimulateGameChange=all` and `SimulateHookErrors`, then Try again.
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
