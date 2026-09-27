LOM_UI_EN maintainer workspace (never shipped)

  translation/StringTable.csv       the full working string table (lom_paths.TABLE)
  translation/scene/scene_text.txt  the full working scene file (lom_paths.SCENE)
  translation/scene/resize/         this mod's own *.resizer.txt, if any (shipped as they are)
  strings/*.csv                     wording overrides with their full review notes (lom_paths.STRINGS_DIR)
  rules/*.json                      layout rules with their comments (shipped without them)
  sentinels.json                    the data-check sentinel keys (hashes only)
  base_ref.lock                     the base_ref release this workspace is aligned with (do not edit)
  translation/revision_record.tsv   per revised table row, the hashes of the game text and of the OverLlm
                                    line it was revised against (publish.py keeps it; do not edit)

Every tool in LOM_Localization/tools edits these files. The plugin folder gets only the difference from the
OverLlm release in LOM_Localization/base_ref: run python LOM_Localization/tools/publish.py build after editing
(the apply tools do it themselves). Since 0.6 the plugin ships every reviewed line, OverLlm's wording included where the
review kept it; the table rows it leaves out are empty or asset names. Never copy these files into the plugin folder by
hand: publish.py builds and checks it.
