"""Build the shipped LOM_UI_EN text (only this mod's own work) from the maintainer's workspace, check it, and rebase it.

  python publish.py [--stage ROOT] build  [--target DIR] [--force] [--no-check] [--rerecord] [--keep-record KEYS|@FILE|all]
  python publish.py [--stage ROOT] check  [--target DIR]
  python publish.py [--stage ROOT] rebase NEW_BASE_DIR [--dry-run] [--allow-converged] [--keep-record KEYS|@FILE|all]
  python publish.py [--stage ROOT] source SRCDUMP.tsv [--restamp KEYS|@FILE|all] [--game-build ID] [--rerecord]
                                         [--keep-record KEYS|@FILE|all]

The model (see lom_paths.py): WORKSPACE holds the full working tables, BASE_REF the OverLlm English patch release they diverged
from. The plugin folder ships only this mod's own work ("overlay-1"), and the plugin lays it over the OverLlm patch's own
installed files at run time (without them, the shipped lines alone are served). This mod's own work is every line it revised
and every line a line-by-line review pass reviewed: the review ledger (ledger.py; workspace/translation/reviewed.tsv for table
keys, workspace/translation/scene/reviewed_scene.tsv for scene lines by their raw key and 'resize/<file>' for resizers) lists
the reviewed ones, which ship even when their text is OverLlm's own line (owner ruling 2026-09-24):
  translation/StringTable.csv       rows whose value (TextTable semantics) differs from BASE_REF's, or keys BASE_REF lacks, and
                                    the rows the ledger lists, as key,"value" ('"' doubled), CRLF, UTF-8 without BOM
  translation/StringTable.meta.tsv  the revision record: '# LOM_UI_EN revision record v1 game=<build id|unknown>', then one
                                    line per shipped row: key TAB TextHash(the game's Chinese the row was revised against) TAB
                                    TextHash(the OverLlm line it was revised against); '-' = not recorded (TextHash = first 16
                                    hex digits of SHA-1 over the UTF-8 text, as OriginalMod.TextHash). A row keeps the hashes
                                    recorded when it was revised (workspace/translation/revision_record.tsv): build records only
                                    new or changed rows (from base_ref/game_source.tsv and BASE_REF). The plugin's fallback mode
                                    ([Translation] FallBackToOriginal) shows OverLlm's line for a row whose game text changed
                                    since it was revised when OverLlm's line changed too (while the record is trusted: under
                                    70%% of an even sample of its rows changed)
  translation/scene/scene_text.txt  the workspace's own line (its last physical line for the key, byte for byte, in workspace
                                    order) for every key whose value differs from BASE_REF's, that BASE_REF lacks or whose line
                                    the scene ledger lists, then one
                                    "<key as escaped in BASE_REF>=" line per BASE_REF key the workspace removed (an empty value
                                    removes that OverLlm line), then an explicit line for each trimmed-variant key the overlay
                                    would otherwise take from a different line than the workspace does; BOM and CRLF like the
                                    workspace file
  translation/scene/resize/*.resizer.txt   this mod's own resizers (workspace/translation/scene/resize), byte for byte; one
                                    the scene ledger lists ('resize/<file>') may be a reviewed copy of OverLlm's
  translation/MANIFEST.json         format overlay-1: counts (reviewed_table, reviewed_scene, reviewed_same_as_overllm among
                                    them), with a ledger 'standalone' {table_rows, table_total, scene_lines, scene_total}
                                    (shipped against the full working copy: the plugin plays a layer without OverLlm, and
                                    says so, at 98%%), BASE_REF md5s, sentinels as {key, original_sha1, revised_sha1}
                                    (no English of the base patch, no time stamp: a rebuild of the same inputs is identical)
  strings/*.csv                     the workspace overrides with each note stripped of quoted OverLlm / game-screen wording
  rules/*.json                      workspace/rules/*.json without comments ('//' lines, "comment" fields) and without fields
                                    the plugin's rule loader does not read (read from the plugin source); every other field kept
  THIRD_PARTY_NOTICES.txt           from LOM_Localization/src/LOM_UI_EN when that file exists
  nametips.tsv                      workspace/nametips.tsv byte for byte, when it exists (the dialog name tips: character ids,
                                    extra name forms and portrait crops; the names themselves are the served table's)
  factiontips.tsv                   workspace/factiontips.tsv byte for byte, when it exists (faction names matched in dialogue
                                    and choices, with this mod's own short spoiler-free description of each)
and the 0.4 layout's translation/scene/resize copies of OverLlm's resizers and profiles/ are removed.

build   writes those files into DIR (default: the plugin folder). A shipped file that differs from what the last publish wrote
        (translation/.publish_state.json) was edited by hand: build refuses unless --force, which first copies every file it
        overwrites or removes to backups/publish-force-<stamp>/. It checks the result in memory before writing, records the
        pending hashes before it writes anything (an interrupted publish is finished by the next build), re-reads the files
        afterwards and does nothing at all when nothing changed. When workspace/translation/revision_record.tsv is missing it
        recovers it from DIR's shipped StringTable.meta.tsv and StringTable.csv (as publish.py wrote them) for the rows
        whose shipped value is the workspace's, says how many, and refuses (exit 2) when any shipped row cannot be
        recovered, unless --rerecord, which records those rows afresh from today's game text and OverLlm line. A row whose
        text changed is re-recorded against today's game text and OverLlm line (the edit counts as a revision for them);
        when its old record's game-text hash is not base_ref/game_source.tsv's (the row was out of date: a term or name pass
        after a game update) build says so and lists those rows, with the record they had, in
        backups/rerecord-<stamp>/rerecorded_out_of_date.tsv. --keep-record KEYS|@FILE|all keeps the old record of the
        changed rows among them (the fallback keeps seeing the game update); @ that listing brings back the records an
        earlier build replaced.
check   verifies DIR: BASE_REF overlaid with DIR's files is exactly the workspace (table: the TextTable map; scene: the XUnity
        key -> value map, tombstones applied, trimmed variants and known values included, as SceneDictionary.LoadOverlay
        builds it); no shipped row or line equals BASE_REF's or the installed OverLlm files' for the same key unless the
        review ledger lists it (every key the ledger lists must be in the workspace, and shipped: the shipped files alone
        hold its workspace text; a listed resizer ships as the workspace's, byte for byte); the files are
        exactly what build would write now; no unknown files under translation/; and no text file in DIR (harness/ and the
        files the game writes while it runs aside: translation/untranslated.txt and compat_report.txt, which release.py
        never packages) holds a whole OverLlm value of 12+ characters or a run of 4+ words that only OverLlm's
        text has (UTF-8, UTF-16 and stray bytes read; each file's scan stops at its first 20 hits); no binary file other than
        a well-formed PE file LOM_UI_EN.dll (and LOM_UI_EN.BepInEx5.dll) at the folder's root (nothing after its last section), the pinned
        Newtonsoft.Json.dll there (PINNED_LIBS, byte for byte; a missing one fails too) and well-formed
        sprites/*.png (signature, every chunk's CRC, a sprite's chunk types only, the image data the size its header says,
        IEND last with nothing after it; their text chunks are scanned), no other file over 16 MB, no link or junction, no
        folder or file that cannot be opened (the folder is walked by long paths, so deep paths and names ending in a dot
        or a space are read too). A missing workspace revision record fails too. Exit code 1 on any failure.
rebase  for a newer OverLlm release (NEW_BASE_DIR holds StringTable.csv and _AutoGeneratedTranslations.txt, or the release's
        Mods/English and BepInEx/Translation/en/Text folders; since its llmkit-upgrade a release has one table per game file
        instead of StringTable.csv, read as the plugin reads them (lashtables.py) and kept in BASE_REF as one StringTable.csv
        with the same rows, and no XUnity file, so BASE_REF keeps its own): workspace := the new release with this mod's revisions (the
        current shipped delta) applied, BASE_REF := the new release. Rows OverLlm changed that this mod also revised keep this
        mod's text and are listed. Refuses a "release" that holds this mod's own edits (a sentinel with this mod's pre-0.4
        text, or more than %d revisions now equal to it without --allow-converged). Backs up the workspace and BASE_REF,
        stages both new versions, then swaps them in; workspace/base_ref.lock ties the two together, so a torn rebase or a
        half restore is refused instead of shipping OverLlm's text as ours. The rows it keeps keep their revision record;
        the rows whose new OverLlm line is not the one they were revised against are listed: the plugin's fallback serves
        OverLlm's line for these once the game text changed, while the revision record is trusted (under 70%% of sampled
        rows changed). The replaced release is kept under base_ref/history/ (its wording stays known as OverLlm's). Run
        build afterwards.
source  takes a harness 'srcdump' file (the game's own Chinese per key: key TAB value, with backslash, TAB, CR and LF
        escaped as \\\\ \\t \\r \\n): after checking it, it becomes base_ref/game_source.tsv (the previous one, the
        workspace record and base_ref.json are backed up to backups/source-<stamp>/), then build runs. Without --restamp it
        never changes an existing record: records with no game-text hash yet ('-') are filled from the dump; a row whose recorded game text
        differs from the dump's (a game update since it was revised) keeps its hash, which is what the plugin's fallback
        compares. Those rows are printed and written to backups/source-<stamp>/game_text_changed.tsv in two groups:
        OverLlm has a newer line (the plugin serves OverLlm's line for them while the record is trusted) and no newer
        OverLlm line (this mod's text kept: needs revising). A row re-revised afterwards is recorded by build against
        the new dump and today's OverLlm line. --restamp KEYS|@FILE|all re-records the listed rows (or all) without a
        value change, from the dump and today's OverLlm line (a maintainer confirming the revision still fits): those
        rows stop falling back. Rows a build recorded against the dump being replaced since that dump was installed
        (revised before this source ran) whose game text the new dump changes are listed on their own (group
        revised_before_source, and revised_before_source.tsv next to the listing) with the exact --restamp command that
        confirms them for the new game text: run source first after a game update, then revise. --game-build names the
        build the dump came from (default: the installed game's Compat fingerprint); --rerecord and --keep-record as for
        build.
--stage ROOT runs against a staging layout (ROOT/plugin, ROOT/workspace, ROOT/base_ref, ROOT/backups); so does LOM_STAGE=ROOT.
"""
import argparse, collections, datetime, hashlib, json, os, re, shutil, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import lom_paths as P
import ledger as LEDGER
import lashtables as LT
from migrate_standalone import idea_parse, repair_table

FORMAT = "overlay-1"
STATE_FORMAT = "publish-state-2"
LOCK_FORMAT = "base-ref-lock-1"
# The 0.4 translation/StringTable.csv (and the OverLlm release file) have no BOM; the shipped table keeps that. The plugin
# (File.ReadAllText) and every tool here read it either way.
TABLE_BOM = False
UTF8_BOM = b"\xef\xbb\xbf"
BS = chr(92)
TAB = chr(9)
CR = chr(13)
LF = chr(10)
NUL = chr(0)
EM = chr(0x2014)
EM_SEP = " " + EM + " "
DIRECTIVES = ("#set ", "#unset ", "#enable ")
REL_TABLE = "translation/StringTable.csv"
REL_META = "translation/StringTable.meta.tsv"
REL_SCENE = "translation/scene/scene_text.txt"
REL_RESIZE = "translation/scene/resize"
REL_MANIFEST = "translation/MANIFEST.json"
REL_STATE = "translation/.publish_state.json"
REL_UNTRANSLATED = "translation/untranslated.txt"
REL_NOTICES = "THIRD_PARTY_NOTICES.txt"
REL_NAMETIPS = "nametips.tsv"
REL_FACTIONTIPS = "factiontips.tsv"
REL_COMPAT_REPORT = "compat_report.txt"
# files the game writes into the plugin folder while it runs (the missing-text log and the compatibility report): release.py
# never packages them, so the OverLlm-text scan skips them (the missing-text log quotes whatever the screen showed, OverLlm's
# lines included)
RUNTIME_FILES = (REL_UNTRANSLATED, REL_COMPAT_REPORT)
MANAGED_DIRS = ("translation", "strings", "rules", "profiles")
LOCK_FILES = ("StringTable.csv", "_AutoGeneratedTranslations.txt")
CONVERGED_LIMIT = 20
MIN_PLUGIN_VERSION = (0, 5, 0)
HYGIENE_CMD = 'python "%s"' % os.path.join(P.TOOLS, "hygiene", "scene_hygiene.py")
# a literal "[...]" / "[...unchanged...]" left in an override by a review pass: players would see it
PLACEHOLDER = re.compile(r"\[(?:\.\.\.|" + chr(0x2026) + r")[^\]\n]*\]")
_HAS_LETTER = re.compile("[a-z]")
_CJK = re.compile("[%s-%s%s-%s]" % (chr(0x3400), chr(0x4DBF), chr(0x4E00), chr(0x9FFF)))

__doc__ = __doc__ % CONVERGED_LIMIT


class PublishError(Exception):
    pass


class WrittenCheckFailed(PublishError):
    """The files were written, but the check of what was written fails."""


def say(*a):
    print(*a, flush=True)


# ---- hashes and files --------------------------------------------------------------------------------------------------

def md5_bytes(b):
    return hashlib.md5(b).hexdigest()


def sha256_bytes(b):
    return hashlib.sha256(b).hexdigest()


def file_bytes(path):
    with open(path, "rb") as f:
        return f.read()


def md5_file(path):
    return md5_bytes(file_bytes(path))


def text_hash(s):
    """OriginalMod.TextHash: first 16 hex digits of SHA-1 over the UTF-8 text."""
    return hashlib.sha1(s.encode("utf-8")).hexdigest()[:16]


def _text(b):
    """File.ReadAllText: UTF-8 with a BOM skipped (strict decoding, so a damaged file stops the run instead of changing text)."""
    if b.startswith(UTF8_BOM):
        b = b[3:]
    return b.decode("utf-8")


def read_text(path):
    return _text(file_bytes(path))


def rel_path(root, rel):
    return os.path.join(root, *rel.split("/"))


def game_running():
    """True while Mortal.exe runs, and also when that cannot be told (tasklist missing or failing): callers refuse then."""
    try:
        r = subprocess.run(["tasklist", "/FI", "IMAGENAME eq Mortal.exe", "/NH"], capture_output=True, text=True, timeout=60)
    except Exception:
        return True
    if r.returncode != 0:
        return True
    return "mortal.exe" in (r.stdout or "").lower()


GAME_RUNNING_MSG = "Mortal.exe is running (or tasklist could not tell); close the game first."


def plugin_version(dll):
    """The version in LOM_UI_EN.dll's [BepInPlugin("lom.ui.english", "LOM_UI_EN", VERSION)] attribute, or None."""
    try:
        d = file_bytes(dll)
    except OSError:
        return None
    m = re.search(b"\x01\x00\x0elom\\.ui\\.english\x09LOM_UI_EN([\x01-\x7f])", d)
    if not m:
        return None
    n = m.group(1)[0]
    return d[m.end():m.end() + n].decode("ascii", "replace")


def version_tuple(s):
    return tuple(int(x) for x in re.findall(r"\d+", s or "")[:3])


# ---- the string table (TextTable semantics) ------------------------------------------------------------------------------

def _skipped_key(k):
    return "/" not in k and (k == "TextFont" or k.startswith("Image_") or k.startswith("TextMeshFont_"))


def table_map(text):
    """TextTable.Load: Ideafixxxer rows; first row wins; a row with more than two fields is re-joined with ','; the LeanPhrase
    dumps that are not string keys (TextFont, Image_*, TextMeshFont_*) are skipped. Returns (ordered key -> value, stats)."""
    m, st = {}, collections.Counter()
    for r in idea_parse(text):
        st["rows"] += 1
        if len(r) < 2 or not r[0]:
            st["short"] += 1
            continue
        k = r[0]
        if _skipped_key(k):
            st["skipped"] += 1
            continue
        if k in m:
            st["duplicates"] += 1
            continue
        if len(r) > 2:
            st["rejoined"] += 1
        m[k] = r[1] if len(r) == 2 else ",".join(r[1:])
    return m, st


def table_serialize(items):
    out = []
    for k, v in items:
        if not k or any(c in k for c in ',"\r\n') or _skipped_key(k):
            raise PublishError("table key %r cannot be written as a key,\"value\" row" % k)
        out.append(k + ',"' + v.replace('"', '""') + '"\r\n')
    return "".join(out)


def table_delta(ws, base):
    """Rows the plugin must lay over the base: workspace keys whose value differs from the base's, or that the base lacks."""
    return [(k, v) for k, v in ws.items() if base.get(k) != v]


def ship_rows(inp):
    """The table rows build ships, in workspace order: table_delta (the revised and added rows) and every row the review
    ledger lists (reviewed.tsv), whatever its value (a reviewed row equal to BASE_REF's ships too, so the mod plays without
    OverLlm)."""
    led = getattr(inp, "led_table", None) or {}
    return [(k, v) for k, v in inp.ws_tt.items() if inp.base_tt.get(k) != v or k in led]


# ---- XUnity scene lines (SceneDictionary semantics) ----------------------------------------------------------------------

def _hex4(h):
    t = h.strip(" \t\n\v\f\r")
    if not t or any(c not in "0123456789abcdefABCDEF" for c in t):
        return None
    return int(t, 16)


def decode(s):
    """SceneDictionary.ReadTranslationLineAndDecode: [key, value], or None where the loader rejects the line (including the
    lines on which the C# code throws: a bad \\u escape, a '//' after a second '=')."""
    if not s:
        return None
    arr, num, esc, sb, i, n = [None, None], 0, False, [], 0, len(s)
    while i < n:
        c = s[i]
        if esc:
            if c == "=" or c == BS:
                sb.append(c)
            elif c == "n":
                sb.append("\n")
            elif c == "r":
                sb.append("\r")
            elif c == "u":
                if i + 4 < n:
                    v = _hex4(s[i + 1:i + 5])
                    if v is None:
                        return None
                    sb.append(chr(v))
                    i += 4
                else:
                    return None
            else:
                sb.append(BS)
                sb.append(c)
            esc = False
        elif c == BS:
            esc = True
        elif c == "=":
            if num > 1:
                return None
            arr[num] = "".join(sb)
            num += 1
            sb = []
        elif c == "%" and i + 2 < n and s[i + 1] == "3" and s[i + 2] == "D":
            sb.append("=")
            i += 2
        elif c == "/" and i + 1 < n and s[i + 1] == "/":
            if num > 1:
                return None
            arr[num] = "".join(sb)
            num += 1
            return arr if num == 2 else None
        else:
            sb.append(c)
        i += 1
    if num != 1:
        return None
    arr[1] = "".join(sb)
    return arr


def raw_key(s):
    """The line's text before its first unescaped '=' (the whole line when there is none)."""
    esc = False
    for i, c in enumerate(s):
        if esc:
            esc = False
        elif c == BS:
            esc = True
        elif c == "=":
            return s[:i]
    return s


def encode_scene(s):
    """The escaped form SceneDictionary decodes back to s (backslash, CR, LF and '=' escaped), or None when s cannot be
    written in a line ('//' would start a comment, '%3D' would decode to '=')."""
    out = []
    for c in s:
        if c == BS:
            out.append(BS + BS)
        elif c == LF:
            out.append(BS + "n")
        elif c == CR:
            out.append(BS + "r")
        elif c == "=":
            out.append(BS + "=")
        else:
            out.append(c)
    r = "".join(out)
    if "//" in r or "%3D" in r:
        return None
    return r


def scene_line(k, v):
    """One XUnity line that SceneDictionary reads as k -> v."""
    ek, ev = encode_scene(k), encode_scene(v)
    if ek is None or ev is None or ek.startswith(DIRECTIVES) or k.startswith(("r:", "sr:")):
        raise PublishError("cannot write a scene line for %r" % k[:60])
    line = ek + "=" + ev
    if decode(line) != [k, v]:
        raise PublishError("cannot write a scene line for %r (it would not decode back)" % k[:60])
    return line


class SceneFile:
    """One XUnity-format file: its physical lines (terminators removed) and the decoded entries SceneDictionary reads."""

    def __init__(self, path=None, data=None, label=None):
        raw = file_bytes(path) if data is None else data
        self.label = label or path
        self.size = len(raw)
        self.md5 = md5_bytes(raw)
        self.bom = raw.startswith(UTF8_BOM)
        text = (raw[3:] if self.bom else raw).decode("utf-8")
        self.nl = "\r\n" if "\r\n" in text else "\n"
        self.ends_nl = text.endswith(self.nl) or text == ""
        lines = text.split(self.nl)
        if lines and lines[-1] == "":
            lines.pop()
        self.lines = lines
        # a line break other than the file's own splits a physical line in SceneDictionary (it splits at every \r and \n)
        self.odd_breaks = [i for i, l in enumerate(lines) if "\r" in l or "\n" in l]
        self.entries, self.rejected, self.directives = [], [], 0
        for i, l in enumerate(lines):
            for piece in (re.split("[\r\n]", l) if "\r" in l or "\n" in l else (l,)):
                if not piece:
                    continue
                if piece.startswith(DIRECTIVES):
                    self.directives += 1
                    continue
                kv = decode(piece)
                if kv is None:
                    self.rejected.append(i)
                    continue
                self.entries.append((kv[0], kv[1], i))
        self.map, self.last, self.regex = {}, {}, []
        for k, v, i in self.entries:
            if not k or not v or k.startswith("sr:"):
                continue
            if k.startswith("r:"):
                self.regex.append((k, v))
                continue
            self.map[k] = v
            self.last[k] = i

    def serialize(self, lines):
        return (UTF8_BOM if self.bom else b"") + "".join(l + self.nl for l in lines).encode("utf-8")


# char.IsWhiteSpace (Unicode Zs/Zl/Zp plus \t \n \v \f \r, U+0085, U+00A0)
_WS = frozenset(chr(c) for c in [9, 10, 11, 12, 13, 32, 0x85, 0xA0, 0x1680] + list(range(0x2000, 0x200B)) + [0x2028, 0x2029, 0x202F, 0x205F, 0x3000])


def _internal_trim(text, between_words):
    """UntranslatedText.PerformInternalTrimming (SceneDictionary.PerformInternalTrimming), statement for statement."""
    if "\n" not in text:
        return text
    b, changed, n, pending, i = [], False, len(text), -1, 0
    while i < n:
        c = text[i]
        if c == "\n":
            start = i - 1
            while start >= 0 and text[start] in _WS:
                start -= 1
            end = i + 1
            while end < n and text[end] in _WS:
                end += 1
            start += 1
            end -= 1
            span = end - start
            last = "\0"
            if span > 0:
                kept, prev, in_run = 0, text[start], False
                start += 1
                k = start
                while k <= end:
                    c4 = text[k]
                    if c4 == prev:
                        if not in_run:
                            kept += 1
                            b.append(prev)
                            in_run = True
                        kept += 1
                        b.append(c4)
                        last = c4
                    elif prev == "\r" and c4 == "\n":
                        if k + 2 > end:
                            k += 1
                            continue
                        if text[k + 1] == "\r" and text[k + 2] == "\n":
                            if not in_run:
                                kept += 1
                                b.append("\r")
                                b.append("\n")
                                in_run = True
                            kept += 1
                            b.append("\r")
                            b.append("\n")
                            last = "\n"
                            k += 1
                    else:
                        in_run = False
                        prev = c4
                    k += 1
                if kept - 1 != span:
                    changed = True
            else:
                changed = True
            if between_words and last not in _WS and b and b[-1] != " ":
                changed = True
                b.append(" ")
            i = end
            pending = -1
        elif c not in _WS:
            if pending != -1:
                b.extend(text[pending:i])
                pending = -1
            b.append(c)
        elif pending == -1:
            pending = i
        i += 1
    if pending != -1:
        b.extend(text[pending:i])
    return "".join(b) if changed else text


def key_forms(text, between_words):
    """SceneDictionary.Key(text, removeInternalWhitespace: true, ...): (ExternallyTrimmed, FullyTrimmed)."""
    n, i = len(text), 0
    while i < n and text[i] in _WS:
        i += 1
    lead, trail = i, 0
    if i != n:
        j = n - 1
        while j > -1 and text[j] in _WS:
            j -= 1
        trail = n - 1 - j
    ext = text[lead:n - trail] if (lead > 0 or trail > 0) else text
    return ext, _internal_trim(ext, between_words)


class SceneSim:
    """What SceneDictionary holds after loading: map (with the trimmed variants), known values, regexes, insertion order."""

    def __init__(self):
        self.map, self.values, self.regexes = {}, set(), []
        self.dups = self.empty = self.removed = self.variants = 0
        self.pre = None

    def ingest(self, k, v):
        if not k or not v:
            self.empty += 1
            return
        if k.startswith("sr:"):
            return
        if k.startswith("r:"):
            self.regexes.append((k, v))
            return
        if k in self.map:
            self.dups += 1
        self.map[k] = v
        self.values.add(v)

    def finish(self):
        self.pre = dict(self.map)
        for k, v in list(self.map.items()):
            kext, kfull = key_forms(k, False)
            vext, vfull = key_forms(v, True)
            if kext != k and kext not in self.map:
                self.map[kext] = vext
                self.values.add(vext)
                self.variants += 1
            if kext != kfull and kfull not in self.map:
                self.map[kfull] = vfull
                self.values.add(vfull)
                self.variants += 1
        return self


def sim_load(entries):
    """SceneDictionary.Load of one file."""
    d = SceneSim()
    for k, v, _ in entries:
        d.ingest(k, v)
    return d.finish()


def sim_overlay(base_entries, over_entries):
    """SceneDictionary.LoadOverlay: base lines in place, each taking this mod's value where it has the key (an empty value
    removes it), then this mod's lines the base lacks, in file order."""
    d = SceneSim()
    sub = {}
    for k, v, _ in over_entries:
        sub[k] = v
    seen = set()
    for k, v, _ in base_entries:
        if k in sub:
            seen.add(k)
            mine = sub[k]
            if not mine:
                d.removed += 1
                continue
            d.ingest(k, mine)
        else:
            d.ingest(k, v)
    for k, v, _ in over_entries:
        if k not in seen:
            d.ingest(k, v)
    return d.finish()


def variant_collisions(pre):
    """Trimmed-variant keys that two or more keys produce with different values: which one wins depends on load order."""
    cand = collections.defaultdict(set)
    for k, v in pre.items():
        kext, kfull = key_forms(k, False)
        vext, vfull = key_forms(v, True)
        if kext != k and kext not in pre:
            cand[kext].add(vext)
        if kext != kfull and kfull not in pre:
            cand[kfull].add(vfull)
    return {k: vs for k, vs in cand.items() if len(vs) > 1}


def hygiene_says(ws, base):
    """What 'scene_hygiene.py --live' will do to the workspace scene file, in words: its own plan (scene_hygiene.describe)
    for SceneFile ws laid over SceneFile base, so the refusal says exactly what running it changes."""
    try:
        import importlib
        me = sys.modules.get(__name__)
        if me is not None and getattr(me, "decode", None) is decode:
            sys.modules.setdefault("publish", me)          # scene_hygiene's 'import publish' gets this module
        return importlib.import_module("hygiene.scene_hygiene").describe(ws.lines, base)
    except Exception as e:
        return ("(what it will do could not be worked out here: %s: %s; run it without --live first, which writes its result "
                "and change log to the staged folder only)" % (type(e).__name__, e))


def scene_delta(ws, base, reviewed=()):
    """(line indexes of the workspace lines to ship, in file order; tombstone lines; explicit trimmed-variant lines) for
    SceneFile ws over SceneFile base. reviewed: decoded keys whose line ships whatever its value (the scene ledger's)."""
    if ws.regex != base.regex:
        raise PublishError("the workspace's regex (r:) lines differ from BASE_REF's; overlay-1 cannot express that")
    if ws.odd_breaks:
        raise PublishError("workspace scene lines %s hold a stray line break (a CR or LF other than the file's own line "
                           "breaks; SceneDictionary splits a line at every CR and LF), e.g. %r. Run: %s --live. It will %s" % (
                               [i + 1 for i in ws.odd_breaks[:10]], ws.lines[ws.odd_breaks[0]][:80], HYGIENE_CMD,
                               hygiene_says(ws, base)))
    # A workspace line the loader rejects (an unescaped '=' or a '//' in the English) is not in the workspace map: shipped
    # as it is, it would turn into a silent removal of OverLlm's line. Only lines inherited unchanged from BASE_REF (rejected
    # there too) are let through.
    base_rejected = {base.lines[i] for i in base.rejected}
    own = sorted({i for i in ws.rejected if ws.lines[i] not in base_rejected})
    if own:
        raise PublishError("workspace scene lines %s are rejected by the loader (an unescaped '=' or a '//' in the English?), so "
                           "publishing would silently drop them, e.g. %r. Run: %s --live. It will %s" % (
                               [i + 1 for i in own[:10]], ws.lines[own[0]][:80], HYGIENE_CMD, hygiene_says(ws, base)))
    rejected_keys = set()
    for i in ws.rejected:
        kv = decode(raw_key(ws.lines[i]) + "=x")
        if kv:
            rejected_keys.add(kv[0])
    keys = [k for k, v in ws.map.items() if base.map.get(k) != v or k in reviewed]
    idx = sorted(ws.last[k] for k in keys)
    tombs = []
    for k in base.map:
        if k not in ws.map:
            if k in rejected_keys:
                raise PublishError("BASE_REF key %r would be removed (the workspace has no loadable line for it), but the "
                                   "workspace has a line for it that the loader rejects. Run: %s --live. It will %s" % (
                                       k[:60], HYGIENE_CMD, hygiene_says(ws, base)))
            line = raw_key(base.lines[base.last[k]]) + "="
            if decode(line) != [k, ""]:
                raise PublishError("cannot write a removal line for BASE_REF key %r" % k)
            tombs.append(line)
    extra = variant_lines(ws, base, [ws.lines[i] for i in idx] + tombs)
    return idx, tombs, extra


def variant_lines(ws, base, ship_lines):
    """Explicit lines for the trimmed-variant keys whose value the overlay would take from a different line than the
    workspace load does: the winner of a variant two keys produce depends on load order, and the lines the release lacks
    load last in the overlay. A real line for the variant key fixes its value in both loads."""
    wl = sim_load(ws.entries)
    if not variant_collisions(wl.pre):
        return []
    ship = SceneFile(data=ws.serialize(ship_lines), label="planned scene lines")
    extra = collections.OrderedDict()
    for _ in range(6):
        ov = sim_overlay(base.entries, ship.entries + [(k, v, -1) for k, v in extra.items()])
        diff = sorted(k for k in wl.map if k not in wl.pre and ov.map.get(k) != wl.map[k])
        if not diff:
            break
        for k in diff:
            if k in extra or k in ov.pre:
                raise PublishError("cannot pin the trimmed-variant scene key %r to the workspace's value" % k[:60])
            extra[k] = wl.map[k]
    else:
        raise PublishError("the trimmed-variant scene keys do not settle")
    return [scene_line(k, v) for k, v in extra.items()]


# ---- text comparison with OverLlm's English -------------------------------------------------------------------------------

_HYPHENS = dict.fromkeys(map(ord, "‐‑‒–—−"), "-")
_WORD = re.compile(r"[a-z0-9]+(?:['-][a-z0-9]+)*")


def _norm(s):
    return re.sub(r"\s+", " ", (s or "").translate(_HYPHENS)).strip().lower()


def _words(s):
    return _WORD.findall(_norm(s))


def _ngrams(ws, n=4):
    return {tuple(ws[i:i + n]) for i in range(len(ws) - n + 1)}


def _grams4(ns):
    ws = _WORD.findall(ns)
    return {" ".join(ws[i:i + 4]) for i in range(len(ws) - 3)}


def _alnum(c):
    return ("a" <= c <= "z") or ("0" <= c <= "9")


def _phrase_in(ph, text):
    """ph occurs in text with no letter or digit glued to either end (both normalised)."""
    if not ph or not text:
        return False
    start = 0
    while True:
        i = text.find(ph, start)
        if i < 0:
            return False
        j = i + len(ph)
        if (i == 0 or not _alnum(text[i - 1]) or not _alnum(ph[0])) and (j == len(text) or not _alnum(text[j]) or not _alnum(ph[-1])):
            return True
        start = i + 1


class OverLlm:
    """OverLlm's English (BASE_REF and the installed patch files), as the checks compare text with it."""

    def __init__(self, values):
        vals = set()
        for v in values:
            nv = _norm(v)
            if nv and _HAS_LETTER.search(nv):
                vals.add(nv)
        self.values = vals
        self.text = NUL.join(sorted(vals))
        g4 = set()
        idx = collections.defaultdict(list)
        for nv in vals:
            g4 |= _grams4(nv)
            if len(nv) >= 12 and not _CJK.search(nv):     # OverLlm's English, not the game's text it left as it was
                idx[nv[:12]].append(nv)
        self.grams4 = g4
        self.long_idx = idx


class Ours:
    """This mod's shipped wording (every shipped row, line and override text), normalised as the note cleaner and the text
    scan compare it (values kept apart so that no phrase spans two)."""

    _caches = {}

    def __init__(self, values, overllm=None):
        norms = [_norm(v) for v in values]
        self.text = NUL.join(norms)
        self.ol = overllm
        digest = md5_bytes(self.text.encode("utf-8")) + ":" + str(id(overllm))
        c = Ours._caches.get(digest)
        if c is None:
            g4 = set()
            for nv in norms:
                g4 |= _grams4(nv)
            c = Ours._caches[digest] = ({}, {}, g4, {})
            if len(Ours._caches) > 8:
                Ours._caches.pop(next(iter(Ours._caches)))
        self._has, self._base, self.grams4, self._only = c

    def has(self, phrase):
        """phrase occurs in this mod's text with no letter or digit glued to either end."""
        r = self._has.get(phrase)
        if r is None:
            t, n, m, start, r = self.text, len(self.text), len(phrase), 0, False
            while phrase:
                i = t.find(phrase, start)
                if i < 0:
                    break
                j = i + m
                if (i == 0 or not _alnum(t[i - 1])) and (j == n or not _alnum(t[j])):
                    r = True
                    break
                start = i + 1
            self._has[phrase] = r
        return r

    def ol_only(self, v):
        """v (a normalised OverLlm value) is not in this mod's text as a phrase, in the sense scan_text finds it in a text
        (_phrase_in: no letter or digit glued to an end of v that is itself a letter or digit; 'little junior sister～' is in
        'little junior sister～are you ready'). Decided without a search of this mod's text when a run of 4 of v's inner
        words (its first and last word may be parts of longer words of ours, the inner ones cannot) is not in this mod's
        text. Cached, so each value costs at most one search per check."""
        r = self._only.get(v)
        if r is None:
            ws = _WORD.findall(v)[1:-1]
            if len(ws) >= 4 and any(" ".join(ws[i:i + 4]) not in self.grams4 for i in range(len(ws) - 3)):
                r = True
            else:
                r = not _phrase_in(v, self.text)
            self._only[v] = r
        return r

    def in_base(self, phrase):
        if self.ol is None:
            return False
        r = self._base.get(phrase)
        if r is None:
            r = self._base[phrase] = phrase in self.ol.text
        return r


def scan_text(text, ol, ours, limit=None):
    """[(what, phrase)]: whole OverLlm values of 12+ characters and runs of 4+ words that only OverLlm's text has (this mod's
    own shipped text does not), found in text; at most limit whole values when limit is given. One pass over the text: a
    12-character window is looked up in OverLlm's index of long values, a candidate is decided by Ours.ol_only (cached), and
    each 4-word run is two set lookups."""
    hits = []
    if ol is None:
        return hits
    nt = _norm(text)
    if len(nt) >= 12:
        idx = ol.long_idx
        seen = set()
        for i in range(len(nt) - 11):
            lst = idx.get(nt[i:i + 12])
            if lst:
                for v in lst:
                    if v not in seen and nt.startswith(v, i) and _phrase_in(v, nt[max(0, i - 1):i + len(v) + 1]) and ours.ol_only(v):
                        seen.add(v)
                        hits.append(("a whole OverLlm value", v))
                if limit is not None and len(hits) >= limit:
                    return hits[:limit]
    ws = _WORD.findall(nt)
    for i in range(len(ws) - 3):
        g = " ".join(ws[i:i + 4])
        if g in ol.grams4 and g not in ours.grams4 and _wordy(ws[i:i + 4]):
            hits.append(("4 words only OverLlm's text has", g))
            break
    return hits


def _wordy(words):
    """A run of words that is wording, not a string of numbers or letters (two of its words have 3+ letters)."""
    return sum(1 for w in words if sum(1 for c in w if "a" <= c <= "z") >= 3) >= 2


def strip_json_comments(s):
    """Newtonsoft's comment syntax ('//' to the end of the line, '/* */') removed outside strings; (text, comment count)."""
    out, i, n, in_str, count = [], 0, len(s), False, 0
    while i < n:
        c = s[i]
        if in_str:
            out.append(c)
            if c == BS and i + 1 < n:
                out.append(s[i + 1])
                i += 2
                continue
            if c == '"':
                in_str = False
            i += 1
            continue
        if c == '"':
            in_str = True
            out.append(c)
            i += 1
            continue
        if c == "/" and i + 1 < n and s[i + 1] == "/":
            j = s.find("\n", i)
            i = n if j < 0 else j
            count += 1
            continue
        if c == "/" and i + 1 < n and s[i + 1] == "*":
            j = s.find("*/", i + 2)
            i = n if j < 0 else j + 2
            count += 1
            continue
        out.append(c)
        i += 1
    return "".join(out), count


def _json_strings(o, path="$", keys=True):
    """(path, string) for every string in a parsed JSON value (and every object key, unless keys=False)."""
    if isinstance(o, dict):
        for k, v in o.items():
            if keys:
                yield path + "." + str(k) + "(key)", str(k)
            for x in _json_strings(v, path + "." + str(k), keys):
                yield x
    elif isinstance(o, list):
        for i, v in enumerate(o):
            for x in _json_strings(v, "%s[%d]" % (path, i), keys):
                yield x
    elif isinstance(o, str):
        yield path, o


# archives and compressed files (their content is not text the scan can read, and could be anything: OverLlm's files too)
_ARCHIVE_MAGIC = (b"PK\x03\x04", b"PK\x05\x06", b"PK\x07\x08", b"\x1f\x8b", b"7z\xbc\xaf\x27\x1c", b"Rar!\x1a\x07",
                  b"\xfd7zXZ\x00", b"\x28\xb5\x2f\xfd", b"\x04\x22\x4d\x18", b"MSCF\x00", b"\xd0\xcf\x11\xe0\xa1\xb1\x1a\xe1")
_BZIP2 = re.compile(b"^BZh[1-9]1AY&SY")


def decode_text(data):
    """(text, encoding) for a text file, read the way a person or a tool would: UTF-8 (a BOM skipped, stray bytes replaced),
    UTF-16 or UTF-32 with a BOM, UTF-16 without one (NUL bytes on one side of the ASCII characters); (None, why) for an
    archive, a compressed file or any other binary file."""
    if data.startswith(_ARCHIVE_MAGIC) or _BZIP2.match(data):
        return None, "an archive or compressed file"
    if data.startswith((b"\xff\xfe\x00\x00", b"\x00\x00\xfe\xff")):
        return data.decode("utf-32", "replace"), "UTF-32"
    if data.startswith((b"\xff\xfe", b"\xfe\xff")):
        return data.decode("utf-16", "replace"), "UTF-16"
    if data.startswith(UTF8_BOM):
        data = data[3:]
    head = data[:65536]
    if b"\x00" in head:
        n = len(head) // 2
        even, odd = head[0::2].count(0), head[1::2].count(0)
        if n and odd >= 0.4 * n and even <= 0.02 * n:
            return data.decode("utf-16-le", "replace"), "UTF-16LE"
        if n and even >= 0.4 * n and odd <= 0.02 * n:
            return data.decode("utf-16-be", "replace"), "UTF-16BE"
        return None, "binary (NUL bytes)"
    try:
        t, how = data.decode("utf-8"), "UTF-8"
    except UnicodeDecodeError:
        t, how = data.decode("utf-8", "replace"), "UTF-8 with stray bytes"
    # a file that is mostly undecodable or control bytes is not text (a raw deflate stream has no NUL byte, say)
    bad = len(_NOT_TEXT.findall(t))
    if bad > max(8, len(t) // 10):
        return None, "binary (%d of its %d characters are not text)" % (bad, len(t))
    return t, how


# undecodable bytes (U+FFFD after decoding) and control characters other than TAB, LF, VT, FF and CR
_NOT_TEXT = re.compile("[%s-%s%s-%s%s]" % (chr(0), chr(8), chr(14), chr(31), chr(0xFFFD)))


def text_segments(rel, data, base_keys=None):
    """The pieces of a file the text scan reads, as (where, text), or None for a binary file (decode_text). Files split into
    their own fields (CSV fields, JSON strings, lines), so that no phrase spans two. The shipped table and scene files
    contribute their keys only (their values are this mod's own text by definition), and only the keys this mod adds: a key
    BASE_REF has is the address of OverLlm's line (the game's text, which a revision or a removal line must name)."""
    bk = base_keys or {}
    t, how = decode_text(data)
    if t is None:
        return None
    low = rel.lower()
    if rel == REL_TABLE:
        return [("key " + k, k) for k in table_map(t)[0] if k not in bk.get("table", ())]
    if low.startswith("translation/scene/") and low.endswith(".txt") and "/resize/" not in low:
        segs = []
        for n, line in enumerate(re.split("\r\n|\n|\r", t), 1):
            kv = decode(line)
            if kv and kv[0] in bk.get("scene", ()):
                continue
            segs.append(("line %d key" % n, kv[0] if kv else line))
        return segs
    if low.endswith(".csv"):
        segs = []
        for n, r in enumerate(parse_strings(t), 1):
            for j, f in enumerate(r["fields"]):
                segs.append(("row %d field %d" % (n, j + 1), f[0]))
        return segs
    if low.endswith(".json"):
        try:
            o = json.loads(strip_json_comments(t)[0])
            return list(_json_strings(o))
        except ValueError:
            pass
    return [("line %d" % n, line) for n, line in enumerate(re.split("\r\n|\n|\r", t), 1)]


HITS_PER_FILE = 20
MAX_TEXT_FILE = 16 << 20
# the plugin DLL for BepInEx 6, and the same source built for BepInEx 5 (build.ps1 -BepInEx5; Lash's English Patch runs on
# BepInEx 5 since its llmkit-upgrade)
_DLL_NAME = re.compile(r"^LOM_UI_EN(\.BepInEx5)?\.dll$", re.I)
_SPRITE_NAME = re.compile(r"^sprites/[^/]+\.png$", re.I)
# third-party libraries the plugin folder ships, pinned by SHA-256 (a known file, byte for byte, cannot hide anything):
# Newtonsoft.Json 13.0.2, Unity's AOT build: Runtime/AOT/Newtonsoft.Json.dll of Unity's com.unity.nuget.newtonsoft-json 3.2.2
# (the same file is in 3.2.1). The game has none; the OverLlm patch puts this same file in BepInEx/core (which then loads
# first). Not the official NuGet build: that one generates code at run time (Reflection.Emit), which this game's stripped
# runtime refuses ("Operation is not supported on this platform"), so no rule, image map or font map could be read.
PINNED_LIBS = {"Newtonsoft.Json.dll": "a56146202232958f46bd6a28b5a7da166aea123ee0d646735a46e5c341dfbf1f"}
_PNG_SIG = b"\x89PNG\r\n\x1a\n"
# the chunk types a sprite may carry: image data and fixed-form colour / size information, plus the three text chunks
# (their text is scanned); anything else (iCCP, eXIf, private chunks) could carry any bytes at all, so it fails
_PNG_CHUNKS = frozenset(t.encode("ascii") for t in ("IHDR", "PLTE", "IDAT", "IEND", "tRNS", "cHRM", "gAMA", "sBIT", "sRGB",
                                                    "cICP", "mDCv", "cLLi", "bKGD", "hIST", "pHYs", "sPLT", "tIME",
                                                    "tEXt", "zTXt", "iTXt"))
_PNG_DEPTHS = {0: (1, 2, 4, 8, 16), 2: (8, 16), 3: (1, 2, 4, 8), 4: (8, 16), 6: (8, 16)}
_PNG_CHANNELS = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}
_ADAM7 = ((0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2))
_PNG_MAX_RAW = 256 << 20


def _inflate(data, limit):
    """zlib data inflated to at most limit bytes: (bytes, the stream ended exactly with nothing after it)."""
    import zlib
    d = zlib.decompressobj()
    try:
        out = d.decompress(data, limit + 1)
        out += d.flush() if not d.unconsumed_tail and len(out) <= limit else b""
    except zlib.error:
        return None, False
    return out, d.eof and not d.unused_data and not d.unconsumed_tail


def png_check(data):
    """(problem or None, [(where, text)] of its text chunks) for a sprite: the PNG signature, then a chunk walk (each chunk's
    length inside the file and its CRC right, IHDR first, only _PNG_CHUNKS types) to IEND with nothing after it, and the
    image data inflating to exactly the size IHDR gives (so no other data hides in it)."""
    import zlib
    texts = []
    if not data.startswith(_PNG_SIG):
        return "no PNG signature", texts
    i, n, ihdr, idat = 8, len(data), None, []
    while True:
        if i + 12 > n:
            return "the chunks end without IEND (truncated at byte %d)" % i, texts
        ln = int.from_bytes(data[i:i + 4], "big")
        t = data[i + 4:i + 8]
        if ln > 0x7FFFFFFF or i + 12 + ln > n:
            return "chunk %r at byte %d runs past the end of the file" % (t, i), texts
        body = data[i + 8:i + 8 + ln]
        if int.from_bytes(data[i + 8 + ln:i + 12 + ln], "big") != zlib.crc32(t + body) & 0xFFFFFFFF:
            return "chunk %r at byte %d has a wrong CRC" % (t, i), texts
        if not all(65 <= c <= 90 or 97 <= c <= 122 for c in t):
            return "a chunk type that is not four letters (%r) at byte %d" % (t, i), texts
        if ihdr is None and t != b"IHDR":
            return "the first chunk is %r, not IHDR" % t, texts
        if t not in _PNG_CHUNKS:
            return ("a chunk of type %s (a sprite needs only image data and colour / size chunks; this one could carry any "
                    "bytes: save the sprite without it)" % t.decode("latin-1"), texts)
        if t == b"IHDR":
            if ihdr is not None or ln != 13:
                return "a second or malformed IHDR chunk", texts
            ihdr = body
        elif t == b"IDAT":
            idat.append(body)
        elif t in (b"tEXt", b"zTXt", b"iTXt"):
            kw, _, rest = body.partition(b"\x00")
            txt = None
            if t == b"tEXt":
                txt = rest.decode("latin-1")
            elif t == b"zTXt":
                raw, ok = _inflate(rest[1:], MAX_TEXT_FILE)
                txt = raw.decode("latin-1") if ok and raw is not None else None
            else:
                flag = rest[:1]
                parts = rest[2:].split(b"\x00", 2)
                if len(parts) == 3:
                    raw, ok = (_inflate(parts[2], MAX_TEXT_FILE) if flag == b"\x01" else (parts[2], True))
                    txt = raw.decode("utf-8", "replace") if ok and raw is not None else None
            if txt is None:
                return "a %s text chunk at byte %d that cannot be read" % (t.decode("ascii"), i), texts
            texts.append(("%s chunk %s" % (t.decode("ascii"), kw.decode("latin-1", "replace")[:40]), txt))
        i += 12 + ln
        if t == b"IEND":
            if ln:
                return "IEND carries data", texts
            if i != n:
                return "%d bytes after IEND (appended data)" % (n - i), texts
            break
    w, h, depth, ctype, comp, filt, inter = (int.from_bytes(ihdr[0:4], "big"), int.from_bytes(ihdr[4:8], "big"), ihdr[8],
                                             ihdr[9], ihdr[10], ihdr[11], ihdr[12])
    if not w or not h or ctype not in _PNG_DEPTHS or depth not in _PNG_DEPTHS[ctype] or comp or filt or inter > 1:
        return "IHDR is not a valid PNG header (%dx%d, depth %d, colour type %d)" % (w, h, depth, ctype), texts
    bits = depth * _PNG_CHANNELS[ctype]
    rowb = lambda px: (px * bits + 7) // 8
    if inter:
        want = sum(((h - y0 + dy - 1) // dy) * (1 + rowb((w - x0 + dx - 1) // dx))
                   for x0, y0, dx, dy in _ADAM7 if w > x0 and h > y0)
    else:
        want = h * (1 + rowb(w))
    if not idat:
        return "no image data (IDAT)", texts
    if want > _PNG_MAX_RAW:
        return "an image of %dx%d (too large for a sprite)" % (w, h), texts
    raw, ok = _inflate(b"".join(idat), want)
    if raw is None or not ok or len(raw) != want:
        return ("the image data does not inflate to the %d bytes a %dx%d image has (%s)" % (
            want, w, h, "not a zlib stream" if raw is None else "%d%s bytes%s" % (
                len(raw), "+" if len(raw) > want else "", "" if ok else ", or data after the stream")), texts)
    return None, texts


def pe_check(data):
    """None for a well-formed PE file (a .NET assembly) that ends where its last section (or its certificate table) ends,
    else why not."""
    n = len(data)
    u16 = lambda o: int.from_bytes(data[o:o + 2], "little")
    u32 = lambda o: int.from_bytes(data[o:o + 4], "little")
    if n < 0x40 or data[:2] != b"MZ":
        return "no MZ header"
    o = u32(0x3C)
    if o + 24 > n or data[o:o + 4] != b"PE\x00\x00":
        return "no PE header"
    nsec, soh = u16(o + 6), u16(o + 20)
    opt = o + 24
    magic = u16(opt)
    if magic not in (0x10B, 0x20B) or opt + soh > n:
        return "no valid optional header"
    dd = opt + (96 if magic == 0x10B else 112)
    ndd = u32(dd - 4)
    if ndd > 16 or dd + 8 * ndd > opt + soh:
        return "a malformed data directory"
    end = opt + soh + 40 * nsec
    if not nsec or end > n:
        return "a malformed section table"
    for s in range(nsec):
        e = opt + soh + 40 * s
        size, ptr = u32(e + 16), u32(e + 20)
        if size:
            end = max(end, ptr + size)
    if ndd > 4 and u32(dd + 36):
        end = max(end, u32(dd + 32) + u32(dd + 36))
    if ndd <= 14 or not u32(dd + 14 * 8 + 4):
        return "not a .NET assembly (no CLR header)"
    if end > n:
        return "its sections run past the end of the file"
    if n > end:
        return "%d bytes after its last section (appended data)" % (n - end)
    return None


def binary_problem(rel, data):
    """None when rel is one of the binary files a plugin folder holds and its bytes are that kind of file: the plugin DLL
    (LOM_UI_EN.dll at the folder's root, a well-formed PE file) or a sprite (sprites/*.png, a well-formed PNG); else why
    not. Their content cannot be read as text, so anything else binary could hold anything (OverLlm's files too)."""
    if _DLL_NAME.match(rel):
        p = pe_check(data)
        return None if p is None else "named like the plugin DLL, but not a well-formed PE file: %s" % p
    if _SPRITE_NAME.match(rel):
        p = png_check(data)[0]
        return None if p is None else "named like a sprite, but not a well-formed PNG: %s" % p
    if rel in PINNED_LIBS:
        return None if hashlib.sha256(data).hexdigest() == PINNED_LIBS[rel] else (
            "named like the shipped %s, but not the pinned official file" % rel)
    return "a plugin folder holds text files, LOM_UI_EN.dll, LOM_UI_EN.BepInEx5.dll, %s and sprites/*.png only" % ", ".join(sorted(PINNED_LIBS))


def expected_binary(rel, data=None):
    """rel is named like one of the binary files a plugin folder holds (and, with data, is one: binary_problem)."""
    if data is not None:
        return binary_problem(rel, data) is None
    return bool(_DLL_NAME.match(rel) or _SPRITE_NAME.match(rel) or rel in PINNED_LIBS)


def scan_files(files, ol, ours, base_keys=None, limit=40):
    """Problems for OverLlm text in {rel: bytes} (a file's scan stops at its first HITS_PER_FILE hits), and for binary files
    other than the expected ones (binary_problem: the DLL, sprites/*.png, each checked to be that kind of file; a sprite's
    text chunks are scanned too); and the list of the expected binary files."""
    probs, binary = [], []
    for rel in sorted(files):
        segs = text_segments(rel, files[rel], base_keys)
        if segs is None:
            why = binary_problem(rel, files[rel])
            if why is not None:
                if len(probs) < limit:
                    probs.append("unexpected binary file %s (%s; %s: a zip, gzip or other binary file cannot be checked and "
                                 "could hold OverLlm's files)" % (rel, decode_text(files[rel])[1], why))
                continue
            binary.append(rel)
            segs = png_check(files[rel])[1] if _SPRITE_NAME.match(rel) else []
        n = 0
        for where, text in segs:
            if not text or len(text) < 12 and text.count(" ") < 3:
                continue
            for what, ph in scan_text(text, ol, ours, limit=HITS_PER_FILE - n):
                n += 1
                if n <= 2 and len(probs) < limit:
                    probs.append("OverLlm text in %s (%s): %s %r" % (rel, where, what, ph[:80]))
            if n >= HITS_PER_FILE:
                break
        if n > 2 and len(probs) < limit:
            probs.append("OverLlm text in %s: %s hits" % (rel, "%d or more (the scan stops there)" % n if n >= HITS_PER_FILE else n))
    return probs, binary


# ---- strings/*.csv (StringOverrides.ParseCsv) and note cleaning ------------------------------------------------------------

def parse_strings(text):
    """StringOverrides.ParseCsv, keeping spans: rows of {"fields": [(value, start, end)], "start", "end"} over text (end of a
    row = just after its '\\n'; a field's raw span excludes the ',' / '\\r\\n' around it)."""
    rows, fields, sb, q = [], [], [], False
    start = row_start = 0
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if q:
            if c == '"':
                if i + 1 < n and text[i + 1] == '"':
                    sb.append('"')
                    i += 1
                else:
                    q = False
            else:
                sb.append(c)
            i += 1
            continue
        if c == '"':
            q = True
        elif c == ",":
            fields.append(("".join(sb), start, i))
            sb = []
            start = i + 1
        elif c == "\n":
            end = i - 1 if i > start and text[i - 1] == "\r" else i
            fields.append(("".join(sb), start, end))
            sb = []
            if len(fields) > 1 or fields[0][0]:
                rows.append({"fields": fields, "start": row_start, "end": i + 1})
            fields = []
            start = row_start = i + 1
        elif c != "\r":
            sb.append(c)
        i += 1
    if sb or fields:
        fields.append(("".join(sb), start, n))
        rows.append({"fields": fields, "start": row_start, "end": n})
    return rows


def csv_field(s):
    return '"' + s.replace('"', '""') + '"'


_QUOTE = re.compile(r"(?<![A-Za-z0-9])'((?:[^'\n]|'(?=[A-Za-z]))+?)'(?![A-Za-z0-9])|\"([^\"\n]+?)\"|"
                    + "‘([^’\n]+?)’|“([^”\n]+?)”")
# a quote opened and never closed: the end of a note that was cut short
_OPEN_TAIL = re.compile("(?:(?<![A-Za-z0-9])'|[\"‘“])((?:[^'\"‘’“”]|'(?=[A-Za-z]))+)$")
# a piece that starts by giving the old or on-screen text
_HEAD = re.compile(r"(?i)^\W*(?:(?:was|previously|formerly)\b|(?:screen|old)\s*[:=])")
# "ruled 20260912 (q04): was <old text>" / "glossary: was ..." / "2026-09-12 ruling: was ...": keep the head only
_WAS_TAIL = re.compile(r"(?is)^(?P<head>(?:ruled|glossary|\d{4}-?\d{2}-?\d{2})[^:]{0,60}?)\s*:\s*was\b.*$")
# "(was X/Y)" and "(..., was X)" inside a piece
_PAREN_WAS = re.compile(r"\s*\((?:was|previously|formerly)\b[^()]*\)", re.I)
_PAREN_INNER_WAS = re.compile(r"(\([^()]*?)[,;]\s*(?:was|previously|formerly)\b[^()]*(\))", re.I)
# words that say a quote is the old / other patch's wording
_OLD_WORDING = re.compile(r"(?i)\b(old|previous|prior|former|base|original)\s*(patch\s+had|line|text|value|wording|string|form|"
                          r"translation|english|rendering|quote|sentence|=|'s\b|had\b|used\b)|\bpatch'?s\s+own\b|\boverllm\b|"
                          r"\b(?:base|original|old|previous|other|english)\s+(?:patch|mod)\b")
# "..., not Ye Yunzhao" / "never Taoist": a capitalised phrase after not / never / instead of / rather than
_NOT_NAME = re.compile(r"\b(?:not|never|instead of|rather than)\s+((?:[A-Z][\w'-]*)(?:\s+[A-Z][\w'-]*)*)")


def _balanced(s):
    d = 0
    for c in s:
        d += (c == "(") - (c == ")")
        if d < 0:
            return False
    return d == 0


def _split_top(s, seps):
    """[(separator before, piece)] split at seps outside parentheses (anywhere when the parentheses do not balance)."""
    out, depth, i, start, before = [], 0, 0, 0, ""
    track = _balanced(s)
    while i < len(s):
        c = s[i]
        if track and c == "(":
            depth += 1
        elif track and c == ")":
            depth -= 1
        if depth == 0:
            hit = next((sep for sep in seps if s.startswith(sep, i)), None)
            if hit:
                out.append((before, s[start:i]))
                before = hit
                i += len(hit)
                start = i
                continue
        i += 1
    out.append((before, s[start:]))
    return out


def _quotes(s):
    for m in _QUOTE.finditer(s):
        yield next(g for g in m.groups() if g is not None)


def base_corpus(values):
    return NUL.join(_norm(v) for v in values)


def _in_refs(nq, refs):
    for r in refs:
        nr = _norm(r)
        if len(nq) <= 2:
            if re.search(r"(?<![a-z0-9])" + re.escape(nq) + r"(?![a-z0-9])", nr):
                return True
        elif nq in nr:
            return True
    return False


def _ref_named(nc, refs, row_ours):
    """The first ref (the row's OverLlm English, the note's was:/screen: text) that the normalised text names as a whole
    phrase while this mod's own text for the row does not."""
    for r in refs:
        nr = _norm(r).strip(" ,.;:!?")
        if len(nr) >= 3 and _HAS_LETTER.search(nr) and _phrase_in(nr, nc) and not _phrase_in(nr, row_ours):
            return r
    return None


_CAP_RUN = re.compile(r"(?<![A-Za-z0-9'])[A-Z][\w'-]*(?: [A-Z][\w'-]*)+")


def _names_ref_part(s, refs, row_ours):
    """A capitalised run of 2+ words (a name) in s that the row's OverLlm English has and this mod's text for the row lacks
    (e.g. 'Diamond Leg' for OverLlm's 'Diamond Leg Lv1'), or None."""
    for m in _CAP_RUN.finditer(s):
        nr = _norm(m.group(0))
        if any(_phrase_in(nr, _norm(r)) for r in refs) and not _phrase_in(nr, row_ours):
            return m.group(0)
    return None


def _exact(s):
    """Whitespace collapsed, case and hyphens kept: how a quote is compared with this mod's own text for the row."""
    return re.sub(r"\s+", " ", s or "").strip()


def _quote_is_old(q, refs, ours=None, row_ours="", nbase=None):
    """A quoted span is old wording when the row's OverLlm English (or the note's was/screen text) has it (or one of its
    '/'-separated alternatives; a single word: as a word) or it holds that English, or when it is several words that only the
    OverLlm text has. A quote of this mod's own text for the row, exactly as that text has it, stays."""
    nq = _norm(q).strip(" ,.;:!?")
    if not _HAS_LETTER.search(nq):
        return False
    eq = _exact(q).strip(" ,.;:!?")
    own = row_ours.split(NUL, 1)[1] if NUL in (row_ours or "") else ""
    if eq and own and _phrase_in(eq.lower(), own.lower()) and eq in own:
        return False
    if " " not in nq and "/" not in nq:
        # a single word: old wording when the row's OverLlm text (or the note's was/screen text) has that word
        return any(_phrase_in(nq, _norm(r)) for r in refs)
    alts = [nq] + [a.strip() for a in nq.split("/") if len(a.strip()) >= 3] if "/" in nq else [nq]
    if any(_in_refs(a, refs) for a in alts):
        return True
    for r in refs:
        nr = _norm(r).strip(" ,.;:!?")
        if len(nr) >= 3 and _HAS_LETTER.search(nr) and _phrase_in(nr, nq):
            return True
    if ours is not None and " " in nq and ours.in_base(nq) and not ours.has(nq):
        return True
    return False


def _clause_is_old(c, refs, ref_grams, ours=None, row_ours="", nbase=None):
    """Why this clause gives the old (OverLlm / screen) wording, or None."""
    s = c.strip()
    if _HEAD.match(s):
        return "starts with was/screen/old"
    if _ref_named(_norm(s), refs, row_ours) or _names_ref_part(s, refs, row_ours):
        return "names the row's OverLlm wording"
    qm = list(_QUOTE.finditer(s))
    qs = [next(g for g in m.groups() if g is not None) for m in qm]
    if any(_quote_is_old(q, refs, ours, row_ours, nbase) for q in qs):
        return "quotes the old wording"
    w = re.search(r"(?i)\bwas\b", s)
    if w and any(m.start() > w.start() for m in qm):
        return "quotes what the text was"
    t = _OPEN_TAIL.search(s)
    if t:
        nt = _norm(t.group(1)).strip(" ,.;:!?")
        if _OLD_WORDING.search(s) or re.search(r"(?i)\b(not|never|was)\s*$", s[:t.start()]) or (len(nt) >= 2 and _in_refs(nt, refs)):
            return "quotes the old wording (cut short)"
    if qs and _OLD_WORDING.search(s):
        return "quotes the old text"
    for m in _NOT_NAME.finditer(s):
        nn = _norm(m.group(1))
        if len(nn) >= 3 and _in_refs(nn, refs) and (" " in nn or ours is None or not ours.has(nn)):
            return "names the old wording after not/never"
    ws = _words(s)
    if ref_grams and len(ws) >= 4 and _ngrams(ws) & ref_grams[4]:
        return "repeats 4+ words of the old wording"
    if ref_grams and len(ws) >= 3:
        for g in _ngrams(ws, 3) & ref_grams[3]:
            if ours is None or not ours.has(" ".join(g)):
                return "repeats 3 words only the old wording has"
    if ours is not None and scan_text(s, ours.ol, ours):
        return "has wording only OverLlm's text has"
    return None


def _drop_old_asides(c, refs, ref_grams, ours, row_ours, nbase):
    """The clause without its parenthesised asides that give the old wording, when what is left gives none; else None."""
    out, changed = c, False
    for m in reversed(list(re.finditer(r"\s*\(([^()]*)\)", c))):
        if _clause_is_old(m.group(1), refs, ref_grams, ours, row_ours, nbase):
            out = out[:m.start()] + out[m.end():]
            changed = True
    if not changed or not out.strip() or _clause_is_old(out, refs, ref_grams, ours, row_ours, nbase):
        return None
    return out


def _as_refs(base_value):
    """OverLlm's English for a row as a list: one line (a str), or the lines of BASE_REF and the earlier releases."""
    if not base_value:
        return []
    if isinstance(base_value, str):
        return [base_value]
    out = []
    for v in base_value:
        if v and v not in out:
            out.append(v)
    return out


def note_problems(note, base_value, ours=None, row_ours=""):
    """Why a shipped note still carries OverLlm's wording (empty when it is clean). An independent test of the note as it is,
    not a rerun of the cleaner: a was:/screen: quote; the row's OverLlm English named as a whole phrase (this mod's own text
    for the row aside); a quote of that English; a whole OverLlm value or 4 words only OverLlm's text has."""
    probs = []
    if not note:
        return probs
    if re.search(r"(?i)\b(was|screen)\s*:", note):
        probs.append("has a was:/screen: quote")
    refs = _as_refs(base_value)
    r = _ref_named(_norm(note), refs, row_ours) or _names_ref_part(note, refs, row_ours)
    if r:
        probs.append("names the row's OverLlm wording %r" % r[:40])
    nbase = _norm(refs[0] if refs else "").strip(" ,.;:!?")
    for q in _quotes(note):
        if _quote_is_old(q, refs, ours, row_ours, nbase):
            probs.append("quotes the old wording %r" % q[:40])
            break
    if ours is not None:
        hits = scan_text(note, ours.ol, ours)
        if hits:
            probs.append("%s %r" % hits[0])
    return probs


def clean_note(note, base_value, ours=None, row_ours=""):
    """The note without the quoted OverLlm / game-screen wording. The 'was: ...' and 'screen: ...' segments go, and so does
    every other segment or clause (split at ' | ', ' — ', '; ' and sentence ends, outside parentheses) that gives the old
    wording: names the row's OverLlm English as a whole phrase or quotes it (this mod's own text for the row, and a single
    word this mod also uses that is not that English, aside), quotes what the text 'was', quotes several words only the
    OverLlm text has, repeats three or more of the old words, or has a whole OverLlm value or 4 words only OverLlm's text
    has. '(was X)' asides are cut out of the pieces that stay. Context and reasoning that give none of it stay, in their
    order; a last pass drops any piece that still makes note_problems() report the note. Returns (new note, [dropped])."""
    if not note:
        return note, []
    dropped = []
    body = note
    refs = _as_refs(base_value)
    nbase = _norm(refs[0] if refs else "").strip(" ,.;:!?")
    for bv in sorted(refs, key=len, reverse=True):
        if body.startswith("was: " + bv):
            rest = body[len("was: " + bv):]
            if rest == "" or rest.startswith(" | ") or rest.startswith(EM_SEP):
                dropped.append("was: " + bv)
                body = rest
                break
    segs = _split_top(body, (" | ", EM_SEP))
    if refs:
        # "screen: <an OverLlm text of the row> - reasoning": the reasoning after the exact old text is a segment of its own
        # (the row's line in BASE_REF or in any earlier release, longest first)
        split = []
        for before, seg in segs:
            for head in ("screen: ", "was: "):
                for bv in sorted(refs, key=len, reverse=True):
                    hit = next((d for d in (" - ", " – ") if seg.startswith(head + bv + d)), None)
                    if hit:
                        dropped.append(head + bv)
                        seg = seg[len(head + bv + hit):]
                        break
            split.append((before, seg))
        segs = split
    for _, seg in segs:
        m = re.match(r"(?is)^\s*(was|screen)\s*:\s*(.*)$", seg)
        if m and m.group(2).strip():
            refs.append(re.sub(r"\s*\((?:overflowed|with|mistranslation|lowercase)[^)]*\)\s*$", "", m.group(2).strip()))
    ref_grams = {3: set(), 4: set()}
    for r in refs:
        ref_grams[3] |= _ngrams(_words(r), 3)
        ref_grams[4] |= _ngrams(_words(r), 4)
    pieces = []          # [joiner, text] per kept clause, in order (the first joiner is dropped when rendering)
    for before, seg in segs:
        seg = seg.strip()
        if not seg:
            continue
        if _HEAD.match(seg):
            dropped.append(seg)
            continue
        m = _WAS_TAIL.match(seg)
        if m:
            dropped.append(seg[len(m.group("head")):].strip(" :"))
            seg = m.group("head").strip()
        clauses = _split_top(seg, ("; ", ". ", "! ", "? "))
        out = []
        for i, (sep, c) in enumerate(clauses):
            for rx, rep in ((_PAREN_WAS, ""), (_PAREN_INNER_WAS, r"\1\2")):
                c2 = rx.sub(rep, c)
                if c2 != c:
                    dropped.append("(was ...) in: " + c.strip())
                    c = c2
            why = _clause_is_old(c, refs, ref_grams, ours, row_ours, nbase) if c.strip() else "empty"
            if why and "(" in c:
                c2 = _drop_old_asides(c, refs, ref_grams, ours, row_ours, nbase)
                if c2 is not None:
                    dropped.append("(...) in: " + c.strip())
                    c, why = c2, None
            if why:
                if c.strip():
                    dropped.append(c.strip())
                continue
            out.append([sep, c, i])
        if not out:
            continue
        first = True
        for j, (sep, c, i) in enumerate(out):
            pieces.append([before if first else sep, c.strip() if len(out) == 1 else (c.lstrip() if first else c)])
            first = False
        pieces[-1][1] = pieces[-1][1].rstrip()
        # the last kept sentence gets back the full stop its separator carried when what followed it was dropped
        last = out[-1][2]
        if last + 1 < len(clauses) and clauses[last + 1][0] in (". ", "! ", "? ") and not pieces[-1][1].endswith((".", "!", "?")):
            pieces[-1][1] += clauses[last + 1][0].strip()

    def render(ps):
        return "".join((j if k else "") + t for k, (j, t) in enumerate(ps)).strip()

    new = render(pieces)
    if new and note_problems(new, base_value, ours, row_ours):
        acc = []
        for p in pieces:
            if note_problems(render(acc + [p]), base_value, ours, row_ours):
                dropped.append(p[1].strip())
                continue
            acc.append(p)
        new = render(acc)
    return new, dropped


def comment_is_old(text, ours):
    """A comment row quoting wording that only the OverLlm text has (not this mod's shipped text), or holding a whole OverLlm
    value or 4 words only OverLlm's text has: that wording is OverLlm's."""
    for q in _quotes(text):
        nq = _norm(q).strip(" ,.;:!?")
        if len(nq) >= 4 and _HAS_LETTER.search(nq) and ours.in_base(nq) and not ours.has(nq):
            return q
    hits = scan_text(text, ours.ol, ours)
    if hits:
        return hits[0][1]
    return None


def strings_texts(named_bytes):
    return [t for data in named_bytes for k, t, _, _ in strings_rows(data)[0]]


def _row_ours(text, key=None, ws_tt=None, base_tt=None):
    """This mod's own wording for one strings row: the override text (what the player sees for the key; a table revision of
    the same key is hidden by it), normalised, then NUL, then as it is (whitespace collapsed) for comparing quotes exactly."""
    return _norm(text) + NUL + _exact(text)


def build_strings(ws_files, refs_of, ours):
    """{file name: shipped bytes} for the workspace strings/*.csv ({name: bytes}), and a report of what changed. Only the
    note field of a row changes (its raw span is replaced); a comment row quoting OverLlm-only wording is left out. refs_of(key)
    gives OverLlm's English for the key (BASE_REF's line and every earlier release's: Inputs.row_refs), or pass a dict."""
    if isinstance(refs_of, dict):
        refs_of = (lambda d: (lambda k: _as_refs(d.get(k))))(refs_of)
    files, report = {}, {"notes": 0, "cleaned": 0, "emptied": 0, "dropped_comments": [], "examples": [], "files": 0}
    for name in sorted(ws_files, key=str.lower):
        raw = ws_files[name]
        bom = raw.startswith(UTF8_BOM)
        text = (raw[3:] if bom else raw).decode("utf-8")
        pieces, pos = [], 0
        for r in parse_strings(text):
            f = r["fields"]
            key = f[0][0].strip()
            if key.startswith("#"):
                q = comment_is_old(" ".join(x[0] for x in f), ours)
                if q:
                    pieces.append(text[pos:r["start"]])
                    pos = r["end"]
                    report["dropped_comments"].append((name, " ".join(x[0] for x in f), q))
                continue
            if not key or key == "key" or len(f) < 3:
                continue
            note, s, e = f[2]
            report["notes"] += bool(note)
            new, dropped = clean_note(note, refs_of(key), ours, _row_ours(f[1][0]))
            if new == note:
                continue
            report["cleaned"] += 1
            report["emptied"] += not new
            report["examples"].append((name, key, note, new))
            pieces.append(text[pos:s])
            pieces.append(csv_field(new))
            pos = e
        pieces.append(text[pos:])
        files[name] = (UTF8_BOM if bom else b"") + "".join(pieces).encode("utf-8")
        report["files"] += 1
    return files, report


def strings_rows(data):
    """(key, text, note) of the data rows and the comment texts of a strings file, as StringOverrides reads it."""
    text = (data[3:] if data.startswith(UTF8_BOM) else data).decode("utf-8")
    rows, comments = [], []
    for r in parse_strings(text):
        f = [x[0] for x in r["fields"]]
        k = f[0].strip()
        if k.startswith("#"):
            comments.append(" ".join(f))
        elif k and k != "key" and len(f) >= 2:
            rows.append((k, f[1], f[2] if len(f) > 2 else None, len(f)))
    return rows, comments


def datafix_overrides(ws_files, base_tt, ws_tt, noop_only=False):
    """The cut-over's override fixes: a row whose text is a '[...]' placeholder goes (the table text then shows; its key is
    listed for re-translation); a row whose text is OverLlm's English for its key goes when it changes nothing (our table
    row has that text too) or is longer than 3 words (it would ship OverLlm's sentence and hide our table revision); a short
    label that changes what the player sees stays. noop_only (rebase): only the rows that change nothing go. Returns
    ({name: bytes}, [(file, key, action, why, text)])."""
    out, log = {}, []
    for name in sorted(ws_files, key=str.lower):
        raw = ws_files[name]
        bom = raw.startswith(UTF8_BOM)
        text = (raw[3:] if bom else raw).decode("utf-8")
        pieces, pos = [], 0
        for r in parse_strings(text):
            f = r["fields"]
            key = f[0][0].strip()
            if not key or key.startswith("#") or key == "key" or len(f) < 2:
                continue
            t = f[1][0]
            action = why = None
            if noop_only:
                if base_tt.get(key) == t and ws_tt.get(key) == t:
                    action, why = "delete", "the release's line and our table row are this text now: the override changes nothing"
            elif PLACEHOLDER.search(t):
                action, why = "delete", "placeholder %r in the text (re-translate)" % PLACEHOLDER.search(t).group(0)
            elif base_tt.get(key) == t:
                if ws_tt.get(key) == t:
                    action, why = "delete", "OverLlm's text, and our table row has the same text: the override changes nothing"
                elif len(_words(t)) > 3:
                    action, why = "delete", "OverLlm's sentence (%d words); it hid our table revision" % len(_words(t))
                else:
                    action, why = "keep", "short label (%d words) that changes what the player sees" % len(_words(t))
            if action is None:
                continue
            log.append((name, key, action, why, t))
            if action == "delete":
                pieces.append(text[pos:r["start"]])
                pos = r["end"]
        pieces.append(text[pos:])
        out[name] = (UTF8_BOM if bom else b"") + "".join(pieces).encode("utf-8")
    return out, log


def datafix_scene_runtime_lines(ws_scene, base_scene, inst_scene):
    """The cut-over's scene fix: workspace lines that are XUnity's own runtime (Google) lines, i.e. the line is in the
    installed OverLlm XUnity file byte for byte, its key is not in BASE_REF, and the workspace serves that same value (no
    revision of ours). Returns (new bytes, [(line number, key, value)])."""
    if inst_scene is None:
        return None, []
    inst_lines = set(inst_scene.lines)
    drop = {}
    for k, v, i in ws_scene.entries:
        if k and v and k not in base_scene.map and inst_scene.map.get(k) == v and ws_scene.map.get(k) == v \
                and ws_scene.lines[i] in inst_lines:
            drop[i] = (k, v)
    if not drop:
        return None, []
    lines = [l for i, l in enumerate(ws_scene.lines) if i not in drop]
    return ws_scene.serialize(lines), [(i + 1, k, v) for i, (k, v) in sorted(drop.items())]


# ---- rules/*.json -----------------------------------------------------------------------------------------------------------

_RULE_CLASSES = ("Rule", "RectRule", "TextRule", "TmpRule", "LayoutRule", "GridRule", "FitterRule", "LayoutElementRule",
                 "MovePanelRule", "ImageRule")
_DESCRIPTIVE = {"comment"}          # read by the loader only for a log line: prose, not function
_schema_cache = {}


def rule_schema(src=None):
    """{class: {lower-case field name: (name, nested rule class or None)}}: the fields the plugin's rule loader
    (Newtonsoft ToObject<Rule>, names matched without case) reads, from the plugin source."""
    src = src or P.PLUGIN_SRC
    if src in _schema_cache:
        return _schema_cache[src]
    try:
        text = read_text(src)
    except OSError as e:
        raise PublishError("cannot read the rule loader's field list from %s (%s)" % (src, e))
    schema = {}
    for cls in _RULE_CLASSES:
        m = re.search(r"public\s+class\s+%s\s*\{" % cls, text)
        if not m:
            raise PublishError("%s: class %s not found; update publish.py's rule schema" % (src, cls))
        depth, i = 1, m.end()
        while depth and i < len(text):
            depth += (text[i] == "{") - (text[i] == "}")
            i += 1
        body = text[m.end():i - 1]
        fields = {}
        for fm in re.finditer(r"(\[JsonIgnore\]\s*)?public\s+([A-Za-z_][\w<>\[\],?.]*)\s+([A-Za-z_]\w*)\s*;", body):
            if fm.group(1):
                continue
            typ = fm.group(2).rstrip("?").replace("[]", "")
            fields[fm.group(3).lower()] = (fm.group(3), typ if typ in _RULE_CLASSES else None)
        schema[cls] = fields
    if not {"id", "path", "paths", "rect", "text"} <= set(schema["Rule"]):
        raise PublishError("%s: the Rule class does not read id/path/paths/rect/text; update publish.py's rule schema" % src)
    _schema_cache[src] = schema
    return schema


def _strip_rule(o, cls, schema, where, removed):
    if not isinstance(o, dict):
        return o
    out = {}
    for k, v in o.items():
        f = schema[cls].get(k.lower())
        if f is None or f[0] in _DESCRIPTIVE:
            removed.append(where + "." + k)
            continue
        out[k] = _strip_rule(v, f[1], schema, where + "." + k, removed) if (f[1] and isinstance(v, dict)) else v
    return out


def _json_pairs(pairs):
    seen = set()
    for k, v in pairs:
        if k in seen:
            raise ValueError("duplicate field %r" % k)
        seen.add(k)
    return dict(pairs)


def strip_rules(name, data, schema):
    """The rule file as the plugin reads it, minus comments and fields the loader does not read: (object, report)."""
    try:
        s, ncom = strip_json_comments(_text(data))
        o = json.loads(s, object_pairs_hook=_json_pairs)
    except ValueError as e:
        raise PublishError("workspace rules/%s does not parse as JSON (after its comments are removed): %s" % (name, e))
    removed = []
    if isinstance(o, dict):
        if not isinstance(o.get("rules"), list):
            raise PublishError("workspace rules/%s: neither an array nor an object with a \"rules\" array" % name)
        removed += ["$." + k for k in o if k != "rules"]
        arr, wrap = o["rules"], True
    elif isinstance(o, list):
        arr, wrap = o, False
    else:
        raise PublishError("workspace rules/%s is not an array" % name)
    rules = []
    for i, r in enumerate(arr):
        if not isinstance(r, dict):
            removed.append("$[%d] (not an object: the loader skips it)" % i)
            continue
        rules.append(_strip_rule(r, "Rule", schema, "$[%d]" % i, removed))
    out = {"rules": rules} if wrap else rules
    return out, {"comments": ncom, "removed": removed, "rules": len(rules)}


def rules_bytes(obj):
    return (json.dumps(obj, indent=2, ensure_ascii=False) + "\n").encode("utf-8")


def ship_rules(ws_rules):
    """{name: shipped bytes} for the workspace rules ({name: bytes}), and a report."""
    schema = rule_schema()
    files, rep = {}, {"files": 0, "rules": 0, "comments": 0, "fields_removed": 0, "comment_fields": 0, "other_removed": []}
    for name in sorted(ws_rules, key=str.lower):
        obj, r = strip_rules(name, ws_rules[name], schema)
        files[name] = rules_bytes(obj)
        rep["files"] += 1
        rep["rules"] += r["rules"]
        rep["comments"] += r["comments"]
        rep["fields_removed"] += len(r["removed"])
        rep["comment_fields"] += sum(1 for x in r["removed"] if x.lower().endswith(".comment"))
        rep["other_removed"] += [name + ":" + x for x in r["removed"] if not x.lower().endswith(".comment")]
    return files, rep


# ---- the revision record (StringTable.meta.tsv) and the game's own text -------------------------------------------------

_DUMP_ESC = {BS: BS, "t": TAB, "r": CR, "n": LF}


def _unescape_dump(s):
    out, i, n = [], 0, len(s)
    while i < n:
        c = s[i]
        if c == BS:
            if i + 1 >= n or s[i + 1] not in _DUMP_ESC:
                return None
            out.append(_DUMP_ESC[s[i + 1]])
            i += 2
        else:
            out.append(c)
            i += 1
    return "".join(out)


def parse_game_source(data):
    """A harness 'srcdump' (key TAB value per line, backslash / TAB / CR / LF escaped): ({key: value}, [problems])."""
    try:
        text = _text(data)
    except UnicodeDecodeError as e:
        return {}, ["not UTF-8: %s" % e]
    m, probs = {}, []
    for n, line in enumerate(text.split("\n"), 1):
        if line.endswith("\r"):
            line = line[:-1]
        if not line:
            continue
        parts = line.split(TAB)
        k = _unescape_dump(parts[0]) if len(parts) == 2 else None
        v = _unescape_dump(parts[1]) if len(parts) == 2 else None
        if not k or v is None:
            if len(probs) < 10:
                probs.append("line %d is not 'key TAB value' with \\\\ \\t \\r \\n escapes" % n)
            continue
        if k in m:
            if len(probs) < 10:
                probs.append("line %d repeats key %r" % (n, k[:60]))
            continue
        m[k] = v
    return m, probs


# The revision record. Each revised table row keeps the hashes of the game text and of the OverLlm line it was revised against
# (TextHash of each), from the build that first shipped it in its current wording: the plugin's fallback serves OverLlm's line
# for a row whose game text changed since then when OverLlm's line changed too. A build keeps the record of a row whose value
# is unchanged (workspace/translation/revision_record.tsv holds the hash of the value it was recorded for) and records a new
# or changed row from base_ref/game_source.tsv and BASE_REF (a changed row that was out of date is said and listed; with
# --keep-record it keeps its old record). Only 'publish.py source' changes an existing record of an unchanged row: it fills a
# game-text hash that was never recorded ('-'), and replaces a recorded one only with --restamp; and --keep-record with an
# earlier build's re-record listing brings back the record that build replaced.
REC_VERSION = "v1"
REC_HEAD = "# LOM_UI_EN revision record " + REC_VERSION + " game=%s"
REC_COLS = ("# key<TAB>TextHash(the game's text when the row was revised)<TAB>TextHash(the OverLlm line it was revised against); "
            "'-' = not recorded")
WS_REC_HEAD = ("# LOM_UI_EN workspace revision record " + REC_VERSION + " (maintainer only, never shipped; written by publish.py "
               "build/source/rebase, never edit): key<TAB>TextHash(game text)<TAB>TextHash(OverLlm line)<TAB>TextHash(this mod's "
               "text the record is for)")
_REC_HASH = re.compile(r"^(?:[0-9a-f]{16}|-)$")
_FP = re.compile(r"^[0-9a-f]{12}$")
FINGERPRINT_ASSEMBLIES = ("Mortal.Core", "Mortal.Story", "Mortal.Combat", "Mortal.Free", "Mortal.Battle", "Fungus",
                          "LeanLocalization", "LeanLocalization.TMP", "Unity.TextMeshPro", "DOTween", "Assembly-CSharp")


def game_fingerprint(game=None):
    """Compat.Fingerprint (the build id the plugin shows): the first 6 bytes of SHA-1 over name + '=' + bytes of each of the
    game's code files in Mortal_Data/Managed, in hex; None when the folder is missing."""
    managed = os.path.join(game or P.GAME, "Mortal_Data", "Managed")
    if not os.path.isdir(managed):
        return None
    h = hashlib.sha1()
    for asm in FINGERPRINT_ASSEMBLIES:
        p = os.path.join(managed, asm + ".dll")
        h.update((asm + "=").encode("utf-8"))
        h.update(file_bytes(p) if os.path.exists(p) else b"missing")
    return h.hexdigest()[:12]


_DUMP_TAG = re.compile(r"^[0-9a-f]{12}$")
# The workspace record's optional fifth field (written only for rows that have one, so a record without any stays in the
# four-field form): the dump a build recorded the row against, as the first 12 hex digits of the md5 of base_ref/game_source.tsv
# at that time. 'source' uses it to tell the rows revised (and recorded against the dump it replaces) since that dump was
# installed from the rows revised before it.
WS_REC_TAG_NOTE = ("# optional 5th field: md5 (first 12 hex digits) of the base_ref/game_source.tsv a build recorded the row "
                   "against (publish.py source lists the rows recorded since that dump was installed)")


def dump_tag(data):
    """The fifth-field tag of a game_source.tsv's bytes (None for no dump)."""
    return md5_bytes(data)[:12] if data is not None else None


def parse_records_full(data):
    """workspace/translation/revision_record.tsv: ({key: (game hash, OverLlm hash, own-text hash)}, {key: dump tag} for the
    rows that have one, [problems])."""
    recs, tags, probs = {}, {}, []
    try:
        text = _text(data)
    except UnicodeDecodeError as e:
        return {}, {}, ["not UTF-8: %s" % e]
    for n, line in enumerate(text.split("\n"), 1):
        line = line.rstrip("\r")
        if not line or line.startswith("#"):
            continue
        f = line.split(TAB)
        if (len(f) not in (4, 5) or not f[0] or not all(_REC_HASH.match(x) for x in f[1:4]) or f[3] == "-"
                or (len(f) == 5 and not _DUMP_TAG.match(f[4]))):
            if len(probs) < 10:
                probs.append("line %d is not 'key TAB hash TAB hash TAB hash [TAB dump tag]'" % n)
            continue
        if f[0] in recs:
            if len(probs) < 10:
                probs.append("line %d repeats key %r" % (n, f[0][:60]))
            continue
        recs[f[0]] = (f[1], f[2], f[3])
        if len(f) == 5:
            tags[f[0]] = f[4]
    return recs, tags, probs


def parse_records(data):
    """workspace/translation/revision_record.tsv: ({key: (game hash, OverLlm hash, own-text hash)}, [problems])."""
    recs, tags, probs = parse_records_full(data)
    return recs, probs


KEEP_LISTING_MARK = "# LOM_UI_EN rows re-recorded while out of date"
KEEP_LISTING_COLS = ("key", "recorded_game_text_hash", "game_source_hash", "revised_against_overllm_hash", "overllm_hash_now",
                     "old_text_hash", "new_text_hash", "old_dump_tag")
RERECORD_LISTING = "rerecorded_out_of_date.tsv"


def parse_keep_record(spec):
    """--keep-record KEYS|@FILE|all: (keys: a set, or 'all'; {key: (old game hash, old OverLlm hash, new game hash, new OverLlm
    hash, new own-text hash, old dump tag or None)} from a listing a build wrote, backups/rerecord-<stamp>/ or a source/rebase
    backup's rerecorded_out_of_date.tsv). KEYS = comma-separated; @FILE = one key per line (up to a TAB; '#' lines and a
    'key' header skipped, so source's game_text_changed.tsv works too) or such a listing. None -> (set(), {})."""
    if spec is None:
        return set(), {}
    if spec == "all":
        return "all", {}
    if not spec.startswith("@"):
        return {k.strip() for k in spec.split(",") if k.strip()}, {}
    path = spec[1:]
    if not os.path.isfile(path):
        raise PublishError("--keep-record %s: no such file" % spec)
    with open(path, encoding="utf-8-sig") as f:
        lines = [l.rstrip("\r\n") for l in f]
    keys, listing = set(), {}
    if lines and lines[0].startswith(KEEP_LISTING_MARK):
        head = next((l.split(TAB) for l in lines if l.startswith("key" + TAB)), None)
        if head is None or any(c not in head for c in KEEP_LISTING_COLS):
            raise PublishError("--keep-record %s: a re-record listing without its column header (%s)" % (
                spec, TAB.join(KEEP_LISTING_COLS)))
        ix = {c: head.index(c) for c in KEEP_LISTING_COLS}
        for n, l in enumerate(lines, 1):
            if not l or l.startswith("#") or l.startswith("key" + TAB):
                continue
            f = l.split(TAB)
            try:
                row = [f[ix[c]] for c in KEEP_LISTING_COLS]
            except IndexError:
                raise PublishError("--keep-record %s: line %d has too few fields" % (spec, n))
            if not all(_REC_HASH.match(x) for x in row[1:7]) or not (row[7] == "-" or _DUMP_TAG.match(row[7])):
                raise PublishError("--keep-record %s: line %d is not a listing row" % (spec, n))
            keys.add(row[0])
            listing[row[0]] = (row[1], row[3], row[2], row[4], row[6], None if row[7] == "-" else row[7])
        return keys, listing
    for l in lines:
        k = l.split(TAB)[0].strip()
        if k and not k.startswith("#") and k != "key":
            keys.add(k)
    return keys, listing


def make_records_full(inp, rows, keep=None):
    """The revision record for the shipped rows: (recs, tags, notes).

    recs = [(key, game hash, OverLlm hash, own-text hash, how)], how = 'kept' (the workspace record's: the row's value is the
    one it was recorded for), 'new' (no record yet), 'changed' (the value changed since it was recorded), 'kept_record' (it
    changed, and --keep-record keeps its old game-text and OverLlm hashes) or 'restored' (--keep-record with an earlier
    build's re-record listing: the record that build replaced comes back); new and changed rows are recorded from
    base_ref/game_source.tsv and BASE_REF as they are now. tags = {key: dump tag} (the dump a build recorded a new or changed
    row against; kept, kept_record and restored rows keep theirs). keep = parse_keep_record's pair. notes: 'stale' = [(key,
    old record, old tag, new record)] for the changed rows re-recorded although their old record's game-text hash is not
    game_source.tsv's (the row was out of date and got edited: the new record takes the edit as a revision for today's game
    text, so the plugin stops falling back for it and stops reporting it); 'keep_nothing' = [(key, why)] for --keep-record
    keys that had no old record to keep."""
    keys, listing = keep or (set(), {})
    keep_all = keys == "all"
    tag_now = getattr(inp, "game_src_tag", None)
    old_tags = getattr(inp, "ws_record_tags", {}) or {}
    out, tags, stale, nothing = [], {}, [], []
    shipped = set()
    for k, v in rows:
        if any(c in k for c in (TAB, CR, LF)) or k.startswith("#"):
            raise PublishError("table key %r cannot be written to the revision record" % k)
        shipped.add(k)
        o = text_hash(v)
        r = inp.ws_records.get(k)
        want = keep_all or k in keys
        lst = listing.get(k) if want else None
        if lst is not None and r is not None and r == (lst[2], lst[3], lst[4]):
            out.append((k, lst[0], lst[1], o, "restored"))
            if lst[5]:
                tags[k] = lst[5]
            continue
        if lst is not None:
            nothing.append((k, "the listing's record is not the one the workspace holds now (re-recorded again, restamped or "
                               "restored since?): not restored"))
        if r is not None and r[2] == o:
            out.append((k, r[0], r[1], o, "kept"))
            if k in old_tags:
                tags[k] = old_tags[k]
            continue
        if r is not None and want:
            out.append((k, r[0], r[1], o, "kept_record"))
            if k in old_tags:
                tags[k] = old_tags[k]
            continue
        s = text_hash(inp.game_src[k]) if inp.game_src is not None and k in inp.game_src else "-"
        b = text_hash(inp.base_tt[k]) if k in inp.base_tt else "-"
        out.append((k, s, b, o, "new" if r is None else "changed"))
        if tag_now and s != "-":
            tags[k] = tag_now
        if r is not None and r[0] != "-" and s != "-" and s != r[0]:
            stale.append((k, r, old_tags.get(k), (s, b, o)))
        if want and r is None and not keep_all:
            nothing.append((k, "a new row: it has no record to keep"))
    if keys and not keep_all:
        nothing += [(k, "not a shipped row") for k in sorted(keys - shipped)]
    return out, tags, {"stale": stale, "keep_nothing": nothing}


def make_records(inp, rows):
    """make_records_full's list alone (no --keep-record)."""
    return make_records_full(inp, rows)[0]


def records_bytes(recs, tags=None):
    """The workspace record file for make_records' list (with the dump tags as a fifth field where a row has one)."""
    tags = tags or {}
    body = [TAB.join(r[:4]) + (TAB + tags[r[0]] if r[0] in tags else "") for r in recs]
    head = [WS_REC_HEAD] + ([WS_REC_TAG_NOTE] if any(r[0] in tags for r in recs) else [])
    return ("\n".join(head + body) + "\n").encode("utf-8")


def adopt_records(inp, recs, tags):
    """Make inp's workspace record the planned one (what build saves before it ships), so the checks of the planned and
    written files compare the shipped meta with it."""
    inp.ws_records = {r[0]: (r[1], r[2], r[3]) for r in recs}
    inp.ws_record_tags = dict(tags)


def write_rerecord_listing(path, stale, what):
    """The re-record listing (KEEP_LISTING_COLS) of make_records_full's 'stale' notes; what = who wrote it."""
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("%s (%s): each row's text changed while its game text had changed since it was revised, and it was "
                "re-recorded against today's game text and OverLlm line. To keep their old record (the plugin then keeps "
                "seeing the game update): publish.py build --keep-record @<this file> (all rows) or --keep-record KEYS\n" % (
                    KEEP_LISTING_MARK, what))
        f.write(TAB.join(KEEP_LISTING_COLS) + "\n")
        for k, old, old_tag, new in stale:
            f.write(TAB.join([k, old[0], new[0], old[1], new[1], old[2], new[2], old_tag or "-"]) + "\n")


def stale_note(stale, listing, cmd_prefix):
    """The warning for rows re-recorded while out of date (make_records_full's 'stale')."""
    keys = [x[0] for x in stale]
    return ("WARNING revision record: %d changed rows were out of date (their game text changed since they were revised: the "
            "recorded game-text hash is not base_ref/game_source.tsv's) and are re-recorded against today's game text and "
            "OverLlm line, i.e. the edit counts as a revision for the new game text: the plugin no longer falls back to "
            "OverLlm's newer line for them or reports them as out of date. Listed in %s (e.g. %s). If the edit was not a "
            "revision for the new text (a term or name pass, a typo fix), keep their old record: %s --keep-record %s" % (
                len(keys), listing, ", ".join(keys[:5]) + (" ..." if len(keys) > 5 else ""), cmd_prefix, _q("@" + listing)))


def keep_note(recs, notes):
    """What --keep-record did, in one line (None when it was not given)."""
    c = collections.Counter(r[4] for r in recs)
    nothing = notes["keep_nothing"]
    if not (c["kept_record"] or c["restored"] or nothing):
        return None
    return ("--keep-record: %d changed rows keep their old record (game text and OverLlm line they were revised against), "
            "%d records an earlier build replaced are restored%s" % (
                c["kept_record"], c["restored"], "; %d keys had nothing to keep: %s" % (
                    len(nothing), "; ".join("%s: %s" % (k[:60], w) for k, w in nothing[:5]) + (" ..." if len(nothing) > 5 else ""))
                if nothing else ""))


def tool_cmd(*args):
    """The command line of a publish.py run in this layout (--stage when staged), for the messages."""
    return "python %s%s %s" % (_q(os.path.join(P.TOOLS, "publish.py")), " --stage %s" % _q(P.STAGE) if P.STAGE else "",
                               " ".join(args))


def _q(p):
    return '"%s"' % p if (" " in p or not p) else p


def ledger_cmd(*args):
    """The command line of a ledger.py run in this layout (--stage when staged), for the messages."""
    return "python %s%s %s" % (_q(os.path.join(P.TOOLS, "ledger.py")), " --stage %s" % _q(P.STAGE) if P.STAGE else "",
                               " ".join(args))


def record_game_build(base_info):
    gs = (base_info or {}).get("game_source") or {}
    fp = gs.get("game_build")
    return fp if isinstance(fp, str) and _FP.match(fp) else "unknown"


def make_meta(recs, game_build):
    """The shipped revision record (translation/StringTable.meta.tsv) for make_records' list: (bytes, report)."""
    lines = [REC_HEAD % game_build, REC_COLS] + [TAB.join(r[:3]) for r in recs]
    c = collections.Counter(r[4] for r in recs)
    return ("\r\n".join(lines) + "\r\n").encode("utf-8"), {
        "rows": len(recs), "with_game_text": sum(1 for r in recs if r[1] != "-"), "kept": c["kept"], "new": c["new"],
        "changed": c["changed"], "kept_record": c["kept_record"], "restored": c["restored"], "game_build": game_build}


def parse_meta(data):
    """A shipped StringTable.meta.tsv: ({key: (game hash, OverLlm hash)} in file order, problem or None)."""
    try:
        lines = _text(data).splitlines()
    except UnicodeDecodeError:
        return {}, "not UTF-8"
    if not lines or not lines[0].startswith(REC_HEAD.split("%")[0]):
        return {}, "its first line is not a '%s...' header" % REC_HEAD.split("%")[0]
    out = {}
    for n, l in enumerate(lines, 1):
        if not l or l.startswith("#"):
            continue
        f = l.split(TAB)
        if len(f) != 3 or not f[0] or not all(_REC_HASH.match(x) for x in f[1:]) or f[0] in out:
            return {}, "line %d is not 'key TAB hash TAB hash' (or repeats a key)" % n
        out[f[0]] = (f[1], f[2])
    return out, None


def recover_records(target, rows):
    """For a missing workspace record: what target's shipped files (as publish.py wrote them) still hold of it. rows =
    [(key, workspace value)] of the rows to ship. Returns (records {key: (game hash, OverLlm hash, own-text hash)} for the
    rows whose shipped value is the workspace's, [keys new or changed since that publish: recorded afresh as usual],
    [keys that cannot be recovered], why the shipped files cannot be used (None when they can), the meta path)."""
    tp, mp = rel_path(target, REL_TABLE), rel_path(target, REL_META)
    why, meta, stt = None, {}, {}
    if not (os.path.exists(tp) and os.path.exists(mp)):
        why = "%s holds no shipped %s and %s" % (target, REL_TABLE, REL_META)
    else:
        tb, mb = file_bytes(tp), file_bytes(mp)
        try:
            state = read_state(target)
        except ValueError:
            state = None
        pair = (sha256_bytes(tb), sha256_bytes(mb))
        gens = [((state or {}).get(g) or {}) for g in ("files", "pending")]
        if not state:
            why = "%s has no %s, so its files are not known to be publish.py's" % (target, REL_STATE)
        elif not any(pair == ((g.get(REL_TABLE) or {}).get("sha256"), (g.get(REL_META) or {}).get("sha256")) for g in gens):
            # both files as one publish wrote them (the last one, or an interrupted one): a meta from another publish than
            # the table would give rows the hashes of another value
            why = ("the shipped %s and %s are not the pair one publish.py build wrote (edited since, or a publish was "
                   "interrupted between them)" % (REL_TABLE, REL_META))
        else:
            meta, p = parse_meta(mb)
            try:
                stt = table_map(_text(tb))[0]
            except UnicodeDecodeError:
                p = p or "the shipped table is not UTF-8"
            if p:
                why = "the shipped %s: %s" % (REL_META, p)
            elif list(meta) != list(stt):
                why = "the shipped %s does not list the shipped table's rows in order" % REL_META
    rec, fresh, lost = {}, [], []
    for k, v in rows:
        if why is not None:
            lost.append(k)
        elif k in stt and stt[k] == v:
            rec[k] = meta[k] + (text_hash(v),)
        else:
            fresh.append(k)
    return rec, fresh, lost, why, mp


def missing_record_help(lost, why):
    return ("%d shipped rows cannot be recovered (%s). Restore %s from a backup (backups/source-<stamp>/revision_record.tsv "
            "or backups/rebase-<stamp>/workspace/translation/revision_record.tsv, the newest one), or rerun with --rerecord, "
            "which records those rows afresh against today's game text (base_ref/game_source.tsv) and OverLlm line: the "
            "plugin's fallback then no longer sees a game update made since they were revised" % (len(lost), why, P.WS_RECORD))


def ensure_records(inp, target, rerecord=False):
    """When the workspace record is missing: recover it into inp.ws_records from target's shipped meta and table (the rows
    whose shipped value is the workspace's keep what they were revised against); refuse (PublishError) when any shipped row
    cannot be recovered, unless rerecord (those rows are then recorded afresh by make_records). Returns a report dict (its
    note, recovery_note(rep), is for the caller to print once the record is saved), or None when the record exists."""
    if inp.ws_records_bytes is not None:
        return None
    rows = ship_rows(inp)
    rec, fresh, lost, why, mp = recover_records(target, rows)
    if lost and not rerecord:
        raise PublishError("the workspace revision record %s is missing; nothing was written. %s" % (
            P.WS_RECORD, missing_record_help(lost, why)))
    inp.ws_records = rec
    inp.ws_record_tags = {}
    return {"recovered": len(rec), "fresh": len(fresh), "rerecorded": len(lost), "from": mp, "why": why}


def recovery_note(rep, saved=True):
    """What ensure_records did, in words: saved = the record is written (a refused or dry run prints nothing, or 'would')."""
    if not rep:
        return None
    now = "are recorded now" if saved else "would be recorded (nothing saved)"
    if rep["recovered"] or not rep["rerecorded"]:
        return ("revision record: %s was missing; %s %d rows from %s (their shipped value is the workspace's)%s%s; the "
                "record's fifth fields (the dump a build recorded each row against) are not in the shipped files, so the "
                "next source lists rows revised before it among the others" % (
                    P.WS_RECORD, "recovered" if saved else "would recover", rep["recovered"], rep["from"],
                    "; %d rows new or changed since that publish %s" % (rep["fresh"], now) if rep["fresh"] else "",
                    "; %d rows could not be recovered (%s) and %s afresh (--rerecord)" % (
                        rep["rerecorded"], rep["why"], "are recorded" if saved else "would be recorded") if rep["rerecorded"]
                    else ""))
    return ("revision record: %s does not exist and nothing can give it back (%s): all %d shipped rows %s from "
            "base_ref/game_source.tsv and BASE_REF (the first record, or --rerecord)" % (P.WS_RECORD, rep["why"],
                                                                                        rep["rerecorded"], now))


# The plugin's fallback (TextTable.Decide / RecordsUsable), as the tools predict it for a game text and OverLlm's line
_PH = re.compile(r"\{(\d+)(?::[^{}]*)?\}|\{\$([A-Za-z0-9_]+)\}")


def placeholders_fit(line, source):
    """TextTable.PlaceholdersFit: every {0}-style index and {$var} of line is one source also has."""
    if not line or "{" not in line:
        return True
    tok = lambda m: ("#" + m.group(1)) if m.group(1) is not None else ("$" + m.group(2))
    have = {tok(m) for m in _PH.finditer(source or "")}
    return all(tok(m) in have for m in _PH.finditer(line))


def record_trust(recs, game_src):
    """TextTable.RecordsUsable on the shipped record recs (make_records' list, in meta order) with game_src as the game's
    text: (differing, readable, trusted: True / False / None = too few rows readable). The plugin checks an even sample of
    the rows with a game-text hash and distrusts the whole record when 70% or more of the readable ones differ."""
    hashed = [r for r in recs if r[1] != "-"]
    if not hashed or game_src is None:
        return 0, 0, (False if not hashed else None)
    step = max(1, len(hashed) // 400)
    readable = differ = 0
    for i in range(0, len(hashed), step):
        k, s = hashed[i][0], hashed[i][1]
        if k in game_src:
            readable += 1
            differ += text_hash(game_src[k]) != s
    needed = min(50, max(1, ((len(hashed) + step - 1) // step) // 2))
    if readable < needed:
        return differ, readable, None
    return differ, readable, differ * 10 < readable * 7


def fallback_view(inp, recs, game_src):
    """What the plugin serves for each shipped row when the game's text is game_src and the installed OverLlm line is
    BASE_REF's (TextTable.Decide): {"overllm": keys whose game text changed since they were revised and OverLlm has a newer
    line (the plugin serves OverLlm's, while the record is trusted), "stale": game text changed, OverLlm's line is the one
    revised against (ours kept, needs revising), "no_line": game text changed, OverLlm has no line for the key (ours kept,
    needs revising), "placeholders": our line uses a placeholder the game's text no longer has and OverLlm's fits (the
    plugin serves OverLlm's whatever the record says), "missing": the game text lacks the key}."""
    out = {"overllm": [], "stale": [], "no_line": [], "placeholders": [], "missing": []}
    if game_src is None:
        return out
    for k, s, b, o, how in recs:
        if k not in game_src:
            out["missing"].append(k)
            continue
        src, ours, ol = game_src[k], inp.ws_tt.get(k), inp.base_tt.get(k)
        if ol is not None and ol != ours and not placeholders_fit(ours, src) and placeholders_fit(ol, src):
            out["placeholders"].append(k)
            continue
        if s == "-" or text_hash(src) == s:
            continue
        if ol is None or ol == ours:
            out["no_line"].append(k)
        elif b == "-" or text_hash(ol) != b:
            out["overllm"].append(k)
        else:
            out["stale"].append(k)
    return out


def trust_note(trust):
    differ, readable, ok = trust
    if ok is False and not readable:
        return "no shipped row has a game-text hash yet, so the plugin's 'game text changed' check is off for every row"
    if ok is None:
        return "the plugin's record check cannot run yet (%d rows readable)" % readable
    pct = 100.0 * differ / readable if readable else 0.0
    if ok:
        return ("the plugin trusts the record: %d of %d sampled rows differ (%.0f%%; it distrusts the whole record at 70%%)" % (
            differ, readable, pct))
    return ("WARNING: the plugin will NOT trust the record: %d of %d sampled rows differ (%.0f%%, 70%% or more), so it takes it "
            "for another game build's or language's and falls back for none of these rows (only its placeholder check runs). "
            "Re-revise rows, or re-record the ones that still fit with --restamp, until under 70%% differ" % (differ, readable, pct))


# ---- inputs --------------------------------------------------------------------------------------------------------------

def _read_dir(d, pred):
    if not os.path.isdir(d):
        return {}
    out = {}
    for f in sorted(os.listdir(d), key=str.lower):
        p = os.path.join(d, f)
        if os.path.isdir(p):
            raise PublishError("%s holds a folder (%s); publish.py reads only the files directly in it" % (d, f))
        if pred(f):
            out[f] = file_bytes(p)
    return out


def _is_resizer(f):
    return f.lower().endswith("resizer.txt")


def lock_data(table_bytes, scene_bytes):
    return {"format": LOCK_FORMAT, "files": {"StringTable.csv": md5_bytes(table_bytes),
                                             "_AutoGeneratedTranslations.txt": md5_bytes(scene_bytes)}}


def write_lock(path, data):
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=1, sort_keys=True)
        f.write("\n")
    os.replace(tmp, path)


def read_disk_inputs():
    """The workspace and BASE_REF as bytes (Inputs(mem=...) takes the same dict)."""
    for p, what in ((P.TABLE, "workspace table"), (P.SCENE, "workspace scene file"), (P.STRINGS_DIR, "workspace strings"),
                    (P.WS_RULES_DIR, "workspace rules"), (P.BASE_REF_TABLE, "BASE_REF table"),
                    (P.BASE_REF_SCENE, "BASE_REF scene file"), (P.WS_SENTINELS, "workspace sentinels"),
                    (P.WS_LOCK, "workspace base_ref.lock")):
        if not os.path.exists(p):
            raise PublishError("%s is missing (%s); the workspace and BASE_REF are created by cutover_0_5.py and change together "
                               "(publish.py rebase); restore both from the same backup" % (p, what))
    mem = {"base_table": file_bytes(P.BASE_REF_TABLE), "base_scene": file_bytes(P.BASE_REF_SCENE)}
    with open(P.WS_LOCK, encoding="utf-8") as f:
        lock = json.load(f)
    want = lock_data(mem["base_table"], mem["base_scene"])
    if lock.get("files") != want["files"]:
        raise PublishError("BASE_REF (%s) is not the OverLlm release this workspace is aligned with (%s says %s, BASE_REF holds "
                           "%s): a rebase was interrupted, or the workspace or BASE_REF was restored on its own. Restore BOTH "
                           "from the same backup (backups/rebase-<stamp>/workspace and /base_ref), or redo the rebase" % (
                               P.BASE_REF, P.WS_LOCK, lock.get("files"), want["files"]))
    mem["table"] = file_bytes(P.TABLE)
    mem["scene"] = file_bytes(P.SCENE)
    mem["strings"] = _read_dir(P.STRINGS_DIR, lambda f: f.lower().endswith(".csv"))
    mem["rules"] = _read_dir(P.WS_RULES_DIR, lambda f: f.lower().endswith(".json"))
    mem["resize"] = _read_dir(P.WS_RESIZE_DIR, _is_resizer)
    with open(P.WS_SENTINELS, encoding="utf-8") as f:
        mem["sentinels"] = json.load(f)
    mem["base_info"] = {}
    if os.path.exists(P.BASE_REF_INFO):
        with open(P.BASE_REF_INFO, encoding="utf-8") as f:
            mem["base_info"] = json.load(f)
    mem["base_resize"] = _read_dir(P.BASE_REF_RESIZE_DIR, _is_resizer)
    mem["game_source"] = file_bytes(P.BASE_REF_SOURCE) if os.path.exists(P.BASE_REF_SOURCE) else None
    mem["records"] = file_bytes(P.WS_RECORD) if os.path.exists(P.WS_RECORD) else None
    mem["ledger"] = file_bytes(P.WS_LEDGER) if os.path.exists(P.WS_LEDGER) else None
    mem["ledger_scene"] = file_bytes(P.WS_SCENE_LEDGER) if os.path.exists(P.WS_SCENE_LEDGER) else None
    mem["history"] = read_history(P.BASE_REF_HISTORY)
    return mem


def read_history(hdir):
    """[(name, StringTable.csv bytes, _AutoGeneratedTranslations.txt bytes)] of the earlier releases kept under
    base_ref/history (oldest first)."""
    out = []
    lh = long_path(hdir)                # by long paths: history/NN-<stamp>/ files can pass 260 characters in a stage
    if not os.path.isdir(lh):
        return out
    for name in sorted(os.listdir(lh)):
        d = os.path.join(lh, name)
        t, s = os.path.join(d, "StringTable.csv"), os.path.join(d, "_AutoGeneratedTranslations.txt")
        if not os.path.isdir(d):
            continue
        if not (os.path.exists(t) and os.path.exists(s)):
            raise PublishError("%s holds neither a complete earlier release (StringTable.csv and _AutoGeneratedTranslations.txt)" % (
                os.path.join(hdir, name)))
        out.append((name, file_bytes(t), file_bytes(s)))
    return out


class Inputs:
    """The workspace and BASE_REF, parsed once (from disk, or from a dict of bytes as read_disk_inputs returns), plus the
    installed OverLlm files (read only) and the OverLlm text corpus the checks compare with."""

    def __init__(self, mem=None):
        if mem is None:
            mem = read_disk_inputs()
        self.mem = mem
        self.ws_table_bytes = mem["table"]
        self.ws_tt, self.ws_tt_stats = table_map(_text(mem["table"]))
        self.base_table_bytes = mem["base_table"]
        self.base_tt, self.base_tt_stats = table_map(_text(mem["base_table"]))
        self.ws_scene = SceneFile(data=mem["scene"], label="workspace scene file")
        self.base_scene = SceneFile(data=mem["base_scene"], label="BASE_REF scene file")
        self.ws_strings = mem.get("strings", {})
        self.ws_rules = mem.get("rules", {})
        self.ws_resize = mem.get("resize", {})
        self.sentinels = mem.get("sentinels") or {"table": [], "scene": []}
        self.base_info = mem.get("base_info") or {}
        self.base_resize = mem.get("base_resize") or {}
        self.game_src, self.game_src_problems = None, []
        if mem.get("game_source") is not None:
            self.game_src, self.game_src_problems = parse_game_source(mem["game_source"])
        self.game_src_tag = dump_tag(mem.get("game_source"))
        self.ws_records_bytes = mem.get("records")
        self.ws_records, self.ws_record_tags = {}, {}
        if self.ws_records_bytes is not None:
            self.ws_records, self.ws_record_tags, rp = parse_records_full(self.ws_records_bytes)
            if rp:
                raise PublishError("the workspace revision record (%s) is damaged: %s. Restore it from the latest backup "
                                   "(backups/rebase-<stamp>/workspace or a copy of the workspace); do not edit it by hand" % (
                                       P.WS_RECORD, "; ".join(rp[:3])))
        # earlier OverLlm releases the workspace was aligned with (rebase keeps them): their wording is OverLlm's too
        self.history = []
        for name, tb, sb in mem.get("history") or []:
            self.history.append((name, table_map(_text(tb))[0], SceneFile(data=sb, label="base_ref/history/" + name)))
        # the OverLlm patch's installed files, when present (read only): its StringTable.csv, or since its llmkit-upgrade the
        # per-file tables in the same folder (lashtables; an older StringTable.csv next to them is left out, as the plugin does)
        self.inst_tt = self.inst_scene = None
        self.inst_layout = LT.find(os.path.dirname(P.BASE_TABLE))
        if self.inst_layout.kind == "per-file":
            self.inst_tt = LT.read_per_file(self.inst_layout.files, crlf=True)[0]
        elif os.path.exists(P.BASE_TABLE):
            b = file_bytes(P.BASE_TABLE)
            self.inst_tt = self.base_tt if md5_bytes(b) == md5_bytes(self.base_table_bytes) else table_map(_text(b))[0]
        if os.path.exists(P.BASE_SCENE):
            b = file_bytes(P.BASE_SCENE)
            self.inst_scene = self.base_scene if md5_bytes(b) == self.base_scene.md5 else SceneFile(data=b, label=P.BASE_SCENE)
        self.inst_resize = _read_dir(P.BASE_RESIZE_DIR, _is_resizer) if os.path.isdir(P.BASE_RESIZE_DIR) else {}
        vals = list(self.base_tt.values()) + list(self.base_scene.map.values())
        if self.inst_tt is not None and self.inst_tt is not self.base_tt:
            vals += list(self.inst_tt.values())
        if self.inst_scene is not None and self.inst_scene is not self.base_scene:
            vals += list(self.inst_scene.map.values())
        for name, htt, hsf in self.history:
            vals += list(htt.values()) + list(hsf.map.values())
        self.ol = OverLlm(vals)
        self.base_norm = self.ol.text
        missing = [k for k in self.base_tt if k not in self.ws_tt]
        if missing:
            raise PublishError("%d BASE_REF table keys are missing from the workspace table (e.g. %s); overlay-1 cannot remove "
                               "table rows" % (len(missing), missing[:5]))
        self._read_ledger(mem)

    def _read_ledger(self, mem):
        """The review ledger (ledger.py): led_table {key: (pass, date)}; led_scene_raw {raw key: (pass, date)} of the scene
        lines and led_resize {file name: (pass, date)} of the resizers; led_scene_keys, the decoded keys of the listed scene
        lines (a raw key names each loadable workspace line with English written with it); and the listed keys the workspace
        does not have (led_table_missing, led_scene_missing, led_resize_missing: check fails on them)."""
        self.led_table, self.led_scene_raw, self.led_resize = {}, {}, {}
        for name, key, what in ((P.WS_LEDGER, "ledger", "table"), (P.WS_SCENE_LEDGER, "ledger_scene", "scene")):
            entries, probs = LEDGER.parse(mem.get(key))
            if probs:
                raise PublishError("the review ledger (%s) is damaged: %s. It is written by ledger.py only (%s names the "
                                   "problem); restore it from a backup, do not edit it by hand" % (
                                       name, "; ".join(probs[:3]), ledger_cmd("show")))
            if what == "table":
                self.led_table = dict(entries)
                continue
            for k, v in entries.items():
                if LEDGER.is_resize_entry(k):
                    self.led_resize[k[len(LEDGER.RESIZE_PREFIX):]] = v
                else:
                    self.led_scene_raw[k] = v
        ws = self.ws_scene
        raw_of = {}
        if self.led_scene_raw:
            for k, v, i in ws.entries:
                if k and v and not k.startswith(("r:", "sr:")):
                    raw_of.setdefault(raw_key(ws.lines[i]), k)
        self.led_scene_keys = {raw_of[r] for r in self.led_scene_raw if r in raw_of}
        self.led_scene_missing = sorted(r for r in self.led_scene_raw if r not in raw_of)
        # a listed line must be the one its key loads from: a later workspace line for the same key written with another
        # raw key wins in the load (and is what ships), so the listed one would not be what players see
        self.led_scene_shadowed = sorted(r for r in self.led_scene_raw if r in raw_of and raw_of[r] in ws.last
                                         and raw_key(ws.lines[ws.last[raw_of[r]]]) != r)
        self.led_table_missing = sorted(k for k in self.led_table if k not in self.ws_tt)
        self.led_resize_missing = sorted(n for n in self.led_resize if n not in self.ws_resize)

    def row_refs(self, key):
        """OverLlm's English for a table key: BASE_REF's line, then each different line an earlier release had (newest first)."""
        refs = []
        for v in [self.base_tt.get(key)] + [htt.get(key) for name, htt, hsf in reversed(self.history)]:
            if v and v not in refs:
                refs.append(v)
        return refs

    def overllm_resizers(self):
        """{md5: name} and the set of non-blank lines of OverLlm's resizers (installed and BASE_REF copies)."""
        md5s, lines = {}, set()
        for src in (self.inst_resize, self.base_resize):
            for name, data in src.items():
                md5s[md5_bytes(data)] = name
                for l in _text(data).splitlines():
                    if l.strip() and not l.strip().startswith(("//", "#")):
                        lines.add(l.strip())
        return md5s, lines


# ---- the plan: every shipped file's bytes --------------------------------------------------------------------------------

def make_plan(inp, keep=None):
    """Every shipped file's bytes: (plan, report). keep = parse_keep_record's pair (--keep-record)."""
    plan, rep = {}, {}
    rows = ship_rows(inp)
    table_text = table_serialize(rows)
    plan[REL_TABLE] = ((UTF8_BOM if TABLE_BOM else b"") + table_text.encode("utf-8"))
    led_t = getattr(inp, "led_table", None) or {}
    rep["table_rows"] = len(rows)
    rep["table_same"] = sum(1 for k, v in rows if inp.base_tt.get(k) == v)
    rep["table_revised"] = sum(1 for k, v in rows if k in inp.base_tt and inp.base_tt[k] != v)
    rep["table_added"] = rep["table_rows"] - rep["table_revised"] - rep["table_same"]
    rep["table_reviewed"] = sum(1 for k, _ in rows if k in led_t)
    recs, tags, notes = make_records_full(inp, rows, keep)
    plan[REL_META], rep["meta"] = make_meta(recs, record_game_build(inp.base_info))
    rep["ws_records_bytes"] = records_bytes(recs, tags)
    rep["records"], rep["record_tags"], rep["record_notes"] = recs, tags, notes

    led_s = getattr(inp, "led_scene_keys", None) or set()
    idx, tombs, extra = scene_delta(inp.ws_scene, inp.base_scene, led_s)
    lines = [inp.ws_scene.lines[i] for i in idx] + tombs + extra
    plan[REL_SCENE] = inp.ws_scene.serialize(lines)
    shipped = [decode(inp.ws_scene.lines[i]) for i in idx]
    rep["scene_lines"] = len(idx)
    rep["scene_same"] = sum(1 for k, v in shipped if inp.base_scene.map.get(k) == v)
    rep["scene_revised"] = sum(1 for k, v in shipped if k in inp.base_scene.map and inp.base_scene.map[k] != v)
    rep["scene_added"] = rep["scene_lines"] - rep["scene_revised"] - rep["scene_same"]
    rep["scene_reviewed"] = sum(1 for k, v in shipped if k in led_s)
    rep["scene_tombstones"] = len(tombs)
    rep["scene_variant_lines"] = len(extra)

    led_r = getattr(inp, "led_resize", None) or {}
    for name, data in inp.ws_resize.items():
        plan[REL_RESIZE + "/" + name] = data
    rep["resizers"] = sorted(inp.ws_resize)
    rep["resizers_reviewed"] = sorted(n for n in inp.ws_resize if n in led_r)
    ol_md5s = inp.overllm_resizers()[0]
    rep["resizers_same"] = sorted(n for n in rep["resizers_reviewed"] if md5_bytes(inp.ws_resize[n]) in ol_md5s)

    # this mod's own wording, as the note cleaner and the text scan know it: the revised and added text only (a reviewed
    # line that is OverLlm's own line stays OverLlm's wording there, so both stay exactly as strict as without the ledger)
    ours = Ours([v for k, v in rows if inp.base_tt.get(k) != v] + [v for k, v in shipped if inp.base_scene.map.get(k) != v]
                + [decode(l)[1] for l in extra] + strings_texts(inp.ws_strings.values()), inp.ol)
    strings, srep = build_strings(inp.ws_strings, inp.row_refs, ours)
    for name, data in strings.items():
        plan["strings/" + name] = data
    rep["strings"] = srep

    rules, rrep = ship_rules(inp.ws_rules)
    for name, data in rules.items():
        plan["rules/" + name] = data
    rep["rules"] = rrep

    if os.path.exists(P.NOTICES_SRC):
        plan[REL_NOTICES] = file_bytes(P.NOTICES_SRC)
    if os.path.exists(P.WS_NAMETIPS):
        plan[REL_NAMETIPS] = file_bytes(P.WS_NAMETIPS)
    if os.path.exists(P.WS_FACTIONTIPS):
        plan[REL_FACTIONTIPS] = file_bytes(P.WS_FACTIONTIPS)

    manifest, mrep = make_manifest(inp, plan, rep)
    plan[REL_MANIFEST] = (json.dumps(manifest, indent=1, ensure_ascii=False) + "\n").encode("utf-8")
    rep["sentinels"] = mrep
    return plan, rep


def sentinel_values(inp, layer):
    if layer == "table":
        return inp.base_tt, inp.ws_tt
    return inp.base_scene.map, inp.ws_scene.map


def make_manifest(inp, plan, rep):
    mrep = {}
    out = {}
    for layer in ("table", "scene"):
        base, ws = sentinel_values(inp, layer)
        keep, dropped = [], []
        for s in inp.sentinels.get(layer, []):
            k = s["key"]
            b, w = base.get(k), ws.get(k)
            if b is not None and text_hash(b) == s["revised_sha1"]:
                raise PublishError("BASE_REF's %s text for sentinel %r is this mod's own pre-0.4 edit: BASE_REF is not the OverLlm "
                                   "release (an install edited in place?)" % (layer, k))
            if b is None or w is None or w == b:
                dropped.append(k)
                continue
            keep.append({"key": k, "original_sha1": text_hash(b), "revised_sha1": s["revised_sha1"]})
        out[layer] = keep
        mrep[layer] = {"kept": len(keep), "dropped": dropped}
    manifest = {
        "format": FORMAT,
        "note": manifest_note(uses_ledger(inp)),
        "base_ref": {
            "release": inp.base_info.get("release", ""),
            "StringTable.csv": {"md5": md5_bytes(inp.base_table_bytes), "size": len(inp.base_table_bytes), "keys": len(inp.base_tt)},
            "_AutoGeneratedTranslations.txt": {"md5": inp.base_scene.md5, "size": inp.base_scene.size, "keys": len(inp.base_scene.map)},
        },
        "workspace": {
            "StringTable.csv": {"md5": md5_bytes(inp.ws_table_bytes), "keys": len(inp.ws_tt)},
            "scene_text.txt": {"md5": inp.ws_scene.md5, "keys": len(inp.ws_scene.map)},
        },
        "counts": {
            "table_rows_shipped": rep["table_rows"], "table_rows_revised": rep["table_revised"], "table_rows_added": rep["table_added"],
            "table_rows_with_game_text_hash": rep["meta"]["with_game_text"],
            "scene_lines_shipped": rep["scene_lines"], "scene_lines_revised": rep["scene_revised"], "scene_lines_added": rep["scene_added"],
            "scene_tombstones": rep["scene_tombstones"], "scene_variant_lines": rep["scene_variant_lines"],
        },
        "outputs": {"StringTable.csv": md5_bytes(plan[REL_TABLE]), "StringTable.meta.tsv": md5_bytes(plan[REL_META]),
                    "scene/scene_text.txt": md5_bytes(plan[REL_SCENE])},
        "table_sentinels": out["table"],
        "scene_sentinels": out["scene"],
    }
    if uses_ledger(inp):
        # the review ledger's counts (only when the workspace has one, so a workspace without it publishes the same bytes)
        manifest["counts"].update({
            "reviewed_table": rep["table_reviewed"], "reviewed_scene": rep["scene_reviewed"],
            "reviewed_same_as_overllm": rep["table_same"] + rep["scene_same"],
            "reviewed_same_as_overllm_table": rep["table_same"], "reviewed_same_as_overllm_scene": rep["scene_same"],
            "reviewed_resizers": len(rep["resizers_reviewed"]),
            "reviewed_resizers_same_as_overllm": len(rep["resizers_same"])})
        # what this mod ships against the full working copy: the plugin (TranslationProfiles.OwnTableComplete) plays a layer
        # in English without the OverLlm patch, and says so, when its share is at least 98%
        manifest["standalone"] = {
            "table_rows": sum(1 for k, v in ship_rows(inp) if v), "table_total": sum(1 for v in inp.ws_tt.values() if v),
            "scene_lines": rep["scene_lines"], "scene_total": len(inp.ws_scene.map)}
    return manifest, mrep


def uses_ledger(inp):
    """The workspace has a review ledger with at least one entry (table, scene line or resizer)."""
    return bool(getattr(inp, "led_table", None) or getattr(inp, "led_scene_raw", None) or getattr(inp, "led_resize", None))


def manifest_note(ledger):
    if not ledger:
        return ("LOM_UI_EN overlay: this folder holds only this mod's own work. translation/StringTable.csv has the rows whose "
                "English differs from the OverLlm English patch release named under base_ref, or that the release lacks; "
                "translation/StringTable.meta.tsv records per row the hashes of the game text and of the release line it was "
                "revised against; translation/scene/scene_text.txt has the scene lines whose English differs or that the "
                "release lacks, plus one 'key=' line (empty value) per release line this mod removes. The plugin lays both "
                "over the OverLlm patch's own installed files. Sentinels keep only hashes (first 16 hex digits of SHA-1 over "
                "the UTF-8 text) of the release's English (original_sha1) and of this mod's pre-0.4 edits of the patch's "
                "files (revised_sha1), for the check of what the installed patch's files hold.")
    return ("LOM_UI_EN overlay: this folder holds only this mod's own work. translation/StringTable.csv has the rows whose "
            "English differs from the OverLlm English patch release named under base_ref, or that the release lacks, and the "
            "rows this mod's line-by-line review kept as they are (counts: reviewed_*); translation/StringTable.meta.tsv "
            "records per row the hashes of the game text and of the release line it was revised against; "
            "translation/scene/scene_text.txt has the scene lines whose English differs, that the release lacks or that the "
            "review kept, plus one 'key=' line (empty value) per release line this mod removes; translation/scene/resize "
            "holds this mod's own resizers (reviewed copies included). The plugin lays them over the OverLlm patch's own "
            "installed files, or serves them alone when that patch is not installed. Sentinels keep only hashes (first 16 hex "
            "digits of SHA-1 over the UTF-8 text) of the release's English (original_sha1) and of this mod's pre-0.4 edits of "
            "the patch's files (revised_sha1), for the check of what the installed patch's files hold.")


# ---- verification (shared by build and check) ----------------------------------------------------------------------------

def _verify_table(inp, files, probs, info):
    if REL_TABLE not in files:
        probs.append("%s is missing" % REL_TABLE)
        return {}
    data = files[REL_TABLE]
    tt, st = table_map(_text(data))
    led = getattr(inp, "led_table", None) or {}
    n_same = sum(1 for k, v in tt.items() if inp.base_tt.get(k) == v)
    info["table"] = "%d rows shipped (%d revised, %d added%s)" % (
        len(tt), sum(1 for k, v in tt.items() if k in inp.base_tt and inp.base_tt[k] != v),
        sum(1 for k in tt if k not in inp.base_tt), ", %d reviewed, same as OverLlm" % n_same if n_same else "")
    for what in ("duplicates", "rejoined", "skipped", "short"):
        if st[what]:
            probs.append("table: %d %s rows" % (st[what], what))
    # a row equal to OverLlm's ships only as a reviewed line (the review ledger lists it)
    same = [k for k, v in tt.items() if inp.base_tt.get(k) == v and k not in led]
    if same:
        probs.append("table: %d shipped rows equal the OverLlm release's row and are not in the review ledger (e.g. %s)" % (
            len(same), same[:3]))
    if inp.inst_tt is None:
        info["table vs the installed OverLlm table"] = "not installed (%s); compared with BASE_REF only" % P.BASE_TABLE
    elif inp.inst_layout.kind == "per-file":
        info["table vs the installed OverLlm table"] = "its %d per-file tables (%s)%s" % (
            len(inp.inst_layout.files), inp.inst_layout.dir,
            "; an older StringTable.csv next to them is left out" if inp.inst_layout.leftover else "")
    if inp.inst_tt is not None and inp.inst_tt is not inp.base_tt:
        same = [k for k, v in tt.items() if inp.inst_tt.get(k) == v and k not in led]
        if same:
            probs.append("table: %d shipped rows equal the installed OverLlm table's row and are not in the review ledger "
                         "(e.g. %s)" % (len(same), same[:3]))
    over = dict(inp.base_tt)
    over.update(tt)
    diff = [k for k in set(over) | set(inp.ws_tt) if over.get(k) != inp.ws_tt.get(k)]
    info["table overlay == workspace"] = "yes (%d keys)" % len(over) if not diff else "NO"
    if diff:
        probs.append("table: BASE_REF + shipped rows differs from the workspace in %d keys (e.g. %s)" % (len(diff), sorted(diff)[:3]))
    rt = table_serialize(list(tt.items())).encode("utf-8")
    info["table round trip"] = "byte-identical" if (UTF8_BOM if TABLE_BOM else b"") + rt == data else "values identical, bytes differ"
    return tt


def _verify_meta(inp, files, tt, probs, info):
    if REL_META not in files:
        probs.append("%s is missing (the revision record the fallback mode reads)" % REL_META)
        return
    try:
        lines = _text(files[REL_META]).splitlines()
    except UnicodeDecodeError:
        probs.append("%s is not UTF-8" % REL_META)
        return
    game = record_game_build(inp.base_info)
    if not lines or lines[0] != REC_HEAD % game:
        probs.append("%s: the first line is not %r" % (REL_META, REC_HEAD % game))
    want = {r[0]: r for r in make_records(inp, list(tt.items()))}
    keys, bad, with_src = [], [], 0
    for l in lines:
        if not l or l.startswith("#"):
            continue
        f = l.split(TAB)
        if len(f) != 3 or not all(_REC_HASH.match(x) for x in f[1:]):
            bad.append(l[:40])
            continue
        k, s, b = f
        keys.append(k)
        w = want.get(k)
        if w is None or (s, b) != (w[1], w[2]):
            bad.append(k)
        with_src += s != "-"
    if keys != list(tt):
        probs.append("%s: its keys are not the shipped table's rows in order (%d lines, %d rows)" % (REL_META, len(keys), len(tt)))
    if bad:
        probs.append("%s: %d lines are not the rows' revision records (wrong hashes or form, e.g. %s)" % (REL_META, len(bad), bad[:3]))
    c = collections.Counter(r[4] for r in want.values())
    # kept records older than today's inputs: the rows the fallback watches (the game text or OverLlm's line changed since)
    recs = [want[k] for k in tt if k in want]
    older_b = sum(1 for k, s, b, o, how in recs if how == "kept" and b != (text_hash(inp.base_tt[k]) if k in inp.base_tt else "-"))
    fv = fallback_view(inp, recs, inp.game_src)
    trust = record_trust(recs, inp.game_src)
    head = ("%d rows (%d kept from when they were revised, %d recorded now), %d with the game's text hash, game build %s; "
            "%d revised against an earlier OverLlm line; %d whose game text (base_ref/game_source.tsv) changed since they were "
            "revised: %d with a newer OverLlm line (the plugin serves OverLlm's line for them while the revision record is "
            "trusted, under 70%% of sampled rows changed), %d with none (this mod's text kept: needs revising); %d whose "
            "placeholders the game text no longer has (the plugin serves OverLlm's line); %s" % (
                len(keys), c["kept"], c["new"] + c["changed"], with_src, game, older_b,
                len(fv["overllm"]) + len(fv["stale"]) + len(fv["no_line"]), len(fv["overllm"]),
                len(fv["stale"]) + len(fv["no_line"]), len(fv["placeholders"]),
                trust_note(trust) if inp.game_src is not None else "the plugin's record check needs the game text"))
    if inp.game_src is None:
        info["revision record"] = (head + "; WARNING: base_ref/game_source.tsv is missing, so new records get no game-text hash "
                                   "and the fallback's 'game text changed' check stays off for them (harness 'srcdump', then "
                                   "publish.py source FILE)")
    else:
        info["revision record"] = head + " (base_ref/game_source.tsv: %d keys)" % len(inp.game_src)
        if inp.game_src_problems:
            probs.append("base_ref/game_source.tsv: %s" % "; ".join(inp.game_src_problems[:3]))


def _verify_scene(inp, files, probs, info):
    if REL_SCENE not in files:
        probs.append("%s is missing" % REL_SCENE)
        return None
    sf = SceneFile(data=files[REL_SCENE], label=REL_SCENE)
    base, ws = inp.base_scene, inp.ws_scene
    if sf.rejected:
        probs.append("scene: %d lines the loader rejects (lines %s)" % (len(sf.rejected), [i + 1 for i in sf.rejected[:5]]))
    if sf.directives:
        probs.append("scene: %d directive lines" % sf.directives)
    base_rejected = {base.lines[i] for i in base.rejected}
    own_rej = [i for i in ws.rejected if ws.lines[i] not in base_rejected]
    if own_rej:
        probs.append("scene: %d workspace lines the loader rejects (they would be lost), e.g. line %d" % (len(own_rej), own_rej[0] + 1))
    tombs = [(k, i) for k, v, i in sf.entries if k and not v]
    lines = [(k, v, i) for k, v, i in sf.entries if k and v]
    wl = sim_load(ws.entries)
    variant = {k for k, v, i in lines if k not in wl.pre and wl.map.get(k) == v}
    led = getattr(inp, "led_scene_keys", None) or set()
    # a line equal to OverLlm's ships only as a reviewed line (the scene ledger lists it)
    same = [k for k, v, i in lines if base.map.get(k) == v and k not in led]
    if same:
        probs.append("scene: %d shipped lines equal the OverLlm release's line for the key and are not in the review ledger "
                     "(e.g. %r)" % (len(same), same[:3]))
    if inp.inst_scene is None:
        info["scene vs the installed OverLlm file"] = "not installed (%s); compared with BASE_REF only" % P.BASE_SCENE
    elif inp.inst_scene is not base:
        same = [k for k, v, i in lines if inp.inst_scene.map.get(k) == v and k not in led]
        if same:
            probs.append("scene: %d shipped lines equal the installed OverLlm file's line for the key and are not in the review "
                         "ledger (XUnity's own runtime lines?), e.g. %r" % (len(same), [k[:50] for k in same[:3]]))
    bad_t = [k for k, _ in tombs if k in ws.map]
    if bad_t:
        probs.append("scene: %d removal lines for keys the workspace has (e.g. %r)" % (len(bad_t), bad_t[:3]))
    useless = [k for k, _ in tombs if k not in base.map]
    if useless:
        info["scene removal lines for keys BASE_REF lacks"] = len(useless)
    c = collections.Counter(k for k, v, i in sf.entries if k)
    dups = [k for k, n in c.items() if n > 1]
    n_same = sum(1 for k, v, i in lines if base.map.get(k) == v)
    info["scene"] = "%d lines shipped (%d revised, %d added, %d trimmed-variant%s) + %d removal lines" % (
        len(lines) - len(variant), sum(1 for k, v, i in lines if k in base.map and base.map[k] != v),
        sum(1 for k, v, i in lines if k not in base.map and k not in variant), len(variant),
        ", %d reviewed, same as OverLlm" % n_same if n_same else "", len(tombs))
    if dups:
        info["scene order-sensitive duplicates in the shipped file (later line wins)"] = "%d, e.g. %r" % (len(dups), dups[:3])
    ov = sim_overlay(base.entries, sf.entries)
    pre_diff = [k for k in set(ov.pre) | set(wl.pre) if ov.pre.get(k) != wl.pre.get(k) and k not in variant]
    post_diff = [k for k in set(ov.map) | set(wl.map) if ov.map.get(k) != wl.map.get(k)]
    if pre_diff:
        probs.append("scene: BASE_REF + shipped lines differs from the workspace in %d keys (e.g. %r)" % (len(pre_diff), sorted(pre_diff)[:3]))
    if post_diff:
        probs.append("scene: after the trimmed-variant pass the maps differ in %d keys (e.g. %r)" % (len(post_diff), sorted(post_diff)[:3]))
    if ov.regexes != wl.regexes:
        probs.append("scene: regex lines differ")
    if ov.values != wl.values:
        probs.append("scene: the known-value sets differ (%d only with the overlay, %d only in the workspace; an earlier duplicate "
                     "line's value in the workspace?), e.g. %r" % (len(ov.values - wl.values), len(wl.values - ov.values),
                                                                   sorted(ov.values ^ wl.values)[:2]))
    info["scene overlay == workspace"] = ("yes (%d keys, %d with trimmed variants, known values equal; %d OverLlm lines replaced, "
                                          "%d removed)" % (len(wl.pre), len(ov.map), sum(1 for k, v, i in lines if k in base.map),
                                                           ov.removed)) if not (pre_diff or post_diff) else "NO"
    order_same = [k for k in ov.pre if k not in variant] == list(wl.pre)
    coll = variant_collisions(wl.pre)
    same_winner = not [k for k in coll if ov.map.get(k) != wl.map.get(k)]
    info["scene load order"] = ("same as the workspace" if order_same else
                                "differs from the workspace (lines the release lacks load last)")
    info["scene order-sensitive trimmed variants"] = (
        "%d (keys two lines produce with different values when trimmed; %s; %d pinned by an explicit line)%s" % (
            len(coll), "the same value wins in both loads" if same_winner else "A DIFFERENT LINE WINS", len(variant),
            (" e.g. %r" % [k[:24] for k in list(coll)[:3]]) if coll else "")) if coll else "none"
    if not same_winner:
        probs.append("scene: a trimmed-variant key takes a different line's value in the overlay")
    return sf


def _verify_manifest(inp, files, probs, info):
    if REL_MANIFEST not in files:
        probs.append("%s is missing" % REL_MANIFEST)
        return
    try:
        m = json.loads(files[REL_MANIFEST].decode("utf-8"))
    except ValueError as e:
        probs.append("MANIFEST.json does not parse: %s" % e)
        return
    if m.get("format") != FORMAT:
        probs.append("MANIFEST.json format is %r, not %r" % (m.get("format"), FORMAT))
    plain = [path for path, s in _json_strings(m, keys=False) if len(s) >= 4 and re.search("[A-Za-z]", s) and not path.endswith(".key")
             and _norm(s) in inp.ol.values]
    if plain:
        probs.append("MANIFEST.json holds OverLlm text at %s" % plain[:5])
    for layer in ("table", "scene"):
        base, ws = sentinel_values(inp, layer)
        ss = m.get(layer + "_sentinels", [])
        for s in ss:
            if set(s) != {"key", "original_sha1", "revised_sha1"}:
                probs.append("MANIFEST.json %s sentinel %r has fields %s" % (layer, s.get("key"), sorted(s)))
                continue
            k = s["key"]
            if k not in base or text_hash(base[k]) != s["original_sha1"]:
                probs.append("MANIFEST.json %s sentinel %r: original_sha1 is not the BASE_REF text's" % (layer, k))
            if ws.get(k) == base.get(k):
                probs.append("MANIFEST.json %s sentinel %r is no longer a revision" % (layer, k))
        info["manifest %s sentinels" % layer] = len(ss)
    outs = m.get("outputs", {})
    for rel, name in ((REL_TABLE, "StringTable.csv"), (REL_META, "StringTable.meta.tsv"), (REL_SCENE, "scene/scene_text.txt")):
        if rel in files and outs.get(name) != md5_bytes(files[rel]):
            probs.append("MANIFEST.json outputs md5 of %s does not match the shipped file" % name)
    br = m.get("base_ref", {})
    if br.get("StringTable.csv", {}).get("md5") != md5_bytes(inp.base_table_bytes) or \
            br.get("_AutoGeneratedTranslations.txt", {}).get("md5") != inp.base_scene.md5:
        probs.append("MANIFEST.json base_ref md5s do not match BASE_REF")
    if "created" in m:
        probs.append("MANIFEST.json has a 'created' time stamp (rebuilds would not be identical)")


def _verify_strings(inp, files, tt, ours, probs, info):
    ws_names = sorted(inp.ws_strings)
    sh_names = sorted(r.split("/", 1)[1] for r in files if r.startswith("strings/"))
    if ws_names != sh_names:
        probs.append("strings: shipped files %s != workspace files %s" % (sh_names, ws_names))
    nprob = notes = short_same = 0
    for name in sh_names:
        try:
            rows, comments = strings_rows(files["strings/" + name])
        except UnicodeDecodeError:
            probs.append("strings/%s is not UTF-8" % name)
            continue
        if name in inp.ws_strings:
            wrows, wcomments = strings_rows(inp.ws_strings[name])
            if [(k, t, n) for k, t, _, n in rows] != [(k, t, n) for k, t, _, n in wrows]:
                probs.append("strings/%s: keys or texts differ from the workspace" % name)
        for k, t, note, _ in rows:
            if PLACEHOLDER.search(t):
                probs.append("strings/%s %s: the text holds a placeholder %r (players would see it)" % (name, k, PLACEHOLDER.search(t).group(0)))
            if inp.base_tt.get(k) == t:
                if len(_words(t)) > 3:
                    probs.append("strings/%s %s: the text is OverLlm's own %d-word line for the key" % (name, k, len(_words(t))))
                else:
                    short_same += 1
            notes += bool(note)
            row_ours = _row_ours(t)
            pr = note_problems(note, inp.row_refs(k), ours, row_ours)
            if pr:
                nprob += 1
                if nprob <= 5:
                    probs.append("strings/%s %s: note %s" % (name, k, "; ".join(pr)))
        for c in comments:
            q = comment_is_old(c, ours)
            if q:
                probs.append("strings/%s comment quotes OverLlm wording %r" % (name, q[:60]))
    if nprob > 5:
        probs.append("strings: %d notes in all still quote old wording" % nprob)
    info["strings"] = "%d files, %d notes, %d still quoting old wording; %d short labels equal to OverLlm's line for the key" % (
        len(sh_names), notes, nprob, short_same)


def _verify_rules(inp, files, probs, info):
    sh = {r.split("/", 1)[1]: d for r, d in files.items() if r.startswith("rules/")}
    if sorted(sh, key=str.lower) != sorted(inp.ws_rules, key=str.lower):
        probs.append("rules: shipped files %s != workspace files %s" % (sorted(sh), sorted(inp.ws_rules)))
    try:
        schema = rule_schema()
    except PublishError as e:
        probs.append("rules: %s" % e)
        return
    n = 0
    for name, data in sorted(sh.items()):
        try:
            got = json.loads(_text(data), object_pairs_hook=_json_pairs)
        except ValueError as e:
            probs.append("rules/%s is not plain JSON (comments left?): %s" % (name, e))
            continue
        if name in inp.ws_rules:
            try:
                want, _ = strip_rules(name, inp.ws_rules[name], schema)
            except PublishError as e:
                probs.append(str(e))
                continue
            if got != want:
                probs.append("rules/%s is not the workspace file without its comments" % name)
        leftover = [p for p, s in _json_strings(got) if p.lower().endswith(".comment(key)")]
        if leftover:
            probs.append("rules/%s still has comment fields at %s" % (name, leftover[:3]))
        n += len(got if isinstance(got, list) else got.get("rules", []))
    info["rules"] = "%d files, %d rules, comments removed" % (len(sh), n)


def _verify_resize(inp, files, probs, info):
    sh = {r[len(REL_RESIZE) + 1:]: d for r, d in files.items() if r.startswith(REL_RESIZE + "/")}
    if sorted(sh) != sorted(inp.ws_resize):
        probs.append("translation/scene/resize: shipped %s != workspace %s" % (sorted(sh), sorted(inp.ws_resize)))
    md5s, lines = inp.overllm_resizers()
    led = getattr(inp, "led_resize", None) or {}
    reviewed = []
    for name, data in sorted(sh.items()):
        if name in inp.ws_resize and inp.ws_resize[name] != data:
            probs.append("translation/scene/resize/%s differs from the workspace's" % name)
        elif name in led and name in inp.ws_resize:
            # a reviewed resizer of this mod's own (the scene ledger lists 'resize/<name>'): it may be OverLlm's, byte for byte
            reviewed.append(name + (" (OverLlm's %s, byte for byte)" % md5s[md5_bytes(data)] if md5_bytes(data) in md5s else ""))
            continue
        if md5_bytes(data) in md5s:
            probs.append("translation/scene/resize/%s is a copy of OverLlm's %s and the review ledger does not list it "
                         "(resize/%s)" % (name, md5s[md5_bytes(data)], name))
            continue
        try:
            copied = [l for l in _text(data).splitlines() if l.strip() in lines]
        except UnicodeDecodeError:
            probs.append("translation/scene/resize/%s is not UTF-8" % name)
            continue
        if copied:
            probs.append("translation/scene/resize/%s: %d lines are OverLlm's resizer lines (e.g. %r) and the review ledger does "
                         "not list it (resize/%s); OverLlm's own resizers are read from its folder" % (
                             name, len(copied), copied[0][:80], name))
    info["own resizers"] = ", ".join(sorted(sh)) or "none"
    if reviewed:
        info["own resizers the review ledger lists"] = ", ".join(reviewed)


def _verify_ledger(inp, files, tt, sf, probs, info):
    """The review ledger: every key it lists is in the workspace (a table key; a scene line's raw key naming a loadable
    workspace line with English; a resizer of the workspace's), and shipped: the shipped files alone (no OverLlm base, as the
    plugin serves them when OverLlm is not installed) hold each listed row and line with the workspace's text, and each
    listed resizer as the workspace's bytes."""
    led_t = getattr(inp, "led_table", None) or {}
    led_raw = getattr(inp, "led_scene_raw", None) or {}
    led_r = getattr(inp, "led_resize", None) or {}
    if not (led_t or led_raw or led_r):
        return
    asset_t = [k for k in inp.led_table_missing if _skipped_key(k)]
    if asset_t:
        probs.append("review ledger: %d listed table keys are asset rows (TextFont, Image_*, TextMeshFont_*) that TextTable.Load "
                     "skips, so they never ship, e.g. %r. %s lists them; %s takes them out" % (
                         len(asset_t), asset_t[:3], ledger_cmd("missing"), ledger_cmd("remove", "table", "@FILE")))
    for what, miss, where in (("table keys", [k for k in inp.led_table_missing if not _skipped_key(k)],
                               "the workspace table has no such row"),
                              ("scene lines", inp.led_scene_missing, "no loadable workspace scene line with English is "
                                                                     "written with that raw key"),
                              ("resizers", ["resize/" + n for n in inp.led_resize_missing],
                               "no such file in " + P.WS_RESIZE_DIR)):
        if miss:
            probs.append("review ledger: %d listed %s are not in the workspace (%s), e.g. %r. %s lists them; %s takes them "
                         "out" % (len(miss), what, where, [k[:60] for k in miss[:3]], ledger_cmd("missing"),
                                  ledger_cmd("remove", "table|scene", "@FILE")))
    shadowed = getattr(inp, "led_scene_shadowed", None) or []
    if shadowed:
        probs.append("review ledger: %d listed scene lines are not the line their key loads from (a later workspace line for "
                     "the same key, written with another raw key, overrides it and is what ships), e.g. %r: list that line "
                     "instead, or remove the duplicate" % (len(shadowed), [k[:60] for k in shadowed[:3]]))
    ws_tt = inp.ws_tt
    unshipped_t = [k for k in led_t if k in ws_tt and tt.get(k) != ws_tt[k]]
    if unshipped_t:
        probs.append("review ledger: %d listed table rows are not shipped with the workspace's text (e.g. %s)" % (
            len(unshipped_t), unshipped_t[:3]))
    led_s = getattr(inp, "led_scene_keys", None) or set()
    alone = sim_load(sf.entries) if sf is not None else SceneSim().finish()
    unshipped_s = [k for k in led_s if alone.pre.get(k) != inp.ws_scene.map.get(k)]
    if unshipped_s:
        probs.append("review ledger: %d listed scene lines are not shipped with the workspace's text (the shipped scene file "
                     "alone does not give it), e.g. %r" % (len(unshipped_s), [k[:50] for k in unshipped_s[:3]]))
    unshipped_r = [n for n in led_r if n in inp.ws_resize and files.get(REL_RESIZE + "/" + n) != inp.ws_resize[n]]
    if unshipped_r:
        probs.append("review ledger: %d listed resizers are not shipped as the workspace's file: %s" % (len(unshipped_r), unshipped_r[:5]))
    same_t = sum(1 for k in led_t if k in ws_tt and inp.base_tt.get(k) == ws_tt[k])
    same_s = sum(1 for k in led_s if inp.base_scene.map.get(k) == inp.ws_scene.map.get(k))
    md5s = inp.overllm_resizers()[0]
    same_r = sum(1 for n in led_r if n in inp.ws_resize and md5_bytes(inp.ws_resize[n]) in md5s)
    info["review ledger"] = ("%d table keys (%d reviewed, same as OverLlm), %d scene lines (%d keys; %d reviewed, same as "
                             "OverLlm), %d resizers (%d byte-identical to OverLlm's); %s" % (
                                 len(led_t), same_t, len(led_raw), len(led_s), same_s, len(led_r), same_r,
                                 "every listed one shipped (the shipped files alone hold them)"
                                 if not (unshipped_t or unshipped_s or unshipped_r) else "NOT all shipped"))


def _cross_copies(inp, tt, sf):
    """Shipped rows equal to an OverLlm value of the other file whose own entry this mod did not replace (ruled accepted)."""
    shipped_scene = {k: v for k, v, i in sf.entries if k and v} if sf else {}
    sv = collections.defaultdict(list)
    for k, v in inp.base_scene.map.items():
        sv[v].append(k)
    t_eq = sum(1 for k, v in tt.items() if v in sv and not any(sk in shipped_scene for sk in sv[v]))
    tv = collections.defaultdict(list)
    for k, v in inp.base_tt.items():
        tv[v].append(k)
    s_eq = sum(1 for k, v in shipped_scene.items() if v in tv and not any(tk in tt for tk in tv[v]))
    return t_eq, s_eq


def verify(inp, files, target=None):
    """Problems (a list of strings; empty = pass) and info for shipped files {relative path: bytes}; with target, the folder
    itself too (unknown files, copies, every other text file)."""
    probs, info = [], collections.OrderedDict()
    tt = _verify_table(inp, files, probs, info)
    _verify_meta(inp, files, tt, probs, info)
    sf = _verify_scene(inp, files, probs, info)
    _verify_manifest(inp, files, probs, info)
    shipped_strings = [files[r] for r in sorted(files) if r.startswith("strings/")]
    try:
        stexts = strings_texts(shipped_strings)
    except UnicodeDecodeError:
        stexts = []
    # this mod's own wording: the shipped text that is not OverLlm's line for its key (a reviewed row or line that stayed
    # OverLlm's is still OverLlm's wording for the note check and the text scan, which stay as strict as without the ledger)
    ours = Ours([v for k, v in tt.items() if inp.base_tt.get(k) != v]
                + ([v for k, v, i in sf.entries if k and v and inp.base_scene.map.get(k) != v] if sf else []) + stexts, inp.ol)
    _verify_strings(inp, files, tt, ours, probs, info)
    _verify_rules(inp, files, probs, info)
    _verify_resize(inp, files, probs, info)
    _verify_ledger(inp, files, tt, sf, probs, info)
    t_eq, s_eq = _cross_copies(inp, tt, sf)
    info["cross-file exact copies (ruled accepted)"] = ("%d table rows equal an OverLlm scene value, %d scene lines equal an "
                                                        "OverLlm table value" % (t_eq, s_eq))
    sp, binary = scan_files({r: d for r, d in files.items() if r != REL_STATE and r not in RUNTIME_FILES}, inp.ol, ours,
                            {"table": inp.base_tt, "scene": inp.base_scene.map})
    probs += sp
    if target:
        _verify_folder(inp, files, target, ours, probs, info)
    return probs, info


long_path = P.long_path        # \?\ paths: no 260-character limit, names ending in a dot or a space kept


def list_folder(root):
    """Every file below root, walked by long paths: ({rel: long path} with '/' in rel, [rel of each link or junction, not
    followed]). A folder that cannot be listed raises PublishError: a check must fail on what it cannot open, never skip it."""
    lroot = long_path(root).rstrip(BS)
    files, links, errors = {}, [], []
    for r, ds, fs in os.walk(lroot, onerror=errors.append):
        rr = r[len(lroot):].strip(BS).replace(BS, "/")
        pre = rr + "/" if rr else ""
        for d in list(ds):
            if P.is_link(os.path.join(r, d)):
                links.append(pre + d + "/")
                ds.remove(d)
        for f in fs:
            p = os.path.join(r, f)
            if P.is_link(p):
                links.append(pre + f)
            else:
                files[pre + f] = p
    if errors:
        raise PublishError("cannot open %d folders under %s (e.g. %s: %s); nothing there could be checked" % (
            len(errors), root, getattr(errors[0], "filename", "?"), errors[0].strerror or errors[0]))
    return files, links


def managed_files(target):
    """{rel: path} of the files publish.py manages in target: translation/, strings/, rules/, profiles/, the notices,
    nametips.tsv and factiontips.tsv."""
    out = {}
    for d in MANAGED_DIRS:
        root = os.path.join(target, d)
        if not os.path.isdir(long_path(root)):
            continue
        fs, _ = list_folder(root)
        for rel, p in fs.items():
            out[d + "/" + rel] = p
    for rel in (REL_NOTICES, REL_NAMETIPS, REL_FACTIONTIPS):
        p = rel_path(target, rel)
        if os.path.exists(p):
            out[rel] = p
    return out


def _verify_folder(inp, files, target, ours, probs, info):
    if os.path.isdir(os.path.join(target, "profiles")):
        probs.append("profiles/ exists (the 0.4 built-in copy of OverLlm's files)")
    allowed = {REL_TABLE, REL_META, REL_SCENE, REL_MANIFEST, REL_STATE, REL_UNTRANSLATED}
    allowed |= {REL_RESIZE + "/" + n for n in inp.ws_resize}
    try:
        managed = managed_files(target)
    except PublishError as e:
        probs.append(str(e))
        managed = {}
    unknown = sorted(rel for rel in managed if rel.startswith("translation/") and rel not in allowed)
    if unknown:
        probs.append("unknown files under translation/ (the plugin may load them; publish.py does not produce them): %s" % unknown[:10])
    if os.path.exists(P.NOTICES_SRC) and not os.path.exists(os.path.join(target, REL_NOTICES)):
        probs.append("THIRD_PARTY_NOTICES.txt is missing")
    if os.path.exists(P.WS_NAMETIPS) and not os.path.exists(os.path.join(target, REL_NAMETIPS)):
        probs.append("nametips.tsv is missing (the dialog name tips then stay off)")
    if os.path.exists(P.WS_FACTIONTIPS) and not os.path.exists(os.path.join(target, REL_FACTIONTIPS)):
        probs.append("factiontips.tsv is missing (faction names then stay plain)")
    for lib in sorted(PINNED_LIBS):
        if not os.path.exists(os.path.join(target, lib)):
            probs.append("%s is missing (without the OverLlm patch's copy in BepInEx/core the plugin cannot load; the pinned "
                         "file is in LOM_Localization/release/vendor/unity-newtonsoft/Runtime-AOT, see docs/DEVELOPMENT.md)" % lib)
    hdir = os.path.join(target, "harness")
    if os.path.isdir(long_path(hdir)):
        # harness/ may hold cmd.txt and an empty out/ (what a harness run leaves when cleaned up); anything else is dev output
        n_out, extra = 0, []
        lh = long_path(hdir)
        for r, ds, fs in os.walk(lh, onerror=lambda e: extra.append("(a folder that cannot be opened: %s)" % e.filename)):
            rr = r[len(lh):].strip(BS).replace(BS, "/") or "."
            for d in ds:
                rel = (d if rr == "." else rr + "/" + d)
                try:
                    empty = not os.listdir(os.path.join(r, d))
                except OSError:
                    empty = False
                if rel != "out" and empty:
                    extra.append(rel + "/")
            for f in fs:
                rel = f if rr == "." else rr + "/" + f
                if rel == "cmd.txt":
                    continue
                if rel.startswith("out/"):
                    n_out += 1
                else:
                    extra.append(rel)
        if n_out or extra:
            info["WARNING harness/"] = ("harness/ holds %s (dev dumps of game screens and tables and other developer files, "
                                        "OverLlm's text included; they are not scanned): harness/ is a developer folder and must "
                                        "never be packaged or shipped" % ", ".join(
                                            ([("%d files in out/" % n_out)] if n_out else []) +
                                            ([("%d other entries (%s)" % (len(extra), ", ".join(extra[:4])))] if extra else [])))
    ref_md5 = {md5_bytes(inp.base_table_bytes): "BASE_REF StringTable.csv", inp.base_scene.md5: "BASE_REF scene file"}
    for p, what in ((P.BASE_TABLE, "the installed OverLlm table"), (P.BASE_SCENE, "the installed OverLlm scene file")):
        if os.path.exists(p):
            ref_md5[md5_file(p)] = what
    for p in (inp.inst_layout.files if inp.inst_layout.kind == "per-file" else []):
        ref_md5[md5_file(p)] = "the installed OverLlm table " + os.path.basename(p)
    md5s, _ = inp.overllm_resizers()
    for h, name in md5s.items():
        ref_md5[h] = "OverLlm's " + name
    # a reviewed resizer (the scene ledger lists resize/<name>) shipped as the workspace's own file may be OverLlm's, byte for
    # byte, at its own shipped path only
    led_r = getattr(inp, "led_resize", None) or {}
    reviewed_copy = {REL_RESIZE + "/" + n: inp.ws_resize[n] for n in led_r if n in inp.ws_resize}
    copies, others, large, unreadable = [], {}, [], []
    try:
        allf, links = list_folder(target)
    except PublishError as e:
        probs.append(str(e))
        return
    runtime = sorted(rel for rel in allf if rel in RUNTIME_FILES)
    if runtime:
        info["runtime files (never packaged, not scanned)"] = ", ".join(runtime)
    for rel, p in sorted(allf.items()):
        if rel.startswith("harness/") or rel in RUNTIME_FILES:
            continue
        try:
            size = os.path.getsize(p)
            data = file_bytes(p) if size <= MAX_TEXT_FILE or expected_binary(rel) else None
        except OSError as e:
            unreadable.append("%s (%s)" % (rel, e.strerror or e))
            continue
        if data is None or (size > MAX_TEXT_FILE and not expected_binary(rel, data)):
            large.append("%s (%.1f MB)" % (rel, size / 1048576.0))
            continue
        if md5_bytes(data) in ref_md5 and reviewed_copy.get(rel) != data:
            copies.append("%s (= %s)" % (rel, ref_md5[md5_bytes(data)]))
        if rel not in files and rel != REL_STATE:
            others[rel] = data
    if unreadable:
        probs.append("files in the folder that cannot be read (so they cannot be checked): %s" % unreadable[:5])
    if copies:
        probs.append("copies of OverLlm files in the folder: %s" % copies)
    if large:
        probs.append("files over %d MB in the folder (no plugin file is that large; not scanned): %s" % (MAX_TEXT_FILE >> 20, large[:5]))
    if links:
        probs.append("links or junctions in the folder (not followed; a plugin folder holds plain files): %s" % links[:5])
    sp, binary = scan_files(others, inp.ol, ours)
    probs += sp
    info["other files scanned for OverLlm text"] = "%d files (%d expected binary files skipped: %s)" % (
        len(others) - len(binary), len(binary), ", ".join(binary[:4]) + (" ..." if len(binary) > 4 else ""))


# ---- build ----------------------------------------------------------------------------------------------------------------

_SHIP_REL = re.compile(r"^(translation/(StringTable\.csv|StringTable\.meta\.tsv|MANIFEST\.json|\.publish_state\.json|"
                       r"scene/scene_text\.txt|scene/resize/[^/\\]+resizer\.txt)|THIRD_PARTY_NOTICES\.txt|nametips\.tsv|factiontips\.tsv|"
                       r"strings/[^/\\]+\.csv|rules/[^/\\]+\.json)$", re.I)


def _guard_target(target):
    """The folder build may write: in stage mode only inside the stage; otherwise the real plugin folder or a folder
    outside the game; never the workspace, BASE_REF, backups, the tools, LOM_Localization, Mods or BepInEx/Translation. The
    target is resolved first (8.3 short names, junctions, links: lom_paths.real / inside / same_path), and the resolved path
    is what build writes to."""
    t = P.real(target)
    if P.STAGE and not P.inside(t, P.STAGE):
        raise PublishError("refusing to publish into %s: in stage mode (%s) the target must be inside the stage" % (target, P.STAGE))
    for q in (os.path.join(P.GAME, "Mods"), os.path.join(P.GAME, "BepInEx", "Translation"), P.REAL_LOCALIZATION,
              P.WORKSPACE, P.BASE_REF, P.BACKUPS, P.LOCALIZATION, P.TOOLS):
        if P.inside(t, q) or P.inside(q, t):
            raise PublishError("refusing to publish into %s (it is, holds or lies inside %s; not a plugin folder)" % (target, q))
    if P.inside(P.GAME, t):
        raise PublishError("refusing to publish into %s (it holds the game folder)" % target)
    if P.inside(t, P.GAME) and not P.same_path(t, P.DEFAULT_PLUGIN):
        raise PublishError("refusing to publish into %s: inside the game folder only %s is a target" % (target, P.DEFAULT_PLUGIN))
    links = P.links_under(t)
    if links:
        raise PublishError("refusing to publish into %s: it holds links or junctions (%s); a write or removal through one would "
                           "land outside the folder" % (target, [os.path.relpath(l, t) for l in links[:5]]))
    return t


def _managed_rel(rel):
    return rel in (REL_NOTICES, REL_NAMETIPS, REL_FACTIONTIPS) or rel.split("/", 1)[0] in MANAGED_DIRS


def _ship_write(target, rel, data):
    if not _SHIP_REL.match(rel):
        raise PublishError("refusing to write %s: not a shipped file" % rel)
    p = rel_path(target, rel)
    if not P.inside(p, target):
        raise PublishError("refusing to write %s: it resolves outside %s (a link or junction?)" % (rel, target))
    os.makedirs(os.path.dirname(p), exist_ok=True)
    tmp = p + ".publish-tmp"
    with open(tmp, "wb") as f:
        f.write(data)
    os.replace(tmp, p)


def _ship_remove(target, rel):
    if not _managed_rel(rel) or ".." in rel.split("/"):
        raise PublishError("refusing to remove %s: not a file publish.py manages" % rel)
    p = rel_path(target, rel)
    if not P.inside(p, target):
        raise PublishError("refusing to remove %s: it resolves outside %s (a link or junction?)" % (rel, target))
    if os.path.exists(p):
        os.chmod(p, 0o666)
        os.remove(p)


def _rmtree(path):
    def onerror(func, p, exc):
        os.chmod(p, 0o666)
        func(p)
    shutil.rmtree(path, onerror=onerror)


def _prune_dirs(target):
    for d in (REL_RESIZE, "profiles/original", "profiles"):
        p = rel_path(target, d)
        if os.path.isdir(p):
            try:
                for r, ds, fs in os.walk(p, topdown=False):
                    if not fs and not os.listdir(r):
                        os.rmdir(r)
            except OSError:
                pass


def read_state(target):
    p = rel_path(target, REL_STATE)
    if not os.path.exists(p):
        return None
    with open(p, encoding="utf-8") as f:
        return json.load(f)


def _state_bytes(state):
    return (json.dumps(state, indent=1, ensure_ascii=False, sort_keys=True) + "\n").encode("utf-8")


def _known_hashes(state):
    known = collections.defaultdict(set)
    for part in ("files", "pending", "before"):
        for rel, rec in ((state or {}).get(part) or {}).items():
            if rec and rec.get("sha256"):
                known[rel].add(rec["sha256"])
    return known


def plan_ops(target, plan, inp, adopt=False):
    """What build would do in target: {"state", "writes", "removes", "conflicts": [(rel, why)]} (nothing is written)."""
    state = read_state(target)
    if adopt and state:
        raise PublishError("%s was published before (%s); adopt is only for the first publish" % (target, state.get("published")))
    known = _known_hashes(state)
    recorded = (state or {}).get("files") or {}
    existing = managed_files(target)
    writes, removes, conflicts = [], [], []
    for rel, data in plan.items():
        p = existing.get(rel)
        if p is None:
            writes.append(rel)
            continue
        cur = sha256_bytes(file_bytes(p))
        if cur == sha256_bytes(data):
            continue
        if cur in known.get(rel, ()) or (adopt and not state):
            writes.append(rel)
        elif rel in recorded:
            conflicts.append((rel, "edited by hand since the publish of %s" % (state or {}).get("published", "?")))
        else:
            conflicts.append((rel, "exists and was not written by publish.py"))
    ol_md5s, _ = inp.overllm_resizers()
    profiles_known = {"profiles/original/StringTable.csv": md5_bytes(inp.base_table_bytes),
                      "profiles/original/xunity_translations.txt": inp.base_scene.md5, "profiles/original/README.txt": None}
    for rel, p in sorted(existing.items()):
        if rel in plan or rel in (REL_STATE, REL_UNTRANSLATED):
            continue
        if rel.endswith(".publish-tmp"):
            removes.append(rel)
            continue
        data = file_bytes(p)
        cur = sha256_bytes(data)
        if rel in known:
            if cur in known[rel]:
                removes.append(rel)
            else:
                conflicts.append((rel, "published earlier, no longer produced, and edited since"))
        elif rel.startswith(REL_RESIZE + "/") and md5_bytes(data) in ol_md5s:
            removes.append(rel)
        elif rel in profiles_known and (profiles_known[rel] is None or md5_bytes(data) == profiles_known[rel]):
            removes.append(rel)
        elif rel.startswith(REL_RESIZE + "/"):
            conflicts.append((rel, "an own resizer that is not in the workspace (move it to %s to publish it)" % P.WS_RESIZE_DIR))
        else:
            conflicts.append((rel, "not produced by publish.py (added by hand?)"))
    return {"state": state, "writes": writes, "removes": removes, "conflicts": conflicts}


def build(target=None, force=False, verbose=True, check_after=True, adopt=False, rerecord=False, keep_record=None):
    """Write the shipped files into target (default: the plugin folder). Returns a result dict with a one-line 'summary'.

    force: overwrite or remove shipped files edited by hand since the last publish (each first copied to
    backups/publish-force-<stamp>/). adopt: the first publish into a folder (cutover_0_5.py): files that no publish wrote
    may be replaced (the caller has backed them up); files a publish recorded are still protected. rerecord: when the
    workspace revision record is missing, record afresh the rows that cannot be recovered from target's shipped files
    (ensure_records; without it build refuses then). keep_record: --keep-record KEYS|@FILE|all (parse_keep_record): the
    changed rows among them keep their old record, and the records an earlier build's re-record listing names come back.
    A changed row whose old record's game-text hash is not game_source.tsv's (it was out of date) is re-recorded by
    default, and said: the warning and backups/rerecord-<stamp>/rerecorded_out_of_date.tsv name them."""
    target = _guard_target(target or P.PLUGIN)
    log = say if verbose else (lambda *a: None)
    keep = parse_keep_record(keep_record)
    inp = Inputs()
    rrep = ensure_records(inp, target, rerecord)
    plan, rep = make_plan(inp, keep)
    old_records_bytes = inp.ws_records_bytes
    adopt_records(inp, rep["records"], rep["record_tags"])
    probs, info = verify(inp, plan)
    if probs:
        raise PublishError("the planned files fail the check: " + " | ".join(probs[:8]))
    ops = plan_ops(target, plan, inp, adopt=adopt)
    if ops["conflicts"] and not force:
        raise PublishError("shipped files changed outside publish.py: %s. Move those edits into the workspace (%s) and rerun, "
                           "or rerun with --force to overwrite them (they are backed up first)" % (
                               "; ".join("%s %s" % c for c in ops["conflicts"]), P.WORKSPACE))
    state = ops["state"]
    writes = list(ops["writes"]) + [rel for rel, _ in ops["conflicts"] if rel in plan]
    removes = list(ops["removes"]) + [rel for rel, _ in ops["conflicts"] if rel not in plan]
    new_files = {rel: {"sha256": sha256_bytes(d), "size": len(d)} for rel, d in plan.items()}
    base_md5 = {"StringTable.csv": md5_bytes(inp.base_table_bytes), "_AutoGeneratedTranslations.txt": inp.base_scene.md5}
    res = {"target": target, "report": rep, "removed": [], "info": info, "conflicts_overwritten": [], "changed": [],
           "backup": None, "record_saved": False, "record_recovery": rrep, "rerecorded_out_of_date": [],
           "rerecord_listing": None}
    notes = rep["record_notes"]
    # the revision record of every shipped row is kept in the workspace before anything is shipped, so a later 'source' or
    # 'rebase' (which change what a new record would hold) keeps what each row was revised against
    if rep["ws_records_bytes"] != old_records_bytes:
        if notes["stale"]:
            # rows re-recorded although they were out of date: say so, list them (with the record they had) next to a copy
            # of the record this replaces, so --keep-record can bring their old record back
            stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
            rdir = P.new_backup_dir("rerecord", stamp)
            if old_records_bytes is not None:
                with open(P.out_path(rdir, "revision_record.tsv"), "wb") as f:
                    f.write(old_records_bytes)
            lpath = P.out_path(rdir, RERECORD_LISTING)
            write_rerecord_listing(lpath, notes["stale"], "publish.py build %s" % stamp)
            res["rerecorded_out_of_date"] = [x[0] for x in notes["stale"]]
            res["rerecord_listing"] = lpath
        save_ws_records(rep["ws_records_bytes"])
        res["record_saved"] = True
        m = rep["meta"]
        note = recovery_note(rrep)
        if note:
            say(note)
        log("revision record: %d rows kept, %d recorded (%d new, %d changed)%s -> %s" % (
            m["kept"], m["new"] + m["changed"], m["new"], m["changed"],
            "; %d changed rows kept their old record, %d restored (--keep-record)" % (m["kept_record"], m["restored"])
            if m["kept_record"] or m["restored"] else "", P.WS_RECORD))
        if notes["stale"]:
            say(stale_note(notes["stale"], res["rerecord_listing"], tool_cmd("build")))
    kn = keep_note(rep["records"], notes)
    if kn:
        say(kn)
    if not writes and not removes and state and state.get("files") == new_files and not state.get("pending"):
        res["summary_head"] = "up to date (nothing changed)" if not res["record_saved"] else "up to date (revision record saved)"
    else:
        existing = managed_files(target)
        if force and ops["conflicts"]:
            stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
            bdir = P.new_backup_dir("publish-force", stamp)
            for rel, why in ops["conflicts"]:
                if rel in existing:
                    dst = P.guard_under(rel_path(bdir, rel), bdir)
                    os.makedirs(os.path.dirname(dst), exist_ok=True)
                    shutil.copy2(existing[rel], P.guard_under(dst, bdir))
            res["backup"] = bdir
            res["conflicts_overwritten"] = ["%s %s" % c for c in ops["conflicts"]]
            log("backed up the files --force overwrites or removes to", bdir)
        before = {rel: {"sha256": sha256_bytes(file_bytes(existing[rel]))} for rel in writes + removes if rel in existing}
        now = datetime.datetime.now().isoformat(timespec="seconds")
        pending = {"format": STATE_FORMAT, "published": (state or {}).get("published"), "base_ref": (state or {}).get("base_ref"),
                   "files": (state or {}).get("files") or {}, "pending": new_files, "before": before, "pending_since": now}
        try:
            _ship_write(target, REL_STATE, _state_bytes(pending))
            for rel in removes:
                _ship_remove(target, rel)
                log("removed", rel)
            _prune_dirs(target)
            for rel in writes:
                _ship_write(target, rel, plan[rel])
            _ship_write(target, REL_STATE, _state_bytes({"format": STATE_FORMAT, "published": now, "base_ref": base_md5,
                                                         "files": new_files}))
        except OSError as e:
            raise PublishError("the publish into %s was interrupted (%s); the workspace holds everything and the state file "
                               "records the pending files, so rerunning publish.py build finishes it" % (target, e))
        for rel, data in plan.items():
            if file_bytes(rel_path(target, rel)) != data:
                raise PublishError("read-back of %s does not match what was written" % rel)
        res["removed"] = removes
        res["changed"] = writes
        res["summary_head"] = "wrote %d files, removed %d" % (len(writes), len(removes))
    if check_after:
        files = read_shipped(target)
        probs, info2 = verify(inp, files, target)
        res["check"] = probs
        res["info"] = info2
        if probs:
            raise WrittenCheckFailed("written, but the check of %s fails: %s" % (target, " | ".join(probs[:8])))
    s = rep["strings"]
    same_t = ", %d reviewed, same as OverLlm" % rep["table_same"] if rep["table_same"] else ""
    same_s = ", %d reviewed, same as OverLlm" % rep["scene_same"] if rep["scene_same"] else ""
    led = ""
    if uses_ledger(inp):
        led = ("; review ledger: %d table rows + %d scene lines + %d resizers shipped as reviewed (reviewed, same as OverLlm: "
               "%d rows + %d lines + %d resizers)" % (rep["table_reviewed"], rep["scene_reviewed"], len(rep["resizers_reviewed"]),
                                                      rep["table_same"], rep["scene_same"], len(rep["resizers_same"])))
    res["summary"] = ("%s: table %d rows (%d revised, %d added%s), scene %d lines (%d revised, %d added%s) + %d removals, rules %d "
                      "files (%d comments + %d comment fields removed), strings %d files (%d of %d notes cleaned), sentinels "
                      "%d+%d%s -> %s%s" % (
                          res["summary_head"], rep["table_rows"], rep["table_revised"], rep["table_added"], same_t,
                          rep["scene_lines"], rep["scene_revised"], rep["scene_added"], same_s, rep["scene_tombstones"],
                          rep["rules"]["files"], rep["rules"]["comments"], rep["rules"]["comment_fields"], s["files"],
                          s["cleaned"], s["notes"], rep["sentinels"]["table"]["kept"], rep["sentinels"]["scene"]["kept"], led,
                          target, "; check ok" if check_after else ""))
    log(res["summary"])
    return res


def save_ws_records(data, path=None):
    """Write the workspace revision record (atomically; only the workspace's own file, never outside the stage)."""
    path = path or P.WS_RECORD
    if not (P.same_path(path, P.WS_RECORD) or P.same_path(path, P.WS_RECORD + ".rebase-tmp")):
        raise PublishError("refusing to write the revision record to %s" % path)
    P.guard_write(path)
    if not os.path.isdir(os.path.dirname(path)):
        raise PublishError("%s does not exist; the workspace is created by cutover_0_5.py" % os.path.dirname(path))
    tmp = path + ".tmp"
    with open(tmp, "wb") as f:
        f.write(data)
    os.replace(tmp, path)


def read_shipped(target):
    """Every file publish.py manages in target, as {rel: bytes} (the state file aside)."""
    return {rel: file_bytes(p) for rel, p in managed_files(target).items() if rel != REL_STATE}


def check(target=None):
    target = P.real(target or P.PLUGIN)
    try:
        inp = Inputs()
    except PublishError as e:
        return ["cannot check: %s" % e], collections.OrderedDict()
    try:
        files = read_shipped(target)
    except (PublishError, OSError) as e:
        return ["cannot read %s: %s" % (target, e)], collections.OrderedDict()
    missing = None
    if inp.ws_records_bytes is None:
        # what build will do about it (the same recovery), and the shipped meta compared with the recovered record
        rec, fresh, lost, why, mp = recover_records(target, ship_rows(inp))
        inp.ws_records, inp.ws_record_tags = rec, {}
        missing = ("the workspace revision record %s is missing. publish.py build %s" % (P.WS_RECORD, (
            "refuses (exit 2): " + missing_record_help(lost, why)) if lost else (
            "recovers it: %d rows from %s (their shipped value is the workspace's)%s; restoring it from a backup "
            "(backups/source-<stamp>/ or backups/rebase-<stamp>/workspace/translation/) works too" % (
                len(rec), mp, ", and records the %d rows new or changed since that publish as usual" % len(fresh) if fresh else ""))))
    probs, info = verify(inp, files, target)
    if missing:
        probs.insert(0, missing)
    try:
        plan, prep = make_plan(inp)
    except PublishError as e:
        probs.append("the workspace cannot be published: %s" % e)
        plan = None
    if plan is not None:
        info["workspace revision record"] = ("MISSING (see the first failure)" if missing else "up to date"
                                             if prep["ws_records_bytes"] == inp.ws_records_bytes else
                                             "not up to date: the next build records %d rows (rows new or changed since the "
                                             "last build; it keeps every recorded row whose text is unchanged)%s" % (
                                                 prep["meta"]["new"] + prep["meta"]["changed"],
                                                 "; %d of them are out of date (their game text changed since they were "
                                                 "revised): the build re-records them as revisions for the new text, lists "
                                                 "them and offers --keep-record" % len(prep["record_notes"]["stale"])
                                                 if prep["record_notes"]["stale"] else ""))
        differ = sorted(rel for rel in plan if files.get(rel) != plan[rel])
        extra = sorted(rel for rel in files if rel not in plan and rel != REL_UNTRANSLATED)
        if differ:
            probs.append("%d shipped files differ from what publish.py build writes now (edited by hand, or the workspace changed "
                         "since the last publish): %s" % (len(differ), differ[:8]))
        if extra:
            probs.append("files publish.py does not produce: %s" % extra[:8])
        info["shipped files == what build writes now"] = "yes (%d files)" % len(plan) if not (differ or extra) else "NO"
    state = read_state(target)
    if state:
        if state.get("pending"):
            probs.append("a publish was interrupted (pending since %s): rerun publish.py build" % state.get("pending_since"))
        info["last publish"] = state.get("published")
        if any(isinstance(v, str) and (":" + BS in v or v.startswith(("/", BS))) for p, v in _json_strings(state)):
            probs.append("translation/.publish_state.json holds absolute paths")
    else:
        info["publish state"] = "none (not written by publish.py)"
    return probs, info


# ---- rebase ---------------------------------------------------------------------------------------------------------------

def _find(d, names):
    for n in names:
        p = os.path.join(d, *n.split("/"))
        if os.path.exists(p):
            return p
    return None


def idea_row_spans(text):
    """The rows idea_parse yields, each with the [start, end) of its physical lines (line terminators included)."""
    out, cur, val, st = [], [], [], "LS"
    row_start = None
    pos, n = 0, len(text)
    for m in re.finditer("\n|\r\n", text + "\n"):
        line = text[pos:m.start()]
        end = min(m.end(), n)
        if len(line) == 0:
            pos = end
            continue
        if row_start is None:
            row_start = pos
        for c in line:
            if c == ",":
                if st in ("LS", "VS", "V", "QQ"):
                    cur.append("".join(val)); val = []; st = "VS"
                else:
                    val.append(",")
            elif c == '"':
                if st in ("LS", "VS"):
                    st = "Q"
                elif st == "V":
                    val.append('"')
                elif st == "Q":
                    st = "QQ"
                else:
                    val.append('"'); st = "Q"
            else:
                if st in ("LS", "VS", "V"):
                    val.append(c); st = "V"
                else:
                    val.append(c)
                    if st == "QQ":
                        st = "Q"
        if st == "LS":
            out.append((cur, row_start, end)); cur = []; row_start = None
        elif st in ("VS", "V", "QQ"):
            cur.append("".join(val)); val = []; out.append((cur, row_start, end)); cur = []; st = "LS"; row_start = None
        else:
            val.append("\r"); val.append("\n")
        pos = end
        if pos >= n:
            break
    if val:
        cur.append("".join(val))
    if cur:
        out.append((cur, row_start if row_start is not None else pos, n))
    return out


def rebase(new_dir, dry_run=False, allow_converged=False, keep_record=None):
    keep = parse_keep_record(keep_record)
    # since its llmkit-upgrade the release has one table per game file instead of StringTable.csv (lashtables); BASE_REF
    # stores them as one StringTable.csv with the same map
    new_layout = None
    for d in (new_dir, os.path.join(new_dir, "Mods", "English")):
        lay = LT.find(d)
        if lay.kind == "per-file":
            new_layout = lay
            break
    new_table = None if new_layout else _find(new_dir, ["StringTable.csv", "Mods/English/StringTable.csv"])
    new_scene = _find(new_dir, ["_AutoGeneratedTranslations.txt", "BepInEx/Translation/en/Text/_AutoGeneratedTranslations.txt"])
    scene_kept = False
    if new_layout and not new_scene and os.path.exists(P.BASE_REF_SCENE):
        # that release no longer ships XUnity's file: an install keeps the one it had, and BASE_REF keeps its own
        new_scene, scene_kept = P.BASE_REF_SCENE, True
    if not (new_table or new_layout) or not new_scene:
        raise PublishError("%s must hold StringTable.csv (or the newer releases' per-file tables) and "
                           "_AutoGeneratedTranslations.txt (or the release's Mods/English and BepInEx/Translation/en/Text "
                           "folders)" % new_dir)
    for p in (P.BASE_REF + ".rebase-new", P.BASE_REF + ".rebase-old"):
        if os.path.exists(p):
            raise PublishError("%s exists: an earlier rebase was interrupted. If publish.py check still accepts the workspace and "
                               "base_ref (the swap never started), delete that folder and any *.rebase-tmp files; otherwise "
                               "restore both from that rebase's backup (backups/rebase-<stamp>/) first" % p)
    inp = Inputs()
    rrep = ensure_records(inp, P.PLUGIN, False)
    say("rebase: workspace %s\n        BASE_REF  %s\n        new base  %s, %s" % (
        P.WORKSPACE, P.BASE_REF, new_table or ("%d per-file tables in %s" % (len(new_layout.files), new_layout.dir)),
        new_scene + (" (kept: the new release has none)" if scene_kept else "")))
    # our revisions and reviewed lines, as the current publish would ship them (a reviewed line the review kept as OverLlm's
    # line is this mod's text too: it is kept like a revision, so the review ledger stays true of what ships)
    t_delta = dict(ship_rows(inp))
    idx, tombs, _ = scene_delta(inp.ws_scene, inp.base_scene, inp.led_scene_keys)
    s_lines = collections.OrderedDict()
    for i in idx:
        s_lines[decode(inp.ws_scene.lines[i])[0]] = inp.ws_scene.lines[i]
    s_tomb = {decode(t)[0] for t in tombs}

    # table: the new release (rows with more than two fields repaired), our rows in place of its rows, ours it lacks appended
    if new_layout:
        per_map, per_st = LT.read_per_file(new_layout.files)
        try:
            new_text_raw = LT.single_text(per_map)
        except ValueError as e:
            raise PublishError("the new release's tables cannot be kept as one StringTable.csv: %s" % e)
        say("new base: %d per-file tables, %d rows (%d spanning lines, %d repeated keys, the last row kept)%s" % (
            len(new_layout.files), per_st["rows"], per_st["multiline"], per_st["duplicates"],
            "; an older StringTable.csv next to them is left out" if new_layout.leftover else ""))
    else:
        new_text_raw = read_text(new_table)
    new_text, repaired = repair_table(new_text_raw)
    new_tt, _ = table_map(new_text)
    nsf = SceneFile(new_scene)

    # a "release" that holds this mod's own edits (the OverLlm files as an 0.2/0.3 install left them) would silently turn our
    # revisions into base text and drop them from the shipped delta
    held = []
    for layer, newmap in (("table", new_tt), ("scene", nsf.map)):
        for s in inp.sentinels.get(layer, []):
            v = newmap.get(s["key"])
            if v is not None and text_hash(v) == s["revised_sha1"]:
                held.append("%s %s" % (layer, s["key"]))
    if held:
        raise PublishError("the new base holds this mod's own pre-0.4 edits (%d sentinels, e.g. %s): it is an OverLlm install "
                           "edited in place by an older LOM_UI_EN, not a release. Rebase onto the release's own files "
                           "(its Mods/English/StringTable.csv and _AutoGeneratedTranslations.txt)" % (len(held), held[:3]))

    old_tt = inp.base_tt
    conflicts_t = sorted(k for k in t_delta if k in old_tt and k in new_tt and new_tt[k] != old_tt[k] and new_tt[k] != t_delta[k])
    # reviewed rows that were OverLlm's line and still are were never revisions: not "converged"
    converged_t = sorted(k for k in t_delta if new_tt.get(k) == t_delta[k] and old_tt.get(k) != t_delta[k])
    reviewed_changed_t = [k for k in conflicts_t if old_tt.get(k) == t_delta[k]]
    upstream_removed_t = sorted(k for k in t_delta if k in old_tt and k not in new_tt)
    upstream_added_t = sorted(k for k in t_delta if k not in old_tt and k in new_tt and new_tt[k] != t_delta[k])
    flowing_t = sorted(k for k in new_tt if k not in t_delta and old_tt.get(k) != new_tt[k])
    dropped_t = sorted(k for k in old_tt if k not in new_tt and k not in t_delta)
    old = inp.base_scene
    conflicts_s = sorted(k for k in list(s_lines) + sorted(s_tomb) if k in old.map and k in nsf.map and nsf.map[k] != old.map[k]
                         and (k in s_tomb or nsf.map[k] != decode(s_lines[k])[1]))
    converged_s = sorted(k for k, l in s_lines.items() if nsf.map.get(k) == decode(l)[1] and old.map.get(k) != decode(l)[1])
    reviewed_changed_s = [k for k in conflicts_s if k in s_lines and old.map.get(k) == decode(s_lines[k])[1]]
    if len(converged_t) + len(converged_s) > CONVERGED_LIMIT and not allow_converged:
        raise PublishError("%d of this mod's revisions (%d table rows, %d scene lines) equal the new base's text, e.g. %s: they "
                           "would stop being shipped. A release that adopted that many is unusual; make sure the new base is "
                           "the release's own files (not an install holding this mod's edits), then rerun with "
                           "--allow-converged" % (len(converged_t) + len(converged_s), len(converged_t), len(converged_s),
                                                  (converged_t + converged_s)[:3]))
    upstream_removed_s = sorted(k for k in s_lines if k in old.map and k not in nsf.map)
    obsolete_tombs = sorted(k for k in s_tomb if k not in nsf.map)
    flowing_s = sorted(k for k in nsf.map if k not in s_lines and k not in s_tomb and old.map.get(k) != nsf.map[k])

    parts, pos, done = [], 0, set()
    nl = "\r\n" if "\r\n" in new_text else "\n"
    for fields, s, e in idea_row_spans(new_text):
        k = fields[0] if fields else ""
        if k in t_delta and k not in done and len(fields) >= 2 and not _skipped_key(k):
            done.add(k)
            parts.append(new_text[pos:s])
            term = new_text[s:e][len(new_text[s:e].rstrip("\r\n")):] or nl
            parts.append(table_serialize([(k, t_delta[k])])[:-2] + term)
            pos = e
    parts.append(new_text[pos:])
    body = "".join(parts)
    if body and not body.endswith("\n"):
        body += nl
    tail = [(k, v) for k, v in t_delta.items() if k not in done]
    body += table_serialize(tail).replace("\r\n", nl) if nl != "\r\n" else table_serialize(tail)
    ws_bom = inp.ws_table_bytes.startswith(UTF8_BOM)
    new_ws_table = (UTF8_BOM if ws_bom else b"") + body.encode("utf-8")
    nt, _ = table_map(body)
    exp = dict(new_tt)
    exp.update(t_delta)
    bad = [k for k in set(nt) | set(exp) if nt.get(k) != exp.get(k)]
    if bad:
        raise PublishError("rebased table check failed for %d keys (e.g. %s)" % (len(bad), sorted(bad)[:3]))

    # wording overrides whose text is now both the new release's line and our table row change nothing: they go too
    new_strings, sdec = datafix_overrides(inp.ws_strings, new_tt, exp, noop_only=True)
    new_strings = {n: b for n, b in new_strings.items() if b != inp.ws_strings[n]}

    # scene: the new release's lines, our line in place of the first line of each key we have (later ones dropped), the keys
    # we removed left out, our lines it lacks appended
    out_lines, placed = [], set()
    line_keys = {}
    for k, v, i in nsf.entries:
        line_keys.setdefault(i, k)
    for i, line in enumerate(nsf.lines):
        k = line_keys.get(i)
        if k is not None and k in s_tomb:
            continue
        if k is not None and k in s_lines:
            if k not in placed:
                placed.add(k)
                out_lines.append(s_lines[k])
            continue
        out_lines.append(line)
    out_lines += [l for k, l in s_lines.items() if k not in placed]
    new_ws_scene = inp.ws_scene.serialize(out_lines)
    chk = SceneFile(data=new_ws_scene, label="rebased workspace scene")
    ov = sim_overlay(nsf.entries, [(k, decode(l)[1], 0) for k, l in s_lines.items()] + [(k, "", 0) for k in sorted(s_tomb)])
    wl = sim_load(chk.entries)
    bad = [k for k in set(ov.map) | set(wl.map) if ov.map.get(k) != wl.map.get(k)]
    if bad:
        raise PublishError("rebased scene check failed for %d keys (e.g. %r)" % (len(bad), sorted(bad)[:3]))

    say("table: %d revised rows kept; OverLlm changed or added %d rows this mod did not revise (taken over), removed %d; conflicts "
        "(OverLlm changed a row this mod revised; this mod's text kept): %d; now equal to OverLlm (no longer revisions): %d; "
        "removed upstream (kept): %d; added upstream with other text (kept ours): %d; rows repaired in the new release: %d" % (
            len(t_delta), len(flowing_t), len(dropped_t), len(conflicts_t), len(converged_t), len(upstream_removed_t),
            len(upstream_added_t), len(repaired)))
    for k in conflicts_t[:50]:
        say("   conflict table %s\n      old OverLlm: %s\n      new OverLlm: %s\n      kept ours:   %s" % (
            k, old_tt[k][:90], new_tt[k][:90], t_delta[k][:90]))
    say("scene: %d revised lines + %d removals kept; OverLlm changed or added %d lines this mod did not touch (taken over); conflicts: %d; "
        "now equal to OverLlm: %d; removed upstream (kept): %d; removals no longer needed: %d" % (
            len(s_lines), len(s_tomb), len(flowing_s), len(conflicts_s), len(converged_s), len(upstream_removed_s), len(obsolete_tombs)))
    for k in conflicts_s[:50]:
        say("   conflict scene %r\n      old OverLlm: %r\n      new OverLlm: %r\n      kept ours:   %r" % (
            k[:60], old.map[k][:80], nsf.map[k][:80], "(removed)" if k in s_tomb else decode(s_lines[k])[1][:80]))
    if uses_ledger(inp):
        n_rt = sum(1 for k, v in t_delta.items() if old_tt.get(k) == v)
        n_rs = sum(1 for k, l in s_lines.items() if old.map.get(k) == decode(l)[1])
        say("review ledger: %d table rows and %d scene lines the review kept as OverLlm's line are kept as this mod's text "
            "(counted above); OverLlm changed %d of those rows and %d of those lines in this release (listed among the "
            "conflicts, the reviewed text kept: review the new line, and take it over by hand where it is better)" % (
                n_rt, n_rs, len(reviewed_changed_t), len(reviewed_changed_s)))
    say("strings: %d wording overrides removed because the new release's line and our table row both equal their text now "
        "(they changed nothing)" % len(sdec))

    # the revision record: every row this rebase keeps as a revision keeps the hashes it was revised against (a record is
    # saved for rows revised since the last build too, from the dump and release they were revised with)
    recs, rtags, rnotes = make_records_full(inp, ship_rows(inp), keep)
    rec_bytes = records_bytes(recs, rtags)
    kept_rows = {k for k in t_delta if new_tt.get(k) != t_delta[k]}
    fb_rows, fb_now, fb_earlier, fb_unarmed = [], 0, 0, []
    for k, s, b, o, how in recs:
        if k not in kept_rows or k not in new_tt:
            continue
        if b != "-" and b == text_hash(new_tt[k]):
            continue
        # the plugin's fallback serves the new release's line for this row once the game text differs from the recorded one
        (fb_rows if s != "-" else fb_unarmed).append(k)
        if k in conflicts_t:
            fb_now += 1
        else:
            fb_earlier += 1
    say("revision record: %d rows kept with the hashes they were revised against. For %d of them the new release's line is not "
        "the OverLlm line they were revised against (%d changed in this release: the conflicts above; %d earlier or added "
        "upstream): for these rows the plugin's fallback ([Translation] FallBackToOriginal) serves OverLlm's new line once "
        "the game text changed since they were revised, while the revision record is trusted (under 70%% of sampled rows "
        "changed) (%d of them have a game-text hash; %d have none yet, so the fallback cannot tell for them until "
        "publish.py source records it). Re-revise them against the new line to renew their record, or re-record one that "
        "still fits with publish.py source DUMP --restamp KEYS." % (
            len(kept_rows & {r[0] for r in recs}), len(fb_rows) + len(fb_unarmed), fb_now, fb_earlier, len(fb_rows),
            len(fb_unarmed)))
    for k in (fb_rows + fb_unarmed)[:50]:
        say("   fallback row %s%s" % (k, "" if k in fb_rows else " (no game-text hash yet)"))
    if len(fb_rows) + len(fb_unarmed) > 50:
        say("   ... %d more (all in the backup's rebase_fallback_rows.tsv)" % (len(fb_rows) + len(fb_unarmed) - 50))
    result = {"table_conflicts": conflicts_t, "scene_conflicts": conflicts_s, "table_converged": converged_t,
              "scene_converged": converged_s, "table_flowing": flowing_t, "scene_flowing": flowing_s,
              "table_upstream_removed": upstream_removed_t, "scene_upstream_removed": upstream_removed_s,
              "obsolete_tombstones": obsolete_tombs, "strings_noop_removed": [(f, k) for f, k, a, w, t in sdec],
              "fallback_rows": fb_rows, "fallback_rows_without_game_hash": fb_unarmed,
              "rerecorded_out_of_date": [x[0] for x in rnotes["stale"]],
              "table_reviewed_changed_upstream": reviewed_changed_t, "scene_reviewed_changed_upstream": reviewed_changed_s}
    if dry_run:
        for note in (recovery_note(rrep, saved=False), keep_note(recs, rnotes)):
            if note:
                say("dry run: " + note)
        if rnotes["stale"]:
            say("dry run: %d rows changed since the last build were out of date (their game text changed since they were "
                "revised) and would be re-recorded against today's game text and OverLlm line (e.g. %s); --keep-record "
                "keeps their old record" % (len(rnotes["stale"]), [x[0] for x in rnotes["stale"][:5]]))
        say("dry run; nothing written")
        return result
    if game_running():
        raise PublishError(GAME_RUNNING_MSG)
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    for p in (P.WORKSPACE, P.BASE_REF, P.BASE_REF + ".rebase-new"):
        P.guard_write(p)
    bdir = P.new_backup_dir("rebase", stamp)
    # by long paths: the backup nests base_ref/history/NN-<stamp>/ one level deeper than the folders it copies
    shutil.copytree(long_path(P.WORKSPACE), long_path(P.out_path(bdir, "workspace")))
    shutil.copytree(long_path(P.BASE_REF), long_path(P.out_path(bdir, "base_ref")))
    say("backup ->", bdir)
    if new_layout:
        # the per-file tables as the one StringTable.csv BASE_REF keeps (write_base_ref copies it from here)
        new_table = P.out_path(bdir, "new_release_StringTable.csv")
        with open(new_table, "wb") as f:
            f.write(new_text_raw.encode("utf-8"))
    with open(P.out_path(bdir, "rebase_fallback_rows.tsv"), "w", encoding="utf-8", newline="\n") as f:
        f.write("key\tgame_text_hash\trevised_against_overllm_hash\tnew_release_line_hash\tchanged_in_this_release\n")
        recd = {x[0]: x for x in recs}
        for k in fb_rows + fb_unarmed:
            r = recd[k]
            f.write("%s\t%s\t%s\t%s\t%s\n" % (k, r[1], r[2], text_hash(new_tt[k]), "yes" if k in conflicts_t else "no"))
    rlisting = None
    if rnotes["stale"]:
        rlisting = P.out_path(bdir, RERECORD_LISTING)
        write_rerecord_listing(rlisting, rnotes["stale"], "publish.py rebase %s" % stamp)
    # stage the complete new BASE_REF and the new workspace files, then swap them in; the lock goes last, so a crash in
    # between leaves a workspace/BASE_REF pair that Inputs refuses instead of one that ships OverLlm's text as ours
    new_ref, old_ref = P.BASE_REF + ".rebase-new", P.BASE_REF + ".rebase-old"
    new_dir_scene = os.path.dirname(new_scene)
    resize = {f: os.path.join(new_dir_scene, f) for f in os.listdir(new_dir_scene) if _is_resizer(f)}
    if not resize and os.path.isdir(P.BASE_REF_RESIZE_DIR):
        resize = {f: os.path.join(P.BASE_REF_RESIZE_DIR, f) for f in os.listdir(P.BASE_REF_RESIZE_DIR) if _is_resizer(f)}
    write_base_ref(new_table, new_scene, "rebased %s from %s" % (stamp, new_dir), release="OverLlm release from %s" % new_dir,
                   dest=new_ref, resize=resize, game_source=P.BASE_REF_SOURCE if os.path.exists(P.BASE_REF_SOURCE) else None,
                   old_info=inp.base_info, history_from=P.BASE_REF, stamp=stamp)
    lock = lock_data(file_bytes(os.path.join(new_ref, "StringTable.csv")), file_bytes(os.path.join(new_ref, "_AutoGeneratedTranslations.txt")))
    tmps = []
    for path, data in ((P.TABLE, new_ws_table), (P.SCENE, new_ws_scene)):
        P.assert_writable(path)
        with open(path + ".rebase-tmp", "wb") as f:
            f.write(data)
        tmps.append(path)
    for name, data in new_strings.items():
        p = P.guard_write(os.path.join(P.STRINGS_DIR, name))
        with open(p + ".rebase-tmp", "wb") as f:
            f.write(data)
    save_ws_records(rec_bytes, P.WS_RECORD + ".rebase-tmp")
    if sdec:
        with open(P.out_path(bdir, "strings_noop_removed.tsv"), "w", encoding="utf-8", newline="\n") as f:
            f.write("".join("%s\t%s\t%s\n" % (fn, k, w) for fn, k, a, w, t in sdec))
    write_lock(P.guard_write(P.WS_LOCK + ".rebase-tmp"), lock)
    os.rename(P.BASE_REF, old_ref)
    os.rename(new_ref, P.BASE_REF)
    for path in tmps + [os.path.join(P.STRINGS_DIR, n) for n in new_strings] + [P.WS_RECORD]:
        os.replace(path + ".rebase-tmp", path)
    os.replace(P.WS_LOCK + ".rebase-tmp", P.WS_LOCK)
    _rmtree(long_path(old_ref))         # by long paths: its history/NN-<stamp>/ files can pass 260 characters in a stage
    for note in (recovery_note(rrep), keep_note(recs, rnotes)):
        if note:
            say(note)
    if rnotes["stale"]:
        say(stale_note(rnotes["stale"], rlisting, tool_cmd("build")))
        result["rerecord_listing"] = rlisting
    say("workspace and BASE_REF rebased; now run: python %s build" % os.path.join(P.TOOLS, "publish.py"))
    return result


BASE_REF_README = """OverLlm English patch release this mod's workspace diverged from (the BASE REFERENCE)

  StringTable.csv                 the release's Mods/English/StringTable.csv
  _AutoGeneratedTranslations.txt  the release's BepInEx/Translation/en/Text/_AutoGeneratedTranslations.txt
  resize/*.resizer.txt            the release's resizers (to tell this mod's own resizers from the patch's)
  game_source.tsv                 the game's own Chinese per key (a harness 'srcdump'; publish.py source), when present
  base_ref.json                   where these came from, their md5s

This is the work of the OverLlm English patch's author (github.com/joshfreitas1984/LegendOfMortalOverLlm), kept here only so
that LOM_Localization/tools/publish.py can tell this mod's own revisions (LOM_Localization/workspace) from the patch's text.
NEVER SHIP THESE FILES and never copy them into BepInEx/plugins/LOM_UI_EN: the plugin reads the patch's own installed files at
run time. Do not edit them either; publish.py rebase replaces them when a newer release comes out, together with the
workspace (workspace/base_ref.lock records which release the workspace is aligned with).
"""


def write_base_ref(table_src, scene_src, how, release=None, dest=None, resize=None, game_source=None, old_info=None,
                   history_from=None, stamp=None):
    """A complete BASE_REF folder at dest (default BASE_REF): the two release files, resize/ (name -> source path),
    game_source.tsv (copied from game_source when given), README.txt and base_ref.json; with history_from (the BASE_REF being
    replaced), history/ holds its history and then that release itself (history/NN-<stamp>/: StringTable.csv,
    _AutoGeneratedTranslations.txt, base_ref.json), so the wording of every earlier release stays known as OverLlm's. Writes
    only inside dest. Every file operation goes by long paths (history/NN-<stamp>/ nests the release files deep enough to pass
    260 characters in a staging root)."""
    dest = long_path(P.guard_write(dest or P.BASE_REF))
    history_from = long_path(history_from) if history_from else None
    os.makedirs(dest, exist_ok=True)
    hist_info = []
    if history_from:
        hsrc, hdst = os.path.join(history_from, "history"), os.path.join(dest, "history")
        if os.path.isdir(hsrc):
            shutil.copytree(hsrc, hdst, dirs_exist_ok=True)
        os.makedirs(hdst, exist_ok=True)
        n = len([d for d in os.listdir(hdst) if os.path.isdir(os.path.join(hdst, d))]) + 1
        name = "%02d-%s" % (n, stamp or datetime.datetime.now().strftime("%Y%m%d-%H%M%S"))
        hd = os.path.join(hdst, name)
        os.makedirs(hd)
        for f in ("StringTable.csv", "_AutoGeneratedTranslations.txt", "base_ref.json"):
            if os.path.exists(os.path.join(history_from, f)):
                shutil.copyfile(os.path.join(history_from, f), os.path.join(hd, f))
        for d in sorted(os.listdir(hdst)):
            if os.path.isdir(os.path.join(hdst, d)):
                hist_info.append({"dir": "history/" + d,
                                  "StringTable.csv": md5_file(os.path.join(hdst, d, "StringTable.csv")),
                                  "_AutoGeneratedTranslations.txt": md5_file(os.path.join(hdst, d, "_AutoGeneratedTranslations.txt"))})
    t_dst, s_dst = os.path.join(dest, "StringTable.csv"), os.path.join(dest, "_AutoGeneratedTranslations.txt")
    for src, dst in ((table_src, t_dst), (scene_src, s_dst)):
        shutil.copyfile(src, dst + ".tmp")
        os.replace(dst + ".tmp", dst)
    rinfo = {}
    if resize:
        rd = os.path.join(dest, "resize")
        os.makedirs(rd, exist_ok=True)
        for name, src in sorted(resize.items()):
            shutil.copyfile(src, os.path.join(rd, name))
            rinfo[name] = {"from": src, "md5": md5_file(src)}
    if game_source:
        shutil.copyfile(game_source, os.path.join(dest, "game_source.tsv"))
    with open(os.path.join(dest, "README.txt"), "w", encoding="utf-8", newline="\r\n") as f:
        f.write(BASE_REF_README)
    old = old_info if old_info is not None else {}
    if old_info is None and os.path.exists(os.path.join(dest, "base_ref.json")):
        with open(os.path.join(dest, "base_ref.json"), encoding="utf-8") as f:
            old = json.load(f)
    info = {"release": release or old.get("release", ""), "how": how, "created": datetime.datetime.now().isoformat(timespec="seconds"),
            "files": {os.path.basename(p): {"from": s, "md5": md5_file(p), "size": os.path.getsize(p)}
                      for s, p in ((table_src, t_dst), (scene_src, s_dst))},
            "resize": rinfo,
            "history": old.get("history", []) + ([{"how": old.get("how"), "files": old.get("files")}] if old else [])}
    if hist_info:
        info["history_dirs"] = hist_info
    if game_source and old.get("game_source"):
        info["game_source"] = old["game_source"]
    with open(os.path.join(dest, "base_ref.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(info, f, indent=1, ensure_ascii=False)


# ---- the game's own text ----------------------------------------------------------------------------------------------------

def _restamp_keys(spec):
    """--restamp KEYS: 'all', comma-separated table keys, or @FILE with one key per line (a line's text up to its first TAB,
    so source's game_text_changed.tsv can be given, filtered or not; '#' lines and a 'key' header line are skipped)."""
    if spec is None:
        return None
    if spec == "all":
        return "all"
    if spec.startswith("@"):
        out = set()
        with open(spec[1:], encoding="utf-8-sig") as f:
            for l in f:
                k = l.rstrip("\r\n").split(TAB)[0].strip()
                if k and not k.startswith("#") and k != "key":
                    out.add(k)
        return out
    return {k.strip() for k in spec.split(",") if k.strip()}


GAME_TEXT_CHANGED = "game_text_changed.tsv"
REVISED_BEFORE_SOURCE = "revised_before_source.tsv"
LISTING_HEAD = ("key\tgroup\twhat the plugin does\trecorded_game_text_hash\tnew_game_text_hash\trevised_against_overllm_hash\t"
                "current_overllm_hash\trecorded\n")


def source(path, verbose=True, restamp=None, game_build=None, rerecord=False, keep_record=None):
    """Check a harness 'srcdump' and make it base_ref/game_source.tsv (a game update: a new dump is accepted), then build.

    Without restamp no existing record is changed here: a record with no game-text hash yet ('-') is filled from the dump, and a row
    whose recorded game text differs from the dump's keeps its recorded hash, which is what the plugin's fallback compares
    (TextTable.Decide): with OverLlm's line changed since the row was revised the plugin serves OverLlm's line, otherwise it
    keeps this mod's text and reports it as needing revision. Those rows are listed (printed, and written to
    backups/source-<stamp>/game_text_changed.tsv with the backup of what this replaces). A row re-revised later is recorded
    by build against this dump and today's BASE_REF. restamp ('all' or a set of keys) re-records rows without a value
    change, from this dump and today's BASE_REF line (the maintainer confirms the revision still fits): they stop falling
    back. Rows revised since the last build are recorded first against the dump in use until now (they were written with
    it). The rows a build recorded against the dump this replaces since that dump was installed (revised before this
    source ran: the record's fifth field) whose game text this dump changes are listed on their own (group
    revised_before_source, and backups/source-<stamp>/revised_before_source.tsv), with the exact --restamp command that
    confirms them as revisions for the new game text."""
    keep = parse_keep_record(keep_record)
    data = file_bytes(path)
    m, probs = parse_game_source(data)
    if probs:
        raise PublishError("%s is not a srcdump: %s" % (path, "; ".join(probs[:5])))
    ws_tt, _ = table_map(read_text(P.TABLE))
    cover = len(set(m) & set(ws_tt))
    if len(m) < 1000 or cover < 0.5 * len(ws_tt):
        raise PublishError("%s has %d keys, %d of the workspace table's %d: not the game's full text table" % (path, len(m), cover, len(ws_tt)))
    cjk = sum(1 for v in m.values() if _CJK.search(v))
    if cjk < 0.5 * len(m):
        raise PublishError("%s: only %d of %d values hold Chinese; the dump must be taken with the game's Chinese text (the "
                           "harness 'srcdump' reads Lean's own translations)" % (path, cjk, len(m)))
    if not os.path.isdir(P.BASE_REF):
        raise PublishError("%s is missing" % P.BASE_REF)
    if game_build is not None and not _FP.match(game_build):
        raise PublishError("--game-build %r is not a 12-digit build id (the plugin's Compatibility page shows it)" % game_build)
    fp = game_build or game_fingerprint()
    inp = Inputs()
    rrep = ensure_records(inp, P.PLUGIN, rerecord)
    prev_tag = inp.game_src_tag           # the dump this replaces (None: the first one)
    # every shipped row's record as it stands (a row revised since the last build is recorded against the text it was
    # revised with, i.e. the dump in use until now)
    recs0, tags, rnotes = make_records_full(inp, ship_rows(inp), keep)
    recs = [list(r) for r in recs0]
    pending = [r[0] for r in recs if r[4] != "kept"]
    want = _restamp_keys(restamp)
    shipped = {r[0] for r in recs}
    ignored = sorted(want - shipped) if isinstance(want, set) else []
    filled, restamped, restamp_same, missing, unrecorded = [], [], [], [], []
    for r in recs:
        k = r[0]
        if want == "all" or (want and k in want):
            s = text_hash(m[k]) if k in m else "-"
            b = text_hash(inp.base_tt[k]) if k in inp.base_tt else "-"
            (restamped if (s, b) != (r[1], r[2]) else restamp_same).append(k)
            r[1], r[2] = s, b
            tags.pop(k, None)             # confirmed for this dump by the maintainer: no longer "revised before source"
            continue
        if k not in m:
            (missing if r[1] != "-" else unrecorded).append(k)
        elif r[1] == "-":
            r[1] = text_hash(m[k])
            filled.append(k)
    final = [tuple(r) for r in recs]
    fv = fallback_view(inp, final, m)
    # the rows a build recorded against the dump this replaces, since that dump was installed (revised before this source
    # ran, most likely for the new game text): listed on their own, with the command that confirms them
    rbs_all = {k for k, t in tags.items() if prev_tag and t == prev_tag}
    rbs = [k for k in fv["overllm"] + fv["stale"] + fv["no_line"] + fv["placeholders"] if k in rbs_all]
    rbs_set = set(rbs)
    rbs_plugin = {}
    for g in ("overllm", "stale", "no_line", "placeholders"):
        for k in fv[g]:
            if k in rbs_set:
                rbs_plugin[k] = g
        fv[g] = [k for k in fv[g] if k not in rbs_set]
    changed = fv["overllm"] + fv["stale"] + fv["no_line"]
    trust = record_trust(final, m)
    new_bytes = records_bytes([r[:4] + ("kept",) for r in final], tags)

    # back up what this replaces, and write the list of the rows whose game text changed next to it
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    bdir = P.new_backup_dir("source", stamp)
    for src, name in ((P.BASE_REF_SOURCE, "game_source.tsv"), (P.WS_RECORD, "revision_record.tsv"),
                      (P.BASE_REF_INFO, "base_ref.json")):
        if os.path.exists(src):
            shutil.copy2(src, P.out_path(bdir, name))
    recd, pend = {r[0]: r for r in final}, set(pending)
    plugin_does = {"overllm": "the plugin serves OverLlm's newer line while the revision record is trusted",
                   "stale": "no newer OverLlm line: the plugin keeps this mod's text and reports it as out of date",
                   "no_line": "OverLlm has no line for the key: the plugin keeps this mod's text and reports it as out of date",
                   "placeholders": "this mod's line uses a placeholder the new game text no longer has: the plugin serves "
                                   "OverLlm's line"}
    groups = (("overllm_newer", fv["overllm"], "OverLlm has a newer line than the one revised against: the plugin serves "
                                                "OverLlm's line while the revision record is trusted"),
              ("needs_revising", fv["stale"], "no newer OverLlm line: this mod's text is kept, needs revising"),
              ("needs_revising", fv["no_line"], "OverLlm has no line for the key: this mod's text is kept, needs revising"),
              ("overllm_placeholders", fv["placeholders"], "this mod's line uses a placeholder the new game text no longer has "
                                                            "and OverLlm's fits: the plugin serves OverLlm's line"),
              ("revised_before_source", rbs, None))

    def row_line(k, g, what):
        r = recd[k]
        return "%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n" % (
            k, g, what, r[1], text_hash(m[k]), r[2], text_hash(inp.base_tt[k]) if k in inp.base_tt else "-",
            "now, against the previous dump (revised since the last build)" if k in pend else
            "by a build since the previous dump was installed (revised before this source ran)" if k in rbs_set else
            "when revised")

    def rbs_what(k):
        return ("revised before this source ran and recorded against the previous dump, whose game text this dump changes: "
                "if it was revised for the new game text, confirm it with --restamp; until then " + plugin_does[rbs_plugin[k]])

    listing = P.out_path(bdir, GAME_TEXT_CHANGED)
    with open(listing, "w", encoding="utf-8", newline="\n") as f:
        f.write(LISTING_HEAD)
        for g, keys, what in groups:
            for k in keys:
                f.write(row_line(k, g, what or rbs_what(k)))
    rbs_listing = None
    if rbs:
        rbs_listing = P.out_path(bdir, REVISED_BEFORE_SOURCE)
        with open(rbs_listing, "w", encoding="utf-8", newline="\n") as f:
            f.write(LISTING_HEAD)
            for k in rbs:
                f.write(row_line(k, "revised_before_source", rbs_what(k)))
    rlisting = None
    if rnotes["stale"]:
        rlisting = P.out_path(bdir, RERECORD_LISTING)
        write_rerecord_listing(rlisting, rnotes["stale"], "publish.py source %s" % stamp)

    # the record first: once game_source.tsv is replaced, a row without a saved record would be recorded against the new dump
    save_ws_records(new_bytes)
    for note in (recovery_note(rrep), keep_note(recs0, rnotes)):
        if note:
            say(note)
    if rnotes["stale"]:
        say(stale_note(rnotes["stale"], rlisting, tool_cmd("build")))
    tmp = P.guard_under(P.BASE_REF_SOURCE + ".tmp", P.BASE_REF)
    with open(tmp, "wb") as f:
        f.write(data)
    os.replace(tmp, P.guard_under(P.BASE_REF_SOURCE, P.BASE_REF))
    info = {}
    if os.path.exists(P.BASE_REF_INFO):
        with open(P.BASE_REF_INFO, encoding="utf-8") as f:
            info = json.load(f)
    info["game_source"] = {"from": P.real(path), "md5": md5_bytes(data), "size": len(data), "keys": len(m),
                           "copied": datetime.datetime.now().isoformat(timespec="seconds"), "game_build": fp or "unknown",
                           "game_build_from": "--game-build" if game_build else ("the installed game" if fp else "unknown")}
    with open(P.guard_under(P.BASE_REF_INFO, P.BASE_REF), "w", encoding="utf-8", newline="\n") as f:
        json.dump(info, f, indent=1, ensure_ascii=False)
    say("game_source.tsv <- %s (%d keys, %d of the workspace table's keys, %d with Chinese; game build %s); what it replaces "
        "(the previous dump, the workspace record, base_ref.json) is backed up in %s" % (path, len(m), cover, cjk,
                                                                                         fp or "unknown", bdir))
    rs = set(restamped) | set(restamp_same) | set(filled)
    gt_changed = [r[0] for r in final if r[0] in m and r[1] != "-" and r[0] not in rs and text_hash(m[r[0]]) != r[1]]
    confirmed = sum(1 for r in final if r[0] in m and r[1] != "-" and r[0] not in rs and text_hash(m[r[0]]) == r[1])
    say("revision record: %d rows; this dump changes none of the recorded hashes: %d game-text hashes filled (first "
        "capture), %d recorded hashes the dump confirms, %d rows whose game text changed since they were revised (their "
        "recorded hash kept, so the plugin sees the change), %d kept although the dump lacks the key, %d still without a "
        "game-text hash (the dump lacks the key)%s" % (
            len(recs), len(filled), confirmed, len(gt_changed), len(missing), len(unrecorded),
            "; %d re-recorded with --restamp (%d of them already matched)" % (len(restamped) + len(restamp_same),
                                                                            len(restamp_same)) if want else ""))
    if changed:
        say("rows whose game text changed since they were revised (the list: %s):" % listing)
        say("  %d: OverLlm has a newer line -> the plugin serves OverLlm's line for them while the revision record is trusted "
            "(under 70%% of sampled rows changed)" % len(fv["overllm"]))
        say("  %d: no newer OverLlm line -> this mod's text is kept, needs revising%s" % (
            len(fv["stale"]) + len(fv["no_line"]), " (%d of them: OverLlm has no line for the key)" % len(fv["no_line"])
            if fv["no_line"] else ""))
        for g, keys, what in groups[:3]:
            for k in keys[:25]:
                say("   %-15s %s" % (g, k))
            if len(keys) > 25:
                say("   %-15s ... %d more (in the list)" % (g, len(keys) - 25))
        say("  re-revise a row to renew its record (build records it against this dump and today's OverLlm line); for a "
            "row whose revision still fits the new game text as it is: publish.py source %s --restamp KEYS|@FILE (it then "
            "stops falling back)" % P.BASE_REF_SOURCE)
    if fv["placeholders"]:
        say("  %d rows use a placeholder the new game text no longer has: the plugin serves OverLlm's line for them (in the "
            "list, group overllm_placeholders)" % len(fv["placeholders"]))
    if rbs:
        c = collections.Counter(rbs_plugin[k] for k in rbs)
        say("rows revised BEFORE this source ran, since the previous dump was installed (a build recorded them against that "
            "dump), whose game text this dump changes: %d (group revised_before_source; the list: %s). Their record says "
            "they were revised for the old game text, so the plugin %s. The ones revised for the new game text (after the "
            "game updated, before this source ran; next time run source first, then revise) need confirming: run this, "
            "with the list or a filtered copy of it (the ones revised for the old text are out of date like the rows "
            "above: leave them out and re-revise them):" % (len(rbs), rbs_listing, ", ".join(s for s in (
                "serves OverLlm's newer line for %d" % c["overllm"] if c["overllm"] else "",
                "reports %d as out of date" % (c["stale"] + c["no_line"]) if c["stale"] + c["no_line"] else "",
                "serves OverLlm's line for %d (placeholders)" % c["placeholders"] if c["placeholders"] else "") if s)))
        say("    " + tool_cmd("source", _q(P.BASE_REF_SOURCE), "--restamp", _q("@" + rbs_listing)))
        for k in rbs[:25]:
            say("   %-21s %s" % ("revised_before_source", k))
        if len(rbs) > 25:
            say("   %-21s ... %d more (in the list)" % ("revised_before_source", len(rbs) - 25))
    pend_rec = [r[0] for r in recs0 if r[4] in ("new", "changed")]
    if pend_rec:
        say("  %d rows revised since the last build were recorded now against the previous dump (they were written with it)%s" % (
            len(pend_rec), "; the ones whose game text this dump changes are in the revised_before_source group above" if rbs
            else ""))
    say("  " + trust_note(trust))
    if restamped:
        say("  --restamp re-recorded %d rows against this dump's text and today's OverLlm line: they stop falling back (the "
            "plugin sees no game-text change for them until the game text changes again)" % len(restamped))
    if ignored:
        say("   --restamp keys that are not shipped rows (ignored): %s" % ignored[:20])
    res = build(verbose=verbose)
    res["source"] = {"filled": filled, "restamped": sorted(restamped), "restamp_unchanged": sorted(restamp_same),
                     "missing": missing, "unrecorded": unrecorded, "overllm_newer": fv["overllm"],
                     "needs_revising": fv["stale"] + fv["no_line"], "placeholders": fv["placeholders"], "trust": trust,
                     "listing": listing, "backup": bdir, "pending": pending, "revised_before_source": rbs,
                     "revised_before_source_listing": rbs_listing,
                     "rerecorded_out_of_date": [x[0] for x in rnotes["stale"]], "rerecord_listing": rlisting}
    return res


# ---- command line ---------------------------------------------------------------------------------------------------------

def _print_info(info):
    for k, v in info.items():
        say("  %-44s %s" % (k, v))


def main(argv=None):
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--stage", help="staging root (ROOT/plugin, ROOT/workspace, ROOT/base_ref, ROOT/backups)")
    sub = ap.add_subparsers(dest="cmd", required=True)
    b = sub.add_parser("build")
    b.add_argument("--target")
    b.add_argument("--force", action="store_true")
    b.add_argument("--no-check", action="store_true")
    b.add_argument("--rerecord", action="store_true", help="the workspace revision record is missing and some shipped rows "
                   "cannot be recovered from the shipped meta: record them afresh from today's game text and OverLlm line")
    keep_help = ("rows whose text changed keep their old revision record (the game text and OverLlm line they were revised "
                 "against) instead of being re-recorded as revisions for today's game text: comma-separated keys, @FILE (one "
                 "key per line, or a game_text_changed.tsv), all, or @ a rerecorded_out_of_date.tsv an earlier build wrote "
                 "(its records come back)")
    b.add_argument("--keep-record", metavar="KEYS|@FILE|all", help=keep_help)
    c = sub.add_parser("check")
    c.add_argument("--target")
    r = sub.add_parser("rebase")
    r.add_argument("new_base_dir")
    r.add_argument("--dry-run", action="store_true")
    r.add_argument("--allow-converged", action="store_true")
    r.add_argument("--keep-record", metavar="KEYS|@FILE|all", help=keep_help)
    s = sub.add_parser("source")
    s.add_argument("srcdump")
    s.add_argument("--restamp", metavar="KEYS|@FILE|all", help="re-record these rows (comma-separated keys, @FILE with one "
                   "key per line or a game_text_changed.tsv, or all) from this dump and today's OverLlm line without a value "
                   "change, confirming their revision still fits: they stop falling back")
    s.add_argument("--rerecord", action="store_true", help="as for build, when the workspace revision record is missing")
    s.add_argument("--game-build", metavar="ID", help="the game build the dump was taken from (default: the installed game's)")
    s.add_argument("--keep-record", metavar="KEYS|@FILE|all", help=keep_help)
    a = ap.parse_args(argv)
    if a.stage:
        try:
            P.stage(a.stage)
        except ValueError as e:
            say(str(e))
            return 2
    say("layout: workspace %s | base_ref %s | plugin %s" % (P.WORKSPACE, P.BASE_REF, P.PLUGIN))
    try:
        if a.cmd in ("build", "source"):
            res = (build(a.target, force=a.force, check_after=not a.no_check, rerecord=a.rerecord, keep_record=a.keep_record)
                   if a.cmd == "build" else
                   source(a.srcdump, restamp=a.restamp, game_build=a.game_build, rerecord=a.rerecord,
                          keep_record=a.keep_record))
            _print_info(res["info"])
            for c in res["conflicts_overwritten"]:
                say("  overwritten (--force):", c)
            return 0
        if a.cmd == "check":
            probs, info = check(a.target)
            _print_info(info)
            for p in probs:
                say("FAIL:", p)
            say("check %s: %s" % (P.real(a.target or P.PLUGIN), "FAILED (%d problems)" % len(probs) if probs else "ok"))
            return 1 if probs else 0
        rebase(a.new_base_dir, dry_run=a.dry_run, allow_converged=a.allow_converged, keep_record=a.keep_record)
        return 0
    except PublishError as e:
        say("publish.py %s: %s" % (a.cmd, e))
        return 2
    except OSError as e:
        say("publish.py %s: %s" % (a.cmd, e))
        return 2


if __name__ == "__main__":
    sys.exit(main())
