"""Release isolation and failure handling: python3 -m unittest discover -s Tools -p test_release.py."""
import fcntl
import io
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import release


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.cache = self.root / ".build-cache"
        self.cache.mkdir()
        self.project = self.cache / "project"
        for name in ("Assets", "Packages", "ProjectSettings"):
            (self.root / name).mkdir()
        for name, value in (("ROOT", self.root), ("CACHE", self.cache), ("PROJECT", self.project)):
            p = patch.object(release, name, str(value))
            p.start()
            self.addCleanup(p.stop)
        p = patch("sys.stdout", new_callable=io.StringIO)
        p.start()
        self.addCleanup(p.stop)

    def test_snapshot_copies_uncommitted_files_prunes_deleted_inputs_and_keeps_cache(self):
        source = self.root / "Assets" / "new.wav"
        source.write_bytes(b"saved audio")
        release.snapshot()
        copied = self.project / "Assets" / "new.wav"
        self.assertEqual(copied.read_bytes(), source.read_bytes())
        self.assertNotEqual(copied.stat().st_ino, source.stat().st_ino)
        copied.write_bytes(b"build copy")
        self.assertEqual(source.read_bytes(), b"saved audio")
        library = self.project / "Library"
        library.mkdir()
        (library / "cache").write_text("keep")
        source.unlink()
        release.snapshot()
        self.assertFalse(copied.exists())
        self.assertEqual((library / "cache").read_text(), "keep")

    def test_batch_launch_uses_isolated_project_and_selected_platform(self):
        command = release.build_command("0.2", "WebGL", "/tmp/output with spaces", "/tmp/build.log")
        self.assertEqual(command[2], str(self.project))
        self.assertEqual(command[command.index("--target") + 1], "WebGL")
        self.assertEqual(command[command.index("--output-path") + 1], "/tmp/output with spaces")
        self.assertNotIn("command", command)
        self.assertIn("-releaseVersion 0.2", command[command.index("--args") + 1])
        self.assertNotIn("-version ", command[command.index("--args") + 1])

    def test_audio_import_failure_cannot_pass_even_when_unity_returns_zero(self):
        log = self.cache / "build.log"
        log.write_text("Errors during import of AudioClip Assets/Audio/wind.wav")
        with patch.object(release.subprocess, "run", return_value=subprocess.CompletedProcess([], 0)):
            with self.assertRaisesRegex(SystemExit, "Audio import failed"):
                release.build_in_batch("0.2", "WebGL", str(self.root / "output"), str(log))

    def test_missing_build_log_cannot_pass_even_when_unity_returns_zero(self):
        with patch.object(release.subprocess, "run", return_value=subprocess.CompletedProcess([], 0)):
            with self.assertRaisesRegex(SystemExit, "without creating its build log"):
                release.build_in_batch("0.2", "WebGL", str(self.root / "output"), str(self.cache / "missing.log"))

    def test_targeted_rebuild_preserves_other_downloads(self):
        existing = self.root / "0.2"
        (existing / "Linux").mkdir(parents=True)
        (existing / "Linux" / "game").write_text("linux")
        (existing / "CoalRun-0.2-Linux.zip").write_bytes(b"linux zip")
        (existing / "build.txt").write_text("Linux Succeeded old\nWebGL Succeeded old\n")
        staging = self.root / "staging"
        staging.mkdir()
        (staging / "build.txt").write_text("WebGL Succeeded new\n")
        release.preserve_other_targets(str(staging), str(existing), ["WebGL"])
        self.assertEqual((staging / "Linux" / "game").read_text(), "linux")
        self.assertEqual((staging / "CoalRun-0.2-Linux.zip").read_bytes(), b"linux zip")
        self.assertEqual((staging / "build.txt").read_text(), "WebGL Succeeded new\nLinux Succeeded old\n")

    def test_failed_build_preserves_existing_release(self):
        existing = self.root / "dist" / "0.2"
        existing.mkdir(parents=True)
        (existing / "old.zip").write_bytes(b"old release")
        with patch("sys.argv", ["release.py", "0.2", "--target", "WebGL"]), \
                patch.object(release, "preflight"), patch.object(release, "snapshot"), \
                patch.object(release.subprocess, "run", return_value=subprocess.CompletedProcess([], 1)):
            with self.assertRaisesRegex(SystemExit, "Build failed"):
                release.main()
        self.assertEqual((existing / "old.zip").read_bytes(), b"old release")

    def test_multiple_platforms_pack_and_keep_previous_release(self):
        existing = self.root / "dist" / "0.2"
        existing.mkdir(parents=True)
        (existing / "old.zip").write_bytes(b"previous")
        calls = []

        def build(version, target, output, log):
            calls.append(target)
            folder = Path(output) / target
            folder.mkdir()
            (folder / "game").write_text(target)
            debug = folder / "CoalRun_BurstDebugInformation_DoNotShip"
            debug.mkdir()
            (debug / "symbols").write_text("not for players")
            (Path(output) / "build.txt").write_text(f"{target} Succeeded 1 MB; 0 errors; 70 audio clips\n")

        with patch("sys.argv", ["release.py", "0.2", "--target", "Linux", "--target", "WebGL"]), \
                patch.object(release, "preflight"), patch.object(release, "snapshot"), \
                patch.object(release, "build_in_batch", side_effect=build):
            release.main()
        self.assertEqual(calls, ["Linux", "WebGL"])
        self.assertEqual(len((existing / "build.txt").read_text().splitlines()), 2)
        for target in calls:
            with zipfile.ZipFile(existing / f"CoalRun-0.2-{target}.zip") as archive:
                self.assertEqual(archive.namelist(), ["game"])
        backups = list(self.cache.glob("0.2-previous-*/0.2/old.zip"))
        self.assertEqual(len(backups), 1)
        self.assertEqual(backups[0].read_bytes(), b"previous")

    def test_concurrent_release_is_rejected_before_sync(self):
        with open(self.cache / "release.lock", "a") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            with patch("sys.argv", ["release.py", "0.2", "--prepare-only"]), \
                    patch.object(release, "preflight"), patch.object(release, "snapshot") as snapshot:
                with self.assertRaisesRegex(SystemExit, "Another release"):
                    release.main()
                snapshot.assert_not_called()

    def test_background_detaches_worker_without_publishing(self):
        with patch("sys.argv", ["release.py", "0.2", "--target", "WebGL", "--background"]), \
                patch.object(release, "preflight"), patch.object(release.subprocess, "Popen") as launch:
            release.main()
        command = launch.call_args.args[0]
        self.assertNotIn("--background", command)
        self.assertNotIn("--release", command)
        self.assertIn("WebGL", command)
        self.assertTrue(launch.call_args.kwargs["start_new_session"])

    def test_publish_failure_restores_previous_release(self):
        existing = self.root / "dist"
        existing.mkdir()
        (existing / "old.zip").write_text("old")
        staging = self.root / "staging"
        staging.mkdir()
        with patch.object(release.os, "rename", side_effect=OSError("publish failed")):
            with self.assertRaisesRegex(OSError, "publish failed"):
                release.publish_output(str(staging), str(existing))
        self.assertEqual((existing / "old.zip").read_text(), "old")


if __name__ == "__main__":
    unittest.main()
