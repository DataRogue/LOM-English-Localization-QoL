"""Every 4-word run (and whole long OverLlm value) that publish.py's scan for OverLlm text would flag, per line of the given
files, so a shipped file this mod writes itself (factiontips.tsv, a README) can be reworded before publish.py build refuses it.

usage: python scan_overllm.py FILE [FILE...]   (loads the workspace and BASE_REF once, about a minute)"""
import io, os, re, sys

TOOLS = r"C:\Program Files (x86)\Steam\steamapps\common\LegendOfMortal\LOM_Localization\tools"
sys.path.insert(0, TOOLS)
import publish as PUB

inp = PUB.Inputs()
# the same "ours" publish.py builds for the planned files (make_plan): this mod's revised/added table and scene text + strings
tt = inp.ws_tt
ours = PUB.Ours([v for k, v in tt.items() if inp.base_tt.get(k) != v]
                + [v for k, v in inp.ws_scene.map.items() if inp.base_scene.map.get(k) != v]
                + PUB.strings_texts(inp.ws_strings.values()), inp.ol)


def all_hits(text):
    out = []
    nt = PUB._norm(text)
    ws = PUB._WORD.findall(nt)
    for i in range(len(ws) - 3):
        g = " ".join(ws[i:i + 4])
        if g in inp.ol.grams4 and g not in ours.grams4 and PUB._wordy(ws[i:i + 4]):
            out.append(g)
    for what, v in PUB.scan_text(text, inp.ol, ours):
        if what != "4 words only OverLlm's text has":
            out.append("WHOLE VALUE: " + v)
    return out


for path in sys.argv[1:]:
    with io.open(path, encoding="utf-8") as f:
        for n, line in enumerate(f.read().split("\n"), 1):
            if not line.strip():
                continue
            h = all_hits(line)
            if h:
                print("%s:%d: %s\n    -> %s" % (os.path.basename(path), n, line[:110], " | ".join(h)))
print("scan done")
