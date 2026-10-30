#!/usr/bin/env python3
"""Gemeinsamer Aufrufvertrag für ausdrücklich teilnehmende SVN-Prozesse, siehe docs/SVN-Koordination.md."""

import argparse
import datetime
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import uuid

LOCK_NAME = "svnnotifier-operation-v1.lock"


class MetadataOnlyParser(argparse.ArgumentParser):
    """Auch ungültige Aufrufe dürfen keine möglicherweise geheimen Argumentwerte ausgeben."""

    def error(self, _message):
        self.print_usage(sys.stderr)
        self.exit(2, "Ungültiger Aufruf. --working-copy, --action und einen Befehl nach -- angeben.\n")


def roots_for(path):
    """Findet Arbeitskopiewurzeln ohne SVN-Prozess und ohne Öffnen der Arbeitskopiedatenbank."""
    target = Path(path).resolve(strict=True)
    directory = target if target.is_dir() else target.parent
    roots = [candidate for candidate in (directory, *directory.parents)
             if (candidate / ".svn" / "wc.db").is_file()]
    if not roots:
        raise ValueError("Keine SVN-Arbeitskopiewurzel gefunden")
    return roots


def diagnostic(root, action, phase, process_id=None, exit_code=None, reason=""):
    """Gibt nur Metadaten aus, niemals Argumente, Umgebungsvariablen oder SVN-Ausgaben."""
    print(json.dumps(dict(utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                          workingCopy=str(root), action=action, phase=phase,
                          processId=process_id, exitCode=exit_code, reason=reason)),
          file=sys.stderr, flush=True)


def main():
    """Hält eigene Reservierungen bis zum Prozessende; fremde und verwaiste Dateien bleiben bestehen."""
    parser = MetadataOnlyParser(description=__doc__)
    parser.add_argument("--working-copy", action="append", required=True)
    parser.add_argument("--action", choices=("status", "info", "update", "commit", "log", "other"), required=True)
    parser.add_argument("command", nargs=argparse.REMAINDER)
    options = parser.parse_args()
    command = options.command[1:] if options.command[:1] == ["--"] else options.command
    if not command:
        parser.error("Befehl nach -- fehlt")
    claims = []
    process = None
    root = ""
    exit_code = None
    previous_handlers = {}
    try:
        roots = {root for path in options.working_copy for root in roots_for(path)}
        for root in sorted(roots, key=lambda candidate: (len(candidate.parts), str(candidate).casefold())):
            lock_path = root / ".svn" / LOCK_NAME
            try:
                descriptor = os.open(lock_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
            except FileExistsError:
                diagnostic(root, options.action, "skip", reason="coordination-busy")
                return 75
            content = json.dumps(dict(protocol=1, token=uuid.uuid4().hex, ownerPid=os.getpid(),
                                      action=options.action, startedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat())).encode()
            claims.append((lock_path, content))
            with os.fdopen(descriptor, "wb") as stream:
                stream.write(content)
                stream.flush()
                os.fsync(stream.fileno())
        diagnostic(root, options.action, "reserve")

        def forward_signal(number, _frame):
            if process is not None and process.poll() is None:
                process.send_signal(number)

        for number in (signal.SIGINT, signal.SIGTERM):
            previous_handlers[number] = signal.signal(number, forward_signal)
        process = subprocess.Popen(command)
        diagnostic(root, options.action, "begin", process.pid)
        exit_code = process.wait()
        diagnostic(root, options.action, "end", process.pid, exit_code)
        return exit_code if exit_code >= 0 else 128 - exit_code
    except (OSError, ValueError) as error:
        diagnostic(root, options.action, "skip" if process is None else "error",
                   reason="coordination-or-process-error:" + type(error).__name__)
        return 74
    finally:
        # Auch nach Signalen oder Diagnosefehlern keine Freigabe vor dem tatsächlichen Prozessende.
        if process is not None:
            process.wait()
        for number, handler in previous_handlers.items():
            signal.signal(number, handler)
        for lock_path, content in reversed(claims):
            try:
                if lock_path.read_bytes() == content:
                    lock_path.unlink()
            except OSError:
                diagnostic(lock_path.parent.parent, options.action, "release-error", reason="claim-retained")


if __name__ == "__main__":
    sys.exit(main())
