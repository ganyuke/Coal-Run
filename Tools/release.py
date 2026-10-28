#!/usr/bin/env python3
"""Builds Coal Run for a release and packs it into dist/<version>/.

    python3 Tools/release.py 0.2              # build and zip
    python3 Tools/release.py 0.2 --release    # ...and make GitHub release v0.2 with the zips

Sets the game's version (Player Settings) to the one given, builds every target the editor has the module
for (see Assets/Editor/Builder.cs), and zips each build as dist/<version>/CoalRun-<version>-<Target>.zip.
The unzipped builds stay beside the zips (dist/<version>/WebGL/ is the folder to put on a web server).

Always builds a snapshot of saved files in .build-cache/project, with its own Library cache.
The interactive Editor can stay open, including in Play mode. Unsaved Editor changes aren't included.
Use --background to detach, --target WebGL to build only the web version, or --prepare-only to
refresh the snapshot without launching Unity. Requires unity, rsync and (on Linux) FFmpeg with AAC.
UNITY_EDITOR overrides the Editor binary. --release needs the GitHub CLI (`gh`), logged in.
"""
import argparse
import fcntl
import os
import re
import shlex
import shutil
import subprocess
import sys
import tempfile
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EDITOR = os.environ.get("UNITY_EDITOR", os.path.expanduser("~/IDEs/6000.5.9f1/Editor/Unity"))
TARGETS = ("Windows", "Linux", "WebGL")
BUILD_TARGETS = {"Windows": "StandaloneWindows64", "Linux": "StandaloneLinux64", "WebGL": "WebGL"}
CACHE = os.path.join(ROOT, ".build-cache")
PROJECT = os.path.join(CACHE, "project")
# Unity leaves these next to a build for debugging crashes; they're not for players.
NOT_SHIPPED = re.compile(r"_(BurstDebugInformation|BackUpThisFolder_ButDontShipItWithYourGame)_DoNotShip$|_DoNotShip$")


def preflight(targets):
    for command in ("unity", "rsync"):
        if shutil.which(command) is None:
            sys.exit(f"{command} isn't installed.")
    if not os.path.exists(EDITOR):
        sys.exit(f"No Unity at {EDITOR}. Set UNITY_EDITOR to the Editor binary.")
    if sys.platform.startswith("linux") and "WebGL" in targets:
        if shutil.which("ffmpeg") is None:
            sys.exit("WebGL audio requires FFmpeg. Install it before building.")
        encoders = subprocess.run(["ffmpeg", "-hide_banner", "-encoders"], capture_output=True,
                                  text=True, check=True).stdout
        if not re.search(r"\baac\s", encoders):
            sys.exit("FFmpeg has no AAC encoder; WebGL audio cannot be imported.")


def snapshot():
    """Mirror only project inputs; never share Library, Temp, or files via hardlinks."""
    os.makedirs(PROJECT, exist_ok=True)
    # Don't sync into an Editor someone has opened on the build copy itself.
    if os.path.exists(os.path.join(PROJECT, "Temp", "UnityLockfile")):
        sys.exit(f"The build copy is locked by Unity: {PROJECT}. Close that instance first.")
    for folder in ("Assets", "Packages", "ProjectSettings"):
        subprocess.run(["rsync", "-aL", "--checksum", "--delete",
                        os.path.join(ROOT, folder) + "/", os.path.join(PROJECT, folder) + "/"], check=True)
    print(f"Snapshot ready: {PROJECT}\nIncludes saved, uncommitted files; save scenes/assets before starting.", flush=True)


def build_command(version, target, output, log):
    # Start with the right platform: switching targets inside a batch Editor cannot reload scripts.
    return ["unity", "build", PROJECT, "--editor-path", EDITOR, "--target", BUILD_TARGETS[target],
            "--execute-method", "CoalRun.Editor.Builder.BuildFromCommandLine", "--output-path", output,
            "--args", shlex.join(["-releaseVersion", version, "-releaseTargets", target]),
            "--log-file", log, "--timeout", "2700", "--allow-dirty-build", "--no-provenance"]


def build_in_batch(version, target, output, log):
    print(f"Building in a separate Unity process (log: {log})...", flush=True)
    result = subprocess.run(build_command(version, target, output, log), cwd=PROJECT)
    if result.returncode != 0:
        sys.exit(f"Build failed (exit {result.returncode}). Existing releases were kept. See {log}")
    # Import errors can occur before BuildPlayer starts and still leave Unity returning success.
    if not os.path.isfile(log):
        sys.exit(f"Unity returned without creating its build log. Existing releases were kept. Expected {log}")
    with open(log, errors="replace") as f:
        if "Errors during import of AudioClip" in f.read():
            sys.exit(f"Audio import failed. Existing releases were kept. See {log}")


def publish_output(staging, dist):
    """Replace a release only after building and packing succeed; keep the previous output."""
    if os.path.exists(dist):
        backup = tempfile.mkdtemp(prefix=os.path.basename(dist) + "-previous-", dir=CACHE)
        shutil.move(dist, os.path.join(backup, os.path.basename(dist)))
        print(f"Previous release kept in {backup}", flush=True)
        try:
            os.rename(staging, dist)
        except OSError:
            shutil.move(os.path.join(backup, os.path.basename(dist)), dist)
            raise
    else:
        os.rename(staging, dist)


def preserve_other_targets(staging, dist, targets):
    # Rebuilding just WebGL should keep the Windows/Linux downloads for the same version.
    for target in TARGETS:
        if target in targets:
            continue
        folder = os.path.join(dist, target)
        if os.path.isdir(folder):
            shutil.copytree(folder, os.path.join(staging, target))
        archive = f"CoalRun-{os.path.basename(dist)}-{target}.zip"
        if os.path.isfile(os.path.join(dist, archive)):
            shutil.copy2(os.path.join(dist, archive), os.path.join(staging, archive))
    previous_report = os.path.join(dist, "build.txt")
    if os.path.isfile(previous_report):
        with open(previous_report) as f:
            previous = [line for line in f if line.split() and line.split()[0] in TARGETS
                        and line.split()[0] not in targets]
        with open(os.path.join(staging, "build.txt"), "a") as f:
            f.writelines(previous)


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
    ap.add_argument("--target", choices=TARGETS, action="append", help="build only this target (repeatable)")
    mode = ap.add_mutually_exclusive_group()
    mode.add_argument("--background", action="store_true", help="detach and print the PID and log path")
    mode.add_argument("--prepare-only", action="store_true", help="check dependencies and sync the build copy only")
    args = ap.parse_args()
    version = args.version.removeprefix("v")
    if not re.fullmatch(r"\d+(\.\d+){1,2}", version):
        sys.exit("The version should look like 0.2 or 0.2.1.")

    targets = tuple(dict.fromkeys(args.target or TARGETS))
    preflight(targets)
    os.makedirs(CACHE, exist_ok=True)
    if args.background:
        log = os.path.join(CACHE, f"{version}-release.log")
        with open(log, "a") as f:
            process = subprocess.Popen([sys.executable, "-u", os.path.abspath(__file__),
                                        *[arg for arg in sys.argv[1:] if arg != "--background"]],
                                       cwd=ROOT, stdin=subprocess.DEVNULL, stdout=f, stderr=subprocess.STDOUT,
                                       start_new_session=True)
        print(f"Background release PID: {process.pid}\nLog: {log}")
        return

    # Keep this descriptor alive through packing; parallel invocations must not mutate the snapshot.
    with open(os.path.join(CACHE, "release.lock"), "a") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            sys.exit("Another release is using the build copy. Wait for it to finish.")
        run_release(version, targets, args.prepare_only, args.release)


def run_release(version, targets, prepare_only, github_release):
    snapshot()
    if prepare_only:
        return
    os.makedirs(os.path.join(ROOT, "dist"), exist_ok=True)
    dist = os.path.join(ROOT, "dist", version)
    staging = tempfile.mkdtemp(prefix=f".{version}-build-", dir=os.path.join(ROOT, "dist"))
    done = os.path.join(staging, "build.txt")
    report = []
    for target in targets:
        if os.path.exists(done):
            os.unlink(done)
        build_in_batch(version, target, staging, os.path.join(CACHE, f"{version}-{target}-build.log"))
        if not os.path.isfile(done):
            sys.exit("Unity returned without a build report. Existing releases were kept.")
        with open(done) as f:
            lines = f.read().splitlines()
        if len(lines) != 1 or not lines[0].startswith(target + " "):
            sys.exit(f"Unexpected build report for {target}. Existing releases were kept.")
        report.extend(lines)
    with open(done, "w") as f:
        f.write("\n".join(report) + "\n")
    print("\n".join("  " + line for line in report))
    if any(" Failed " in line or " Cancelled " in line for line in report):
        sys.exit("A build failed; see the editor's console (or the batch log). Nothing was zipped.")

    zips = []
    for target in targets:
        folder = os.path.join(staging, target)
        if not os.path.isdir(folder):
            continue
        zip_path = os.path.join(staging, f"CoalRun-{version}-{target}.zip")
        pack(folder, zip_path)
        zips.append(os.path.join(dist, os.path.basename(zip_path)))
        print(f"  {os.path.basename(zip_path)}  {os.path.getsize(zip_path) / 1048576:.1f} MB")
    if not zips:
        sys.exit("Nothing was built.")
    preserve_other_targets(staging, dist, targets)
    publish_output(staging, dist)
    print(f"Release ready: {dist}", flush=True)

    if not github_release:
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
