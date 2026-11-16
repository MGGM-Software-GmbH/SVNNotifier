//
// SVN Notifier
// Copyright 2007 SIA Computer Hardware Design (www.chd.lv)
//
// This file is part of SVN Notifier.
//
// SVN Notifier is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 3 of the License, or
// (at your option) any later version.
//
// SVN Notifier is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>
//

using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CHD.SVN_Notifier
{
    public class SvnFolderProcess
    {
        public readonly SvnItem folder;
        public readonly Process process;
        public readonly bool isUpdateCommand;
        public ArrayList processOutput = new ArrayList();
        public bool updateError;
        internal Task<SvnProcessResult>? Completion;
        internal bool IsInteractive;

        public SvnFolderProcess(SvnItem f, Process p, bool isUpdateCommand)
        {
            folder = f;
            process = p;
            updateError = false;
            this.isUpdateCommand = isUpdateCommand;
        }
    }

    public delegate void SvnErrorAddedHandler(string path, string error);

    public class SvnTools
    {
        public static event SvnErrorAddedHandler? ErrorAdded;
        private static readonly SvnOperationRunner runner = new(message => Config.WriteLog("SvnOperation", message), rejected: (root, reason) =>
        {
            if (reason.StartsWith("coordination-error:", StringComparison.Ordinal))
                ErrorAdded?.Invoke(root, Loc.Svn_CoordinationError(reason));
        });
        public static readonly ArrayList svnFolderProcesses = ArrayList.Synchronized(new ArrayList());
        internal static bool HasActiveOperations => runner.HasActiveOperations;

        /// <summary>Wartet auf eigene Schreibvorgänge und Dialoge derselben Arbeitskopie, ohne sie für einen manuellen Auftrag zu beenden.</summary>
        internal static Task WaitForWorkingCopyAsync(SvnItem folder)
        {
            var roots = WorkingCopyLease.FindRoots(folder.Path);
            lock (svnFolderProcesses.SyncRoot)
                return Task.WhenAll(svnFolderProcesses.Cast<SvnFolderProcess>()
                    .Where(process => process.Completion is { IsCompleted: false } &&
                        WorkingCopyLease.FindRoots(process.folder.Path).Intersect(roots, StringComparer.OrdinalIgnoreCase).Any())
                    .Select(process => process.Completion!));
        }

        /// <summary>Hält Pfadänderungen vom Ziel eines bereits gestarteten Schreibvorgangs oder Dialogs fern.</summary>
        internal static bool HasRunningWrite(SvnItem folder)
        {
            lock (svnFolderProcesses.SyncRoot)
                return svnFolderProcesses.Cast<SvnFolderProcess>().Any(process => ReferenceEquals(process.folder, folder) && process.Completion is { IsCompleted: false });
        }

        /// <summary>Wartet vor einem Konfigurationswechsel auf sämtliche bereits gestarteten Schreibvorgänge und Dialoge.</summary>
        internal static Task WaitForTrackedOperationsAsync()
        {
            lock (svnFolderProcesses.SyncRoot)
                return Task.WhenAll(svnFolderProcesses.Cast<SvnFolderProcess>().Where(process => process.Completion is not null).Select(process => process.Completion!));
        }

        /// <summary>Bereinigt beim Start nur nachweislich verwaiste Notifier-Reservierungen konfigurierter Arbeitskopien.</summary>
        internal static void RecoverReservations(IEnumerable<SvnItem> folders) => runner.RecoverReservations(folders.Select(folder => folder.Path));

        // TODO: Optimize speed; join GetRepositoryHeadRevision and GetRepositoryCommitedRevision functions into one:
        // void int GetRepositoryRevisions (string path, out int headRevision, out int committedRevision)
        private static int GetRepositoryRevision(SvnItem folder, string revision, CancellationToken cancellation = default)
        {
            if (cancellation.IsCancellationRequested) return -1;
            var arguments = new[] { "info", "--non-interactive", "--xml" };
            if (!string.IsNullOrEmpty(revision)) arguments = arguments.Concat(new[] { "-r", revision }).ToArray();
            arguments = arguments.Concat(new[] { "--", CommandLineTarget(folder.Path) }).ToArray();
            var completion = runner.TryStart(folder.Path, "info", Config.SVNpath, arguments, true, Config.SvnCommandTimeout, cancellation);
            if (completion is null) { folder.DeferOperation(); return -1; }
            var result = completion.GetAwaiter().GetResult();
            if (cancellation.IsCancellationRequested) return -1;
            if (!result.Success) { ReportFailure(folder, result); return -1; }
            try
            {
                var entry = XDocument.Parse(result.Output).Root?.Element("entry");
                folder.RepositoryUrl = entry?.Element("url")?.Value;
                return int.Parse(entry?.Attribute("revision")?.Value ?? "-1");
            }
            catch { folder.DeferOperation(); ErrorAdded?.Invoke(folder.Path, Loc.Svn_InvalidInfo); return -1; }
        }

        public static int GetRepositoryHeadRevision(SvnItem folder) => GetRepositoryRevision(folder, "HEAD");
        public static int GetRepositoryCommitedRevision(SvnItem folder, CancellationToken cancellation = default) => GetRepositoryRevision(folder, "COMMITTED", cancellation);

        public static void OpenChangeLogWindow(SvnItem folder, bool updateRevisions)
        {
            if (updateRevisions)
            {
                int head = GetRepositoryHeadRevision(folder);
                int committed = GetRepositoryCommitedRevision(folder);
                if (head < 0 || committed < 0) return;
                folder.reviewedRevision = head;
                folder.updateRevision = committed;
            }
            StartTracked(folder, "log", new[] { "/command:log", "/path:" + folder.Path, "/revend:" + folder.updateRevision }, false, false);
        }

        public static void OpenLogWindow(string path) =>
            StartTracked(new SvnItem(path, PathType.Directory), "log", new[] { "/command:log", "/path:" + path }, false, false);

        /// <summary>
        /// This method waits until updating will finish
        /// </summary>
        public static bool Update(SvnItem folder, bool updateAll)
        {
            var arguments = new[] { "/command:update", "/path:" + folder.Path, "/notempfile", "/closeonend:" + Config.UpdateWindowAction };
            if (Config.ChangeLogBeforeUpdate && !updateAll)
                arguments = arguments.Concat(new[] { "/rev:" + folder.reviewedRevision }).ToArray();
            var completion = runner.TryStart(folder.Path, "update-dialog", Config.TortoiseSVNpath, arguments, false, captureOutput: false, tortoiseSyntax: true);
            if (completion is null) { folder.DeferOperation(); return false; }
            var result = completion.GetAwaiter().GetResult();
            if (!result.Success) ReportFailure(folder, result);
            return result.Success;
        }

        /// <summary>Startet nur nach erfolgreicher Reservierung der gesamten Arbeitskopie.</summary>
        public static bool BeginUpdateSilently(SvnItem folder)
        {
            int committed = GetRepositoryCommitedRevision(folder);
            if (committed < 0) return false;
            folder.updateRevision = committed;
            return BeginPreparedUpdateSilently(folder);
        }

        /// <summary>Startet das stille Update nach der getrennten, unterbrechbaren Revisionsprüfung.</summary>
        internal static bool BeginPreparedUpdateSilently(SvnItem folder)
        {
            return StartTracked(folder, "update", new[] { "update", "--non-interactive", "--", CommandLineTarget(folder.Path) }, true, true);
        }

        private static bool StartTracked(SvnItem folder, string action, string[] arguments, bool update, bool commandLine)
        {
            // Skip this folder if update or commit is in progress. Die Reservierung gilt auch für andere Unterordner.
            var completion = runner.TryStart(folder.Path, action, commandLine ? Config.SVNpath : Config.TortoiseSVNpath, arguments, false, captureOutput: commandLine, tortoiseSyntax: !commandLine);
            if (completion is null) { folder.DeferOperation(); return false; }
            lock (svnFolderProcesses.SyncRoot)
                svnFolderProcesses.Add(new SvnFolderProcess(folder, new Process(), update) { Completion = completion, IsInteractive = !commandLine });
            return true;
        }

        /// <summary>Liest nur abgeschlossene Ergebnisse. Laufende Prozesse werden ohne blockierende Stream-Lesezugriffe überwacht.</summary>
        public static void ReadProcessOutput(SvnFolderProcess process)
        {
            if (process.Completion is null || !process.Completion.IsCompleted) return;
            var result = process.Completion.GetAwaiter().GetResult();
            process.processOutput.Clear();
            foreach (string line in (result.Output + "\n" + result.Error).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                process.processOutput.Add(line.TrimEnd('\r'));
            // TortoiseSVN liefert beim Abbrechen -1 und zeigt fachliche Fehler selbst an. Der Rückgabecode allein ist keine zusätzliche Diagnose.
            bool handledByDialog = process.IsInteractive && result.ExitCode == -1 && !result.Cancelled && string.IsNullOrWhiteSpace(result.Error);
            process.updateError = (!result.Success && !handledByDialog) || !string.IsNullOrWhiteSpace(result.Error)
                || result.Output.Split('\n').Any(line => line.StartsWith("C ") || line.StartsWith("Skipped "));
            if (process.updateError && process.processOutput.Count == 0)
                process.processOutput.Add(result.Cancelled ? Loc.Svn_Cancelled : Loc.Svn_ProcessError(result.ExitCode));
            if (process.updateError) process.folder.DeferOperation();
        }

        /// <summary>Entnimmt beendete Prozesse atomar, unabhängig davon, ob ihr Eintrag noch überwacht wird.</summary>
        internal static SvnFolderProcess[] TakeCompleted()
        {
            lock (svnFolderProcesses.SyncRoot)
            {
                var completed = svnFolderProcesses.Cast<SvnFolderProcess>().Where(item => item.Completion?.IsCompleted == true).ToArray();
                foreach (var item in completed)
                {
                    ReadProcessOutput(item);
                    svnFolderProcesses.Remove(item);
                    item.process.Dispose();
                }
                return completed;
            }
        }

        public static bool Commit(SvnItem folder) =>
            StartTracked(folder, "commit-dialog", new[] { "/command:commit", "/path:" + folder.Path, "/notempfile" }, false, false);

        /// <summary>Öffnet die TortoiseSVN-Cleanup-Auswahl und reserviert die Arbeitskopie bis zum Prozessende.</summary>
        public static bool Cleanup(SvnItem folder) =>
            StartTracked(folder, "cleanup-dialog", new[] { "/command:cleanup", "/path:" + folder.Path }, false, false);

        /// <summary>Ein belegter Koordinationspfad oder eine vorgezogene manuelle Prüfung erhält den bisherigen Status.</summary>
        public static SvnStatus? GetSvnFolderStatus(SvnItem folder, CancellationToken cancellation = default)
        {
            if (cancellation.IsCancellationRequested) return null;
            string target;
            try { target = CommandLineTarget(folder.Path); }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or System.Security.SecurityException)
            {
                folder.DeferOperation();
                ErrorAdded?.Invoke(folder.Path, Loc.Svn_CoordinationError("coordination-error:" + exception.GetType().Name));
                return SvnStatus.Error;
            }
            string? rejection = null;
            var completion = runner.TryStart(folder.Path, "status", Config.SVNpath,
                new[] { "status", "-u", "--non-interactive", "--xml", "--", target }, true, Config.SvnCommandTimeout, cancellation,
                onRejected: reason => rejection = reason);
            if (completion is null)
            {
                folder.DeferOperation();
                // Belegung bleibt wiederholbar; ein dauerhaft ungültiges Ziel beendet dagegen den manuellen Prüfauftrag.
                return rejection?.StartsWith("coordination-error:", StringComparison.Ordinal) == true ? SvnStatus.Error : null;
            }
            var result = completion.GetAwaiter().GetResult();
            if (cancellation.IsCancellationRequested) return null;
            if (!result.Success) { ReportFailure(folder, result); return SvnStatus.Error; }
            if (folder.RepositoryUrl is null) GetRepositoryRevision(folder, "", cancellation); // Fill folder.RepositoryUrl
            if (cancellation.IsCancellationRequested) return null;
            try
            {
                // Because SVN may return non-valid XML in some cases?
                //http://svn.collab.net/repos/svn/trunk/subversion/svn/status.c
                //http://blog.wolfman.com/articles/category/svn
                // Der vorhandene XML-Parser verwendet statischen Zustand; auch unterschiedliche Arbeitskopien dürfen ihn nicht mischen.
                lock (typeof(SvnXml))
                {
                    SvnXml.Create(result.Output);
                    SvnXml.ParseXmlForStatus();
                    if (!SvnXml.ContainsKey("revision"))
                    {
                        folder.DeferOperation();
                        ErrorAdded?.Invoke(folder.Path, "Folder not found in repository");
                        return SvnStatus.Error;
                    }
                    if (SvnXml.ContainsKey("NeedUpdate"))
                        return SvnXml.ContainsKey("Modified") ? SvnStatus.NeedUpdate_Modified : SvnStatus.NeedUpdate;
                    return SvnXml.ContainsKey("Modified") ? SvnStatus.UpToDate_Modified : SvnStatus.UpToDate;
                }
            }
            catch { folder.DeferOperation(); return SvnStatus.Error; }
        }

        /// <summary>SVN erhält denselben lokalen Pfad wie die Reservierung, ohne Options- oder Peg-Revisionsauswertung des Dateinamens.</summary>
        private static string CommandLineTarget(string path) => Path.GetFullPath(path) + "@";

        /// <summary>Die Fehleranzeige erhält keine ungefilterten SVN-Ausgaben mit möglicherweise enthaltenen Zugangsdaten.</summary>
        private static void ReportFailure(SvnItem folder, SvnProcessResult result)
        {
            folder.DeferOperation();
            ErrorAdded?.Invoke(folder.Path, result.TimedOut ? Loc.Svn_TimedOut(result.TimeoutSeconds) :
                result.Cancelled ? Loc.Svn_Cancelled : Loc.Svn_ProcessError(result.ExitCode));
        }

        /// <summary>Stoppt neue Aufträge und wartet auf die Freigabe aller bereits angenommenen Vorgänge.</summary>
        internal static Task StopAsync() => runner.StopAsync();

        /// <summary>Beendet vor dem Programmupdate alle eigenen SVN-Vorgänge und verhindert weitere Starts.</summary>
        internal static Task PauseForApplicationUpdateAsync() => runner.PauseForApplicationUpdateAsync();

        /// <summary>Gibt die SVN-Verarbeitung nach einem fehlgeschlagenen Programmupdate wieder frei.</summary>
        internal static void ResumeAfterApplicationUpdate() => runner.ResumeAfterApplicationUpdate();

        public static void KillBackgroundProcess() => _ = StopAsync();
    }
}
