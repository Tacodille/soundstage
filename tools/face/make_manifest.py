"""Writes src/Soundstage/Face/face-pack.json: every file the on-demand face helper downloads, with URL, size and SHA-256.

Dev tool. Run it when a pinned version changes; the plugin never runs it. Sources are official ones only:
  python.org            the Windows embeddable CPython (the SHA-256 is measured here once and pinned, python.org lists MD5 only)
  PyPI                  each wheel, with the SHA-256 and size PyPI publishes
  Google AI Edge        face_landmarker.task, SHA-256 measured here once and pinned

    make_manifest.py --lean <venv with the working minimal install> --out <face-pack.json> [--model <task file>]
"""
import argparse
import hashlib
import json
import subprocess
import sys
import urllib.request

PYTHON_EMBED = "https://www.python.org/ftp/python/{v}/python-{v}-embed-amd64.zip"
MODEL_URL = "https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/1/face_landmarker.task"
# Packages the helper needs to import, found by running it in a venv with --no-deps (see docs/face-model-decision.md).
PACKAGES = ["mediapipe", "numpy", "opencv-python-headless", "absl-py", "flatbuffers", "certifi", "matplotlib", "packaging",
            "pillow", "pyparsing", "cycler", "python-dateutil", "kiwisolver", "fonttools"]
UA = {"User-Agent": "soundstage-dev-tool/0.1"}


def get(url):
    return urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=120).read()


def sha256_url(url):
    h = hashlib.sha256()
    n = 0
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=300) as r:
        while True:
            b = r.read(1 << 20)
            if not b:
                break
            h.update(b)
            n += len(b)
    return h.hexdigest(), n


def installed_versions(py):
    out = subprocess.check_output([py, "-m", "pip", "freeze"], text=True)
    d = {}
    for line in out.splitlines():
        if "==" in line:
            n, v = line.split("==")
            d[n.lower().replace("_", "-")] = v
    return d


def wheel_for(name, version):
    info = json.loads(get(f"https://pypi.org/pypi/{name}/{version}/json"))
    cands = [u for u in info["urls"] if u["packagetype"] == "bdist_wheel"]

    def score(u):
        fn = u["filename"]
        if "cp314-cp314-win_amd64" in fn:
            return 3
        if "abi3-win_amd64" in fn or "none-win_amd64" in fn:
            return 2
        if fn.endswith("none-any.whl"):
            return 1
        return 0

    best = max(cands, key=score)
    if score(best) == 0:
        raise SystemExit(f"no usable wheel for {name} {version}: {[c['filename'] for c in cands]}")
    return {"name": name, "version": version, "file": best["filename"], "url": best["url"],
            "sha256": best["digests"]["sha256"], "size": best["size"], "kind": "wheel"}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--lean", required=True, help="python.exe of the venv the pack was proven in")
    ap.add_argument("--out", required=True)
    ap.add_argument("--model", help="the downloaded face_landmarker.task, to take its hash from instead of downloading again")
    a = ap.parse_args()

    pyver = subprocess.check_output([a.lean, "-c", "import platform;print(platform.python_version())"], text=True).strip()
    have = installed_versions(a.lean)
    files = []
    embed_url = PYTHON_EMBED.format(v=pyver)
    sha, size = sha256_url(embed_url)
    files.append({"name": "python-embeddable", "version": pyver, "file": f"python-{pyver}-embed-amd64.zip", "url": embed_url,
                  "sha256": sha, "size": size, "kind": "python"})
    for p in PACKAGES:
        files.append(wheel_for(p, have[p]))
    if a.model:
        data = open(a.model, "rb").read()
        sha, size = hashlib.sha256(data).hexdigest(), len(data)
    else:
        sha, size = sha256_url(MODEL_URL)
    files.append({"name": "face_landmarker", "version": "float16/1", "file": "face_landmarker.task", "url": MODEL_URL,
                  "sha256": sha, "size": size, "kind": "model"})
    manifest = {
        "formatVersion": 1,
        "helperVersion": "1",
        "pythonTag": "cp314",
        "licences": {
            "face_landmarker.task": "Apache-2.0 (Google AI Edge MediaPipe model)",
            "python": "PSF-2.0 (python.org embeddable)",
            "mediapipe": "Apache-2.0", "absl-py": "Apache-2.0", "flatbuffers": "Apache-2.0",
            "numpy": "BSD-3-Clause AND 0BSD AND MIT AND Zlib AND CC0-1.0 (per PyPI)",
            "opencv-python-headless": "Apache-2.0 (the wheel also bundles FFmpeg under LGPL-2.1, per the opencv-python project)",
            "matplotlib": "Matplotlib licence (PSF-based)", "pillow": "MIT-CMU",
            "other packages": "see each wheel; none is redistributed by Soundstage, the user downloads them from PyPI",
        },
        "files": files,
    }
    with open(a.out, "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, indent=2)
        f.write("\n")
    total = sum(x["size"] for x in files)
    print(f"{len(files)} files, {total/1e6:.1f} MB to download; wrote {a.out}")


if __name__ == "__main__":
    sys.exit(main())
