export const meta = {
  name: 'lom-update-terms',
  description: 'Settle one English for each recurring new Legend of Mortal term (names, places, titles, techniques) before the story is translated (Sonnet)',
  phases: [{ title: 'Decide', detail: 'one Sonnet term decider per batch of candidate terms', model: 'sonnet' }],
}
// args: { batches: [{batch, file}] } (files from tools/update/term_candidates.py)
const LOC = 'C:/Program Files (x86)/Steam/steamapps/common/LegendOfMortal/LOM_Localization'
const CARD = LOC + '/tools/update/translator_card.md'
const SCHEMA = { type: 'object', properties: { items: { type: 'array', items: { type: 'object', properties: {
  n: { type: 'integer' }, real: { type: 'boolean' }, en: { type: 'string' },
  cat: { type: 'string', enum: ['person', 'place', 'org', 'title', 'technique', 'item', 'address', 'other'] }, note: { type: 'string' } },
  required: ['n', 'real'] } } }, required: ['items'] }

function prompt(b) {
  return `Read these two files in one go (two Read calls in the same turn): the house card ${CARD} and the candidate list ${b.file}.
Background: the English edition is being translated in about 300 independent chunks. A name or term that recurs across chunks needs ONE fixed English or the story contradicts itself. The candidate list holds Chinese character runs that are frequent in the new chapters and rare in the published text. Many are fragments or ordinary words, not terms.
Task: for each numbered candidate decide from its two contexts: real = true when it is a proper noun or term of art that must have one fixed English (a person's name or nickname, a form of address used as a name, a place, building, organisation, rank or title, a technique, item or creature); real = false for a fragment, an ordinary word or a phrase (leave en empty). For a real term give en following the house card: pinyin names surname first; nicknames and epithets translated; places and organisations in the established style (Kongtong Sect, Iron Fist Gate, Tang Clan); titles and ranks as English office names; derived address forms from the settled name (丁承烈 = Ding Chenglie, so 丁護衛 = Guard Ding; 孟仙謠 = Meng Xianyao, so 本縣主 said by her = this County Lady). Add cat and, when the choice is a judgment call, a note of at most 12 words.
Output: items [{n, real, en, cat, note}] covering every candidate number exactly once.`
}

phase('Decide')
const rr = await parallel(args.batches.map(b => () => agent(prompt(b), { label: 'terms:' + b.batch, phase: 'Decide', schema: SCHEMA, model: 'sonnet', effort: 'medium' })))
const ok = rr.filter(Boolean)
log(`Term batches ${ok.length}/${args.batches.length}; ${ok.reduce((n, r) => n + (r.items || []).filter(x => x.real).length, 0)} real terms`)
return { batches: args.batches.length, ok: ok.length }
