"""Scene-only audit, step 2: sort every scene-only line into dead and live, and cut the live ones into review chunks.

A scene-only line is a line of the workspace scene file whose Chinese is no row of the game's string table (the definition
tools/fullpass/extract_all.py used: 3,220 lines in the full pass). Most of them were copied from older game builds. Each is
matched against what the installed game can still draw (scan_game.py's game_text.json, the Lua scripts in gamedata/lua), the
way the plugin's scene lookup matches text (SceneDictionary: exact, then with single line breaks and the space around them
removed; with rich text, the pieces between tags):
  dead (not reviewed)
    simplified      a Simplified-Chinese key (the mod runs on the Traditional Chinese slot only)
    internal        an internal label that never displays: object names, flag names, debug and test labels
    not-in-game     the Chinese is nowhere in the installed game: an older wording of a line the game has since changed, or text
                    it removed. No text the game draws can match it
    junk            a key without Chinese, a half-translated capture, a broken tag fragment
  live (reviewed)
    table-variant   a row of the string table with other line breaks, or without its markup ({size=24}...): the English the
                    scene layer gives that row when the table layer does not answer. Should say what the row's English says
    table-fragment  the part of a row between two rich-text tags
    dice-header     a dice-check header (DiceHeader/*, a table the harness dump misses); in game the strings override answers
                    first, so it should say what the override says
    ui              a label in a prefab (menus, buttons, credits), with its object path
    data            text in a game data asset (dates, duel names, story data)
    code, lua       a string literal in the game's code or in a Lua script
    composed        text the game builds at run time (numbers, dates, tooltips) from pieces
Writes LOM_Localization/scene_audit/: classified.tsv (every scene-only line: line, class, detail, key, English) and
chunks/p_NNNN.json + chunks/manifest.json (the live lines, about --per lines and --chars Chinese characters a chunk, with the
row or override they should agree with, the story lines before a dice check, and the established English of the names in them).
Usage: python classify.py [--per 10] [--chars 1100] [--also not-in-game]      (run scan_game.py first)
"""
import os, re, io, sys, csv, glob, json, argparse, difflib, collections
sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..'))
import lom_paths
import publish as PB
from migrate_standalone import table_map
from hygiene.scene_hygiene import raw_key

BS = chr(92)
ap = argparse.ArgumentParser()
ap.add_argument('--per', type=int, default=10)
ap.add_argument('--chars', type=int, default=1100)
ap.add_argument('--also', default='', help='dead classes to review as well, comma-separated (e.g. not-in-game)')
a = ap.parse_args()
OUT = lom_paths.log_dir('scene_audit')
GT = os.path.join(OUT, 'game_text.json')
if not os.path.exists(GT):
    sys.exit('%s is missing: run scan_game.py first' % GT)
game = json.load(open(GT, encoding='utf-8'))
st = os.stat(os.path.join(lom_paths.GAME, 'Mortal_Data', 'sharedassets0.assets'))
if (st.st_size, int(st.st_mtime)) != (game['game']['sharedassets0_size'], game['game']['sharedassets0_mtime']):
    sys.exit('the game changed since %s was written: run scan_game.py again' % GT)

CJK = re.compile(r'[\u3400-\u9fff\uf900-\ufaff]')
TAG = re.compile(r'\{/?[a-zA-Z]+(?:=[^}]*)?\}|</?[a-zA-Z]+(?:=[^>]*)?>')
WSNL = re.compile(r'[ \t\r\n\u3000]*\n[ \t\r\n\u3000]*')


def ft(s):
    """The plugin's fully trimmed lookup form of Chinese text: outer space gone, a single line break and the space around
    it removed (a double one stays)."""
    return WSNL.sub(lambda m: '' if m.group(0).count('\n') == 1 else m.group(0), s.strip())


def unescape(v):
    """game_source.tsv / table-source escapes: backslash-r-backslash-n, backslash-n, backslash-t, double backslash."""
    return v.replace(BS + 'r' + BS + 'n', '\r\n').replace(BS + 'n', '\n').replace(BS + 'r', '\r').replace(BS + 't', '\t').replace(BS + BS, BS)


def natural(k):
    return [int(x) if x.isdigit() else x for x in re.split(r'(\d+)', k or '')]


# ---- the game's string table and our English for it ---------------------------------------------------------------------------
source = {}
for line in open(lom_paths.BASE_REF_SOURCE, encoding='utf-8'):
    k, _, v = line.rstrip('\n').partition('\t')
    source[k] = unescape(v)
tables = dict(source)
for k, v in game['sources'].get('zh-tw', {}).items():
    tables.setdefault(k, unescape(v))          # the tables the dump misses (DiceHeader)
en = table_map(open(lom_paths.TABLE, encoding='utf-8-sig').read(), True)
override = {}
for f in sorted(glob.glob(os.path.join(lom_paths.STRINGS_DIR, '*.csv'))):
    for r in csv.reader(open(f, encoding='utf-8-sig')):
        if len(r) >= 2 and '/' in r[0] and not r[0].startswith('#'):
            override.setdefault(r[0], r[1])


def english(k):
    return override.get(k) or en.get(k, '')


# ---- scene-only lines (extract_all.py's definition) ----------------------------------------------------------------------------
zh_keys = set(v.replace('\r\n', '\n').replace('\n', BS + 'n') for v in source.values())
raw = io.open(lom_paths.SCENE, encoding='utf-8-sig', newline='').read()
nl = '\r\n' if '\r\n' in raw else '\n'
scene_only = []
for n, line in enumerate(raw.split(nl), 1):
    if not line or line.startswith('//'):
        continue
    kv = PB.decode(line)
    rk = raw_key(line)
    if kv is None or not kv[0] or not kv[1] or rk == line or rk in zh_keys:
        continue
    scene_only.append({'line': n, 'text': line, 'zh': rk, 'en': line[len(rk) + 1:], 'u': kv[0]})

# ---- everything the game can draw, in the plugin's lookup forms ------------------------------------------------------------------
index = collections.defaultdict(set)     # form -> {(kind, detail)}


def add(text, kind, detail):
    if not text or not CJK.search(text):
        return
    for form in (text, ft(text)):
        index[form].add((kind, detail))
    if TAG.search(text):
        stripped = TAG.sub('', text)
        for form in (stripped, ft(stripped)):
            index[form].add((kind + '~markup', detail))
        for frag in TAG.split(text):
            if CJK.search(frag):
                for form in (frag, frag.strip(), ft(frag)):
                    index[form].add((kind + '~fragment', detail))


for k, v in tables.items():
    add(v, 'dice' if k.startswith('DiceHeader/') else 'table', k)
UI_CLASSES = {'Text', 'TextMeshProUGUI', 'TextMeshPro', 'LeanLocalizedText', 'LeanLocalizedTextMeshProUGUI', 'Dropdown',
              'InputField', 'Button', 'Toggle'}
INTERNAL_CLASSES = {'FlagData', 'StoryMappingItem', 'AddressableData', 'SoundData', 'MusicData', 'SpriteData', 'StatGroupVariable',
                    'GameEvent', 'MissionCheckData'}
DEBUG_PATH = re.compile(r'/(?:Test|Debug|Cheat)[A-Za-z]*(?:/| \(|$)|/PreviewPanel/')   # dev-only panels (not the credits' External Test)
for s, hits in game['objects'].items():
    for fn, tn, cls, path in hits:
        if tn == 'GameObject' or cls in INTERNAL_CLASSES or (path and DEBUG_PATH.search(path)):
            add(s, 'internal', '%s %s' % (tn if tn == 'GameObject' else cls, path or fn))
        elif tn == 'TextAsset':
            add(s, 'lua', cls)
        elif cls in UI_CLASSES:
            add(s, 'ui', '%s %s' % (cls, path or fn))
        else:
            add(s, 'data', '%s %s' % (cls, fn))
for s in game['code']:
    add(s, 'code', '')
LUA = os.path.join(lom_paths.REAL_LOCALIZATION, 'gamedata', 'lua')
lua_text = {}
for f in glob.glob(os.path.join(LUA, '*.lua')):
    t = io.open(f, encoding='utf-8').read()
    lua_text[os.path.basename(f)[:-4]] = t
    for m in re.finditer(r'"([^"\n]*[\u3400-\u9fff][^"\n]*)"', t):
        add(m.group(1), 'lua', os.path.basename(f))
SIMPLIFIED = game['simplified']
TRADITIONAL_ONLY = set(SIMPLIFIED.values()) - set(SIMPLIFIED)

# ---- nearest current row (for the record and for composed lines) -----------------------------------------------------------------
grams = collections.defaultdict(set)
for k, v in tables.items():
    s = v.replace('\r', '').replace('\n', '')
    for i in range(len(s) - 1):
        grams[s[i:i + 2]].add(k)


def nearest(u):
    s = u.replace('\r', '').replace('\n', '')
    cnt = collections.Counter()
    for i in range(len(s) - 1):
        post = grams.get(s[i:i + 2])
        if post and len(post) < 3000:
            cnt.update(post)
    best = (None, 0.0)
    for k, _ in cnt.most_common(12):
        r = difflib.SequenceMatcher(None, s, tables[k].replace('\r', '').replace('\n', ''), autojunk=False).ratio()
        if r > best[1]:
            best = (k, r)
    return best


# ---- names with an established English (the table's name families) ----------------------------------------------------------
NAME_FAMILIES = ('Character/', 'CombatCharacter/Name/', 'CombatCharacter/Title/', 'CombatSkill/Name/', 'Book/Name/', 'Special/Name/',
                 'PlayerTalent/Name/', 'Misc/Name/', '_Misc/Name/', 'Equip/Name/', 'Position/Name/', 'Position/title/',
                 'MapPosition/Name/', 'Facility/Name/', 'Flag/Name/', 'EnemyTeam/', 'BattleSkill/Name/', 'PlayerInfo/Title/',
                 'CharacterTitle/', 'Library/Title/')
freq = collections.Counter()
names = collections.defaultdict(dict)    # Chinese name -> {English: key}
for k, v in source.items():
    if k.startswith(NAME_FAMILIES) and 2 <= len(v) <= 12 and CJK.search(v) and '{' not in v and english(k):
        names[v].setdefault(english(k), k)
corpus = '\n'.join(v for k, v in source.items() if k.startswith(('Story/', 'LegendInfo/')))
for nm in names:
    freq[nm] = corpus.count(nm) if len(nm) <= 2 else 0


# ---- classify ----------------------------------------------------------------------------------------------------------------------
NAME_SUFFIX = re.compile(r'^(.+?)[-_]?\d+$')
HALF_EN = re.compile(r'[A-Za-z]{3,}')


def classify(x):
    u = x['u']
    hits = collections.defaultdict(set)
    for form in (u, ft(u)):
        for kind, detail in index.get(form, ()):
            hits[kind].add(detail)
    in_tables = any(k.split('~')[0] in ('table', 'dice') for k in hits)
    if not in_tables and any(c in SIMPLIFIED for c in u) and not any(c in TRADITIONAL_ONLY for c in u):
        return 'simplified', ''
    whole = {k: v for k, v in hits.items() if '~' not in k or k.endswith('~markup')}
    for kind, cls in (('table', 'table-variant'), ('dice', 'dice-header'), ('ui', 'ui'), ('data', 'data'), ('code', 'code'),
                      ('lua', 'lua')):
        found = sorted(set(whole.get(kind, ())) | set(whole.get(kind + '~markup', ())), key=natural)
        if found:
            return cls, ' | '.join(found[:4])
    for kind, cls in (('table~fragment', 'table-fragment'), ('dice~fragment', 'dice-header'), ('ui~fragment', 'ui'),
                      ('data~fragment', 'data'), ('code~fragment', 'code'), ('lua~fragment', 'lua')):
        if hits.get(kind):
            return cls, ' | '.join(sorted(hits[kind], key=natural)[:4])
    if hits.get('internal') or hits.get('internal~markup') or hits.get('internal~fragment'):
        return 'internal', ' | '.join(sorted(hits.get('internal') or hits.get('internal~markup') or hits['internal~fragment'])[:2])
    if not CJK.search(u):
        return 'junk', 'no Chinese in the key'
    if 'TextMeshFont_' in u or HALF_EN.search(u) or re.search(r'<[a-z]+$|\{[a-z]+$', u):
        return 'junk', 'half-translated capture or broken tag'
    if re.search('測試|测试|旗標', u):
        return 'internal', 'test or flag label'
    m = NAME_SUFFIX.match(u)
    if m and not re.search(r'[\s+\-%：:]', u.replace(m.group(1), '', 1)) and all(p in names or p == '傳奇' for p in m.group(1).split('-') if p):
        return 'internal', 'object name with a number (%s)' % m.group(1)
    k, r = nearest(u)
    x['near'] = (k, r)
    if re.search(r'\d', u) or re.fullmatch(r'第.{1,3}年.{1,3}月[上中下]旬|.{1,3}月[‧·．][上中下]旬', u) or re.search(r'\[決鬥\]|\[戰役\]', u):
        if r >= 0.9 and len(CJK.findall(u)) >= 6:
            return 'not-in-game', '%s %.2f' % (k, r)
        return 'composed', '%s %.2f' % (k, r) if k else ''
    return 'not-in-game', '%s %.2f' % (k, r) if k else 'no similar row'


for x in scene_only:
    x['class'], x['detail'] = classify(x)
LIVE = ['dice-header', 'table-variant', 'table-fragment', 'ui', 'data', 'code', 'lua', 'composed']
REVIEW = LIVE + [c for c in a.also.split(',') if c]
counts = collections.Counter(x['class'] for x in scene_only)

# ---- context for the reviewers ---------------------------------------------------------------------------------------------------
def name_hints(u):
    out, taken = [], [False] * len(u)
    for nm in sorted((n for n in names if n in u), key=len, reverse=True):
        if len(nm) == 2 and freq[nm] > 800:
            continue                       # a common word (弟子, 師兄): the glossary covers it
        for m in re.finditer(re.escape(nm), u):
            if not any(taken[m.start():m.end()]):
                for i in range(m.start(), m.end()):
                    taken[i] = True
                for e, k in list(names[nm].items())[:3]:
                    out.append([nm, e, k])
                break
        if len(out) >= 10:
            break
    return out


def ref(k):
    d = {'key': k, 'zh': tables.get(k, ''), 'en': english(k)}
    if k in override and en.get(k) and en[k] != override[k]:
        d['note'] = 'the game shows the strings override (this en); the table row says: ' + en[k]
    return d


story_keys = sorted((k for k in source if k.startswith('Story/')), key=natural)
story_pos = {k: i for i, k in enumerate(story_keys)}
dice_ctx = {}
RX_DICE = re.compile(r'checkpointmanager\.Dice\("([^"]+)"')
RX_STORY = re.compile(r'GetStoryText\("([^"]+)"\)')
for name, t in lua_text.items():
    for m in RX_DICE.finditer(t):
        before = RX_STORY.findall(t[:m.start()])[-3:]
        dice_ctx.setdefault('DiceHeader/' + m.group(1), (name, ['Story/' + s for s in before]))


def context(x):
    c = {}
    cls, det = x['class'], x['detail']
    keys = [s.strip() for s in det.split(' | ')] if cls in ('table-variant', 'table-fragment', 'dice-header') else []
    if keys:
        c['ref'] = [ref(k) for k in keys[:2]]
        if cls == 'table-fragment':
            c['note'] = 'this line is the part of the row between rich-text tags; its English is the matching part of the row'
        k0 = keys[0]
        if k0 in story_pos:
            i = story_pos[k0]
            c['neighbours'] = [{'key': story_keys[j], 'zh': source[story_keys[j]], 'en': english(story_keys[j])}
                               for j in (i - 1, i + 1) if 0 <= j < len(story_keys)]
        if cls == 'dice-header':
            sc, before = dice_ctx.get(k0, (None, []))
            if sc:
                c['script'] = sc
                c['before'] = [{'key': k, 'zh': source.get(k, ''), 'en': english(k)} for k in before if k in source]
            c['note'] = ('a dice-check header: the title of the dice panel (the code adds "......"). In game the strings '
                         'override answers first; this line is its fallback, so it should say what the override says')
    elif cls in ('ui', 'data', 'code', 'lua'):
        c['where'] = det
    elif cls == 'composed' and x.get('near', (None, 0))[0] and x['near'][1] >= 0.5:
        c['ref'] = [ref(x['near'][0])]
        c['note'] = 'built by the game at run time; the nearest row is shown for its names and terms'
    elif cls == 'not-in-game' and x.get('near', (None, 0))[0] and x['near'][1] >= 0.5:
        c['ref'] = [ref(x['near'][0])]
        c['note'] = 'an older wording of this row (the game now shows the row); its names and terms still apply'
    h = name_hints(x['u'])
    if h:
        c['names'] = h
    return c


# ---- output ----------------------------------------------------------------------------------------------------------------------
live = [x for x in scene_only if x['class'] in REVIEW]


def order(x):
    det = x['detail'].split(' | ')[0]
    return (REVIEW.index(x['class']), natural(det) if x['class'] in ('dice-header', 'table-variant', 'table-fragment', 'composed', 'not-in-game') else det, x['line'])


live.sort(key=order)
chunk_dir = os.path.join(OUT, 'chunks')
os.makedirs(chunk_dir, exist_ok=True)
for f in glob.glob(os.path.join(chunk_dir, 'p_*.json')):
    os.remove(f)
chunks, cur, chars = [], [], 0
for x in live:
    n = len(CJK.findall(x['u']))
    if cur and (len(cur) >= a.per or chars + n > a.chars or cur[-1]['class'] != x['class']):
        chunks.append(cur)
        cur, chars = [], 0
    cur.append(x)
    chars += n
if cur:
    chunks.append(cur)
manifest = []
for i, ch in enumerate(chunks, 1):
    lines = []
    for x in ch:
        d = {'id': 'L%d' % x['line'], 'kind': x['class'], 'zh': x['zh'], 'en': x['en']}
        d.update(context(x))
        lines.append(d)
    body = {'chunk': i, 'kind': ch[0]['class'], 'lines': lines}
    json.dump(body, io.open(lom_paths.out_path(chunk_dir, 'p_%04d.json' % i), 'w', encoding='utf-8', newline='\n'), ensure_ascii=False, indent=1)
    manifest.append({'chunk': i, 'kind': ch[0]['class'], 'lines': len(ch), 'ids': ['L%d' % x['line'] for x in ch],
                     'chars': sum(len(CJK.findall(x['u'])) for x in ch)})
json.dump(manifest, io.open(lom_paths.out_path(chunk_dir, 'manifest.json'), 'w', encoding='utf-8', newline='\n'), ensure_ascii=False, indent=0)
with io.open(lom_paths.out_path(OUT, 'classified.tsv'), 'w', encoding='utf-8', newline='\n') as f:
    w = csv.writer(f, delimiter='\t', lineterminator='\n')
    w.writerow(['line', 'class', 'detail', 'key', 'english'])
    for x in scene_only:
        w.writerow([x['line'], x['class'], x['detail'], x['zh'], x['en']])
print(json.dumps({'scene_only': len(scene_only), 'classes': dict(counts.most_common()),
                  'dead': sum(v for k, v in counts.items() if k not in LIVE), 'live': len(live), 'chunks': len(chunks),
                  'chunks_by_kind': dict(collections.Counter(c[0]['class'] for c in chunks))}, ensure_ascii=False, indent=1))
