"""Glossary-conformance sweep, step 1: rows whose Chinese holds a ruled term but whose English has none of the term's ruled forms.

Terms come from LOM_Localization/glossary/terminology.tsv (proposed_en + allowalt). Only fixed-form terms are checked:
names, titles, sects, places, techniques, manuals, items, medicines (facilities, status effects and creatures only when
owner-ruled: most read as common words), two or more Chinese characters long, high confidence or owner-ruled. An entry that
names several forms ('母丹/子丹', '全真(教)') adds each part that has no row of its own. Every occurrence not inside a longer
one is checked (唐代掌門 gives 唐代 and 代掌門; 大師兄 is not also checked as 師兄). A form counts only as a whole word
(plural, possessive and common inflections allowed).
Rows: the table rows that ship (the review ledger plus the revision record), English from the workspace table (or --table).

Usage: python candidates.py [--table CSV] [--out DIR] [--per N] [--keys FILE]
Writes DIR/p_NNNN.json chunks in the full pass's format (lines: {id 'table|key', key, zh, en, speaker, before, after,
terms:[{zh, ruled, alts, category, why}], editable}), DIR/manifest.json (category 'glossary') and DIR/summary.json (counts
per term). apply_fullpass.py <journal> --chunks DIR --pass glossary-... applies an accepted fix with the same checks.
"""
import os, io, re, sys, csv, json, argparse, collections
sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..'))
import lom_paths
import ledger
from migrate_standalone import table_map

BS = chr(92)
LOC = lom_paths.REAL_LOCALIZATION        # glossary and fullpass chunks are read-only inputs
ap = argparse.ArgumentParser()
ap.add_argument('--table', default=lom_paths.TABLE)
ap.add_argument('--ledger', help='a reviewed.tsv to use instead of the workspace ledger (e.g. a dry run copy)')
ap.add_argument('--out', default=os.path.join(lom_paths.LOCALIZATION, 'fullpass', 'glossary'))
ap.add_argument('--per', type=int, default=30)
ap.add_argument('--keys', help='only these table keys (one per line)')
a = ap.parse_args()

FIXED = {'person', 'epithet_title', 'sect_faction', 'place', 'technique_move', 'martial_art_style', 'manual_book',
         'item_equipment', 'building_facility', 'medicine_poison', 'status_effect', 'historical_real', 'creature',
         'honorific_kinship', 'person_epithet'}
CJK = re.compile(r'[一-鿿]')
PROSE = {'stat_mechanic', 'other', 'position_activity', 'dialog_misc', 'idiom', 'status_effect', 'building_facility', 'creature'}
STOP = set('老子 姑娘 佩服 百姓 少年 讀書 分心 處世 下山 手下留情 武學 夫人 弟子 不平 茶肆 退隱江湖 師門 江湖人 大哥 大人'.split())
terms = {}
multi = []      # entries naming several forms ('母丹/子丹', '全真(教)'): their parts are added after every term's own row


def zh_parts(z):
    """'母丹/子丹' -> [母丹, 子丹]; '全真(教)' -> [全真, 全真教]; '全真(教/派)' -> [全真, 全真教, 全真派]."""
    m = re.match(r'^([^(（/／]+)[(（]([^)）]*)[)）]$', z)
    if m:
        return [m.group(1)] + [m.group(1) + x.strip() for x in re.split(r'[/／]', m.group(2)) if x.strip()]
    if re.search(r'[(（)）]', z):
        return []
    return [p.strip() for p in re.split(r'[/／]', z) if p.strip()]


for r in csv.DictReader(open(os.path.join(LOC, 'glossary', 'terminology.tsv'), encoding='utf-8'), delimiter='\t'):
    z = r['zh'].strip()
    if r.get('keep') == '0':
        continue
    if re.search(r'[/／(（]', z):
        multi.append(r)
        continue
    if len(z) < 2:
        continue
    ruled = r['owner_ruled'] == '1'
    if not ruled and (r['category'] not in FIXED or r['confidence'] != 'high'):
        continue
    forms = [f.strip() for f in re.split(r'\s*/\s*|\s*;\s*', r['proposed_en']) if f.strip()]
    alts = [f.strip() for f in re.split(r'\s*;\s*', r.get('allowalt') or '') if f.strip()]
    if not forms:
        continue
    # a lower-case form (your master, this old man, baozi...) is a phrase context decides, not a fixed name; the same for
    # common words the glossary lists for their interface use (a stat, an event title) and the ambiguous ones in STOP
    if forms[0][:1].islower() or z in STOP:
        continue
    if r['category'] in PROSE and not ruled:
        continue
    terms[z] = {'zh': z, 'ruled': forms[0], 'alts': forms[1:] + alts, 'category': r['category'],
                'why': (r.get('rationale') or '')[:220], 'owner': ruled}
for r in multi:
    ruled = r['owner_ruled'] == '1'
    if not ruled and (r['category'] not in FIXED or r['confidence'] != 'high'):
        continue
    if r['category'] in PROSE and not ruled:
        continue
    paren = re.search(r'[(（]', r['zh']) is not None
    parts = zh_parts(r['zh'].strip())
    forms = [f.strip() for f in re.split(r'\s*/\s*|\s*;\s*', r['proposed_en']) if f.strip()]
    per = [f.strip() for f in r['proposed_en'].split('/') if f.strip()]
    alts = [f.strip() for f in re.split(r'\s*;\s*', r.get('allowalt') or '') if f.strip()]
    if not forms:
        continue
    paired = len(per) == len(parts) and not paren
    if not paired and not paren:
        # an alias group with one English ('魏才女/魏菊/小菊' -> Wei Ju) names the person, not how each alias reads (a nickname,
        # a self-reference like 本公子): only groups that pair each form with its own English, or optional suffixes, count
        continue
    for j, part in enumerate(parts):
        own = per[j] if paired else forms[0]
        if len(part) < 2 or part in terms or part in STOP or own[:1].islower() or CJK.search(own):
            continue
        terms[part] = {'zh': part, 'ruled': own, 'alts': [f for f in forms if f != own] + alts, 'category': r['category'],
                       'why': (r.get('rationale') or '')[:220], 'owner': ruled}
print('terms checked:', len(terms))
by_first = collections.defaultdict(list)
for z in terms:
    by_first[z[0]].append(z)
for c in by_first:
    by_first[c].sort(key=len, reverse=True)


def found_terms(zh):
    """Every term occurrence not inside a longer one (overlaps allowed: 唐代掌門 gives 唐代 and 代掌門), in text order."""
    occ = []
    for i in range(len(zh)):
        for z in by_first.get(zh[i], ()):
            if zh.startswith(z, i):
                occ.append((i, i + len(z), z))
    return [z for s, e, z in occ if not any(s2 <= s and e <= e2 and e2 - s2 > e - s for s2, e2, _ in occ)]


def norm(s):
    return re.sub(r"[\s\-'" + chr(8217) + "]+", ' ', s.lower()).strip()


def has_form(en_n, t):
    for f in [t['ruled']] + t['alts']:
        fn = norm(f)
        # a whole word (plural, possessive and common inflections allowed): 'i' is the word I, 'outer fort' not 'outer fortress'
        if fn and re.search(r'(?<![a-z0-9])' + re.escape(fn) + r'(?:s|es|d|ed|ing|ive|ened)?(?![a-z0-9])', en_n):
            return True
    return False


zh = {}
for line in open(lom_paths.BASE_REF_SOURCE, encoding='utf-8'):
    k, _, v = line.rstrip('\n').partition('\t')
    zh[k] = v.replace(BS + 'r', '').replace(BS + 'n', '\n')
en = table_map(io.open(a.table, encoding='utf-8-sig', newline='').read(), True)
if a.keys:
    keys = [l.rstrip('\r\n') for l in open(a.keys, encoding='utf-8') if l.strip()]
else:
    if a.ledger:
        led, _ = ledger.parse(open(a.ledger, 'rb').read())
    else:
        led = ledger.load(scene=False)
    rec = set()
    if os.path.exists(lom_paths.WS_RECORD):
        for line in open(lom_paths.WS_RECORD, encoding='utf-8'):
            if line.strip() and not line.startswith('#'):
                rec.add(line.split('\t', 1)[0])
    keys = sorted((set(led) | rec) & set(en))
print('rows considered:', len(keys))

# context: the fullpass chunks give each row its speaker and neighbours
ctx = {}
CH = os.path.join(LOC, 'fullpass', 'chunks')
for fn in sorted(os.listdir(CH)):
    if not re.match(r'^p_[0-9]{4}[.]json$', fn):
        continue
    ch = json.load(open(os.path.join(CH, fn), encoding='utf-8'))
    seq = [x for x in ch.get('context', []) + ch.get('lines', []) if x.get('zh')]
    # the chunk's own order is context first; keep the lines in script order when they carry an index
    for i, x in enumerate(seq):
        k = x.get('key') or (x['id'].split('|', 1)[1] if x.get('id', '').startswith('table|') else None)
        if k and k not in ctx:
            ctx[k] = (seq, i)

flag = []
per_term = collections.Counter()
for k in keys:
    z, e = zh.get(k, ''), en.get(k, '')
    if not z or not e:
        continue
    en_n = norm(e)
    miss = [terms[t] for t in dict.fromkeys(found_terms(z)) if not has_form(en_n, terms[t])]
    if not miss:
        continue
    for t in miss:
        per_term[t['zh']] += 1
    d = {'id': k, 'key': k, 'zh': z, 'en': e, 'terms': [{x: t[x] for x in ('zh', 'ruled', 'alts', 'category', 'why')} for t in miss]}
    if k in ctx:
        seq, i = ctx[k]
        d['speaker'] = seq[i].get('speaker')
        d['before'] = [{'speaker': x.get('speaker'), 'zh': x['zh'][:240], 'en': (x.get('en') or '')[:240]} for x in seq[max(0, i - 2):i]]
        d['after'] = [{'speaker': x.get('speaker'), 'zh': x['zh'][:240], 'en': (x.get('en') or '')[:240]} for x in seq[i + 1:i + 2]]
    flag.append(d)
print('rows flagged:', len(flag), '| term hits:', sum(per_term.values()))
print('top terms:', ', '.join('%s %d (%s)' % (t, n, terms[t]['ruled']) for t, n in per_term.most_common(40)))
os.makedirs(a.out, exist_ok=True)
for f in os.listdir(a.out):
    if re.match(r'^p_[0-9]{4}[.]json$', f) or f == 'manifest.json':
        os.remove(os.path.join(a.out, f))
# chunks in the full pass's format (apply_fullpass.py --chunks DIR applies them): one script's rows stay together
flag.sort(key=lambda d: d['key'])
man = []
for b in range(0, len(flag), a.per):
    n = b // a.per + 1
    lines = []
    for d in flag[b:b + a.per]:
        x = dict(d)
        x['id'] = 'table|' + d['key']
        x['editable'] = True
        x['kind'] = 'row'
        lines.append(x)
    json.dump({'chunk': n, 'category': 'glossary', 'context': [], 'lines': lines},
              open(os.path.join(a.out, 'p_%04d.json' % n), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    man.append({'chunk': n, 'category': 'glossary', 'lines': len(lines)})
json.dump(man, open(os.path.join(a.out, 'manifest.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
json.dump({'rows': len(flag), 'chunks': len(man), 'per_term': per_term.most_common()},
          open(os.path.join(a.out, 'summary.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print('chunks:', len(man), '->', a.out)
