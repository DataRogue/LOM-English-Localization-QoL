export const meta = {
  name: 'lom-scene-audit',
  description: 'Scene-only line audit: a Sonnet reviewer per chunk of about 10 live scene-only lines fixes only real errors (meaning, names, terms, consistency with the table row or override, typos), a second pass covers any line it skipped, and a Sonnet refuter checks every rewrite',
  phases: [
    { title: 'Review', detail: 'one Sonnet reviewer per chunk (keep / rewrite each line), plus one for any lines it left out', model: 'sonnet' },
    { title: 'Verify', detail: 'one Sonnet refuter per chunk with rewrites', model: 'sonnet' },
  ],
}
// args: { chunks: [{chunk, ids: ['L123', ...]}] } from scene_audit/chunks/manifest.json; dir optional
// Keep this file LF-only: the Workflow tool refuses a script with CR characters.
const LOC = 'C:/Program Files (x86)/Steam/steamapps/common/LegendOfMortal/LOM_Localization'
const D = args.dir || (LOC + '/scene_audit/chunks')
const LIST = args.chunks
const file = (i) => `${D}/p_${String(i).padStart(4, '0')}.json`
const EDIT_SCHEMA = { type: 'object', properties: { items: { type: 'array', items: { type: 'object', properties: {
  id: { type: 'string' }, keep: { type: 'boolean' }, en: { type: 'string' }, why: { type: 'string' }, ref_issue: { type: 'string' } },
  required: ['id', 'keep'] } } }, required: ['items'] }
const VERDICT_SCHEMA = { type: 'object', properties: { verdicts: { type: 'array', items: { type: 'object', properties: {
  id: { type: 'string' }, accept: { type: 'boolean' }, reason: { type: 'string' } }, required: ['id', 'accept'] } } }, required: ['verdicts'] }
async function ag(prompt, opts) {
  let r = await agent(prompt, opts)
  if (r == null) { log('retrying ' + opts.label + ' after a null return'); r = await agent(prompt, Object.assign({}, opts, { label: opts.label + ':retry' })) }
  return r
}
const CONTEXT = `Background: Legend of Mortal (活俠傳) is a Song-dynasty wuxia game. The player is Zhao Huo (趙活), an outer disciple of the Tang Clan (唐門), a Sichuan sect famed for hidden weapons and poisons; narration addresses him as "you". You audit lines of its English mod's scene file: text the game draws that is not a row of its string table (dice-check headers, interface labels, credits, dates, tooltips, and story lines whose markup or line breaks differ from their table row).
A full review pass already went over these lines, but a spot check still found real errors in a third of them: a reversed negation, 南宮深 written "Nanguang" (it is Nangong Shen), 阿活 written "Ahu" (A'Huo), "Lefteous Guardian" (左護法 is the Left Guardian), "it's own", an unresolved "Tang Da Jing or Tang the Great Whale" (唐大鯨 is Tang Dajing), and names that differ from the established English in the string table. Your job is to find errors of that kind and leave good lines alone.
Binding terminology: ${LOC}/glossary/conventions.md (read it first; the owner's rulings at its end are binding and override older lines above them, e.g. 護法 = Guardian, 極招 = Ultimate, 內力 = Internal Force, 師叔 = Martial Uncle, 小師妹 = Little Junior Sister, 大俠 = Great Hero, 唐門 = Tang Clan, 唐掌門 = Sect Leader Tang) and ${LOC}/glossary/rulings_2026-09-12.md.
Names: every person, place, sect, item, book, technique and skill must match its established English in the string table. Each line's "names" lists the ones found in it ([Chinese, English, key]). For any other name, look it up: Grep ${LOC}/base_ref/game_source.tsv (lines are key TAB Chinese) with the pattern "\\t<Chinese>$" to find the row that is exactly that name, then Grep ${LOC}/workspace/translation/StringTable.csv (rows are key,"English") with "^<key>," for its English. A name with no row: pinyin, surname first, given-name syllables joined (Nangong Shen, Liu Zixu), 阿X = A'X, X兒 = X'er.`
const KINDS = `Line kinds (each line has "kind"; its context fields are hints, not text to translate):
- dice-header: the title of a dice-check panel (the game appends "......"). ref gives the strings override players actually see for it (and the other keys with the same header); "before" gives the story lines just before the check. The line is the override's fallback: it must say what ref.en says, as a short heading: no trailing period (a question keeps its "?").
- table-variant: a row of the string table without its markup ({size=24}...{/size}) or with other line breaks; ref is that row (its en is the reviewed English players see), neighbours are the rows before and after it. The line must say what ref.en says (meaning, names, terms), without markup. Keep the line's own wording when it says the same thing correctly.
- table-fragment: the part of a row between two rich-text tags; follow the matching part of ref.en.
- ui: an interface label; "where" is the object path. CreditPanel is the credits: a real name is pinyin, surname first, given name joined (Liu Zixu); a handle or nickname (no common surname, e.g. 密瓜瓜, 柳丁, 熊) is romanized as one joined word, not translated (Miguagua, Liuding, Xiong); companies as they call themselves in English. Labels stay short; no trailing period unless the Chinese is a sentence.
- data, composed: text in a game data asset, or built by the game at run time (numbers, stat changes, tooltips). Dates follow the game's own English: 第X年Y月Z旬 is "Year X, Early/Mid/Late <Month>" (第一年四月上旬 = "Year One, Early April"); Y月‧Z旬 is "<Month>‧Early" (四月‧中旬 = "April‧Mid"). Keep numbers as they are.`
const FORMAT = `Format of zh and en: as written in the scene file. A backslash followed by n is a line break, a backslash followed by = is "=", a double backslash is one backslash. Write a new en in the same form: backslash-n for each line break (the same number as the current en or the zh), never a real line break, never "//"; keep every rich-text tag (<color=#...>...</color>) and placeholder ({0}, {0:N0}) exactly; no Chinese characters; no notes or brackets that are not in the Chinese.`
function reviewPrompt(c, only) {
  return `${CONTEXT}
${KINDS}
${FORMAT}
Task: audit chunk ${c.chunk}. Read ${file(c.chunk)}. Each line has id, kind, zh (the Chinese, authoritative), en (the current English) and context (ref, neighbours, before, where, names).${only ? ` Only these ids are yours this time (the others were already answered): ${only.join(', ')}.` : ''}
For every line decide keep=true, or keep=false with the full corrected en. Rewrite only for a real error:
1. meaning: the English says something the Chinese does not (a reversed or dropped negation, the wrong subject, speaker or addressee, a misread idiom, a dropped or added clause);
2. names and terms: a name or title that differs from its established English (names, ref, your lookups) or breaks the glossary;
3. consistency: a line with ref that differs from ref.en in meaning, names or terms;
4. form: a typo or misspelling, an unresolved alternative ("X or Y" for one Chinese thing), a leftover note, Chinese characters, a broken name form (Lin RongSheng, Dai Wen Kai), a trailing period on a label the Chinese does not end with one, a format break.
Do not rewrite a line that is correct just to restyle it. A rewrite keeps the length close to the current English (labels at most about 1.2 times it).
Output: every id${only ? ' listed above' : ' in the chunk'} exactly once: keep, en (only when keep=false: the full new line in the file form), why (only when keep=false, at most 15 words naming the error), ref_issue (optional, at most 20 words, only when you believe ref.en itself is wrong: it is reported to the maintainer, not changed here).`
}
function verifyPrompt(c, props) {
  return `${CONTEXT}
${KINDS}
${FORMAT}
Task: adversarially verify the proposed rewrites for chunk ${c.chunk}. The chunk (Chinese, current English, ref, names) is ${file(c.chunk)}; match by id. Proposals below are [{id, new, why}]. Try to refute each one: the old line had no real error (a style-only change is rejected); the new line says something the Chinese does not or drops something it says; it changes who speaks or is addressed; it gets a name or term wrong (check a changed name yourself in ${LOC}/base_ref/game_source.tsv and ${LOC}/workspace/translation/StringTable.csv); it contradicts ref.en; it loses or adds a line break, tag or placeholder; it contains Chinese; it is too long for a label. Accept only rewrites that fix a real error without adding one. One verdict per id; reason at most 15 words when rejecting.
Proposals (JSON):
${JSON.stringify(props)}`
}
phase('Review')
const results = await pipeline(LIST,
  async (c) => {
    const r = await ag(reviewPrompt(c, null), { label: `review:${c.chunk}`, phase: 'Review', schema: EDIT_SCHEMA, model: 'sonnet', effort: 'high' })
    const items = ((r && r.items) || []).filter(x => c.ids.includes(x.id))
    const missing = c.ids.filter(id => !items.some(x => x.id === id))
    if (missing.length) {
      log(`review:${c.chunk} left out ${missing.length} of ${c.ids.length}; asking again for those`)
      const r2 = await ag(reviewPrompt(c, missing), { label: `review-rest:${c.chunk}`, phase: 'Review', schema: EDIT_SCHEMA, model: 'sonnet', effort: 'high' })
      for (const x of ((r2 && r2.items) || [])) { if (missing.includes(x.id) && !items.some(y => y.id === x.id)) { items.push(x) } }
    }
    return items
  },
  async (items, c) => {
    const fixes = items.filter(x => x.keep === false && x.en).map(x => ({ id: x.id, new: x.en, why: x.why || '' }))
    const answered = items.length
    if (!fixes.length) { return { chunk: c.chunk, answered, fixes: 0, accepted: 0 } }
    const v = await ag(verifyPrompt(c, fixes), { label: `verify:${c.chunk}`, phase: 'Verify', schema: VERDICT_SCHEMA, model: 'sonnet', effort: 'high' })
    const vs = (v && v.verdicts) || []
    const accepted = fixes.filter(f => vs.some(x => x.id === f.id && x.accept) && !vs.some(x => x.id === f.id && !x.accept)).length
    return { chunk: c.chunk, answered, fixes: fixes.length, accepted, unverified: fixes.filter(f => !vs.some(x => x.id === f.id)).length }
  })
const ok = results.filter(Boolean)
const total = LIST.reduce((n, c) => n + c.ids.length, 0)
const answered = ok.reduce((n, r) => n + r.answered, 0)
const proposed = ok.reduce((n, r) => n + r.fixes, 0)
const accepted = ok.reduce((n, r) => n + r.accepted, 0)
log(`Answered ${answered}/${total} lines in ${ok.length}/${LIST.length} chunks; ${proposed} rewrites proposed, ${accepted} accepted`)
// The per-line decisions are in the journal (review:N, review-rest:N, verify:N); apply_audit.py reads them from there.
return { chunks: LIST.length, chunksOk: ok.length, lines: total, answered, proposed, accepted,
  unanswered: LIST.filter(c => { const r = ok.find(x => x.chunk === c.chunk); return !r || r.answered < c.ids.length }).map(c => c.chunk),
  unverified: ok.filter(r => r.unverified).map(r => r.chunk) }
