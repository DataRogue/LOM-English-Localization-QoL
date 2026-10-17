export const meta = {
  name: 'lom-update-pass',
  description: 'Translate the new Legend of Mortal text from the Chinese (Sonnet), compare it with the Chinese and Lash\'s machine draft (Sonnet), and referee the contested lines (Sonnet)',
  phases: [
    { title: 'Translate', detail: 'one Sonnet translator per chunk (the draft is hidden from it)', model: 'sonnet' },
    { title: 'Compare', detail: 'one Sonnet reviewer per chunk: translation vs the Chinese vs Lash\'s draft', model: 'sonnet' },
    { title: 'Referee', detail: 'mode referee: Sonnet referees over batches of the lines that need a ruling', model: 'sonnet' },
  ],
}
// args: { set: 'story' | 'legend' | 'data' | 'changed', chunks: [{chunk, count, set?}], dir?: chunk folder, full?: bool }
//       or { mode: 'referee', batches: [{batch, file}] }
// Chunk files come from tools/update/make_chunks.py: p_NNNN.txt (the view), l_NNNN.txt (Lash's draft, reviewer and referee only).
const LOC = 'C:/Program Files (x86)/Steam/steamapps/common/LegendOfMortal/LOM_Localization'
const SET = args.set
const setOf = (c) => c.set || SET
const dirOf = (c) => (c.dir || args.dir || (LOC + '/update_pass/chunks/' + setOf(c)))
const CARD = LOC + '/tools/update/translator_card.md'
const pad = (n) => String(n).padStart(4, '0')
const pfile = (c) => dirOf(c) + '/p_' + pad(c.chunk) + '.txt'
const lfile = (c) => dirOf(c) + '/l_' + pad(c.chunk) + '.txt'
const tag = (c) => setOf(c) + ':' + c.chunk
const LIST = args.chunks
const AGENT = args.agent || null
const withAgent = (o) => (AGENT ? Object.assign({ agentType: AGENT }, o) : o)

const T_SCHEMA = { type: 'object', properties: { items: { type: 'array', items: { type: 'object', properties: {
  n: { type: 'integer' }, en: { type: 'string' }, kept: { type: 'boolean' }, note: { type: 'string' } }, required: ['n', 'en'] } } }, required: ['items'] }
const R_SCHEMA = { type: 'object', properties: { items: { type: 'array', items: { type: 'object', properties: {
  n: { type: 'integer' }, verdict: { type: 'string', enum: ['ok', 'fix'] }, lash: { type: 'string', enum: ['same', 'minor', 'differs'] },
  who: { type: 'string', enum: ['ours', 'lash', 'both', 'neither'] }, en: { type: 'string' }, note: { type: 'string' } },
  required: ['n', 'verdict', 'lash'] } } }, required: ['items'] }
const F_SCHEMA = { type: 'object', properties: { items: { type: 'array', items: { type: 'object', properties: {
  id: { type: 'string' }, pick: { type: 'string', enum: ['T', 'R', 'X'] }, en: { type: 'string' }, reason: { type: 'string' } },
  required: ['id', 'pick'] } } }, required: ['items'] }

const REPAIR_SCHEMA = { type: 'object', properties: { items: { type: 'array', items: { type: 'object', properties: {
  id: { type: 'string' }, en: { type: 'string' } }, required: ['id', 'en'] } } }, required: ['items'] }

async function ag(prompt, opts) {
  let r = await agent(prompt, opts)
  if (r == null) { log('retrying ' + opts.label + ' after a null return'); r = await agent(prompt, Object.assign({}, opts, { label: opts.label + ':retry' })) }
  return r
}
const one = (s) => String(s).replace(/\r/g, '').replace(/\n/g, '\\n')

function translatePrompt(c, only) {
  const which = only ? `only the lines ${only.join(', ')}` : `each of the ${c.count} lines marked *N (N = 1..${c.count})`
  return `Read these two files in one go (two Read calls in the same turn): the house card ${CARD} and your chunk ${pfile(c)}.
Task: translate ${which} from Traditional Chinese into English, following the house card, the SETTLED ENGLISH list and the VOICE SAMPLES of the chunk. The '.' lines are context the player has already read and the next> lines are what follows; they are not yours to translate, but read them: they tell you who speaks, who is addressed, and what the line answers.
Before you answer, re-check every line: stutters and repeated syllables are kept (我、我 = I-I), words quoted inside a line use single quotes, no they/them for a known individual, the number of line breaks equals the Chinese, every listed name and term is used, a humble or haughty self-reference keeps its voice, and no Chinese is left.
Output: items [{n, en}] with ${only ? 'those numbers' : 'every number 1..' + c.count} exactly once. en is the complete English line (real line breaks where the Chinese has \\n). Add note (at most 12 words) only when the Chinese is truly ambiguous.`
}

function revisePrompt(c, only) {
  const which = only ? `only the lines ${only.join(', ')}` : `each of the ${c.count} lines marked *N (N = 1..${c.count})`
  return `Read these two files in one go (two Read calls in the same turn): the house card ${CARD} and your chunk ${pfile(c)}.
Task: the Chinese of these lines changed in the game update. For ${which} you see the NEW Chinese, the OLD Chinese that the current English was written for ("was:"), and the current English that players have now. Decide for each: (a) the change does not alter the meaning (a typo, punctuation, a variant character, a name made consistent): keep the current English exactly as it is and set kept = true; or (b) the new text changes the meaning, adds or removes a sentence, or the current English is wrong for it: write the English for the NEW Chinese, keeping every phrase of the current English that is still right (change only what the new text changes) and following the house card; kept = false. A current line that is itself weak for the new Chinese may be improved in case (b), but never rewrite a line only for taste.
Output: items [{n, en, kept}] with ${only ? 'those numbers' : 'every number 1..' + c.count} exactly once. en is the complete English line for the NEW Chinese (for kept = true, exactly the current English).`
}

function comparePrompt(c, t) {
  const lines = t.map(x => x.n + '\t' + one(x.en)).join('\n')
  return `Read these three files in one go (three Read calls in the same turn): the house card ${CARD} (you are the Reviewer), the chunk ${pfile(c)} and Lash's independent machine translation ${lfile(c)} (one "n | text" per line, \\n marks a line break; "(none)" = no draft).
The translator's English for the ${c.count} lines (n, TAB, English; \\n marks a line break):
${lines}
Task, for every n: (1) check the translator's English against the Chinese in the chunk: meaning, completeness, settled names and terms, pronouns and gender, register and the speaker's voice, idiom, line-break count, tags and placeholders. (2) Compare it with Lash's draft and set lash: same (same meaning), minor (only wording differs) or differs (meaning, names, pronouns, tone or content differ). When lash is differs, decide from the Chinese alone whose reading is right and set who: ours, lash, both (both acceptable) or neither. (3) verdict: ok (leave it; this is the default, and you must not rewrite for taste) or fix (a real error only, see the house card); a fix carries en, the whole corrected line in our own wording, never Lash's phrasing copied, and a note of at most 15 words saying what was wrong.
Output: items [{n, verdict, lash, who (only when lash is differs), en (only for a fix), note}] covering every n 1..${c.count} exactly once.`
}

function refereeBatchPrompt(b) {
  return `Read these two files in one go (two Read calls in the same turn): the house card ${CARD} (you are the Referee) and the batch file ${b.file}. It lists lines that need a ruling: for each, a few lines of the scene around it (Chinese, with published English where the player has already seen it), the translator's English (T), the reviewer's fix (R, when there is one), Lash's machine translation (L) and the reviewer's finding. Item ids look like story:12:5.
Task: for every item decide from the Chinese alone which line is best: pick T, pick R, or pick X and write a better line in en (faithful, natural, in the speaker's voice, the same number of line breaks as the Chinese, settled names and terms, our own wording and never Lash's phrasing copied). Prefer T when R is only a matter of taste; prefer R or X when T misreads, drops or adds content, misgenders, breaks a settled term or the voice. A reason of at most 15 words.
Output: items [{id, pick, en (only for X), reason}] covering every item id of the batch exactly once.`
}

function repairPrompt(b) {
  return `Read these two files in one go (two Read calls in the same turn): the house card ${CARD} and the repair batch ${b.file}. Each item gives the Chinese (a line break inside a line is written \\n), an English line that failed an automatic check, and the problem.
Task: return the corrected English for every item, changing as little as possible: keep the meaning, the wording and the voice, and fix only the stated problem. For a line-break problem the English must contain exactly as many real line breaks as the Chinese has \\n, at the matching places (the break after the sentence or clause where the Chinese breaks); never add or drop text, and no empty lines.
Output: items [{id, en}] covering every item id exactly once.`
}

async function stageTranslate(c) {
  const first = setOf(c) === 'changed' ? revisePrompt(c) : translatePrompt(c)
  const r = await ag(first, withAgent({ label: 'translate:' + tag(c), phase: 'Translate', schema: T_SCHEMA, model: 'sonnet', effort: 'medium' }))
  if (!r) { log('translate:' + tag(c) + ' returned nothing'); return null }
  const got = new Map()
  const take = (items) => { for (const x of (items || [])) { if (x && Number.isInteger(x.n) && x.n >= 1 && x.n <= c.count && typeof x.en === 'string' && x.en.trim() && !got.has(x.n)) { got.set(x.n, x.en) } } }
  take(r.items)
  const missing = []
  for (let n = 1; n <= c.count; n++) { if (!got.has(n)) { missing.push(n) } }
  if (missing.length) {
    log('translate:' + tag(c) + ' missed ' + missing.length + ' lines; asking again')
    const r2 = await ag((setOf(c) === 'changed' ? revisePrompt(c, missing) : translatePrompt(c, missing)), withAgent({ label: 'translate2:' + tag(c), phase: 'Translate', schema: T_SCHEMA, model: 'sonnet', effort: 'medium' }))
    take(r2 && r2.items)
  }
  return { set: setOf(c), chunk: c.chunk, count: c.count, t: Array.from(got, ([n, en]) => ({ n, en })).sort((a, b) => a.n - b.n) }
}

async function stageCompare(t, c) {
  if (!t) { return null }
  const r = await ag(comparePrompt(c, t.t), withAgent({ label: 'compare:' + tag(c), phase: 'Compare', schema: R_SCHEMA, model: 'sonnet', effort: 'medium' }))
  if (!r) { log('compare:' + tag(c) + ' returned nothing'); return { set: setOf(c), chunk: c.chunk, count: c.count, t: t.t, r: null } }
  return { set: setOf(c), chunk: c.chunk, count: c.count, t: t.t, r: r.items || [] }
}

if (args.mode === 'referee') {
  // args.batches: [{batch: 3, file: '<path to b_003.txt>'}]; the files come from apply_update.py batches
  phase('Referee')
  const rr = await parallel(args.batches.map(b => () => ag(refereeBatchPrompt(b), withAgent({ label: 'referee:batch:' + b.batch, phase: 'Referee', schema: F_SCHEMA, model: 'sonnet', effort: 'high' }))))
  const okb = rr.filter(Boolean)
  log(`Referee batches ${okb.length}/${args.batches.length}; ${okb.reduce((n, r) => n + (r.items || []).length, 0)} rulings`)
  return { batches: args.batches.length, ok: okb.length, rulings: okb.reduce((n, r) => n + (r.items || []).length, 0) }
}
if (args.mode === 'repair') {
  phase('Referee')
  const rp = await parallel(args.batches.map(b => () => ag(repairPrompt(b), withAgent({ label: 'repair:batch:' + b.batch, phase: 'Referee', schema: REPAIR_SCHEMA, model: 'sonnet', effort: 'medium' }))))
  const okr = rp.filter(Boolean)
  log(`Repair batches ${okr.length}/${args.batches.length}; ${okr.reduce((n, r) => n + (r.items || []).length, 0)} lines`)
  return { batches: args.batches.length, ok: okr.length }
}
phase('Translate')
const results = await pipeline(LIST, stageTranslate, stageCompare)
const ok = results.filter(Boolean)
let lines = 0, fixes = 0, differs = 0, flagged = 0
for (const r of ok) {
  lines += r.t.length
  for (const it of (r.r || [])) { if (it.verdict === 'fix') { fixes++ } if (it.lash === 'differs') { differs++ } if (it.verdict === 'fix' || (it.lash === 'differs' && it.who && it.who !== 'ours')) { flagged++ } }
}
log(`Chunks ${ok.length}/${LIST.length}: ${lines} lines translated; reviewer fixes ${fixes}; Lash differs ${differs}; for the referee ${flagged}`)
const stats = { set: SET, chunks: LIST.length, chunksOk: ok.length, missing: LIST.filter(c => !ok.some(r => r.chunk === c.chunk && r.set === setOf(c))).map(c => tag(c)), lines, fixes, differs, flagged }
if (args.full) { stats.results = ok }
return stats
