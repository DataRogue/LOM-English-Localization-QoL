# Phase 2: find game-text lines where a settled glossary term is rendered with a retired variant.
# Usage: python phase2_candidates.py <terminology.json> <out dir>
import json, io, os, re, sys, csv, collections
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')); import lom_paths
term_path, OUT = sys.argv[1], sys.argv[2]
os.makedirs(OUT, exist_ok=True)
SP = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
terms = json.load(io.open(term_path, encoding='utf-8'))

# ---- rules: settled terms whose current text contains retired renderings
GENERIC_SKIP = set(['master', 'leader', 'hero', 'brother', 'sister', 'sect', 'gang', 'the master', 'you', 'he', 'she', 'they', 'it', 'lord', 'lady', 'miss', 'sir', 'young', 'old', 'elder', 'senior', 'junior', 'clan', 'family', 'palace', 'tower', 'hall', 'school', 'style', 'skill', 'art', 'arts', 'technique', 'power', 'strength', 'energy', 'qi'])
def clean_variant(v):
    v = re.sub(r'\s*\(\d+\)$', '', v).strip()
    v = re.sub(r"\s*\(sometimes.*\)$", '', v).strip()
    return v
rules = []
for t in terms:
    if t.get('keep') is False or t.get('needs_user_decision'): continue
    prop = (t.get('proposed_en') or '').strip()
    if not prop or not re.search(r'[A-Za-z]', prop): continue
    alts = set(a.strip().lower() for a in (t.get('allowalt') or []) if a)
    variants = []
    for v in (t.get('current_en') or []):
        v = clean_variant(v)
        if not v or len(v) < 4 or not re.search(r'[A-Za-z]', v): continue
        vl = v.lower()
        if vl == prop.lower() or vl in alts: continue
        if vl in GENERIC_SKIP: continue
        if re.search(r'[一-鿿]', v): continue           # untranslated leftovers handled separately
        if vl in prop.lower() or prop.lower() in vl: continue    # partial forms (Zhao vs Zhao Huo) are not errors
        if len(vl.split()) == 1 and vl in ('brother', 'master'): continue
        variants.append(v)
    if variants:
        rules.append({'zh': t['zh'], 'to': prop, 'variants': variants, 'category': t.get('category'), 'owner_ruled': bool(t.get('owner_ruled')), 'uses': t.get('corpus', 0)})
print('rules', len(rules))
zh_list = sorted(set(r['zh'] for r in rules), key=len, reverse=True)
big = re.compile('|'.join(re.escape(z) for z in zh_list))
by_zh = collections.defaultdict(list)
for r in rules: by_zh[r['zh']].append(r)

# ---- corpus: live StringTable (en) + raw table (zh); XUnity file (zh=en)
def read_csv(path):
    d = {}
    with io.open(path, encoding='utf-8-sig', newline='') as f:
        for row in csv.reader(f):
            if len(row) >= 2: d[row[0]] = row[1]
    return d
en_tab = read_csv(lom_paths.TABLE)
zh_tab = read_csv(os.path.join(SP, 'overllm/Files_Raw_StringTable.csv'))
lines = []   # (source, key, zh, en)
missing_zh = 0
for k, en in en_tab.items():
    zh = zh_tab.get(k)
    if zh is None: missing_zh += 1; continue
    lines.append(('table', k, zh, en))
xu_n = 0
with io.open(lom_paths.SCENE, encoding='utf-8-sig') as f:
    for i, raw in enumerate(f):
        raw = raw.rstrip('\n')
        if '=' not in raw or raw.startswith('//'): continue
        zh, en = raw.split('=', 1)
        lines.append(('xunity', str(i), zh, en)); xu_n += 1
print('table lines', len(en_tab), 'without zh', missing_zh, '| xunity lines', xu_n)

# ---- scan
def has_variant(en, v):
    return re.search(r'(?<![A-Za-z])' + re.escape(v) + r'(?![A-Za-z])', en, re.I) is not None
cands = []
per_term = collections.Counter(); per_variant = collections.Counter()
for src, key, zh, en in lines:
    hits = set(big.findall(zh))
    if not hits: continue
    triggered = []
    for z in hits:
        for r in by_zh[z]:
            if has_variant(en, r['to']) and not any(has_variant(en, v) for v in r['variants']): continue
            vs = [v for v in r['variants'] if has_variant(en, v)]
            if vs:
                triggered.append({'zh': z, 'to': r['to'], 'found': vs, 'owner_ruled': r['owner_ruled']})
                per_term[z] += 1
                for v in vs: per_variant[(z, v)] += 1
    if triggered:
        cands.append({'src': src, 'key': key, 'zh': zh, 'en': en, 'rules': triggered})
print('candidate lines', len(cands), 'table:', sum(1 for c in cands if c['src'] == 'table'), 'xunity:', sum(1 for c in cands if c['src'] == 'xunity'))
print('distinct terms triggered', len(per_term))
json.dump(cands, io.open(os.path.join(OUT, 'phase2_candidates.json'), 'w', encoding='utf-8', newline='\n'), ensure_ascii=False)
json.dump(rules, io.open(os.path.join(OUT, 'phase2_rules.json'), 'w', encoding='utf-8', newline='\n'), ensure_ascii=False, indent=0)
with io.open(os.path.join(OUT, 'phase2_summary.tsv'), 'w', encoding='utf-8', newline='\n') as f:
    f.write('zh\tto\tvariant\tlines\n')
    for (z, v), n in per_variant.most_common(): f.write('%s\t%s\t%s\t%d\n' % (z, [r['to'] for r in by_zh[z]][0], v, n))
print('top 40 term/variant pairs:')
for (z, v), n in per_variant.most_common(40): print('  %5d  %s: %s -> %s' % (n, z, v, [r['to'] for r in by_zh[z]][0]))
