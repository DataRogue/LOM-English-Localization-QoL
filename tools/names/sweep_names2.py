# Second name sweep: two-character personal names and nickname forms (小X, X兒, 阿X, X哥/姐/兄 ...) from the settled glossary.
# Flags lines whose Chinese contains the form while the English lacks its settled rendering (or the bare syllable).
# Usage: python sweep_names2.py <out dir>
import csv, io, os, re, sys, json, collections
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')); import lom_paths
OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
for f in os.listdir(OUT): os.remove(os.path.join(OUT, f))
SCR = os.path.dirname(os.path.abspath(__file__))
zh = {r[0]: r[1] for r in csv.reader(io.open(os.path.join(SCR, '..', 'overllm', 'Files_Raw_StringTable.csv'), encoding='utf-8-sig', newline='')) if len(r) >= 2}
en = {r[0]: r[1] for r in csv.reader(io.open(lom_paths.TABLE, encoding='utf-8-sig', newline='')) if len(r) >= 2}
gl = list(csv.DictReader(io.open(os.path.join(SCR, '..', 'terms', 'out', 'terminology.tsv'), encoding='utf-8'), delimiter='\t'))
STOP = set('''lord lady doctor man person master elder sect chief young old boss aunt auntie uncle grandma grandpa sir madam miss monk nun daoist general captain guard servant maid shopkeeper innkeeper waiter boy girl kid child beggar soldier soldiers villager merchant priest abbot king prince princess queen emperor envoy officer magistrate constable leader head sedan masked mysterious unknown voice in the of chair brother sister junior senior little big second third fourth fifth sixth eldest mother father son daughter wife husband grandmother grandfather mrs mr steward butler cook chef nurse teacher scholar swordsman hero heroine bandit thief pirate ghost spirit god goddess immortal fairy guildmaster fire lightning crowd youth female woman women people commoners physician everyone mister maiden echo horse ox boar parrot monkey martial household elderly county vice you i me we they this that someone stranger patron patriarch matriarch madame mistress disciple disciples apprentice clan family gang palace school village troops men benefactor teacher-uncle'''.split())
NAMEISH = re.compile(r"^[A-Z][a-z]+(?:'[a-z]+)?$")
def settled(r): return (r.get('fable_en') or r.get('proposed_en') or '').strip()
def tokens(e): return [t for t in re.split(r'[\s\-]+', e.replace('’', "'")) if t]
def variants(e):
    out = {e.lower()}
    for t in tokens(e):
        tl = t.lower().replace("'", '')
        if tl in STOP or len(tl) < 2: continue
        out.add(t.lower()); out.add(tl)
        for i in range(2, len(tl) - 1): out.add(tl[:i] + ' ' + tl[i:]); out.add(tl[:i] + "'" + tl[i:])
    return out
entries = []
NICK = re.compile(r'^(小|阿|老|本)[一-鿿]{1,2}$|^[一-鿿]{1,2}(兒|兄|哥|姐|妹|姊)$')
for r in gl:
    z = r['zh'].strip(); e = settled(r); cat = r.get('category', '')
    if not e or not re.fullmatch(r'[一-鿿]{2,3}', z) or re.search(r'[一-鿿]', e): continue
    toks = tokens(e)
    if not toks or not any(NAMEISH.match(t) and t.lower().replace("'", '') not in STOP for t in toks): continue   # must carry a pinyin-like name token
    if len(z) == 2 and cat == 'person' and not NICK.match(z):
        if all(t.lower() in STOP for t in toks): continue
        entries.append({'zh': z, 'en': e, 'kind': 'two-char'})
    elif NICK.match(z) and cat in ('person', 'honorific_kinship', 'epithet_title'):
        entries.append({'zh': z, 'en': e, 'kind': 'nickname'})
# drop forms that are just part of a longer settled name handled by the first sweep (e.g. 雲舟 in 葉雲舟) unless they also stand alone
allnames = set(r['zh'].strip() for r in gl if r.get('category') == 'person') | set(zh[k].strip() for k in zh if k.startswith('Character/'))
for it in entries: it['super'] = sorted(n for n in allnames if it['zh'] in n and n != it['zh'] and len(n) <= 4)
lines = []
for k in zh:
    if k.split('/')[0] in ('Story', 'LegendInfo', 'CombatTalking', 'CharacterIntro0', 'CharacterIntro1', 'CharacterIntro2', 'CharacterIntro3', 'CharacterIntro4') and k in en:
        lines.append(('table|' + k, zh[k], en[k]))
for i, line in enumerate(io.open(lom_paths.SCENE, encoding='utf-8-sig', newline='').read().split('\r\n'), 1):
    if '=' in line and not line.startswith('//'):
        z, e = line.split('=', 1); lines.append(('xunity|%d' % i, z, e))
by_id = collections.OrderedDict(); per = collections.Counter(); uses = collections.Counter()
for it in entries:
    vs = variants(it['en'])
    for lid, s, g in lines:
        if it['zh'] not in s: continue
        stripped = s
        for sup in it['super']: stripped = stripped.replace(sup, '')
        if it['zh'] not in stripped: continue   # only the longer name is present
        uses[it['zh']] += 1
        gl_ = g.lower().replace('’', "'")
        if any(v in gl_ for v in vs): continue
        per[it['zh']] += 1
        rec = by_id.setdefault(lid, {'id': lid, 'zh': s, 'en': g, 'characters': []})
        rec['characters'].append({'zh': it['zh'], 'settled': it['en'], 'kind': it['kind']})
merged = list(by_id.values())
PER = 40; n = 0
for i in range(0, len(merged), PER):
    n += 1; json.dump(merged[i:i + PER], io.open(os.path.join(OUT, 'n_%03d.json' % n), 'w', encoding='utf-8', newline='\n'), ensure_ascii=False, indent=1)
json.dump(entries, io.open(os.path.join(OUT, '..', 'inventory2.json'), 'w', encoding='utf-8', newline='\n'), ensure_ascii=False, indent=1)
print(json.dumps({'entries': len(entries), 'twoChar': sum(1 for e in entries if e['kind'] == 'two-char'), 'nick': sum(1 for e in entries if e['kind'] == 'nickname'), 'lines': len(merged), 'batches': n}, ensure_ascii=False))
print('top flagged forms:', [(z, per[z], uses[z]) for z, _ in per.most_common(25)])
print('inventory sample:', [(e['zh'], e['en'], e['kind']) for e in entries[:60]])
