# Walk every Fungus Lua story script and record each player choice menu with its context:
# the lines said just before the menu (speaker + zh + en), the options (zh + en), and the first lines each option leads to.
# Usage: python extract_choices.py <lua dir> <out json>
import os, io, re, sys, csv, json, collections
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')); import lom_paths
LUA, OUT = sys.argv[1], sys.argv[2]
SCR = os.path.dirname(os.path.abspath(__file__))
en = {r[0]: r[1] for r in csv.reader(io.open(lom_paths.TABLE, encoding='utf-8-sig', newline='')) if len(r) >= 2}
zh = {r[0]: r[1] for r in csv.reader(io.open(os.path.join(SCR, '..', 'overllm', 'Files_Raw_StringTable.csv'), encoding='utf-8-sig', newline='')) if len(r) >= 2}
def txt(key):
    k = 'Story/' + key
    return zh.get(k, ''), en.get(k, '')
def name(ck):
    k = 'Character/' + ck
    return {'key': ck, 'zh': zh.get(k, ''), 'en': en.get(k, '')}

RX_SAYDLG = re.compile(r'setsaydialog\(saydialogs\.(\w+)\)')
RX_CHAR = re.compile(r'setcharacter\(characters\.Get\("([^"]+)"\)')
RX_SAY = re.compile(r'say\(luamanager\.GetStoryText\("([^"]+)"\)')
RX_OPT = re.compile(r'^\s*(\w+)\[(\d+)\]\s*=\s*(.+)$')
RX_OKEY = re.compile(r'"(?:[a-z]+\+)?(O_[A-Za-z0-9_]+)(?:\|([^"]*))?(?:\+[^"]*)?"')
RX_CHOOSE = re.compile(r'choose\((\w+)\)')
RX_ROLL = re.compile(r'ExecuteRoll\((\w+),')
RX_INDEX = re.compile(r'--index:(O_[A-Za-z0-9_]+)')
RX_NEXT = re.compile(r'SetNextScript\("([^"]+)"')
RX_MENUDLG = re.compile(r'setmenudialog\(menudialogs\.(\w+)\)')

scripts = {}
for fn in sorted(os.listdir(LUA)):
    if not fn.endswith('.lua'): continue   # duplicates (Simplified copies) were written to lua_dupes/ by the extraction step
    scripts[fn[:-4]] = io.open(os.path.join(LUA, fn), encoding='utf-8').read().split('\n')

def events_of(lines):
    """Sequential list of say events and menu markers in one script."""
    ev = []; kind = 'character'; speaker = None; tables = collections.OrderedDict(); menudlg = None
    for i, line in enumerate(lines):
        m = RX_SAYDLG.search(line)
        if m: kind = m.group(1); continue
        m = RX_CHAR.search(line)
        if m: speaker = m.group(1); continue
        m = RX_MENUDLG.search(line)
        if m: menudlg = m.group(1); continue
        m = RX_SAY.search(line)
        if m: ev.append({'t': 'say', 'i': i, 'key': m.group(1), 'kind': kind, 'speaker': speaker if kind == 'character' else None}); continue
        m = RX_OPT.match(line)
        if m and 'O_' in m.group(3):
            ok = RX_OKEY.search(m.group(3))
            if ok:
                tables.setdefault(m.group(1), []).append({'key': ok.group(1), 'cond': ok.group(2) or '', 'conditional': ' and ' in m.group(3)})
            continue
        m = RX_CHOOSE.search(line) or RX_ROLL.search(line)
        if m:
            var = m.group(1); opts = tables.pop(var, [])
            if opts: ev.append({'t': 'menu', 'i': i, 'var': var, 'menu': menudlg or ('Dice' if 'ExecuteRoll' in line else 'Options'), 'dice': 'ExecuteRoll' in line, 'opts': opts})
            continue
        m = RX_INDEX.search(line)
        if m: ev.append({'t': 'index', 'i': i, 'key': m.group(1)}); continue
        m = RX_NEXT.search(line)
        if m: ev.append({'t': 'next', 'i': i, 'script': m.group(1)}); continue
    return ev

allev = {s: events_of(l) for s, l in scripts.items()}
callers = collections.defaultdict(list)
for s, ev in allev.items():
    for e in ev:
        if e['t'] == 'next': callers[e['script']].append(s)

def say_row(e):
    z, g = txt(e['key'])
    row = {'key': e['key'], 'kind': e['kind'], 'zh': z, 'en': g}
    if e['speaker']: row['speaker'] = name(e['speaker'])
    return row

groups = []; N_CTX = 6
for s, ev in allev.items():
    says_before = []
    for idx, e in enumerate(ev):
        if e['t'] == 'say': says_before.append(e)
        if e['t'] != 'menu': continue
        ctx = [say_row(x) for x in says_before[-N_CTX:]]
        ctx_src = s
        if len(ctx) < 2 and callers.get(s):
            c = callers[s][0]
            tail = [x for x in allev[c] if x['t'] == 'say'][-N_CTX:]
            ctx = [say_row(x) for x in tail] + ctx; ctx_src = c + ' -> ' + s
        opts = []
        for o in e['opts']:
            z, g = txt(o['key'])
            # response: first says after the branch marker for this option, before the next marker
            resp = []
            for j in range(idx + 1, len(ev)):
                if ev[j]['t'] == 'index' and ev[j]['key'] == o['key']:
                    for k in range(j + 1, len(ev)):
                        if ev[k]['t'] in ('index', 'menu'): break
                        if ev[k]['t'] == 'say': resp.append(say_row(ev[k]))
                        if len(resp) >= 2: break
                    break
            opts.append({'key': o['key'], 'zh': z, 'en': g, 'cond': o['cond'], 'conditional': o['conditional'], 'response': resp})
        groups.append({'id': '%s#%s' % (s, e['var']), 'script': s, 'menu': e['menu'], 'dice': e['dice'], 'contextFrom': ctx_src, 'context': ctx, 'options': opts})

json.dump(groups, io.open(OUT, 'w', encoding='utf-8', newline='\n'), ensure_ascii=False, indent=0)
n_opt = sum(len(g['options']) for g in groups); keys = set(o['key'] for g in groups for o in g['options'])
print('scripts', len(scripts), 'menus', len(groups), 'options', n_opt, 'distinct option keys', len(keys),
      'menus with no context', sum(1 for g in groups if not g['context']), 'options without en', sum(1 for g in groups for o in g['options'] if not o['en']),
      'menu kinds', dict(collections.Counter(g['menu'] for g in groups)))
