# Legend of Mortal - English terminology glossary (proposed)

Generated 2026-09-12 from the game's string tables, story script, code enums and asset catalog.

Files
- Glossary.proposed.yaml  - OverLlm GlossaryLine format (raw / rawTraditional / result / allowalt / literal / context). Entries marked `# OWNER RULING` reproduce your LOM_Strings_EN overrides; `# REVIEW` entries wait on a question in questions.json.
- terminology.tsv / terminology.json - flat table: zh, pinyin, category, corpus uses, current rendering, base glossary entry, proposed English, status, confidence, owner_ruled, needs_user_decision, reviewer notes, earlier Fable decision, rationale.
- terminology_review.md   - the same as a markdown report (decisions needed first).
- questions.json          - the 23 rulings only you can make, with options, recommendation and affected terms.
- sweep_log.json          - every cross-batch consistency change (applied or flagged).
- lom_glossary.html       - the interactive review page (same content as the published artifact).
- conventions.md          - the binding rules the deciders and reviewers worked from.

Pipeline: 139 Sonnet extraction agents -> 71 Sonnet decide batches -> 3 adversarial Sonnet lenses per batch -> Sonnet completeness critic + fill -> 236 Sonnet consistency sweeps -> 20 Fable arbiters -> 1 Fable synthesis. 4,366 decisions, 4,223 kept, 143 dropped as non-terms.

## 2026-09-12 evening: rulings applied
The owner answered all 23 questions (rulings_2026-09-12.md, also appended to conventions.md). 200 family terms were
harmonized (Internal Force, Guardian, Xie Wuchen, Soul-Stealing, Ultimate, Yue Ping, Martial Uncle, Kick, Dashing Fan ...),
no term is flagged any more, and 89 rows of BepInEx\plugins\LOM_UI_EN\strings\*.csv that still used a retired form were
corrected (each row's note records the old text; the untouched originals are in LOM_Localization\backups\strings\).
