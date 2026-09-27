export const meta = {
  name: 'lom-fullpass-review',
  description: 'Full review pass: Sonnet reviews every remaining line in context (story scripts, unreferenced rows, chronicle, data tables, scene-only lines) for fidelity and wuxia voice, then a Sonnet refuter checks each rewrite',
  phases: [
    { title: 'Review', detail: 'one Sonnet reviewer per chunk (keep / rewrite each editable line)', model: 'sonnet' },
    { title: 'Verify', detail: 'one Sonnet refuter per chunk with rewrites', model: 'sonnet' },
  ],
}
// args: { chunks: [{chunk, category}] } or { from, to, skip: [numbers] } (categories from the chunk ranges below), dir optional
const LOC = 'C:/Program Files (x86)/Steam/steamapps/common/LegendOfMortal/LOM_Localization'
const D = args.dir || (LOC + '/fullpass/chunks')
const RANGES = [[1, 1597, 'script'], [1598, 1669, 'unref'], [1670, 1836, 'chronicle'], [1837, 1991, 'data'], [1992, 2072, 'scene']]
function catOf(n) { for (const r of RANGES) { if (n >= r[0] && n <= r[1]) { return r[2] } } return 'script' }
function expand() {
  if (args.chunks) { return args.chunks }
  const skip = new Set(args.skip || [])
  const out = []
  for (let n = args.from; n <= args.to; n++) { if (!skip.has(n)) { out.push({ chunk: n, category: catOf(n) }) } }
  return out
}
const LIST = expand()
const EDIT_SCHEMA = { type: 'object', properties: { items: { type: 'array', items: { type: 'object', properties: {
  id: { type: 'string' }, keep: { type: 'boolean' }, en: { type: 'string' }, drop: { type: 'boolean' }, note: { type: 'string' } },
  required: ['id', 'keep'] } } }, required: ['items'] }
const VERDICT_SCHEMA = { type: 'object', properties: { verdicts: { type: 'array', items: { type: 'object', properties: {
  id: { type: 'string' }, accept: { type: 'boolean' }, reason: { type: 'string' } }, required: ['id', 'accept'] } } }, required: ['verdicts'] }
async function ag(prompt, opts) {
  let r = await agent(prompt, opts)
  if (r == null) { log('retrying ' + (opts.label || 'agent') + ' after a null return'); r = await agent(prompt, Object.assign({}, opts, { label: (opts.label || 'agent') + ':retry' })) }
  return r
}
const file = (i) => `${D}/p_${String(i).padStart(4, '0')}.json`
const CONTEXT = `Background: Legend of Mortal (活俠傳) is a Song-dynasty wuxia visual novel. The player is Zhao Huo (趙活), an outer disciple of the Tang Clan (唐門), a Sichuan sect famed for hidden weapons and poisons; narration addresses him as "you". Most of the English you will see was produced by a small local model translating each line alone: it used they/them for everyone regardless of the Chinese, translated names and nicknames as words, calqued idioms, mixed up who speaks to whom, flattened humble or arrogant self-references (奴家, 老衲, 本座, 在下, 洒家) into plain "I", and often wrote stiff or unidiomatic English.
Binding terminology: ${LOC}/glossary/conventions.md (conventions and the owner's rulings at its end; read it first) and ${LOC}/glossary/Glossary.proposed.yaml (grep it by the Chinese term). Examples: 唐門 Tang Clan, 內力 Internal Force, 武林 Wulin, 江湖 Jianghu, 師兄 Senior Brother, 師弟 Junior Brother, 師姐 Senior Sister, 師妹 Junior Sister, 師叔 Martial Uncle, 師父 Master, 掌門 Sect Leader, 少俠 Young Hero, 護法 Guardian, 極招 Ultimate. Character names are the settled English of their Character/ rows; never rename a character.
Keep the wuxia spirit: this is a world of sects, masters, grudges and honour. Narration is literary and vivid, a little elevated, never modern slang and never mock-Shakespearean. Martial courtesy stays (bowing and cupping fists, "this humble one", "Senior", "Young Hero", "Your Excellency") where the Chinese has it; humble, haughty, rustic, scholarly, playful or menacing voices stay distinct. Idioms keep their image when it reads well in English (a mantis trying to stop a chariot) or become an English idiom of the same force, never a flat paraphrase and never an explanation. Technique and item names are evocative titles (Soul-Stealing Orchid, Flying Swallow Meteor Feather), not word-by-word glosses.`
const RULES = {
  script: `The chunk is a story scene (or several consecutive short scenes separated by a "break" line) in display order. kind: character = spoken by speaker; narrative / center = narration to "you"; think = the player's thought; choice = a menu the player sees; branch = which option the following lines belong to.`,
  unref: `The chunk is story rows no script walk reached, in key order: neighbouring keys (D_ dialogue, N_ narration, T_ thought, O_ option) usually belong to the same scene, so read them as a sequence.`,
  chronicle: `The chunk is chronicle entries (LegendInfo): third-person summaries of what happened, read later in the Legend screen; keep them literary, past tense as the Chinese implies, second person where the Chinese addresses "you".`,
  data: `The chunk is game data text shown in the interface: names (skills, techniques, items, talents, titles, places), descriptions, labels, battle barks and tooltips. Rows of one entity (its Name, Desc, Info...) sit next to each other: keep a name identical wherever it is repeated in its own description and consistent with the glossary. Names and labels must fit small UI boxes: at most about 1.2 times the current English length for names, labels and barks; descriptions may be up to 1.4 times. Keep every number, placeholder ({0}, {0:N0}, {$var}), rich-text tag and line break exactly.`,
  scene: `The chunk is scene-only lines: text the game draws that is no table row (interface labels, credits, key help, and some old or Simplified-Chinese strings); ui, when present, gives the screen, object path and box size. Keep labels short enough for their box. If a line is clearly junk that no player should ever see as English (a captured number like 血-380, a dynamic fragment), set drop=true instead of rewriting. Credits: people and studio names stay as their owners write them.`,
}
function reviewPrompt(i, cat) {
  return `${CONTEXT}
${RULES[cat] || RULES.script}
Task: review chunk ${i}. Read ${file(i)}: context (lines shown just before, read-only) and lines. Each line has id, kind, speaker (settled English name, when spoken), zh (the Chinese source, authoritative) and en (the current English). Only lines with editable=true are yours; everything else is context: read the whole chunk before deciding, so each line fits what comes before and after it.
For every editable line decide keep (the English is faithful to the Chinese, reads as natural, fitting English in the right voice, and matches the scene) or rewrite. Rewrite when the English misreads the Chinese, drops or adds content, uses the wrong person, gender or addressee, uses "they" for a person the scene makes clear, renders a name or term against the glossary, loses the wuxia register, or reads as stilted machine output a native reader would stumble on. A rewrite is a faithful, natural English line in the speaker's voice with the same information, similar length (at most about 1.4 times the current English for story text; data rows as their rules say), sentence case, the same line breaks as the current English or the Chinese, the same rich-text tags and placeholders, no Chinese characters, and no notes or brackets inside the line. Do not change lines that are already good just to vary the style.
Output: every editable id exactly once with keep; en (the full new line) only when keep=false; drop=true only for scene junk; note (at most 12 words) only when the Chinese is ambiguous.`
}
function verifyPrompt(i, changed) {
  return `${CONTEXT}
Task: adversarially verify rewrites for chunk ${i}. The chunk with the Chinese source, the old English and the surrounding lines is ${file(i)}; match by id. Proposals below are [{id, new, drop}]. Try to refute each: the new line says something the Chinese does not, drops something it says, changes who speaks or is addressed, gets a gender or number wrong, breaks a settled glossary term or a character's name, loses the wuxia register or the speaker's voice (a humble line made blunt, a joke made flat, narration made modern), is too long for its place, loses or adds a rich-text tag, placeholder or line break, contains Chinese characters, or is not clearly better than the old line. A drop is right only for junk no player should see. Accept only rewrites that are faithful and an improvement. One verdict per id; reason at most 15 words when rejecting.
Proposals (JSON):
${JSON.stringify(changed)}`
}
phase('Review')
const results = await pipeline(LIST,
  (c) => ag(reviewPrompt(c.chunk, c.category), { label: `review:${c.chunk}`, phase: 'Review', schema: EDIT_SCHEMA, model: 'sonnet', effort: 'medium' }),
  async (res, c) => {
    if (!res) { log(`review:${c.chunk} returned nothing`); return null }
    const items = res.items || []
    const fixes = items.filter(x => x.keep === false && (x.en || x.drop)).map(x => ({ id: x.id, new: x.en || '', drop: !!x.drop, note: x.note || '' }))
    if (!fixes.length) { return { chunk: c.chunk, reviewed: items.length, fixes: [], verdicts: [] } }
    const v = await ag(verifyPrompt(c.chunk, fixes.map(f => ({ id: f.id, new: f.new, drop: f.drop }))), { label: `verify:${c.chunk}`, phase: 'Verify', schema: VERDICT_SCHEMA, model: 'sonnet', effort: 'medium' })
    return { chunk: c.chunk, reviewed: items.length, fixes, verdicts: (v && v.verdicts) || [] }
  })
const ok = results.filter(Boolean)
const proposed = ok.reduce((n, r) => n + r.fixes.length, 0)
const accepted = ok.reduce((n, r) => n + r.fixes.filter(f => r.verdicts.some(v => v.id === f.id && v.accept) && !r.verdicts.some(v => v.id === f.id && !v.accept)).length, 0)
log(`Reviewed ${ok.reduce((n, r) => n + r.reviewed, 0)} lines in ${ok.length}/${LIST.length} chunks; ${proposed} rewrites proposed, ${accepted} accepted`)
// The full per-chunk results are in the journal (one result per agent); return only the stats.
return { chunks: LIST.length, chunksOk: ok.length, missing: LIST.filter(c => !ok.some(r => r.chunk === c.chunk)).map(c => c.chunk), reviewed: ok.reduce((n, r) => n + r.reviewed, 0), proposed, accepted }
