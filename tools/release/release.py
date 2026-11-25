"""Package a Soundstage release: the plugin zip, the ReShade add-on and the repo.json Dalamud reads.

By default nothing leaves this machine. The zip, the add-on and repo.json are written into dist/ (gitignored).

    python tools/release/release.py                 build, then package into dist/ (the csproj version is left alone)
    python tools/release/release.py --dry-run       build, then write only dist/repo.json (no zip copied)
    python tools/release/release.py --version 0.1.1 set the csproj version first
    python tools/release/release.py --publish       the only mode that touches a releases clone: copy into publish/,
                                                    run the scrub, commit and push. Needs publish/ to be a clone of
                                                    Tacodille/soundstage-releases, and bumps the patch number unless --version is given.

What it does, in order:
  1. (--version, or --publish) sets <Version> in the csproj.
  2. Builds the plugin in Release. DalamudPackager writes bin/Release/Soundstage/latest.zip and the manifest.
  3. Writes repo.json from the packaged manifest, with download links into the releases repository's raw URL.
  4. Copies the zip, and soundstage.addon64 if tools/build_addon.py has built it, beside repo.json.
  5. (--publish only) scrub_check.py must pass, then git add, commit and push in publish/.

The add-on ships as a separate file beside the zip. The plugin never writes into the game folder, so the user copies it in.
"""
import argparse
import json
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CSPROJ = ROOT / "src" / "Soundstage" / "Soundstage.csproj"
PACKAGED = ROOT / "src" / "Soundstage" / "bin" / "Release" / "Soundstage"
ADDON = ROOT / "reshade-addon" / "out" / "soundstage.addon64"
DIST = ROOT / "dist"
PUBLISH = ROOT / "publish"
RAW = "https://raw.githubusercontent.com/Tacodille/soundstage-releases/main"
ZIP_PATH = "plugins/Soundstage/latest.zip"
ADDON_PATH = "plugins/Soundstage/soundstage.addon64"


def dotnet() -> str:
    found = shutil.which("dotnet")
    if found:
        return found
    fallback = Path(r"C:\Program Files\dotnet\dotnet.exe")
    if fallback.exists():
        return str(fallback)
    raise SystemExit("dotnet was not found on PATH.")


def current_version() -> str:
    m = re.search(r"<Version>(\d+\.\d+\.\d+)</Version>", CSPROJ.read_text(encoding="utf-8"))
    if not m:
        raise SystemExit("No <Version>X.Y.Z</Version> in the csproj.")
    return m[1]


def set_version(new: str) -> None:
    text = CSPROJ.read_text(encoding="utf-8")
    CSPROJ.write_text(re.sub(r"<Version>\d+\.\d+\.\d+</Version>", f"<Version>{new}</Version>", text, count=1), encoding="utf-8", newline="\n")


def bump_patch() -> str:
    major, minor, patch = (int(x) for x in current_version().split("."))
    return f"{major}.{minor}.{patch + 1}"


def run(args: list[str], cwd: Path) -> None:
    print("  >", " ".join(args))
    subprocess.run(args, cwd=cwd, check=True, timeout=1800)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--version", help="set the csproj version before building")
    ap.add_argument("--dry-run", action="store_true", help="build and write repo.json only")
    ap.add_argument("--publish", action="store_true", help="copy into publish/, commit and push (the only mode that does)")
    a = ap.parse_args()

    target = PUBLISH if a.publish else DIST
    if a.publish and not (PUBLISH / ".git").exists():
        raise SystemExit(f"{PUBLISH} isn't a clone of the releases repository, so there is nothing to publish into.")

    if a.version:
        set_version(a.version)
    elif a.publish:
        set_version(bump_patch())
    version = current_version()
    print(f"Version {version}  ({'publish' if a.publish else 'local packaging into dist/'}{', dry run' if a.dry_run else ''})")

    run([dotnet(), "build", "src/Soundstage", "-c", "Release"], ROOT)

    manifest = json.loads((PACKAGED / "Soundstage.json").read_text(encoding="utf-8"))
    if manifest["InternalName"] != "Soundstage":
        raise SystemExit(f"Unexpected InternalName {manifest['InternalName']!r} in the packaged manifest.")
    if not manifest["AssemblyVersion"].startswith(version):
        raise SystemExit(f"Packaged version {manifest['AssemblyVersion']} doesn't match {version}; was the build stale?")

    target.mkdir(parents=True, exist_ok=True)
    if not a.dry_run:
        zip_target = target / ZIP_PATH
        zip_target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(PACKAGED / "latest.zip", zip_target)
        print(f"zip -> {zip_target}")
        if ADDON.exists():
            shutil.copyfile(ADDON, target / ADDON_PATH)
            print(f"add-on -> {target / ADDON_PATH}")
        else:
            print("add-on: not built (python tools/build_addon.py), so it is left out of this package.")

    link = f"{RAW}/{ZIP_PATH}"
    entry = {
        **manifest,
        "DownloadLinkInstall": link,
        "DownloadLinkUpdate": link,
        "DownloadLinkTesting": link,
        "IsHide": False,
        "IsTestingExclusive": False,
        "LastUpdate": int(time.time()),
        "DownloadCount": 0,
    }
    (target / "repo.json").write_text(json.dumps([entry], indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"repo.json -> {target / 'repo.json'} ({manifest['InternalName']} {manifest['AssemblyVersion']})")

    if not a.publish:
        print("Nothing was published. Pass --publish to copy into publish/, commit and push.")
        return 0
    if a.dry_run:
        print("Dry run: not committing.")
        return 0

    scrub = subprocess.run([sys.executable, str(ROOT / "tools" / "scrub_check.py")], cwd=ROOT)
    if scrub.returncode != 0:
        raise SystemExit("scrub_check.py found something. Fix it before publishing.")
    run(["git", "add", "-A"], PUBLISH)
    run(["git", "commit", "-m", f"Release {manifest['AssemblyVersion']}"], PUBLISH)
    run(["git", "push"], PUBLISH)
    print(f"Published. Repository link for /xlsettings: {RAW}/repo.json")
    return 0


if __name__ == "__main__":
    sys.exit(main())
