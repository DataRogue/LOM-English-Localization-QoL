# Apply verified proofreading rewrites (ids 'table|<key>') to the string table, and mirror them into XUnity lines that carry
# the same Chinese and the same old English. Usage: python apply_proof.py <result json> <chunks dir> <log name> [--live]
#   [--keep-record KEYS|@FILE|all]: passed to the publish after a --live write (publish.py build --keep-record)
import json, io, os, re, sys, csv, shutil, collections, datetime
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')); import lom_paths
from hygiene.scene_hygiene import rewrite_line   # writes the new English escaped, so an '=' in it cannot break the line
KEEP = lom_paths.take_keep_record()   # --keep-record KEYS|@FILE|all, for the publish after a --live write
RES, CDIR, NAME = sys.argv[1], sys.argv[2], sys.argv[3]; LIVE = '--live' in sys.argv
SCR = os.path.dirname(os.path.abspath(__file__))
CSV_PATH = lom_paths.TABLE
XU_PATH = lom_paths.SCENE
OUT = lom_paths.log_dir(NAME) if LIVE else lom_paths.staged_dir('proof')
# log_dir / staged_dir create OUT and refuse a folder that resolves outside LOM_Localization (the stage's under
# LOM_STAGE): a junction or link there, or a '..' in the log name; each file in it goes through out_path
stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
r = json.load(io.open(RES, encoding='utf-8'))
if 'result' in r and 'fixChunks' not in r: r = r['result'] if not isinstance(r['result'], str) else json.loads(r['result'])
fixes = [f for ch in r.get('fixChunks', []) for f in ch]
verdicts = collections.defaultdict(list)
for ch in r.get('verdictChunks', []):
    for v in ch: verdicts[v['id']].append(v)
old_of = {}; zh_of = {}
for fn in sorted(os.listdir(CDIR)):
    if not re.fullmatch(r'p_\d{3}\.json', fn): continue
    for it in json.load(io.open(os.path.join(CDIR, fn), encoding='utf-8'))['lines']:
        if it.get('editable'): old_of[it['id']] = it['en']; zh_of[it['id']] = it['zh']
CJK = re.compile(r'[\u4e00-\u9fff]'); TAG = re.compile(r'</?[a-zA-Z][^<>]*>'); PH = re.compile(r'\{\d+(?::[^}]*)?\}')
skipped = collections.Counter(); edits = {}; log_rows = []
for f in fixes:
    k = f['id']; new = f['new']
    vs = verdicts.get(k, [])
    if not vs or not all(v.get('accept') for v in vs): skipped['refuted or unverified'] += 1; continue
    old = old_of.get(k)
    if old is None: skipped['unknown id'] += 1; continue
    # string-table rows hold REAL line breaks (the Chinese has them; the base patch dropped most of them). A rewrite may keep
    # the old line structure or restore the Chinese one; anything else is refused. Literal backslash-n sequences must match.
    new = new.replace('\r', '').strip()
    zh_nl = zh_of.get(k, '').count('\n')
    if new.count('\n') not in (old.count('\n'), zh_nl): skipped['line breaks differ from both old and Chinese'] += 1; continue
    if new == old: skipped['no change'] += 1; continue
    if CJK.search(new): skipped['introduces Chinese'] += 1; continue
    if sorted(TAG.findall(old)) != sorted(TAG.findall(new)): skipped['tags differ'] += 1; continue
    if sorted(PH.findall(old)) != sorted(PH.findall(new)): skipped['placeholders differ'] += 1; continue
    if old.count('\\n') != new.count('\\n'): skipped['backslash-n differs'] += 1; continue
    ratio = len(new) / max(1, len(old))
    if ratio < 0.5 or ratio > 1.6: skipped['length ratio'] += 1; continue
    if k in edits: skipped['duplicate'] += 1; continue
    edits[k] = (old, new, f.get('note', ''))
print('rewrites', len(fixes), '| applying', len(edits), '| skipped', dict(skipped))
def backup(path):
    dst = lom_paths.backup_path(path, stamp)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    lom_paths.guard_under(dst, lom_paths.BACKUPS)   # again once the backups folder exists
    shutil.copy2(path, dst)   # backup_path picks a free name, so every run keeps its own backup
    return dst
def csv_line(key, text): return key + ',"' + text.replace('"', '""') + '"'
raw = io.open(CSV_PATH, encoding='utf-8', newline='').read(); had_bom = raw.startswith('\ufeff'); raw = raw.lstrip('\ufeff')
nl = '\r\n' if '\r\n' in raw else '\n'
table_edits = {k.split('|', 1)[1]: v for k, v in edits.items()}
out_lines = []; n_csv = 0; seen = set()
for line in raw.split(nl):
    if line == '': out_lines.append(line); continue
    try: row = next(csv.reader([line]))
    except Exception: out_lines.append(line); continue
    if len(row) >= 2 and row[0] in table_edits and row[0] not in seen:
        old, new, why = table_edits[row[0]]
        if row[1] == old:
            out_lines.append(csv_line(row[0], new)); n_csv += 1; seen.add(row[0]); log_rows.append(('table', row[0], old, new, why)); continue
        skipped['table: live text differs'] += 1
    out_lines.append(line)
csv_out = ('\ufeff' if had_bom else '') + nl.join(out_lines)
raw2 = io.open(XU_PATH, encoding='utf-8', newline='').read(); had_bom2 = raw2.startswith('\ufeff'); raw2 = raw2.lstrip('\ufeff')
nl2 = '\r\n' if '\r\n' in raw2 else '\n'
mirror = {}
for k, (old, new, why) in edits.items():
    z = zh_of.get(k)
    if z: mirror[(z.replace('\n', '\\n'), old.replace('\n', '\\n'))] = (new.replace('\n', '\\n'), k)   # XUnity lines carry literal backslash-n
out2 = []; n_xu = 0
for line in raw2.split(nl2):
    if '=' in line and not line.startswith('//'):
        z, e = line.split('=', 1)
        if (z, e) in mirror:
            new, k = mirror[(z, e)]; nl_ = rewrite_line(line, z, new)
            if nl_ is None: skipped['xunity: new English cannot be written as a scene line'] += 1; out2.append(line); continue
            out2.append(nl_); n_xu += 1; log_rows.append(('xunity-mirror', k, e, nl_[len(z) + 1:], '')); continue
    out2.append(line)
xu_out = ('\ufeff' if had_bom2 else '') + nl2.join(out2)
if LIVE:
    lom_paths.assert_writable(CSV_PATH); lom_paths.assert_writable(XU_PATH)
    b1 = backup(CSV_PATH); b2 = backup(XU_PATH)
    io.open(CSV_PATH, 'w', encoding='utf-8', newline='').write(csv_out); io.open(XU_PATH, 'w', encoding='utf-8', newline='').write(xu_out)
    print('LIVE files written; backups:', b1, b2)
else:
    io.open(lom_paths.out_path(OUT, 'StringTable.csv'), 'w', encoding='utf-8', newline='').write(csv_out)
    io.open(lom_paths.out_path(OUT, '_AutoGeneratedTranslations.txt'), 'w', encoding='utf-8', newline='').write(xu_out)
    print('staged copies written to', OUT)
with io.open(lom_paths.out_path(OUT, '%s_changes.%s.tsv' % (NAME, stamp)), 'w', encoding='utf-8', newline='') as f:
    w = csv.writer(f, delimiter='\t', lineterminator='\n'); w.writerow(['source', 'key', 'old', 'new', 'note'])
    for row in log_rows: w.writerow(row)
print('rows rewritten: StringTable', n_csv, '| XUnity mirrored', n_xu, '| skipped', dict(skipped))
print('csv re-parse rows', sum(1 for _ in csv.reader(io.StringIO(csv_out.lstrip('\ufeff')))))

# the workspace was written: rebuild the shipped plugin files from it (publish.py build; prints one line)
if LIVE:
    lom_paths.publish(keep_record=KEEP)
