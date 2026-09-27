# Changelog

Player-facing changes. The detailed engineering notes for every version are in
[src/LOM_UI_EN/README.md](src/LOM_UI_EN/README.md).

## 1.0.0 (unreleased)

First public release, tested with game version `release_1.0.5000.13` (Steam build 20337760).

- **Two packages.**
  - A full package that includes BepInEx 6.0.0-be.692, so the mod installs on a game with nothing else.
  - A mod-only package, for updating or for installing next to Lash's English Patch.
- **Newtonsoft.Json ships with the mod.** The mod depends on it, and until now only Lash's English Patch installed it.
- **New name, "LOM English Localization + QoL".** The plugin keeps its folder, `LOM_UI_EN`, so settings carry over.
- **Mod Settings > About** says where to get updates and how to report a problem.
- **The patch this mod grew from is called Lash's English Patch everywhere.** Mod Settings, notices and the README
  used to call it OverLlm. The option on the Which translation buttons reads "Lash's".
- **Text fixes.**
  - 唐門 now reads "Tang Clan" everywhere, and 唐掌門 "Sect Leader Tang".
  - The four staff handles in the credits are romanized like the rest.
  - A few scene lines were corrected along the way.

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
