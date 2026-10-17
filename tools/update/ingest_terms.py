"""Update pass: turn the journal of terms_workflow.js into update_pass/terms_proposed.tsv (zh TAB English TAB category TAB note TAB
lines), for the maintainer's review. Approved lines are appended to update_pass/terms.tsv (the term sheet every chunk view uses).
Usage: python ingest_terms.py JOURNAL [--batch-size 50]"""
import argparse, io, json, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import upd_common as U
from upd_common import pdir


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("journal")
    ap.add_argument("--batch-size", type=int, default=50)
    a = ap.parse_args()
    rows = U.read_json(pdir("term_candidates.json"))
    label_of, res = {}, {}
    for line in io.open(a.journal, encoding="utf-8"):
        try:
            e = json.loads(line)
        except ValueError:
            continue
        if e.get("type") == "started" and e.get("agentId"):
            label_of[e["agentId"]] = e.get("label", "")
        elif e.get("type") == "result" and e.get("agentId") and isinstance(e.get("result"), dict):
            res[label_of.get(e["agentId"], "")] = e["result"]
    out, n_real = [], 0
    for lab, r in sorted(res.items()):
        if not lab.startswith("terms:"):
            continue
        b = int(lab.split(":")[1])
        for it in r.get("items") or []:
            idx = (b - 1) * a.batch_size + it["n"] - 1
            if not (0 <= idx < len(rows)):
                continue
            c = rows[idx]
            if it.get("real") and (it.get("en") or "").strip():
                n_real += 1
                out.append((c["zh"], it["en"].strip(), it.get("cat", ""), (it.get("note") or "").replace("\t", " "), c["df"]))
    out.sort(key=lambda t: -t[4])
    U.write_text(pdir("terms_proposed.tsv"), "".join("%s\t%s\t%s\t%s\t%d\n" % t for t in out))
    print("%d candidates decided, %d real terms -> %s" % (sum(len(r.get("items") or []) for r in res.values()), n_real, pdir("terms_proposed.tsv")))
    for t in out:
        print("  %-8s %-34s %-9s %3d  %s" % (t[0], t[1][:34], t[2], t[4], t[3]))


if __name__ == "__main__":
    main()
