"""Package a release of LOM English Localization + QoL from the installed plugin folder: exactly the files that were tested
in game, nothing the game or the tools wrote there, checked before and after.

  python release.py [--candidate] [--out DIR] [--skip-rebuild-check]

Gates (any failure refuses to package):
  1. publish.py check passes on the plugin folder (the translation is what the workspace says, no OverLlm text beyond the
     review ledger, only the expected binary files).
  2. src/LOM_UI_EN/build.ps1 (deterministic) rebuilt into a temporary folder gives the installed LOM_UI_EN.dll byte for byte,
     so the published source is the shipped binary (--skip-rebuild-check skips this, and says so in the listing).
  3. The plugin folder's README.txt is src/LOM_UI_EN/PLAYER_README.txt byte for byte.
  4. compat_verified.json was written by this version (Mod Settings > Advanced > Mark as checked, or the harness
     `invoke static LOM_UI_EN.Compat.MarkVerified`, after the in-game test). --candidate lets an unverified build through and
     names the zips ...-candidate, for testing the packages themselves.
  5. Every file in the plugin folder is either shipped (SHIP below) or known not to be (the files the game writes while it
     runs, the tools' publish state, a harness folder); an unknown file refuses, so a new file never ships or goes
     missing unnoticed.

Writes into DIR (default LOM_Localization/release/dist/<version>[-candidate]/):
  LOM-English-Localization-QoL-<version>-mod-only.zip   BepInEx/plugins/LOM_UI_EN/... only
  LOM-English-Localization-QoL-<version>-full.zip       the same plus BepInEx 6.0.0-be.692 (Unity Mono, x86) exactly as in the
                                                        official zip (release/vendor, pinned by SHA-256) and
                                                        BepInEx/THIRD_PARTY_NOTICES-BepInEx.txt (its components, sources and
                                                        licences)
  SHA256SUMS.txt, and FILES.txt (every packaged file with its size and SHA-256)
The zips are deterministic (sorted entries, one fixed time stamp: the date the build was marked as checked) and are read
back after writing: every entry's bytes must equal its source.
"""
import argparse, datetime, hashlib, io, json, os, re, shutil, subprocess, sys, tempfile, zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import lom_paths as P
import publish

SLUG = "LOM-English-Localization-QoL"
SRC = os.path.join(P.REAL_LOCALIZATION, "src", "LOM_UI_EN")
RELEASE = os.path.join(P.REAL_LOCALIZATION, "release")
VENDOR = os.path.join(RELEASE, "vendor")
BEPINEX_ZIP = "BepInEx-Unity.Mono-win-x86-6.0.0-be.692+851521c.zip"
BEPINEX_SHA256 = "97720c5f5c70abfb2ae19dba6000529049ae67f053303b3ce2b49e6ad6c0eca6"
BEPINEX_SOURCE = "https://builds.bepinex.dev/projects/bepinex_be/692/BepInEx-Unity.Mono-win-x86-6.0.0-be.692%2B851521c.zip"
PLUGIN_PREFIX = "BepInEx/plugins/LOM_UI_EN/"

# what ships from the plugin folder (paths relative to it, '/' separated)
SHIP = re.compile(r"^(LOM_UI_EN\.dll|Newtonsoft\.Json\.dll|README\.txt|THIRD_PARTY_NOTICES\.txt|compat_verified\.json|fonts\.json"
                  r"|nametips\.tsv|factiontips\.tsv|rules/[^/]+\.json|sprites/[^/]+\.png|sprites/spritemap\.json|strings/[^/]+\.csv"
                  r"|translation/(StringTable\.csv|StringTable\.meta\.tsv|MANIFEST\.json|scene/scene_text\.txt"
                  r"|scene/resize/[^/]+\.resizer\.txt))$")
# what never ships: the files the game writes while it runs, publish.py's own state, a harness folder
NOT_SHIPPED = set(publish.RUNTIME_FILES) | {publish.REL_STATE}
NOT_SHIPPED_PREFIX = ("harness/",)

BEPINEX_NOTICE_HEAD = """LOM English Localization + QoL - the bundled mod loader

The full package bundles BepInEx 6.0.0-be.692 (Unity Mono, x86) unmodified: doorstop_config.ini, winhttp.dll,
.doorstop_version, changelog.txt and everything in BepInEx/core are byte for byte the files of
%(zip)s
(SHA-256 %(sha)s), downloaded from
%(url)s
BepInEx: https://github.com/BepInEx/BepInEx, source of this build:
https://github.com/BepInEx/BepInEx/tree/851521cc126e4f9d841d2d9bfe857558f0395939
Licence: GNU Lesser General Public License 2.1 (the full text is below).

BepInEx includes:
  UnityDoorstop 4 (winhttp.dll)                     https://github.com/NeighTools/UnityDoorstop         LGPL-2.1
  HarmonyX (0Harmony.dll)                           https://github.com/BepInEx/HarmonyX                 MIT
  MonoMod (MonoMod.Utils, MonoMod.RuntimeDetour)    https://github.com/MonoMod/MonoMod                  MIT
  Mono.Cecil (Mono.Cecil*.dll)                      https://github.com/jbevain/cecil                    MIT
  SemanticVersioning (SemanticVersioning.dll)       https://github.com/adamreeve/semver.net             MIT
  AssetRipper.Primitives                            https://github.com/AssetRipper/AssetRipper.Primitives  MIT
Their licence texts follow, then the LGPL-2.1.
"""
LICENSE_FILES = [("HarmonyX", "HarmonyX-LICENSE.txt"), ("MonoMod", "MonoMod-LICENSE.txt"), ("Mono.Cecil", "cecil-LICENSE.txt"),
                 ("SemanticVersioning", "semver.net-LICENSE.txt"), ("AssetRipper.Primitives", "AssetRipper.Primitives-LICENSE.txt"),
                 ("BepInEx and UnityDoorstop: GNU Lesser General Public License 2.1", "BepInEx-LICENSE.txt")]


class ReleaseError(Exception):
    pass


def sha256(b):
    return hashlib.sha256(b).hexdigest()


def read(p):
    with open(p, "rb") as f:
        return f.read()


def source_version():
    text = open(os.path.join(SRC, "LOM_UI_EN.cs"), encoding="utf-8").read()
    m = re.search(r'public const string VERSION = "([^"]+)";', text)
    if not m:
        raise ReleaseError("no VERSION constant in src/LOM_UI_EN/LOM_UI_EN.cs")
    return m.group(1)


def plugin_files(plugin):
    """{rel: path} of every file in the plugin folder, links refused (publish.list_folder)."""
    files, links = publish.list_folder(plugin)
    if links:
        raise ReleaseError("links or junctions in the plugin folder: %s" % links[:5])
    return files


def classify(files):
    ship, skipped, unknown = {}, [], []
    for rel, p in sorted(files.items()):
        if SHIP.match(rel):
            ship[rel] = p
        elif rel in NOT_SHIPPED or rel.startswith(NOT_SHIPPED_PREFIX):
            skipped.append(rel)
        else:
            unknown.append(rel)
    return ship, skipped, unknown


def rebuild_matches(installed_dll):
    tmp = tempfile.mkdtemp(prefix="lom_release_build_")
    try:
        r = subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", os.path.join(SRC, "build.ps1"),
                            "-Game", P.GAME, "-OutDir", tmp], capture_output=True, text=True)
        if r.returncode != 0:
            raise ReleaseError("build.ps1 failed:\n%s%s" % (r.stdout, r.stderr))
        built = read(os.path.join(tmp, "LOM_UI_EN.dll"))
        return sha256(built) == sha256(read(installed_dll)), sha256(built)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def bepinex_entries():
    """[(name, bytes or None for a folder)] of the official BepInEx zip, after checking its pinned hash."""
    p = os.path.join(VENDOR, BEPINEX_ZIP)
    if not os.path.exists(p):
        raise ReleaseError("%s is missing: download it from %s into %s" % (BEPINEX_ZIP, BEPINEX_SOURCE, VENDOR))
    data = read(p)
    if sha256(data) != BEPINEX_SHA256:
        raise ReleaseError("%s is not the pinned official file (SHA-256 %s, expected %s)" % (BEPINEX_ZIP, sha256(data), BEPINEX_SHA256))
    out = []
    with zipfile.ZipFile(io.BytesIO(data)) as z:
        for i in z.infolist():
            name = i.filename.replace("\\", "/")
            if name.startswith(PLUGIN_PREFIX) or "/../" in "/" + name:
                raise ReleaseError("unexpected entry in the BepInEx zip: %s" % name)
            out.append((name, None if i.is_dir() else z.read(i)))
    return out


def bepinex_notice():
    parts = [BEPINEX_NOTICE_HEAD % {"zip": BEPINEX_ZIP, "sha": BEPINEX_SHA256, "url": BEPINEX_SOURCE}]
    for title, name in LICENSE_FILES:
        p = os.path.join(VENDOR, "licenses", name)
        if not os.path.exists(p):
            raise ReleaseError("licence text missing: %s" % p)
        text = read(p).decode("utf-8").replace("\r\n", "\n").strip("\n")
        parts.append("=" * 78 + "\n" + title + "\n\n" + text + "\n")
    return "\n".join(parts).encode("utf-8")


def write_zip(path, entries, stamp):
    """entries: [(name, bytes or None for a folder)], written sorted with one time stamp, deflated."""
    seen = set()
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for name, data in sorted(entries, key=lambda e: e[0]):
            if name in seen:
                raise ReleaseError("duplicate zip entry %s" % name)
            seen.add(name)
            zi = zipfile.ZipInfo(name, date_time=stamp)
            if data is None:
                zi.external_attr = 0o40755 << 16 | 0x10
                z.writestr(zi, b"")
            else:
                zi.compress_type = zipfile.ZIP_DEFLATED
                zi.external_attr = 0o100644 << 16
                z.writestr(zi, data)
    with zipfile.ZipFile(path) as z:
        bad = z.testzip()
        if bad:
            raise ReleaseError("%s: entry %s is corrupt" % (path, bad))
        want = {n: d for n, d in entries if d is not None}
        got = {i.filename: z.read(i) for i in z.infolist() if not i.is_dir()}
        if set(got) != set(want) or any(sha256(got[n]) != sha256(want[n]) for n in want):
            raise ReleaseError("%s does not read back as written" % path)


def main(argv=None):
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--candidate", action="store_true", help="package a build not yet marked as checked in game")
    ap.add_argument("--out", help="output folder")
    ap.add_argument("--skip-rebuild-check", action="store_true", help="do not rebuild the DLL from source to compare")
    a = ap.parse_args(argv)
    plugin = P.DEFAULT_PLUGIN
    version = source_version()
    probs, notes = [], []

    # 1. publish.py check
    cp, _info = publish.check(plugin)
    probs += ["publish.py check: " + p for p in cp]
    # 5. every file accounted for
    files = plugin_files(plugin)
    ship, skipped, unknown = classify(files)
    if unknown:
        probs.append("files in the plugin folder that are neither shipped nor known not to be (add them to SHIP or move "
                     "them out): %s" % unknown[:10])
    for need in ("LOM_UI_EN.dll", "Newtonsoft.Json.dll", "README.txt", "THIRD_PARTY_NOTICES.txt", "compat_verified.json",
                 "translation/StringTable.csv", "translation/scene/scene_text.txt", "translation/MANIFEST.json"):
        if need not in ship:
            probs.append("missing from the plugin folder: %s" % need)
    # 2. source == binary
    if "LOM_UI_EN.dll" in ship:
        if a.skip_rebuild_check:
            notes.append("rebuild check SKIPPED (--skip-rebuild-check)")
        else:
            same, built = rebuild_matches(ship["LOM_UI_EN.dll"])
            if same:
                notes.append("LOM_UI_EN.dll = a fresh deterministic build of src/LOM_UI_EN (SHA-256 %s)" % built)
            else:
                probs.append("the installed LOM_UI_EN.dll is not the build of the current source (fresh build %s, installed %s): "
                             "build, install and test it first" % (built[:16], sha256(read(ship["LOM_UI_EN.dll"]))[:16]))
    # 3. README
    if "README.txt" in ship and read(ship["README.txt"]) != read(os.path.join(SRC, "PLAYER_README.txt")):
        probs.append("README.txt is not src/LOM_UI_EN/PLAYER_README.txt: copy it over")
    # 4. verified in game by this version
    stamp_date = datetime.date.today()
    if "compat_verified.json" in ship:
        v = json.loads(read(ship["compat_verified.json"]).decode("utf-8-sig"))
        if v.get("mod_version") != version:
            msg = "compat_verified.json was marked by %s, not %s: test this build in game, then Mark as checked" % (
                v.get("mod_version"), version)
            if a.candidate:
                notes.append("CANDIDATE: " + msg)
            else:
                probs.append(msg)
        else:
            try:
                stamp_date = datetime.date.fromisoformat(v.get("verified_on", ""))
            except ValueError:
                pass
            notes.append("checked in game on %s with game %s (fingerprint %s)" % (
                v.get("verified_on"), v.get("game_version"), v.get("fingerprint")))
    if probs:
        for p in probs:
            print("REFUSED:", p)
        print("release %s: NOT packaged (%d problems)" % (version, len(probs)))
        return 1

    tag = version + ("-candidate" if a.candidate else "")
    out = a.out or os.path.join(RELEASE, "dist", tag)
    os.makedirs(out, exist_ok=True)
    stamp = (stamp_date.year, stamp_date.month, stamp_date.day, 12, 0, 0)
    plugin_entries = [(PLUGIN_PREFIX + rel, read(p)) for rel, p in ship.items()]
    mod_zip = os.path.join(out, "%s-%s-mod-only.zip" % (SLUG, tag))
    full_zip = os.path.join(out, "%s-%s-full.zip" % (SLUG, tag))
    write_zip(mod_zip, plugin_entries, stamp)
    full = bepinex_entries() + [("BepInEx/THIRD_PARTY_NOTICES-BepInEx.txt", bepinex_notice())] + plugin_entries
    write_zip(full_zip, full, stamp)

    sums = ["%s  %s" % (sha256(read(z)), os.path.basename(z)) for z in (full_zip, mod_zip)]
    with open(os.path.join(out, "SHA256SUMS.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(sums) + "\n")
    with open(os.path.join(out, "FILES.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("# %s %s: files in the full package (the mod-only package is the %s part)\n" % (SLUG, tag, PLUGIN_PREFIX))
        for name, data in sorted(full, key=lambda e: e[0]):
            if data is not None:
                f.write("%s\t%d\t%s\n" % (name, len(data), sha256(data)))
    for n in notes:
        print("  " + n)
    print("  shipped from the plugin folder: %d files, %.1f MB" % (len(ship), sum(len(d) for _, d in plugin_entries) / 1048576.0))
    print("  not shipped: %s" % (", ".join(skipped) or "none"))
    for z in (full_zip, mod_zip):
        print("  %-60s %6.1f MB" % (os.path.basename(z), os.path.getsize(z) / 1048576.0))
    print("release %s: packaged into %s" % (tag, out))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (ReleaseError, publish.PublishError) as e:
        print("REFUSED:", e)
        sys.exit(1)
