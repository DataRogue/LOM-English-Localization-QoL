"""Update pass, step 0: the game's own Chinese per key, read from the game files, in the harness 'srcdump' format (key TAB value,
with backslash, TAB, CR and LF escaped as \\ \t \r \n), so that publish.py source can take a game update without starting the game.

The text tables are Lean CSV TextAssets in Mortal_Data/sharedassets0.assets ('key = value' lines; Lean turns a literal \n into
the platform's line break, CRLF). Each table is there once per language: Name_zh-tw / Name_zh-cn / Name_kr, and the story tables
three times under the same name Story_N. The game runs in Traditional Chinese, so the Traditional copy is taken: the _zh-tw one,
and for a name without a language suffix the copy whose text is Traditional (no Hangul, more Traditional-only than
Simplified-only characters). Checked on build 26237817 (release 1.0.6001.0): against the harness srcdump of build 25987745 every
key is accounted for, and every unchanged row is byte-identical.

Usage: python dump_source.py OUT.tsv [--compare OLD.tsv]
--compare prints the keys added, removed and changed against an earlier dump (base_ref/game_source.tsv), and how many of the
changed rows only gained or lost spaces. Reads the game files only (UnityPy); writes OUT.tsv.
"""
import argparse, collections, io, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
import lom_paths as P

sys.stdout.reconfigure(encoding="utf-8")
LANG = re.compile(r"_(zh-tw|zh-cn|kr)$")
HANGUL = re.compile("[\uac00-\ud7a3]")
# common characters whose Traditional and Simplified forms differ
TRAD = set("這們說時會來為對過還麼個裡與從間點門開關問頭見長東車馬書學國對讓認話謝錢")
SIMP = set("这们说时会来为对过还么个里与从间点门开关问头见长东车马书学国对让认话谢钱")


def esc(s):
    return s.replace("\\", "\\\\").replace("\t", "\\t").replace("\r", "\\r").replace("\n", "\\n")


def unesc(s):
    out, i = [], 0
    while i < len(s):
        if s[i] == "\\" and i + 1 < len(s):
            out.append({"\\": "\\", "t": "\t", "r": "\r", "n": "\n"}.get(s[i + 1], "\\" + s[i + 1]))
            i += 2
        else:
            out.append(s[i])
            i += 1
    return "".join(out)


def parse_lean(text):
    """A Lean CSV: 'key = value' per line, '//' comments; a literal \\n in the value is a line break (CRLF)."""
    m = {}
    for line in text.split("\n"):
        if line.endswith("\r"):
            line = line[:-1]
        if line.startswith("//"):
            continue
        i = line.find(" = ")
        if i < 0:
            continue
        m[line[:i].strip()] = line[i + 3:].replace("\\n", "\r\n")
    return m


def is_traditional(text):
    if HANGUL.search(text):
        return False
    t = sum(text.count(c) for c in TRAD)
    s = sum(text.count(c) for c in SIMP)
    return t > s


def read_game(game):
    import UnityPy
    env = UnityPy.load(os.path.join(game, "Mortal_Data", "sharedassets0.assets"))
    by_base = collections.defaultdict(list)
    for obj in env.objects:
        if obj.type.name != "TextAsset":
            continue
        ta = obj.read()
        raw = ta.m_Script
        data = raw.encode("utf-8", "surrogateescape") if isinstance(raw, str) else bytes(raw)
        text = data.decode("utf-8-sig", "replace")
        if " = " not in text:
            continue
        name = ta.m_Name
        m = LANG.search(name)
        by_base[LANG.sub("", name)].append((m.group(1) if m else None, name, text))
    out, used = {}, []
    for base, lst in sorted(by_base.items()):
        tw = [x for x in lst if x[0] == "zh-tw"] or [x for x in lst if x[0] is None and is_traditional(x[2])]
        if not tw:
            continue
        if len(tw) > 1:
            sys.exit("%s: %d Traditional copies, cannot choose" % (base, len(tw)))
        lang, name, text = tw[0]
        m = parse_lean(text)
        if not m:
            continue
        used.append((name, len(m)))
        for k, v in m.items():
            out.setdefault(k, v)
    return out, used


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("--compare")
    ap.add_argument("--game", default=P.GAME)
    a = ap.parse_args()
    m, used = read_game(a.game)
    with io.open(a.out, "w", encoding="utf-8", newline="\n") as f:
        for k in sorted(m):
            f.write("%s\t%s\n" % (k, esc(m[k])))
    print("%d keys from %d tables -> %s" % (len(m), len(used), a.out))
    if a.compare:
        old = {}
        for line in io.open(a.compare, encoding="utf-8"):
            line = line.rstrip("\n")
            if line:
                k, _, v = line.partition("\t")
                old[k] = unesc(v)
        added = sorted(set(m) - set(old))
        removed = sorted(set(old) - set(m))
        changed = sorted(k for k in set(m) & set(old) if m[k] != old[k])
        sp = [k for k in changed if old[k].count("\n") == m[k].count("\n")
              and re.sub(r"[ \t\u3000]", "", old[k]) == re.sub(r"[ \t\u3000]", "", m[k])]
        print("against %s: %d added, %d removed, %d changed (%d of them only in spaces)" % (
            a.compare, len(added), len(removed), len(changed), len(sp)))
        for name, keys in (("added", added), ("removed", removed)):
            c = collections.Counter(k.split("/")[0] for k in keys)
            print("  %s by family: %s" % (name, ", ".join("%s %d" % x for x in c.most_common())))


if __name__ == "__main__":
    main()
