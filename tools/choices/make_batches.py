# Split the extracted choice menus into review batches (m_NNN.json) and the table's unreferenced option keys into u_NNN.json.
# Usage: python make_batches.py <choice_groups.json> <batches dir>
import json, io, os, sys, csv, re
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')); import lom_paths
SRC, OUT = sys.argv[1], sys.argv[2]
SCR = os.path.dirname(os.path.abspath(__file__))
os.makedirs(OUT, exist_ok=True)
for f in os.listdir(OUT): os.remove(os.path.join(OUT, f))
groups = json.load(io.open(SRC, encoding='utf-8'))
def slim_line(l):
    d = {'kind': l['kind'], 'zh': l['zh'], 'en': l['en']}
    if l.get('speaker'): d['speaker'] = l['speaker']['en'] or l['speaker']['zh'] or l['speaker']['key']
    return d
def slim(g):
    return {'id': g['id'], 'script': g['script'], 'menu': g['menu'], 'dice': g['dice'],
            'context': [slim_line(l) for l in g['context']],
            'options': [{'key': o['key'], 'zh': o['zh'], 'en': o['en'], 'cond': o['cond'], 'leadsTo': [slim_line(r) for r in o['response']]} for o in g['options']]}
PER = 12
menus = [slim(g) for g in groups]
n_m = 0
for i in range(0, len(menus), PER):
    n_m += 1
    json.dump(menus[i:i + PER], io.open(os.path.join(OUT, 'm_%03d.json' % n_m), 'w', encoding='utf-8', newline='\n'), ensure_ascii=False, indent=1)
# unreferenced option keys: no script context, judge on the Chinese alone
en = {r[0]: r[1] for r in csv.reader(io.open(lom_paths.TABLE, encoding='utf-8-sig', newline='')) if len(r) >= 2}
zh = {r[0]: r[1] for r in csv.reader(io.open(os.path.join(SCR, '..', 'overllm', 'Files_Raw_StringTable.csv'), encoding='utf-8-sig', newline='')) if len(r) >= 2}
used = set(o['key'] for g in groups for o in g['options'])
unref = [{'key': k[6:], 'zh': zh.get(k, ''), 'en': en[k]} for k in sorted(en) if k.startswith('Story/O_') and k[6:] not in used]
# group siblings (same stem) together so the reviewer sees the whole menu when it is one
unref.sort(key=lambda x: (re.sub(r'_\d+$', '', x['key']), x['key']))
PER_U = 40; n_u = 0
for i in range(0, len(unref), PER_U):
    n_u += 1
    json.dump(unref[i:i + PER_U], io.open(os.path.join(OUT, 'u_%03d.json' % n_u), 'w', encoding='utf-8', newline='\n'), ensure_ascii=False, indent=1)
print(json.dumps({'menuBatches': n_m, 'menus': len(menus), 'options': sum(len(m['options']) for m in menus), 'unrefBatches': n_u, 'unref': len(unref)}))
