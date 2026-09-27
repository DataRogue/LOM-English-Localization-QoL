"""Full review pass, step 3: apply the reviewed lines to the workspace and record them in the review ledger.

Reads the journals of one or more fullpass_workflow.js runs (review:<chunk> and verify:<chunk> agent results) and:
  - table rows: an accepted rewrite (every verdict for it accepts) replaces the workspace row, after the same checks as the
    proofread pass (line breaks, tags, placeholders, no Chinese, length 0.5-1.6x, the old text must still be there);
  - scene-only lines: an accepted rewrite replaces the line; an accepted drop removes it (publish then ships a removal line);
  - every editable line the reviewer answered for (kept, rewritten, or a rewrite the refuter turned down) goes into the review
    ledger (workspace/translation/reviewed.tsv, scene/reviewed_scene.tsv): reviewed lines ship even when they equal OverLlm's;
  - scene lines whose Chinese equals a reviewed table row (CRLF normalised) take that row's final English and join the ledger.
Usage: python apply_fullpass.py <journal.jsonl> [<journal.jsonl> ...] [--chunks DIR] [--live] [--pass NAME]
       [--keep-record KEYS|@FILE|all]   (--live publishes at the end, like every apply tool)
Dry run (default) writes staged copies and the change log to LOM_Localization/staged/fullpass.
"""
import os, io, re, sys, csv, json, glob, datetime, argparse, collections, shutil
sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..'))
import lom_paths
import ledger
from migrate_standalone import table_map
from hygiene.scene_hygiene import rewrite_line, decode_raw_value

BS = chr(92)
KEEP_RECORD = lom_paths.take_keep_record()   # --keep-record KEYS|@FILE|all, for the publish after a --live write
ap = argparse.ArgumentParser()
ap.add_argument('journals', nargs='+')
ap.add_argument('--chunks', default=os.path.join(lom_paths.REAL_LOCALIZATION, 'fullpass', 'chunks'))
ap.add_argument('--live', action='store_true')
ap.add_argument('--pass', dest='pass_name', default='fullpass-2026-09')
a = ap.parse_args()
stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
today = datetime.date.today().isoformat()
OUT = lom_paths.log_dir('fullpass') if a.live else lom_paths.staged_dir('fullpass')

# ---- results from the journals: label -> result ------------------------------------------------------------------------
label_of = {}
results = {}
for jp in a.journals:
    for line in open(jp, encoding='utf-8'):
        try:
            e = json.loads(line)
        except ValueError:
            continue
        if e.get('type') == 'started' and e.get('agentId'):
            label_of[e['agentId']] = e.get('label', '')
        elif e.get('type') == 'result' and e.get('agentId'):
            lab = label_of.get(e['agentId'], '')
            r = e.get('result')
            if isinstance(r, str):
                try:
                    r = json.loads(r)
                except ValueError:
                    r = None
            if lab and isinstance(r, dict):
                results[lab.replace(':retry', '')] = r   # a later run (resume, retry) wins
review = {int(l.split(':')[1]): r for l, r in results.items() if l.startswith('review:')}
verify = {int(l.split(':')[1]): r for l, r in results.items() if l.startswith('verify:')}
print('journals:', len(a.journals), '| review results', len(review), '| verify results', len(verify))

# ---- chunk lines ---------------------------------------------------------------------------------------------------------
lines_of = {}
for n in review:
    p = os.path.join(a.chunks, 'p_%04d.json' % n)
    ch = json.load(open(p, encoding='utf-8'))
    lines_of[n] = {x['id']: x for x in ch['lines'] if x.get('editable')}

MANIFEST = {e['chunk']: e['category'] for e in json.load(open(os.path.join(a.chunks, 'manifest.json'), encoding='utf-8'))}


def category_of(n):
    return MANIFEST.get(n, 'script')


CJK = re.compile(r'[一-鿿]')
TAG = re.compile(r'</?[a-zA-Z][^<>]*>')
PH = re.compile(r'\{\d+(?::[^}]*)?\}|\{\$[A-Za-z0-9_]+\}')
skipped = collections.Counter()
table_new = {}
scene_new = {}
scene_drop = {}      # key -> the reviewer's note
reviewed_table = set()
reviewed_scene = set()
log_rows = []
for n, r in sorted(review.items()):
    own = lines_of.get(n, {})
    items = {x.get('id'): x for x in (r.get('items') or []) if x.get('id') in own}
    skipped['editable lines the reviewer left out (not reviewed)'] += len(set(own) - set(items))
    verdicts = collections.defaultdict(list)
    for v in ((verify.get(n) or {}).get('verdicts') or []):
        verdicts[v.get('id')].append(v)
    for i, x in items.items():
        ln = own[i]
        kind = 'table' if i.startswith('table|') else 'scene'
        key = i.split('|', 1)[1]
        (reviewed_table if kind == 'table' else reviewed_scene).add(key)
        if x.get('keep') is not False:
            continue
        vs = verdicts.get(i, [])
        if not vs or not all(v.get('accept') for v in vs):
            skipped['rewrite refuted or unverified (old line kept, still reviewed)'] += 1
            continue
        if kind == 'scene' and x.get('drop'):
            scene_drop[key] = x.get('note', '')
            continue
        old = ln['en']
        new = (x.get('en') or '').replace('\r', '').strip()
        if not new:
            skipped['empty rewrite'] += 1
            continue
        if kind == 'table' and '\n\n' in new:
            skipped['blank line inside (the game parser drops it)'] += 1
            continue
        if kind == 'table':
            zh_nl = ln.get('zh', '').count('\n')
            if new.count('\n') not in (old.count('\n'), zh_nl):
                skipped['line breaks differ from both old and Chinese'] += 1
                continue
        else:
            if new.count(BS + 'n') not in (old.count(BS + 'n'), ln.get('zh', '').count(BS + 'n')):
                skipped['scene: backslash-n count differs'] += 1
                continue
        if new == old:
            skipped['no change'] += 1
            continue
        if CJK.search(new):
            skipped['introduces Chinese'] += 1
            continue
        o_cmp, n_cmp = old, new
        if kind == 'scene':
            try:
                o_cmp, n_cmp = decode_raw_value(old), decode_raw_value(new)
            except ValueError:
                pass
        if sorted(TAG.findall(o_cmp)) != sorted(TAG.findall(n_cmp)):
            skipped['tags differ'] += 1
            continue
        if sorted(PH.findall(o_cmp)) != sorted(PH.findall(n_cmp)):
            skipped['placeholders differ'] += 1
            continue
        # Length: the lower bound guards against dropped content (a rewrite that loses a sentence). The upper bound is loose
        # for story text, where the old English was often cut short: a short line may grow by 20 characters, a longer one while
        # it stays a normal length for its Chinese; interface data keeps a tight bound (small boxes).
        ratio = len(new) / max(1, len(old))
        zlen = len(ln.get('zh', '').replace('\r', '').replace('\n', ''))
        story = category_of(n) in ('script', 'unref', 'chronicle') or (category_of(n) == 'glossary' and key.startswith(('Story/', 'LegendInfo/')))
        if ratio < 0.5 and len(old) > 12:
            skipped['length: shorter than half the old line'] += 1
            continue
        if story:
            long_ok = ratio <= 1.6 or len(new) - len(old) <= 20 or (ratio <= 2.2 and len(new) <= 4.5 * max(1, zlen))
        else:
            long_ok = ratio <= 1.6 or len(new) - len(old) <= 8
        if not long_ok:
            skipped['length: longer than its place allows'] += 1
            continue
        if kind == 'table':
            table_new[key] = (old, new, x.get('note', ''))
        else:
            scene_new[key] = (old, new, x.get('note', ''))
print('reviewed: table', len(reviewed_table), '| scene-only', len(reviewed_scene), '| rewrites to apply: table', len(table_new), 'scene', len(scene_new), 'drops', len(scene_drop))
print('skipped:', dict(skipped))

# ---- table: whole rows by key (rows that span several physical lines included), the rest byte for byte --------------------
import rowspans
raw = io.open(lom_paths.TABLE, encoding='utf-8', newline='').read()
had_bom = raw.startswith(chr(0xFEFF))
raw = raw.lstrip(chr(0xFEFF))
nl = '\r\n' if '\r\n' in raw else '\n'
new_raw, applied, mismatched = rowspans.rewrite(raw, {k: (v[0], v[1]) for k, v in table_new.items()}, nl)
n_csv = len(applied)
cur_table = table_map(raw, True)
done = {k for k in mismatched if cur_table.get(k, '').replace('\r\n', '\n') == table_new[k][1]}
skipped['table: already applied (the workspace holds the rewrite)'] += len(done)
skipped['table: workspace text differs from the reviewed one (rewrite not applied)'] += len(mismatched - done)
for k in sorted(applied):
    old, new, why = table_new[k]
    log_rows.append(('table', k, old, new, why))
csv_out = (chr(0xFEFF) if had_bom else '') + new_raw
final_table = table_map(csv_out.lstrip('﻿'), True)

# ---- scene ---------------------------------------------------------------------------------------------------------------
zh = {}
for line in open(lom_paths.BASE_REF_SOURCE, encoding='utf-8'):
    k, _, v = line.rstrip('\n').partition('\t')
    zh[k] = v.replace(BS + 'r' + BS + 'n', BS + 'n').replace(BS + 'r', '')
# every reviewed row counts (this run's, earlier runs' from the ledger, the older passes'), so a run with fewer journals
# (a glossary pass, one wave) sees the same candidates; this run's rewrites win where the line's own English is none of them
before = set(json.load(open(os.path.join(lom_paths.REAL_LOCALIZATION, 'fullpass', 'reviewed_before.json'), encoding='utf-8')))
old_t, old_s = ledger.load()
by_zh = collections.defaultdict(set)
changed_zh = collections.defaultdict(set)
for k in reviewed_table | before | set(old_t):
    z = zh.get(k)
    if z and final_table.get(k):
        v = final_table[k].replace('\r\n', '\n').replace('\n', BS + 'n')
        by_zh[z].add(v)
        if k in applied:
            changed_zh[z].add(v)


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


raw2 = io.open(lom_paths.SCENE, encoding='utf-8', newline='').read()
had_bom2 = raw2.startswith('﻿')
raw2 = raw2.lstrip('﻿')
nl2 = '\r\n' if '\r\n' in raw2 else '\n'
out2 = []
n_mirror = n_scene = n_drop = 0
conflicts = 0
scene_ledger_keys = set()   # raw keys as publish.py reads them (ledger.scene_key of the line as written)


def emit(line, reviewed):
    out2.append(line)
    if reviewed:
        scene_ledger_keys.add(ledger.scene_key(line))


for line in raw2.split(nl2):
    kv = split(line) if line and not line.startswith('//') else None
    if not kv:
        out2.append(line)
        continue
    z, e = kv
    if z in scene_drop:
        n_drop += 1
        log_rows.append(('scene-drop', z, e, '', scene_drop[z]))
        continue
    if z in scene_new and scene_new[z][0] != e:
        skipped['scene: already applied' if e == scene_new[z][1] else 'scene: workspace text differs from the reviewed one (rewrite not applied)'] += 1
    if z in scene_new and scene_new[z][0] == e:
        nl_ = rewrite_line(line, z, scene_new[z][1])
        if nl_ is None:
            skipped['scene: new English cannot be written as a scene line'] += 1
            emit(line, True)
            continue
        emit(nl_, True)
        n_scene += 1
        log_rows.append(('scene', z, e, scene_new[z][1], scene_new[z][2]))
        continue
    cands = by_zh.get(z)
    if cands:
        if len(cands) > 1:
            conflicts += 1
        new = e if e in cands else sorted(changed_zh.get(z) or cands)[0]   # the line's own English when it is one of them
        reviewed_scene.add(z)
        if new != e:
            nl_ = rewrite_line(line, z, new)
            if nl_ is None:
                skipped['mirror: English cannot be written as a scene line'] += 1
                out2.append(line)   # not reviewed: its English is not the reviewed row's
                continue
            emit(nl_, True)
            n_mirror += 1
            log_rows.append(('scene-mirror', z, e, new, ''))
            continue
    emit(line, z in reviewed_scene)
print('skipped at the workspace:', {k: v for k, v in skipped.items() if k.startswith(('table:', 'scene:', 'mirror:'))})
scene_out = ('﻿' if had_bom2 else '') + nl2.join(out2)
print('table rows rewritten', n_csv, '| scene-only rewritten', n_scene, 'dropped', n_drop, '| scene lines mirrored from reviewed rows', n_mirror, '(Chinese with several reviewed Englishes:', conflicts, ')')

# ---- ledger --------------------------------------------------------------------------------------------------------------
led_t_keys = sorted(reviewed_table | {k for k in before if k in final_table})
led_s_keys = sorted(scene_ledger_keys)
led_t_all = dict(old_t)
led_s_all = dict(old_s)
for k in led_t_keys:
    led_t_all.setdefault(k, (a.pass_name, today))
for k in led_s_keys:
    led_s_all.setdefault(k, (a.pass_name, today))
for k in scene_drop:
    led_s_all.pop(k, None)

if a.live:
    lom_paths.assert_writable(lom_paths.TABLE)
    lom_paths.assert_writable(lom_paths.SCENE)
    for p in (lom_paths.TABLE, lom_paths.SCENE) + tuple(x for x in (ledger.path(False), ledger.path(True)) if os.path.exists(x)):
        dst = lom_paths.backup_path(p, stamp)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copy2(p, dst)
    io.open(lom_paths.TABLE, 'w', encoding='utf-8', newline='').write(csv_out)
    io.open(lom_paths.SCENE, 'w', encoding='utf-8', newline='').write(scene_out)
    # a dropped line leaves the ledger; the rest is added (a key already listed keeps its first entry)
    gone = [k for k in old_s if k in scene_drop]
    if gone:
        ledger.remove(gone, scene=True)
    ledger.add(led_t_keys, a.pass_name)
    ledger.add(led_s_keys, a.pass_name, scene=True)
    print('LIVE: workspace table, scene file and ledgers written (backups in %s)' % lom_paths.BACKUPS)
else:
    io.open(lom_paths.out_path(OUT, 'StringTable.csv'), 'w', encoding='utf-8', newline='').write(csv_out)
    io.open(lom_paths.out_path(OUT, 'scene_text.txt'), 'w', encoding='utf-8', newline='').write(scene_out)
    io.open(lom_paths.out_path(OUT, 'reviewed.tsv'), 'wb').write(ledger.serialize(led_t_all))
    io.open(lom_paths.out_path(OUT, 'reviewed_scene.tsv'), 'wb').write(ledger.serialize(led_s_all))
    print('staged copies written to', OUT)
with io.open(lom_paths.out_path(OUT, 'fullpass_changes.%s.tsv' % stamp), 'w', encoding='utf-8', newline='') as f:
    w = csv.writer(f, delimiter='\t', lineterminator='\n')
    w.writerow(['source', 'key', 'old', 'new', 'note'])
    for row in log_rows:
        w.writerow(row)
print('ledger: table', len(led_t_all), 'rows (%d new)' % (len(led_t_all) - len(old_t)), '| scene', len(led_s_all), 'lines (%d new)' % (len(led_s_all) - len(old_s)))
if a.live:
    lom_paths.publish(keep_record=KEEP_RECORD)   # last: a failing publish exits 3 or 4, after the change log is written
