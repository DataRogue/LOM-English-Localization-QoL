"""Scene-only audit, step 1: list every piece of Chinese text the installed game can still draw, and where it lives.

The scene file (workspace/translation/scene/scene_text.txt) holds lines whose Chinese is no row of the string table: prefab
labels, credits, dice headers, text the game builds at run time, and many lines copied from older game builds. To tell the
lines the game can still show from dead ones, this reads the game itself (Mortal_Data, read only) and writes
LOM_Localization/scene_audit/game_text.json:
  sources      every LeanLocalization CSV text asset (the string tables), by language: {lang: {key: text}}. The harness
               srcdump (base_ref/game_source.tsv) misses the tables only a later scene loads (DiceHeader, in the story scene).
  simplified   {simplified char: traditional char}: the characters that differ between the zh-cn and zh-tw tables for the
               same key (the mod runs on the Traditional Chinese slot only, so a Simplified line never shows)
  objects      {text: [[file, type, class, path], ...]}: Chinese strings in prefabs, scenes and data assets (MonoBehaviour,
               GameObject names, text assets other than the string tables), with the object's hierarchy path in scene files
  code         Chinese string literals of the game's own assemblies (Mortal_Data/Managed/Mortal*.dll and the rest)
Usage: python scan_game.py        (about a minute; needs UnityPy with its TypeTreeGenerator, as tools/nametips does)
"""
import os, re, sys, glob, json, struct, collections
sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..'))
import lom_paths

DATA = os.path.join(lom_paths.GAME, 'Mortal_Data')
BOM = chr(0xFEFF)
CJK = re.compile(r'[\u3400-\u9fff\uf900-\ufaff]')
HANGUL = re.compile(r'[\uac00-\ud7a3]')
UTF8_RUN = re.compile(rb'(?:[\x09\x0a\x0d\x20-\x7e]|[\xc2-\xdf][\x80-\xbf]|[\xe0-\xef][\x80-\xbf]{2}|[\xf0-\xf4][\x80-\xbf]{3})+')
LANG = {'ChineseTraditional': 'zh-tw', 'ChineseSimplified': 'zh-cn', 'Korean': 'kr'}
MAX_OBJECT_TEXT = 5000   # longer runs are string tables or Lua scripts, read apart


def asset_files():
    names = [os.path.basename(f) for f in glob.glob(os.path.join(DATA, '*')) if os.path.isfile(f)]
    keep = [n for n in names if re.fullmatch(r'level\d+|sharedassets\d+\.assets|resources\.assets|globalgamemanagers\.assets', n)]
    return sorted(keep, key=lambda n: (not n.startswith('level'), [int(x) if x.isdigit() else x for x in re.split(r'(\d+)', n)]))


def parse_csv(text):
    """A LeanLanguageCSV source: 'key = value' lines ('\\n' escapes line breaks), '//' comments."""
    out = {}
    for line in text.replace('\r\n', '\n').split('\n'):
        if not line or line.lstrip().startswith('//') or ' = ' not in line:
            continue
        k, v = line.split(' = ', 1)
        k = k.strip().lstrip(BOM)
        if k and '/' in k:
            out[k] = v
    return out


def text_of(ta):
    raw = ta.m_Script
    if isinstance(raw, str):
        return raw
    b = bytes(raw)
    try:
        return b.decode('utf-8-sig')
    except UnicodeDecodeError:
        return b.decode('utf-8', 'replace')


def us_heap(path):
    """The #US (user string) heap of a .NET assembly: every string literal of its code."""
    data = open(path, 'rb').read()
    root = data.find(b'BSJB')
    if root < 0:
        return []
    vlen = struct.unpack_from('<I', data, root + 12)[0]
    p = root + 16 + vlen
    nstreams = struct.unpack_from('<H', data, p + 2)[0]
    p += 4
    heap = None
    for _ in range(nstreams):
        off, size = struct.unpack_from('<II', data, p)
        p += 8
        e = data.index(b'\x00', p)
        name = data[p:e].decode('ascii')
        p = (e + 4) & ~3
        if name == '#US':
            heap = (root + off, size)
    if not heap:
        return []
    i, end = heap[0] + 1, heap[0] + heap[1]
    out = []
    while i < end:
        b0 = data[i]
        if b0 & 0x80 == 0:
            n, i = b0, i + 1
        elif b0 & 0xC0 == 0x80:
            n, i = ((b0 & 0x3F) << 8) | data[i + 1], i + 2
        else:
            n, i = ((b0 & 0x1F) << 24) | (data[i + 1] << 16) | (data[i + 2] << 8) | data[i + 3], i + 4
        if n:
            out.append(data[i:i + n - 1].decode('utf-16-le', 'replace'))
            i += n
    return out


def main():
    import UnityPy
    from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
    gen = TypeTreeGenerator("2020.3.49f1")
    gen.load_local_game(lom_paths.GAME)
    envs = {}
    for fn in asset_files():
        envs[fn.lower()] = UnityPy.load(os.path.join(DATA, fn))

    # ---- string tables: LeanLanguageCSV components (language + source text asset) ----------------------------------------
    csv_lang = {}   # (file, path_id) of a source text asset -> language
    lean_components = set()
    for fn, env in envs.items():
        want = []
        for obj in env.objects:
            if obj.type.name != 'MonoBehaviour':
                continue
            try:
                cls = obj.read(check_read=False).m_Script.read().m_ClassName
            except Exception:
                continue
            if cls == 'LeanLanguageCSV':
                want.append(obj)
                lean_components.add((fn, obj.path_id))
        if not want:
            continue
        env.typetree_generator = gen
        for obj in want:
            try:
                t = obj.read_typetree()
            except Exception:
                continue
            src = t.get('Source') or {}
            fid, pid = src.get('m_FileID', 0), src.get('m_PathID', 0)
            if not pid:
                continue
            target = fn if fid == 0 else env.file.externals[fid - 1].path.lower().split('/')[-1]
            csv_lang[(target, pid)] = LANG.get(t.get('Language'), t.get('Language'))
        env.typetree_generator = None
    sources = collections.defaultdict(dict)
    csv_assets = set()
    for fn, env in envs.items():
        for obj in env.objects:
            if obj.type.name != 'TextAsset' or (fn, obj.path_id) not in csv_lang:
                continue
            csv_assets.add((fn, obj.path_id))
            sources[csv_lang[(fn, obj.path_id)]].update(parse_csv(text_of(obj.read())))
    print('string tables:', {k: len(v) for k, v in sources.items()}, 'from', len(csv_assets), 'text assets')

    # ---- simplified characters: zh-cn vs zh-tw text of the same key, where both have the same length ----------------------
    pairs, same = collections.defaultdict(collections.Counter), collections.Counter()
    tw, cn = sources.get('zh-tw', {}), sources.get('zh-cn', {})
    for k, t in tw.items():
        s = cn.get(k)
        if s is None or len(s) != len(t):
            continue
        for a, b in zip(t, s):
            if a == b:
                same[b] += 1
            elif CJK.match(a) and CJK.match(b):
                pairs[b][a] += 1
    simplified = {b: c.most_common(1)[0][0] for b, c in pairs.items() if sum(c.values()) >= 2 and sum(c.values()) >= 4 * same[b]}
    print('simplified-only characters:', len(simplified))

    # ---- strings in prefabs, scenes and data assets --------------------------------------------------------------------------
    objects = collections.defaultdict(list)
    for fn, env in envs.items():
        scripts, paths = {}, {}

        def go_path(pptr):
            try:
                go = pptr.read()
            except Exception:
                return ''
            names = [go.m_Name]
            try:
                tr = go.m_Component[0].component.read()   # a GameObject's first component is its (Rect)Transform
                for _ in range(40):
                    if not getattr(tr.m_Father, 'm_PathID', 0):
                        break
                    tr = tr.m_Father.read()
                    names.append(tr.m_GameObject.read().m_Name)
            except Exception:
                pass
            return '/' + '/'.join(reversed(names))

        n = 0
        for obj in env.objects:
            tn = obj.type.name
            if tn not in ('MonoBehaviour', 'GameObject', 'TextAsset') or (fn, obj.path_id) in csv_assets or (fn, obj.path_id) in lean_components:
                continue
            try:
                data = obj.get_raw_data()
            except Exception:
                continue
            found = []
            for m in UTF8_RUN.finditer(data):
                try:
                    s = m.group(0).decode('utf-8')
                except UnicodeDecodeError:
                    continue
                if CJK.search(s) and len(s) <= MAX_OBJECT_TEXT:
                    found.append(s)
            if not found:
                continue
            cls, path = '', ''
            if tn == 'MonoBehaviour':
                try:
                    mb = obj.read(check_read=False)
                    key = (mb.m_Script.m_FileID, mb.m_Script.m_PathID)
                    if key not in scripts:
                        scripts[key] = mb.m_Script.read().m_ClassName
                    cls = scripts[key]
                    if fn.startswith('level'):
                        g = (mb.m_GameObject.m_FileID, mb.m_GameObject.m_PathID)
                        if g not in paths:
                            paths[g] = go_path(mb.m_GameObject)
                        path = paths[g]
                except Exception:
                    cls = cls or '?'
            elif tn == 'TextAsset':
                try:
                    cls = obj.read(check_read=False).m_Name
                except Exception:
                    cls = '?'
            else:
                try:
                    cls = obj.read(check_read=False).m_Name
                except Exception:
                    cls = ''
            for s in found:
                objects[s].append([fn, tn, cls, path])
                n += 1
        print(fn, n, 'strings', flush=True)

    # ---- string literals of the game's code ----------------------------------------------------------------------------------
    code = set()
    for f in glob.glob(os.path.join(DATA, 'Managed', '*.dll')):
        try:
            code.update(s for s in us_heap(f) if CJK.search(s))
        except Exception as ex:
            print('skipped', os.path.basename(f), ex)
    print('code literals:', len(code))

    out = lom_paths.out_path(lom_paths.log_dir('scene_audit'), 'game_text.json')
    st = os.stat(os.path.join(DATA, 'sharedassets0.assets'))
    json.dump({'game': {'sharedassets0_size': st.st_size, 'sharedassets0_mtime': int(st.st_mtime)},
               'sources': sources, 'simplified': simplified, 'objects': objects, 'code': sorted(code)},
              open(out, 'w', encoding='utf-8'), ensure_ascii=False)
    print('wrote', out)


if __name__ == '__main__':
    main()
