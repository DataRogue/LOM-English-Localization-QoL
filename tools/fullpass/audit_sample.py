"""Stratified random sample of ACCEPTED rewrites from fullpass journals, for an independent audit.
Usage: python audit_sample.py <out dir> <n per batch> <journal.jsonl> [...]  -> out/a_NN.json batches of {id, category, zh, old, new, context}"""
import os, sys, json, random, collections
sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..'))
import lom_paths
C = os.path.join(lom_paths.LOCALIZATION, 'fullpass', 'chunks')
OUT, PER = sys.argv[1], int(sys.argv[2])
os.makedirs(OUT, exist_ok=True)
man = {e['chunk']: e['category'] for e in json.load(open(os.path.join(C, 'manifest.json'), encoding='utf-8'))}
label_of, res = {}, {}
for jp in sys.argv[3:]:
    for line in open(jp, encoding='utf-8'):
        e = json.loads(line)
        if e.get('type') == 'started':
            label_of[e['agentId']] = e.get('label', '')
        elif e.get('type') == 'result':
            res[label_of.get(e['agentId'], '').replace(':retry', '')] = e.get('result')
pool = collections.defaultdict(list)
for lab, r in res.items():
    if not lab.startswith('review:') or not isinstance(r, dict):
        continue
    n = int(lab.split(':')[1])
    v = res.get('verify:%d' % n) or {}
    acc = {x['id'] for x in (v.get('verdicts') or []) if x.get('accept')}
    ch = json.load(open(os.path.join(C, 'p_%04d.json' % n), encoding='utf-8'))
    seq = ch['context'] + ch['lines']
    idx = {x.get('id'): i for i, x in enumerate(seq)}
    for it in r.get('items') or []:
        if it.get('keep') is False and it['id'] in acc and it['id'] in idx and (it.get('en') or it.get('drop')):
            i = idx[it['id']]
            ln = seq[i]
            ctx = [{'speaker': x.get('speaker'), 'kind': x.get('kind'), 'zh': x.get('zh', '')[:200], 'en': x.get('en', '')[:200]} for x in seq[max(0, i - 3):i] if x.get('zh')]
            pool[man.get(n, 'script')].append({'id': it['id'], 'chunk': n, 'category': man.get(n, 'script'), 'kind': ln.get('kind'), 'speaker': ln.get('speaker'),
                                               'zh': ln.get('zh', ''), 'old': ln.get('en', ''), 'new': it.get('en', ''), 'drop': bool(it.get('drop')), 'context_before': ctx})
random.seed(20260924)
want = {'script': 240, 'unref': 30, 'chronicle': 30, 'data': 30, 'scene': 30}
sample = []
for cat, k in want.items():
    got = pool.get(cat, [])
    sample += random.sample(got, min(k, len(got)))
random.shuffle(sample)
for b in range(0, len(sample), PER):
    json.dump(sample[b:b + PER], open(os.path.join(OUT, 'a_%02d.json' % (b // PER + 1)), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print(json.dumps({'pool': {c: len(v) for c, v in pool.items()}, 'sampled': len(sample), 'batches': (len(sample) + PER - 1) // PER}))
