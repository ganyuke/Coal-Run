#!/usr/bin/env python3
"""Builds Coal Run for a release and packs it into dist/<version>/.

    python3 Tools/release.py 0.2              # build and zip
    python3 Tools/release.py 0.2 --release    # ...and make GitHub release v0.2 with the zips

Sets the game's version (Player Settings) to the one given, builds every target the editor has the module
for (see Assets/Editor/Builder.cs), and zips each build as dist/<version>/CoalRun-<version>-<Target>.zip.
The unzipped builds stay beside the zips (dist/<version>/WebGL/ is the folder to put on a web server).

If the editor is open on the project it builds there, through the `unity` CLI; otherwise it runs Unity in
batch mode (UNITY_EDITOR overrides where to find it). --release needs the GitHub CLI (`gh`), logged in.
"""
import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import time
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EDITOR = os.environ.get("UNITY_EDITOR", os.path.expanduser("~/IDEs/6000.5.9f1/Editor/Unity"))
TARGETS = ("Windows", "Linux", "WebGL")
# Unity leaves these next to a build for debugging crashes; they're not for players.
NOT_SHIPPED = re.compile(r"_(BurstDebugInformation|BackUpThisFolder_ButDontShipItWithYourGame)_DoNotShip$|_DoNotShip$")


def unity_eval(code):
    """Runs C# in the open editor. Returns the result, or None if no editor answers."""
    try:
        out = subprocess.run(["unity", "command", "eval", code, "--no-banner", "--format", "json"],
                             capture_output=True, text=True, timeout=60)
    except (FileNotFoundError, subprocess.TimeoutExpired):
        return None
    try:
        data = json.loads(out.stdout)
    except json.JSONDecodeError:
        return None
    if not data.get("success"):
        return None
    return data["data"]["result"]["result"]


def build_in_editor(version, done):
    if unity_eval("return UnityEditor.EditorApplication.isPlaying;") == "True":
        sys.exit("The editor is in Play mode. Stop it and run this again.")
    # Queued for the editor's next tick, so this call returns straight away rather than timing out on a long build.
    unity_eval(f'UnityEditor.EditorApplication.delayCall += () => CoalRun.Editor.Builder.Build("{version}"); return 0;')
    print("Building in the open editor...", flush=True)
    start = time.time()
    while not os.path.exists(done):
        if time.time() - start > 45 * 60:
            sys.exit("Gave up after 45 minutes. Check the editor's console.")
        time.sleep(3)


def build_in_batch(version, log):
    if not os.path.exists(EDITOR):
        sys.exit(f"No editor open and no Unity at {EDITOR}. Open the project, or set UNITY_EDITOR.")
    print(f"Building in batch mode (log: {os.path.relpath(log, ROOT)})...", flush=True)
    result = subprocess.run([EDITOR, "-batchmode", "-quit", "-projectPath", ROOT, "-logFile", log,
                             "-executeMethod", "CoalRun.Editor.Builder.BuildFromCommandLine", "-version", version])
    if result.returncode != 0 and not os.path.exists(os.path.join(ROOT, "dist", version, "build.txt")):
        sys.exit(f"Unity exited with {result.returncode} before building. See the log.")


def pack(folder, zip_path):
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
        for here, dirs, files in os.walk(folder):
            dirs[:] = [d for d in dirs if not NOT_SHIPPED.search(d)]
            for f in files:
                path = os.path.join(here, f)
                z.write(path, os.path.relpath(path, folder))


def main():
    ap = argparse.ArgumentParser(description="Build Coal Run into dist/<version>/ and optionally release it.")
    ap.add_argument("version", help="like 0.2 or 0.2.1")
    ap.add_argument("--release", action="store_true", help="make GitHub release v<version> with the zips")
    args = ap.parse_args()
    version = args.version.removeprefix("v")
    if not re.fullmatch(r"\d+(\.\d+){1,2}", version):
        sys.exit("The version should look like 0.2 or 0.2.1.")

    dist = os.path.join(ROOT, "dist", version)
    if os.path.exists(dist):
        shutil.rmtree(dist)
    done = os.path.join(dist, "build.txt")
    if unity_eval("return 0;") is not None:
        build_in_editor(version, done)
    else:
        os.makedirs(os.path.join(ROOT, "dist"), exist_ok=True)
        build_in_batch(version, os.path.join(ROOT, "dist", f"{version}-build.log"))

    with open(done) as f:
        report = f.read().splitlines()
    print("\n".join("  " + line for line in report))
    if any(" Failed " in line or " Cancelled " in line for line in report):
        sys.exit("A build failed; see the editor's console (or the batch log). Nothing was zipped.")

    zips = []
    for target in TARGETS:
        folder = os.path.join(dist, target)
        if not os.path.isdir(folder):
            continue
        zip_path = os.path.join(dist, f"CoalRun-{version}-{target}.zip")
        pack(folder, zip_path)
        zips.append(zip_path)
        print(f"  {os.path.relpath(zip_path, ROOT)}  {os.path.getsize(zip_path) / 1048576:.1f} MB")
    if not zips:
        sys.exit("Nothing was built.")

    if not args.release:
        return
    if shutil.which("gh") is None:
        sys.exit("The GitHub CLI (gh) isn't installed, so no release was made. The zips are in dist/.")
    head = subprocess.run(["git", "rev-parse", "HEAD"], cwd=ROOT, capture_output=True, text=True).stdout.strip()
    if subprocess.run(["git", "status", "--porcelain", "--untracked-files=no"], cwd=ROOT, capture_output=True, text=True).stdout.strip():
        print("Note: there are uncommitted changes; the release is tagged on the last commit, which doesn't have them.")
    subprocess.run(["gh", "release", "create", f"v{version}", *zips, "--target", head,
                    "--title", f"Coal Run {version}", "--generate-notes"], cwd=ROOT, check=True)


if __name__ == "__main__":
    main()
