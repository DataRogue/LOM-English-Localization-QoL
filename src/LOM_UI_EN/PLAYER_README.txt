LOM English Localization + QoL (plugin LOM_UI_EN)
A reviewed English translation of Legend of Mortal (活俠傳) with UI fixes, quick save, character and faction tooltips
and duel gauge tiers.
Updates and bug reports: https://github.com/DataRogue/LOM-English-Localization-QoL

Settings: F8 anywhere, or the "Mod Settings" button on the title screen and in pause menus. Pages: Translation (which
English), Localization (this mod's fixes for English), Extras (tooltips, duel gauges, quick save), then Compatibility,
Advanced (technical settings most players never need) and About.

INSTALL
Keep the game's language on Traditional Chinese (the default): this mod shows that language in English.
 - Full package (LOM-English-Localization-QoL-<version>-full.zip): extract it into the game folder, the one that holds
   Mortal.exe, and start the game. It includes BepInEx 5.4.23.3, the mod loader this mod runs on, the same version
   Lash's English Patch installs.
 - Mod-only package (LOM-English-Localization-QoL-<version>-mod-only.zip): for updating, or when BepInEx 5.4.23.3 is
   already installed (the installer of Lash's English Patch installs it). Extract it into the game folder.
Since version 1.2.0 this mod runs on BepInEx 5.4.23.3 only. An older setup on BepInEx 6 (this mod's full package up to
1.1.x, or Lash's English Patch up to its 2026-02 releases) does not load it. Version 1.1.x of this mod still runs on
BepInEx 6.
The game is 64-bit since its October 2026 update (game version release_1.0.6000.3) and loads only the 64-bit BepInEx
loader (winhttp.dll). From 1.3.0 the full package brings that one; up to 1.2.0 it brought the 32-bit one, which the
updated game ignores: it then starts without any mod. See the update steps below.
To update, delete the folder BepInEx/plugins/LOM_UI_EN first; your settings are kept (they are saved in
BepInEx/config), and 1.1.x's LOM_UI_EN.BepInEx5.dll, no longer used, goes with the folder. Then:
 - you installed this mod's full package (any version up to 1.2.0): extract the new full package. It brings the 64-bit
   loader the updated game needs (and moves a 1.1.x or older install from BepInEx 6 to BepInEx 5.4.23.3);
 - you use Lash's English Patch set up by its installer: run its newest installer once (from 2026-10-16 on it installs
   the 64-bit BepInEx; the patch's in-game updater cannot, as no mod loads until then), then extract the new mod-only
   package;
 - the game already runs the 64-bit BepInEx 5.4.23.3 (this mod's 1.3.0 full package, or Lash's installer from
   2026-10-16 on): extract the new mod-only package;
 - you use Lash's English Patch from before its installer (its 2026-02 releases): run Lash's installer first (it
   updates the patch and moves the game to BepInEx 5.4.23.3), then extract the mod-only package, and delete
   Mods/English/StringTable.csv (the new patch reads that old file over part of its new text).
This mod carries its own complete translation: every line of the game was reviewed in its scene for fidelity and the
wuxia voice of the original, with a ruled glossary, corrected names and choice lines checked against the story. It
grew from Lash's English Patch (https://github.com/joshfreitas1984/LegendOfMortalOverLlm), which is optional.
Installed alongside, that patch adds a second source: "Lash's" in Mod Settings > Translation shows its own text,
and "Fall back to Lash's English Patch" shows its line wherever one of this mod's no longer fits the game after an
update.
This mod reads that patch's files where the patch installed them and never changes them: its older versions' single
table (Mods/English/StringTable.csv) or its newer versions' table per game file (Mods/English/Story_1.csv and so on).
Mod Settings > Translation says what it found. After installing a newer version of that patch over an older one,
delete Mods/English/StringTable.csv: the newer patch reads it too, over part of its new text (Mod Settings says so
when it matters).
Where this mod has its own line, it shows over the newer versions' labels, screen text and code text too. Their text
resizer shrinks the game's text (to 80% on most screens); this mod's layout fixes size the text without it, so it is
held back. Mod Settings > Advanced > "Resizer of Lash's English Patch" lets it run.
Lash's English Patch now updates itself: when a newer version is out, the game offers it at start. Its updates change
only that patch's own files, never this mod's folder.

UNINSTALL
Delete the folder BepInEx/plugins/LOM_UI_EN (and BepInEx/config/lom.ui.english.cfg and lom.strings.english.cfg for its
settings). If nothing else uses BepInEx, also delete the BepInEx folder, winhttp.dll, doorstop_config.ini,
.doorstop_version and changelog.txt from the game folder. Steam's "Verify integrity of game files" never removes mods.

CHARACTER AND FACTION TOOLTIPS
In dialogue and in choice menus, the names of characters who have portrait art are coloured. Point at one to see the
character's portrait and title, and, once the character is listed in Status > Social, your affinity with them (level,
progress to the next level and the exact value out of 100). A character with an entry in Status > Social gets a tooltip
only once the game has introduced them, so a name mentioned before a reveal shows no face. A few introduction cards
show a character without adding them to Status > Social (Xia Lingxi, Meng Xianyao, Meng Gong): the mod remembers such a
card once it has played, for every save on this PC, in BepInEx/config/lom.ui.english.met.txt; delete the file to forget.
The names of sects, clans and other factions are coloured too (in a different colour); point at one for a short,
spoiler-free description of who they are.
Mod Settings > Extras turns either kind off or changes its colour. nametips.tsv and factiontips.tsv list the
characters and factions and the name forms matched.

DUEL POISON AND PARALYSIS GAUGES
Poison and paralysis build up on each fighter; at 50 and 100 they set off stronger effects, and at 150 the strongest,
after which the gauge empties. Each gauge now has a mark at every tier, lit while that tier is in effect. Pointing at a
gauge shows the exact build-up, every tier with what it does and how far off it is, and how much the build-up falls at
the start of each round. Mod Settings > Extras > Duels turns either off.

WHEN THE GAME UPDATES
This mod checks the game at every start. Each feature that no longer fits the game (a renamed screen, a changed menu)
turns itself off and says so rather than break the game; everything else keeps working. A short notice appears on
screen, and Mod Settings > Compatibility lists what stopped working and what replaces it. "Write a report" there
creates compat_report.txt in this folder.
Text the update added or changed may show in Chinese until the mod is updated; with Lash's English Patch installed, the
fallback fills it from that patch where it has a line.

REPORTING A PROBLEM
Open an issue at https://github.com/DataRogue/LOM-English-Localization-QoL/issues and attach compat_report.txt (Mod
Settings > Compatibility > Write a report) and BepInEx/LogOutput.log from the game folder, with a screenshot if it is
about text or layout.

USING LASH'S ENGLISH PATCH ON ITS OWN (needs that patch installed)
 - Mod Settings > Translation > Which translation > "Lash's" shows the text of Lash's English Patch: handed back to
   its plugins when they run, otherwise read from its files.
 - "UI only": this mod adds no text at all and Lash's English Patch translates on its own; this mod keeps its layout
   fixes, translated images, fonts, quick save and the rest.

FILES
 LOM_UI_EN.dll           the mod, for BepInEx 5.4.23.3
 translation/            this mod's translation (StringTable.csv = game data rows, scene/ = scene lines; a scene line
                         with nothing after '=' hides that line of Lash's English Patch)
 translation/untranslated.txt   Chinese seen in game that had no English (for translators; written by the game)
 rules/, sprites/, strings/     layout fixes, translated images, wording fixes
 nametips.tsv            the characters whose names in dialogue show a tooltip, and the name forms matched
 factiontips.tsv         the factions whose names show a tooltip, the forms matched and their descriptions
 Newtonsoft.Json.dll     a JSON library the mod needs (older versions of Lash's English Patch have their own copy, which
                         then loads instead)
 compat_report.txt       written at startup and from Mod Settings > Compatibility
 compat_verified.json    the game build this mod was last checked with
 THIRD_PARTY_NOTICES.txt credits and licences of work this mod adapted from other projects
