"""Compare two LOM_UI_EN harness dumps (dump NAME) object by object: text, text layout and rect.

  python cmp_text.py BEFORE.txt AFTER.txt [--rects] [--limit N]

A dump line is 'indent name [components] rect=... local=... | T:"text" fs=.. bf=.. al=.. h=.. v=.. ls=.. font=.. pref=..'
(TMP lines use 'TMP:"..."'). Paths are rebuilt from the indentation; duplicate sibling names get [i] suffixes in dump order.
Reports: objects only in one dump, text changes, still-Chinese text, and layout property changes (fs/bf/al/h/v/ls/font,
and with --rects the screen rect).
"""
import re, sys
from collections import OrderedDict

sys.stdout.reconfigure(encoding='utf-8')
HAN = re.compile('[一-鿿㐀-䶿가-힯]')
PROPS = ('fs', 'bf', 'al', 'h', 'v', 'ls', 'font', 'as', 'ov', 'ww')


def parse(path):
    rows = OrderedDict()
    stack = []
    counts = {}
    for line in open(path, encoding='utf-8-sig'):
        if line.startswith('#') or not line.strip():
            continue
        depth = len(line) - len(line.lstrip(' '))
        body = line.strip()
        name = body.split(' [')[0]
        if name.startswith('(inactive) '):
            name = name[len('(inactive) '):]
        stack = stack[:depth]
        parent = '/'.join(stack)
        k = (parent, name)
        counts[k] = counts.get(k, 0) + 1
        stack.append(name if counts[k] == 1 else '%s[%d]' % (name, counts[k] - 1))
        p = '/' + '/'.join(stack)
        info = {'line': body}
        m = re.search(r'rect=(\S+)', body)
        info['rect'] = m.group(1) if m else None
        m = re.search(r'\| (T|TMP):"((?:[^"\\]|\\.)*)"(.*)$', body)
        if m:
            info['kind'] = m.group(1)
            info['text'] = m.group(2)
            rest = m.group(3)
            mm = re.search(r'font=(.*?)(?= pref=| key=| !|$)', rest)
            if mm:
                info['font'] = mm.group(1)
            for prop in PROPS:
                if prop == 'font':
                    continue
                mm = re.search(r'\b' + prop + r'=(\S+)', rest)
                if mm:
                    info[prop] = mm.group(1)
        rows[p] = info
    return rows


def main():
    a, b = parse(sys.argv[1]), parse(sys.argv[2])
    rects = '--rects' in sys.argv
    limit = 60
    if '--limit' in sys.argv:
        limit = int(sys.argv[sys.argv.index('--limit') + 1])
    only_a = [p for p in a if p not in b]
    only_b = [p for p in b if p not in a]
    text_diff, still_cjk, layout = [], [], []
    for p, x in a.items():
        y = b.get(p)
        if y is None:
            continue
        if 'text' in x or 'text' in y:
            if x.get('text') != y.get('text'):
                text_diff.append((p, x.get('text'), y.get('text')))
            if y.get('text') and HAN.search(y['text']):
                still_cjk.append((p, y['text']))
            ch = [(k, x.get(k), y.get(k)) for k in PROPS if x.get(k) != y.get(k)]
            if ch:
                layout.append((p, ch))
        if rects and x.get('rect') != y.get('rect'):
            layout.append((p, [('rect', x.get('rect'), y.get('rect'))]))
    print('objects: %d vs %d; only before %d, only after %d' % (len(a), len(b), len(only_a), len(only_b)))
    print('text changes: %d; Chinese/Korean still shown after: %d; layout changes: %d' % (len(text_diff), len(still_cjk), len(layout)))
    for p, x, y in text_diff[:limit]:
        print('TEXT  %s\n      - %s\n      + %s' % (p, (x or '')[:160], (y or '')[:160]))
    for p, t in still_cjk[:limit]:
        print('CJK   %s: %s' % (p, t[:120]))
    for p, ch in layout[:limit]:
        print('PROP  %s: %s' % (p, ', '.join('%s %s -> %s' % c for c in ch)))
    for p in only_a[:limit // 3]:
        print('GONE  ' + p)
    for p in only_b[:limit // 3]:
        print('NEW   ' + p)


if __name__ == '__main__':
    main()
