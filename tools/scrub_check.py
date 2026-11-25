#!/usr/bin/env python3
"""Scrub check: finds personal paths, names, emails and wording that should not be in a public tree.

    python tools/scrub_check.py                 scan the tracked files of this repo (plus new, unignored ones)
    python tools/scrub_check.py --root DIR      scan every text file under DIR (no git needed)
    python tools/scrub_check.py --rev COMMIT    scan the tree of a commit (use it on the orphan commit before a push)

Exit 0 when nothing is found, 1 when something is (each hit is printed as file:line [rule] text), 2 on a usage error.

What it looks for is in RULES below. Two things it cannot hardcode, because writing them here would leak them:
  - the private character's name and any other private word: put one per line in tools/scrub_private.local.txt
    (gitignored), or pass SCRUB_EXTRA="word1,word2" in the environment.
The dev notes under docs/implement-notes/ are left out of the scan; they are not part of the public tree.
"""
from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent

# (rule name, compiled regex, applies to a path predicate or None for everywhere)
def _ci(p: str) -> re.Pattern[str]:
    return re.compile(p, re.IGNORECASE)


def _banwording_scope(rel: str) -> bool:
    return rel in ("README.md", "SETUP-AI.md") or rel.startswith("docs/") or rel.startswith("src/")


RULES: list[tuple[str, re.Pattern[str], object]] = [
    ("windows-user-path", re.compile(r"[A-Za-z]:[\\/]+Users[\\/]"), None),
    ("aios-path", _ci(r"[A-Za-z]:[\\/]+AIOS|\bAIOS\b"), None),
    ("username", re.compile(r"\bJacob\b|van der Linde"), None),
    ("taco-path-segment", _ci(r"[\\/]taco[\\/]"), None),
    ("taco-name", re.compile(r"\bTaco\b"), None),
    ("email", re.compile(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}"), None),
    ("apartment", _ci(r"\bapartment\b"), None),
    ("housing-lamp", _ci(r"housing[- ]lamp|lamp calibration"), None),
    ("keylight-alias", re.compile(r"/keylight\b"), None),
    ("ban-or-tos-wording", _ci(r"\b(?:ban|banned|banning|terms of service|tos)\b"), _banwording_scope),
]

# Paths left out of the scan (posix, relative to the root).
SKIP_PREFIXES = ("docs/implement-notes/", ".git/")
SKIP_FILES = {"tools/scrub_check.py"}
MAX_BYTES = 4_000_000


def extra_terms(root: Path) -> list[re.Pattern[str]]:
    words: list[str] = []
    env = os.environ.get("SCRUB_EXTRA", "")
    words += [w.strip() for w in env.split(",") if w.strip()]
    for candidate in (REPO / "tools" / "scrub_private.local.txt", root / "tools" / "scrub_private.local.txt"):
        if candidate.is_file():
            words += [w.strip() for w in candidate.read_text(encoding="utf-8", errors="replace").splitlines() if w.strip() and not w.startswith("#")]
    seen = []
    for w in dict.fromkeys(words):
        seen.append(re.compile(r"\b" + re.escape(w) + r"\b", re.IGNORECASE))
    return seen


def git(args: list[str], cwd: Path) -> bytes:
    return subprocess.run(["git", *args], cwd=str(cwd), check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE).stdout


def list_files_git(root: Path) -> list[str]:
    out = git(["ls-files", "--cached", "--others", "--exclude-standard", "-z"], root)
    return [p for p in out.decode("utf-8", "replace").split("\0") if p]


def list_files_walk(root: Path) -> list[str]:
    found = []
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d != ".git"]
        for name in filenames:
            found.append(Path(dirpath, name).relative_to(root).as_posix())
    return found


def is_git_repo(root: Path) -> bool:
    try:
        top = git(["rev-parse", "--show-toplevel"], root).decode().strip()
    except Exception:
        return False
    try:
        return Path(top).resolve() == root.resolve()
    except OSError:
        return False


def scan_text(rel: str, text: str, extras: list[re.Pattern[str]]) -> list[str]:
    hits = []
    for number, line in enumerate(text.splitlines(), 1):
        for name, pattern, scope in RULES:
            if scope is not None and not scope(rel):  # type: ignore[operator]
                continue
            m = pattern.search(line)
            if m:
                hits.append(f"{rel}:{number}: [{name}] {m.group(0)!r} in: {line.strip()[:110]}")
        for pattern in extras:
            m = pattern.search(line)
            if m:
                hits.append(f"{rel}:{number}: [private-term] (a word from the private list) in: {line.strip()[:60]}")
    return hits


def looks_binary(data: bytes) -> bool:
    return b"\0" in data[:4096]


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--root", type=Path, default=None, help="scan this folder instead of the repo")
    ap.add_argument("--rev", default=None, help="scan the tree of this commit")
    args = ap.parse_args()

    root = (args.root or REPO).resolve()
    if not root.is_dir():
        print(f"not a folder: {root}", file=sys.stderr)
        return 2
    extras = extra_terms(root)

    hits: list[str] = []
    scanned = 0

    if args.rev:
        names = git(["ls-tree", "-r", "-z", "--name-only", args.rev], root).decode("utf-8", "replace").split("\0")
        for rel in [n for n in names if n]:
            if rel.startswith(SKIP_PREFIXES) or rel in SKIP_FILES:
                continue
            data = git(["show", f"{args.rev}:{rel}"], root)
            if looks_binary(data) or len(data) > MAX_BYTES:
                continue
            scanned += 1
            hits += scan_text(rel, data.decode("utf-8", "replace"), extras)
    else:
        rels = list_files_git(root) if is_git_repo(root) else list_files_walk(root)
        for rel in rels:
            if rel.startswith(SKIP_PREFIXES) or rel in SKIP_FILES:
                continue
            path = root / rel
            try:
                if not path.is_file() or path.stat().st_size > MAX_BYTES:
                    continue
                data = path.read_bytes()
            except OSError:
                continue
            if looks_binary(data):
                continue
            scanned += 1
            hits += scan_text(rel, data.decode("utf-8", "replace"), extras)

    for h in hits:
        print(h)
    print(f"scrub_check: {scanned} text files scanned, {len(hits)} hit(s), "
          f"{'private-term list loaded' if extras else 'no private-term list (tools/scrub_private.local.txt)'}")
    return 1 if hits else 0


if __name__ == "__main__":
    sys.exit(main())
