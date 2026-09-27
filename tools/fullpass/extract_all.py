"""Full review pass, step 1: cut every line not yet reviewed line by line into context chunks for the Sonnet review.

Categories (each chunk has at most PER editable lines; already-reviewed or repeated lines appear as read-only context):
  script     every Lua story script (LOM_Localization/gamedata/lua) walked in order: say lines with speaker and dialog kind,
             menus and branch markers as read-only flow, CTX preceding lines as context
  unref      Story/* rows no script references, in key order (neighbouring keys are the same scene), CTX preceding keys
  chronicle  LegendInfo/* entries in key order (chapter summaries)
  data       every other table family, grouped per entity (CombatSkill/Name/X next to CombatSkill/Desc/X), key order
  scene      scene-only lines (XUnity-format lines whose Chinese is no table row even with CRLF normalised), with the
             screen path and box size from the UI dumps where known
Scene lines whose Chinese equals a table row are not chunked: apply_fullpass.py gives them the reviewed table line.
Usage: python extract_all.py [--per 35] [--out DIR]   (writes DIR/p_NNNN.json and DIR/manifest.json)
"""
import os, io, re, sys, csv, json, glob, argparse, collections
sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..'))
import lom_paths
from migrate_standalone import table_map

BS = chr(92)
LOC = lom_paths.REAL_LOCALIZATION        # read-only inputs (gamedata, reviewed_before.json); output goes to lom_paths.LOCALIZATION
LUA = os.path.join(LOC, 'gamedata', 'lua')
if not glob.glob(os.path.join(LUA, '*.lua')):
    sys.exit('no Lua scripts in %s' % LUA)
# the 2026-09-12/13 session scratchpad that held the chapter 1-3 proofread chunks and the choice-pass batches
OLD = os.path.join(os.environ.get('TEMP', ''), 'claude', 'C--Program-Files--x86--Steam-steamapps-common-LegendOfMortal',
                   '18b21701-065b-49cd-bccf-11e91c11579b', 'scratchpad')
UI_DUMPS = glob.glob(os.path.join(LOC, 'gamedata', 'ui', '*_ui.jsonl')) or glob.glob(os.path.join(OLD, 'ui', '*_ui.jsonl'))
ap = argparse.ArgumentParser()
ap.add_argument('--per', type=int, default=35)
ap.add_argument('--out', default=os.path.join(lom_paths.LOCALIZATION, 'fullpass', 'chunks'))
a = ap.parse_args()
PER, CTX, PER_CHRON, PER_SCENE = a.per, 5, 20, 40
os.makedirs(a.out, exist_ok=True)
for f in glob.glob(os.path.join(a.out, 'p_*.json')):
    os.remove(f)

en = table_map(open(lom_paths.TABLE, encoding='utf-8-sig').read(), True)
zh = {}
for line in open(lom_paths.BASE_REF_SOURCE, encoding='utf-8'):
    k, _, v = line.rstrip('\n').partition('\t')
    zh[k] = v.replace(BS + 'r' + BS + 'n', '\n').replace(BS + 'n', '\n').replace(BS + 'r', '').replace(BS + 't', '\t').replace(BS + BS, BS)
skip_key = lambda k: '/' not in k and (k == 'TextFont' or k.startswith('Image_') or k.startswith('TextMeshFont_'))

# ---- lines already reviewed line by line (chapters 1-3 proofread, choice pass) -------------------------------------------
reviewed = set()
for d in (os.path.join(OLD, 'proof', 'chunks'), os.path.join(OLD, 'proof', 'chunks3')):
    for f in glob.glob(os.path.join(d, 'p_*.json')):
        for ln in json.load(open(f, encoding='utf-8')).get('lines', []):
            if ln.get('editable') and str(ln.get('id', '')).startswith('table|'):
                reviewed.add(ln['id'][6:])
for f in glob.glob(os.path.join(OLD, 'choices', 'batches', '*.json')):
    if re.search(r'[mu]_\d{3}\.json$', f):
        for k in re.findall(r'"(O_[A-Za-z0-9_]+)"', open(f, encoding='utf-8').read()):
            reviewed.add('Story/' + k)
RB = os.path.join(LOC, 'fullpass', 'reviewed_before.json')
if not os.path.exists(RB):
    if lom_paths.STAGE is not None:
        sys.exit('%s is missing; a staged run does not write into LOM_Localization: create it with a run outside stage mode' % RB)
    json.dump(sorted(reviewed), open(RB, 'w', encoding='utf-8'), ensure_ascii=False)
# lines the full pass reviewed since (the workspace ledger): a later run chunks only what is still unreviewed
import ledger
_led_t, _led_s = ledger.load()
reviewed.update(_led_t)
ledger_scene = set(_led_s)   # raw scene keys (ledger.scene_key of the line as written)

taken = set()  # keys already editable in an earlier chunk


def editable(k):
    return k in en and k not in reviewed and k not in taken and not skip_key(k) and bool(zh.get(k))


def line_for(k, kind, speaker=None, extra=None):
    ed = editable(k)
    if ed:
        taken.add(k)
    d = {'id': 'table|' + k, 'key': k, 'kind': kind, 'speaker': speaker, 'zh': zh.get(k, ''), 'en': en.get(k, ''), 'editable': ed}
    if extra:
        d.update(extra)
    return d


def name(ck):
    e = en.get('Character/' + ck, '').strip()
    return e or zh.get('Character/' + ck, '').strip() or ck


chunks = []


def cut(seq, script, category, per=PER):
    i = 0
    n = 0
    while i < len(seq):
        j = i
        count = 0
        while j < len(seq) and count < per:
            if seq[j].get('editable'):
                count += 1
            j += 1
        if count:
            n += 1
            chunks.append({'category': category, 'script': script, 'part': n, 'context': [dict(x, editable=False) for x in seq[max(0, i - CTX):i]], 'lines': seq[i:j]})
        i = j


# ---- 1. story scripts ------------------------------------------------------------------------------------------------
RX_SAYDLG = re.compile(r'setsaydialog\(saydialogs\.(\w+)\)')
RX_CHAR = re.compile(r'setcharacter\(characters\.Get\("([^"]+)"\)')
RX_SAY = re.compile(r'say\(luamanager\.GetStoryText\("([^"]+)"\)')
RX_OPT = re.compile(r'^\s*(\w+)\[(\d+)\]\s*=\s*(.+)$')
RX_OKEY = re.compile(r'"(?:[a-z]+\+)?(O_[A-Za-z0-9_]+)(?:\|([^"]*))?(?:\+[^"]*)?"')
RX_CHOOSE = re.compile(r'choose\((\w+)\)|ExecuteRoll\((\w+),')
RX_INDEX = re.compile(r'--index:(O_[A-Za-z0-9_]+)')
RX_ANYKEY = re.compile(r'GetStoryText\("([^"]+)"\)')
referenced = set()
scripts = sorted(os.path.basename(f)[:-4] for f in glob.glob(os.path.join(LUA, '*.lua')))
for s in scripts:
    text = io.open(os.path.join(LUA, s + '.lua'), encoding='utf-8').read()
    for k in RX_ANYKEY.findall(text):
        referenced.add('Story/' + k)
    kind = 'character'
    speaker = None
    tables = collections.OrderedDict()
    seq = []
    for line in text.split('\n'):
        m = RX_SAYDLG.search(line)
        if m:
            kind = m.group(1)
            continue
        m = RX_CHAR.search(line)
        if m:
            speaker = m.group(1)
            continue
        m = RX_SAY.search(line)
        if m:
            k = 'Story/' + m.group(1)
            if k in en:
                seq.append(line_for(k, kind, name(speaker) if (kind == 'character' and speaker) else None))
            continue
        m = RX_OPT.match(line)
        if m and 'O_' in m.group(3):
            ok = RX_OKEY.search(m.group(3))
            if ok:
                tables.setdefault(m.group(1), []).append(ok.group(1))
            continue
        m = RX_CHOOSE.search(line)
        if m:
            var = m.group(1) or m.group(2)
            opts = tables.pop(var, [])
            if opts:
                seq.append({'id': 'menu', 'kind': 'choice', 'options': [{'key': o, 'zh': zh.get('Story/' + o, ''), 'en': en.get('Story/' + o, '')} for o in opts], 'editable': False})
            continue
        m = RX_INDEX.search(line)
        if m:
            seq.append({'id': 'branch', 'kind': 'branch', 'option': m.group(1), 'en': en.get('Story/' + m.group(1), ''), 'editable': False})
    cut(seq, s, 'script')

# ---- 2. story rows no script references, key order ----------------------------------------------------------------------
story = sorted(k for k in en if k.startswith('Story/') and k not in referenced)
seq = []
for k in story:
    kind = 'choice' if k.startswith('Story/O_') else ('think' if k.startswith('Story/T_') else ('narrative' if k.startswith('Story/N_') else 'line'))
    seq.append(line_for(k, kind))
cut(seq, 'Story rows no script references (key order: neighbouring keys are the same scene)', 'unref')

# ---- 3. chronicle ------------------------------------------------------------------------------------------------------
seq = [line_for(k, 'chronicle') for k in sorted(k for k in en if k.startswith('LegendInfo/'))]
cut(seq, 'LegendInfo (chronicle entries the player reads later, in key order)', 'chronicle', PER_CHRON)

# ---- 4. every other table family, per entity -----------------------------------------------------------------------------
fam = collections.defaultdict(list)
for k in en:
    if skip_key(k) or k.startswith('Story/') or k.startswith('LegendInfo/'):
        continue
    parts = k.split('/')
    fam[parts[0]].append(k)


def entity_key(k):
    parts = k.split('/')
    if len(parts) >= 3:
        return (parts[-1], '/'.join(parts[1:-1]))
    return (parts[-1] if len(parts) > 1 else k, '')


for f in sorted(fam):
    keys = sorted(fam[f], key=entity_key)
    seq = [line_for(k, 'data') for k in keys]
    cut(seq, 'table family ' + f + ' (rows of one entity next to each other)', 'data')

# ---- 5. scene-only lines -----------------------------------------------------------------------------------------------
def split(line):
    i = 0
    while True:
        i = line.find('=', i)
        if i < 0:
            return None
        if i > 0 and line[i - 1] == BS:
            i += 1
            continue
        return line[:i], line[i + 1:]


zh_keys = set(v.replace('\r\n', '\n').replace('\n', BS + 'n') for v in zh.values())
ui = {}
for f in UI_DUMPS:
    for line in open(f, encoding='utf-8'):
        try:
            o = json.loads(line)
        except ValueError:
            continue
        for c in o.get('comps', []):
            t = c.get('text')
            if t:
                ui.setdefault(t.replace('\r\n', '\n').replace('\n', BS + 'n'), {'screen': os.path.basename(f)[:-9], 'path': o.get('path'), 'box': [o.get('w'), o.get('h')], 'size': c.get('size')})
scene_only = []
for line in open(lom_paths.SCENE, encoding='utf-8-sig'):
    kv = split(line.rstrip('\r\n'))
    if kv and kv[0] and kv[1] and kv[0] not in zh_keys and ledger.scene_key(line.rstrip('\r\n')) not in ledger_scene:
        scene_only.append(kv)
scene_only.sort(key=lambda kv: ((ui.get(kv[0]) or {}).get('path') or '~', len(kv[0]), kv[0]))
seq = []
for z, e in scene_only:
    d = {'id': 'scene|' + z, 'key': None, 'kind': 'scene', 'speaker': None, 'zh': z, 'en': e, 'editable': True}
    if z in ui:
        d['ui'] = ui[z]
    seq.append(d)
cut(seq, 'scene-only lines (text the game draws that is no table row: labels, credits, key help, old or Simplified-Chinese strings, number junk)', 'scene', PER_SCENE)

# ---- merge small consecutive chunks of one category and script family (a scene-break marker between them) -----------------
def fam_of(ch):
    return ch['category'] + ':' + (re.sub(r'_.*', '', ch['script']) if ch['category'] == 'script' else ch['script'])


def n_edit(ch):
    return sum(1 for x in ch['lines'] if x.get('editable'))


merged = []
for ch in chunks:
    prev = merged[-1] if merged else None
    if prev is not None and fam_of(prev) == fam_of(ch) and n_edit(prev) + n_edit(ch) <= PER:
        prev['lines'] = prev['lines'] + [{'id': 'break', 'kind': 'break', 'script': ch['script'], 'note': 'a different scene starts here; the lines below are its preceding context, then its lines', 'editable': False}] + [dict(x, editable=False) for x in ch['context']] + ch['lines']
        prev['scripts'].append(ch['script'])
    else:
        ch = dict(ch)
        ch['scripts'] = [ch['script']]
        merged.append(ch)
chunks = merged

manifest = []
for n, ch in enumerate(chunks, 1):
    ch['chunk'] = n
    json.dump(ch, io.open(os.path.join(a.out, 'p_%04d.json' % n), 'w', encoding='utf-8', newline='\n'), ensure_ascii=False, indent=1)
    manifest.append({'chunk': n, 'category': ch['category'], 'script': ch['script'], 'scripts': len(ch.get('scripts', [])), 'part': ch['part'], 'editable': sum(1 for x in ch['lines'] if x.get('editable'))})
json.dump(manifest, open(os.path.join(a.out, 'manifest.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=0)
by = collections.Counter()
lines = collections.Counter()
for m in manifest:
    by[m['category']] += 1
    lines[m['category']] += m['editable']
remaining = [k for k in en if not skip_key(k) and k not in reviewed and k not in taken and zh.get(k)]
print(json.dumps({'chunks': len(chunks), 'by_category': dict(by), 'editable_lines': dict(lines), 'total_editable': sum(lines.values()),
                  'reviewed_before': len(reviewed), 'table_rows_not_chunked': len(remaining), 'examples_not_chunked': remaining[:5],
                  'scene_only': len(scene_only), 'scene_with_ui_context': sum(1 for z, e in scene_only if z in ui)}, ensure_ascii=False, indent=1))
