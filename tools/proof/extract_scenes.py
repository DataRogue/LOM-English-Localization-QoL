# Build proofreading chunks for given chapters: every ch<N>_* Lua script walked in order (say lines with speaker and dialog kind,
# menu options as read-only flow markers), cut into windows of PER editable lines with CTX preceding lines as context.
# LegendInfo/Ch_<N>_* chronicle entries (not in scripts) are chunked by key order. Usage: python extract_scenes.py <lua dir> <out dir> <chapters e.g. 1,2>
import os, io, re, sys, csv, json, collections
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')); import lom_paths
LUA, OUT, CHAPTERS = sys.argv[1], sys.argv[2], [c.strip() for c in sys.argv[3].split(',')]
os.makedirs(OUT, exist_ok=True)
for f in os.listdir(OUT): os.remove(os.path.join(OUT, f))
SCR = os.path.dirname(os.path.abspath(__file__))
en = {r[0]: r[1] for r in csv.reader(io.open(lom_paths.TABLE, encoding='utf-8-sig', newline='')) if len(r) >= 2}
zh = {r[0]: r[1] for r in csv.reader(io.open(os.path.join(SCR, '..', 'overllm', 'Files_Raw_StringTable.csv'), encoding='utf-8-sig', newline='')) if len(r) >= 2}
def name(ck):
    k = 'Character/' + ck; e = en.get(k, '').strip(); z = zh.get(k, '').strip()
    return e or z or ck
RX_SAYDLG = re.compile(r'setsaydialog\(saydialogs\.(\w+)\)')
RX_CHAR = re.compile(r'setcharacter\(characters\.Get\("([^"]+)"\)')
RX_SAY = re.compile(r'say\(luamanager\.GetStoryText\("([^"]+)"\)')
RX_OPT = re.compile(r'^\s*(\w+)\[(\d+)\]\s*=\s*(.+)$')
RX_OKEY = re.compile(r'"(?:[a-z]+\+)?(O_[A-Za-z0-9_]+)(?:\|([^"]*))?(?:\+[^"]*)?"')
RX_CHOOSE = re.compile(r'choose\((\w+)\)|ExecuteRoll\((\w+),')
RX_INDEX = re.compile(r'--index:(O_[A-Za-z0-9_]+)')
def walk(lines):
    ev = []; kind = 'character'; speaker = None; tables = collections.OrderedDict()
    for line in lines:
        m = RX_SAYDLG.search(line)
        if m: kind = m.group(1); continue
        m = RX_CHAR.search(line)
        if m: speaker = m.group(1); continue
        m = RX_SAY.search(line)
        if m: ev.append({'t': 'say', 'key': m.group(1), 'kind': kind, 'speaker': speaker if kind == 'character' else None}); continue
        m = RX_OPT.match(line)
        if m and 'O_' in m.group(3):
            ok = RX_OKEY.search(m.group(3))
            if ok: tables.setdefault(m.group(1), []).append(ok.group(1))
            continue
        m = RX_CHOOSE.search(line)
        if m:
            var = m.group(1) or m.group(2); opts = tables.pop(var, [])
            if opts: ev.append({'t': 'menu', 'opts': opts})
            continue
        m = RX_INDEX.search(line)
        if m: ev.append({'t': 'index', 'key': m.group(1)})
    return ev
PER, CTX = 35, 5
chunks = []; seen_keys = set(); stats = collections.Counter()
scripts = sorted(fn[:-4] for fn in os.listdir(LUA) if fn.endswith('.lua') and any(re.match(r'ch%s_' % c, fn) for c in CHAPTERS))
for s in scripts:
    ev = walk(io.open(os.path.join(LUA, s + '.lua'), encoding='utf-8').read().split('\n'))
    seq = []
    for e in ev:
        if e['t'] == 'say':
            k = 'Story/' + e['key']
            if k not in en: stats['say without table row'] += 1; continue
            dup = k in seen_keys; seen_keys.add(k)
            seq.append({'id': 'table|' + k, 'key': e['key'], 'kind': e['kind'], 'speaker': name(e['speaker']) if e['speaker'] else None, 'zh': zh.get(k, ''), 'en': en[k], 'editable': not dup})
            stats['duplicate line (context only)' if dup else 'editable line'] += 1
        elif e['t'] == 'menu':
            seq.append({'id': 'menu', 'kind': 'choice', 'options': [{'key': o, 'zh': zh.get('Story/' + o, ''), 'en': en.get('Story/' + o, '')} for o in e['opts']], 'editable': False})
        elif e['t'] == 'index':
            seq.append({'id': 'branch', 'kind': 'branch', 'option': e['key'], 'en': en.get('Story/' + e['key'], ''), 'editable': False})
    # windows of PER editable lines, keeping the flow markers in between
    i = 0; n = 0
    while i < len(seq):
        j = i; count = 0
        while j < len(seq) and count < PER:
            if seq[j].get('editable'): count += 1
            j += 1
        n += 1
        chunks.append({'script': s, 'part': n, 'context': [dict(x, editable=False) for x in seq[max(0, i - CTX):i]], 'lines': seq[i:j]})
        i = j
# chronicle entries
li = sorted(k for k in en if any(re.match(r'LegendInfo/Ch_%s_' % c, k) for c in CHAPTERS))
for i in range(0, len(li), 20):
    chunks.append({'script': 'LegendInfo', 'part': i // 20 + 1, 'context': [], 'lines': [{'id': 'table|' + k, 'key': k, 'kind': 'chronicle', 'speaker': None, 'zh': zh.get(k, ''), 'en': en[k], 'editable': True} for k in li[i:i + 20]]})
    stats['editable line'] += len(li[i:i + 20])
for n, ch in enumerate(chunks, 1):
    json.dump(ch, io.open(os.path.join(OUT, 'p_%03d.json' % n), 'w', encoding='utf-8', newline='\n'), ensure_ascii=False, indent=1)
print(json.dumps({'scripts': len(scripts), 'chunks': len(chunks), 'chronicle': len(li), 'stats': dict(stats)}, ensure_ascii=False))
