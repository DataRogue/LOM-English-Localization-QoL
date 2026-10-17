"""Update pass, step 1: dump the story scripts (Lua TextAssets) of the installed game into gamedata/lua.

The game's resources.assets holds up to two assets per script name: the key-based one (say(luamanager.GetStoryText("D_..."))),
which the string tables serve, and an inline-text one in Simplified Chinese that this mod never sees. The key-based variant
(the one with the most GetStoryText calls) is kept. The previous set is moved to gamedata/lua_<tag> first (--keep-old TAG), so
the walkers of the older passes can still be pointed at it.

Usage: python extract_lua.py [--keep-old TAG] [--out DIR]
Reads Mortal_Data/resources.assets (read only; UnityPy). Writes only below LOM_Localization/gamedata (lom_paths.guard_write).
"""
import argparse, collections, glob, hashlib, json, os, re, shutil, sys

sys.stdout.reconfigure(encoding="utf-8")
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
import lom_paths as P

RX_KEY = re.compile(r'GetStoryText\("([^"]+)"\)')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--keep-old", default="1.0.5000.13", help="the old set is moved to gamedata/lua_<TAG> (default: the previous game version)")
    ap.add_argument("--out", default=os.path.join(P.REAL_LOCALIZATION, "gamedata", "lua"))
    a = ap.parse_args()
    import UnityPy
    src = os.path.join(P.GAME, "Mortal_Data", "resources.assets")
    env = UnityPy.load(src)
    by_name = collections.defaultdict(list)
    for obj in env.objects:
        if obj.type.name != "TextAsset":
            continue
        ta = obj.read()
        raw = ta.m_Script
        data = raw.encode("utf-8", "surrogateescape") if isinstance(raw, str) else bytes(raw)
        by_name[ta.m_Name].append((obj.path_id, data))
    chosen, manifest = {}, {}
    for name, lst in by_name.items():
        scored = [(len(RX_KEY.findall(d.decode("utf-8", "replace"))), -i, d) for i, (pid, d) in enumerate(lst)]
        scored.sort(reverse=True)
        n, negi, d = scored[0]
        chosen[name] = d
        manifest[name] = {"assets": len(lst), "chosen": -negi, "story_keys": n,
                          "identical": len({hashlib.md5(x[2]).hexdigest() for x in scored}) == 1}
    out = a.out
    P.guard_write(out)
    if os.path.isdir(out) and glob.glob(os.path.join(out, "*.lua")):
        old = os.path.join(os.path.dirname(out), "lua_" + a.keep_old)
        if os.path.exists(old):
            sys.exit("%s already exists: remove or rename it first (the old script set is moved there)" % old)
        shutil.move(out, old)
        print("old scripts moved to", old)
    os.makedirs(out, exist_ok=True)
    for name, d in chosen.items():
        # names such as 'LineBreaking Following Characters' are text assets that are no scripts; keep every asset anyway
        with open(P.guard_write(os.path.join(out, name + ".lua")), "wb") as f:
            f.write(d)
    with open(P.guard_write(os.path.join(out, "_manifest.json")), "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=0, sort_keys=True)
    keyed = sum(1 for m in manifest.values() if m["story_keys"])
    print("wrote %d scripts to %s (%d with story keys; %d names had two assets)" % (
        len(chosen), out, keyed, sum(1 for m in manifest.values() if m["assets"] > 1)))


if __name__ == "__main__":
    main()
