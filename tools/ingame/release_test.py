"""Release test: set up and undo the in-game test scenarios of a release candidate, exactly and verifiably.

  python release_test.py prep | a_setup | a_teardown | b_setup | b_teardown | final | status
  python release_test.py l_setup TABLES_DIR PLUGIN_DLL [--leftover] | l_teardown | l5_setup | l5_teardown
  python release_test.py r_setup RELEASE_ZIP [--clean] | r_teardown

  prep        refuses while the game runs; backs up the save folder and the game's registry key, parks the harness output
              (a finished run's state.json moves into that run's folder first)
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
  l_setup     scenario L, the llmkit-upgrade release of Lash's English Patch (2026-09): Mods/English held aside, the release's
              per-file tables (TABLES_DIR, e.g. its Mods/English) and its own plugin (PLUGIN_DLL, FanslationStudio.
              LegendOfMortal.Plugin.dll, built for BepInEx 5) put in; --leftover also puts the older StringTable.csv back
              next to them, as unpacking the new release over the old one leaves it. Harness on, steam_appid.txt. On this
              game's BepInEx 6 the new plugin never loads: this mod reads the tables itself
  l_teardown  everything L put in moved to the run folder (the plugin's BepInEx/plugins/raw dump too), Mods/English back
  l5_setup    scenario L5, on top of L: BepInEx 5 instead of 6, as the new release may ship it. The official
              BepInEx_win_x86_5.4.*.zip from release/vendor (not pinned: test only) is extracted in place of the loader
              files it replaces (doorstop files, BepInEx/core), which are held aside with BepInEx/config/BepInEx.cfg and
              the chainloader cache; this mod then runs from LOM_UI_EN.BepInEx5.dll and the new plugin runs too
  l5_teardown BepInEx 5's files and whatever it created moved to the run folder, BepInEx 6 back, verified
  r_setup     scenario R, a release zip of Lash's English Patch (e.g. EnglishPatch-2026.09.28.13.56.zip: BepInEx 5, its own
              plugin and FanslationStudio.Plugins pack, the per-file tables) extracted into the game folder as a player's unzip
              does: over the current install (default, as unpacking a new version over an older one does, its leftovers kept) or
              --clean (the current BepInEx folder, Mods and the loader's root files held aside first, then this mod's plugin
              folder and settings put back, as its mod-only package installs them). This mod's harness cache
              (BepInEx/cache/LOM_UI_EN) stays in place. Harness on, steam_appid.txt
  r_teardown  the release's install moved to the run folder, everything R changed back, verified against the hashes taken
              before

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
# LOM_RELEASE_TEST_DIR: a different run area (with LOM_GAME, for trying the scenario steps on a copy of the game folder)
HERE = os.environ.get("LOM_RELEASE_TEST_DIR") or os.path.join(LOC, "_release_test")
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
    if st.get("run") and st.get("final_done") and os.path.isdir(st["run"]):
        # a finished run: its record goes into its own folder, and a new run starts from nothing
        shutil.move(STATE, os.path.join(st["run"], "state.json"))
        st = {}
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
    t = open(p, encoding="utf-8", newline="").read()     # newline="": the cfg's CRLF stays as it is
    a, b = ("Harness = false", "Harness = true") if on else ("Harness = true", "Harness = false")
    if t.count(a) == 0 and t.count(b) == 1:
        print("harness already %s" % ("on" if on else "off"))   # e.g. r_setup right after b_setup
        return
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


# ---- scenarios L and L5: the llmkit-upgrade release of Lash's English Patch --------------------------------------------------

MODS_EN = os.path.join(G, "Mods", "English")
LLMKIT_DLL = "FanslationStudio.LegendOfMortal.Plugin.dll"
LASH_PLUGIN = os.path.join(G, "BepInEx", "plugins", LLMKIT_DLL)
RAW_DUMP = os.path.join(G, "BepInEx", "plugins", "raw")          # the new plugin's dump of the game's sources (BepInEx 5)
BIE5_ZIPS = os.path.join(os.environ.get("LOM_VENDOR_DIR") or os.path.join(LOC, "release", "vendor"), "BepInEx_win_x86_5.4.*.zip")
BIE_CFG = os.path.join(CFG, "BepInEx.cfg")
CACHE = os.path.join(G, "BepInEx", "cache")
LOM_CACHE = "LOM_UI_EN"                                          # this mod's cache (the harness) stays where it is


def _run_active():
    st = load()
    if not st.get("run") or st.get("final_done"):
        sys.exit("REFUSED: run prep first")
    return st


def l_setup(tables, dll, leftover=False):
    need_closed()
    st = _run_active()
    if st.get("l") and not st["l"].get("undone"):
        sys.exit("REFUSED: scenario L is set up already")
    import lashtables          # tools/, on sys.path above
    lay = lashtables.find(tables)
    if lay.kind != "per-file":
        sys.exit("REFUSED: %s holds no per-file tables of the llmkit-upgrade release" % tables)
    if os.path.basename(dll) != LLMKIT_DLL or not os.path.isfile(dll):
        sys.exit("REFUSED: the plugin must be a file named %s" % LLMKIT_DLL)
    for p in (LASH_PLUGIN, RAW_DUMP):
        if os.path.exists(p):
            sys.exit("REFUSED: %s exists already" % p)
    run = st["run"]
    held = os.path.join(HOLD, "L_Mods_English")
    n = move_verified(MODS_EN, held) if os.path.exists(MODS_EN) else 0
    os.makedirs(MODS_EN)
    added = {}
    for f in lay.files:
        dst = os.path.join(MODS_EN, os.path.basename(f))
        shutil.copy2(f, dst)
        added[os.path.relpath(dst, G)] = sha(dst)
    if leftover:
        old = os.path.join(held, "StringTable.csv")
        if not os.path.exists(old):
            sys.exit("REFUSED: --leftover needs the held Mods/English to hold StringTable.csv")
        dst = os.path.join(MODS_EN, "StringTable.csv")
        shutil.copy2(old, dst)
        added[os.path.relpath(dst, G)] = sha(dst)
    shutil.copy2(dll, LASH_PLUGIN)
    added[os.path.relpath(LASH_PLUGIN, G)] = sha(LASH_PLUGIN)
    set_harness(True)
    with open(APPID, "w") as f:
        f.write("1859910")
    st["l"] = {"held_mods_english": held if n else None, "held_files": n, "added": added, "leftover": leftover,
               "tables_from": tables, "plugin_from": dll, "plugin_sha256": sha(dll)}
    save(st)
    print("scenario L set up: Mods/English (%d files) held, %d tables%s and %s put in, harness on" % (
        n, len(lay.files), " plus the old StringTable.csv" if leftover else "", LLMKIT_DLL))


def l_teardown():
    need_closed()
    st = _run_active()
    l = st.get("l")
    if not l or l.get("undone"):
        sys.exit("REFUSED: scenario L is not set up")
    if st.get("l5") and not st["l5"].get("undone"):
        sys.exit("REFUSED: undo L5 first (l5_teardown)")
    run = st["run"]
    pre = _attempt(run, "L")
    log = os.path.join(G, "BepInEx", "LogOutput.log")
    if os.path.exists(log):
        shutil.copy2(log, os.path.join(run, pre + "LogOutput.log"))
    changed = [rel for rel, h in l["added"].items() if not os.path.exists(os.path.join(G, rel)) or sha(os.path.join(G, rel)) != h]
    if changed:
        print("note: files the test put in changed or went missing while it ran: %s" % changed)
    n = move_verified(MODS_EN, os.path.join(run, pre + "Mods_English_test"))
    move_verified(LASH_PLUGIN, os.path.join(run, pre + "plugin", LLMKIT_DLL))
    raw = move_verified(RAW_DUMP, os.path.join(run, pre + "raw_dump")) if os.path.exists(RAW_DUMP) else 0
    if l.get("held_mods_english"):
        move_verified(l["held_mods_english"], MODS_EN)
    if os.path.isdir(HOLD) and not any(fs for r, ds, fs in os.walk(HOLD)):
        shutil.rmtree(HOLD)   # the holding area is empty folders only now
    set_harness(False)
    l["undone"] = True
    save(st)
    print("scenario L undone: %d test tables and the plugin moved to the run folder%s, Mods/English back (%d files)" % (
        n, (", its raw dump (%d files) too" % raw) if raw else "", l.get("held_files", 0)))


def _attempt(run, scenario):
    """The file prefix of this teardown in the run folder: '<scenario>_' the first time, '<scenario>-2_' and on when the scenario
    ran again in the same run (L with and without --leftover), so no attempt's files meet another's."""
    k = 1
    while glob.glob(os.path.join(run, ("%s_*" % scenario) if k == 1 else ("%s-%d_*" % (scenario, k)))):
        k += 1
    return ("%s_" % scenario) if k == 1 else ("%s-%d_" % (scenario, k))


def _cache_files():
    """BepInEx/cache files outside this mod's own folder, relative."""
    out = {}
    for rel, h in (tree_hashes(CACHE) if os.path.isdir(CACHE) else {}).items():
        if not rel.startswith(LOM_CACHE + "/"):
            out[rel] = h
    return out


def l5_setup():
    need_closed()
    st = _run_active()
    if not st.get("l") or st["l"].get("undone"):
        sys.exit("REFUSED: set up scenario L first")
    if st.get("l5") and not st["l5"].get("undone"):
        sys.exit("REFUSED: scenario L5 is set up already")
    zips = sorted(glob.glob(BIE5_ZIPS))
    if not zips:
        sys.exit("REFUSED: no %s: download the official BepInEx 5 (Unity Mono, x86) zip from "
                 "https://github.com/BepInEx/BepInEx/releases into release/vendor" % os.path.basename(BIE5_ZIPS))
    zpath = zips[-1]
    if not os.path.exists(os.path.join(PLUGIN, "LOM_UI_EN.BepInEx5.dll")):
        sys.exit("REFUSED: the plugin folder has no LOM_UI_EN.BepInEx5.dll (build.ps1 -BepInEx5, then install it)")
    run = st["run"]
    with zipfile.ZipFile(zpath) as z:
        entries = [i for i in z.infolist() if not i.is_dir()]
        names = [i.filename for i in entries]
        roots = sorted({n.split("/")[0] for n in names})
        if [n for n in names if "/" in n and not n.startswith("BepInEx/core/")]:
            sys.exit("REFUSED: %s holds files outside the game root and BepInEx/core: %s" % (
                os.path.basename(zpath), [n for n in names if "/" in n and not n.startswith("BepInEx/core/")][:5]))
        held = {}
        for n in [r for r in roots if r != "BepInEx"]:
            src = os.path.join(G, n)
            if os.path.exists(src):
                move_verified(src, os.path.join(HOLD, "L5_root", n))
                held[n] = os.path.join(HOLD, "L5_root", n)
        move_verified(CORE, os.path.join(HOLD, "L5_core"))
        held["BepInEx/core"] = os.path.join(HOLD, "L5_core")
        if os.path.exists(BIE_CFG):
            move_verified(BIE_CFG, os.path.join(HOLD, "L5_config", "BepInEx.cfg"))
            held["BepInEx/config/BepInEx.cfg"] = os.path.join(HOLD, "L5_config", "BepInEx.cfg")
        cache_before = _cache_files()
        for rel in cache_before:
            move_verified(os.path.join(CACHE, *rel.split("/")), os.path.join(HOLD, "L5_cache", *rel.split("/")))
        added = {}
        for i in entries:
            dst = os.path.join(G, *i.filename.split("/"))
            if os.path.exists(dst):
                sys.exit("REFUSED: %s exists after holding the loader files (undo with l5_teardown)" % dst)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            with z.open(i) as src, open(dst, "wb") as out:
                shutil.copyfileobj(src, out)
            added[i.filename] = sha(dst)
    st["l5"] = {"zip": zpath, "zip_sha256": sha(zpath), "held": held, "cache_held": sorted(cache_before), "added": added,
                "bepinex_dirs_before": sorted(os.listdir(os.path.join(G, "BepInEx"))),
                "configs_before": sorted(os.listdir(CFG)) if os.path.isdir(CFG) else []}
    save(st)
    print("scenario L5 set up: %s extracted (%d files), BepInEx 6's loader files, BepInEx.cfg and %d cache files held" % (
        os.path.basename(zpath), len(added), len(cache_before)))


def l5_teardown():
    need_closed()
    st = _run_active()
    l5 = st.get("l5")
    if not l5 or l5.get("undone"):
        sys.exit("REFUSED: scenario L5 is not set up")
    run = st["run"]
    pre = _attempt(run, "L5")
    out = os.path.join(run, pre + "bepinex5")
    log = os.path.join(G, "BepInEx", "LogOutput.log")
    if os.path.exists(log):
        shutil.copy2(log, os.path.join(run, pre + "LogOutput.log"))
    # BepInEx 5's own files (the zip's), then whatever it created: its config, its cache, new BepInEx folders
    for rel in sorted({r.split("/")[0] for r in l5["added"] if "/" not in r} | ({"BepInEx/core"})):
        p = os.path.join(G, *rel.split("/"))
        if os.path.exists(p):
            move_verified(p, os.path.join(out, *rel.split("/")))
    if os.path.exists(BIE_CFG):
        move_verified(BIE_CFG, os.path.join(out, "created", "BepInEx.cfg"))
    # the settings files of plugins only BepInEx 5 loads (Lash's plugin writes FanslationStudio.LegendOfMortal.Plugin.cfg)
    for f in sorted(os.listdir(CFG)) if "configs_before" in l5 and os.path.isdir(CFG) else []:
        if f not in l5["configs_before"]:
            move_verified(os.path.join(CFG, f), os.path.join(out, "created", "config", f))
    for rel in _cache_files():
        move_verified(os.path.join(CACHE, *rel.split("/")), os.path.join(out, "created", "cache", *rel.split("/")))
    for d in sorted(os.listdir(os.path.join(G, "BepInEx"))):
        if d not in l5["bepinex_dirs_before"]:
            move_verified(os.path.join(G, "BepInEx", d), os.path.join(out, "created", "BepInEx", d))
    # BepInEx 6 back
    for rel, src in sorted(l5["held"].items()):
        move_verified(src, os.path.join(G, *rel.split("/")))
    for rel in l5["cache_held"]:
        move_verified(os.path.join(HOLD, "L5_cache", *rel.split("/")), os.path.join(CACHE, *rel.split("/")))
    # the holding folders are empty now
    for d in ("L5_root", "L5_core", "L5_config", "L5_cache"):
        p = os.path.join(HOLD, d)
        for r, ds, fs in sorted(os.walk(p, topdown=False), reverse=False) if os.path.isdir(p) else []:
            if not os.listdir(r):
                os.rmdir(r)
    l5["undone"] = True
    save(st)
    print("scenario L5 undone: BepInEx 5 and what it created moved to %s, BepInEx 6 back (verified)" % out)


# ---- scenario R: a release zip of Lash's English Patch, installed as a player would --------------------------------------------

BEPINEX = os.path.join(G, "BepInEx")
MODS = os.path.join(G, "Mods")
LOM_CACHE_DIR = os.path.join(CACHE, LOM_CACHE)


def _game_tree():
    """Hashes of what a release install touches: BepInEx (without this mod's harness cache), Mods, the loader's root files."""
    out = {}
    for rel, h in (tree_hashes(BEPINEX) if os.path.isdir(BEPINEX) else {}).items():
        if not rel.startswith("cache/" + LOM_CACHE + "/"):
            out["BepInEx/" + rel] = h
    for rel, h in (tree_hashes(MODS) if os.path.isdir(MODS) else {}).items():
        out["Mods/" + rel] = h
    for n in ROOT_LOADER_FILES:
        if os.path.isfile(os.path.join(G, n)):
            out[n] = sha(os.path.join(G, n))
    return out


ROOT_LOADER_FILES = ("doorstop_config.ini", "winhttp.dll", ".doorstop_version", "changelog.txt")


def _copy_verified(src, dst):
    shutil.copytree(src, dst)
    if tree_hashes(src) != tree_hashes(dst):
        sys.exit("HASH MISMATCH copying %s -> %s" % (src, dst))


def r_setup(zpath, clean=False):
    need_closed()
    st = _run_active()
    if st.get("r") and not st["r"].get("undone"):
        sys.exit("REFUSED: scenario R is set up already")
    if not os.path.isfile(zpath):
        sys.exit("REFUSED: no such zip: %s" % zpath)
    run = st["run"]
    n = 1 + sum(1 for d in os.listdir(run) if re.match(r"^R\d+_before$", d))
    before = os.path.join(run, "R%d_before" % n)
    with zipfile.ZipFile(zpath) as z:
        entries = [i for i in z.infolist() if not i.is_dir()]
        roots = sorted({i.filename for i in entries if "/" not in i.filename})
        odd = [r for r in roots if r not in ROOT_LOADER_FILES]
        tops = sorted({i.filename.split("/")[0].lower() for i in entries if "/" in i.filename})
        if odd or [t for t in tops if t not in ("bepinex", "mods")]:
            sys.exit("REFUSED: the zip holds files outside BepInEx, Mods and the loader's root files: %s %s" % (odd, tops))
        game_before = _game_tree()
        cache_hold = os.path.join(run, "R%d_harness_cache" % n)
        if os.path.isdir(LOM_CACHE_DIR):
            move_verified(LOM_CACHE_DIR, cache_hold)
        if clean:
            move_verified(BEPINEX, os.path.join(before, "BepInEx"))
            if os.path.isdir(MODS):
                move_verified(MODS, os.path.join(before, "Mods"))
            for r in ROOT_LOADER_FILES:
                if os.path.isfile(os.path.join(G, r)):
                    move_verified(os.path.join(G, r), os.path.join(before, "root", r))
            # this mod as its mod-only package installs it, with its settings
            _copy_verified(os.path.join(before, "BepInEx", "plugins", "LOM_UI_EN"), PLUGIN)
            os.makedirs(CFG, exist_ok=True)
            for c in ("lom.ui.english.cfg", "lom.strings.english.cfg"):
                if os.path.isfile(os.path.join(before, "BepInEx", "config", c)):
                    shutil.copy2(os.path.join(before, "BepInEx", "config", c), os.path.join(CFG, c))
        else:
            _copy_verified(BEPINEX, os.path.join(before, "BepInEx"))
            if os.path.isdir(MODS):
                _copy_verified(MODS, os.path.join(before, "Mods"))
            for r in ROOT_LOADER_FILES:
                if os.path.isfile(os.path.join(G, r)):
                    os.makedirs(os.path.join(before, "root"), exist_ok=True)
                    shutil.copy2(os.path.join(G, r), os.path.join(before, "root", r))
        if os.path.isdir(cache_hold):
            move_verified(cache_hold, LOM_CACHE_DIR)
        # a player's unzip: every entry written where it says (Windows paths ignore case, so "BepinEx/" lands in BepInEx)
        written = 0
        for i in entries:
            dst = os.path.join(G, *i.filename.split("/"))
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            with z.open(i) as src, open(dst, "wb") as out:
                shutil.copyfileobj(src, out)
            written += 1
    set_harness(True)
    with open(APPID, "w") as f:
        f.write("1859910")
    st["r"] = {"zip": zpath, "zip_sha256": sha(zpath), "clean": clean, "before": before, "n": n, "game_before": game_before}
    save(st)
    print("scenario R set up (%s): %s extracted (%d files)%s, harness on" % (
        "clean install" if clean else "over the current install", os.path.basename(zpath), written,
        "; the previous BepInEx, Mods and loader files held in " + before if clean else "; the previous state copied to " + before))


def r_teardown():
    need_closed()
    st = _run_active()
    r = st.get("r")
    if not r or r.get("undone"):
        sys.exit("REFUSED: scenario R is not set up")
    run, n, before = st["run"], r["n"], r["before"]
    after = os.path.join(run, "R%d_after" % n)
    log = os.path.join(BEPINEX, "LogOutput.log")
    if os.path.exists(log):
        shutil.copy2(log, os.path.join(run, "R%d_LogOutput.log" % n))
    cache_hold = os.path.join(run, "R%d_harness_cache_after" % n)
    if os.path.isdir(LOM_CACHE_DIR):
        move_verified(LOM_CACHE_DIR, cache_hold)
    move_verified(BEPINEX, os.path.join(after, "BepInEx"))
    if os.path.isdir(MODS):
        move_verified(MODS, os.path.join(after, "Mods"))
    for name in ROOT_LOADER_FILES:
        if os.path.isfile(os.path.join(G, name)):
            move_verified(os.path.join(G, name), os.path.join(after, "root", name))
    move_verified(os.path.join(before, "BepInEx"), BEPINEX)
    if os.path.isdir(os.path.join(before, "Mods")):
        move_verified(os.path.join(before, "Mods"), MODS)
    for name in ROOT_LOADER_FILES:
        if os.path.isfile(os.path.join(before, "root", name)):
            move_verified(os.path.join(before, "root", name), os.path.join(G, name))
    if os.path.isdir(cache_hold):
        move_verified(cache_hold, LOM_CACHE_DIR)
    now = _game_tree()
    diff = sorted(k for k in set(now) | set(r["game_before"]) if now.get(k) != r["game_before"].get(k))
    if diff:
        sys.exit("GAME FOLDER DIFFERS from before scenario R: %s" % diff[:10])
    r["undone"] = True
    save(st)
    print("scenario R undone: the release's install moved to %s; BepInEx, Mods and the loader files back, %d files verified" % (
        after, len(now)))


if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "r_setup":
        a = [x for x in sys.argv[2:] if x != "--clean"]
        if len(a) != 1:
            sys.exit("usage: release_test.py r_setup RELEASE_ZIP [--clean]")
        r_setup(a[0], "--clean" in sys.argv[2:])
    elif cmd == "r_teardown":
        r_teardown()
    elif cmd == "l_setup":
        a = [x for x in sys.argv[2:] if x != "--leftover"]
        if len(a) != 2:
            sys.exit("usage: release_test.py l_setup TABLES_DIR PLUGIN_DLL [--leftover]")
        l_setup(a[0], a[1], "--leftover" in sys.argv[2:])
    else:
        {"prep": prep, "a_setup": a_setup, "a_teardown": a_teardown, "b_setup": b_setup, "b_teardown": b_teardown,
         "final": final, "status": status, "l_teardown": l_teardown, "l5_setup": l5_setup, "l5_teardown": l5_teardown}[cmd]()
