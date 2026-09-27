export const meta = {
  name: 'lom-glossary-sweep',
  description: 'Glossary-conformance sweep: Sonnet checks each reviewed line whose Chinese holds a ruled term its English lacks, fixes only real misses, then a Sonnet refuter checks each fix',
  phases: [
    { title: 'Fix', detail: 'one Sonnet fixer per chunk of flagged lines (keep or minimal term fix)', model: 'sonnet' },
    { title: 'Verify', detail: 'one Sonnet refuter per chunk with fixes', model: 'sonnet' },
  ],
}
// args: { dir, from, to } (chunks p_NNNN.json written by candidates.py) or { dir, chunks: [n...] }
const LOC = 'C:/Program Files (x86)/Steam/steamapps/common/LegendOfMortal/LOM_Localization'
const D = args.dir || (LOC + '/fullpass/glossary')
function expand() {
  if (args.chunks) { return args.chunks }
  const out = []
  for (let n = args.from; n <= args.to; n++) { out.push(n) }
  return out
}
const LIST = expand()
const EDIT_SCHEMA = { type: 'object', properties: { items: { type: 'array', items: { type: 'object', properties: {
  id: { type: 'string' }, keep: { type: 'boolean' }, en: { type: 'string' }, note: { type: 'string' } },
  required: ['id', 'keep'] } } }, required: ['items'] }
const VERDICT_SCHEMA = { type: 'object', properties: { verdicts: { type: 'array', items: { type: 'object', properties: {
  id: { type: 'string' }, accept: { type: 'boolean' }, reason: { type: 'string' } }, required: ['id', 'accept'] } } }, required: ['verdicts'] }
async function ag(prompt, opts) {
  let r = await agent(prompt, opts)
  if (r == null) { log('retrying ' + (opts.label || 'agent') + ' after a null return'); r = await agent(prompt, Object.assign({}, opts, { label: (opts.label || 'agent') + ':retry' })) }
  return r
}
const file = (i) => `${D}/p_${String(i).padStart(4, '0')}.json`
const CONTEXT = `Background: Legend of Mortal (活俠傳) is a Song-dynasty wuxia visual novel. The player is Zhao Huo (趙活), an outer disciple of the Tang Clan (唐門); narration addresses him as "you". Every English line here has already been reviewed line by line for fidelity and wuxia voice; this pass only checks the owner's binding terminology.
Binding terminology: ${LOC}/glossary/conventions.md (read it first, especially the owner's rulings at its end: 大人 office titles, 娘子 Lady X, 師叔 Martial Uncle, 護法 Guardian, 內力 Internal Force, 真人 Immortal X, 大俠 Great Hero ...) and ${LOC}/glossary/Glossary.proposed.yaml (grep it by the Chinese term for its context note).`
function fixPrompt(i) {
  return `${CONTEXT}
Task: glossary check of chunk ${i}. Read ${file(i)}. Each line has id, key (the table row; Story/ and LegendInfo/ rows are story text, other prefixes are interface data), speaker, zh (the Chinese source, authoritative), en (the current English), before/after (neighbouring lines), and terms: glossary terms found in the Chinese whose ruled English (ruled, alts = allowed alternatives, why = the ruling's note) the English does not contain.
For every line decide:
- keep=true when the flag is a false alarm: the Chinese characters are not the glossary term in this sentence (they are part of another word, or ordinary language: 氣力 as plain strength in prose rather than the duel-bar stat, 道人 in an idiom, a technique name used as an everyday phrase); or the English already conveys the term acceptably (a pronoun or "you" where English would not repeat the title, the title said once in the sentence, a vocative English naturally drops, the plural or possessive of the ruled form, a lower-case generic mention "the sect leader"); or the ruled form would make this line wrong or awkward.
- keep=false with en = the full corrected line when the English really misses the ruling: it uses a different or retired rendering of the term (a title like "Master" or "Head" where the ruling says Sect Leader, "Little Sister" for Junior Martial Sister, "Lord Song" where the office title is ruled, a name spelled another way, a technique or place named differently), or it silently drops a title or name the reader needs. Work the ruled form (or an allowed alternative) in with the smallest change that reads naturally: keep every other word, the punctuation, the line breaks, rich-text tags and placeholders exactly; Title Case for a title before a name (Sect Leader Xiang), and grammar adjusted only where the swap needs it.
Only terminology: do not reword anything else, even if you would phrase it differently.
Output: every id exactly once with keep; en (the full new line) only when keep=false; note (at most 12 words) naming the term fixed or why it was kept when unsure.`
}
function verifyPrompt(i, changed) {
  return `${CONTEXT}
Task: adversarially verify glossary fixes for chunk ${i}. The chunk with the Chinese, the old English, the flagged terms and the neighbouring lines is ${file(i)}; match by id. Proposals below are [{id, new}]. Try to refute each: the Chinese characters are not the glossary term in this sentence, the old English already conveyed the term acceptably, the fix changes anything besides the term (other words, meaning, who speaks or is addressed, punctuation, line breaks, tags, placeholders), the fix uses a form that is neither the ruled form nor an allowed alternative, reads unnaturally, or contains Chinese characters. Accept only fixes that bring the line into line with the ruling and read naturally. One verdict per id; reason at most 15 words when rejecting.
Proposals: ${JSON.stringify(changed)}`
}
phase('Fix')
const results = await pipeline(LIST,
  async (n) => ag(fixPrompt(n), { label: `review:${n}`, phase: 'Fix', schema: EDIT_SCHEMA, model: 'sonnet', effort: 'medium' }),
  async (r, n) => {
    if (!r || !r.items) { return { chunk: n, ok: false } }
    const fixes = r.items.filter(x => x.keep === false && x.en)
    if (!fixes.length) { return { chunk: n, ok: true, fixes: 0, accepted: 0 } }
    const v = await ag(verifyPrompt(n, fixes.map(f => ({ id: f.id, new: f.en }))), { label: `verify:${n}`, phase: 'Verify', schema: VERDICT_SCHEMA, model: 'sonnet', effort: 'medium' })
    const acc = v && v.verdicts ? v.verdicts.filter(x => x.accept).length : 0
    return { chunk: n, ok: !!v, fixes: fixes.length, accepted: acc }
  })
const done = results.filter(Boolean)
return {
  chunks: LIST.length,
  chunksOk: done.filter(x => x.ok).length,
  failed: done.filter(x => !x.ok).map(x => x.chunk),
  fixes: done.reduce((s, x) => s + (x.fixes || 0), 0),
  accepted: done.reduce((s, x) => s + (x.accepted || 0), 0),
}
