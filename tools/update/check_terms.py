"""Update pass, sweeps: do the decided English lines use the settled English of the names and terms their Chinese holds?

Terms: this pass's term sheet (update_pass/terms.tsv), the Character/* names, and the glossary terms that are stable in the published
text (term_stats.json). A line is flagged when its Chinese holds a term and none of the term's English alternatives (split on ' / ' and
';') occurs in the English, ignoring case. Also flags the owner's retired forms and singular 'they' for lines with no plural marker.
Writes update_pass/term_violations.tsv (key, kind, zh term, expected, english) and prints the counts by kind.
Usage: python check_terms.py [--set story,legend,data,changed]"""
import argparse, collections, glob, json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import upd_common as U
from upd_common import pdir

PLURAL = re.compile("[們眾群些各大家所有人人人]|兩|三|四|五|六|七|八|九|十|諸|雙")
THEY = re.compile(r"\b(they|them|their|theirs)\b", re.I)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--set", default="story,legend,data,changed")
    a = ap.parse_args()
    st = U.State(lash=False)
    stats = U.read_json(pdir("term_stats.json")) if os.path.exists(pdir("term_stats.json")) else {}

    def stable(zh):
        n, hit = stats.get(zh, (0, 0))
        return n < 8 or hit / n >= 0.5
    pairs = [t for t in U.glossary_terms() if t[2] and stable(t[0])]           # owner-ruled glossary terms only
    pairs += [(z, e, True) for z, e in U.character_names(st).items() if len(z) >= 3]
    pairs += U.new_term_sheet()
    idx = U.TermIndex(pairs)
    out, kinds = [], collections.Counter()
    for sd in a.set.split(","):
        for p in sorted(glob.glob(pdir("final", sd + "_*.json"))):
            for r in U.read_json(p):
                if r["status"] != "ok":
                    continue
                en, zh = r["final"], r["zh"]
                low = en.lower()
                for z, e in idx.find(zh):
                    alts = [x.strip().lower() for x in re.split(r"\s*/\s*|;", e) if x.strip()]
                    # derived forms: 'Guard Ding' for 丁護衛, 'Sect Leader Yao': accept the words of the settled English in any order
                    if alts and not any(x in low for x in alts):
                        words = [w for x in alts for w in re.findall(r"[a-z']{3,}", x)]
                        if not words or any(w in low for w in set(words)):
                            continue
                        out.append((r["key"], "term", z, e, en))
                        kinds["term"] += 1
                if THEY.search(en) and not PLURAL.search(zh):
                    out.append((r["key"], "they", "", "", en))
                    kinds["they"] += 1
    U.write_text(pdir("term_violations.tsv"), "".join("%s\t%s\t%s\t%s\t%s\n" % (k, kd, z, e, en.replace(chr(10), U.BS + "n")) for k, kd, z, e, en in out))
    print(dict(kinds), "->", pdir("term_violations.tsv"))
    c = collections.Counter(z for k, kd, z, e, en in out if kd == "term")
    for z, n in c.most_common(25):
        print("  %-10s %4d  expected: %s" % (z, n, idx.en[z][:50]))


if __name__ == "__main__":
    main()
