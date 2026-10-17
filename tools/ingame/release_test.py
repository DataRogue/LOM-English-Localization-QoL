"""Release test: set up and undo the in-game test scenarios of a release candidate, exactly and verifiably.

  python release_test.py prep | a_setup [FULL_ZIP] | a_teardown | b_setup | b_teardown | final | status
  python release_test.py r_setup RELEASE_ZIP [--clean] [--installer] | r_teardown

Since 1.2.0 this mod runs on BepInEx 5.4.23.3 only, the version Lash's English Patch installs (the 1.0.x and 1.1.x
branches keep the BepInEx 6 scenarios L and L5 and the older A).

  prep        refuses while the game runs; backs up the save folder and the game's registry key, parks the harness output
              (a finished run's state.json moves into that run's folder first)
  a_setup     scenario A, a clean install: the game's BepInEx folder, Mods and the loader's root files held aside (this mod's
              harness cache stays where run_batch.ps1 looks), the stock Unity.Addressables.dll in (the installed one when
              it is stock, else BepInEx/backup_stock's Unity.Addressables.dll.stock-<Steam build id>), the newest
              release/dist/*-candidate/*-full.zip (or FULL_ZIP, e.g. an earlier release's for a before/after comparison;
              its -mod-only.zip must lie next to it) extracted as a player would (BepInEx 5.4.23.3 with it), a fresh
              lom.ui.english.cfg with only the harness on, steam_appid.txt
  a_teardown  compares the extracted plugin folder with the -mod-only zip, moves the clean install to the run folder, then
              puts everything back and verifies the game folder against the hashes taken before (repeatable: each
              attempt's files go to A<n>_* in the run folder)
  b_setup     scenario B, the current install as it is: harness on in the player's cfg, steam_appid.txt
  b_teardown  harness off, the BepInEx log kept
  final       saves put back (changed files restored, new files moved out, then compared with the backup), the registry
              key imported and checked value by value, steam_appid.txt removed, the harness output kept in the run folder
  r_setup     scenario R, a release of Lash's English Patch: RELEASE_ZIP (e.g. EnglishPatch-2026.10.05.09.58.zip) over the
              current install as a player's unzip does (default, its leftovers kept), or --clean (the BepInEx folder, Mods
              and the loader's root files held aside first, then this mod's plugin folder and settings put back, as its
              mod-only package installs them). --installer installs the release as that patch's installer does (its releases
              from 2026.09.29 on ship no BepInEx and are meant for it): BepInEx 5.4.23.3 first (the installer's pinned zip
              from release/vendor, dll_search_path_override set as the installer sets it, its marker file), then the files
              the release's manifest lists (a seed-only file kept when it exists) and the manifest in
              BepInEx/release-manifest.json, where that patch's in-game updater reads it. Harness on, steam_appid.txt
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
PLUGIN = os.path.join(G, "BepInEx", "plugins", "LOM_UI_EN")
CFG = os.path.join(G, "BepInEx", "config")
ADDR = os.path.join(G, "Mortal_Data", "Managed", "Unity.Addressables.dll")
# a stock copy kept for one game build (Steam build id): only that build may get it back
ADDR_STOCK_FMT = os.path.join(G, "BepInEx", "backup_stock", "Unity.Addressables.dll.stock-%s")
HARNESS_OUT = os.path.join(G, "BepInEx", "cache", "LOM_UI_EN", "harness", "out")
VENDOR = os.environ.get("LOM_VENDOR_DIR") or os.path.join(LOC, "release", "vendor")
BEPINEX = os.path.join(G, "BepInEx")
MODS = os.path.join(G, "Mods")
CACHE = os.path.join(G, "BepInEx", "cache")
LOM_CACHE = "LOM_UI_EN"                                          # this mod's cache (the harness) stays where it is
LOM_CACHE_DIR = os.path.join(CACHE, LOM_CACHE)
# the loader's files at the game root, and a release manifest a player's unzip of Lash's English Patch leaves there
ROOT_LOADER_FILES = ("doorstop_config.ini", "winhttp.dll", ".doorstop_version", "changelog.txt", "release-manifest.json")
INSTALLER_MARKER = os.path.join(G, "BepInEx", "installer-bepinex.sha256")
INSTALLED_MANIFEST = os.path.join(G, "BepInEx", "release-manifest.json")


def _candidate(kind):
    """The newest release/dist/*-candidate/*-<kind>.zip (release.py --candidate writes them)."""
    found = sorted(glob.glob(os.path.join(LOC, "release", "dist", "*-candidate", "*-candidate-%s.zip" % kind)), key=os.path.getmtime)
    if not found:
        sys.exit("no candidate %s zip: run python tools/release.py --candidate first" % kind)
    return found[-1]


APPID = os.path.join(G, "steam_appid.txt")


def game_build():
    """The installed game's Steam build id (steamapps/appmanifest_1859910.acf, two folders above the game folder), or None."""
    acf = os.path.join(os.path.dirname(os.path.dirname(G)), "appmanifest_1859910.acf")
    try:
        m = re.search(r'"buildid"\s+"(\d+)"', open(acf, encoding="utf-8", errors="replace").read())
    except OSError:
        return None
    return m.group(1) if m else None


def stock_addressables():
    """(path of a stock Unity.Addressables.dll for the installed game build, how it was found), or (None, why not).
    Lash's English Patch up to its 2026-02 releases (Binarizer) patches that DLL, adding the static field ModPaths; a DLL
    without the name is the game's own. The installed one is taken when it is stock, else the copy backup_stock keeps for
    this build: a stock DLL of another build would not match the game's other assemblies."""
    with open(ADDR, "rb") as f:
        patched = b"ModPaths" in f.read()
    if not patched:
        return ADDR, "the installed one (stock)"
    build = game_build()
    kept = ADDR_STOCK_FMT % build if build else None
    if kept and os.path.isfile(kept):
        return kept, "backup_stock's copy for build " + build
    return None, "the installed Unity.Addressables.dll is a patched one and BepInEx/backup_stock has no stock copy for game build %s (%s)" % (build, kept)


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


def _copy_verified(src, dst):
    shutil.copytree(src, dst)
    if tree_hashes(src) != tree_hashes(dst):
        sys.exit("HASH MISMATCH copying %s -> %s" % (src, dst))


def _run_active():
    st = load()
    if not st.get("run") or st.get("final_done"):
        sys.exit("REFUSED: run prep first")
    return st


def _attempt(run, scenario):
    """The file prefix of this teardown in the run folder: '<scenario>_' the first time, '<scenario>-2_' and on when the scenario
    ran again in the same run, so no attempt's files meet another's."""
    k = 1
    while glob.glob(os.path.join(run, ("%s_*" % scenario) if k == 1 else ("%s-%d_*" % (scenario, k)))):
        k += 1
    return ("%s_" % scenario) if k == 1 else ("%s-%d_" % (scenario, k))


def _game_tree():
    """Hashes of what an install touches: BepInEx (without this mod's harness cache), Mods, the loader's root files."""
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


def _hold_install(dest, cache_hold):
    """Moves BepInEx (this mod's harness cache out first), Mods and the loader's root files into dest."""
    if os.path.isdir(LOM_CACHE_DIR):
        move_verified(LOM_CACHE_DIR, cache_hold)
    move_verified(BEPINEX, os.path.join(dest, "BepInEx"))
    if os.path.isdir(MODS):
        move_verified(MODS, os.path.join(dest, "Mods"))
    for r in ROOT_LOADER_FILES:
        if os.path.isfile(os.path.join(G, r)):
            move_verified(os.path.join(G, r), os.path.join(dest, "root", r))


def _swap_install(after, before, cache_hold):
    """Moves the install under test to after and the held one back from before; the harness cache goes with the game."""
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


def _extract(zpath, created=None):
    """A player's unzip: every entry written where it says (Windows paths ignore case, so "BepinEx/" lands in BepInEx)."""
    n = 0
    with zipfile.ZipFile(zpath) as z:
        for i in z.infolist():
            if i.is_dir():
                continue
            dst = os.path.join(G, *i.filename.replace("\\", "/").split("/"))
            if created is not None and not os.path.exists(dst):
                created.append(i.filename)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            with z.open(i) as src, open(dst, "wb") as out:
                shutil.copyfileobj(src, out)
            n += 1
    return n


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


def a_setup(full_zip=None):
    need_closed()
    st = _run_active()
    if st.get("a"):
        sys.exit("REFUSED: scenario A is already set up")
    run = st["run"]
    # every input checked before anything moves (a failure half-way would leave nothing for a_teardown to undo)
    full_zip = full_zip or _candidate("full")
    mod_zip = re.sub(r"-full\.zip$", "-mod-only.zip", full_zip)
    for z in (full_zip, mod_zip):
        if not os.path.isfile(z) or not zipfile.is_zipfile(z):
            sys.exit("REFUSED: %s is missing or not a zip (the -mod-only.zip must lie next to the -full.zip)" % z)
    addr_stock, addr_how = stock_addressables()
    if addr_stock is None:
        sys.exit("REFUSED: " + addr_how)
    pre = _attempt(run, "A")
    a = {"pre": pre, "game_before": _game_tree(), "before": os.path.join(run, pre + "held"), "addr_stock_from": addr_how}
    # 1. the stock Unity.Addressables.dll, read before its folder (BepInEx/backup_stock) goes aside
    stock = os.path.join(run, pre + "Unity.Addressables.dll.stock")
    shutil.copy2(addr_stock, stock)
    if sha(stock) != sha(addr_stock):
        sys.exit("copy of the stock Addressables failed")
    # 2. the whole install aside: BepInEx, Mods, the loader's root files (Lash's English Patch, any BepInEx, this mod)
    cache_hold = os.path.join(run, pre + "harness_cache")
    _hold_install(a["before"], cache_hold)
    # 3. stock Unity.Addressables.dll in (keep the one installed; older versions of Lash's English Patch needed a patched one)
    a["addr_sha"] = sha(ADDR)
    shutil.copy2(ADDR, os.path.join(run, pre + "Unity.Addressables.dll.before"))
    shutil.copy2(stock, ADDR)
    a["addr_stock_sha"] = sha(ADDR)
    # 4. the full package, extracted as a player would
    a["full_zip"] = full_zip
    created = []
    a["files"] = _extract(a["full_zip"], created)
    if os.path.isdir(cache_hold):
        move_verified(cache_hold, LOM_CACHE_DIR)
    # 5. a fresh settings file with only the harness switched on (BepInEx adds every default)
    os.makedirs(CFG, exist_ok=True)
    with open(os.path.join(CFG, "lom.ui.english.cfg"), "w", encoding="utf-8", newline="\n") as f:
        f.write("[Dev]\n\nHarness = true\n")
    with open(APPID, "w") as f:
        f.write("1859910")
    st["a"] = a
    save(st)
    print("scenario A set up: the install held in %s, Addressables stock %s (%s), %s extracted (%d files)" % (
        a["before"], a["addr_stock_sha"][:12], addr_how, os.path.basename(a["full_zip"]), a["files"]))


def a_teardown():
    need_closed()
    st = _run_active()
    a = st.get("a")
    if not a:
        sys.exit("REFUSED: scenario A is not set up")
    run, pre = st["run"], a["pre"]
    log = os.path.join(BEPINEX, "LogOutput.log")
    if os.path.exists(log):
        shutil.copy2(log, os.path.join(run, pre + "LogOutput.log"))
    # the extracted plugin folder against the mod-only package
    mod_zip = re.sub(r"-full\.zip$", "-mod-only.zip", a["full_zip"])
    with zipfile.ZipFile(mod_zip) as z:
        want = {i.filename[len("BepInEx/plugins/LOM_UI_EN/"):]: hashlib.sha256(z.read(i)).hexdigest() for i in z.infolist() if not i.is_dir()}
    got = tree_hashes(PLUGIN)
    runtime = sorted(k for k in got if k not in want)
    differ = sorted(k for k in want if got.get(k) != want[k])
    print("extracted plugin folder vs mod-only zip: %d files, %d differ %s; written by the game: %s" % (len(want), len(differ), differ[:5], runtime))
    _swap_install(os.path.join(run, pre + "install"), a["before"], os.path.join(run, pre + "harness_cache_after"))
    shutil.copy2(os.path.join(run, pre + "Unity.Addressables.dll.before"), ADDR)
    if sha(ADDR) != a["addr_sha"]:
        sys.exit("restore of Unity.Addressables.dll failed")
    now = _game_tree()
    diff = sorted(k for k in set(now) | set(a["game_before"]) if now.get(k) != a["game_before"].get(k))
    if diff:
        sys.exit("GAME FOLDER DIFFERS from before scenario A: %s" % diff[:10])
    st["a_done"] = True
    del st["a"]
    save(st)
    print("scenario A undone: the clean install moved to %s; the previous install back, %d files verified" % (
        os.path.join(run, pre + "install"), len(now)))


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


# ---- scenario R: a release of Lash's English Patch, installed as a player or its installer would --------------------------------

def _bepinex5_plan():
    """BepInEx 5.4.23.3 as the installer of Lash's English Patch installs it (FanslationStudio.Installer.Core
    BepInExInstaller): its pinned official zip, doorstop_config.ini's dll_search_path_override rewritten
    (release.doorstop_ini: the same regex and replacement). Checked and read before r_setup moves anything:
    ([(relative path, bytes)], the zip's SHA-256 for the marker)."""
    import release          # tools/, on sys.path above
    zpath = os.path.join(VENDOR, release.BEPINEX_ZIP)
    if not os.path.isfile(zpath):
        sys.exit("REFUSED: no %s: download it from %s into %s" % (release.BEPINEX_ZIP, release.BEPINEX_SOURCE, VENDOR))
    if sha(zpath) != release.BEPINEX_SHA256:
        sys.exit("REFUSED: %s is not the pinned file" % zpath)
    files = []
    with zipfile.ZipFile(zpath) as z:
        for i in z.infolist():
            if i.is_dir():
                continue
            data = z.read(i)
            if i.filename == release.DOORSTOP_INI:
                data = release.doorstop_ini(data)
            files.append((i.filename, data))
    return files, release.BEPINEX_SHA256


def _manifest_plan(zpath):
    """The release as the installer's PatchApplier reads it: every file its release-manifest.json lists, checked against
    the listed SHA-256. Checked and read before r_setup moves anything: ([(path, bytes)], seed-only paths, manifest)."""
    with zipfile.ZipFile(zpath) as z:
        names = {i.filename.replace("\\", "/").lower(): i for i in z.infolist() if not i.is_dir()}
        if "release-manifest.json" not in names:
            sys.exit("REFUSED: %s has no release-manifest.json (a release before 2026.09.29: install it without --installer)" % zpath)
        manifest_bytes = z.read(names["release-manifest.json"])
        man = json.loads(manifest_bytes.decode("utf-8-sig"))
        seed = {s.lower() for s in man.get("seedOnly", [])}
        planned = []
        for f in man["files"]:
            i = names.get(f["path"].lower())
            if i is None:
                sys.exit("REFUSED: the release lacks %s, which its manifest lists" % f["path"])
            data = z.read(i)
            if hashlib.sha256(data).hexdigest() != f["sha256"].lower():
                sys.exit("REFUSED: %s does not match its manifest hash" % f["path"])
            planned.append((f["path"], data))
    return planned, seed, manifest_bytes


def _install_planned(bepinex, manifest):
    """Writes what the two plans read, as the installer does: BepInEx and its marker BepInEx/installer-bepinex.sha256, then
    the release's files (a seed-only file such as BepInEx/config/BepInEx.cfg kept when it exists) and the manifest in
    BepInEx/release-manifest.json, where the patch's updater reads it. Returns (BepInEx files, written, kept)."""
    (bie_files, bie_sha), (planned, seed, manifest_bytes) = bepinex, manifest
    for rel, data in bie_files:
        dst = os.path.join(G, *rel.split("/"))
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        with open(dst, "wb") as out:
            out.write(data)
    os.makedirs(os.path.dirname(INSTALLER_MARKER), exist_ok=True)
    with open(INSTALLER_MARKER, "w", encoding="utf-8", newline="") as f:
        f.write(bie_sha)
    written = kept = 0
    for rel, data in planned:
        dst = os.path.join(G, *rel.split("/"))
        if rel.lower() in seed and os.path.exists(dst):
            kept += 1
            continue
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        with open(dst, "wb") as out:
            out.write(data)
        written += 1
    with open(INSTALLED_MANIFEST, "wb") as out:
        out.write(manifest_bytes)
    return len(bie_files), written, kept


def r_setup(zpath, clean=False, installer=False):
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
        entries = [i.filename.replace("\\", "/") for i in z.infolist() if not i.is_dir()]
    roots = sorted({e for e in entries if "/" not in e})
    odd = [r for r in roots if r not in ROOT_LOADER_FILES]
    tops = sorted({e.split("/")[0].lower() for e in entries if "/" in e})
    if odd or [t for t in tops if t not in ("bepinex", "mods")]:
        sys.exit("REFUSED: the zip holds files outside BepInEx, Mods, the loader's root files and its manifest: %s %s" % (odd, tops))
    # every input checked (and the installer's files read) before anything moves: a failure half-way would leave nothing
    # for r_teardown to undo
    plans = (_bepinex5_plan(), _manifest_plan(zpath)) if installer else None
    if not os.path.isfile(os.path.join(CFG, "lom.ui.english.cfg")):
        sys.exit("REFUSED: no BepInEx/config/lom.ui.english.cfg (start the game once with this mod first)")
    if clean and not os.path.isdir(PLUGIN):
        sys.exit("REFUSED: no plugin folder %s to put back after the clean install" % PLUGIN)
    game_before = _game_tree()
    cache_hold = os.path.join(run, "R%d_harness_cache" % n)
    if clean:
        _hold_install(before, cache_hold)
        # this mod as its mod-only package installs it, with its settings
        _copy_verified(os.path.join(before, "BepInEx", "plugins", "LOM_UI_EN"), PLUGIN)
        os.makedirs(CFG, exist_ok=True)
        for c in ("lom.ui.english.cfg", "lom.strings.english.cfg"):
            if os.path.isfile(os.path.join(before, "BepInEx", "config", c)):
                shutil.copy2(os.path.join(before, "BepInEx", "config", c), os.path.join(CFG, c))
    else:
        if os.path.isdir(LOM_CACHE_DIR):
            move_verified(LOM_CACHE_DIR, cache_hold)
        _copy_verified(BEPINEX, os.path.join(before, "BepInEx"))
        if os.path.isdir(MODS):
            _copy_verified(MODS, os.path.join(before, "Mods"))
        for r in ROOT_LOADER_FILES:
            if os.path.isfile(os.path.join(G, r)):
                os.makedirs(os.path.join(before, "root"), exist_ok=True)
                shutil.copy2(os.path.join(G, r), os.path.join(before, "root", r))
    if os.path.isdir(cache_hold):
        move_verified(cache_hold, LOM_CACHE_DIR)
    if installer:
        bie, written, kept = _install_planned(*plans)
        how = "as its installer does: BepInEx 5.4.23.3 (%d files), %d release files written, %d seed-only kept" % (bie, written, kept)
    else:
        how = "unzipped (%d files)" % _extract(zpath)
    set_harness(True)
    with open(APPID, "w") as f:
        f.write("1859910")
    st["r"] = {"zip": zpath, "zip_sha256": sha(zpath), "clean": clean, "installer": installer, "before": before, "n": n,
               "game_before": game_before}
    save(st)
    print("scenario R set up (%s): %s %s, harness on" % (
        "clean install" if clean else "over the current install", os.path.basename(zpath), how) +
        ("; the previous BepInEx, Mods and loader files held in " + before if clean else "; the previous state copied to " + before))


def r_teardown():
    need_closed()
    st = _run_active()
    r = st.get("r")
    if not r or r.get("undone"):
        sys.exit("REFUSED: scenario R is not set up")
    run, n, before = st["run"], r["n"], r["before"]
    log = os.path.join(BEPINEX, "LogOutput.log")
    if os.path.exists(log):
        shutil.copy2(log, os.path.join(run, "R%d_LogOutput.log" % n))
    _swap_install(os.path.join(run, "R%d_after" % n), before, os.path.join(run, "R%d_harness_cache_after" % n))
    now = _game_tree()
    diff = sorted(k for k in set(now) | set(r["game_before"]) if now.get(k) != r["game_before"].get(k))
    if diff:
        sys.exit("GAME FOLDER DIFFERS from before scenario R: %s" % diff[:10])
    r["undone"] = True
    save(st)
    print("scenario R undone: the release's install moved to %s; BepInEx, Mods and the loader files back, %d files verified" % (
        os.path.join(run, "R%d_after" % n), len(now)))


if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "r_setup":
        a = [x for x in sys.argv[2:] if x not in ("--clean", "--installer")]
        if len(a) != 1:
            sys.exit("usage: release_test.py r_setup RELEASE_ZIP [--clean] [--installer]")
        r_setup(a[0], "--clean" in sys.argv[2:], "--installer" in sys.argv[2:])
    elif cmd == "r_teardown":
        r_teardown()
    elif cmd == "a_setup" and len(sys.argv) > 2:
        a_setup(sys.argv[2])
    else:
        {"prep": prep, "a_setup": a_setup, "a_teardown": a_teardown, "b_setup": b_setup, "b_teardown": b_teardown,
         "final": final, "status": status}[cmd]()
