"""Update pass: vocabulary that first appears in the new text (names, places, techniques, titles ...), so one English can be
settled for each before the story is translated in 300 separate chunks.

A candidate is a run of 2-6 Chinese characters that occurs in at least MIN_DF lines still to translate, hardly ever in the
published text, is no part of a longer candidate that is about as frequent, and is no known glossary term or character name.
Writes update_pass/term_candidates.json [{zh, df, old, samples: [context, ...]}] sorted by frequency, and batches
update_pass/terms/c_NNN.txt (60 candidates each, for the Sonnet term deciders).
Usage: python term_candidates.py [--min-df 4]
"""
import argparse, collections, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import upd_common as U
from upd_common import CJK, pdir

RUN = re.compile(r"[㐀-鿿豈-﫿]+")
STOP_CHARS = set("的了是我你他她它們在有不就也都和與而但把被讓給對從到說這那個一二三四五六七八九十")


def grams(text, nmin=2, nmax=6):
    out = set()
    for run in RUN.findall(text):
        for n in range(nmin, nmax + 1):
            for i in range(0, len(run) - n + 1):
                out.add(run[i:i + n])
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--min-df", type=int, default=4)
    ap.add_argument("--batch", type=int, default=60)
    a = ap.parse_args()
    st = U.State(lash=False)
    cls = U.classify_added(st)
    todo = [k for k, (c, x) in cls.items() if c in ("fresh", "move") and k.startswith(("Story/", "LegendInfo/", "CombatTalking/", "BattleDialog/"))]
    texts_new = [st.new[k] for k in todo]
    old_keys = [k for k in st.new if k in st.ws and k not in st.added_set]
    texts_old = [st.new[k] for k in old_keys]
    print(len(texts_new), "new lines;", len(texts_old), "published lines")
    df_new, df_old = collections.Counter(), collections.Counter()
    for t in texts_new:
        df_new.update(grams(t))
    cands = {g: n for g, n in df_new.items() if n >= a.min_df and not (set(g) & STOP_CHARS and len(g) <= 3 and g[0] in STOP_CHARS)}
    for t in texts_old:
        for g in grams(t) & cands.keys():
            df_old[g] += 1
    cands = {g: n for g, n in cands.items() if df_old[g] <= max(2, 0.12 * n)}
    # known terms: glossary, names, the term sheet
    known = {z for z, e, r in U.glossary_terms()} | set(U.character_names(st)) | {z for z, e, r in U.new_term_sheet()}
    known_parts = set()
    for z in known:
        for n in range(2, min(len(z), 6) + 1):
            for i in range(0, len(z) - n + 1):
                known_parts.add(z[i:i + n])
    cands = {g: n for g, n in cands.items() if g not in known_parts and not any(g in z for z in known if len(z) <= 8 and len(z) >= len(g))}
    # keep maximal runs: drop g when a longer candidate containing it is nearly as frequent
    keep = {}
    bylen = sorted(cands, key=len, reverse=True)
    for g in bylen:
        if any(g in h and cands[h] >= 0.7 * cands[g] for h in keep if len(h) > len(g)):
            continue
        keep[g] = cands[g]
    print(len(cands), "candidates;", len(keep), "after keeping the maximal ones")
    # function characters at an edge mean a fragment ('小竹的', '跟小梅', '位護法'), not a term
    edge = set("的了是在跟和與而但把被讓給對從到這那個也都就還又才只能會要可以去來見說有沒不位代")
    keep = {g: n for g, n in keep.items() if g[0] not in edge and g[-1] not in edge}
    # only terms that occur in more than one chunk need a sheet: within one chunk the translator is consistent by itself
    chunk_of = {}
    for sd in ("story", "legend", "data"):
        d = pdir("chunks", sd)
        if not os.path.isdir(d):
            continue
        for f in os.listdir(d):
            if f.startswith("p_") and f.endswith(".json"):
                for it in U.read_json(os.path.join(d, f))["items"].values():
                    chunk_of[it["key"]] = sd + ":" + f[2:6]
    spread = {}
    for g in keep:
        spread[g] = len({chunk_of.get(k) for k in todo if g in st.new[k]} - {None})
    keep = {g: n for g, n in keep.items() if spread[g] >= 2}
    print(len(keep), "after dropping fragments and single-chunk terms")
    rows = []
    for g, n in sorted(keep.items(), key=lambda kv: (-kv[1], kv[0])):
        samples = []
        for t in texts_new:
            i = t.find(g)
            if i >= 0:
                samples.append(t[max(0, i - 14):i + len(g) + 14].replace(chr(10), " "))
                if len(samples) == 2:
                    break
        rows.append({"zh": g, "df": n, "old": df_old[g], "chunks": spread[g], "samples": samples})
    U.write_json(pdir("term_candidates.json"), rows)
    os.makedirs(pdir("terms"), exist_ok=True)
    nb = 0
    for i in range(0, len(rows), a.batch):
        nb += 1
        body = ["CANDIDATE TERMS %d (%d). Each: zh | lines and chunks of the new text containing it | published lines containing it | two contexts" % (nb, len(rows[i:i + a.batch]))]
        for j, r in enumerate(rows[i:i + a.batch], 1):
            body.append("%d. %s | %d lines in %d chunks | %d in published | %s" % (j, r["zh"], r["df"], r["chunks"], r["old"], " ~ ".join(r["samples"])))
        U.write_text(pdir("terms", "c_%03d.txt" % nb), chr(10).join(body) + chr(10))
    print("wrote %d batches" % nb)
    for r in rows[:60]:
        print("  %-10s %4d %3d  %s" % (r["zh"], r["df"], r["old"], r["samples"][0] if r["samples"] else ""))


if __name__ == "__main__":
    main()
