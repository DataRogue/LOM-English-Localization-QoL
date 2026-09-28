"""The OverLlm patch's game data table in both of its layouts, read as the plugin reads them (BaseTables.cs).

  single    releases up to 2026.02: Mods/English/StringTable.csv in Binarizer's format (publish.table_map reads it)
  per-file  the llmkit-upgrade layout (release 2026.09.28 on): one CSV per game source file (Story_1.csv, System_zh-cn.csv, ...)
            in Mods/English, read by the patch's own plugin FanslationStudio.LegendOfMortal.Plugin: every *.csv of the folder in
            turn (StringTable.csv too), each whole (CsvUtility.ParseFile: a quoted value may span lines), the first two fields,
            "\\n" turned into a line break, a later row of a key replacing an earlier one. LOM_UI_EN leaves an older StringTable.csv
            next to them out. The folder also holds the patch's prefab and code text (*.yaml), which these tools do not read.

find(folder) -> Layout; read_per_file(files) -> ({key: value}, stats); single_text(map) -> the same table as one
StringTable.csv in Binarizer's format (what BASE_REF stores), which publish.table_map reads back to the same map.
"""
import collections, io, os, re

SINGLE = "StringTable.csv"
PER_FILE_NAME = re.compile(r"^(Story_[0-9]+|[A-Za-z0-9]+(_[A-Za-z0-9]+)*_(zh-cn|zh-tw|kr))\.csv$", re.I)

Layout = collections.namedtuple("Layout", "kind dir files leftover")


def find(folder):
    """The layout in folder: kind 'per-file' (files = every *.csv but StringTable.csv, in the plugin's order), 'single'
    (files = [StringTable.csv]) or None; leftover = a StringTable.csv next to per-file tables."""
    if not folder or not os.path.isdir(folder):
        return Layout(None, folder, [], None)
    single, per, known = None, [], False
    for name in os.listdir(folder):
        p = os.path.join(folder, name)
        if not name.lower().endswith(".csv") or not os.path.isfile(p):
            continue
        if name.lower() == SINGLE.lower():
            single = p
            continue
        per.append(p)
        known = known or bool(PER_FILE_NAME.match(name))
    if known:
        # Directory.GetFiles on NTFS: names sorted case-insensitively (OrdinalIgnoreCase in BaseTableSet.Find)
        per.sort(key=lambda p: os.path.basename(p).upper())
        return Layout("per-file", folder, per, single)
    if single:
        return Layout("single", folder, [single], None)
    return Layout(None, folder, [], None)


def parse_file(content):
    """CsvUtility.ParseFile of the patch's plugin (LlmKitCsv.ParseFile): every row of a file's text, as its fields."""
    fields, cur, q, i, n = [], [], False, 0, len(content)
    while i < n:
        c = content[i]
        if c == '"':
            if q and i + 1 < n and content[i + 1] == '"':
                cur.append('"')
                i += 2
                continue
            q = not q
            i += 1
            continue
        if c == "\r" and i + 1 < n and content[i + 1] == "\n":
            i += 1
            continue
        if q:
            cur.append("\n" if c == "\r" else c)
            i += 1
            continue
        if c == ",":
            fields.append("".join(cur))
            cur = []
            i += 1
            continue
        if c == "\n" or c == "\r":
            fields.append("".join(cur))
            cur = []
            if len(fields) > 1 or fields[0]:
                yield fields
            fields = []
            i += 1
            continue
        cur.append(c)
        i += 1
    fields.append("".join(cur))
    if len(fields) > 1 or fields[0]:
        yield fields


def _text(path):
    """File.ReadAllText: UTF-8 (a UTF-8 or UTF-16 byte order mark honoured; a bad byte read as U+FFFD, as StreamReader does)."""
    with open(path, "rb") as f:
        b = f.read()
    if b.startswith(b"\xef\xbb\xbf"):
        return b[3:].decode("utf-8", "replace")
    if b.startswith(b"\xff\xfe") or b.startswith(b"\xfe\xff"):
        return b.decode("utf-16", "replace")
    return b.decode("utf-8", "replace")


def rows(text):
    """(key, value) of every row of a file's text (LlmKitCsv.Rows)."""
    for f in parse_file(text):
        if len(f) < 2 or not f[0]:
            continue
        yield f[0], f[1].replace("\\n", "\n")


def _skipped_key(k):
    return "/" not in k and (k == "TextFont" or k.startswith("Image_") or k.startswith("TextMeshFont_"))


def read_per_file(files, crlf=False):
    """TextTable.LoadBase of a per-file layout: ({key: value} in first-seen order, Counter of rows/duplicates/skipped/multiline).
    crlf = line breaks as Binarizer's parser gives them (each run of them one CRLF: it skips empty lines), the form BASE_REF
    and the workspace table hold: the plugin serves them as they are, and compares a line's hash either way
    (OriginalMod.SameText / BinarizerForm)."""
    m, st = {}, collections.Counter()
    for p in files:
        for k, v in rows(_text(p)):
            st["rows"] += 1
            if _skipped_key(k):
                st["skipped"] += 1
                continue
            if k in m:
                st["duplicates"] += 1
            if "\n" in v:
                st["multiline"] += 1
            m[k] = binarizer_form(v) if crlf and "\n" in v else v
    return m, st


def binarizer_form(v):
    """OriginalMod.BinarizerForm: each run of line breaks one CRLF."""
    return re.sub("\n+", "\r\n", v.replace("\r\n", "\n"))


def read(layout):
    """The map of a found layout; the single file through publish.table_map (Binarizer's semantics)."""
    if layout.kind == "per-file":
        return read_per_file(layout.files)
    if layout.kind == "single":
        import publish
        return publish.table_map(publish.read_text(layout.files[0]))
    return {}, collections.Counter()


def single_text(m, nl="\r\n"):
    """The map as one StringTable.csv: key,"value" rows ('"' doubled), UTF-8 text; publish.table_map reads it back to m, with
    every line break inside a value as CRLF (Binarizer's parser gives nothing else)."""
    out = []
    for k, v in m.items():
        if not k or any(c in k for c in ',"\r\n') or _skipped_key(k):
            raise ValueError("table key %r cannot be written as a key,\"value\" row" % k)
        out.append(k + ',"' + v.replace('"', '""') + '"' + nl)
    return "".join(out)
