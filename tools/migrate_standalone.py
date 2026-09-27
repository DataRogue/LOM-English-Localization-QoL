"""Move this mod's revised English out of the original OverLlm patch's files into LOM_UI_EN's own folder (LOM_UI_EN 0.4).

SUPERSEDED since LOM_UI_EN 0.5: the plugin folder ships only this mod's revisions (publish.py build), the full tables live in
LOM_Localization/workspace and the OverLlm release in LOM_Localization/base_ref (cutover_0_5.py made that layout). Every
command below now refuses (and so does each command function when this module is imported and called), because each
would recreate full tables in the plugin folder or rewrite the OverLlm patch's own files. The parsers and helpers (idea_parse, table_map, repair_table, simple_xunity, md5, ...) stay importable; publish.py
uses them.

  python migrate_standalone.py plan                  what 'copy' would do; writes nothing
  python migrate_standalone.py copy [--stage DIR]    M1 backup + M2 copy into plugins/LOM_UI_EN/translation (or DIR)
  python migrate_standalone.py restore-base [--stage DIR]
                                                     M4: put the base patch's two files back to their pristine state
                                                     (only after in-game verification, and only with the owner's go-ahead)
  python migrate_standalone.py revert BACKUP_DIR     copy an M1 backup back to the base patch's files

Every command refuses to run while Mortal.exe is running, and checks the expected hashes first (--force-with-report to go on
with a mismatch, printing what differs). 'copy' only writes inside our plugin folder and the backups folder.
"""
import argparse, datetime, hashlib, json, os, re, shutil, subprocess, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lom_paths as P

sys.stdout.reconfigure(encoding="utf-8")

EXPECT = {
    # live files as audited for the 0.4 design (2026-09-15 edits)
    "base_table_live": "d8a6b686e29b6e9e4271fa5280d025fe",
    "base_scene_live": "a24de77181095cdd4f386fcab0c9c046",
    # the untouched release table (profiles/original/StringTable.csv == the release zip)
    "orig_table": "c9e1292a4a8f3e8b27d8bb01ba9563d4",
}
RESIZERS = ["mainmenu.resizer.txt", "status.resizer.txt", "story.resizer.txt"]


def md5(path):
    h = hashlib.md5()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def game_running():
    """True while Mortal.exe runs, and also when tasklist cannot tell (fail closed)."""
    try:
        r = subprocess.run(["tasklist", "/FI", "IMAGENAME eq Mortal.exe", "/NH"], capture_output=True, text=True, timeout=60)
    except Exception:
        return True
    return r.returncode != 0 or "mortal.exe" in (r.stdout or "").lower()


# ---- Ideafixxxer.CsvParser (Fungus.dll), the parser Binarizer and LOM_UI_EN use --------------------------------------

def idea_parse(data):
    lines, cur, val, st = [], [], [], "LS"
    for text in re.split("\n|\r\n", data):
        if len(text) == 0:
            continue
        for c in text:
            if c == ",":
                if st in ("LS", "VS", "V"):
                    cur.append("".join(val)); val = []; st = "VS"
                elif st == "Q":
                    val.append(",")
                elif st == "QQ":
                    cur.append("".join(val)); val = []; st = "VS"
            elif c == '"':
                if st in ("LS", "VS"):
                    st = "Q"
                elif st == "V":
                    val.append('"')
                elif st == "Q":
                    st = "QQ"
                elif st == "QQ":
                    val.append('"'); st = "Q"
            else:
                if st in ("LS", "VS", "V"):
                    val.append(c); st = "V"
                elif st == "Q":
                    val.append(c)
                elif st == "QQ":
                    val.append(c); st = "Q"
        if st == "LS":
            lines.append(cur); cur = []
        elif st in ("VS", "V", "QQ"):
            cur.append("".join(val)); val = []; lines.append(cur); cur = []; st = "LS"
        elif st == "Q":
            val.append("\r"); val.append("\n")
    if val:
        cur.append("".join(val))
    if cur:
        lines.append(cur)
    return lines


def table_map(text, join_extra):
    """First row wins, like Binarizer; join_extra = TextTable's repair of rows with more than two fields."""
    m = {}
    for r in idea_parse(text):
        if len(r) < 2 or not r[0] or r[0] in m:
            continue
        m[r[0]] = ",".join(r[1:]) if join_extra else r[1]
    return m


def repair_table(text):
    """Rewrite the physical lines whose row has more than two fields as key,"joined value" (quotes doubled)."""
    bad = {r[0]: ",".join(r[1:]) for r in idea_parse(text) if len(r) > 2 and r[0]}
    if not bad:
        return text, []
    out, fixed = [], []
    for line in text.splitlines(keepends=True):
        body = line.rstrip("\r\n")
        ending = line[len(body):]
        key = body.split(",", 1)[0]
        if key in bad and key not in fixed:
            rows = idea_parse(body)
            if len(rows) == 1 and len(rows[0]) > 2 and ",".join(rows[0][1:]) == bad[key]:
                out.append(key + ',"' + bad[key].replace('"', '""') + '"' + ending)
                fixed.append(key)
                continue
        out.append(line)
    return "".join(out), fixed


# ---- XUnity lines (only the simple ones, for sentinels) --------------------------------------------------------------

def simple_xunity(path):
    """key -> value for lines without escapes or comments: one '=', no backslash, no '//', no '%3D'. Later lines win."""
    d = {}
    with open(path, encoding="utf-8-sig") as f:
        for line in f:
            line = line.rstrip("\r\n")
            if not line or line.startswith("#") or "\\" in line or "//" in line or "%3D" in line or line.count("=") != 1:
                continue
            k, v = line.split("=", 1)
            if k and v:
                d[k] = v
    return d


def all_xunity_values(path):
    vals = set()
    with open(path, encoding="utf-8-sig") as f:
        for line in f:
            i = line.find("=")
            if i > 0:
                vals.add(line[i + 1:].rstrip("\r\n"))
    return vals


HAN = re.compile(r"^[一-鿿]{2,12}$")
PLAIN = re.compile(r"^[A-Za-z][A-Za-z '\-]{1,38}[A-Za-z]$")


def pick(cands, n):
    cands = sorted(cands)
    if len(cands) <= n:
        return cands
    step = len(cands) / float(n)
    return [cands[int(i * step)] for i in range(n)]


def table_sentinels(revised, original, n=24):
    c = [k for k in revised if k in original and not k.startswith("Story/") and revised[k] != original[k]
         and PLAIN.match(revised[k]) and PLAIN.match(original[k])]
    return [{"key": k, "revised": revised[k], "original": original[k]} for k in pick(c, n)], len(c)


def scene_sentinels(rev_path, orig_path, n=24):
    r, o = simple_xunity(rev_path), simple_xunity(orig_path)
    values = all_xunity_values(rev_path) | all_xunity_values(orig_path)
    c = [k for k in r if k in o and r[k] != o[k] and HAN.match(k) and k not in values and PLAIN.match(r[k]) and PLAIN.match(o[k])]
    return [{"key": k, "revised": r[k], "original": o[k]} for k in pick(c, n)], len(c)


# ---- commands --------------------------------------------------------------------------------------------------------

def _superseded(name):
    """Every command of this 0.4 migration refuses since 0.5, also when imported and called directly: each would recreate
    full tables in the plugin folder or rewrite the OverLlm patch's own files."""
    raise SystemExit("migrate_standalone.py %s: superseded by cutover_0_5.py / publish.py. Since LOM_UI_EN 0.5 the plugin folder "
                     "ships only this mod's revisions (python publish.py build) and the full tables live in LOM_Localization/"
                     "workspace; this 0.4 migration command would recreate full tables in the plugin folder or rewrite the OverLlm "
                     "patch's own files." % name)


def check(label, path, want, force, problems):
    got = md5(path) if os.path.exists(path) else "missing"
    ok = got == want
    print("  %-16s %s %s%s" % (label, got, "ok" if ok else "EXPECTED " + want, "" if os.path.exists(path) else " (missing)"))
    if not ok:
        problems.append(label)


def preflight(args, need):
    if game_running():
        sys.exit("Mortal.exe is running; close the game first.")
    problems = []
    print("hash check:")
    for label, path, key in need:
        check(label, path, EXPECT[key], args.force_with_report, problems)
    if problems and not args.force_with_report:
        sys.exit("hash mismatch (%s); rerun with --force-with-report to continue anyway" % ", ".join(problems))
    return problems


def cmd_copy(args, dry):
    _superseded("copy")
    preflight(args, [("base table", P.BASE_TABLE, "base_table_live"), ("base scene", P.BASE_SCENE, "base_scene_live"),
                     ("original table", P.ORIG_TABLE, "orig_table")])
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    dest = os.path.abspath(args.stage) if args.stage else P.TRANSLATION
    backup = os.path.join(dest, "_backup-" + stamp) if args.stage else os.path.join(P.BACKUPS, "migration-" + stamp)
    for p in (os.path.join(dest, "StringTable.csv"), os.path.join(dest, "scene", "scene_text.txt")):
        if os.path.exists(p) and not args.stage:
            sys.exit("%s already exists; the copy has been made before. Move it away to redo it." % p)
    print("backup ->", backup)
    print("copy   ->", dest)
    table_text = open(P.BASE_TABLE, encoding="utf-8", newline="").read()
    fixed_text, fixed = repair_table(table_text)
    print("table: %d rows re-joined: %s" % (len(fixed), ", ".join(fixed)))
    after_join = table_map(fixed_text, join_extra=False)
    broken = [k for k in fixed if after_join.get(k, "").count("{") != after_join.get(k, "").count("}")]
    if broken:
        print("  WARNING: still malformed after the re-join (the base had lost part of the tag; fix by hand in translation/): %s" % ", ".join(broken))
    before = table_map(table_text, join_extra=True)
    after = table_map(fixed_text, join_extra=False)
    diff = [k for k in set(before) | set(after) if before.get(k) != after.get(k)]
    if diff:
        sys.exit("repair check failed: %d keys differ after the repair, e.g. %s" % (len(diff), diff[:3]))
    original = table_map(open(P.ORIG_TABLE, encoding="utf-8", newline="").read(), join_extra=True)
    ts, tn = table_sentinels(after, original)
    ss, sn = scene_sentinels(P.BASE_SCENE, P.ORIG_SCENE)
    print("sentinels: table %d of %d candidates, scene %d of %d" % (len(ts), tn, len(ss), sn))
    if len(ts) < 12 or len(ss) < 12:
        sys.exit("too few sentinels")
    if dry:
        for s in ts[:5] + ss[:5]:
            print("   ", s["key"], "|", s["original"], "->", s["revised"])
        print("plan only; nothing written")
        return
    # M1 backup
    os.makedirs(backup)
    files = {"StringTable.csv": P.BASE_TABLE, "_AutoGeneratedTranslations.txt": P.BASE_SCENE}
    for r in RESIZERS:
        files[r] = os.path.join(P.BASE_RESIZE_DIR, r)
    for c in ("lom.ui.english.cfg", "lom.strings.english.cfg"):
        files[c] = os.path.join(P.GAME, "BepInEx", "config", c)
    hashes = {}
    for name, src in files.items():
        if os.path.exists(src):
            shutil.copy2(src, os.path.join(backup, name))
            hashes[name] = {"from": src, "md5": md5(src), "size": os.path.getsize(src)}
    json.dump(hashes, open(os.path.join(backup, "hashes.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    # M2 copy
    os.makedirs(os.path.join(dest, "scene", "resize"), exist_ok=True)
    out_table = os.path.join(dest, "StringTable.csv")
    with open(out_table, "w", encoding="utf-8", newline="") as f:
        f.write(fixed_text)
    out_scene = os.path.join(dest, "scene", "scene_text.txt")
    shutil.copyfile(P.BASE_SCENE, out_scene)
    for r in RESIZERS:
        shutil.copyfile(os.path.join(P.BASE_RESIZE_DIR, r), os.path.join(dest, "scene", "resize", r))
    manifest = {
        "created": datetime.datetime.now().isoformat(timespec="seconds"),
        "note": "LOM_UI_EN 0.4 migration: revised text copied out of the OverLlm base patch's files. Sentinels are lines whose revised and original English differ; the plugin compares them with what the base patch serves to tell whether its files are pristine or still hold this mod's edits.",
        "sources": {k: v for k, v in hashes.items() if k in ("StringTable.csv", "_AutoGeneratedTranslations.txt")},
        "outputs": {"StringTable.csv": md5(out_table), "scene/scene_text.txt": md5(out_scene)},
        "repaired_rows": fixed,
        "original": {"StringTable.csv": md5(P.ORIG_TABLE), "xunity_translations.txt": md5(P.ORIG_SCENE)},
        "table_sentinels": ts,
        "scene_sentinels": ss,
    }
    with open(os.path.join(dest, "MANIFEST.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, indent=1, ensure_ascii=False)
    if md5(out_scene) != md5(P.BASE_SCENE):
        sys.exit("scene copy is not byte-identical")
    print("done. backup %s; wrote %s, %s, %d resizers, MANIFEST.json" % (backup, out_table, out_scene, len(RESIZERS)))


def cmd_restore_base(args):
    _superseded("restore-base")
    """M4: the base patch's table back to the release file; its XUnity file back to the built-in original plus the lines
    XUnity appended at runtime whose keys the original lacks (its own machine translations since our edits)."""
    if game_running():
        sys.exit("Mortal.exe is running; close the game first.")
    for p in (P.TABLE, P.SCENE, P.MANIFEST):
        if not os.path.exists(p):
            sys.exit("%s is missing: run 'copy' (and verify in game) before restoring the base patch" % p)
    if md5(P.ORIG_TABLE) != EXPECT["orig_table"] and not args.force_with_report:
        sys.exit("profiles/original/StringTable.csv is not the release file")
    # The base files must still be what 'copy' captured: anything written to them since (an old tool still pointed at them)
    # exists nowhere else and would be lost.
    manifest = json.load(open(P.MANIFEST, encoding="utf-8"))
    src = manifest.get("sources", {})
    problems = []
    want_t = src.get("StringTable.csv", {}).get("md5")
    if want_t and md5(P.BASE_TABLE) != want_t:
        problems.append("Mods/English/StringTable.csv changed since the copy (md5 %s, copied %s)" % (md5(P.BASE_TABLE), want_t))
    s = src.get("_AutoGeneratedTranslations.txt", {})
    if s.get("md5"):
        # XUnity only appends to its file, so the copied bytes must still be its first bytes.
        with open(P.BASE_SCENE, "rb") as f:
            head = f.read(int(s.get("size", 0)))
        if hashlib.md5(head).hexdigest() != s["md5"]:
            problems.append("the XUnity file was edited (not only appended to) since the copy")
    if problems:
        ours = table_map(open(P.TABLE, encoding="utf-8", newline="").read(), join_extra=True)
        base = table_map(open(P.BASE_TABLE, encoding="utf-8", newline="").read(), join_extra=True)
        diff = [k for k in base if ours.get(k) != base[k]]
        for pr in problems:
            print("CHANGED:", pr)
        print("  %d table keys differ between the base and translation/StringTable.csv (the copy may lack edits made since), e.g.:" % len(diff))
        for k in diff[:10]:
            print("    %s | base: %s | ours: %s" % (k, base[k][:60], (ours.get(k) or "")[:60]))
        if not args.force_with_report:
            sys.exit("refusing to restore: move those edits into translation/ first, or rerun with --force-with-report")
    dest_table = os.path.join(args.stage, "StringTable.csv") if args.stage else P.BASE_TABLE
    dest_scene = os.path.join(args.stage, "_AutoGeneratedTranslations.txt") if args.stage else P.BASE_SCENE
    orig_lines = open(P.ORIG_SCENE, encoding="utf-8-sig", newline="").read().splitlines(keepends=True)
    def key_of(line):
        i = 0
        while True:
            i = line.find("=", i)
            if i < 0 or (i > 0 and line[i - 1] != "\\"):
                return line[:i] if i > 0 else None
            i += 1
    orig_keys = {key_of(l) for l in orig_lines}
    live_lines = open(P.BASE_SCENE, encoding="utf-8-sig", newline="").read().splitlines(keepends=True)
    # Live lines whose key the original lacks are XUnity's own runtime appends (its machine translations of text it met
    # later); they are the base patch's content, not ours, so they stay. Every other live line reverts to the original.
    extra = [l for l in live_lines if key_of(l) and key_of(l) not in orig_keys]
    extra = [l if l.endswith("\n") else l + "\n" for l in extra]
    print("restore: table <- %s; scene <- %s + %d appended lines" % (P.ORIG_TABLE, P.ORIG_SCENE, len(extra)))
    for l in extra[:12]:
        print("   +", l.rstrip()[:100])
    if args.dry_run:
        print("dry run; nothing written")
        return
    if not args.stage:
        stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
        b = backup_base_files("restore-" + stamp)
        print("backup ->", b, "(undo with: migrate_standalone.py revert %s)" % b)
    else:
        os.makedirs(args.stage, exist_ok=True)
    shutil.copyfile(P.ORIG_TABLE, dest_table)
    text = "".join(orig_lines)
    if text and not text.endswith("\n"):
        text += "\n"
    with open(dest_scene, "w", encoding="utf-8", newline="") as f:
        f.write(text + "".join(extra))
    if md5(dest_table) != EXPECT["orig_table"]:
        sys.exit("restored table hash mismatch")
    print("done")


def backup_base_files(folder):
    _superseded("backup_base_files")
    """Copy the base patch's two files into LOM_Localization/backups/<folder> with a hashes.json that 'revert' accepts."""
    b = os.path.join(P.BACKUPS, folder)
    os.makedirs(b)
    hashes = {}
    for name, path in (("StringTable.csv", P.BASE_TABLE), ("_AutoGeneratedTranslations.txt", P.BASE_SCENE)):
        shutil.copy2(path, os.path.join(b, name))
        hashes[name] = {"from": path, "md5": md5(path), "size": os.path.getsize(path)}
    json.dump(hashes, open(os.path.join(b, "hashes.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    return b


def cmd_revert(args):
    _superseded("revert")
    if game_running():
        sys.exit("Mortal.exe is running; close the game first.")
    h = json.load(open(os.path.join(args.backup, "hashes.json"), encoding="utf-8"))
    for name, info in h.items():
        src = os.path.join(args.backup, name)
        if md5(src) != info["md5"]:
            sys.exit("backup file %s does not match its recorded hash" % name)
    b = backup_base_files("before-revert-" + datetime.datetime.now().strftime("%Y%m%d-%H%M%S"))
    print("current base files backed up to", b)
    for name in ("StringTable.csv", "_AutoGeneratedTranslations.txt"):
        if name in h:
            shutil.copy2(os.path.join(args.backup, name), h[name]["from"])
            print("restored", h[name]["from"])


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    for name in ("plan", "copy", "restore-base"):
        s = sub.add_parser(name)
        s.add_argument("--stage")
        s.add_argument("--force-with-report", action="store_true")
        s.add_argument("--dry-run", action="store_true")
    r = sub.add_parser("revert")
    r.add_argument("backup")
    a = ap.parse_args()
    _superseded(a.cmd)
    if a.cmd == "plan":
        cmd_copy(a, dry=True)
    elif a.cmd == "copy":
        cmd_copy(a, dry=a.dry_run)
    elif a.cmd == "restore-base":
        cmd_restore_base(a)
    else:
        cmd_revert(a)


if __name__ == "__main__":
    main()
