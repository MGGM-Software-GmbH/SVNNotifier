"""Protokolltests mit isolierten Arbeitskopien und optional Windows-/Container-Gegenstelle."""

import argparse
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import unittest

OPTIONS = None


class CoordinationTests(unittest.TestCase):
    """Verwendet ausschließlich neu angelegte Dateien und lokale Test-Repositories."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="protocol-", dir=OPTIONS.shared_directory)
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.wc = self.root / "working"
        (self.wc / ".svn").mkdir(parents=True)
        (self.wc / ".svn" / "wc.db").write_text("synthetische Testmarkierung")
        (self.wc / "first").mkdir()
        (self.wc / "second").mkdir()
        self.claim = self.wc / ".svn" / "svnnotifier-operation-v1.lock"

    def command(self, path, *child):
        return [sys.executable, OPTIONS.script, "--working-copy", str(path), "--action", "status", "--", *child]

    def run_command(self, path, *child):
        return subprocess.run(self.command(path, *child), capture_output=True, text=True, timeout=15)

    def holder(self, path):
        process = subprocess.Popen(self.command(path, sys.executable, "-c", "import time; time.sleep(1)"), stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        self.addCleanup(lambda: process.communicate(timeout=10))
        while True:
            line = process.stderr.readline()
            self.assertTrue(line)
            if json.loads(line)["phase"] == "begin":
                return process

    def test_subfolders_are_exclusive(self):
        holder = self.holder(self.wc / "first")
        result = self.run_command(self.wc / "second", sys.executable, "-c", "raise SystemExit(0)")
        self.assertEqual(result.returncode, 75)
        self.assertIn("coordination-busy", result.stderr)
        holder.wait(timeout=10)
        self.assertFalse(self.claim.exists())
        self.assertEqual(self.run_command(self.wc, sys.executable, "-c", "pass").returncode, 0)

    def test_foreign_claim_is_preserved(self):
        self.claim.write_text("foreign")
        self.assertEqual(self.run_command(self.wc, sys.executable, "-c", "pass").returncode, 75)
        self.assertEqual(self.claim.read_text(), "foreign")

    def test_process_failure_releases_claim(self):
        self.assertEqual(self.run_command(self.wc, sys.executable, "-c", "raise SystemExit(7)").returncode, 7)
        self.assertFalse(self.claim.exists())

    def test_changed_claim_is_preserved(self):
        holder = self.holder(self.wc)
        self.claim.write_text("changed-owner")
        holder.wait(timeout=10)
        self.assertEqual(self.claim.read_text(), "changed-owner")

    def test_start_failure_releases_claim(self):
        self.assertEqual(self.run_command(self.wc, str(self.root / "missing")).returncode, 74)
        self.assertFalse(self.claim.exists())

    def test_diagnostics_exclude_arguments(self):
        result = self.run_command(self.wc, sys.executable, "-c", "pass", "--password=diagnostic-test-sentinel")
        self.assertEqual(result.returncode, 0)
        self.assertNotIn("diagnostic-test-sentinel", result.stderr)
        events = [json.loads(line) for line in result.stderr.splitlines()]
        self.assertEqual([event["phase"] for event in events], ["reserve", "begin", "end"])
        self.assertGreater(events[1]["processId"], 0)
        self.assertEqual(events[-1]["exitCode"], 0)

    def test_invalid_arguments_are_not_logged(self):
        result = subprocess.run([sys.executable, OPTIONS.script, "--working-copy", str(self.wc), "--action", "parser-secret-sentinel"], capture_output=True, text=True)
        self.assertEqual(result.returncode, 2)
        self.assertNotIn("parser-secret-sentinel", result.stderr)

    def test_signal_waits_for_child_and_releases(self):
        holder = self.holder(self.wc)
        holder.send_signal(signal.SIGTERM)
        holder.wait(timeout=10)
        self.assertFalse(self.claim.exists())

    def test_parent_crash_keeps_fail_closed_claim(self):
        holder = self.holder(self.wc)
        holder.kill()
        holder.wait(timeout=10)
        self.assertTrue(self.claim.exists())
        self.assertEqual(self.run_command(self.wc, sys.executable, "-c", "pass").returncode, 75)
        time.sleep(1.1)  # Eigenes Testkind ist vor dem Entfernen des eigenen Testverzeichnisses beendet.

    def test_local_svn_repository(self):
        repository = self.root / "repository"
        working = self.root / "real-working"
        subprocess.run(["svnadmin", "create", str(repository)], check=True, capture_output=True)
        subprocess.run(["svn", "checkout", repository.as_uri(), str(working)], check=True, capture_output=True)
        fixture = working / "fixture.txt"
        fixture.write_text("isolierte Testdatei")
        for arguments in (["add", str(fixture)], ["commit", str(working), "-m", "Isolierte Testdaten"], ["update", str(working)], ["status", "-u", "--xml", str(working)]):
            result = self.run_command(working, "svn", *arguments)
            self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("<status>", result.stdout)

    @unittest.skipUnless(os.name != "nt", "Plattformübergreifender Test wird von Linux aus gesteuert")
    def test_windows_container_mutual_exclusion(self):
        if not OPTIONS.windows_driver or not OPTIONS.container:
            self.skipTest("Windows-Gegenstelle oder Testcontainer fehlt")
        windows_path = subprocess.check_output(["wslpath", "-w", str(self.wc)], text=True).strip()
        container_path = "/shared/" + str(self.wc.relative_to(Path(OPTIONS.shared_directory).resolve()))
        windows = subprocess.Popen([OPTIONS.windows_driver, "--hold", windows_path, "1500"], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        self.addCleanup(lambda: windows.communicate(timeout=10))
        self.assertEqual(windows.stdout.readline().strip(), "acquired")
        blocked = subprocess.run(["docker", "exec", OPTIONS.container, "python3", "/tools/svn-coordinated.py", "--working-copy", container_path + "/second", "--action", "status", "--", "true"], capture_output=True, text=True, timeout=10)
        self.assertEqual(blocked.returncode, 75, blocked.stderr)
        windows.wait(timeout=10)
        container = subprocess.Popen(["docker", "exec", OPTIONS.container, "python3", "/tools/svn-coordinated.py", "--working-copy", container_path + "/first", "--action", "status", "--", "sleep", "1.5"], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        self.addCleanup(lambda: container.communicate(timeout=10))
        while True:
            line = container.stderr.readline()
            self.assertTrue(line)
            if json.loads(line)["phase"] == "begin":
                break
        blocked = subprocess.run([OPTIONS.windows_driver, "--hold", windows_path + "\\second", "100"], capture_output=True, timeout=10)
        self.assertEqual(blocked.returncode, 75)
        container.wait(timeout=10)
        self.assertFalse(self.claim.exists())
        self.assertEqual(subprocess.run([OPTIONS.windows_driver, "--hold", windows_path, "10"], capture_output=True, timeout=10).returncode, 0)

    def test_windows_container_simultaneous_starts(self):
        if not OPTIONS.windows_driver or not OPTIONS.container:
            self.skipTest("Windows-Gegenstelle oder Testcontainer fehlt")
        windows_path = subprocess.check_output(["wslpath", "-w", str(self.wc)], text=True).strip()
        container_path = "/shared/" + str(self.wc.relative_to(Path(OPTIONS.shared_directory).resolve()))
        barrier = self.wc / "start-barrier"
        launcher = "import os,sys,time; print('ready',flush=True); " \
                   "exec('while not os.path.exists(sys.argv[1]): time.sleep(.001)'); " \
                   "os.execv('/usr/bin/python3',['python3','/tools/svn-coordinated.py','--working-copy',sys.argv[2],'--action','status','--','sleep','.7'])"
        for _ in range(10):
            with subprocess.Popen([OPTIONS.windows_driver, "--hold", windows_path, "700", windows_path + "\\start-barrier"], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True) as windows, \
                 subprocess.Popen(["docker", "exec", OPTIONS.container, "python3", "-c", launcher, container_path + "/start-barrier", container_path], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True) as container:
                self.assertEqual(windows.stdout.readline().strip(), "ready")
                self.assertEqual(container.stdout.readline().strip(), "ready")
                barrier.write_text("start")
                windows.communicate(timeout=10)
                container.communicate(timeout=10)
                self.assertEqual(sorted([windows.returncode, container.returncode]), [0, 75])
            barrier.unlink()
            self.assertFalse(self.claim.exists())


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--script", default=str(Path(__file__).resolve().parents[2] / "tools" / "svn-coordinated.py"))
    parser.add_argument("--shared-directory", required=True)
    parser.add_argument("--windows-driver")
    parser.add_argument("--container")
    OPTIONS, remaining = parser.parse_known_args()
    OPTIONS.shared_directory = str(Path(OPTIONS.shared_directory).resolve())
    Path(OPTIONS.shared_directory).mkdir(parents=True, exist_ok=True)
    unittest.main(argv=[sys.argv[0], *remaining])
