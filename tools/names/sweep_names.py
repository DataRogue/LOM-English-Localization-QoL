# Find lines where a character's bare given name (e.g. 默鈴 of 唐默鈴) or full name appears in the Chinese but the English
# does not carry the settled romanization: the base 7B-model patch translated such names as words ("Mermaid Bell").
# Scans the string table (Story/LegendInfo/CombatTalking/CharacterIntro) and the XUnity file; writes review batches.
# Usage: python sweep_names.py <out dir>
import csv, io, os, re, sys, json, collections
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')); import lom_paths
OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
for f in os.listdir(OUT): os.remove(os.path.join(OUT, f))
SCR = os.path.dirname(os.path.abspath(__file__))
zh = {r[0]: r[1] for r in csv.reader(io.open(os.path.join(SCR, '..', 'overllm', 'Files_Raw_StringTable.csv'), encoding='utf-8-sig', newline='')) if len(r) >= 2}
en = {r[0]: r[1] for r in csv.reader(io.open(lom_paths.TABLE, encoding='utf-8-sig', newline='')) if len(r) >= 2}
STOP = set('lord lady doctor man person master elder sect chief young old boss aunt uncle grandma grandpa sir madam miss monk nun daoist general captain guard servant maid shopkeeper innkeeper waiter boy girl kid child beggar soldier villager merchant priest abbot king prince princess queen emperor envoy officer magistrate constable leader head sedan masked mysterious unknown voice in the of chair brother sister junior senior little big second third fourth fifth sixth eldest mother father son daughter wife husband grandmother grandfather mrs mr steward butler cook chef nurse teacher scholar swordsman hero heroine bandit thief pirate ghost spirit god goddess immortal fairy guildmaster fire lightning'.split())
names = {}
for k in zh:
    if k.startswith('Character/') and k.count('/') == 1:
        z = zh[k].strip(); e = en.get(k, '').strip()
        if len(z) == 3 and re.fullmatch(r'[一-鿿]+', z) and e and not re.search(r'[一-鿿]', e):
            toks = e.replace('-', ' ').replace("'", '').split()
            if len(toks) != 2 or any(t.lower() in STOP for t in toks) or not toks[1][0].isupper(): continue
            names.setdefault(z, {'key': k[10:], 'en': e, 'given': z[1:], 'givenEn': toks[1], 'surnameEn': toks[0]})
def variants(tok):
    t = tok.lower(); out = {t}
    for i in range(2, len(t) - 1): out.add(t[:i] + ' ' + t[i:]); out.add(t[:i] + '-' + t[i:])
    return out
lines = []   # (id, zh, en)
for k in zh:
    if k.split('/')[0] in ('Story', 'LegendInfo', 'CombatTalking', 'CharacterIntro0', 'CharacterIntro1', 'CharacterIntro2', 'CharacterIntro3', 'CharacterIntro4') and k in en:
        lines.append(('table|' + k, zh[k], en[k]))
xu_path = lom_paths.SCENE
for i, line in enumerate(io.open(xu_path, encoding='utf-8-sig', newline='').read().split('\r\n'), 1):
    if '=' in line and not line.startswith('//'):
        z, e = line.split('=', 1); lines.append(('xunity|%d' % i, z, e))
items = []; stats = collections.Counter()
for z, info in names.items():
    given = info['given']; gv = variants(info['givenEn']); full = {info['en'].lower()} | {info['surnameEn'].lower() + ' ' + v for v in gv}
    for lid, s, g in lines:
        gl = g.lower()
        if z in s:
            if not any(v in gl for v in full) and not any(v in gl for v in gv):   # English often keeps only the given name; that is fine
                items.append({'id': lid, 'zh': s, 'en': g, 'kind': 'full', 'character': {'zh': z, 'en': info['en']}}); stats['full'] += 1
        elif given in s:
            if not any(v in gl for v in gv):
                items.append({'id': lid, 'zh': s, 'en': g, 'kind': 'given', 'character': {'zh': z, 'en': info['en'], 'given': given, 'givenEn': info['givenEn']}}); stats['given'] += 1
# one item per line: merge characters when several match the same line
by_id = collections.OrderedDict()
for it in items:
    if it['id'] in by_id: by_id[it['id']]['characters'].append(dict(it['character'], kind=it['kind']))
    else: by_id[it['id']] = {'id': it['id'], 'zh': it['zh'], 'en': it['en'], 'characters': [dict(it['character'], kind=it['kind'])]}
merged = list(by_id.values())
PER = 40; n = 0
for i in range(0, len(merged), PER):
    n += 1; json.dump(merged[i:i + PER], io.open(os.path.join(OUT, 'n_%03d.json' % n), 'w', encoding='utf-8', newline='\n'), ensure_ascii=False, indent=1)
per_char = collections.Counter(c['zh'] for m in merged for c in m['characters'])
print(json.dumps({'names': len(names), 'lines': len(merged), 'byKind': dict(stats), 'batches': n, 'topCharacters': per_char.most_common(12)}, ensure_ascii=False))
