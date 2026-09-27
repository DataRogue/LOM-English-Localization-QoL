# Apply phase-2 edits (Tier A deterministic + verified Tier B) to the live game text.
# Usage: python apply_phase2.py <phase2 dir> <workflow result json or '-'> [--live] [--keep-record KEYS|@FILE|all]
#   without --live: writes staged copies + logs under LOM_Localization/staged/phase2 (the stage's under LOM_STAGE)
#   --keep-record: passed to the publish after a --live write (publish.py build --keep-record: the changed rows among them keep
#   their old revision record, e.g. rows out of date since a game update that this term pass only touches up)
#   the Tier B edits that diverge from their line go to diverged.<stamp>.json next to the change log (never into <phase2 dir>)
import json, io, os, re, sys, csv, shutil, collections, datetime, difflib
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')); import lom_paths
from hygiene.scene_hygiene import rewrite_line   # writes the new English escaped, so an '=' in it cannot break the line
KEEP = lom_paths.take_keep_record()   # --keep-record KEYS|@FILE|all, for the publish after a --live write
D = sys.argv[1]; RES = sys.argv[2]; LIVE = '--live' in sys.argv
CSV_PATH = lom_paths.TABLE
XU_PATH = lom_paths.SCENE
OUT = lom_paths.log_dir('phase2') if LIVE else lom_paths.staged_dir('phase2')
# log_dir / staged_dir create OUT and refuse a folder that resolves outside LOM_Localization (the stage's under
# LOM_STAGE): a junction or link there, or a '..' in the log name; each file in it goes through out_path
stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')

# ---- inputs
tierA = json.load(io.open(os.path.join(D, 'tierA.json'), encoding='utf-8'))
bad = []; lines = []; verdicts = []
if RES != '-':
    r = json.load(io.open(RES, encoding='utf-8'))
    if 'result' in r and 'lineChunks' not in r: r = r['result'] if not isinstance(r['result'], str) else json.loads(r['result'])
    bad = r.get('badPairs', [])
    for ch in r.get('lineChunks', []): lines += ch
    for ch in r.get('verdictChunks', []): verdicts += ch
bad_keys = set((b['zh'], b['variant']) for b in bad)
rejected = set(v['id'] for v in verdicts if v.get('verdict') == 'reject')
verified = set(v['id'] for v in verdicts if v.get('verdict') == 'accept')
print('tierA lines', len(tierA), '| bad pairs', len(bad_keys), '| tierB lines', len(lines), 'changed', sum(1 for l in lines if l.get('changed')), '| verdicts', len(verdicts), 'rejected', len(rejected))

# ---- structural checks
TAG = re.compile(r'</?[a-zA-Z][^<>]*>'); PH = re.compile(r'\{\d+(?::[^}]*)?\}'); CJK = re.compile(r'[一-鿿]')
def structural_ok(old, new, src):
    if new is None or new == old: return False, 'no change'
    if CJK.search(new) and not CJK.search(old): return False, 'introduces Chinese'
    if sorted(TAG.findall(old)) != sorted(TAG.findall(new)): return False, 'rich-text tags differ'
    if sorted(PH.findall(old)) != sorted(PH.findall(new)): return False, 'placeholders differ'
    if old.count('\\n') != new.count('\\n'): return False, 'backslash-n count differs'
    if '\n' in new or '\r' in new: return False, 'contains a real newline'
    if '"' in new and '"' not in old and src == 'table': return False, 'introduces a double quote'
    ratio = len(new) / max(1, len(old))
    if ratio < 0.5 or ratio > 2.5: return False, 'length ratio %.2f' % ratio
    return True, ''

# ---- build the edit map: (src, key) -> new text
edits = {}   # (src,key) -> (old, new, how)
zh_of = {}   # (src,key) -> zh source text (XUnity lines are matched on zh AND en, never on en alone)
log_rows = []; skipped = collections.Counter()
def apply_a(en, es):
    out = en
    for v, to, zh in sorted(es, key=lambda e: -len(e[0])):
        out = re.sub(r'(?<![A-Za-z])' + re.escape(v) + r'(?![A-Za-z])', to, out)
    return out
for t in tierA:
    es = [e for e in t['edits'] if (e[2], e[0]) not in bad_keys]
    if not es: skipped['tierA: all pairs flagged'] += 1; continue
    new = apply_a(t['en'], es)
    ok, why = structural_ok(t['en'], new, t['src'])
    if not ok: skipped['tierA: ' + why] += 1; continue
    edits[(t['src'], t['key'])] = (t['en'], new, 'A:' + ';'.join('%s>%s' % (e[0], e[1]) for e in es))
    zh_of[(t['src'], t['key'])] = t['zh']
# Tier B: only verified edits; apply on top of Tier A when both touch a line
b_by_id = {}
for l in lines:
    if not l.get('changed') or not l.get('en'): continue
    if l['id'] in rejected: skipped['tierB: rejected by verifier'] += 1; continue
    if l['id'] not in verified and '--no-verify' not in sys.argv: skipped['tierB: no accept verdict'] += 1; continue
    b_by_id[l['id']] = l
tierB_orig = {}
for sub, prefix in (('tierB', 'b_'), ('tierB_new', 'c_')):   # the fresh (post-ruling) batch files carry the current rules, so they override run-1's
    if not os.path.isdir(os.path.join(D, sub)): continue
    for fn in sorted(os.listdir(os.path.join(D, sub))):
        if not re.fullmatch(prefix + r'\d{3}\.json', fn): continue
        for item in json.load(io.open(os.path.join(D, sub, fn), encoding='utf-8')): tierB_orig[item['id']] = item
diverged = []
for lid, l in b_by_id.items():
    src, key = lid.split('|', 1)
    orig = tierB_orig.get(lid)
    if orig is None: skipped['tierB: unknown id'] += 1; continue
    base_old = orig['en']; new = l['en']
    # the editors often returned real newlines where the source has the literal two-character \n sequence; restore it
    if '\n' in new and '\n' not in base_old: new = new.replace('\r', '').replace('\n', '\\n')
    ok, why = structural_ok(base_old, new, src)
    if not ok: skipped['tierB: ' + why] += 1; continue
    # a term swap keeps most of the line; an edit that shares little with the original is another line's text (seen: xunity|49282, 50887, 50889)
    ratio = difflib.SequenceMatcher(None, base_old, new, autojunk=False).ratio()
    tos = [r.get('to', '') for r in (orig.get('rules') or []) if r.get('to')]
    present = any(t.lower() in new.lower() for t in tos)
    if (len(base_old) > 80 and ratio < 0.55) or (not present and ratio < 0.8):
        skipped['tierB: edit diverges from original'] += 1; diverged.append({'id': lid, 'ratio': round(ratio, 2), 'termPresent': present, 'old': base_old, 'new': new}); continue
    if (src, key) in edits:
        # both tiers touched the line: re-apply the Tier A pairs on the Sonnet result so nothing is lost
        a_old, a_new, how = edits[(src, key)]
        es = [e for e in next(t for t in tierA if t['src'] == src and t['key'] == key)['edits'] if (e[2], e[0]) not in bad_keys]
        merged = apply_a(new, es)
        edits[(src, key)] = (base_old, merged, how + '|B')
    else:
        edits[(src, key)] = (base_old, new, 'B')
    zh_of[(src, key)] = orig['zh']
print('edits to apply', len(edits), '| skipped', dict(skipped))
# the Tier B edits that diverge from their line, for review: into OUT (log_dir / staged_dir, each file through out_path, like
# every other output of this tool), never into the input folder
json.dump(diverged, io.open(lom_paths.out_path(OUT, 'diverged.' + stamp + '.json'), 'w', encoding='utf-8', newline='\n'), ensure_ascii=False, indent=1)

# ---- rewrite files
def backup(path):
    dst = lom_paths.backup_path(path, stamp)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    lom_paths.guard_under(dst, lom_paths.BACKUPS)   # again once the backups folder exists
    shutil.copy2(path, dst)   # backup_path picks a free name, so every run keeps its own backup
    return dst
def csv_line(key, text):
    return key + ',"' + text.replace('"', '""') + '"'
# StringTable.csv: UTF-8 no BOM, CRLF, key,"text" per line, no multi-line values
raw = io.open(CSV_PATH, encoding='utf-8', newline='').read()
had_bom = raw.startswith('﻿'); raw = raw.lstrip('﻿')
nl = '\r\n' if '\r\n' in raw else '\n'
out_lines = []; n_csv = 0; seen_keys = set()
for line in raw.split(nl):
    if line == '' : out_lines.append(line); continue
    try: row = next(csv.reader([line]))
    except Exception: out_lines.append(line); continue
    if len(row) >= 2 and ('table', row[0]) in edits and row[0] not in seen_keys:
        old, new, how = edits[('table', row[0])]
        if row[1] == old:
            out_lines.append(csv_line(row[0], new)); n_csv += 1; seen_keys.add(row[0]); log_rows.append(('table', row[0], old, new, how)); continue
        else: skipped['table: live text differs from analysed text'] += 1
    out_lines.append(line)
csv_out = ('﻿' if had_bom else '') + nl.join(out_lines)
# XUnity file: UTF-8 BOM, CRLF, zh=en per line; keyed by line index at analysis time -> re-key by zh+en to be safe
raw2 = io.open(XU_PATH, encoding='utf-8', newline='').read()
had_bom2 = raw2.startswith('﻿'); raw2 = raw2.lstrip('﻿')
nl2 = '\r\n' if '\r\n' in raw2 else '\n'
xu_edits = {}
for (src, key), (old, new, how) in edits.items():
    if src == 'xunity': xu_edits.setdefault((zh_of.get((src, key), ''), old), []).append((key, new, how))
out2 = []; n_xu = 0
for i, line in enumerate(raw2.split(nl2)):
    if '=' in line and not line.startswith('//'):
        zh, en = line.split('=', 1)
        cands = xu_edits.get((zh, en))
        if cands:
            # same Chinese and same English as analysed: prefer the entry recorded for this line index
            pick = next((c for c in cands if c[0] == str(i)), None) or cands[0]
            nl_ = rewrite_line(line, zh, pick[1])
            if nl_ is None: skipped['xunity: new English cannot be written as a scene line'] += 1; out2.append(line); continue
            out2.append(nl_); n_xu += 1; log_rows.append(('xunity', str(i), en, nl_[len(zh) + 1:], pick[2])); continue
    out2.append(line)
xu_out = ('﻿' if had_bom2 else '') + nl2.join(out2)
print('lines rewritten: StringTable.csv', n_csv, '| XUnity', n_xu, '| skipped', dict(skipped))
# ---- write
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
with io.open(lom_paths.out_path(OUT, 'phase2_changes.' + stamp + '.tsv'), 'w', encoding='utf-8', newline='') as f:
    w = csv.writer(f, delimiter='\t', lineterminator='\n'); w.writerow(['source', 'key', 'old', 'new', 'how'])
    for r in log_rows: w.writerow(r)
json.dump({'stamp': stamp, 'live': LIVE, 'tierA_lines': len(tierA), 'bad_pairs': sorted(list(bad_keys)), 'tierB_changed': sum(1 for l in lines if l.get('changed')), 'rejected': len(rejected), 'applied_csv': n_csv, 'applied_xunity': n_xu, 'skipped': dict(skipped)},
      io.open(lom_paths.out_path(OUT, 'phase2_summary.' + stamp + '.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
# sanity: re-parse the CSV
n = 0
for row in csv.reader(io.StringIO(csv_out.lstrip('﻿'))): n += 1
print('csv re-parse rows', n)

# the workspace was written: rebuild the shipped plugin files from it (publish.py build; prints one line)
if LIVE:
    lom_paths.publish(keep_record=KEEP)
