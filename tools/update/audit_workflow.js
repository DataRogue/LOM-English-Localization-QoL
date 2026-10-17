export const meta = {
  name: 'lom-update-audit',
  description: 'Independent audit of a random sample of the new Legend of Mortal English: one fidelity reader and one voice/terms reader per sample file (Sonnet)',
  phases: [{ title: 'Audit', detail: 'two lenses per sample file', model: 'sonnet' }],
}
// args: { files: [{n, file}] } (files from update_pass/audit, written by the maintainer's audit_make.py)
const LOC = 'C:/Program Files (x86)/Steam/steamapps/common/LegendOfMortal/LOM_Localization'
const CARD = LOC + '/tools/update/translator_card.md'
const SCHEMA = { type: 'object', properties: { items: { type: 'array', items: { type: 'object', properties: {
  id: { type: 'integer' }, verdict: { type: 'string', enum: ['ok', 'minor', 'wrong'] }, issue: { type: 'string' }, fix: { type: 'string' } },
  required: ['id', 'verdict'] } } }, required: ['items'] }
const LENS = {
  F: 'FIDELITY. Judge only whether the English says what the Chinese says: complete (nothing dropped or added), correct meaning, correct person, number and gender, correct names, correct speaker and addressee. wrong = a real mistranslation, omission, addition or misgendering; minor = a small inaccuracy a reader would barely notice.',
  V: 'VOICE AND TERMS. Judge the English as English and against the house card: natural, idiomatic, in the speaker\'s register (wuxia voice, humour kept), settled names and terms, the owner\'s rulings, American spelling, straight single quotes inside lines, "..." and em dashes, no leftover Chinese or full-width marks, no stiffness a native reader would stumble on. wrong = breaks a ruling or a settled term, or reads as clearly broken English; minor = a weaker but acceptable choice.',
}
function prompt(f, lens) {
  return `Read these two files in one go (two Read calls in the same turn): the house card ${CARD} and the audit sample ${f.file}.
You are an independent auditor. Each item has the Chinese (ZH), the published English (EN) and sometimes the lines before it. Lens: ${LENS[lens]}
For every item give verdict ok (no issue under your lens), minor, or wrong; for minor or wrong add issue (at most 12 words) and, for wrong, fix: the whole corrected English line. Do not mark ok lines down for taste.
Output: items [{id, verdict, issue, fix}] covering every item id exactly once (the number after === in each header).`
}
phase('Audit')
const tasks = []
for (const f of args.files) { for (const lens of ['F', 'V']) { tasks.push(() => agent(prompt(f, lens), { label: 'audit:' + lens + ':' + f.n, phase: 'Audit', schema: SCHEMA, model: 'sonnet', effort: 'medium' })) } }
const rr = await parallel(tasks)
const ok = rr.filter(Boolean)
log(`Audit agents ${ok.length}/${tasks.length}`)
return { agents: tasks.length, ok: ok.length }
