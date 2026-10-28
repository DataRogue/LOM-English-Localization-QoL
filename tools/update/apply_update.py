"""Update pass, step 4: workflow journals -> final English per line -> checks -> workspace rows and review ledger.

  collect JOURNAL...        parse the journals of update_workflow.js runs into update_pass/raw/<set>_<NNNN>.json (T = translator,
                            R = reviewer, F = referee; a later run of the same chunk replaces the earlier one)
  batches [--set S] [--chunks ..] [--size 40]   the lines that need a ruling (a reviewer fix, or Lash differs and the reviewer does
                            not side with ours) -> update_pass/referee/b_NNN.txt; prints the args of the referee workflow run
  decide [--set S] [--chunks 1-9,12]   choose the final English of every line (referee ruling, else the translator's line) and
                            check it against the Chinese (line breaks, tags and placeholders, no Chinese or full-width marks,
                            length band, the owner's retired forms); writes update_pass/final/<set>_<NNNN>.json and a summary
  apply [--set S] [--chunks ..] [--live] [--publish]   rows that passed -> workspace table (DiceHeader -> strings/17_library_position.csv)
                            and the review ledger (pass update-2026-10). Without --live nothing is written. --publish also runs
                            publish.py build + check at the end (default: no, the plugin folder stays as it is until the pass is done)
  auto [--live]             the symbol-only rows (English = the symbols) and the exact re-keyed lines (English inherited)
  report [--set S]          counts, the comparison with Lash's draft, failures
Backups of every file written go to LOM_Localization/backups (lom_paths.backup_path).
"""
import argparse, collections, csv, datetime, glob, io, json, os, re, shutil, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "fullpass"))
sys.path.insert(0, os.path.join(HERE, "..", "fullpass"))
import upd_common as U
from upd_common import CJK, pdir
import lom_paths as P
import ledger
import publish

TAG = re.compile(r"</?[a-zA-Z][^<>]*>")
PH = re.compile(r"\{[^{}]*\}")
BRACKET_ANY = re.compile(r"\[[^\[\]]*\]")
FULLWIDTH = re.compile("[「」『』《》，。！？：；（）～、]")
RETIRED = [(r"Inner (Strength|Power)", "Internal Force"), (r"\bProtector\b", "Guardian"), (r"Jie Wuchen", "Xie Wuchen"),
           (r"Soul-Stealer", "Soul-Stealing"), (r"Extreme Move", "Ultimate"), (r"Le Ping", "Yue Ping"), (r"Helmsman", "Branch Leader"),
           (r"Zhao Lang", "endearment"), (r"Little Junior Sister", "Junior Martial Sister"), (r"Third Fragrance", "Di San Xiang"),
           (r"Xiaozhu", "Xiao Zhu"), (r"Jinwu Shangren", "Venerable Golden Crow"), (r"\bMaster Uncle\b", "Martial Uncle")]


def parse_range(spec):
    out = set()
    for part in (spec or "").split(","):
        part = part.strip()
        if not part:
            continue
        if "-" in part:
            a, b = part.split("-", 1)
            out.update(range(int(a), int(b) + 1))
        else:
            out.add(int(part))
    return out


# ---- collect --------------------------------------------------------------------------------------------------------------------
def cmd_collect(journals):
    label_of, results = {}, {}
    for jp in journals:
        for line in io.open(jp, encoding="utf-8"):
            try:
                e = json.loads(line)
            except ValueError:
                continue
            if e.get("type") == "started" and e.get("agentId"):
                label_of[e["agentId"]] = e.get("label", "")
            elif e.get("type") == "result" and e.get("agentId"):
                lab = label_of.get(e["agentId"], "")
                r = e.get("result")
                if isinstance(r, str):
                    try:
                        r = json.loads(r)
                    except ValueError:
                        r = None
                if lab and isinstance(r, dict):
                    results[lab.replace(":retry", "")] = r      # a later result of the same label wins
    per = collections.defaultdict(dict)
    nref = 0
    for lab, r in results.items():
        m = re.match(r"^referee:batch:(\d+)$", lab)
        if m:
            os.makedirs(pdir("raw_referee"), exist_ok=True)
            U.write_json(pdir("raw_referee", "batch_%03d.json" % int(m.group(1))), r.get("items") or [])
            nref += 1
            continue
        m = re.match(r"^repair:batch:(\d+)$", lab)
        if m:
            os.makedirs(pdir("raw_repair"), exist_ok=True)
            U.write_json(pdir("raw_repair", "batch_%03d.json" % int(m.group(1))), r.get("items") or [])
            nref += 1
            continue
        m = re.match(r"^(translate2?|compare):([a-z]+):(\d+)$", lab)
        if not m:
            continue
        role, st, n = m.group(1), m.group(2), int(m.group(3))
        per[(st, n)].setdefault(role, r.get("items") or [])
    os.makedirs(pdir("raw"), exist_ok=True)
    for (st, n), d in sorted(per.items()):
        path = pdir("raw", "%s_%04d.json" % (st, n))
        old = U.read_json(path) if os.path.exists(path) else {}
        old.update(d)
        U.write_json(path, old)
    print("collected %d chunks and %d referee batches from %d journals (%d labelled results)" % (len(per), nref, len(journals), len(results)))


# ---- decide ---------------------------------------------------------------------------------------------------------------------
def check_line(zh, en, key):
    probs = []
    if not en or not en.strip():
        return ["empty"]
    zl = re.sub(r"\\(?=[㐀-鿿])", "\n", zh.replace("\r\n", "\n"))     # a stray backslash before Chinese is a line break
    el = en.replace("\r\n", "\n")
    if CJK.search(el):
        probs.append("chinese")
    if FULLWIDTH.search(el):
        probs.append("fullwidth")
    core = el.strip()
    lead_z, trail_z = zl[:len(zl) - len(zl.lstrip())], zl[len(zl.rstrip()):]
    lead_e, trail_e = el[:len(el) - len(el.lstrip())], el[len(el.rstrip()):]
    if lead_e != lead_z or trail_e != trail_z or core.count("\n\n") != zl.strip().count("\n\n") or "  " in core:
        probs.append("space")        # the English keeps the Chinese line's own leading and trailing line breaks, and no more
    if zl.count("\n") != el.count("\n"):
        probs.append("linebreaks %d/%d" % (zl.count("\n"), el.count("\n")))
    if sorted(TAG.findall(zl)) != sorted(TAG.findall(el)):
        probs.append("tags")
    if sorted(PH.findall(zl)) != sorted(PH.findall(el)):
        probs.append("placeholders")
    # full-width ［］ and 【】 become [ ] in the English (the published convention: 工作【護衛】 = the [Guard] job)
    zb = BRACKET_ANY.findall(zl.replace("［", "[").replace("］", "]").replace("【", "[").replace("】", "]"))
    eb = BRACKET_ANY.findall(el)
    if not zl.startswith("解鎖條件") and (len(zb) != len(eb) or sorted(b for b in zb if not CJK.search(b)) != sorted(b for b in eb if b in zb)):
        probs.append("brackets")
    nz = len(CJK.findall(zl))
    if nz >= 12 and len(el) < 0.8 * nz:
        probs.append("too short")
    if len(el) > 7 * nz + 40:
        probs.append("too long")
    for rx, ruled in RETIRED:
        if re.search(rx, el):
            probs.append("retired form (%s)" % ruled)
    return probs


def load_referee():
    out = {}
    for p in sorted(glob.glob(pdir("raw_referee", "batch_*.json"))):
        for x in U.read_json(p):
            if isinstance(x, dict) and x.get("id"):
                out[x["id"]] = x
    return out


REFEREE = {}
OVERRIDES = {}
REPAIR = {}


def load_repairs():
    out = {}
    for p in sorted(glob.glob(pdir("raw_repair", "batch_*.json"))):
        for x in U.read_json(p):
            if isinstance(x, dict) and x.get("id") and x.get("en"):
                out[x["id"]] = x["en"]
    return out


def load_overrides():
    """update_pass/overrides.tsv: key TAB English (backslash-n for a line break): the maintainer's own decisions, which win over
    the pipeline's line. Later lines win."""
    out = {}
    p = pdir("overrides.tsv")
    if os.path.exists(p):
        for line in io.open(p, encoding="utf-8"):
            line = line.rstrip(chr(10))
            if not line or line.startswith("#") or chr(9) not in line:
                continue
            k, v = line.split(chr(9), 1)
            out[k] = v.replace(U.BS + "n", chr(10))
    return out


UNLOCK_MAP = {}


def unlock_map(st):
    """zh word -> English of the stat names and level words the unlock-condition lines name (StatLevel, PlayerStat, PlayerInfo rows)."""
    m = {}
    for fam in ("StatLevel/", "PlayerStat/", "PlayerInfo/"):
        for k, z in st.new.items():
            if k.startswith(fam) and z not in m and st.english(k).strip():
                m[z] = st.english(k).strip()
    return m


def unlock_candidate(zh):
    """'Unlock Condition: Temperament is Brave or above' built from a 解鎖條件 line with its [stat] [level] words, so that all of them
    read alike and use the stat and level names players see. None when a word is unknown."""
    if not zh.startswith("解鎖條件"):
        return None
    body = zh[len("解鎖條件"):].lstrip("：:")
    plain = re.match(r"^([^\d>=＞［］]+)>=(\d+)$", body)
    if plain and plain.group(1) in UNLOCK_MAP:
        return "Unlock Condition: %s >= %s" % (UNLOCK_MAP[plain.group(1)], plain.group(2))
    if "［" not in body and ">=" not in body:
        return None
    bad = []

    def tok(m):
        e = UNLOCK_MAP.get(m.group(1))
        if e is None:
            bad.append(m.group(1))
            return ""
        return chr(0) + e + chr(0)
    s = re.sub("［([^］]*)］", tok, body)
    if bad:
        return None
    for a, b in (("不是", " is not "), ("以上", " or above"), ("以下", " or below"), ("為", " is "), ("且", " and "), ("或", " or "), (">=", " >= ")):
        s = s.replace(a, b)
    s = re.sub(r"\s+", " ", s.replace(chr(0), "")).strip()
    if CJK.search(s):
        return None
    return "Unlock Condition: " + s


def decide_chunk(st, setname, n):
    meta = U.read_json(pdir("chunks", setname, "p_%04d.json" % n))
    raw_path = pdir("raw", "%s_%04d.json" % (setname, n))
    raw = U.read_json(raw_path) if os.path.exists(raw_path) else {}
    T = {x["n"]: x["en"] for x in (raw.get("translate") or []) + (raw.get("translate2") or []) if isinstance(x, dict) and "n" in x and x.get("en")}
    R = {x["n"]: x for x in (raw.get("compare") or []) if isinstance(x, dict) and "n" in x}
    F = {int(x["id"].split(":")[2]): x for x in REFEREE.values() if x["id"].startswith("%s:%d:" % (setname, n))}
    out = []
    for sn, item in meta["items"].items():
        i = int(sn)
        key, zh = item["key"], item["zh"]
        t = T.get(i)
        r = R.get(i)
        f = F.get(i)
        cands = []
        if key in OVERRIDES:
            cands.append(("M", OVERRIDES[key]))
        u = unlock_candidate(zh)
        if u:
            cands.append(("U", u))
        if f:
            if f.get("pick") == "R" and r and r.get("en"):
                cands.append(("R", r["en"]))
            elif f.get("pick") == "X" and f.get("en"):
                cands.append(("X", f["en"]))
        if t:
            cands.append(("T", t))
        rp = REPAIR.get("%s:%d:%d" % (setname, n, i))
        if rp:
            cands.append(("P", rp))        # the repair of a line that failed the checks (tried only when the earlier ones failed)
        final, src, problems = None, None, []
        tried = []
        for s, en in cands:
            p = check_line(zh, en, key)
            tried.append((s, p))
            if not p:
                final, src = en, s
                break
        rec = {"n": i, "key": key, "zh": zh, "final": final, "source": src, "T": t, "lash": (r or {}).get("lash"), "who": (r or {}).get("who"),
               "verdict": (r or {}).get("verdict"), "note": (r or {}).get("note") or (f or {}).get("reason") or "", "tried": tried}
        if final is None:
            rec["status"] = "fail"
            rec["reason"] = "no translation" if not t else "; ".join("%s: %s" % (s, ",".join(p)) for s, p in tried)
        else:
            rec["status"] = "ok"
        out.append(rec)
    return out


def chunk_numbers(setname, spec):
    have = sorted(int(re.search(r"(\d+)", os.path.basename(p)).group(1)) for p in glob.glob(pdir("chunks", setname, "p_*.json")))
    want = parse_range(spec) if spec else None
    return [n for n in have if want is None or n in want]


def sets_of(spec):
    return [spec] if spec else [d for d in ("data", "legend", "story", "changed") if os.path.isdir(pdir("chunks", d))]


def cmd_decide(setspec, chunks):
    st = U.State()
    REFEREE.update(load_referee())
    OVERRIDES.update(load_overrides())
    REPAIR.update(load_repairs())
    UNLOCK_MAP.update(unlock_map(st))
    os.makedirs(pdir("final"), exist_ok=True)
    tot = collections.Counter()
    for s in sets_of(setspec):
        for n in chunk_numbers(s, chunks):
            if not os.path.exists(pdir("raw", "%s_%04d.json" % (s, n))):
                continue
            recs = decide_chunk(st, s, n)
            U.write_json(pdir("final", "%s_%04d.json" % (s, n)), recs)
            for r in recs:
                tot["lines"] += 1
                tot[r["status"]] += 1
                if r["status"] == "ok":
                    tot["from_" + r["source"]] += 1
                if r["lash"]:
                    tot["lash_" + r["lash"]] += 1
                    if r["lash"] == "differs" and r["who"]:
                        tot["lash_differs_who_" + r["who"]] += 1
    print(dict(tot))


# ---- referee batches -------------------------------------------------------------------------------------------------------------
def flagged_lines(setname, n):
    raw_path = pdir("raw", "%s_%04d.json" % (setname, n))
    if not os.path.exists(raw_path):
        return []
    raw = U.read_json(raw_path)
    T = {x["n"]: x["en"] for x in (raw.get("translate") or []) + (raw.get("translate2") or []) if isinstance(x, dict) and "n" in x}
    out = []
    for x in raw.get("compare") or []:
        if not isinstance(x, dict) or x.get("n") not in T:
            continue
        if x.get("verdict") == "fix" or (x.get("lash") == "differs" and x.get("who") and x.get("who") != "ours"):
            out.append((x["n"], T[x["n"]], x))
    return out


def one(s):
    return s.replace("\r\n", "\n").replace("\n", U.BS + "n")


def cmd_batches(setspec, chunks, size):
    st = U.State()
    ruled = set(load_referee())
    items = []
    for s in sets_of(setspec):
        for n in chunk_numbers(s, chunks):
            meta = U.read_json(pdir("chunks", s, "p_%04d.json" % n))
            for i, t, r in flagged_lines(s, n):
                rid = "%s:%d:%d" % (s, n, i)
                if rid in ruled or str(i) not in meta["items"]:
                    continue
                it = meta["items"][str(i)]
                lines = ["=== %s  (key %s) ===" % (rid, it["key"])]
                for b in it.get("before", []):
                    lines.append("   " + b)
                lines.append("   >> %s: %s" % (it.get("speaker") or it.get("kind"), one(it["zh"])))
                lines.append("T: " + one(t))
                lines.append("R: " + (one(r.get("en") or "") if r.get("verdict") == "fix" else "(no fix proposed)"))
                lines.append("L: " + one(st.lash.get(it["key"], "") or "(none)"))
                lines.append("reviewer: lash=%s%s | %s" % (r.get("lash"), (" who=" + r["who"]) if r.get("who") else "", r.get("note") or ""))
                items.append(chr(10).join(lines))
    os.makedirs(pdir("referee"), exist_ok=True)
    existing = [int(re.search(r"(\d+)", os.path.basename(p)).group(1)) for p in glob.glob(pdir("referee", "b_*.txt"))]
    start = max(existing + [0]) + 1
    batches = []
    for k in range(0, len(items), size):
        b = start + k // size
        path = pdir("referee", "b_%03d.txt" % b)
        head = "BATCH %d: %d lines that need a ruling (the Chinese is authoritative; T translator, R reviewer's fix, L Lash's machine draft)" % (b, len(items[k:k + size]))
        U.write_text(path, head + chr(10) * 2 + (chr(10) * 2).join(items[k:k + size]) + chr(10))
        batches.append({"batch": b, "file": path.replace(U.BS, "/")})
    print("%d lines to rule in %d batches (already ruled: %d)" % (len(items), len(batches), len(ruled)))
    print(json.dumps({"mode": "referee", "batches": batches}, ensure_ascii=False))


def cmd_repairs(setspec, chunks, size):
    """Lines that failed the checks (no passing candidate yet) -> update_pass/repair/r_NNN.txt for the repair workflow."""
    items = []
    done = set(load_repairs())
    for s in sets_of(setspec):
        for n in chunk_numbers(s, chunks):
            p = pdir("final", "%s_%04d.json" % (s, n))
            if not os.path.exists(p):
                continue
            for r in U.read_json(p):
                rid = "%s:%d:%d" % (s, n, r["n"])
                if r["status"] == "fail" and r["T"] and rid not in done:
                    lines = ["=== %s  (key %s) ===" % (rid, r["key"]), "ZH: " + one(r["zh"]), "EN: " + one(r["T"]), "PROBLEM: " + r["reason"]]
                    items.append(chr(10).join(lines))
    os.makedirs(pdir("repair"), exist_ok=True)
    existing = [int(re.search(r"(\d+)", os.path.basename(p)).group(1)) for p in glob.glob(pdir("repair", "r_*.txt"))]
    start = max(existing + [0]) + 1
    batches = []
    for k in range(0, len(items), size):
        b = start + k // size
        path = pdir("repair", "r_%03d.txt" % b)
        head = "REPAIR BATCH %d: %d lines that failed an automatic check" % (b, len(items[k:k + size]))
        U.write_text(path, head + chr(10) * 2 + (chr(10) * 2).join(items[k:k + size]) + chr(10))
        batches.append({"batch": b, "file": path.replace(U.BS, "/")})
    print("%d lines to repair in %d batches" % (len(items), len(batches)))
    print(json.dumps({"mode": "repair", "batches": batches}, ensure_ascii=False))


# ---- apply ----------------------------------------------------------------------------------------------------------------------
def load_finals(setspec, chunks):
    finals = {}
    fails = []
    for s in sets_of(setspec):
        for n in chunk_numbers(s, chunks):
            p = pdir("final", "%s_%04d.json" % (s, n))
            if not os.path.exists(p):
                continue
            for r in U.read_json(p):
                if r["status"] == "ok":
                    finals[r["key"]] = (r["final"], s, n)
                else:
                    fails.append((s, n, r["n"], r["key"], r["reason"]))
    return finals, fails


def dice_row(key, zh, en):
    return [key, en.replace("\n", U.BS + "n"), "was: (no English entry) - dice-check header (%s); new in game %s, code appends '......'" % (
        zh.replace("\n", U.BS + "n"), U.GAME_VERSION)]


def write_rows(st, rows, live, label):
    """rows: {key: english}. Table rows are appended to the workspace table, DiceHeader rows to the strings file; every table key
    goes into the review ledger. Returns (table_keys, dice_keys)."""
    table_rows = {k: v for k, v in rows.items() if not k.startswith("DiceHeader/")}
    dice_rows = {k: v for k, v in rows.items() if k.startswith("DiceHeader/")}
    for k in rows:
        if k not in st.added_set:
            sys.exit("%s is not a key the update added: refusing to write it" % k)
        if k in st.ws and not k.startswith("DiceHeader/"):
            sys.exit("%s already has a row in the workspace table: refusing to overwrite" % k)
    print("%s: %d table rows, %d DiceHeader overrides" % (label, len(table_rows), len(dice_rows)))
    if not live:
        for k in list(rows)[:5]:
            print("   ", k, "=>", rows[k][:100].replace("\n", " / "))
        return [], []
    import rowspans
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    strings_file = os.path.join(P.STRINGS_DIR, "17_library_position.csv")
    for p in [P.TABLE, strings_file] + [x for x in (ledger.path(False), ledger.path(True)) if os.path.exists(x)]:
        dst = P.backup_path(p, stamp)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copy2(p, dst)
    if table_rows:
        P.assert_writable(P.TABLE)
        raw = io.open(P.TABLE, encoding="utf-8", newline="").read()
        nl = "\r\n" if "\r\n" in raw else "\n"
        if not raw.endswith(nl):
            raw += nl
        add = "".join(rowspans.serialize(k, v, nl) + nl for k, v in sorted(table_rows.items()))
        io.open(P.TABLE, "w", encoding="utf-8", newline="").write(raw + add)
        ledger.add(sorted(table_rows), U.PASS_NAME)
    if dice_rows:
        raw = io.open(strings_file, encoding="utf-8", newline="").read()
        buf = io.StringIO()
        w = csv.writer(buf, quoting=csv.QUOTE_ALL, lineterminator="\n")
        for k in sorted(dice_rows):
            w.writerow(dice_row(k, st.new[k], dice_rows[k]))
        if not raw.endswith("\n"):
            raw += "\n"
        P.guard_write(strings_file)
        io.open(strings_file, "w", encoding="utf-8", newline="").write(raw + buf.getvalue())
    print("LIVE: written (backups in %s)" % P.BACKUPS)
    return sorted(table_rows), sorted(dice_rows)


def replace_rows(st, edits, live, label, note=None):
    """edits: {key: new English} for rows that already exist: the workspace table row, or the strings override when the key has
    one (the text players see). The ledger lists the table keys (a key already listed keeps its first entry)."""
    import rowspans
    note = note or "update " + U.GAME_VERSION
    tab = {k: v for k, v in edits.items() if k not in st.ov and k in st.ws}
    ovr = {k: v for k, v in edits.items() if k in st.ov}
    missing = [k for k in edits if k not in st.ws and k not in st.ov]
    if missing:
        sys.exit("no row or override for %s" % missing[:5])
    same = [k for k, v in edits.items() if st.english(k).replace(chr(13) + chr(10), chr(10)) == v.replace(chr(13) + chr(10), chr(10))]
    print("%s: %d table rows and %d strings overrides to replace (%d already read that way)" % (label, len(tab), len(ovr), len(same)))
    if not live:
        for k in list(edits)[:5]:
            print("   ", k, ":", st.english(k)[:60].replace(chr(10), " / "), "=>", edits[k][:80].replace(chr(10), " / "))
        return
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    files = {}
    for k in ovr:
        files.setdefault(st.ov[k][0], []).append(k)
    paths = [P.TABLE] + [os.path.join(P.STRINGS_DIR, f) for f in files] + [x for x in (ledger.path(False), ledger.path(True)) if os.path.exists(x)]
    for p in paths:
        dst = P.backup_path(p, stamp)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copy2(p, dst)
    if tab:
        P.assert_writable(P.TABLE)
        raw = io.open(P.TABLE, encoding="utf-8", newline="").read()
        nl = "\r\n" if "\r\n" in raw else "\n"
        out, applied, mismatched = rowspans.rewrite(raw, {k: (st.ws[k], v) for k, v in tab.items()}, nl)
        if mismatched:
            sys.exit("the workspace text of %s differs from the one read a moment ago" % sorted(mismatched)[:5])
        io.open(P.TABLE, "w", encoding="utf-8", newline="").write(out)
        ledger.add(sorted(tab), U.PASS_NAME)
    for f, keys in files.items():
        path = os.path.join(P.STRINGS_DIR, f)
        raw = io.open(path, encoding="utf-8", newline="").read()
        rows = list(csv.reader(io.StringIO(raw)))
        buf = io.StringIO()
        csv.writer(buf, quoting=csv.QUOTE_ALL, lineterminator=chr(10)).writerows(rows)
        if buf.getvalue() != raw:
            sys.exit("%s does not round-trip through the csv writer; edit it by hand" % f)
        for r in rows:
            if r and r[0] in ovr and r[0] in keys:
                old = r[1]
                r[1] = ovr[r[0]].replace(chr(13) + chr(10), chr(10)).replace(chr(10), U.BS + "n")
                if len(r) > 2:
                    r[2] = "was: %s | %s | %s" % (old, note, r[2])
        buf = io.StringIO()
        csv.writer(buf, quoting=csv.QUOTE_ALL, lineterminator=chr(10)).writerows(rows)
        P.guard_write(path)
        io.open(path, "w", encoding="utf-8", newline="").write(buf.getvalue())
    print("LIVE: replaced (backups in %s)" % P.BACKUPS)


def cmd_apply_changed(live):
    """The changed set: a line whose English differs from the current one replaces the row (or the strings override); a line that
    keeps the current English is only listed in update_pass/restamp.txt, for publish.py source --restamp @FILE after the build."""
    st = U.State()
    finals, fails = load_finals("changed", None)

    def norm(s):
        return s.replace(chr(13) + chr(10), chr(10)).strip()
    edits = {k: v[0] for k, v in finals.items() if norm(st.english(k)) != norm(v[0])}
    same = sorted(k for k in finals if k not in edits)
    print("changed set: %d lines ok, %d revised, %d keep their English (to restamp), %d failed" % (len(finals), len(edits), len(same), len(fails)))
    U.write_text(pdir("restamp.txt"), chr(10).join(same) + chr(10))
    replace_rows(st, edits, live, "changed", note="Chinese changed in game " + U.GAME_VERSION)
    if fails:
        print("failures:", [f[3] for f in fails][:6])


def cmd_fix(live):
    """update_pass/fixes.tsv: key TAB English (backslash-n = line break) -> replaces existing rows (maintainer corrections)."""
    st = U.State()
    edits = {}
    for line in io.open(pdir("fixes.tsv"), encoding="utf-8"):
        line = line.rstrip(chr(10))
        if line and not line.startswith("#") and chr(9) in line:
            k, v = line.split(chr(9), 1)
            edits[k] = v.replace(U.BS + "n", chr(10))
    replace_rows(st, edits, live, "fix")


def cmd_apply(setspec, chunks, live, publish_after):
    st = U.State()
    finals, fails = load_finals(setspec, chunks)
    rows = {k: v[0] for k, v in finals.items() if (k not in st.ws and k not in st.ov)}
    skipped = len(finals) - len(rows)
    print("lines ok: %d (already in the workspace: %d), failed: %d" % (len(finals), skipped, len(fails)))
    if fails:
        out = pdir("failed.json")
        U.write_json(out, fails, indent=0)
        print("failures listed in", out)
        for f in fails[:8]:
            print("   ", f)
    write_rows(st, rows, live, "apply")
    if live and publish_after:
        P.publish()


def cmd_auto(live):
    st = U.State()
    cls = U.classify_added(st)
    rows = {k: x for k, (c, x) in cls.items() if c in ("symbol", "move") and x and k not in st.ws}
    print("auto rows: %d symbol, %d moved" % (sum(1 for k in rows if cls[k][0] == "symbol"), sum(1 for k in rows if cls[k][0] == "move")))
    write_rows(st, rows, live, "auto")


def cmd_report(setspec):
    tot = collections.Counter()
    for s in sets_of(setspec):
        for p in sorted(glob.glob(pdir("final", s + "_*.json"))):
            for r in U.read_json(p):
                tot[(s, r["status"])] += 1
                if r["lash"]:
                    tot[(s, "lash_" + r["lash"])] += 1
    for k, v in sorted(tot.items()):
        print(k, v)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("cmd", choices=["collect", "batches", "repairs", "decide", "apply", "auto", "report", "fix", "apply-changed"])
    ap.add_argument("--size", type=int, default=40)
    ap.add_argument("journals", nargs="*")
    ap.add_argument("--set")
    ap.add_argument("--chunks")
    ap.add_argument("--live", action="store_true")
    ap.add_argument("--publish", action="store_true")
    a = ap.parse_args()
    if a.cmd == "collect":
        cmd_collect(a.journals)
    elif a.cmd == "fix":
        cmd_fix(a.live)
    elif a.cmd == "apply-changed":
        cmd_apply_changed(a.live)
    elif a.cmd == "batches":
        cmd_batches(a.set, a.chunks, a.size)
    elif a.cmd == "repairs":
        cmd_repairs(a.set, a.chunks, a.size)
    elif a.cmd == "decide":
        cmd_decide(a.set, a.chunks)
    elif a.cmd == "apply":
        cmd_apply(a.set, a.chunks, a.live, a.publish)
    elif a.cmd == "auto":
        cmd_auto(a.live)
    else:
        cmd_report(a.set)


if __name__ == "__main__":
    main()
