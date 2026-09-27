# Static overflow analysis for a scene dump: resolve each text's English (override CSVs, then StringTable), estimate its width,
# and flag boxes where the English cannot fit under the prefab's font size / wrap / overflow settings.
# Usage: python analyze_overflow.py <ui.jsonl> <out.tsv> [existing rules dir]
import json, io, os, sys, csv, re, math, collections
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')); import lom_paths
SRC, OUT = sys.argv[1], sys.argv[2]; RULES = sys.argv[3] if len(sys.argv) > 3 else ''
# ---- English lookups
en = {}
with io.open(lom_paths.TABLE, encoding='utf-8-sig', newline='') as f:
    for row in csv.reader(f):
        if len(row) >= 2: en[row[0]] = row[1]
ov = {}
sdir = lom_paths.STRINGS_DIR
for fn in sorted(os.listdir(sdir)):
    if fn.endswith('.csv'):
        for row in csv.reader(io.open(os.path.join(sdir, fn), encoding='utf-8-sig', newline='')):
            if len(row) >= 2 and row[0] != 'key': ov[row[0]] = row[1]
def english(key):
    if key in ov: return ov[key], 'override'
    if key in en: return en[key], 'table'
    return None, 'missing'
# ---- existing rule coverage (which paths already have a rule)
covered = []
def strip_comments(txt):
    out = []; in_str = False
    i = 0
    while i < len(txt):
        ch = txt[i]
        if in_str:
            out.append(ch)
            if ch == chr(92): out.append(txt[i + 1]); i += 2; continue
            if ch == '"': in_str = False
            i += 1; continue
        if ch == '"': in_str = True; out.append(ch); i += 1; continue
        if txt.startswith('//', i):
            j = txt.find(chr(10), i); i = len(txt) if j < 0 else j; continue
        out.append(ch); i += 1
    return ''.join(out)
if RULES and os.path.isdir(RULES):
    for fn in os.listdir(RULES):
        if not fn.endswith('.json'): continue
        try: data = json.loads(strip_comments(io.open(os.path.join(RULES, fn), encoding='utf-8').read()))
        except Exception as e: print('rule parse failed', fn, e); continue
        if isinstance(data, dict): data = data.get('rules', [])
        for rule in data:
            for p in ([rule.get('path')] if rule.get('path') else []) + list(rule.get('paths') or []):
                covered.append((fn + ':' + str(rule.get('id', '?')), p))
def pat_to_rx(p):
    p = p.strip(); anchored = p.startswith('/')
    out = '^' if anchored else '^(?:.*/)?'
    i = 0
    while i < len(p):
        if p.startswith('**', i): out += '.*'; i += 2
        elif p[i] == '*': out += '[^/]*'; i += 1
        else: out += re.escape(p[i]); i += 1
    return re.compile(out + '$')
covered_rx = [(fn, pat_to_rx(p)) for fn, p in covered]
def rule_for(path):
    return [fn for fn, rx in covered_rx if rx.match(path)]
# ---- width estimate: average advance of Latin text ~0.52 em for Palatino/Source Serif, digits ~0.55, caps wider
def est_width(text, size):
    w = 0.0
    for ch in text:
        if ch == ' ': w += 0.25
        elif ch.isupper(): w += 0.68
        elif ch.isdigit(): w += 0.55
        elif ch in 'ilj.,\'!|:;': w += 0.28
        elif ch in 'mw': w += 0.85
        elif ord(ch) > 0x2E80: w += 1.0
        else: w += 0.52
    return w * size
ALIGN = {0: 'UpperLeft', 1: 'UpperCenter', 2: 'UpperRight', 3: 'MiddleLeft', 4: 'MiddleCenter', 5: 'MiddleRight', 6: 'LowerLeft', 7: 'LowerCenter', 8: 'LowerRight'}
rows = [json.loads(l) for l in io.open(SRC, encoding='utf-8')]
out = []
for r in rows:
    key = ''
    for c in r['comps']:
        if c['type'].startswith('LeanLocalized') and c.get('key'): key = c['key']
    for c in r['comps']:
        if c['type'] not in ('Text', 'TextMeshProUGUI'): continue
        zh = c.get('text') or ''
        text, src = (english(key) if key else (None, 'no-key'))
        sample = text if text is not None else ''
        if text is None and zh and re.search(r'[一-鿿]', zh): sample = ''   # placeholder Chinese, set at runtime
        w, h = r.get('w', 0) or 0, r.get('h', 0) or 0
        size = c.get('size') or 0
        wrap = (c.get('hOverflow') == 0) if c['type'] == 'Text' else bool(c.get('wrap'))
        trunc = (c.get('vOverflow') == 0) if c['type'] == 'Text' else (c.get('overflow') == 0)
        bestfit = bool(c.get('bestFit')) if c['type'] == 'Text' else bool(c.get('autoSize'))
        issue = []
        if w > 0 and size and w < 1.6 * size and h > 3 * size: issue.append('vertical strip (%.0fx%.0f at %d)' % (w, h, size))
        if abs(r.get('rotZ', 0) or 0) > 45: issue.append('rotated %.0f' % r['rotZ'])
        if sample and size and w > 0:
            lines = sample.split('\\n') if '\\n' in sample else sample.split('\n')
            widest = max(est_width(l, size) for l in lines)
            line_h = size * 1.25 * (c.get('lineSpacing') or 1.0) if c['type'] == 'Text' else size * 1.2
            if widest > w:
                if wrap:
                    n_lines = sum(max(1, math.ceil(est_width(l, size) / max(1, w))) for l in lines)
                    need_h = n_lines * line_h
                    if trunc and need_h > h and not bestfit: issue.append('wraps to %d lines, needs %.0f > %.0f tall: truncated' % (n_lines, need_h, h))
                    elif trunc and need_h > h and bestfit: issue.append('best fit must shrink (%d lines at %d need %.0f > %.0f)' % (n_lines, size, need_h, h))
                    elif not trunc and need_h > h: issue.append('wraps to %d lines and overflows the box vertically' % n_lines)
                else:
                    issue.append('single line %.0f px in a %.0f px box: clipped/overflowing' % (widest, w))
            elif len(lines) * line_h > h and trunc and not bestfit and len(lines) > 1:
                issue.append('%d lines need %.0f > %.0f tall' % (len(lines), len(lines) * line_h, h))
        if src == 'missing' and key: issue.append('key missing in StringTable')
        if not issue: continue
        out.append({'path': r['path'], 'file': r['file'], 'active': r['active'], 'type': c['type'], 'w': w, 'h': h, 'size': size, 'align': ALIGN.get(c.get('align'), c.get('align')), 'wrap': wrap, 'trunc': trunc, 'bestFit': bestfit, 'min': c.get('min'), 'max': c.get('max'), 'key': key, 'zh': zh[:30].replace('\n', '\\n'), 'en': (sample or '')[:80].replace('\n', '\\n'), 'src': src, 'rules': ','.join(rule_for(r['path'])), 'issue': '; '.join(issue)})
with io.open(OUT, 'w', encoding='utf-8', newline='') as f:
    w = csv.writer(f, delimiter='\t', lineterminator='\n')
    w.writerow(['path', 'file', 'active', 'type', 'w', 'h', 'size', 'align', 'wrap', 'trunc', 'bestFit', 'min', 'max', 'key', 'zh', 'en', 'src', 'rules', 'issue'])
    for o in out: w.writerow([o[k] for k in ['path', 'file', 'active', 'type', 'w', 'h', 'size', 'align', 'wrap', 'trunc', 'bestFit', 'min', 'max', 'key', 'zh', 'en', 'src', 'rules', 'issue']])
print('flagged', len(out), 'of', sum(1 for r in rows for c in r['comps'] if c['type'] in ('Text', 'TextMeshProUGUI')), 'texts; already under a rule:', sum(1 for o in out if o['rules']))
print(collections.Counter(o['issue'].split(' (')[0].split(':')[0][:40] for o in out).most_common(8))
