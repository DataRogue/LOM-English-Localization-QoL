"""Shared helpers of the update pass (tools/update): the game text before and after the October 2026 update, the workspace,
the strings overrides, Lash's 10.16 draft (comparison only), the glossary and character names, and the Lua scene walker.

Everything the pass writes goes to LOM_Localization/update_pass (lom_paths.LOCALIZATION; the stage's in stage mode) except the
workspace itself, which only apply_update.py changes (lom_paths.assert_writable). No game text is kept in this file.
"""
import collections, csv, glob, io, json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
import lom_paths as P
import publish
import ledger

sys.stdout.reconfigure(encoding="utf-8")
BS = chr(92)
CJK = re.compile(r"[㐀-鿿豈-﫿]")
# A later game update is a pass of its own: LOM_UPDATE_PASS names it (its review-ledger pass, update-<date>; its files go to
# update_pass_<date>), LOM_UPDATE_LUA_OLD the previous game version's script folder under gamedata/. Unset: the first pass.
PASS_NAME = os.environ.get("LOM_UPDATE_PASS") or "update-2026-10"
GAME_VERSION = os.environ.get("LOM_UPDATE_GAME") or "1.0.6000.3"     # the game version the pass is for (notes in strings rows)
PASS_DIR = os.path.join(P.LOCALIZATION, "update_pass" if PASS_NAME == "update-2026-10" else
                        "update_pass_" + re.sub(r"^update-", "", PASS_NAME))
LUA_DIR = os.path.join(P.REAL_LOCALIZATION, "gamedata", "lua")
LUA_OLD_DIR = os.path.join(P.REAL_LOCALIZATION, "gamedata", os.environ.get("LOM_UPDATE_LUA_OLD") or "lua_1.0.5000.13")


def pdir(*parts):
    return os.path.join(PASS_DIR, *parts)


def write_text(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    P.guard_write(path)
    with io.open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def write_json(path, obj, indent=None):
    write_text(path, json.dumps(obj, ensure_ascii=False, indent=indent))


def read_json(path):
    with io.open(path, encoding="utf-8") as f:
        return json.load(f)


def read_dump(path):
    m, probs = publish.parse_game_source(open(path, "rb").read())
    if probs:
        raise SystemExit("%s: %s" % (path, probs[:3]))
    return m


def overrides():
    """The strings overrides {key: (file, text)}: what players see for those keys (first file wins, as the plugin reads them)."""
    ov = {}
    for f in sorted(glob.glob(os.path.join(P.STRINGS_DIR, "*.csv"))):
        with io.open(f, encoding="utf-8-sig", newline="") as fh:
            for row in csv.reader(fh):
                if len(row) >= 2 and row[0] and not row[0].startswith("#") and row[0] != "key":
                    ov.setdefault(row[0], (os.path.basename(f), row[1]))
    return ov


class State:
    """old / new game text, the workspace table, overrides, Lash's draft and the key sets."""

    def __init__(self, lash=True):
        self.old = read_dump(pdir("old_game_source.tsv"))
        self.new = read_dump(pdir("new_game_source.tsv"))
        self.ws = publish.table_map(publish.read_text(P.TABLE))[0]
        self.ov = overrides()
        self.lash = read_json(pdir("lash_en.json")) if lash and os.path.exists(pdir("lash_en.json")) else {}
        self.added = sorted(set(self.new) - set(self.old))
        self.added_set = set(self.added)
        self.removed = sorted(set(self.old) - set(self.new))
        self.changed = sorted(k for k in set(self.old) & set(self.new) if self.old[k] != self.new[k])
        self.led_t, self.led_s = ledger.load()

    def english(self, key):
        """What players see for a key now: the strings override, else the workspace row ('' when none)."""
        if key in self.ov:
            return self.ov[key][1]
        return self.ws.get(key, "")


# ---- symbol-only rows --------------------------------------------------------------------------------------------------------
_SYM = [("……", "..."), ("…", "..."), ("！", "!"), ("？", "?"), ("～", "~"), ("──", "—"), ("，", ","), ("、", ","),
        ("：", ":"), ("；", ";"), ("（", "("), ("）", ")"), ("「", "\""), ("」", "\""), ("『", "'"), ("』", "'")]


def symbol_english(zh):
    """The English of a row whose Chinese holds no Chinese character (only punctuation, blacked-out blocks, Latin letters):
    full-width punctuation as ASCII, a full stop after an ellipsis dropped; blocks, letters and {placeholders} stay."""
    s = zh
    for a, b in _SYM:
        s = s.replace(a, b)
    s = s.replace("。", ".")
    s = re.sub(r"\.{4,}", "...", s)
    return s


MOVE_MIN_CJK = 5      # a removed row's English is inherited only by a line of at least this many Chinese characters


def classify_added(st):
    """{key: (class, extra)} for every added key: override (the strings layer already answers), empty, symbol (extra = the
    English), move (extra = the English of the removed row with the same Chinese, when it is one and the line is not a short
    interjection), fresh (extra = the English of existing rows with the same Chinese, as hints)."""
    rem = collections.defaultdict(set)
    for k in st.removed:
        if k in st.ws:
            rem[st.old[k]].add(st.ws[k])
    cur = collections.defaultdict(list)
    for k, z in st.new.items():
        if k in st.ws and k not in st.added_set:
            e = st.english(k)
            if e and e not in cur[z]:
                cur[z].append(e)
    out = {}
    for k in st.added:
        z = st.new[k]
        if k in st.ov:
            out[k] = ("override", None)
        elif not z.strip():
            out[k] = ("empty", None)
        elif not CJK.search(z):
            out[k] = ("symbol", symbol_english(z))
        elif z in rem and len(rem[z]) == 1 and z not in cur and len(CJK.findall(z)) >= MOVE_MIN_CJK:
            out[k] = ("move", next(iter(rem[z])))
        else:
            hints = list(cur.get(z, []))
            for e in sorted(rem.get(z, ())):
                if e not in hints:
                    hints.append(e)
            out[k] = ("fresh", hints)
    return out


# ---- glossary, names -----------------------------------------------------------------------------------------------------------
def glossary_terms():
    """[(zh, en, ruled)] from glossary/terminology.json (the 2026-09 decisions, rulings applied); zh variants split on '/'."""
    data = read_json(os.path.join(P.REAL_LOCALIZATION, "glossary", "terminology.json"))
    out = []
    for e in data:
        en = (e.get("proposed_en") or "").strip()
        if not en:
            continue
        for z in e["zh"].split("/"):
            z = z.strip()
            if len(z) >= 2 and CJK.search(z):
                out.append((z, en, bool(e.get("owner_ruled"))))
    return out


def character_names(st):
    """{zh name: English} of the Character/* rows (strings override over the table), skipping rows with no English."""
    names = {}
    for k, z in st.new.items():
        if k.startswith("Character/") and len(z) >= 2 and CJK.search(z):
            e = st.english(k).strip()
            if e and not CJK.search(e):
                names.setdefault(z, e)
    return names


def new_term_sheet():
    """[(zh, en, True)] of the update pass's own term sheet (update_pass/terms.tsv: zh TAB en TAB note), decided for this pass."""
    p = pdir("terms.tsv")
    out = []
    if os.path.exists(p):
        for line in io.open(p, encoding="utf-8"):
            parts = line.rstrip("\n").split("\t")
            if len(parts) >= 2 and parts[0] and parts[1] and not parts[0].startswith("#"):
                out.append((parts[0], parts[1], True))
    return out


class TermIndex:
    """Longest-match lookup of known terms in a text: first-character buckets, so 20k lines scan quickly."""

    def __init__(self, pairs):
        self.by_first = collections.defaultdict(list)
        self.en = {}
        self.ruled = {}
        for zh, en, *rest in pairs:
            r = bool(rest and rest[0])
            if zh not in self.en or r:
                self.en[zh] = en
                self.ruled[zh] = r or self.ruled.get(zh, False)
        for zh in self.en:
            self.by_first[zh[0]].append(zh)
        for lst in self.by_first.values():
            lst.sort(key=len, reverse=True)

    def find(self, text):
        """Terms occurring in text, non-overlapping, longest first at each position: [(zh, en)] in order of first occurrence."""
        i, n, seen, out = 0, len(text), set(), []
        while i < n:
            hit = None
            for zh in self.by_first.get(text[i], ()):
                if text.startswith(zh, i):
                    hit = zh
                    break
            if hit:
                if hit not in seen:
                    seen.add(hit)
                    out.append((hit, self.en[hit]))
                i += len(hit)
            else:
                i += 1
        return out


# ---- the Lua scene walker --------------------------------------------------------------------------------------------------------
RX_SAYDLG = re.compile(r"setsaydialog\(saydialogs\.(\w+)\)")
RX_CHAR = re.compile(r'setcharacter\(characters\.Get\("([^"]+)"\)')
RX_SAY = re.compile(r'say\(luamanager\.GetStoryText\("([^"]+)"\)')
RX_OPT = re.compile(r"^\s*(\w+)\[(\d+)\]\s*=\s*(.+)$")
RX_OKEY = re.compile(r'"(?:[a-z]+\+)?(O_[A-Za-z0-9_]+)(?:\|([^"]*))?(?:\+[^"]*)?"')
RX_CHOOSE = re.compile(r"choose\((\w+)\)|ExecuteRoll\((\w+),")
RX_INDEX = re.compile(r"--index:(O_[A-Za-z0-9_]+)")
RX_ANYKEY = re.compile(r'GetStoryText\("([^"]+)"\)')
RX_SHOW = re.compile(r'(?:LoadCharacterAsset|characters\.Get)\("([^"]+)"\)')
RX_NEXT = re.compile(r'SetNextScript\("([^"]+)"')


def lua_scripts(lua_dir=None):
    d = lua_dir or LUA_DIR
    return sorted(os.path.basename(f)[:-4] for f in glob.glob(os.path.join(d, "*.lua")))


def walk_script(name, lua_dir=None):
    """Events of a script in order: {'t': 'line', key, kind, speaker} | {'t': 'menu', options: [keys]} |
    {'t': 'branch', option} | {'t': 'next', script}; also the cast (character ids, first appearance order)."""
    text = io.open(os.path.join(lua_dir or LUA_DIR, name + ".lua"), encoding="utf-8", errors="replace").read()
    kind, speaker = "character", None
    tables, ev, cast = collections.OrderedDict(), [], []
    for line in text.split("\n"):
        for m in RX_SHOW.finditer(line):
            if m.group(1) not in cast:
                cast.append(m.group(1))
        m = RX_SAYDLG.search(line)
        if m:
            kind = m.group(1)
            continue
        m = RX_CHAR.search(line)
        if m:
            speaker = m.group(1)
            continue
        m = RX_SAY.search(line)
        if m:
            ev.append({"t": "line", "key": "Story/" + m.group(1), "kind": kind, "speaker": speaker if kind == "character" else None})
            continue
        m = RX_OPT.match(line)
        if m and "O_" in m.group(3):
            ok = RX_OKEY.search(m.group(3))
            if ok:
                tables.setdefault(m.group(1), []).append("Story/" + ok.group(1))
            continue
        m = RX_CHOOSE.search(line)
        if m:
            opts = tables.pop(m.group(1) or m.group(2), [])
            if opts:
                ev.append({"t": "menu", "options": opts})
            continue
        m = RX_INDEX.search(line)
        if m:
            ev.append({"t": "branch", "option": "Story/" + m.group(1)})
            continue
        m = RX_NEXT.search(line)
        if m:
            ev.append({"t": "next", "script": m.group(1)})
    return ev, cast


def script_keys(name, lua_dir=None):
    text = io.open(os.path.join(lua_dir or LUA_DIR, name + ".lua"), encoding="utf-8", errors="replace").read()
    return ["Story/" + k for k in RX_ANYKEY.findall(text)]


FEMALE_IDS = ("girl", "female", "women", "woman", "aunt", "maid", "mother", "sister", "nun", "lady")
MALE_IDS = ("brother", "boy", "child_boy", "father", "uncle", "monk")


def gender_of(cid):
    base = re.sub(r"[0-9_].*$", "", cid)
    if base in FEMALE_IDS or "girl" in cid or "female" in cid:
        return "she"
    if base in MALE_IDS:
        return "he"
    return ""
