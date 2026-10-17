"""Update pass: how reliably does each glossary term's settled English show up where its Chinese does? Terms that rarely do
(老子 as a rude 'I', 自己, 下山 ...) mislead a translator who is told to use them, so make_chunks leaves them out of the hint list.
Writes update_pass/term_stats.json {zh: [rows containing it, rows whose English holds its settled English]}.
Usage: python term_stats.py"""
import collections, json, os, re, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import upd_common as U


def words(en):
    return [w for w in re.findall(r"[A-Za-z][A-Za-z'’-]+", en.lower()) if len(w) > 2]


def main():
    st = U.State(lash=False)
    rows = [(st.new[k], st.english(k).lower()) for k in st.ws if k in st.new and k not in st.added_set and k.startswith(("Story/", "LegendInfo/", "CombatTalking/", "BattleDialog/"))]
    print(len(rows), "published rows")
    terms = {}
    for zh, en, ruled in U.glossary_terms() + [(z, e, True) for z, e in U.character_names(st).items()]:
        terms.setdefault(zh, (en, ruled))
    stats = {}
    for i, (zh, (en, ruled)) in enumerate(terms.items()):
        alts = [a.strip().lower() for a in re.split(r"\s*/\s*|;", en) if a.strip()]
        n = hit = 0
        for z, e in rows:
            if zh in z:
                n += 1
                if any(a in e for a in alts):
                    hit += 1
                if n >= 400:
                    break
        stats[zh] = [n, hit]
    U.write_json(U.pdir("term_stats.json"), stats)
    low = [(zh, n, hit) for zh, (n, hit) in stats.items() if n >= 8 and hit / n < 0.5]
    print(len(stats), "terms;", len(low), "with n>=8 and hit rate < 50%")
    for zh, n, hit in sorted(low, key=lambda x: -x[1])[:40]:
        print("  %-10s %4d %4d  %s" % (zh, n, hit, terms[zh][0][:40]))


if __name__ == "__main__":
    main()
