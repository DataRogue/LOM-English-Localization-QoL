"""The review ledger: the lines a line-by-line review pass has reviewed (owner ruling 2026-09-24: the mod must play without
OverLlm, so a reviewed line ships even when its text stays OverLlm's own line; a line neither reviewed nor revised does not).

  workspace/translation/reviewed.tsv               table keys (lom_paths.WS_LEDGER)
  workspace/translation/scene/reviewed_scene.tsv   scene lines, by their raw XUnity key as written in the workspace scene file
                                                   (the line's text before its first unescaped '=', escapes as written:
                                                   scene_key(line)), and 'resize/<file>' for a resizer of this mod's own,
                                                   workspace/translation/scene/resize/<file> (e.g. a reviewed copy of one of
                                                   OverLlm's) (lom_paths.WS_SCENE_LEDGER)

Format: one entry per line, 'key TAB pass-name TAB date' (date YYYY-MM-DD), UTF-8 without a BOM, LF line ends, sorted by key
(code-point order), each key once. A key may hold a TAB (a scene key can: the last two TABs of a line separate the fields),
never a CR or LF; a pass name holds no TAB, CR or LF. An existing entry is kept as it is when a later pass adds the same key
(the first review is what the ledger records). publish.py reads both files: build ships every listed row and line (and
listed resizer) whatever its text, and check fails on a listed key the workspace does not have. Only this module writes
them (lom_paths.assert_ledger_writable: the two files, inside the workspace, and inside the stage in stage mode).

  import ledger
  ledger.add(keys, "fullpass-2026-09")                          # table keys
  ledger.add(raw_keys, "fullpass-2026-09", scene=True)          # scene lines: ledger.scene_key(line) is a line's raw key
  ledger.add([ledger.resize_entry("story.resizer.txt")], "resize-review", scene=True)
  table, scene = ledger.load()                                  # {key: (pass name, date)} each; load(scene=True) for one
  ledger.remove(keys, scene=False)                              # e.g. a line a later pass removed from the workspace

Command line (under LOM_STAGE, or with --stage ROOT, the stage's workspace):
  python ledger.py [--stage ROOT] add table|scene PASS KEY|@FILE [KEY|@FILE ...]   (@FILE: one key per line, taken exactly)
  python ledger.py [--stage ROOT] remove table|scene KEY|@FILE [...]
  python ledger.py [--stage ROOT] show        counts per pass
  python ledger.py [--stage ROOT] missing     the listed keys the workspace does not have, and the listed scene lines a
                                              later line for the same key overrides (what publish.py check fails on);
                                              one 'layer TAB key' per line
"""
import collections, datetime, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import lom_paths as P

TAB = chr(9)
LF = chr(10)
CR = chr(13)
BS = chr(92)
BOM = chr(0xFEFF)
RESIZE_PREFIX = "resize/"
_DATE = re.compile(r"^\d{4}-\d{2}-\d{2}$")


class LedgerError(Exception):
    pass


def path(scene=False):
    """The ledger file: WS_SCENE_LEDGER for scene lines, WS_LEDGER for table keys (both follow lom_paths.stage)."""
    return P.WS_SCENE_LEDGER if scene else P.WS_LEDGER


def scene_key(line):
    """The raw key of a scene line as written in the file: its text before the first unescaped '=' (the whole line when
    there is none), exactly as publish.raw_key reads it."""
    esc = False
    for i, c in enumerate(line):
        if esc:
            esc = False
        elif c == BS:
            esc = True
        elif c == "=":
            return line[:i]
    return line


def resize_entry(name):
    """The scene-ledger entry for this mod's own resizer workspace/translation/scene/resize/<name>."""
    if not is_resize_entry(RESIZE_PREFIX + name):
        raise LedgerError("%r is not a resizer file name (*resizer.txt, no folder)" % name)
    return RESIZE_PREFIX + name


def is_resize_entry(key):
    """key names a resizer ('resize/<file>resizer.txt'), not a scene line."""
    if not key.startswith(RESIZE_PREFIX):
        return False
    n = key[len(RESIZE_PREFIX):]
    return bool(n) and n.lower().endswith("resizer.txt") and "/" not in n and BS not in n and n not in (".", "..")


def parse(data):
    """A ledger file's bytes: ({key: (pass name, date)} in file order, [problems]). Problems: not UTF-8, a BOM, a CR, a line
    without its three fields, an empty key or pass name, a date that is not YYYY-MM-DD, a key listed twice, keys out of
    order, no LF at the end."""
    out, probs = collections.OrderedDict(), []

    def bad(msg):
        if len(probs) < 10:
            probs.append(msg)

    if data is None:
        return out, probs
    try:
        text = data.decode("utf-8")
    except UnicodeDecodeError as e:
        return out, ["not UTF-8: %s" % e]
    if text.startswith(BOM):
        bad("it starts with a BOM")
        text = text[1:]
    if CR in text:
        bad("it holds a CR (the format is LF only)")
    if text and not text.endswith(LF):
        bad("its last line has no LF")
    prev = None
    for n, line in enumerate(text.split(LF), 1):
        if line == "" and n == text.count(LF) + 1:
            break
        f = line.rstrip(CR).rsplit(TAB, 2)
        if len(f) != 3 or not f[0] or not f[1].strip() or not _DATE.match(f[2]):
            bad("line %d is not 'key TAB pass-name TAB YYYY-MM-DD'" % n)
            continue
        k = f[0]
        if k in out:
            bad("line %d repeats key %r" % (n, k[:60]))
            continue
        if prev is not None and k < prev:
            bad("line %d: key %r is out of order (the file is sorted by key)" % (n, k[:60]))
        prev = k
        out[k] = (f[1], f[2])
    return out, probs


def serialize(entries):
    """The file's bytes for {key: (pass name, date)}: sorted by key, 'key TAB pass TAB date' + LF each."""
    return "".join("%s%s%s%s%s%s" % (k, TAB, entries[k][0], TAB, entries[k][1], LF) for k in sorted(entries)).encode("utf-8")


def _read(scene):
    p = path(scene)
    if not os.path.exists(p):
        return collections.OrderedDict()
    with open(p, "rb") as f:
        data = f.read()
    entries, probs = parse(data)
    if probs:
        raise LedgerError("the review ledger %s is damaged: %s. It is written by ledger.py only; restore it from a backup, or "
                          "rewrite it from its readable lines (python ledger.py show names the problem)" % (p, "; ".join(probs[:3])))
    return entries


def load(scene=None):
    """The ledger: load() -> (table entries, scene entries); load(scene=False) or load(scene=True) -> one of them. Entries are
    {key: (pass name, date)}; a missing file is an empty ledger. Raises LedgerError for a damaged file."""
    if scene is None:
        return _read(False), _read(True)
    return _read(bool(scene))


def _write(scene, entries):
    p = P.assert_ledger_writable(path(scene))
    tmp = p + ".tmp"
    if P.is_link(tmp) or (os.path.isfile(tmp) and os.stat(tmp).st_nlink > 1):
        raise PermissionError("ledger: refusing to write %s: it is a link, junction or hard link" % tmp)
    P.guard_write(tmp)
    data = serialize(entries)
    with open(tmp, "wb") as f:
        f.write(data)
    os.replace(tmp, p)
    return p


def _check_keys(keys):
    keys = [keys] if isinstance(keys, str) else list(keys)
    for k in keys:
        if not isinstance(k, str) or not k:
            raise LedgerError("a ledger key must be a non-empty string (got %r)" % (k,))
        if CR in k or LF in k:
            raise LedgerError("a ledger key cannot hold a CR or LF: %r" % k[:60])
        if k.startswith(BOM):
            raise LedgerError("a ledger key cannot start with a BOM: %r" % k[:60])
    return keys


def is_asset_key(k):
    """A LeanPhrase asset row of the table (TextFont, Image_*, TextMeshFont_*): TextTable.Load and publish.table_map skip it."""
    return "/" not in k and (k == "TextFont" or k.startswith("Image_") or k.startswith("TextMeshFont_"))


def add(keys, pass_name, scene=False, date=None):
    """Record keys as reviewed by pass_name (on date, default today): table keys, or with scene=True raw scene keys
    (scene_key(line)) and resize entries (resize_entry(name)). Keys already listed keep their entry. Writes the ledger
    (atomically, through lom_paths.assert_ledger_writable) only when something was added; returns how many were added."""
    if not isinstance(pass_name, str) or not pass_name.strip() or any(c in pass_name for c in (TAB, CR, LF)):
        raise LedgerError("pass name %r: a non-empty name without TAB, CR or LF" % (pass_name,))
    date = date or datetime.date.today().isoformat()
    if not _DATE.match(date):
        raise LedgerError("date %r is not YYYY-MM-DD" % (date,))
    keys = _check_keys(keys)
    assets = [] if scene else [k for k in keys if is_asset_key(k)]
    if assets:
        raise LedgerError("%d table keys are asset rows (TextFont, Image_*, TextMeshFont_*) that TextTable.Load and publish.py "
                          "skip, so they never ship and cannot be listed, e.g. %r" % (len(assets), assets[:3]))
    entries = _read(scene)
    n = 0
    for k in keys:
        if k not in entries:
            entries[k] = (pass_name, date)
            n += 1
    if n:
        _write(scene, entries)
    return n


def remove(keys, scene=False):
    """Take keys out of the ledger (a line a later pass removed from the workspace); returns how many were listed."""
    keys = set(_check_keys(keys))
    entries = _read(scene)
    gone = [k for k in entries if k in keys]
    for k in gone:
        del entries[k]
    if gone:
        _write(scene, entries)
    return len(gone)


def _keys_arg(args):
    out = []
    for a in args:
        if a.startswith("@"):
            with open(a[1:], "rb") as f:
                data = f.read()
            text = data.decode("utf-8")
            if text.startswith(BOM):
                text = text[1:]
            for line in text.split(LF):
                line = line[:-1] if line.endswith(CR) else line
                if line:
                    out.append(line)
        else:
            out.append(a)
    return out


def main(argv=None):
    import argparse
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--stage", help="staging root (its workspace/ holds the ledger)")
    sub = ap.add_subparsers(dest="cmd", required=True)
    a_ = sub.add_parser("add")
    a_.add_argument("layer", choices=("table", "scene"))
    a_.add_argument("pass_name")
    a_.add_argument("keys", nargs="+")
    r_ = sub.add_parser("remove")
    r_.add_argument("layer", choices=("table", "scene"))
    r_.add_argument("keys", nargs="+")
    sub.add_parser("show")
    sub.add_parser("missing")
    a = ap.parse_args(argv)
    if a.stage:
        try:
            P.stage(a.stage)
        except ValueError as e:
            print(e)
            return 2
    try:
        if a.cmd == "add":
            n = add(_keys_arg(a.keys), a.pass_name, scene=a.layer == "scene")
            print("ledger %s: %d added (%s)" % (a.layer, n, path(a.layer == "scene")))
            return 0
        if a.cmd == "remove":
            n = remove(_keys_arg(a.keys), scene=a.layer == "scene")
            print("ledger %s: %d removed (%s)" % (a.layer, n, path(a.layer == "scene")))
            return 0
        if a.cmd == "show":
            code = 0
            for scene in (False, True):
                p = path(scene)
                data = open(p, "rb").read() if os.path.exists(p) else None
                entries, probs = parse(data)
                c = collections.Counter(v[0] for v in entries.values())
                nres = sum(1 for k in entries if scene and is_resize_entry(k))
                print("%s: %s, %d entries%s%s" % (p, "missing (empty)" if data is None else "%d bytes" % len(data), len(entries),
                                                  " (%d resizers)" % nres if nres else "",
                                                  "".join("\n  %-30s %d" % (k, v) for k, v in sorted(c.items()))))
                for pr in probs:
                    print("  PROBLEM:", pr)
                    code = 1
            return code
        import publish as PB
        try:
            inp = PB.Inputs()
        except PB.PublishError as e:
            print("ledger.py missing: the workspace cannot be read: %s" % e)
            return 2
        miss =[("table", k) for k in inp.led_table_missing] + [("scene", k) for k in inp.led_scene_missing] + [
            ("scene", RESIZE_PREFIX + n) for n in inp.led_resize_missing]
        for layer, k in miss:
            print("%s\t%s" % (layer, k))
        shadowed = getattr(inp, "led_scene_shadowed", [])
        for k in shadowed:
            print("scene-overridden\t%s" % k)
        print("%d listed keys the workspace does not have (python ledger.py remove table|scene @FILE takes them out; the key is "
              "everything after the first TAB of a line)%s" % (len(miss), "; %d listed scene lines a later line for the same key "
                                                               "overrides (scene-overridden)" % len(shadowed) if shadowed else ""),
              file=sys.stderr)
        return 1 if miss or shadowed else 0
    except (LedgerError, PermissionError, OSError) as e:
        print("ledger.py %s: %s" % (a.cmd, e))
        return 2


if __name__ == "__main__":
    sys.exit(main())
