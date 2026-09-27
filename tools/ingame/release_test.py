"""Release test: set up and undo the in-game test scenarios of a release candidate, exactly and verifiably.

  python release_test.py prep | a_setup | a_teardown | b_setup | b_teardown | final | status

  prep        refuses while the game runs; backs up the save folder and the game's registry key, parks the harness output
  a_setup     scenario A, a clean install: Lash's English Patch aside (overllm_aside.ps1 out), the BepInEx/core files the
              official be.692 zip lacks aside (Newtonsoft.Json among them), the stock Unity.Addressables.dll in, the plugin
              folder and this mod's and BepInEx's configs aside, the newest release/dist/*-candidate/*-full.zip extracted as a
              player would, a fresh lom.ui.english.cfg with only the harness on, steam_appid.txt
  a_teardown  compares the extracted plugin folder with the -mod-only zip, then undoes all of A (repeatable: each attempt's
              leftovers go to A<n>_* in the run folder)
  b_setup     scenario B, the normal setup: harness on in the player's cfg, steam_appid.txt
  b_teardown  harness off, the BepInEx log kept
  final       saves put back (changed files restored, new files moved out, then compared with the backup), the registry
              key imported and checked value by value, steam_appid.txt removed, the harness output kept in the run folder

Launch the game in between (Mortal.exe, working directory the game folder) and drive it with run_batch.ps1 and
screens05.ps1; docs/DEVELOPMENT.md has the whole checklist. Every move and overwrite is recorded in state.json and verified
by SHA-256; every step refuses while the game runs. Nothing is deleted: files the test created are moved into the run
folder (LOM_Localization/_release_test/run_<stamp>, outside the repository).
"""
import glob, hashlib, json, os, re, shutil, subprocess, sys, time, zipfile

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import lom_paths as P

G = P.GAME
LOC = P.REAL_LOCALIZATION
HERE = os.path.join(LOC, "_release_test")
STATE = os.path.join(HERE, "state.json")
REGKEY = r"HKCU\Software\Obb Studio\Mortal"


def _saves_folder():
    """The game's save folder: the one Steam-id-named folder under LocalLow/Obb Studio/Mortal."""
    base = os.path.join(os.path.expanduser("~"), "AppData", "LocalLow", "Obb Studio", "Mortal")
    ids = [d for d in (os.listdir(base) if os.path.isdir(base) else []) if d.isdigit() and os.path.isdir(os.path.join(base, d))]
    if len(ids) != 1:
        sys.exit("cannot tell the save folder: %d Steam-id folders in %s" % (len(ids), base))
    return os.path.join(base, ids[0])


SAVES = _saves_folder()
HOLD = os.path.join(G, "BepInEx", "_lom_release_test")          # holding area inside the game (not scanned by BepInEx)
PLUGIN = os.path.join(G, "BepInEx", "plugins", "LOM_UI_EN")
CORE = os.path.join(G, "BepInEx", "core")
CFG = os.path.join(G, "BepInEx", "config")
ADDR = os.path.join(G, "Mortal_Data", "Managed", "Unity.Addressables.dll")
ADDR_STOCK = os.path.join(G, "BepInEx", "backup_stock", "Unity.Addressables.dll.stock-20337760")
HARNESS_OUT = os.path.join(G, "BepInEx", "cache", "LOM_UI_EN", "harness", "out")
BEPINEX_ZIP = os.path.join(LOC, "release", "vendor", "BepInEx-Unity.Mono-win-x86-6.0.0-be.692+851521c.zip")


def _candidate(kind):
    """The newest release/dist/*-candidate/*-<kind>.zip (release.py --candidate writes them)."""
    found = sorted(glob.glob(os.path.join(LOC, "release", "dist", "*-candidate", "*-candidate-%s.zip" % kind)), key=os.path.getmtime)
    if not found:
        sys.exit("no candidate %s zip: run python tools/release.py --candidate first" % kind)
    return found[-1]
OVERLLM_ASIDE = os.path.join(LOC, "tools", "ingame", "overllm_aside.ps1")
APPID = os.path.join(G, "steam_appid.txt")


def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest()


def tree_hashes(root):
    out = {}
    for r, ds, fs in os.walk(root):
        for f in fs:
            p = os.path.join(r, f)
            out[os.path.relpath(p, root).replace("\\", "/")] = sha(p)
    return out


def game_running():
    r = subprocess.run(["tasklist", "/FI", "IMAGENAME eq Mortal.exe"], capture_output=True, text=True)
    return "Mortal.exe" in r.stdout


def need_closed():
    if game_running():
        sys.exit("REFUSED: the game is running")


def load():
    return json.load(open(STATE, encoding="utf-8")) if os.path.exists(STATE) else {}


def save(st):
    os.makedirs(HERE, exist_ok=True)
    json.dump(st, open(STATE, "w", encoding="utf-8"), indent=1)


def move_verified(src, dst):
    """Move a file or folder, verifying every file's hash."""
    before = tree_hashes(src) if os.path.isdir(src) else {"": sha(src)}
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    if os.path.exists(dst):
        sys.exit("REFUSED: %s exists" % dst)
    shutil.move(src, dst)
    after = tree_hashes(dst) if os.path.isdir(dst) else {"": sha(dst)}
    if before != after:
        sys.exit("HASH MISMATCH moving %s -> %s" % (src, dst))
    return len(before)


def ps(script, *args):
    r = subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script] + list(args), capture_output=True, text=True)
    print(r.stdout.strip())
    if r.returncode != 0:
        sys.exit("FAILED: %s %s (%d): %s" % (os.path.basename(script), args, r.returncode, r.stderr.strip()))


def prep():
    need_closed()
    st = load()
    if st.get("run"):
        sys.exit("REFUSED: a run is already prepared: %s" % st["run"])
    run = os.path.join(HERE, "run_" + time.strftime("%Y%m%d-%H%M%S"))
    os.makedirs(run)
    shutil.copytree(SAVES, os.path.join(run, "saves_backup"))
    st["saves_hashes"] = tree_hashes(SAVES)
    r = subprocess.run(["reg", "export", REGKEY, os.path.join(run, "registry_backup.reg"), "/y"], capture_output=True, text=True)
    if r.returncode != 0:
        sys.exit("reg export failed: %s" % r.stderr)
    if os.path.isdir(HARNESS_OUT) and os.listdir(HARNESS_OUT):
        n = move_verified(HARNESS_OUT, os.path.join(run, "harness_out_before"))
        st["harness_out_moved"] = n
    st["run"] = run
    save(st)
    print("prepared %s: saves %d files backed up, registry exported, harness out moved (%s files)" % (run, len(st["saves_hashes"]), st.get("harness_out_moved", 0)))


def official_core():
    with zipfile.ZipFile(BEPINEX_ZIP) as z:
        return {os.path.basename(i.filename) for i in z.infolist() if i.filename.startswith("BepInEx/core/") and not i.is_dir()}


def a_setup():
    need_closed()
    st = load()
    run = st["run"]
    if st.get("a"):
        sys.exit("REFUSED: scenario A is already set up")
    a = {}
    # 1. Lash's English Patch aside (its own hash-verified script)
    ps(OVERLLM_ASIDE, "out")
    a["overllm_aside"] = True
    # 2. BepInEx/core files the official be.692 zip does not have (the KR/OverLlm package's extras, Newtonsoft.Json among them)
    extras = sorted(f for f in os.listdir(CORE) if f not in official_core())
    for f in extras:
        move_verified(os.path.join(CORE, f), os.path.join(HOLD, "core_extras", f))
    a["core_extras"] = extras
    # 3. stock Unity.Addressables.dll (keep the patched one)
    a["addr_patched_sha"] = sha(ADDR)
    shutil.copy2(ADDR, os.path.join(HOLD, "Unity.Addressables.dll.patched"))
    if sha(os.path.join(HOLD, "Unity.Addressables.dll.patched")) != a["addr_patched_sha"]:
        sys.exit("copy of the patched Addressables failed")
    shutil.copy2(ADDR_STOCK, ADDR)
    a["addr_stock_sha"] = sha(ADDR)
    # 4. the installed plugin folder, and this mod's and BepInEx's settings (a fresh install has none)
    a["plugin_files"] = move_verified(PLUGIN, os.path.join(HOLD, "LOM_UI_EN_installed"))
    a["configs"] = []
    for f in ("lom.ui.english.cfg", "lom.strings.english.cfg", "BepInEx.cfg"):
        p = os.path.join(CFG, f)
        if os.path.exists(p):
            move_verified(p, os.path.join(HOLD, "config_user", f))
            a["configs"].append(f)
    # 5. the full package, extracted as a player would; every overwrite recorded
    created, overwritten = [], {}
    a["full_zip"] = _candidate("full")
    with zipfile.ZipFile(a["full_zip"]) as z:
        for i in z.infolist():
            target = os.path.join(G, *i.filename.rstrip("/").split("/"))
            if i.is_dir():
                if not os.path.isdir(target):
                    os.makedirs(target)
                    created.append(i.filename)
                continue
            data = z.read(i)
            if os.path.exists(target):
                old = sha(target)
                if old != hashlib.sha256(data).hexdigest():
                    keep = os.path.join(HOLD, "overwritten", *i.filename.split("/"))
                    os.makedirs(os.path.dirname(keep), exist_ok=True)
                    shutil.copy2(target, keep)
                    overwritten[i.filename] = old
            else:
                created.append(i.filename)
            os.makedirs(os.path.dirname(target), exist_ok=True)
            with open(target, "wb") as f:
                f.write(data)
    a["zip_created"] = created
    a["zip_overwritten_changed"] = overwritten
    # 6. a fresh settings file with only the harness switched on (BepInEx adds every default)
    with open(os.path.join(CFG, "lom.ui.english.cfg"), "w", encoding="utf-8", newline="\n") as f:
        f.write("[Dev]\n\nHarness = true\n")
    with open(APPID, "w") as f:
        f.write("1859910")
    st["a"] = a
    save(st)
    print("scenario A set up: core extras moved %d (%s), Addressables stock %s, plugin folder moved (%d files), configs moved %s,"
          " full zip: %d created, %d overwritten with different bytes %s" % (len(extras), ", ".join(extras), a["addr_stock_sha"][:12],
          a["plugin_files"], a["configs"], len(created), len(overwritten), list(overwritten)))


def a_teardown():
    need_closed()
    st = load()
    run, a = st["run"], st["a"]
    # attempt number: a scenario A run again after a fix keeps each attempt's leftovers apart (A1_*, A2_*, ...)
    n = 1 + len(glob.glob(os.path.join(run, "A*_plugin_extracted")))
    pre = "A%d_" % n
    shutil.copy2(os.path.join(G, "BepInEx", "LogOutput.log"), os.path.join(run, pre + "LogOutput.log"))
    # the extracted plugin folder: compare with the mod-only package, then keep it in the run folder
    mod_zip = re.sub(r"-full\.zip$", "-mod-only.zip", a["full_zip"])
    with zipfile.ZipFile(mod_zip) as z:
        want = {i.filename[len("BepInEx/plugins/LOM_UI_EN/"):]: hashlib.sha256(z.read(i)).hexdigest() for i in z.infolist() if not i.is_dir()}
    got = tree_hashes(PLUGIN)
    runtime = sorted(k for k in got if k not in want)
    differ = sorted(k for k in want if got.get(k) != want[k])
    print("extracted plugin folder vs mod-only zip: %d files, %d differ %s; written by the game: %s" % (len(want), len(differ), differ[:5], runtime))
    move_verified(PLUGIN, os.path.join(run, pre + "plugin_extracted"))
    # files the zip created at the game root / in BepInEx (not directories that existed); the plugin folder is done
    for rel in a["zip_created"]:
        if rel.startswith("BepInEx/plugins/LOM_UI_EN") or rel.endswith("/"):
            continue
        p = os.path.join(G, *rel.split("/"))
        if os.path.isfile(p):
            move_verified(p, os.path.join(run, pre + "zip_created", *rel.split("/")))
    for rel, old in a["zip_overwritten_changed"].items():
        keep = os.path.join(HOLD, "overwritten", *rel.split("/"))
        shutil.copy2(keep, os.path.join(G, *rel.split("/")))
        if sha(os.path.join(G, *rel.split("/"))) != old:
            sys.exit("restore of %s failed" % rel)
    # settings: the generated ones go to the run folder, the player's come back
    for f in ("lom.ui.english.cfg", "lom.strings.english.cfg", "BepInEx.cfg"):
        p = os.path.join(CFG, f)
        if os.path.exists(p):
            move_verified(p, os.path.join(run, pre + "generated_config", f))
    for f in a["configs"]:
        move_verified(os.path.join(HOLD, "config_user", f), os.path.join(CFG, f))
    # Addressables, core extras, plugin folder, Lash's English Patch
    shutil.copy2(os.path.join(HOLD, "Unity.Addressables.dll.patched"), ADDR)
    if sha(ADDR) != a["addr_patched_sha"]:
        sys.exit("restore of the patched Addressables failed")
    for f in a["core_extras"]:
        move_verified(os.path.join(HOLD, "core_extras", f), os.path.join(CORE, f))
    move_verified(os.path.join(HOLD, "LOM_UI_EN_installed"), PLUGIN)
    ps(OVERLLM_ASIDE, "back")
    left = [os.path.relpath(os.path.join(r, f), HOLD) for r, ds, fs in os.walk(HOLD) for f in fs]
    moved_rest = 0
    for rel in left:   # the patched Addressables copy and overwritten originals: keep them with the run
        move_verified(os.path.join(HOLD, rel), os.path.join(run, pre + "hold_leftovers", rel))
        moved_rest += 1
    shutil.rmtree(HOLD)   # empty folders only now
    st["a_done"] = True
    del st["a"]
    save(st)
    print("scenario A undone: everything back and verified (%d holding files kept in the run folder)" % moved_rest)


def set_harness(on):
    p = os.path.join(CFG, "lom.ui.english.cfg")
    t = open(p, encoding="utf-8").read()
    a, b = ("Harness = false", "Harness = true") if on else ("Harness = true", "Harness = false")
    if t.count(a) != 1:
        sys.exit("cannot switch the harness: '%s' found %d times" % (a, t.count(a)))
    open(p, "w", encoding="utf-8", newline="").write(t.replace(a, b))
    print("harness %s" % ("on" if on else "off"))


def b_setup():
    need_closed()
    set_harness(True)
    with open(APPID, "w") as f:
        f.write("1859910")


def b_teardown():
    need_closed()
    st = load()
    shutil.copy2(os.path.join(G, "BepInEx", "LogOutput.log"), os.path.join(st["run"], "B_LogOutput.log"))
    set_harness(False)


def final():
    need_closed()
    st = load()
    run = st["run"]
    # saves: put back every file the run changed; files the run created go to the run folder
    now = tree_hashes(SAVES)
    old = st["saves_hashes"]
    restored, created = [], []
    for rel, h in old.items():
        if now.get(rel) != h:
            shutil.copy2(os.path.join(run, "saves_backup", *rel.split("/")), os.path.join(SAVES, *rel.split("/")))
            restored.append(rel)
    for rel in now:
        if rel not in old:
            move_verified(os.path.join(SAVES, *rel.split("/")), os.path.join(run, "saves_created_by_run", *rel.split("/")))
            created.append(rel)
    if tree_hashes(SAVES) != old:
        sys.exit("SAVES DIFFER from the backup after restoring")
    imports = restore_registry(run)
    if os.path.exists(APPID):
        os.remove(APPID)          # this script's own temporary file
    if os.path.isdir(HARNESS_OUT) and os.listdir(HARNESS_OUT):
        move_verified(HARNESS_OUT, os.path.join(run, "harness_out_run"))
    if os.path.isdir(os.path.join(run, "harness_out_before")):
        move_verified(os.path.join(run, "harness_out_before"), HARNESS_OUT)
    st["final_done"] = True
    save(st)
    print("final: saves restored (%d changed files put back: %s; %d created moved out: %s), registry restored and checked "
          "(%d import%s), steam_appid.txt removed, harness output kept in %s" % (
              len(restored), restored, len(created), created, imports, "" if imports == 1 else "s", run))


def _reg_values(text):
    """{value name: data} of a .reg export (continued lines joined)."""
    out, cur = {}, ""
    for line in text.splitlines():
        cur += line.strip()
        if cur.endswith("\\"):
            cur = cur[:-1]
            continue
        m = re.match(r'^"((?:[^"\\]|\\.)+)"=(.*)$', cur)
        if m:
            out[m.group(1)] = m.group(2)
        cur = ""
    return out


def restore_registry(run):
    """Imports the backed-up key, exports it again and compares every value; on 2026-09-27 the first import was undone
    (the game's fullscreen mode read 3 = windowed afterwards), so it retries until the values match."""
    backup = os.path.join(run, "registry_backup.reg")
    want = _reg_values(open(backup, encoding="utf-16").read())
    diff = []
    for attempt in range(1, 4):
        subprocess.run(["reg", "import", backup], capture_output=True, text=True)
        check = os.path.join(run, "registry_after_import.reg")
        subprocess.run(["reg", "export", REGKEY, check, "/y"], capture_output=True, text=True)
        got = _reg_values(open(check, encoding="utf-16").read())
        diff = sorted(k for k in want if got.get(k) != want[k])
        if not diff:
            return attempt
        time.sleep(2)
    sys.exit("REGISTRY still differs from the backup after 3 imports: %s (import %s by hand)" % (diff[:5], backup))


def status():
    print(json.dumps(load(), indent=1)[:3000])
    print("game running:", game_running())


{"prep": prep, "a_setup": a_setup, "a_teardown": a_teardown, "b_setup": b_setup, "b_teardown": b_teardown, "final": final,
 "status": status}[sys.argv[1]]()
