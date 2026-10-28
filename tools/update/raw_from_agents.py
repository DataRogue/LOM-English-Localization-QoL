"""Update pass, step 5 without the Workflow tool: the results of lom-text agents (Agent tool) -> update_pass/raw, as collect would.

A small pass (the 2026-10-27 update: about 130 new rows) runs the translate and compare steps as a few lom-text agents, each over
several chunks, instead of one update_workflow.js run per chunk. Each agent answers with
{"chunks": [{"set": S, "chunk": N, "items": [...]}]}; save that JSON to a file, then:

  python raw_from_agents.py translate FILE...   items {n, en} (changed set: {n, kept, en?}; a kept line takes the current English)
                                                -> raw/<set>_<NNNN>.json "translate", and chunks/<set>/t_NNNN.txt for the reviewer
                                                (n TAB English, backslash-n for a line break)
  python raw_from_agents.py compare FILE...     items {n, verdict, lash, who?, en?, note?} -> raw/<set>_<NNNN>.json "compare"
  python raw_from_agents.py referee FILE...     {"items": [{id, pick, en?, reason}]} -> raw_referee/batch_NNN.json (one file per
                                                batch, named ..._NNN.json after the batch number)

Every chunk must come back whole (each line number of its p_NNNN.json once). Then apply_update.py batches / decide / apply as usual.
"""
import json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import upd_common as U


def main():
    role, files = sys.argv[1], sys.argv[2:]
    if role not in ("translate", "compare", "referee") or not files:
        sys.exit(__doc__)
    if role == "referee":
        for fn in files:
            m = re.search(r"(\d+)\.json$", fn)
            if not m:
                sys.exit("%s: name the file after its batch number (..._003.json)" % fn)
            items = json.load(open(fn, encoding="utf-8"))["items"]
            os.makedirs(U.pdir("raw_referee"), exist_ok=True)
            U.write_json(U.pdir("raw_referee", "batch_%03d.json" % int(m.group(1))), items)
            print("referee batch", int(m.group(1)), len(items))
        return
    st = U.State(lash=False) if role == "translate" else None
    for fn in files:
        for ch in json.load(open(fn, encoding="utf-8"))["chunks"]:
            s, n = ch["set"], int(ch["chunk"])
            ids = U.read_json(U.pdir("chunks", s, "p_%04d.json" % n))["items"]
            items = []
            for it in ch["items"]:
                it = dict(it)
                if role == "translate" and s == "changed" and it.get("kept"):
                    it["en"] = st.english(ids[str(it["n"])]["key"]).replace("\r\n", "\n")
                items.append(it)
            got, want = sorted(int(x["n"]) for x in items), sorted(int(k) for k in ids)
            if got != want:
                sys.exit("%s:%d: items %s, the chunk has %s" % (s, n, got, want))
            path = U.pdir("raw", "%s_%04d.json" % (s, n))
            raw = U.read_json(path) if os.path.exists(path) else {}
            raw[role] = items
            U.write_json(path, raw)
            if role == "translate":
                U.write_text(U.pdir("chunks", s, "t_%04d.txt" % n), "".join(
                    "%d\t%s\n" % (x["n"], x["en"].replace("\n", U.BS + "n")) for x in sorted(items, key=lambda x: x["n"])))
            print(s, n, role, len(items))


if __name__ == "__main__":
    main()
