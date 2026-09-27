"""Data hygiene for this mod's scene text (lom_paths.SCENE, translation/scene/scene_text.txt, XUnity line format).

    python hygiene/scene_hygiene.py [--fixes verified.json] [--live] [--keep-record KEYS|@FILE|all]

Reads the file the way SceneDictionary does: it splits the whole file at every CR and LF (empty pieces skipped), so a stray CR
or LF inside a physical line (the file split at its own line breaks) makes two lines of it:
  stray_line_break   a line whose stray break leaves one piece (a CR at its end, a CR alone) is rewritten as that piece; it
               loads the same
  a line whose stray break leaves two or more pieces makes the run refuse (exit 2, naming the line), as publish.py build
               refuses it: one line whose line break lost its backslash-n escape and two lines run together look alike, and
               rewriting it as one line would drop the others' English. Fix it by hand.
Removes lines that can never serve an English player:
  hangul_key   a key containing Hangul (text captured while the Korean UI plugin had half-translated a screen; the game's
               source text is Chinese, so these keys never match)
  junk_key     test keys ('test', '123', '131231'), Unity object ToString names ('icon_x (UnityEngine.Sprite)',
               'DFT_S5_SDF_Battle_001 (TMPro.TMP_FontAsset)'), and bare Fungus tag fragments ('{size', '{punch', '{color')
and handles each line the loader rejects (an unescaped second '=' in the English, a '//' after it, a bad backslash-u escape,
or no '=' at all) as follows:
  unloadable_duplicate   removed when its key already has a line that loads (with a value) elsewhere in the file: the loader
               uses that line, so players' text is unchanged (a rewrite would leave two loadable lines with different values)
  unloadable_superseded  a key with no loadable line and two or more rejected lines keeps one: its last rejected line with
               English is rewritten escaped (below; the one a later line of a key would win with), the key's other rejected
               lines are removed, so exactly one loadable line remains (two with different values would make the overlay
               differ from the workspace); when that last line cannot be written escaped the run refuses, naming it
  unloadable   removed when it has no English to keep: no '=' at all on a key the OverLlm release in BASE_REF lacks, a key
               that cannot be decoded, or a line BASE_REF itself has (rejected there too); it never loaded
  unloadable_escaped   otherwise rewritten, never removed: the line again with its value escaped the way SceneDictionary
               reads it back (an unescaped '=' in the English becomes backslash-'='; scene_hygiene.rewrite_line). For a key
               BASE_REF has, removing the line would publish a removal of OverLlm's line (players would lose both); for a
               line this mod adds (BASE_REF lacks the key) it would drop the English. A line that cannot be written that
               way (a '//' or a bad backslash-u escape in the English, a trailing backslash, or no '=' on a key BASE_REF
               has) makes the run refuse, naming the line, and nothing is written: fix it by hand or with --fixes.
and rewrites the values listed in --fixes (a JSON list of {"line", "old_line", "en", "category", "note"}; "old_line" must
equal the file's line exactly, "en" is the new decoded value; the key is kept byte for byte). plan() is what a run does and
describe() says it in words; publish.py quotes describe() when it refuses to build a workspace with rejected lines.

Without --live the result and its change log go to LOM_Localization/staged/scene_hygiene/ (under LOM_STAGE: the stage's).
With --live the scene file is backed up to LOM_Localization/backups/scene_text.txt.<stamp>.bak, rewritten in place (BOM and
CRLF kept) and the change log goes to LOM_Localization/scene_hygiene/ (lom_paths.log_dir: a junction or link there is
refused). Either way the output is checked the way SceneDictionary loads it: every line must decode to a non-empty key and
value, with no stray line break left, or nothing is written. --keep-record is passed to that publish (publish.py build
--keep-record).
"""
import os, sys, io, re, json, shutil, argparse, datetime, collections
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import lom_paths
import publish as PB

BS = chr(92)
HAN = re.compile("[%s-%s%s-%s%s-%s]" % (chr(0x4E00), chr(0x9FAF), chr(0x3400), chr(0x4DBF), chr(0xF900), chr(0xFAFF)))
HANGUL = re.compile("[%s-%s%s-%s%s-%s%s-%s%s-%s]" % (chr(0xAC00), chr(0xD7AF), chr(0x1100), chr(0x11FF), chr(0x3130), chr(0x318F),
                                                  chr(0xA960), chr(0xA97F), chr(0xD7B0), chr(0xD7FF)))
JUNK_KEYS = {"test", "123", "131231"}
TOSTRING = re.compile(r"^[A-Za-z0-9_.-]+ \((?:UnityEngine|TMPro)\.[A-Za-z_.]+\)$")
FUNGUS_FRAGMENT = re.compile(r"^\{[a-z]+$")
DIRECTIVES = ("#set ", "#unset ", "#enable ")


def decode(s):
    """SceneDictionary.ReadTranslationLineAndDecode: [key, value], or None when the loader rejects the line (publish.decode,
    which also covers the lines on which the C# code throws)."""
    return PB.decode(s)


def _decode_old(s):
    """The earlier decoder, kept for reference (it crashed on a '//' after a second '=')."""
    if not s:
        return None
    arr, num, esc, sb, i, n = [None, None], 0, False, [], 0, len(s)
    while i < n:
        c = s[i]
        if esc:
            if c in ("=", BS):
                sb.append(c)
            elif c == "n":
                sb.append("\n")
            elif c == "r":
                sb.append("\r")
            elif c == "u":
                if i + 4 >= n:
                    return None
                try:
                    sb.append(chr(int(s[i + 1:i + 5], 16)))
                except ValueError:
                    return None
                i += 4
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
        elif c == "%" and s[i + 1:i + 3] == "3D":
            sb.append("=")
            i += 2
        elif c == "/" and s[i + 1:i + 2] == "/":
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


def encode_value(v):
    out = []
    for ch in v:
        out.append({BS: BS + BS, "\n": BS + "n", "\r": BS + "r", "=": BS + "="}.get(ch, ch))
    s = "".join(out)
    if "//" in s or "%3D" in s:
        raise ValueError("value would not round-trip (contains // or %%3D): %r" % v)
    return s


def decode_raw_value(raw):
    """A value in the form the apply tools hold it (XUnity-escaped: backslash-n = line break, backslash-= = '='), decoded as
    SceneDictionary would, except that an unescaped '=' stays a literal '=' instead of making the whole line unloadable.
    Raises ValueError for '//' (a comment in XUnity's format), a bad backslash-u escape or a trailing backslash."""
    out, i, n = [], 0, len(raw)
    while i < n:
        c = raw[i]
        if c == BS:
            if i + 1 >= n:
                raise ValueError("value ends in a backslash: %r" % raw)
            d = raw[i + 1]
            if d in ("=", BS):
                out.append(d)
            elif d == "n":
                out.append(chr(10))
            elif d == "r":
                out.append(chr(13))
            elif d == "u":
                h = raw[i + 2:i + 6]
                if len(h) < 4 or any(x not in "0123456789abcdefABCDEF" for x in h):
                    raise ValueError("bad unicode escape in %r" % raw)
                out.append(chr(int(h, 16)))
                i += 6
                continue
            else:
                out.append(BS)
                out.append(d)
            i += 2
        elif c == "%" and raw[i + 1:i + 3] == "3D":
            out.append("=")
            i += 3
        elif c == "/" and raw[i + 1:i + 2] == "/":
            raise ValueError("'//' would start a comment: %r" % raw)
        else:
            out.append(c)
            i += 1
    return "".join(out)


def rewrite_line(old_line, raw_key_text, new_raw_value):
    """The scene line raw_key_text=<the new value, escaped so SceneDictionary reads it back exactly> for the apply tools, or
    None when the value cannot be written in a line or the line would not keep old_line's key. (Writing the value in raw, as
    the tools did, made a line with an '=' in its English unloadable: publish.py then shipped a removal of OverLlm's line.)"""
    try:
        val = decode_raw_value(new_raw_value)
        line = raw_key_text + "=" + encode_value(val)
    except ValueError:
        return None
    kv, old = decode(line), decode(old_line)
    if kv is None or kv[1] != val or not kv[0] or (old is not None and old[0] != kv[0]):
        return None
    return line


def tsv(s):
    return (s or "").replace("\t", BS + "t").replace("\r", "<CR>").replace("\n", "<LF>")


def pieces(ln):
    """What SceneDictionary reads of one physical line (the file split at its own line breaks): it splits the whole file at
    every CR and LF and skips the empty pieces, so a stray CR or LF inside a line makes two (or more) lines of it."""
    if "\r" not in ln and "\n" not in ln:
        return [ln] if ln else []
    return [p for p in re.split("[\r\n]", ln) if p]


def load_stats(lines):
    """What SceneDictionary.LoadFile counts for these lines (before the trimmed-variant pass), split as it splits them (at
    every CR and LF). A physical line holding a stray line break is listed as bad too (publish.py refuses it)."""
    st = collections.Counter()
    keys = set()
    bad = []
    for n, ln in enumerate(lines, 1):
        if "\r" in ln or "\n" in ln:
            bad.append((n, "stray line break", ln))
        for p in pieces(ln):
            st["lines"] += 1
            if p.startswith(DIRECTIVES):
                continue
            kv = decode(p)
            if kv is None:
                st["rejected"] += 1
                bad.append((n, "rejected", p))
                continue
            if not kv[0] or not kv[1]:
                st["empty"] += 1
                bad.append((n, "empty key or value", p))
                continue
            if kv[0].startswith(("sr:", "r:")):
                st["regex"] += 1
                continue
            if kv[0] in keys:
                st["duplicates"] += 1
            keys.add(kv[0])
            st["entries"] += 1
    st["distinct_keys"] = len(keys)
    return st, bad


def loadable_lines(lines):
    """{key: [line numbers]} of the lines that load with a non-empty key and value (directives aside; each physical line
    split as SceneDictionary splits it)."""
    out = collections.defaultdict(list)
    for n, ln in enumerate(lines, 1):
        for p in pieces(ln):
            kv = decode(p) if not p.startswith(DIRECTIVES) else None
            if kv and kv[0] and kv[1] and n not in out[kv[0]]:
                out[kv[0]].append(n)
    return out


def _key_of(ln):
    """The decoded key of a line (also of one the loader rejects), or None when it cannot be decoded."""
    kx = decode(raw_key(ln) + "=x")
    return kx[0] if kx else None


def plan(lines, base_sf, fixes=None):
    """What a run does to the scene file's lines (split at its line breaks): (out lines, change log rows, counts, refused),
    refused = [(line number, key, line, why)] (a run with any refused line writes nothing). base_sf is BASE_REF's scene file
    (anything with .map, .lines and .rejected, as publish.SceneFile). Raises ValueError for --fixes that no longer match."""
    fixes = fixes or {}
    base_keys = set(base_sf.map)
    base_rejected = {base_sf.lines[i] for i in base_sf.rejected}
    out, log, refused = [], [], []
    counts = collections.Counter()

    # 1. stray line breaks. SceneDictionary splits the file at every CR and LF: a physical line (the file split at its own
    # line breaks) holding another CR or LF is two or more lines to it. With one piece that loads the same (a stray CR at
    # the end, a CR alone) the stray break goes; with two or more the run refuses, naming the line: whether it is one line
    # whose line break lost its backslash-n escape or two lines run together cannot be told, and rewriting it as one line
    # would drop the second line's English.
    work, multi = list(lines), set()
    for n, ln in enumerate(lines, 1):
        if "\r" not in ln and "\n" not in ln:
            continue
        ps = pieces(ln)
        if len(ps) > 1:
            multi.add(n)
            refused.append((n, _key_of(ps[0]), ln, "the line holds a stray line break (a %s inside it), and SceneDictionary "
                                                   "splits lines at every CR and LF, so it reads %d lines here (%s): one line "
                                                   "whose line break lost its backslash-n escape, or lines run together? "
                                                   "Fix it by hand (rewriting it as one line would drop the English of the "
                                                   "others)" % ("CR" if "\r" in ln else "LF", len(ps),
                                                                " | ".join(repr(p[:40]) for p in ps[:3]))))
            continue
        work[n - 1] = ps[0] if ps else ""
        counts["rewrite:stray_line_break"] += 1
        log.append((n, "rewrite", "stray_line_break", raw_key(work[n - 1]), ln, work[n - 1],
                    "a stray line break SceneDictionary skips (it splits lines at every CR and LF): removed, the line loads "
                    "the same"))

    # 2. one loadable line per key: every rejected line of a key that has a loadable line goes (the loader uses that one);
    # of a key without one, the last rejected line with English is rewritten escaped and the key's other rejected lines go
    key_lines = loadable_lines([l for l in work])
    for n in fixes:
        if 0 < n <= len(work) and fixes[n].get("en"):
            k = _key_of(work[n - 1])
            if k is not None and n not in key_lines[k]:
                key_lines[k].append(n)
    chosen = {}
    for n, ln in enumerate(work, 1):
        if n in multi or n in fixes or not ln or ln.startswith(DIRECTIVES) or decode(ln) is not None or HANGUL.search(raw_key(ln)):
            continue
        k = _key_of(ln)
        if k is None or ln in base_rejected or raw_key(ln) == ln or key_lines.get(k):
            continue
        chosen[k] = n                 # the last one wins (the loader's order: a later line of a key replaces an earlier one)

    for n, ln in enumerate(work, 1):
        if n in multi:
            out.append(lines[n - 1])
            continue
        if n in fixes:
            f = fixes[n]
            if lines[n - 1] != f["old_line"]:
                raise ValueError("line %d no longer matches the reviewed text; re-run the review" % n)
            rk = raw_key(ln)
            key = decode(rk + "=x")[0]
            new = rk + "=" + encode_value(f["en"])
            if decode(new) != [key, f["en"]]:
                raise ValueError("line %d: rewritten line does not decode back to its key and value" % n)
            old = ln[len(rk) + 1:]
            out.append(new)
            counts["rewrite:" + f.get("category", "value")] += 1
            log.append((n, "rewrite", f.get("category", "value"), rk, old, new[len(rk) + 1:], f.get("note", "")))
            continue
        if ln == "" or ln.startswith(DIRECTIVES):
            out.append(ln)
            continue
        kv = decode(ln)
        rk = raw_key(ln)
        cat = None
        if HANGUL.search(rk):
            cat = "hangul_key"
        elif kv is None:
            cat = "unloadable"
        elif kv[0] in JUNK_KEYS or TOSTRING.match(kv[0]) or FUNGUS_FRAGMENT.match(kv[0]):
            cat = "junk_key"
        why = None
        if cat == "unloadable":
            kx = decode(rk + "=x")
            key = kx[0] if kx else None
            others = [m for m in key_lines.get(key, []) if m != n] if key else []
            if key is None:
                why = "loader rejects the line and its key cannot be decoded (a bad backslash-u escape?); it never loaded"
            elif ln in base_rejected:
                why = "loader rejects the line; BASE_REF has the same line, rejected there too; it never loaded"
            elif others:
                cat = "unloadable_duplicate"
                why = ("loader rejects the line; its key has a loadable line (line %s), which is what the loader uses, so "
                       "removing this one leaves players' text unchanged" % ", ".join(map(str, others[:5])))
            elif key in chosen and chosen[key] != n:
                cat = "unloadable_superseded"
                why = ("loader rejects the line; its key has no loadable line, and the key's last rejected line with English "
                       "(line %d) is rewritten with its value escaped, so that one loadable line remains (two loadable lines "
                       "with different values would make the overlay differ from the workspace); this one never loaded" %
                       chosen[key])
            elif rk == ln:
                if key in base_keys:
                    refused.append((n, key, ln, "no '=' in the line, so there is no English to keep, and BASE_REF has the key "
                                                "(removing it would publish a removal of OverLlm's line)"))
                    out.append(ln)
                    continue
                why = "loader rejects the line (no '=' separator; a key BASE_REF lacks, with no English); it never loaded"
            else:
                new = rewrite_line(ln, rk, ln[len(rk) + 1:])
                kn = decode(new) if new is not None else None
                if kn is None or kn[0] != key:
                    refused.append((n, key, ln, "the English cannot be written escaped (a '//' or a bad backslash-u escape in it, "
                                                "or a trailing backslash)"))
                    out.append(ln)
                    continue
                out.append(new)
                counts["rewrite:unloadable_escaped"] += 1
                log.append((n, "rewrite", "unloadable_escaped", rk, ln[len(rk) + 1:], new[len(rk) + 1:],
                            "the loader rejected the line (an unescaped '='); " + (
                                "BASE_REF has the key, so it is rewritten with the value escaped instead of removed (a removal "
                                "would publish a removal of OverLlm's line)" if key in base_keys else
                                "a line this mod adds (BASE_REF lacks the key), rewritten with the value escaped so its English "
                                "loads instead of being dropped")))
                continue
        if cat is None:
            out.append(ln)
            continue
        counts["remove:" + cat] += 1
        if why is None:
            why = {"hangul_key": "key contains Hangul (Korean capture residue)",
                   "junk_key": "test / Unity object name / Fungus tag fragment key"}[cat]
            if kv is not None and not kv[1]:
                why += "; empty value, line did not load"
        log.append((n, "remove", cat, rk, ln[len(rk) + 1:] if rk != ln else "", "", why))
    missing = sorted(set(fixes) - multi - {r[0] for r in log if r[1] == "rewrite"})
    if missing:
        raise ValueError("fixes for lines not reached: %s" % missing)
    refused.sort(key=lambda r: r[0])
    log.sort(key=lambda r: r[0])
    return out, log, counts, refused


def _nums(rows):
    ns = [str(r[0]) for r in rows]
    return ", ".join(ns[:10]) + (" and %d more" % (len(ns) - 10) if len(ns) > 10 else "")


def describe(lines, base_sf):
    """What 'scene_hygiene.py --live' does to these lines, in one sentence (publish.py quotes it when it refuses a workspace
    whose scene file has lines the loader rejects)."""
    out, log, counts, refused = plan(lines, base_sf)
    if refused:
        return ("refuse and write nothing: %d line%s cannot be fixed without losing text (%s; e.g. line %d: %s). Fix %s by hand "
                "in %s first (or give --fixes entries)" % (
                    len(refused), "s" if len(refused) > 1 else "",
                    "; ".join("line %d: %s" % (n, w) for n, k, l, w in refused[:3]), refused[0][0], tsv(refused[0][2][:80]),
                    "it" if len(refused) == 1 else "them", lom_paths.SCENE))
    parts = []
    groups = collections.OrderedDict()
    for r in log:
        groups.setdefault((r[1], r[2]), []).append(r)
    for (action, cat), rows in groups.items():
        n = len(rows)
        s = "s" if n > 1 else ""
        if (action, cat) == ("rewrite", "unloadable_escaped"):
            nb = sum(1 for r in rows if "BASE_REF has the key" in r[6])
            parts.append("rewrite %d line%s with the '=' in the English escaped so %s load%s as written (line%s %s; %d for keys "
                         "BASE_REF has, %d this mod adds)" % (n, s, "they" if n > 1 else "it", "" if n > 1 else "s", s,
                                                             _nums(rows), nb, n - nb))
        elif (action, cat) == ("remove", "unloadable_duplicate"):
            parts.append("remove %d line%s whose key already has a loadable line (line%s %s): the loader uses that line, so "
                         "players' text is unchanged" % (n, s, s, _nums(rows)))
        elif (action, cat) == ("remove", "unloadable_superseded"):
            kept = sorted({int(re.search(r"\(line (\d+)\)", r[6]).group(1)) for r in rows})
            parts.append("remove %d rejected line%s of a key whose last rejected line is the one rewritten (line%s %s; the "
                         "rewritten line%s kept: %s), so one loadable line per key remains" % (
                             n, s, s, _nums(rows), "s" if len(kept) > 1 else "", ", ".join(map(str, kept[:10]))))
        elif (action, cat) == ("rewrite", "stray_line_break"):
            parts.append("remove the stray line break from %d line%s (line%s %s): SceneDictionary splits lines at every CR and "
                         "LF and skips the empty piece, so %s the same" % (n, s, s, _nums(rows),
                                                                          "they load" if n > 1 else "it loads"))
        elif (action, cat) == ("remove", "unloadable"):
            parts.append("remove %d line%s that never loaded and hold no English to keep (line%s %s: no '=' on a key BASE_REF "
                         "lacks, an undecodable key, or a line BASE_REF has rejected too)" % (n, s, s, _nums(rows)))
        elif (action, cat) == ("remove", "hangul_key"):
            parts.append("remove %d Hangul-key line%s (line%s %s)" % (n, s, s, _nums(rows)))
        elif (action, cat) == ("remove", "junk_key"):
            parts.append("remove %d junk-key line%s (line%s %s)" % (n, s, s, _nums(rows)))
        else:
            parts.append("%s %d %s line%s (line%s %s)" % (action, n, cat, s, s, _nums(rows)))
    if not parts:
        return "change nothing"
    return ("; ".join(parts) + "; it backs the scene file up, logs every change (old and new text) under "
            "LOM_Localization/scene_hygiene/ and then publishes")


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")   # the report prints Chinese; a piped cp1252 stdout would stop it
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--fixes", help="JSON list of verified value rewrites")
    ap.add_argument("--live", action="store_true")
    ap.add_argument("--keep-record", metavar="KEYS|@FILE|all", help="passed to the publish after a --live write (publish.py "
                    "build --keep-record: changed table rows keep their old revision record)")
    a = ap.parse_args()
    if a.keep_record is not None:
        lom_paths.take_keep_record(["scene_hygiene.py", "--keep-record", a.keep_record])   # a missing @FILE stops it here

    data = open(lom_paths.SCENE, "rb").read()
    bom = data.startswith(b"\xef\xbb\xbf")
    text = (data[3:] if bom else data).decode("utf-8")
    nl = "\r\n" if "\r\n" in text else "\n"
    lines = text.split(nl)
    before, _ = load_stats(lines)

    fixes = {}
    if a.fixes:
        for f in json.load(io.open(a.fixes, encoding="utf-8")):
            fixes[int(f["line"])] = f

    # the OverLlm release the workspace is laid over: an unloadable line whose key it has is rewritten escaped, never removed
    if not os.path.exists(lom_paths.BASE_REF_SCENE):
        sys.exit("%s is missing: without the OverLlm release's scene file this tool cannot tell which unloadable lines replace "
                 "OverLlm's line (removing one of those would remove OverLlm's line too); nothing written" % lom_paths.BASE_REF_SCENE)
    base_sf = PB.SceneFile(lom_paths.BASE_REF_SCENE)
    try:
        out, log, counts, refused = plan(lines, base_sf, fixes)
    except ValueError as e:
        sys.exit(str(e))
    if refused:
        for n, key, ln, why in refused:
            print("REFUSED line %d (key %r): %s -- %s" % (n, (key or "")[:40], tsv(ln[:160]), why))
        print("%d lines cannot be fixed without losing text (listed above): a line the loader rejects whose key BASE_REF has "
              "cannot be removed (that would publish a removal of OverLlm's line), a line this mod adds cannot be removed "
              "(that would drop its English), neither can be rewritten escaped, and a line with a stray line break is two or "
              "more lines to SceneDictionary. Fix them by hand in %s (or give --fixes entries for them); nothing written" % (
                  len(refused), lom_paths.SCENE))
        sys.exit(2)

    after, bad = load_stats(out)
    if bad:
        for b in bad[:20]:
            print("BAD", b[0], b[1], tsv(b[2][:160]))
        sys.exit("output has %d lines that would not load; nothing written" % len(bad))
    left_hangul = [ln for ln in out if HANGUL.search(raw_key(ln))]
    if left_hangul:
        sys.exit("Hangul keys left: %d" % len(left_hangul))
    han_values = [(i, ln) for i, ln in enumerate(out, 1) if ln and decode(ln) and HAN.search(decode(ln)[1])]

    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    if a.live:
        dest = lom_paths.assert_writable(lom_paths.SCENE)
        logdir = lom_paths.log_dir("scene_hygiene")          # LOCALIZATION/scene_hygiene, guarded (no junction out of it)
    else:
        logdir = lom_paths.staged_dir("scene_hygiene")      # LOCALIZATION/staged/...: the stage under LOM_STAGE
        dest = lom_paths.out_path(logdir, "scene_text.txt")
    if a.live:
        os.makedirs(lom_paths.BACKUPS, exist_ok=True)
        bak = lom_paths.backup_path(lom_paths.SCENE, stamp)
    logpath = lom_paths.out_path(logdir, "scene_hygiene_changes.%s.tsv" % stamp)
    with io.open(logpath, "w", encoding="utf-8", newline="\n") as f:
        f.write("line_before\taction\tcategory\tkey\told_value\tnew_value\tnote\n")
        for r in log:
            f.write("\t".join([str(r[0])] + [tsv(x) for x in r[1:]]) + "\n")
    if a.live:
        shutil.copy2(lom_paths.SCENE, bak)
        print("backup", bak)
    body = nl.join(out).encode("utf-8")
    with open(dest, "wb") as f:
        f.write((b"\xef\xbb\xbf" if bom else b"") + body)

    # read back what was written and check it the way the plugin loads it
    back = open(dest, "rb").read()
    btext = (back[3:] if back.startswith(b"\xef\xbb\xbf") else back).decode("utf-8")
    reread, bad = load_stats(btext.split(nl))
    if bad or reread != after:
        sys.exit("read-back check failed: %s" % (bad[:5] or reread))

    print("wrote", dest)
    print("log  ", logpath)
    print("changes", dict(sorted(counts.items())))
    print("physical lines (newline count) before %d after %d" % (text.count(nl), btext.count(nl)))
    for label, st in (("before", before), ("after ", after)):
        print("%s loader view: lines %d entries %d rejected %d empty %d duplicates %d distinct keys %d" % (
            label, st["lines"], st["entries"], st["rejected"], st["empty"], st["duplicates"], st["distinct_keys"]))
    print("values still containing Han characters: %d" % len(han_values))
    for i, ln in han_values:
        print("   ", i, ln[:140])
    if a.live:
        # the workspace was written: rebuild the shipped plugin files from it (publish.py build; prints one line)
        lom_paths.publish(keep_record=a.keep_record)


if __name__ == "__main__":
    main()
