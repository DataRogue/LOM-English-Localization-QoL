export const meta = {
  name: 'lom-rowfix',
  description: 'Scene audit follow-up: a Sonnet fixer per chunk of about 5 live table rows or strings overrides the audit flagged as wrong, then a Sonnet refuter checks every fix',
  phases: [
    { title: 'Fix', detail: 'one Sonnet fixer per chunk (keep / rewrite each flagged line)', model: 'sonnet' },
    { title: 'Verify', detail: 'one Sonnet refuter per chunk with rewrites', model: 'sonnet' },
  ],
}
// args: { chunks: [{chunk, ids}] } from scene_audit/rowfix/manifest.json (rowfix.py chunks). Keep this file LF-only.
const LOC = 'C:/Program Files (x86)/Steam/steamapps/common/LegendOfMortal/LOM_Localization'
const D = args.dir || (LOC + '/scene_audit/rowfix')
const file = (i) => `${D}/p_${String(i).padStart(4, '0')}.json`
const EDIT_SCHEMA = { type: 'object', properties: { items: { type: 'array', items: { type: 'object', properties: {
  id: { type: 'string' }, keep: { type: 'boolean' }, en: { type: 'string' }, why: { type: 'string' } }, required: ['id', 'keep'] } } },
  required: ['items'] }
const VERDICT_SCHEMA = { type: 'object', properties: { verdicts: { type: 'array', items: { type: 'object', properties: {
  id: { type: 'string' }, accept: { type: 'boolean' }, reason: { type: 'string' } }, required: ['id', 'accept'] } } }, required: ['verdicts'] }
async function ag(prompt, opts) {
  let r = await agent(prompt, opts)
  if (r == null) { log('retrying ' + opts.label + ' after a null return'); r = await agent(prompt, Object.assign({}, opts, { label: opts.label + ':retry' })) }
  return r
}
const CONTEXT = `Background: Legend of Mortal (活俠傳) is a Song-dynasty wuxia game. The player is Zhao Huo (趙活), an outer disciple of the Tang Clan (唐門), a Sichuan sect famed for hidden weapons and poisons; narration addresses him as "you". You fix lines of its English mod that players see today and that an audit flagged as wrong: rows of the game's string table, and strings overrides (the maintainer's own layer, which the game shows instead of the table row; for DiceHeader keys, the title of a dice-check panel, to which the game appends "......").
Binding terminology: ${LOC}/glossary/conventions.md (the owner's rulings at its end override older lines above them) and ${LOC}/glossary/rulings_2026-09-12.md.
Names: each line's "names" lists names found in it ([Chinese, English, key]). For any other name, Grep ${LOC}/base_ref/game_source.tsv (key TAB Chinese) with "\\t<Chinese>$" for the row that is exactly that name, then Grep ${LOC}/workspace/translation/StringTable.csv (key,"English") with "^<key>," for its English; to see how the table renders a term in context, Grep StringTable.csv for the English candidates.
Keep the wuxia spirit: literary narration, distinct voices (humble, haughty, rustic, playful, menacing), martial courtesy where the Chinese has it; interjections and battle cries read as natural English cries, never word-by-word glosses.`
const FORMAT = `Each line's zh is the Chinese with the game's markup: {size=..}...{/size} and {punch=..}...{/punch} are the game's text effects. The new en keeps every markup tag of the current en exactly (add none, remove none, even where zh has more), the same line breaks as the current en (a real line break in JSON), every placeholder ({0}), no Chinese characters, no notes or brackets that are not in the Chinese, and a length close to the current en. A dice header stays a short heading with no trailing period (a question keeps its "?").`
function fixPrompt(c, only) {
  return `${CONTEXT}
${FORMAT}
Task: chunk ${c.chunk}. Read ${file(c.chunk)}. Each line has id, key, zh, en (what players see now), lives_in, issue (what the audit found), before/after (the rows around it in the story) or, for a dice header, before (the story lines just before the dice check) and same_header (other keys with the same header), and names.${only ? ` Only these ids are yours this time: ${only.join(', ')}.` : ''}
For every line decide keep=true (the issue is mistaken and the English is right) or keep=false with the corrected en: fix what the issue names and anything else plainly wrong in that line, and keep what is right. The new line says what the Chinese says, fits the rows around it (who speaks to whom, what comes next), and uses the established English of names and terms.
Output: every id${only ? ' listed above' : ' in the chunk'} exactly once: keep, en (only when keep=false: the full new line), why (only when keep=false, at most 15 words).`
}
function verifyPrompt(c, props) {
  return `${CONTEXT}
${FORMAT}
Task: adversarially verify the proposed fixes for chunk ${c.chunk}. The chunk (Chinese, current English, issue, surrounding rows, names) is ${file(c.chunk)}; match by id. Proposals below are [{id, new, why}]. Try to refute each: the new line misreads the Chinese or the scene (check the rows around it); it gets a name or term wrong (check a changed name yourself in ${LOC}/base_ref/game_source.tsv and ${LOC}/workspace/translation/StringTable.csv); it adds or removes a markup tag, line break or placeholder; it contains Chinese; it is clumsy or unnatural English for the speaker; it is not clearly better than the current line. Accept only fixes that are right and clearly better. One verdict per id; reason at most 15 words when rejecting.
Proposals (JSON):
${JSON.stringify(props)}`
}
phase('Fix')
const results = await pipeline(args.chunks,
  async (c) => {
    const r = await ag(fixPrompt(c, null), { label: `review:${c.chunk}`, phase: 'Fix', schema: EDIT_SCHEMA, model: 'sonnet', effort: 'high' })
    const items = ((r && r.items) || []).filter(x => c.ids.includes(x.id))
    const missing = c.ids.filter(id => !items.some(x => x.id === id))
    if (missing.length) {
      log(`review:${c.chunk} left out ${missing.length}; asking again for those`)
      const r2 = await ag(fixPrompt(c, missing), { label: `review-rest:${c.chunk}`, phase: 'Fix', schema: EDIT_SCHEMA, model: 'sonnet', effort: 'high' })
      for (const x of ((r2 && r2.items) || [])) { if (missing.includes(x.id) && !items.some(y => y.id === x.id)) { items.push(x) } }
    }
    return items
  },
  async (items, c) => {
    const fixes = items.filter(x => x.keep === false && x.en).map(x => ({ id: x.id, new: x.en, why: x.why || '' }))
    if (!fixes.length) { return { chunk: c.chunk, answered: items.length, fixes: 0, accepted: 0 } }
    const v = await ag(verifyPrompt(c, fixes), { label: `verify:${c.chunk}`, phase: 'Verify', schema: VERDICT_SCHEMA, model: 'sonnet', effort: 'high' })
    const vs = (v && v.verdicts) || []
    const accepted = fixes.filter(f => vs.some(x => x.id === f.id && x.accept) && !vs.some(x => x.id === f.id && !x.accept)).length
    return { chunk: c.chunk, answered: items.length, fixes: fixes.length, accepted }
  })
const ok = results.filter(Boolean)
log(`Answered ${ok.reduce((n, r) => n + r.answered, 0)} lines in ${ok.length}/${args.chunks.length} chunks; ${ok.reduce((n, r) => n + r.fixes, 0)} fixes proposed, ${ok.reduce((n, r) => n + r.accepted, 0)} accepted`)
// The per-line decisions are in the journal (review:N, review-rest:N, verify:N); rowfix.py apply reads them from there.
return { chunks: args.chunks.length, chunksOk: ok.length, answered: ok.reduce((n, r) => n + r.answered, 0),
  proposed: ok.reduce((n, r) => n + r.fixes, 0), accepted: ok.reduce((n, r) => n + r.accepted, 0) }
