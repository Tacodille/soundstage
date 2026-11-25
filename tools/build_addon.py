"""Build the Soundstage ReShade add-on (reshade-addon/soundstage.cpp) into reshade-addon/out/soundstage.addon64.

Finds MSVC through vswhere, loads the x64 environment (vcvars64.bat) and runs `cl /LD`. No cmake needed.
It never copies the add-on into the game folder: that is a step for the person using Soundstage (see the test
checklist). It prints where the file has to go.

Usage: python tools/build_addon.py [--game-folder "<path to FFXIV's game folder>"]
Exit 0 on success, 1 on any failure.
"""
import argparse
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "reshade-addon" / "soundstage.cpp"
INCLUDE = ROOT / "reshade-addon" / "include"
OUT = ROOT / "reshade-addon" / "out"
DEFAULT_GAME = r"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game"


def find_vcvars() -> Path:
    candidates = [
        Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Microsoft Visual Studio" / "Installer" / "vswhere.exe",
        Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Microsoft Visual Studio" / "Installer" / "vswhere.exe",
    ]
    vswhere = next((c for c in candidates if c.exists()), None)
    if vswhere is None:
        sys.exit("vswhere.exe not found: install the Visual Studio Build Tools with the C++ workload.")
    result = subprocess.run(
        [str(vswhere), "-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath"],
        capture_output=True, text=True,
    )
    install = result.stdout.strip().splitlines()[0] if result.stdout.strip() else ""
    if not install:
        sys.exit("No Visual Studio install with the C++ x64 tools found (vswhere printed nothing).")
    vcvars = Path(install) / "VC" / "Auxiliary" / "Build" / "vcvars64.bat"
    if not vcvars.exists():
        sys.exit(f"vcvars64.bat not found at {vcvars}")
    return vcvars


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--game-folder", default=DEFAULT_GAME)
    args = parser.parse_args()

    vcvars = find_vcvars()
    OUT.mkdir(parents=True, exist_ok=True)
    target = OUT / "soundstage.addon64"
    if target.exists():
        target.unlink()

    # /LD builds a DLL; the .addon64 extension is only a name ReShade looks for. /MT keeps the C runtime inside the file,
    # so nothing has to be installed beside it.
    cl = (
        f'cl /nologo /std:c++17 /EHsc /O2 /MT /LD /W3 /I"{INCLUDE}" "{SRC}" '
        f'/Fo"{OUT}\\\\" /Fe"{target}" user32.lib'
    )
    # A small .bat avoids cmd.exe's quoting rules for a command line that holds several quoted paths.
    bat = OUT / "build.bat"
    bat.write_text(f'@echo off\r\ncall "{vcvars}" >nul\r\nif errorlevel 1 exit /b 1\r\n{cl}\r\n', encoding="ascii", newline="")
    print(cl)
    result = subprocess.run(["cmd.exe", "/d", "/c", str(bat)], cwd=str(OUT), stdin=subprocess.DEVNULL)
    if result.returncode != 0 or not target.exists():
        print(f"BUILD FAILED (cl exit {result.returncode})")
        return 1

    print(f"Built {target} ({target.stat().st_size} bytes)")
    print()
    print("Next (a step for you, the script does not do it): copy that file into the folder that holds dxgi.dll:")
    print(f"  {args.game_folder}")
    print("then restart the game. ReShade.log in that folder should show a '[Soundstage] add-on loaded' line.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
