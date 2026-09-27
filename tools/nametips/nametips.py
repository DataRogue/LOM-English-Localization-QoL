"""Dialog name tips (LOM_UI_EN 0.7): maintain workspace/nametips.tsv, which publish.py ships byte for byte as
plugins/LOM_UI_EN/nametips.tsv.

  python nametips.py dump                    the game's story characters (id, Chinese key, portrait addresses) and affinity
                                             records, read from Mortal_Data with UnityPy + TypeTreeGeneratorAPI, to
                                             LOM_Localization/nametips/game_characters.json (the other commands read it)
  python nametips.py crops [--all] [--live]  head position in each listed character's portrait (the crop column), from the
                                             Addressables bundles; without --all only rows that have no crop yet. --live
                                             writes the workspace file (backup in backups/), otherwise the result goes to
                                             LOM_Localization/staged/nametips/nametips.tsv
  python nametips.py candidates              every story character's served English name (workspace table), how often it and
                                             its last word occur in the English story text, and whether nametips.tsv lists it,
                                             to LOM_Localization/nametips/candidates.tsv: the review list after a game update
  python nametips.py check                   nametips.tsv and factiontips.tsv against the game dump and the workspace table:
                                             unknown ids, names that are not English, name forms two entries share, missing
                                             crops, long descriptions, and how often each entry's forms occur in the story
                                             text. Exit code 1 on errors.
  python nametips.py import-factions PROPOSAL.tsv [--live]
                                             workspace/factiontips.tsv from a reviewed proposal (columns id, title, forms,
                                             description); staged unless --live (backup in backups/)
Then run publish.py build.

File format: TAB-separated, '#' starts a comment line. Columns: id (story character id; the plugin matches the served
Character/<id> name and shows CharacterTitle/<id> and the affinity record of that id), aliases (comma-separated extra name
forms, e.g. a given name the text uses on its own), portrait (address to use instead of the game's own, only where the
game's points at a missing image), crop (head centre x, top of the figure y; 0-1 of the portrait). Matching is
case-sensitive and whole-word; a form two listed characters share is not linked (the plugin logs it).
"""
import csv, datetime, glob, io, json, os, re, struct, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
import lom_paths as P

DATA = os.path.join(P.GAME, "Mortal_Data")
AA = os.path.join(DATA, "StreamingAssets", "aa", "StandaloneWindows")
DUMP_NAME = "game_characters.json"
TAB = chr(9)
LF = chr(10)
CR = chr(13)
# Crop window the plugin shows (NameTips.CropHeight/CropMargin), for the preview only.
CROP_H, CROP_MARGIN, FRAME_ASPECT = 640.0, 40.0, 118.0 / 144.0

# Mortal.Core RelationshipStatType: enum value -> its StringValue (= the story character id of that affinity record).
REL_TYPES = {0: "sister1", 1: "brother1", 2: "brother2", 3: "brother3", 4: "brother4", 5: "master", 6: "girl1", 7: "special3",
             8: "girl2", 9: "special4", 10: "special6", 11: "special1", 12: "girl4", 13: "special2", 14: "special7",
             102: "special102", 103: "special103", 205: "special205", 206: "special206", 605: "girl5", 606: "girl6", 607: "girl7",
             608: "girl8", 609: "girl9", 401: "special401", 403: "special403", 404: "special404", 405: "special405",
             409: "special409", 800: "special800", 808: "special808", 809: "special809", 825: "special825", 999: "special999",
             990: "girl2_3_1", 991: "girl2_4"}

HEADER = """# LOM_UI_EN dialog name tips: the characters whose names in story dialogue show a portrait tip on hover.
# TAB-separated columns: id, aliases, portrait, crop. Lines starting with '#' are comments.
#   id        story character id; its served Character/<id> text is the name matched, CharacterTitle/<id> the title shown,
#             and the affinity record with that id the affinity shown. Characters with an affinity record are linked
#             only after the game introduces them (Status > Social lists them), unless [Story] NameTooltipsMetOnly is off
#   aliases   more name forms matched for the same character, comma-separated (a given name used on its own)
#   portrait  portrait address to use instead of the game's own (only where the game's points at a missing image)
#   crop      head centre x and top of the figure y in the portrait, 0-1 (tools/nametips/nametips.py crops)
# Matching is case-sensitive and whole-word. A form that two listed characters share is not linked.
"""


FACTION_HEADER = """# LOM_UI_EN faction tips: sects, clans and other factions whose names in dialogue and choices show a short description.
# TAB-separated columns: id, title, forms, description. Lines starting with '#' are comments.
#   id           short name for this list (not shown)
#   title        the name shown at the top of the tip; always matched
#   forms        more name forms matched for it, comma-separated; all matching is case-sensitive and whole-word, and the
#                longest form in a text wins
#   description  the tip's text: who they are, as the story introduces them. Never events, fates or secrets.
"""


def out_dir():
    return P.log_dir("nametips")


def read_factions(path=None):
    """[(line text, row dict or None)] of factiontips.tsv; a row: id, title, forms (list), description."""
    path = path or P.WS_FACTIONTIPS
    out = []
    if not os.path.exists(path):
        return out
    with io.open(path, encoding="utf-8-sig") as f:
        for line in f.read().split(LF):
            line = line.rstrip(CR)
            if not line.strip() or line.lstrip().startswith("#"):
                out.append((line, None))
                continue
            cols = line.split(TAB) + ["", "", ""]
            out.append((line, {"id": cols[0].strip(), "title": cols[1].strip(),
                               "forms": [x.strip() for x in cols[2].split(",") if x.strip()], "description": cols[3].strip()}))
    while out and out[-1][0] == "" and out[-1][1] is None:
        out.pop()
    return out


def cmd_import_factions(src, live):
    """Write workspace/factiontips.tsv from a reviewed proposal TSV (header with id, title, forms, description)."""
    # Ids are numbers and the title is not repeated among the forms (the plugin always matches the title): publish.py's scan
    # for OverLlm text reads each line as prose, and "kongtong_sect Kongtong Sect Kongtong Sect" is a run OverLlm has.
    rows = []
    with io.open(src, encoding="utf-8-sig", newline="") as f:
        for r in csv.DictReader(f, delimiter=TAB):
            if not (r.get("id") or "").strip():
                continue
            title = r["title"].strip()
            desc = " ".join((r["description"] or "").split())
            forms = ",".join(x.strip() for x in (r["forms"] or "").split(",") if x.strip() and x.strip() != title)
            rows.append(TAB.join(["%02d" % (len(rows) + 1), title, forms, desc]))
    data = (FACTION_HEADER + LF.join(rows) + LF).encode("utf-8")
    if not live:
        p = P.out_path(P.staged_dir("nametips"), "factiontips.tsv")
        with open(p, "wb") as f:
            f.write(data)
        print("staged: %s (%d factions; add --live to write the workspace file)" % (p, len(rows)))
        return
    P.assert_nametips_writable(P.WS_FACTIONTIPS)
    if os.path.exists(P.WS_FACTIONTIPS):
        b = P.backup_path(P.WS_FACTIONTIPS, datetime.datetime.now().strftime("%Y%m%d-%H%M%S"))
        with open(P.WS_FACTIONTIPS, "rb") as f, open(b, "wb") as g:
            g.write(f.read())
        print("backup:", b)
    tmp = P.WS_FACTIONTIPS + ".tmp"
    with open(tmp, "wb") as f:
        f.write(data)
    os.replace(tmp, P.WS_FACTIONTIPS)
    print("wrote %s (%d factions) - now run: python %s build" % (P.WS_FACTIONTIPS, len(rows), os.path.join(P.TOOLS, "publish.py")))


# ---- the data file --------------------------------------------------------------------------------------------------------

def read_tips(path=None):
    """[(line text, row dict or None)] keeping comments and order; a row: id, aliases (list), portrait, crop (str)."""
    path = path or P.WS_NAMETIPS
    out = []
    if not os.path.exists(path):
        return out
    with io.open(path, encoding="utf-8-sig") as f:
        for line in f.read().split(LF):
            line = line.rstrip(CR)
            if not line.strip() or line.lstrip().startswith("#"):
                out.append((line, None))
                continue
            cols = line.split(TAB) + ["", "", ""]
            out.append((line, {"id": cols[0].strip(), "aliases": [a.strip() for a in cols[1].split(",") if a.strip()],
                               "portrait": cols[2].strip(), "crop": cols[3].strip()}))
    while out and out[-1][0] == "" and out[-1][1] is None:
        out.pop()
    return out


def row_line(r):
    cols = [r["id"], ",".join(r["aliases"]), r["portrait"], r["crop"]]
    while cols and cols[-1] == "":
        cols.pop()
    return TAB.join(cols)


def tips_bytes(items):
    return (LF.join(line if r is None else row_line(r) for line, r in items) + LF).encode("utf-8")


def write_tips(items, live):
    data = tips_bytes(items)
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    if not live:
        p = P.out_path(P.staged_dir("nametips"), "nametips.tsv")
        with open(p, "wb") as f:
            f.write(data)
        print("staged:", p, "(add --live to write the workspace file)")
        return p
    P.assert_nametips_writable(P.WS_NAMETIPS)
    if os.path.exists(P.WS_NAMETIPS):
        b = P.backup_path(P.WS_NAMETIPS, stamp)
        with open(P.WS_NAMETIPS, "rb") as f, open(b, "wb") as g:
            g.write(f.read())
        print("backup:", b)
    tmp = P.WS_NAMETIPS + ".tmp"
    with open(tmp, "wb") as f:
        f.write(data)
    os.replace(tmp, P.WS_NAMETIPS)
    print("wrote", P.WS_NAMETIPS, "- now run: python", os.path.join(P.TOOLS, "publish.py"), "build")
    return P.WS_NAMETIPS


# ---- game data ------------------------------------------------------------------------------------------------------------

def cmd_dump():
    import UnityPy
    from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
    gen = TypeTreeGenerator("2020.3.49f1")
    gen.load_local_game(P.GAME)
    files = ["globalgamemanagers.assets"] + sorted(f for f in os.listdir(DATA) if re.fullmatch(r"sharedassets\d+\.assets", f))
    envs, index, scripts = {}, {}, {}
    for fn in files:
        e = UnityPy.load(os.path.join(DATA, fn))
        e.typetree_generator = gen
        envs[fn.lower()] = e
        index[fn.lower()] = {o.path_id: o for o in e.objects}
    for f, e in envs.items():
        for o in e.objects:
            if o.type.name == "MonoScript":
                scripts[(f, o.path_id)] = o.read().m_ClassName

    def ext(env, fid):
        return None if fid == 0 else env.file.externals[fid - 1].path.lower().split("/")[-1]

    def cls(fn, o):
        b = o.get_raw_data()
        fid, pid = struct.unpack_from("<i", b, 16)[0], struct.unpack_from("<q", b, 20)[0]
        return scripts.get((ext(envs[fn], fid) or fn, pid), "?")

    want = {"StoryCharacterData", "StoryMappingItem", "RelationshipStat"}
    found = {k: [] for k in want}
    for fn, e in envs.items():
        for o in e.objects:
            if o.type.name != "MonoBehaviour":
                continue
            try:
                c = cls(fn, o)
            except Exception:
                continue
            if c in want:
                found[c].append((fn, o.path_id, o.read_typetree()))
    maps = {(fn, pid): (t.get("Key"), t.get("Value")) for fn, pid, t in found["StoryMappingItem"]}

    def mapped(fn, pptr):
        if not pptr or not pptr.get("m_PathID"):
            return None
        target = ext(envs[fn], pptr.get("m_FileID", 0)) or fn
        return maps.get((target, pptr["m_PathID"]))

    chars = {}
    for fn, pid, t in found["StoryCharacterData"]:
        m = mapped(fn, t.get("_mapping"))
        if not m or not m[1]:
            continue
        ports = []
        for p in t.get("_portraitResourceList") or []:
            pm = mapped(fn, p.get("_mapping"))
            ports.append([pm[1] if pm else "", p.get("_addressKey", "")])
        chars[m[1]] = {"zh_key": m[0], "asset": t.get("m_Name"), "portraits": ports}
    rels = {}
    unknown = []
    for fn, pid, t in found["RelationshipStat"]:
        typ = t.get("_type")
        rid = REL_TYPES.get(typ)
        if rid is None:
            unknown.append((t.get("m_Name"), typ))
            continue
        rels[rid] = t.get("m_Name")
    for cid, c in chars.items():
        c["affinity"] = cid in rels
    p = P.out_path(out_dir(), DUMP_NAME)
    with io.open(p, "w", encoding="utf-8", newline=LF) as f:
        json.dump({"characters": chars, "affinity_records": rels}, f, ensure_ascii=False, indent=1, sort_keys=True)
    print("%d story characters, %d affinity records -> %s" % (len(chars), len(rels), p))
    if unknown:
        print("affinity records of an unknown type (add them to REL_TYPES from Mortal.Core RelationshipStatType):", unknown)


def load_dump():
    p = os.path.join(P.LOCALIZATION, "nametips", DUMP_NAME)
    if not os.path.exists(p):
        sys.exit("no game dump yet: run  python nametips.py dump")
    with io.open(p, encoding="utf-8") as f:
        return json.load(f)


def portrait_key(dump, r):
    if r["portrait"]:
        return r["portrait"]
    c = dump["characters"].get(r["id"])
    if not c or not c["portraits"]:
        return None
    return next((a for m, a in c["portraits"] if m == "normal"), c["portraits"][0][1])


def catalog_addresses():
    """{address: internal id} from the Addressables catalog (aa/catalog.json, format 1.x). An address need not be its asset's
    path in the bundle: a folder renamed after the asset was made addressable keeps the old address (Special_833_南宮秀 is
    the address of the asset at Special_833_段智秀), so bundles are searched by the internal id."""
    import base64
    with io.open(os.path.join(os.path.dirname(AA), "catalog.json"), encoding="utf-8") as f:
        c = json.load(f)
    keys = base64.b64decode(c["m_KeyDataString"])
    buckets = base64.b64decode(c["m_BucketDataString"])
    entries = base64.b64decode(c["m_EntryDataString"])
    ids = c["m_InternalIds"]

    def key_at(off):
        t = keys[off]
        if t in (0, 1):
            n = struct.unpack_from("<i", keys, off + 1)[0]
            return keys[off + 5:off + 5 + n].decode("ascii" if t == 0 else "utf-16-le", "replace")
        return None

    out = {}
    n_entries = struct.unpack_from("<i", entries, 0)[0]
    pos = 4
    for _ in range(struct.unpack_from("<i", buckets, 0)[0]):
        off, count = struct.unpack_from("<ii", buckets, pos)
        idx = struct.unpack_from("<%di" % count, buckets, pos + 8) if count else ()
        pos += 8 + 4 * count
        k = key_at(off)
        if not k or "/" not in k:
            continue
        for e in idx:
            if 0 <= e < n_entries:
                internal = ids[struct.unpack_from("<i", entries, 4 + e * 28)[0]]
                if not internal.lower().endswith(".bundle"):
                    out[k] = internal
                    break
    return out


def head_crop(img):
    """(head centre x, top y) of the figure, 0-1: the top of the opaque area, and the centre of the opaque columns of the
    260 px band below it (the head, not the arms or a weapon further down)."""
    a = img.getchannel("A").point(lambda v: 255 if v > 40 else 0)
    bbox = a.getbbox()
    if not bbox:
        return None
    w, h = img.size
    x0, y0, x1, y1 = bbox
    band = a.crop((0, y0, w, min(h, y0 + 260)))
    px = band.load()
    bw, bh = band.size
    total = weighted = 0
    for x in range(0, bw, 2):
        s = sum(1 for y in range(0, bh, 4) if px[x, y])
        total += s
        weighted += x * s
    cx = weighted / total if total else (x0 + x1) / 2.0
    return cx / w, y0 / float(h)


def cmd_crops(all_rows, live):
    import UnityPy
    dump = load_dump()
    items = read_tips()
    if not items:
        sys.exit("%s is missing or empty" % P.WS_NAMETIPS)
    addresses = catalog_addresses()
    todo = {}
    for line, r in items:
        if r is None or (r["crop"] and not all_rows):
            continue
        k = portrait_key(dump, r)
        if not k:
            print("no portrait address for", r["id"])
        elif k not in addresses:
            print("not an address in the catalog (the game cannot load it either):", r["id"], k)
        else:
            todo.setdefault(addresses[k].lower(), []).append(r)
    print(len(todo), "portraits to measure", flush=True)
    bundles = sorted(glob.glob(os.path.join(AA, "*.bundle")), key=lambda b: ("portrait" not in b.lower() and "portarit" not in b.lower(), b))
    done = 0
    for bf in bundles:
        if not todo:
            break
        env = UnityPy.load(bf)
        for path, obj in list(env.container.items()):
            rows = todo.pop(path.lower(), None)
            if not rows:
                continue
            crop = head_crop(obj.read().image)
            for r in rows:
                if crop:
                    r["crop"] = "%.4f,%.4f" % crop
                    done += 1
                else:
                    print("empty image for", r["id"], path)
    for k, rows in todo.items():
        print("not in any bundle:", k, [r["id"] for r in rows])
    print(done, "crops measured")
    write_tips(items, live)


# ---- review ---------------------------------------------------------------------------------------------------------------

def read_table():
    csv.field_size_limit(10 ** 9)
    t = {}
    with io.open(P.TABLE, encoding="utf-8-sig", newline="") as f:
        for row in csv.reader(f):
            if len(row) >= 2:
                t[row[0]] = row[1]
    return t


def story_corpus(table):
    return [(k, v.replace("\\n", " ")) for k, v in table.items() if k.startswith("Story/")]


def word_re(s):
    return re.compile(r"(?<![\w])" + re.escape(s) + r"(?![\w])")


def english(s):
    return bool(s) and not re.search("[" + chr(0x3000) + "-" + chr(0x9FFF) + chr(0xFF00) + "-" + chr(0xFFEF) + "?{}()]", s) and bool(re.search("[A-Za-z]", s))


def cmd_candidates():
    dump = load_dump()
    table = read_table()
    story = story_corpus(table)
    text = LF.join(v for _, v in story)
    listed = {r["id"] for _, r in read_tips() if r}
    by_name = {}
    for cid, c in sorted(dump["characters"].items()):
        name = (table.get("Character/" + cid) or "").strip()
        by_name.setdefault(name, []).append(cid)
    rows = []
    for name, ids in by_name.items():
        full = len(word_re(name).findall(text)) if english(name) else 0
        parts = name.split()
        given, gcount = "", ""
        if len(parts) == 2 and len(parts[1]) >= 4:
            given = parts[1]
            gcount = str(len(re.findall(r"(?<![\w])(?<!" + re.escape(parts[0]) + r" )" + re.escape(given) + r"(?![\w])", text)))
        rows.append((full, name, ",".join(ids), ",".join(i for i in ids if dump["characters"][i]["affinity"]),
                     ",".join(i for i in ids if i in listed), given, gcount))
    rows.sort(key=lambda r: -r[0])
    p = P.out_path(out_dir(), "candidates.tsv")
    with io.open(p, "w", encoding="utf-8", newline=LF) as f:
        f.write("count\tname\tids\taffinity_ids\tlisted_ids\tlast_word\tlast_word_alone\n")
        for r in rows:
            f.write(TAB.join(str(x) for x in r) + LF)
    new = [r for r in rows if not r[4] and r[0] >= 5 and english(r[1])]
    print("%d names -> %s; %d not listed that occur 5+ times (listed ones are ignored):" % (len(rows), p, len(new)))
    for r in new[:40]:
        print("  %5d  %-30s %s" % (r[0], r[1], r[2][:60]))


def cmd_check():
    dump = load_dump()
    table = read_table()
    story = story_corpus(table)
    text = LF.join(v for _, v in story)
    items = [r for _, r in read_tips() if r]
    if not items:
        sys.exit("%s is missing or empty" % P.WS_NAMETIPS)
    errors, warnings = [], []
    forms = {}
    seen = set()
    for r in items:
        cid = r["id"]
        if cid in seen:
            errors.append("%s is listed twice" % cid)
        seen.add(cid)
        c = dump["characters"].get(cid)
        if c is None:
            errors.append("%s is not a story character of this game build" % cid)
        name = (table.get("Character/" + cid) or "").strip()
        if not english(name):
            warnings.append("%s: its name (Character/%s = %r) is not an English name; only its aliases are matched" % (cid, cid, name))
        else:
            forms.setdefault(name, []).append(cid)
        for a in r["aliases"]:
            if not english(a):
                errors.append("%s: alias %r is not an English name form" % (cid, a))
            forms.setdefault(a, []).append(cid)
            if len(re.findall("[aeiouAEIOU]+", a)) < 2:
                warnings.append("%s: alias %r is one syllable (often another character's name too)" % (cid, a))
        if not r["crop"]:
            warnings.append("%s: no crop (run: python nametips.py crops --live)" % cid)
        elif not re.fullmatch(r"0?\.\d+,0?\.\d+|[01](\.0+)?,[01](\.0+)?", r["crop"]):
            errors.append("%s: crop %r is not 'x,y' in 0-1" % (cid, r["crop"]))
        if c is not None and not r["portrait"] and not c["portraits"]:
            warnings.append("%s: the game has no portrait for this character (tip without picture)" % cid)
    cjk = re.compile("[" + chr(0x3000) + "-" + chr(0x9FFF) + chr(0xFF00) + "-" + chr(0xFFEF) + "]")
    fitems = [r for _, r in read_factions() if r]
    fseen = set()
    for r in fitems:
        fid = "faction:" + r["id"]
        if fid in fseen:
            errors.append("%s is listed twice" % fid)
        fseen.add(fid)
        if not (r["title"] and r["description"]):
            errors.append("%s: a title and a description are both needed" % fid)
        if cjk.search(r["description"] + r["title"]):
            errors.append("%s: Chinese in the title or description" % fid)
        if len(r["description"]) > 170:
            warnings.append("%s: the description is %d characters (keep it to two short sentences)" % (fid, len(r["description"])))
        # the plugin matches the title as well as the listed forms
        fforms = [r["title"]] + [x for x in r["forms"] if x != r["title"]]
        for f in fforms:
            if not english(f):
                errors.append("%s: form %r is not an English name form" % (fid, f))
            forms.setdefault(f, []).append(fid)
        n = sum(len(word_re(x).findall(text)) for x in fforms if english(x))
        if n == 0:
            warnings.append("%s: none of its forms occurs in the story text" % fid)
    for f, ids in sorted(forms.items()):
        if len(set(ids)) > 1:
            errors.append("%r is a name form of %s: the plugin links neither" % (f, ", ".join(sorted(set(ids)))))
    counts = []
    for r in items:
        fs = [x for x in [(table.get("Character/" + r["id"]) or "").strip()] + r["aliases"] if english(x)]
        n = sum(len(word_re(x).findall(text)) for x in fs)
        counts.append((n, r["id"]))
        if n == 0:
            warnings.append("%s: none of its name forms occurs in the story text" % r["id"])
    for e in errors:
        print("ERROR", e)
    for w in warnings:
        print("warning", w)
    counts.sort(reverse=True)
    print("%d characters, %d factions, %d name forms, %d errors, %d warnings; most mentioned: %s" % (
        len(items), len(fitems), len(forms), len(errors), len(warnings), ", ".join("%s %d" % (i, n) for n, i in counts[:8])))
    sys.exit(1 if errors else 0)


def main(argv):
    if not argv or argv[0] in ("-h", "--help"):
        print(__doc__)
        return
    cmd = argv[0]
    if cmd == "dump":
        cmd_dump()
    elif cmd == "crops":
        cmd_crops("--all" in argv, "--live" in argv)
    elif cmd == "candidates":
        cmd_candidates()
    elif cmd == "check":
        cmd_check()
    elif cmd == "import-factions" and len(argv) > 1:
        cmd_import_factions(argv[1], "--live" in argv)
    else:
        sys.exit("unknown command %r (dump, crops, candidates, check, import-factions PROPOSAL.tsv [--live])" % cmd)


if __name__ == "__main__":
    main(sys.argv[1:])
