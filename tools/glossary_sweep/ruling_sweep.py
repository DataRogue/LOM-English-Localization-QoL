"""A newly ruled term across the text players see: split the lines that break a ruling into exact swaps and judgment cases.

  python ruling_sweep.py RULINGS.json --out DIR

RULINGS.json: [{"zh": "大公子", "form": "Eldest Young Lord", "swap": ["Young Lord", "young lord"], "base": "公子"}, ...]
  form   the ruled English; a line conforms when its English holds the form (any case) as often as its Chinese holds the term
  swap   the retired forms, longest first where one contains another ("The Third Fragrance", "the Third Fragrance", "Third
         Fragrance"): each whole-word match that is not part of the form itself is replaced by the form
  base   optional: a line is an exact swap only when its Chinese holds the base no more often than the term (大公子: no other
         公子 in the line, so a "Young Lord" there can only be the 大公子)
Every table row and strings override (the text players see for the key) whose Chinese holds a term more often than its
English holds the form is either
  - an exact swap: the missing occurrences are all retired forms, no "a"/"an" stands before one, and the base check holds.
    Written to DIR/edits.json ([{key, old, new, why}], for tools/scene_audit/rowfix.py apply --edits);
  - or a judgment case (another rendering, a name or pronoun, counts that do not match): DIR/issues.json ([{key, issue}],
    for rowfix.py chunks), for the Sonnet fixer and refuter.
swap(zh, en, rulings) is also what rowfix.py apply --rulings uses on the scene lines players can still see.
"""
import os, io, re, sys, csv, glob, json, argparse, collections
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..'))
import lom_paths
from migrate_standalone import table_map

BS, CR, LF = chr(92), chr(13), chr(10)


def _count_form(en, form):
    return len(re.findall(r'(?<![A-Za-z])' + re.escape(form) + r'(?![A-Za-z])', en, re.I))


def _swap_spans(en, r):
    """Whole-word matches of the retired forms that do not overlap a match of the ruled form: [(start, end)]."""
    taken = [(m.start(), m.end()) for m in re.finditer(r'(?<![A-Za-z])' + re.escape(r['form']) + r'(?![A-Za-z])', en, re.I)]
    spans = []
    for s in r['swap']:
        for m in re.finditer(r'(?<![A-Za-z])' + re.escape(s) + r'(?![A-Za-z])', en):
            a, b = m.start(), m.end()
            if any(a < y and x < b for x, y in taken + spans):
                continue
            spans.append((a, b))
    return sorted(spans)


def check(zh, en, r):
    """(needs work, exact swap text or None, why) for one line and one ruling."""
    n = zh.count(r['zh'])
    if not n:
        return False, None, ''
    have = _count_form(en, r['form'])
    if have >= n:
        return False, None, ''
    spans = _swap_spans(en, r)
    if not spans:
        return True, None, 'no retired form to swap'
    if have + len(spans) != n:
        return True, None, 'counts differ (Chinese %d, ruled %d, retired %d)' % (n, have, len(spans))
    if r.get('base') and zh.count(r['base']) > n:
        return True, None, 'the Chinese has another %s' % r['base']
    for a, _ in spans:
        if re.search(r'(?:^|[^A-Za-z])(?:a|an|A|An)\s+$', en[:a]):
            return True, None, 'an article stands before a retired form'
    out, pos = [], 0
    for a, b in spans:
        out.append(en[pos:a])
        out.append(r['form'])
        pos = b
    out.append(en[pos:])
    return True, ''.join(out), 'retired form swapped'


def swap(zh, en, rulings):
    """en with every ruling's exact swap applied (None when a ruling needs judgment or nothing changes)."""
    cur = en
    for r in rulings:
        need, new, _ = check(zh, cur, r)
        if need and new is None:
            return None
        if new is not None:
            cur = new
    return cur if cur != en else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('rulings')
    ap.add_argument('--out', required=True)
    a = ap.parse_args()
    rulings = json.load(open(a.rulings, encoding='utf-8'))
    zh = {}
    for line in open(lom_paths.BASE_REF_SOURCE, encoding='utf-8'):
        k, _, v = line.rstrip(LF).partition('\t')
        zh[k] = v.replace(BS + 'r' + BS + 'n', LF).replace(BS + 'n', LF).replace(BS + 'r', '').replace(BS + 't', '\t').replace(BS + BS, BS)
    gt = os.path.join(lom_paths.REAL_LOCALIZATION, 'scene_audit', 'game_text.json')
    if os.path.exists(gt):
        for k, v in json.load(open(gt, encoding='utf-8'))['sources'].get('zh-tw', {}).items():
            zh.setdefault(k, v.replace(BS + 'n', LF))
    en = table_map(open(lom_paths.TABLE, encoding='utf-8-sig').read(), True)
    ov = {}
    for f in sorted(glob.glob(os.path.join(lom_paths.STRINGS_DIR, '*.csv'))):
        for row in csv.reader(io.StringIO(io.open(f, encoding='utf-8', newline='').read())):
            if len(row) >= 2 and '/' in row[0] and not row[0].startswith('#'):
                ov.setdefault(row[0], row[1].replace(BS + 'n', LF))
    edits, issues, counts = [], [], collections.Counter()
    for k in sorted(set(en) | set(ov)):
        z = zh.get(k)
        if not z:
            continue
        e = ov.get(k, en.get(k, '')).replace(CR + LF, LF)
        whys, cur, judged = [], e, False
        for r in rulings:
            need, new, why = check(z, cur, r)
            if not need:
                continue
            if new is None:
                judged = True
                whys.append('%s is ruled "%s" (owner, 2026-09-27); this line: %s' % (r['zh'], r['form'], why))
                counts[r['zh'] + ' judgment'] += 1
            else:
                cur = new
                counts[r['zh'] + ' swap'] += 1
        if judged:
            issues.append({'key': k, 'issue': ' '.join(whys) + '. Use the ruled form wherever the line uses another rendering of the '
                           'term; keep a name, a pronoun or a lower-case generic mention where the English reads naturally without the title.'})
        elif cur != e:
            edits.append({'key': k, 'old': e, 'new': cur, 'why': 'owner ruling 2026-09-27: ' + ', '.join(
                '%s = %s' % (r['zh'], r['form']) for r in rulings if z.count(r['zh']))})
    os.makedirs(a.out, exist_ok=True)
    json.dump(edits, io.open(os.path.join(a.out, 'edits.json'), 'w', encoding='utf-8', newline=LF), ensure_ascii=False, indent=0)
    json.dump(issues, io.open(os.path.join(a.out, 'issues.json'), 'w', encoding='utf-8', newline=LF), ensure_ascii=False, indent=0)
    print(json.dumps({'exact_swaps': len(edits), 'judgment_cases': len(issues), 'by_term': dict(counts)}, ensure_ascii=False))


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    main()
