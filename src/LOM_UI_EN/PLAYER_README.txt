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
   Mortal.exe, and start the game. It includes BepInEx 6.0.0-be.692, the mod loader this mod runs on.
 - Mod-only package (LOM-English-Localization-QoL-<version>-mod-only.zip): for updating, or when BepInEx 6.0.0-be.692 is
   already installed (the OverLlm patch below includes it). Extract it into the game folder.
To update, delete the folder BepInEx/plugins/LOM_UI_EN first, then extract the new version; your settings are kept
(they are saved in BepInEx/config).
This mod carries its own complete translation: every line of the game was reviewed in its scene for fidelity and the
wuxia voice of the original, with a ruled glossary, corrected names and choice lines checked against the story. It
grew from the OverLlm English patch (https://github.com/joshfreitas1984/LegendOfMortalOverLlm), which is optional.
Installed alongside, that patch adds a second source: "OverLlm" in Mod Settings > Translation shows its own text, and
"Fall back to OverLlm" shows its line wherever one of this mod's no longer fits the game after an update.
This mod reads that patch's files where the patch installed them and never changes them. Mod Settings > Translation
says what it found.

UNINSTALL
Delete the folder BepInEx/plugins/LOM_UI_EN (and BepInEx/config/lom.ui.english.cfg and lom.strings.english.cfg for its
settings). If nothing else uses BepInEx, also delete the BepInEx folder, winhttp.dll, doorstop_config.ini,
.doorstop_version and changelog.txt from the game folder. Steam's "Verify integrity of game files" never removes mods.

CHARACTER AND FACTION TOOLTIPS
In dialogue and in choice menus, the names of characters who have portrait art are coloured. Point at one to see the
character's portrait and title, and, once the character is listed in Status > Social, your affinity with them (level,
progress to the next level and the exact value out of 100). A character with an entry in Status > Social gets a tooltip
only once the game has introduced them, so a name mentioned before a reveal shows no face.
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
Text the update added or changed may show in Chinese until the mod is updated; with the OverLlm patch installed, the
fallback fills it from that patch where it has a line.

REPORTING A PROBLEM
Open an issue at https://github.com/DataRogue/LOM-English-Localization-QoL/issues and attach compat_report.txt (Mod
Settings > Compatibility > Write a report) and BepInEx/LogOutput.log from the game folder, with a screenshot if it is
about text or layout.

USING THE ORIGINAL ENGLISH PATCH ON ITS OWN (needs that patch installed)
 - Mod Settings > Translation > Which translation > "OverLlm" shows the OverLlm patch's own text: handed back to its
   plugins when they run, otherwise read from its files.
 - "UI only": this mod adds no text at all and the OverLlm patch translates on its own; this mod keeps its layout
   fixes, translated images, fonts, quick save and the rest.

FILES
 translation/            this mod's translation (StringTable.csv = game data rows, scene/ = scene lines and resize/
                         layout files; a scene line with nothing after '=' hides that line of the OverLlm patch)
 translation/untranslated.txt   Chinese seen in game that had no English (for translators; written by the game)
 rules/, sprites/, strings/     layout fixes, translated images, wording fixes
 nametips.tsv            the characters whose names in dialogue show a tooltip, and the name forms matched
 factiontips.tsv         the factions whose names show a tooltip, the forms matched and their descriptions
 Newtonsoft.Json.dll     a JSON library the mod needs (the OverLlm patch has its own copy, which then loads instead)
 compat_report.txt       written at startup and from Mod Settings > Compatibility
 compat_verified.json    the game build this mod was last checked with
 THIRD_PARTY_NOTICES.txt credits and licences of work this mod adapted from other projects
