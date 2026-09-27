"""Scene-only audit, follow-up: fix the live table rows and strings overrides the audit found wrong, and bring along the scene
lines that show the same text.

  python rowfix.py chunks ISSUES.json [--per 5]
      ISSUES.json is a list of {"key": "Story/D_S0602_01_019", "issue": "what is wrong with it"}. Writes scene_audit/rowfix/
      p_NNNN.json and manifest.json. A key with a strings override (workspace/strings/*.csv) is judged on the override: that is
      the text players see. Each line gives the Chinese (with its markup), that English and where it lives, the issue, the
      rows around it (story keys) or the story lines before the dice check (DiceHeader), and the established English of the
      names in it.
  python rowfix.py apply JOURNAL.jsonl ... [--arbiter FILE] [--live] [--keep-record KEYS|@FILE|all]
      Applies the rewrites every verdict accepts (review:N, review-rest:N and verify:N results of rowfix_workflow.js) when they
      keep the markup ({size=..}, {punch=..}, <color=..>), placeholders and line breaks of the English they replace and hold no
      Chinese: table rows in the workspace table, overrides in their strings file (whose note then starts with 'was: <old>').
      Then every scene line showing the same text follows, where every row and override with that Chinese agrees: a line keyed
      by the Chinese as the table has it takes the English as it is; a line keyed by the Chinese without its markup, or with
      other line breaks, takes the English without its markup. --arbiter takes the maintainer's calls: {"reject": {id: why},
      "use": {id: text}} (use: an accepted fix as the maintainer trimmed it, e.g. without markup the fixer added).
      --live writes the workspace (backups in backups/), logs to scene_audit/ and publishes; otherwise staged copies go to
      staged/rowfix.
"""
import os, io, re, sys, csv, glob, json, shutil, argparse, datetime, collections
sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..'))
sys.path.insert(0, os.path.join(HERE, '..', 'fullpass'))
import lom_paths
import publish as PB
import rowspans
from migrate_standalone import table_map
from hygiene.scene_hygiene import rewrite_line, encode_value, raw_key

BS, CR, LF, BOM = chr(92), chr(13), chr(10), chr(0xFEFF)
CJK = re.compile('[' + chr(0x3400) + '-' + chr(0x9FFF) + chr(0xF900) + '-' + chr(0xFAFF) + ']')
TAG = re.compile(r'\{/?[a-zA-Z]+(?:=[^}]*)?\}|</?[a-zA-Z]+(?:=[^>]*)?>')
PH = re.compile(r'\{\d+(?::[^}]*)?\}|\{\$[A-Za-z0-9_]+\}')
WSNL = re.compile('[ \t' + CR + LF + chr(0x3000) + ']*' + LF + '[ \t' + CR + LF + chr(0x3000) + ']*')
KEEP_RECORD = lom_paths.take_keep_record()
DIR = os.path.join(lom_paths.REAL_LOCALIZATION, 'scene_audit', 'rowfix')


def natural(k):
    return [int(x) if x.isdigit() else x for x in re.split(r'(\d+)', k or '')]


def unescape(v):
    return v.replace(BS + 'r' + BS + 'n', CR + LF).replace(BS + 'n', LF).replace(BS + 'r', CR).replace(BS + 't', '\t').replace(BS + BS, BS)


def lf(s):
    return s.replace(CR + LF, LF)


def ft(s):
    return WSNL.sub(lambda m: '' if m.group(0).count(LF) == 1 else m.group(0), s.strip())


def strip_tags(s):
    return TAG.sub('', s)


def load():
    """(chinese per key with markup, table English per key, overrides {key: [file, row index]}, rows of each strings file)."""
    zh = {}
    for line in open(lom_paths.BASE_REF_SOURCE, encoding='utf-8'):
        k, _, v = line.rstrip(LF).partition('\t')
        zh[k] = unescape(v)
    gt = os.path.join(lom_paths.REAL_LOCALIZATION, 'scene_audit', 'game_text.json')
    if os.path.exists(gt):
        for k, v in json.load(open(gt, encoding='utf-8'))['sources'].get('zh-tw', {}).items():
            zh.setdefault(k, unescape(v))    # DiceHeader: the tables the harness dump misses (scan_game.py)
    en = table_map(open(lom_paths.TABLE, encoding='utf-8-sig').read(), True)
    ov, files = {}, {}
    for f in sorted(glob.glob(os.path.join(lom_paths.STRINGS_DIR, '*.csv'))):
        rows = list(csv.reader(io.StringIO(io.open(f, encoding='utf-8', newline='').read())))
        files[f] = rows
        for i, r in enumerate(rows):
            if len(r) >= 2 and '/' in r[0] and not r[0].startswith('#'):
                ov.setdefault(r[0], [f, i])
    return zh, en, ov, files


def shown(k, en, ov, files):
    """The English players see for a key: the strings override (its backslash-n is a line break) or the table row."""
    if k in ov:
        f, i = ov[k]
        return files[f][i][1].replace(BS + 'n', LF)
    return lf(en.get(k, ''))


# ---- chunks ----------------------------------------------------------------------------------------------------------------------
NAME_FAMILIES = ('Character/', 'CombatCharacter/Name/', 'CombatCharacter/Title/', 'CombatSkill/Name/', 'Book/Name/', 'Special/Name/',
                 'PlayerTalent/Name/', 'Misc/Name/', 'Equip/Name/', 'Position/Name/', 'Position/title/', 'MapPosition/Name/',
                 'Facility/Name/', 'EnemyTeam/', 'BattleSkill/Name/', 'CharacterTitle/', 'Library/Title/')


def chunks(issues_path, per):
    zh, en, ov, files = load()
    issues = json.load(open(issues_path, encoding='utf-8'))
    names = collections.defaultdict(dict)
    for k, v in zh.items():
        if k.startswith(NAME_FAMILIES) and 2 <= len(v) <= 12 and CJK.search(v) and '{' not in v and shown(k, en, ov, files):
            names[v].setdefault(shown(k, en, ov, files), k)
    story = sorted((k for k in zh if k.startswith('Story/')), key=natural)
    pos = {k: i for i, k in enumerate(story)}
    dice = {}
    for f in glob.glob(os.path.join(lom_paths.REAL_LOCALIZATION, 'gamedata', 'lua', '*.lua')):
        t = io.open(f, encoding='utf-8').read()
        for m in re.finditer(r'checkpointmanager\.Dice\("([^"]+)"', t):
            before = re.findall(r'GetStoryText\("([^"]+)"\)', t[:m.start()])[-4:]
            dice.setdefault('DiceHeader/' + m.group(1), (os.path.basename(f), ['Story/' + s for s in before]))
    lines = []
    for n, it in enumerate(issues, 1):
        k = it['key']
        if k not in zh:
            sys.exit('%s: no Chinese for this key' % k)
        d = {'id': 'R%d' % n, 'key': k, 'zh': lf(zh[k]), 'en': shown(k, en, ov, files),
             'lives_in': ('strings override ' + os.path.basename(ov[k][0])) if k in ov else 'string table', 'issue': it['issue']}
        if k in story:
            i = pos[k]
            d['before'] = [{'key': story[j], 'zh': lf(zh[story[j]]), 'en': shown(story[j], en, ov, files)} for j in range(max(0, i - 3), i)]
            d['after'] = [{'key': story[j], 'zh': lf(zh[story[j]]), 'en': shown(story[j], en, ov, files)} for j in range(i + 1, min(len(story), i + 3))]
        if k.startswith('DiceHeader/'):
            sc, before = dice.get(k, (None, []))
            if sc:
                d['script'] = sc
                d['before'] = [{'key': b, 'zh': lf(zh.get(b, '')), 'en': shown(b, en, ov, files)} for b in before if b in zh]
            d['same_header'] = [{'key': o, 'en': shown(o, en, ov, files)} for o in sorted(zh) if o != k and o.startswith('DiceHeader/') and zh[o] == zh[k]]
        hints = []
        for nm in sorted((x for x in names if x in zh[k]), key=len, reverse=True)[:8]:
            for e, key in list(names[nm].items())[:2]:
                hints.append([nm, e, key])
        if hints:
            d['names'] = hints
        lines.append(d)
    os.makedirs(DIR, exist_ok=True)
    for f in glob.glob(os.path.join(DIR, 'p_*.json')):
        os.remove(f)
    manifest = []
    for c in range(0, len(lines), per):
        part = lines[c:c + per]
        num = c // per + 1
        json.dump({'chunk': num, 'lines': part}, io.open(lom_paths.out_path(DIR, 'p_%04d.json' % num), 'w', encoding='utf-8', newline=LF),
                  ensure_ascii=False, indent=1)
        manifest.append({'chunk': num, 'ids': [x['id'] for x in part]})
    json.dump(manifest, io.open(lom_paths.out_path(DIR, 'manifest.json'), 'w', encoding='utf-8', newline=LF), ensure_ascii=False, indent=0)
    print('%d lines in %d chunks -> %s' % (len(lines), len(manifest), DIR))
    print(json.dumps({'chunks': manifest}, separators=(',', ':')))


# ---- apply -----------------------------------------------------------------------------------------------------------------------
def apply(journals, arbiter, live):
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
    out_dir = lom_paths.log_dir('scene_audit') if live else lom_paths.staged_dir('rowfix')
    arb = json.load(open(arbiter, encoding='utf-8')) if arbiter else {}
    reject, use = arb.get('reject', {}), arb.get('use', {})   # use: {id: the accepted fix as the maintainer trimmed it}
    label_of, results = {}, {}
    for jp in journals:
        for line in open(jp, encoding='utf-8'):
            try:
                e = json.loads(line)
            except ValueError:
                continue
            if e.get('type') == 'started' and e.get('agentId'):
                label_of[e['agentId']] = e.get('label', '')
            elif e.get('type') == 'result' and e.get('agentId'):
                r = e.get('result')
                if isinstance(r, str):
                    try:
                        r = json.loads(r)
                    except ValueError:
                        r = None
                lab = label_of.get(e['agentId'], '')
                if lab and isinstance(r, dict):
                    results[lab.replace(':retry', '')] = r
    zh, en, ov, files = load()
    log, skipped = [], []
    table_edits, ov_edits, changed = {}, {}, set()
    for f in sorted(glob.glob(os.path.join(DIR, 'p_*.json'))):
        ch = json.load(open(f, encoding='utf-8'))
        n = ch['chunk']
        lines = {x['id']: x for x in ch['lines']}
        items = {}
        for lab in ('review:%d' % n, 'review-rest:%d' % n):
            for x in ((results.get(lab) or {}).get('items') or []):
                if x.get('id') in lines and x['id'] not in items:
                    items[x['id']] = x
        verdicts = collections.defaultdict(list)
        for v in ((results.get('verify:%d' % n) or {}).get('verdicts') or []):
            verdicts[v.get('id')].append(v)
        for i, ln in lines.items():
            x = items.get(i)
            if x is None:
                skipped.append((i, ln['key'], ln['en'], '', 'not answered'))
                continue
            if x.get('keep') is not False or not x.get('en'):
                continue
            vs = verdicts.get(i, [])
            old, new = ln['en'], lf(use.get(i, x['en'])).strip(' \t')
            why = None
            if not vs or not all(v.get('accept') for v in vs):
                why = 'refuted: ' + '; '.join(v.get('reason', '') for v in vs if not v.get('accept'))
            elif i in reject:
                why = 'maintainer: ' + reject[i]
            elif shown(ln['key'], en, ov, files) != old:
                why = 'the English changed since the chunk was cut'
            elif new == old:
                why = 'no change'
            elif CJK.search(new):
                why = 'introduces Chinese'
            elif sorted(TAG.findall(new)) != sorted(TAG.findall(old)):
                why = 'markup differs'
            elif sorted(PH.findall(new)) != sorted(PH.findall(old)):
                why = 'placeholders differ'
            elif new.count(LF) not in (old.count(LF), lf(ln['zh']).count(LF)):
                why = 'line breaks differ'
            elif ln['key'] in ov and BS + 'n' in new:
                why = 'a literal backslash-n would read as a line break in a strings file'
            if why:
                skipped.append((i, ln['key'], old, new, why))
                continue
            k = ln['key']
            if k in ov:
                ov_edits[k] = (old, new, x.get('why', ''))
            else:
                table_edits[k] = (en[k], new, x.get('why', ''))
            changed.add(k)
            log.append((i, k, 'override' if k in ov else 'table', old, new, x.get('why', '')))
    print('accepted and checked: %d table rows, %d overrides; skipped %d' % (len(table_edits), len(ov_edits), len(skipped)))
    for s in skipped:
        print('  skipped', s[0], s[1], '-', s[4])

    # table rows, whole rows by key
    raw = io.open(lom_paths.TABLE, encoding='utf-8', newline='').read()
    bom = raw.startswith(BOM)
    raw = raw.lstrip(BOM)
    nl = CR + LF if CR + LF in raw else LF
    new_raw, applied, mismatched = rowspans.rewrite(raw, {k: (v[0], v[1]) for k, v in table_edits.items()}, nl)
    if mismatched:
        sys.exit('table rows changed under us, nothing written: %s' % sorted(mismatched))
    table_out = (BOM if bom else '') + new_raw
    en2 = table_map(table_out.lstrip(BOM), True)

    # overrides: the text column; the note keeps what the text was
    files2 = {f: [list(r) for r in rows] for f, rows in files.items()}
    touched = set()
    for k, (old, new, why) in ov_edits.items():
        f, i = ov[k]
        row = files2[f][i]
        row[1] = new.replace(LF, BS + 'n')   # a strings file writes a line break as backslash-n
        if len(row) > 2:
            old_text = old.replace(LF, BS + 'n')
            row[2] = 'was: %s | %s | %s' % (old_text, why, row[2]) if why else 'was: %s | %s' % (old_text, row[2])
        touched.add(f)
    file_out = {}
    for f in touched:
        orig = io.open(f, encoding='utf-8', newline='').read()
        buf = io.StringIO()
        csv.writer(buf, quoting=csv.QUOTE_ALL, lineterminator=LF).writerows(files[f])
        if buf.getvalue() != orig:
            sys.exit('%s does not round-trip through the csv writer; edit it by hand' % f)
        buf = io.StringIO()
        csv.writer(buf, quoting=csv.QUOTE_ALL, lineterminator=LF).writerows(files2[f])
        file_out[f] = buf.getvalue()

    # scene lines that show the changed text follow, where every row and override with that Chinese agrees
    def shown2(k):
        if k in ov:
            f, i = ov[k]
            return files2[f][i][1].replace(BS + 'n', LF)
        return lf(en2.get(k, ''))

    exact, variant = collections.defaultdict(set), collections.defaultdict(set)
    for k, v in zh.items():
        exact[lf(v)].add(k)
        variant[ft(strip_tags(lf(v)))].add(k)
    watch = {lf(zh[k]) for k in changed}
    watch_v = {ft(strip_tags(lf(zh[k]))) for k in changed}
    sraw = io.open(lom_paths.SCENE, encoding='utf-8', newline='').read()
    sbom = sraw.startswith(BOM)
    sraw = sraw.lstrip(BOM)
    snl = CR + LF if CR + LF in sraw else LF
    slines = sraw.split(snl)
    mirrored = 0
    for idx, line in enumerate(slines):
        if not line or line.startswith('//'):
            continue
        kv = PB.decode(line)
        if not kv or not kv[0]:
            continue
        key = lf(kv[0])
        if key in watch:
            cands = {shown2(k) for k in exact[key]}
        elif not TAG.search(key) and ft(key) in watch_v:
            cands = {strip_tags(shown2(k)) for k in variant[ft(key)]}
        else:
            continue
        if len(cands) != 1:
            skipped.append(('scene', 'line %d' % (idx + 1), kv[1], ' | '.join(sorted(cands)), 'the rows with this Chinese disagree'))
            continue
        val = cands.pop()
        if lf(kv[1]) == val:
            continue
        try:
            nl_ = rewrite_line(line, raw_key(line), encode_value(val))
        except ValueError:
            nl_ = None
        if nl_ is None:
            skipped.append(('scene', 'line %d' % (idx + 1), kv[1], val, 'cannot be written as a scene line'))
            continue
        slines[idx] = nl_
        mirrored += 1
        log.append(('scene', 'line %d' % (idx + 1), 'scene', kv[1], val, 'follows its row'))
    scene_out = (BOM if sbom else '') + snl.join(slines)
    print('scene lines that follow: %d' % mirrored)

    if live:
        for p in [lom_paths.TABLE, lom_paths.SCENE] + sorted(file_out):
            dst = lom_paths.backup_path(p, stamp)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copy2(p, dst)
        lom_paths.assert_writable(lom_paths.TABLE)
        lom_paths.assert_writable(lom_paths.SCENE)
        io.open(lom_paths.TABLE, 'w', encoding='utf-8', newline='').write(table_out)
        io.open(lom_paths.SCENE, 'w', encoding='utf-8', newline='').write(scene_out)
        for f, text in file_out.items():
            if not lom_paths.inside(f, lom_paths.STRINGS_DIR) or lom_paths.is_link(f):
                sys.exit('refusing to write %s' % f)
            lom_paths.guard_write(f)
            io.open(f, 'w', encoding='utf-8', newline='').write(text)
        print('LIVE: workspace written (backups in %s)' % lom_paths.BACKUPS)
    else:
        io.open(lom_paths.out_path(out_dir, 'StringTable.csv'), 'w', encoding='utf-8', newline='').write(table_out)
        io.open(lom_paths.out_path(out_dir, 'scene_text.txt'), 'w', encoding='utf-8', newline='').write(scene_out)
        for f, text in file_out.items():
            io.open(lom_paths.out_path(out_dir, os.path.basename(f)), 'w', encoding='utf-8', newline='').write(text)
        print('staged copies written to', out_dir)
    with io.open(lom_paths.out_path(out_dir, 'rowfix_changes.%s.tsv' % stamp), 'w', encoding='utf-8', newline='') as fh:
        w = csv.writer(fh, delimiter='\t', lineterminator=LF)
        w.writerow(['id', 'key', 'where', 'old', 'new', 'why'])
        w.writerows(log)
    with io.open(lom_paths.out_path(out_dir, 'rowfix_skipped.%s.tsv' % stamp), 'w', encoding='utf-8', newline='') as fh:
        w = csv.writer(fh, delimiter='\t', lineterminator=LF)
        w.writerow(['id', 'key', 'old', 'new', 'why'])
        w.writerows(skipped)
    if live:
        lom_paths.publish(keep_record=KEEP_RECORD)


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest='cmd', required=True)
    c = sub.add_parser('chunks')
    c.add_argument('issues')
    c.add_argument('--per', type=int, default=5)
    p = sub.add_parser('apply')
    p.add_argument('journals', nargs='+')
    p.add_argument('--arbiter')
    p.add_argument('--live', action='store_true')
    a = ap.parse_args()
    if a.cmd == 'chunks':
        chunks(a.issues, a.per)
    else:
        apply(a.journals, a.arbiter, a.live)
