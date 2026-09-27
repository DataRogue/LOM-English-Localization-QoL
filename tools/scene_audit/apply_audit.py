"""Scene-only audit, step 4: apply the accepted rewrites to the workspace scene file.

Reads the journals of audit_workflow.js runs (review:N, review-rest:N and verify:N results) and the chunks they reviewed
(scene_audit/chunks, from classify.py). A rewrite is applied when every verdict for it accepts and it passes the checks
apply_fullpass.py makes for scene lines: the same number of line breaks as the old English or the Chinese, the same
rich-text tags and placeholders, no Chinese, nothing that XUnity's format cannot hold ('//', a real line break), and a length
close to the old line's. It replaces the line by exact match: the physical line zh=en the chunk was cut from must still be
in the file exactly once, or the rewrite is skipped and reported. The raw key does not change, so the review ledger needs
nothing. A later journal wins for the same label, so an override journal (a copy of a verify:N record whose verdict for one id
says accept false) turns down a single rewrite.
Writes, in LOM_Localization/scene_audit (--live) or staged/scene_audit (otherwise):
  scene_audit_changes.<stamp>.tsv   every applied rewrite: line, id, kind, old, new, why
  scene_audit_skipped.<stamp>.tsv   every accepted rewrite that was not applied, and why
  ref_issues.<stamp>.tsv            the reviewers' notes about the table rows and overrides the lines follow (not applied)
--live writes the workspace scene file (backed up first to backups/scene_text.txt.<stamp>.bak) and publishes, like every
apply tool; without --live the new scene file goes to staged/scene_audit.
Usage: python apply_audit.py <journal.jsonl> [<journal.jsonl> ...] [--chunks DIR] [--arbiter FILE] [--live]
       [--keep-record KEYS|@FILE|all]
"""
import os, io, re, sys, csv, json, glob, shutil, argparse, datetime, collections
sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..'))
import lom_paths
import publish as PB
from hygiene.scene_hygiene import rewrite_line, decode_raw_value, raw_key

BS, CR, LF = chr(92), chr(13), chr(10)
BOM = chr(0xFEFF)
KEEP_RECORD = lom_paths.take_keep_record()
ap = argparse.ArgumentParser()
ap.add_argument('journals', nargs='+')
ap.add_argument('--chunks', default=os.path.join(lom_paths.REAL_LOCALIZATION, 'scene_audit', 'chunks'))
ap.add_argument('--live', action='store_true')
ap.add_argument('--arbiter', help='JSON {"accept": {id: why}, "reject": {id: why}}: the maintainer calls; accept lets a rewrite past '
                'the length checks (never the format checks), reject turns a rewrite down')
a = ap.parse_args()
stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
OUT = lom_paths.log_dir('scene_audit') if a.live else lom_paths.staged_dir('scene_audit')
ARB = json.load(open(a.arbiter, encoding='utf-8')) if a.arbiter else {}
ARB_ACCEPT, ARB_REJECT = ARB.get('accept', {}), ARB.get('reject', {})

# ---- journal results: label -> result (a later record wins) ---------------------------------------------------------------------
label_of, results = {}, {}
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
                results[lab.replace(':retry', '')] = r


def by_chunk(prefix):
    return {int(l.split(':')[1]): r for l, r in results.items() if l.startswith(prefix + ':')}


review, rest, verify = by_chunk('review'), by_chunk('review-rest'), by_chunk('verify')
print('journals:', len(a.journals), '| review', len(review), '| review-rest', len(rest), '| verify', len(verify))

CJK = re.compile(r'[\u3400-\u9fff\uf900-\ufaff]')
TAG = re.compile(r'</?[a-zA-Z][^<>]*>|\{/?[a-zA-Z]+(?:=[^}]*)?\}')
PH = re.compile(r'\{\d+(?::[^}]*)?\}|\{\$[A-Za-z0-9_]+\}')
LABELS = ('ui', 'dice-header', 'code', 'lua')
skipped = collections.Counter()
accepted, ref_issues, skip_rows = [], [], []
answered = total = 0
for n in sorted(set(review) | set(rest)):
    ch = json.load(open(os.path.join(a.chunks, 'p_%04d.json' % n), encoding='utf-8'))
    lines = {x['id']: x for x in ch['lines']}
    items = {}
    for src in (review.get(n), rest.get(n)):
        for x in ((src or {}).get('items') or []):
            if x.get('id') in lines and x.get('id') not in items:
                items[x['id']] = x
    total += len(lines)
    answered += len(items)
    verdicts = collections.defaultdict(list)
    for v in ((verify.get(n) or {}).get('verdicts') or []):
        verdicts[v.get('id')].append(v)
    for i, x in items.items():
        ln = lines[i]
        if x.get('ref_issue'):
            ref_issues.append((n, i, ln['kind'], ' | '.join(r['key'] for r in ln.get('ref', [])), x['ref_issue']))
        if x.get('keep') is not False or not x.get('en'):
            continue
        vs = verdicts.get(i, [])
        if not vs or not all(v.get('accept') for v in vs):
            skipped['refuted or unverified (old line kept)'] += 1
            continue
        if i in ARB_REJECT:
            skipped['turned down by the maintainer'] += 1
            skip_rows.append((n, i, ln['kind'], ln['en'], x['en'], 'maintainer: ' + ARB_REJECT[i]))
            continue
        # a real line break (a JSON escape the reviewer meant as the file's backslash-n) is written in the old line's form
        old, new = ln['en'], x['en'].replace(CR + LF, LF)
        if LF in new:
            new = new.replace(LF, BS + 'r' + BS + 'n' if BS + 'r' + BS + 'n' in old else BS + 'n')
        new = new.strip(' ' + chr(9))
        why = None
        if new == old:
            why = 'no change'
        elif '\n' in new or '\r' in new:
            why = 'a real line break inside'
        elif CJK.search(new):
            why = 'introduces Chinese'
        elif new.count(BS + 'n') not in (old.count(BS + 'n'), ln['zh'].count(BS + 'n')):
            why = 'backslash-n count differs from both the old line and the Chinese'
        else:
            try:
                o_dec, n_dec = decode_raw_value(old), decode_raw_value(new)
            except ValueError as e:
                o_dec, n_dec, why = None, None, 'cannot be written as a scene line (%s)' % e
            if why is None:
                if sorted(TAG.findall(o_dec)) != sorted(TAG.findall(n_dec)):
                    why = 'tags differ'
                elif sorted(PH.findall(o_dec)) != sorted(PH.findall(n_dec)):
                    why = 'placeholders differ'
                else:
                    ratio = len(new) / max(1, len(old))
                    zlen = len(CJK.findall(ln['zh']))
                    if i in ARB_ACCEPT:
                        pass
                    elif ratio < 0.5 and len(old) > 12:
                        why = 'shorter than half the old line'
                    elif ln['kind'] in LABELS and not (ratio <= 1.6 or len(new) - len(old) <= 8):
                        why = 'too long for a label'
                    elif ln['kind'] not in LABELS and not (ratio <= 1.6 or len(new) - len(old) <= 20 or (ratio <= 2.2 and len(new) <= 4.5 * max(1, zlen))):
                        why = 'too long'
        if why:
            skipped[why] += 1
            skip_rows.append((n, i, ln['kind'], old, new, why))
            continue
        accepted.append({'chunk': n, 'id': i, 'kind': ln['kind'], 'zh': ln['zh'], 'old': old, 'new': new, 'why': x.get('why', '')})
print('lines answered %d of %d; rewrites accepted and checked: %d' % (answered, total, len(accepted)))
print('skipped:', dict(skipped))

# ---- the scene file: replace each line by exact match ------------------------------------------------------------------------------
raw = io.open(lom_paths.SCENE, encoding='utf-8', newline='').read()
bom = raw.startswith(BOM)
raw = raw.lstrip(BOM)
nl = '\r\n' if '\r\n' in raw else '\n'
lines = raw.split(nl)
where = collections.defaultdict(list)
for idx, line in enumerate(lines):
    where[line].append(idx)
changes = []
for r in accepted:
    old_line = r['zh'] + '=' + r['old']
    hits = where.get(old_line, [])
    if len(hits) != 1:
        why = 'the line is no longer in the scene file' if not hits else 'the line is in the scene file %d times' % len(hits)
        skipped[why] += 1
        skip_rows.append((r['chunk'], r['id'], r['kind'], r['old'], r['new'], why))
        continue
    new_line = rewrite_line(old_line, raw_key(old_line), r['new'])
    old_kv, new_kv = PB.decode(old_line), PB.decode(new_line) if new_line else None
    if new_line is None or new_kv is None or old_kv is None or new_kv[0] != old_kv[0]:
        skipped['cannot be written as a scene line with the same key'] += 1
        skip_rows.append((r['chunk'], r['id'], r['kind'], r['old'], r['new'], 'cannot be written with the same key'))
        continue
    lines[hits[0]] = new_line
    changes.append((hits[0] + 1, r['id'], r['kind'], r['old'], new_line[len(raw_key(new_line)) + 1:], r['why']))
out_text = (BOM if bom else '') + nl.join(lines)
print('applied %d rewrites to the scene file' % len(changes))

# ---- write -------------------------------------------------------------------------------------------------------------------------
if a.live and changes:
    lom_paths.assert_writable(lom_paths.SCENE)
    dst = lom_paths.backup_path(lom_paths.SCENE, stamp)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    shutil.copy2(lom_paths.SCENE, dst)
    io.open(lom_paths.SCENE, 'w', encoding='utf-8', newline='').write(out_text)
    print('LIVE: workspace scene file written (backup %s)' % dst)
elif not a.live:
    io.open(lom_paths.out_path(OUT, 'scene_text.txt'), 'w', encoding='utf-8', newline='').write(out_text)
    print('staged copy written to', OUT)


def tsv(name, header, rows):
    with io.open(lom_paths.out_path(OUT, '%s.%s.tsv' % (name, stamp)), 'w', encoding='utf-8', newline='') as f:
        w = csv.writer(f, delimiter='\t', lineterminator='\n')
        w.writerow(header)
        for row in rows:
            w.writerow(row)


tsv('scene_audit_changes', ['line', 'id', 'kind', 'old', 'new', 'why'], changes)
tsv('scene_audit_skipped', ['chunk', 'id', 'kind', 'old', 'new', 'why'], skip_rows)
tsv('ref_issues', ['chunk', 'id', 'kind', 'ref', 'issue'], ref_issues)
print('change log, skipped list and %d ref notes written to %s' % (len(ref_issues), OUT))
if a.live and changes:
    lom_paths.publish(keep_record=KEEP_RECORD)
