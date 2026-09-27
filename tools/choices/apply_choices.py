# Apply the verified choice fixes to the live game text (Story/O_* rows of the string table and the matching
# XUnity lines), with backups and a change log. Usage: python apply_choices.py <workflow result json> [--live]
#   [--keep-record KEYS|@FILE|all]: passed to the publish after a --live write (publish.py build --keep-record)
#   without --live: staged copies + logs under choices/staged, nothing in the game folder is touched
import json, io, os, re, sys, csv, shutil, collections, datetime
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')); import lom_paths
from hygiene.scene_hygiene import rewrite_line   # writes the new English escaped, so an '=' in it cannot break the line
KEEP = lom_paths.take_keep_record()   # --keep-record KEYS|@FILE|all, for the publish after a --live write
RES = sys.argv[1]; LIVE = '--live' in sys.argv
SCR = os.path.dirname(os.path.abspath(__file__))
CSV_PATH = lom_paths.TABLE
XU_PATH = lom_paths.SCENE
OUT = lom_paths.log_dir('choices') if LIVE else lom_paths.staged_dir('choices')
# log_dir / staged_dir create OUT and refuse a folder that resolves outside LOM_Localization (the stage's under
# LOM_STAGE): a junction or link there, or a '..' in the log name; each file in it goes through out_path
stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
r = json.load(io.open(RES, encoding='utf-8'))
if 'result' in r and 'fixChunks' not in r: r = r['result'] if not isinstance(r['result'], str) else json.loads(r['result'])
fixes = [f for ch in r.get('fixChunks', []) for f in ch]
verdicts = [v for ch in r.get('verdictChunks', []) for v in ch]
by_key = collections.defaultdict(list)
for v in verdicts: by_key[v['key']].append(v)
# batch files give zh + the English the reviewers saw (the 'old' text) -- a scratchpad input, left where it is
old_of = {}; zh_of = {}
bdir = os.path.join(SCR, 'batches')
for fn in sorted(os.listdir(bdir)):
    if not re.fullmatch(r'[mu]_\d{3}\.json', fn): continue   # agents leave scratch files next to the batches
    data = json.load(io.open(os.path.join(bdir, fn), encoding='utf-8'))
    if fn.startswith('m_'):
        for m in data:
            for o in m['options']: old_of[o['key']] = o['en']; zh_of[o['key']] = o['zh']
    else:
        for o in data: old_of[o['key']] = o['en']; zh_of[o['key']] = o['zh']
CJK = re.compile(r'[一-鿿]'); TAG = re.compile(r'</?[a-zA-Z][^<>]*>')
skipped = collections.Counter(); edits = {}; log_rows = []
for f in fixes:
    k = f['key']; new = f['new'].strip()
    vs = by_key.get(k, []); need = 2 if f['batch'].startswith('m') else 1
    if len(vs) < need or not all(v.get('accept') for v in vs): skipped['refuted or unverified'] += 1; continue
    old = old_of.get(k)
    if old is None: skipped['unknown key'] += 1; continue
    if new == old: skipped['no change'] += 1; continue
    if CJK.search(new): skipped['introduces Chinese'] += 1; continue
    if TAG.search(new) != None and not TAG.search(old): skipped['introduces a tag'] += 1; continue
    if '\n' in new or '\r' in new: skipped['real newline'] += 1; continue
    if len(new) > max(90, 3 * len(zh_of.get(k, '')) + 20): skipped['too long'] += 1; continue
    if k in edits: skipped['duplicate fix'] += 1; continue
    edits[k] = (old, new, f.get('reason', ''))
print('fixes', len(fixes), '| applying', len(edits), '| skipped', dict(skipped))
def backup(path):
    dst = lom_paths.backup_path(path, stamp)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    lom_paths.guard_under(dst, lom_paths.BACKUPS)   # again once the backups folder exists
    shutil.copy2(path, dst)   # backup_path picks a free name, so every run keeps its own backup
    return dst
def csv_line(key, text): return key + ',"' + text.replace('"', '""') + '"'
raw = io.open(CSV_PATH, encoding='utf-8', newline='').read(); had_bom = raw.startswith('﻿'); raw = raw.lstrip('﻿')
nl = '\r\n' if '\r\n' in raw else '\n'
out_lines = []; n_csv = 0; seen = set()
for line in raw.split(nl):
    if line == '': out_lines.append(line); continue
    try: row = next(csv.reader([line]))
    except Exception: out_lines.append(line); continue
    key = row[0][6:] if len(row) >= 2 and row[0].startswith('Story/') else None
    if key in edits and key not in seen:
        old, new, why = edits[key]
        if row[1] == old:
            out_lines.append(csv_line(row[0], new)); n_csv += 1; seen.add(key); log_rows.append(('table', row[0], old, new, why)); continue
        skipped['table: live text differs from reviewed text'] += 1
    out_lines.append(line)
csv_out = ('﻿' if had_bom else '') + nl.join(out_lines)
raw2 = io.open(XU_PATH, encoding='utf-8', newline='').read(); had_bom2 = raw2.startswith('﻿'); raw2 = raw2.lstrip('﻿')
nl2 = '\r\n' if '\r\n' in raw2 else '\n'
xu = {}
for k, (old, new, why) in edits.items():
    z = zh_of.get(k)
    if z: xu[(z.replace('\n', '\\n'), old)] = (new, k, why)
out2 = []; n_xu = 0
for line in raw2.split(nl2):
    if '=' in line and not line.startswith('//'):
        z, e = line.split('=', 1)
        if (z, e) in xu:
            new, k, why = xu[(z, e)]; nl_ = rewrite_line(line, z, new)
            if nl_ is None: skipped['xunity: new English cannot be written as a scene line'] += 1; out2.append(line); continue
            out2.append(nl_); n_xu += 1; log_rows.append(('xunity', k, e, nl_[len(z) + 1:], why)); continue
    out2.append(line)
xu_out = ('﻿' if had_bom2 else '') + nl2.join(out2)
if LIVE:
    lom_paths.assert_writable(CSV_PATH); lom_paths.assert_writable(XU_PATH)
    b1 = backup(CSV_PATH); b2 = backup(XU_PATH)
    io.open(CSV_PATH, 'w', encoding='utf-8', newline='').write(csv_out)
    io.open(XU_PATH, 'w', encoding='utf-8', newline='').write(xu_out)
    print('LIVE files written; backups:', b1, b2)
else:
    io.open(lom_paths.out_path(OUT, 'StringTable.csv'), 'w', encoding='utf-8', newline='').write(csv_out)
    io.open(lom_paths.out_path(OUT, '_AutoGeneratedTranslations.txt'), 'w', encoding='utf-8', newline='').write(xu_out)
    print('staged copies written to', OUT)
with io.open(lom_paths.out_path(OUT, 'choice_changes.%s.tsv' % stamp), 'w', encoding='utf-8', newline='') as f:
    w = csv.writer(f, delimiter='\t', lineterminator='\n'); w.writerow(['source', 'key', 'old', 'new', 'reason'])
    for row in log_rows: w.writerow(row)
print('rows rewritten: StringTable', n_csv, '| XUnity', n_xu, '| skipped', dict(skipped))
print('csv re-parse rows', sum(1 for _ in csv.reader(io.StringIO(csv_out.lstrip('﻿')))))

# the workspace was written: rebuild the shipped plugin files from it (publish.py build; prints one line)
if LIVE:
    lom_paths.publish(keep_record=KEEP)
