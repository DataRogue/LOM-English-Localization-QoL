"""Update pass, step 2: cut the lines to translate into chunks for the Sonnet workflow (update_workflow.js).

Sets (update_pass/chunks/<set>/): story (Story/* rows in scene order, from the Lua scripts of the new build), legend
(LegendInfo/* in key order), data (every other family, rows of one entity together), changed (rows whose Chinese changed).
Per chunk: p_NNNN.txt (the translator's view: short line numbers, terms, voice samples, scene cast, context), p_NNNN.json (line
number -> key, the apply tool's map), l_NNNN.txt (Lash's 10.16 draft per line, for the comparison step only: never shown to the
translator), and manifest.json.

Usage:
  python make_chunks.py plan                         classify every added key (update_pass/plan.json) and print the counts
  python make_chunks.py story  [--per 30]            [--scripts a,b,...]
  python make_chunks.py legend [--per 16]
  python make_chunks.py data   [--per 40]
Only rows that are not in the workspace table yet are cut, so a rerun after an apply chunks what is left.
"""
import argparse, collections, json, os, re, shutil, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import upd_common as U
from upd_common import CJK, pdir

CTX = 6          # lines of context before a chunk's first editable line
AFTER = 3        # Chinese-only lines after it
KIND = {"character": "say", "narrative": "narr", "center": "caption", "center2": "caption", "think": "thought", "option": "option"}


def esc(s):
    return s.replace("\r\n", "\n").replace("\n", U.BS + "n")


def build_terms(st):
    """Glossary + character names + this pass's term sheet. Glossary terms whose settled English rarely shows up where the Chinese
    does in the published text (update_pass/term_stats.json: 老子 as a rude 'I', 自己, the stale 小竹) are left out: they mislead."""
    stats = U.read_json(pdir("term_stats.json")) if os.path.exists(pdir("term_stats.json")) else {}

    def stable(zh):
        n, hit = stats.get(zh, (0, 0))
        return n < 8 or hit / n >= 0.5
    pairs = [t for t in U.glossary_terms() if stable(t[0])]
    pairs += [(z, e, True) for z, e in U.character_names(st).items()]
    pairs += U.new_term_sheet()
    return U.TermIndex(pairs)


def speaker_namer(st):
    def name(cid):
        if not cid:
            return None
        k = "Character/" + cid
        e = st.english(k).strip()
        if e and not CJK.search(e):
            return e
        z = st.new.get(k) or st.old.get(k)
        return (z + "?") if z else cid
    return name


# ---- story ----------------------------------------------------------------------------------------------------------------------
def scene_items(st, name, todo, taken, hints, namer, extra=None):
    ev, cast = U.walk_script(name)
    items = []

    def mk(k, kind, sid=None):
        edit = k in todo and k not in taken
        if edit:
            taken.add(k)
        d = {"type": "line", "key": k, "kind": kind, "sid": sid, "speaker": namer(sid) if sid else None, "zh": st.new[k],
             "en": "" if edit else st.english(k), "edit": edit}
        if edit and hints.get(k):
            d["hint"] = hints[k][:2]
        if edit and extra and k in extra:
            d["old"], d["cur"] = extra[k]
        return d

    for e in ev:
        if e["t"] == "line":
            if e["key"] in st.new:
                items.append(mk(e["key"], e["kind"], e["speaker"]))
        elif e["t"] == "menu":
            opts = [o for o in e["options"] if o in st.new]
            if opts:
                items.append({"type": "menu"})
                items.extend(mk(o, "option") for o in opts)
        elif e["t"] == "branch":
            o = e["option"]
            if o in st.new:
                items.append({"type": "branch", "zh": st.new[o], "en": st.english(o)})
    return items, cast


GAP = 10          # a run of this many lines without a line to translate ends a segment (the next one starts with its own context)
MAX_ITEMS = 70    # and no segment is longer than this many lines


def cut_scene(items, per):
    segs, n, pos = [], len(items), 0
    while True:
        while pos < n and not items[pos].get("edit"):
            pos += 1
        if pos >= n:
            break
        i, cnt, j, gap, last = pos, 0, pos, 0, pos
        while j < n and j - i < MAX_ITEMS:
            if items[j].get("edit"):
                if cnt == per:
                    break
                cnt += 1
                gap, last = 0, j
            else:
                gap += 1
                if gap > GAP:
                    break
            j += 1
        end = last + 1
        after = [x for x in items[end:end + 8] if x["type"] == "line"][:AFTER]
        segs.append({"ctx": items[max(0, i - CTX):i], "lines": items[i:end], "after": after})
        pos = end
    return segs


def family(script):
    return re.split(r"_", script, 1)[0]


def n_edit(seg):
    return sum(1 for x in seg["lines"] if x.get("edit"))


def line_text(it, n=None, show_en=True):
    if it["type"] == "menu":
        return "   (menu: the player chooses one of the options below)"
    if it["type"] == "branch":
        return "   (the lines below follow the option: %s)" % esc(it["zh"])
    who = it.get("speaker") or ""
    head = "%s%s" % (KIND.get(it["kind"], it["kind"]), (" " + who) if who else "")
    zh = esc(it["zh"])
    if n is not None:
        extra = ""
        if it.get("hint"):
            extra = "   [the same Chinese already reads: %s]" % " / ".join('"%s"' % esc(h) for h in it["hint"])
        if it.get("old") is not None:
            return "*%d  %s: %s\n      was: %s\n      current English: %s" % (n, it["key"] if it["kind"] in ("data", "chronicle") else head, zh, esc(it["old"]), esc(it["cur"]))
        return "*%d  %s: %s%s" % (n, head, zh, extra)
    if it.get("en") and show_en:
        return ".   %s: %s  =>  %s" % (head, zh, esc(it["en"]))
    return ".   %s: %s" % (head, zh)


def pick_voices(pool, sids, st):
    out = collections.OrderedDict()
    for sid in sids:
        keys = pool.get(sid) or []
        if not keys:
            continue
        pick = [keys[len(keys) // 4], keys[len(keys) // 2], keys[(3 * len(keys)) // 4]] if len(keys) >= 3 else keys[:3]
        seen, rows = set(), []
        for k in pick:
            if k not in seen:
                seen.add(k)
                rows.append((st.new[k], st.english(k)))
        out[sid] = rows
    return out


def chunk_story(st, per, only=None, todo=None, hints=None, extra=None):
    cls = U.classify_added(st)
    if todo is None:
        todo = {k for k, (c, x) in cls.items() if c == "fresh" and k.startswith("Story/") and k not in st.ws and k not in st.ov}
    if hints is None:
        hints = {k: x for k, (c, x) in cls.items() if c == "fresh" and x}
    namer = speaker_namer(st)
    scripts = U.lua_scripts()
    taken, segs_by_script, voice_pool = set(), [], collections.defaultdict(list)
    referenced = set()
    for name in scripts:
        items, cast = scene_items(st, name, todo, taken, hints, namer, extra)
        for it in items:
            if it["type"] == "line":
                referenced.add(it["key"])
                if (not it["edit"] and it["kind"] == "character" and it["sid"] and it["en"] and "\n" not in it["zh"]
                        and 10 <= len(it["zh"]) <= 60 and 15 <= len(it["en"]) <= 160 and it["key"] in st.led_t
                        and not re.search(r"\.\.\. \.|\s\.|  ", it["en"])):
                    voice_pool[it["sid"]].append(it["key"])
        if only and name not in only:
            continue
        for seg in cut_scene(items, per):
            seg["script"], seg["cast"], seg["family"] = name, cast, family(name)
            segs_by_script.append(seg)
    # key order for the Story rows no script reaches
    unref = sorted(k for k in todo if k not in referenced)
    if unref and not only:
        items = [{"type": "line", "key": k, "kind": ("option" if k.startswith("Story/O_") else "think" if k.startswith("Story/T_") else
                                                      "narrative" if k.startswith("Story/N_") else "character"), "sid": None, "speaker": None,
                  "zh": st.new[k], "en": "", "edit": True, **({"hint": hints[k][:2]} if hints.get(k) else {}),
                  **({"old": extra[k][0], "cur": extra[k][1]} if extra and k in extra else {})} for k in unref]
        for seg in cut_scene(items, per):
            seg["script"], seg["cast"], seg["family"] = "unreferenced Story rows (key order)", [], "~unref"
            segs_by_script.append(seg)
    # merge small neighbours of one script family into one chunk
    chunks, cur = [], None
    for seg in segs_by_script:
        if cur and cur["family"] == seg["family"] and cur["editable"] + n_edit(seg) <= per:
            cur["segs"].append(seg)
            cur["editable"] += n_edit(seg)
        else:
            cur = {"family": seg["family"], "segs": [seg], "editable": n_edit(seg)}
            chunks.append(cur)
    return chunks, voice_pool, len(todo), len(unref)


def render_story(st, ch, terms, voice_pool, n_chunk, setname):
    L, ids, texts = [], {}, []
    n = 0
    sids = []
    body = []
    for seg in ch["segs"]:
        cast, seen_names = [], set()
        speakers = [x.get("sid") for x in seg["ctx"] + seg["lines"] + seg["after"] if x.get("sid")]
        for cid in list(dict.fromkeys(speakers)) + list(seg["cast"]):
            k = "Character/" + cid
            e = st.english(k).strip()
            if not e or e in seen_names or len(cast) >= 14:
                continue
            seen_names.add(e)
            g = U.gender_of(cid)
            cast.append("%s (%s)" % (e, g) if g else e)
            if cid not in sids:
                sids.append(cid)
        body.append("--- scene %s | cast: %s ---" % (seg["script"], "; ".join(cast) if cast else "see the lines"))
        base = len(body)
        for it in seg["ctx"]:
            body.append(line_text(it))
            texts.append(it.get("zh", ""))
        for it in seg["lines"]:
            if it.get("edit"):
                n += 1
                ids[str(n)] = {"key": it["key"], "zh": it["zh"], "kind": it["kind"], "speaker": it.get("speaker"), "hint": it.get("hint", []),
                               "before": body[max(base, len(body) - 3):], **({"old": it["old"], "cur": it["cur"]} if it.get("old") is not None else {})}
                body.append(line_text(it, n))
            else:
                body.append(line_text(it))
            texts.append(it.get("zh", ""))
            if it.get("sid") and it["sid"] not in sids:
                sids.append(it["sid"])
        for it in seg["after"]:
            body.append("next>  %s: %s" % ((it.get("speaker") or KIND.get(it["kind"], it["kind"])), esc(it["zh"])))
            texts.append(it.get("zh", ""))
    found = terms.find("\n".join(texts))
    if len(found) > 50:
        found = sorted(found, key=lambda t: (not terms.ruled.get(t[0], False)))[:50]
    if setname == "changed":
        L.append("CHUNK %d (changed) - the Chinese of the %d lines marked *N changed in the update: decide for each whether the current English still fits" % (n_chunk, n))
    else:
        L.append("CHUNK %d (%s) - translate the %d lines marked *N" % (n_chunk, setname, n))
    if found:
        L.append("SETTLED ENGLISH for terms in this chunk (use these exactly; names and titles first):")
        L.extend("  %s = %s" % (z, e) for z, e in found)
    voices = pick_voices(voice_pool, sids, st)
    if voices:
        L.append("VOICE SAMPLES (published English lines of these speakers, for tone and habits only):")
        for sid, rows in voices.items():
            nm = st.english("Character/" + sid).strip() or sid
            for z, e in rows:
                L.append("  %s: %s  =>  %s" % (nm, esc(z), esc(e)))
    L.append("LINES (line breaks are written \\n; '.' lines are context already shown to the player, '*N' lines are yours):")
    L.extend(body)
    return "\n".join(L) + "\n", ids


# ---- legend, data -------------------------------------------------------------------------------------------------------------------
def entity_key(k):
    p = k.split("/")
    if len(p) >= 3:
        return (p[-1], "/".join(p[1:-1]))
    return (p[-1] if len(p) > 1 else k, "")


def exemplars(st, key, n=6):
    """Up to n existing rows of the same family and sub-family as key (nearest in key order), as (key, zh, en)."""
    prefix = key.rsplit("/", 1)[0] + "/"
    cand = sorted(k for k in st.ws if k.startswith(prefix) and k in st.old and st.old[k] == st.new.get(k) and st.old[k] and k not in st.added_set)
    if not cand:
        fam = key.split("/")[0] + "/"
        cand = sorted(k for k in st.ws if k.startswith(fam) and k in st.old and st.old[k] == st.new.get(k) and st.old[k])
    if not cand:
        return []
    import bisect
    i = bisect.bisect_left(cand, key)
    lo = max(0, min(i - n // 2, len(cand) - n))
    return [(k, st.new[k], st.english(k)) for k in cand[lo:lo + n]]


def chunk_rows(keys, per):
    out = []
    for i in range(0, len(keys), per):
        out.append(keys[i:i + per])
    return out


def render_rows(st, groups, terms, n_chunk, setname, kind_note):
    """groups: [(header, [keys])]; each key is a '*N' line."""
    L, ids, texts, n = [], {}, [], 0
    body = []
    for header, keys, ex in groups:
        body.append("--- %s ---" % header)
        for k, z, e in ex:
            body.append(".   %s | %s  =>  %s" % (k, esc(z), esc(e)))
            texts.append(z)
        for k in keys:
            n += 1
            z = st.new[k]
            ids[str(n)] = {"key": k, "zh": z, "kind": setname, "speaker": None, "hint": []}
            hint = ""
            c = st._cls.get(k)
            if c and c[0] == "fresh" and c[1]:
                hint = "   [the same Chinese already reads: %s]" % " / ".join('"%s"' % esc(h) for h in c[1][:2])
                ids[str(n)]["hint"] = c[1][:2]
            body.append("*%d  %s | %s%s" % (n, k, esc(z), hint))
            texts.append(z)
    found = terms.find("\n".join(texts))
    if len(found) > 50:
        found = sorted(found, key=lambda t: (not terms.ruled.get(t[0], False)))[:50]
    L.append("CHUNK %d (%s) - translate the %d rows marked *N. %s" % (n_chunk, setname, n, kind_note))
    if found:
        L.append("SETTLED ENGLISH for terms in this chunk (use these exactly):")
        L.extend("  %s = %s" % (z, e) for z, e in found)
    L.append("ROWS (line breaks are written \\n; '.' rows are published examples of the same kind, '*N' rows are yours):")
    L.extend(body)
    return "\n".join(L) + "\n", ids


def write_chunk(setname, n, text, ids, lash_lines, meta):
    d = pdir("chunks", setname)
    os.makedirs(d, exist_ok=True)
    U.write_text(os.path.join(d, "p_%04d.txt" % n), text)
    U.write_json(os.path.join(d, "p_%04d.json" % n), dict(meta, chunk=n, items=ids), indent=0)
    U.write_text(os.path.join(d, "l_%04d.txt" % n), "\n".join("%s | %s" % (i, esc(lash_lines.get(i, "(none)"))) for i in ids) + "\n")


def lash_for(st, ids):
    return {i: st.lash.get(v["key"], "") or "" for i, v in ids.items()}


def reset_set(setname):
    d = pdir("chunks", setname)
    os.makedirs(d, exist_ok=True)
    for f in os.listdir(d):
        if re.match(r"^[pl]_\d{4}\.(txt|json)$", f) or f == "manifest.json":
            os.remove(os.path.join(d, f))


def cmd_plan(st):
    cls = U.classify_added(st)
    st._cls = cls
    c = collections.Counter(v[0] for v in cls.values())
    print("added keys:", len(st.added), dict(c))
    print("changed keys:", len(st.changed), "| removed keys:", len(st.removed))
    todo = [k for k, (cl, x) in cls.items() if cl == "fresh" and k not in st.ws and k not in st.ov]
    fam = collections.Counter(("Story/" + re.match(r"([A-Z]+)_", k.split("/")[1]).group(1)) if k.startswith("Story/") and re.match(r"([A-Z]+)_", k.split("/")[1]) else k.split("/")[0] for k in todo)
    print("fresh rows still without English:", len(todo))
    for f, n in fam.most_common():
        print("  %-24s %6d" % (f, n))
    U.write_json(pdir("plan.json"), {"counts": dict(c), "classes": {k: [a, b] for k, (a, b) in cls.items()}})
    return cls


def cmd_story(st, per, only):
    cls = cmd_plan(st)
    terms = build_terms(st)
    chunks, voice_pool, ntodo, nunref = chunk_story(st, per, set(only.split(",")) if only else None)
    reset_set("story")
    manifest = []
    for n, ch in enumerate(chunks, 1):
        text, ids = render_story(st, ch, terms, voice_pool, n, "story")
        meta = {"set": "story", "category": "story", "scripts": [s["script"] for s in ch["segs"]]}
        write_chunk("story", n, text, ids, lash_for(st, ids), meta)
        manifest.append({"chunk": n, "category": "story", "editable": len(ids), "scripts": meta["scripts"][:3], "chars": len(text)})
    U.write_json(pdir("chunks", "story", "manifest.json"), manifest)
    print("story: %d chunks, %d editable lines (%d of the todo list; %d not reached by a script), mean %.1f lines, mean view %d chars" % (
        len(chunks), sum(m["editable"] for m in manifest), ntodo, nunref, sum(m["editable"] for m in manifest) / max(1, len(manifest)),
        sum(m["chars"] for m in manifest) / max(1, len(manifest))))


def cmd_legend(st, per):
    cls = cmd_plan(st)
    terms = build_terms(st)
    keys = sorted(k for k, (c, x) in cls.items() if c == "fresh" and k.startswith("LegendInfo/") and k not in st.ws)
    reset_set("legend")
    manifest = []
    allk = sorted(k for k in st.new if k.startswith("LegendInfo/"))
    for n, grp in enumerate(chunk_rows(keys, per), 1):
        i = allk.index(grp[0])
        ctx = [(k, st.new[k], st.english(k)) for k in allk[max(0, i - 3):i] if st.english(k)]
        text, ids = render_rows(st, [("LegendInfo chronicle entries in key order (Ch_<chapter>_<scene>_<n>: the entries of one scene sit together; "
                                       "each is a third-person summary the player reads later in the Legend screen)", grp, ctx)],
                                terms, n, "legend", "")
        write_chunk("legend", n, text, ids, lash_for(st, ids), {"set": "legend", "category": "legend"})
        manifest.append({"chunk": n, "category": "legend", "editable": len(ids), "chars": len(text)})
    U.write_json(pdir("chunks", "legend", "manifest.json"), manifest)
    print("legend: %d chunks, %d rows" % (len(manifest), sum(m["editable"] for m in manifest)))


def cmd_data(st, per):
    cls = cmd_plan(st)
    terms = build_terms(st)
    keys = [k for k, (c, x) in cls.items() if c == "fresh" and not k.startswith(("Story/", "LegendInfo/")) and k not in st.ws and k not in st.ov]
    fam = collections.defaultdict(list)
    for k in keys:
        fam[k.split("/")[0]].append(k)
    for f in fam:
        fam[f].sort(key=entity_key)
    reset_set("data")
    # chunks: (family, keys) pieces, small families share a chunk
    pieces = []
    for f in sorted(fam):
        ks = fam[f]
        for i in range(0, len(ks), per):
            pieces.append((f, ks[i:i + per]))
    chunks, cur = [], None
    for f, ks in pieces:
        if cur and cur["n"] + len(ks) <= per and len(ks) < per:
            cur["parts"].append((f, ks))
            cur["n"] += len(ks)
        else:
            cur = {"parts": [(f, ks)], "n": len(ks)}
            chunks.append(cur)
    manifest = []
    for n, ch in enumerate(chunks, 1):
        groups = []
        for f, ks in ch["parts"]:
            groups.append(("family %s (rows of one entity are next to each other: Name, Desc, ...)" % f, ks, exemplars(st, ks[0])))
        text, ids = render_rows(st, groups, terms, n, "data",
                                "These are interface texts and names: keep them short enough for small boxes (a name about as long as the Chinese suggests; descriptions concise).")
        write_chunk("data", n, text, ids, lash_for(st, ids), {"set": "data", "category": "data", "families": [f for f, _ in ch["parts"]]})
        manifest.append({"chunk": n, "category": "data", "editable": len(ids), "families": [f for f, _ in ch["parts"]], "chars": len(text)})
    U.write_json(pdir("chunks", "data", "manifest.json"), manifest)
    print("data: %d chunks, %d rows" % (len(manifest), sum(m["editable"] for m in manifest)))


def cmd_changed(st, per):
    """The rows whose Chinese changed in the update: the new Chinese, the old Chinese the current English was written for, and the
    current English (the strings override when there is one). Story rows in scene order, the others in key order. Rows whose
    Chinese only gained or lost spaces (the line breaks unchanged) keep their English unreviewed: they are listed in
    update_pass/restamp_spaces.txt for publish.py source --restamp."""
    def spaces_only(k):
        a, b = st.old[k].replace("\r\n", "\n"), st.new[k].replace("\r\n", "\n")
        return a.count("\n") == b.count("\n") and re.sub(r"[ \t　]", "", a) == re.sub(r"[ \t　]", "", b)
    spaced = sorted(k for k in st.changed if st.english(k).strip() and spaces_only(k))
    U.write_text(pdir("restamp_spaces.txt"), "".join(k + "\n" for k in spaced))
    keys = [k for k in st.changed if st.english(k).strip() and CJK.search(st.new[k]) and k not in set(spaced)]
    extra = {k: (st.old[k], st.english(k)) for k in keys}
    story = {k for k in keys if k.startswith("Story/")}
    terms = build_terms(st)
    chunks, voice_pool, ntodo, nunref = chunk_story(st, per, None, todo=story, hints={}, extra=extra)
    other = sorted(k for k in keys if k not in story)
    items = [{"type": "line", "key": k, "kind": "chronicle" if k.startswith("LegendInfo/") else "data", "sid": None, "speaker": None,
              "zh": st.new[k], "en": "", "edit": True, "old": extra[k][0], "cur": extra[k][1]} for k in other]
    for seg in cut_scene(items, per):
        seg["script"], seg["cast"], seg["family"] = "changed rows outside the story scripts (key order)", [], "~other"
        chunks.append({"family": "~other", "segs": [seg], "editable": n_edit(seg)})
    # the changes are scattered over many scenes: pack neighbours into chunks of up to `per` rows whatever their script family
    packed, cur = [], None
    for ch in chunks:
        if cur and cur["editable"] + ch["editable"] <= per:
            cur["segs"] += ch["segs"]
            cur["editable"] += ch["editable"]
        else:
            cur = {"family": "~", "segs": list(ch["segs"]), "editable": ch["editable"]}
            packed.append(cur)
    chunks = packed
    reset_set("changed")
    manifest = []
    for n, ch in enumerate(chunks, 1):
        text, ids = render_story(st, ch, terms, voice_pool, n, "changed")
        meta = {"set": "changed", "category": "changed", "scripts": [s["script"] for s in ch["segs"]]}
        write_chunk("changed", n, text, ids, lash_for(st, ids), meta)
        manifest.append({"chunk": n, "category": "changed", "editable": len(ids), "scripts": meta["scripts"][:3], "chars": len(text)})
    U.write_json(pdir("chunks", "changed", "manifest.json"), manifest)
    print("changed: %d chunks, %d rows (%d story rows, %d others; %d not reached by a script); %d rows only changed spaces "
          "(restamp_spaces.txt)" % (len(chunks), sum(m["editable"] for m in manifest), len(story), len(other), nunref, len(spaced)))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("cmd", choices=["plan", "story", "legend", "data", "changed"])
    ap.add_argument("--per", type=int)
    ap.add_argument("--scripts")
    a = ap.parse_args()
    st = U.State()
    st._cls = U.classify_added(st)
    if a.cmd == "plan":
        cmd_plan(st)
    elif a.cmd == "story":
        cmd_story(st, a.per or 80, a.scripts)
    elif a.cmd == "changed":
        cmd_changed(st, a.per or 80)
    elif a.cmd == "legend":
        cmd_legend(st, a.per or 30)
    else:
        cmd_data(st, a.per or 70)


if __name__ == "__main__":
    main()
