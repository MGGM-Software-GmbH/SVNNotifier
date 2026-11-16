using System.ComponentModel;

namespace CHD.SVN_Notifier;

internal enum NotificationKind { IncomingChanges, AutoUpdateCompleted, ApplicationUpdateCheckFailed }

internal sealed record NotificationRequest(NotificationKind Kind, IReadOnlyList<string> Paths, string Title, string Text);

/// <summary>Steuert Repository-Prüfungen und SVN-Aufträge unabhängig vom verwendeten Fensterframework.</summary>
internal sealed class ApplicationCoordinator : IDisposable
{
    private readonly Action<Action> dispatch;
    private readonly List<SvnItem> forced = new();
    private SvnItem? checking;
    private CancellationTokenSource? checkCancellation;
    private int manualRequests;
    private Task manualCompletion = Task.CompletedTask;
    private Task polling = Task.CompletedTask;
    private bool stopping;
    private bool installation;
    private DateTime pausedUntil;

    internal BindingList<SvnItem> Folders { get; }
    internal Dictionary<string, string> Errors { get; } = new();
    internal bool Active { get; set; }
    internal bool Suspended { get; set; }
    internal bool IsBusy => !polling.IsCompleted || manualRequests > 0 || installation;
    internal event Action? Changed;
    internal event Action<NotificationRequest>? NotificationRequested;
    internal event Action<SvnFolderProcess>? UpdateLogRequested;
    internal event Action<string>? StatusChanged;

    /// <summary>Der Benutzerschalter unterdrückt nur neue Meldungen, nicht die zugrunde liegenden Aufträge.</summary>
    internal void Notify(NotificationRequest request)
    {
        if (Config.ShowNotifications) NotificationRequested?.Invoke(request);
    }

    internal ApplicationCoordinator(Action<Action> dispatch)
    {
        this.dispatch = dispatch;
        Folders = new BindingList<SvnItem>(Config.ReadSvnFolders().OrderBy(item => item.Path, WindowsPathComparer.Instance).ToList());
        ShortenPaths();
        SvnTools.ErrorAdded += OnError;
        SvnTools.RecoverReservations(Folders);
        if (Config.DoPauseAfterApplicationStartup)
            pausedUntil = DateTime.Now.AddSeconds(Config.PauseAfterApplicationStartupInterval);
    }

    /// <summary>Hält Modelländerungen nach Hintergrundaufrufen auf dem Oberflächenthread.</summary>
    private Task OnUiAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatch(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(exception); }
        });
        return completion.Task;
    }

    private void OnError(string path, string error) => dispatch(() =>
    {
        if (stopping || installation) return;
        path ??= "Checking for new version";
        Errors[path] = $"[{DateTime.Now}] {Environment.NewLine}{path}:{Environment.NewLine}{error.Replace("\n", Environment.NewLine)}{Environment.NewLine}{Environment.NewLine}";
        Changed?.Invoke();
    });

    internal void Save()
    {
        ShortenPaths();
        Config.SaveSvnFolders(Folders);
        Changed?.Invoke();
    }

    internal void Add(string path, PathType type)
    {
        if (stopping || installation) throw new InvalidOperationException(Loc.AppUpdate_RepositoryBusy);
        if (Folders.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))) return;
        var item = new SvnItem(path, type);
        int index = 0;
        while (index < Folders.Count && WindowsPathComparer.Instance.Compare(Folders[index].Path, item.Path) < 0) index++;
        Folders.Insert(index, item);
        Save();
    }

    /// <summary>Zieht das ausgewählte Repository vor und unterbricht dafür ausschließlich Leseprüfungen.</summary>
    internal void CheckNow(SvnItem item)
    {
        if (stopping || installation || !Folders.Contains(item)) return;
        if (checking == item && checkCancellation is { IsCancellationRequested: false }) return;
        RequestCheck(item);
        forced.Remove(item);
        forced.Insert(0, item);
        if (item.RetryReady) checkCancellation?.Cancel();
        _ = PollAsync();
    }

    /// <summary>Behält zurückgestellte Aufträge und fasst wiederholte Anforderungen desselben Eintrags zusammen.</summary>
    private void RequestCheck(SvnItem item)
    {
        if (Folders.Contains(item) && !forced.Contains(item)) forced.Add(item);
    }

    /// <summary>Startet die manuelle Aktion nach dem Ende eigener, nicht unterbrechbarer Vorgänge derselben Arbeitskopie.</summary>
    internal Task RunManualAsync(SvnItem item, Func<Task> action) => RunManualAsync(async () =>
    {
        await SvnTools.WaitForWorkingCopyAsync(item);
        if (!stopping && !installation) await action();
    });

    /// <summary>Gibt manuellen SVN-Aktionen Vorrang vor Leseprüfungen und neuen Auto-Updates, ohne Schreibprozesse zu beenden.</summary>
    internal async Task RunManualAsync(Func<Task> action)
    {
        if (stopping || installation) return;
        Task previous = manualCompletion;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manualCompletion = completion.Task;
        manualRequests++;
        checkCancellation?.Cancel();
        try
        {
            await previous;
            await polling;
            if (!stopping && !installation) await action();
        }
        finally
        {
            manualRequests--;
            completion.SetResult();
        }
    }

    internal void Resume()
    {
        Suspended = false;
        pausedUntil = Config.DoPauseAfterWindowsResume
            ? DateTime.Now.AddSeconds(Config.PauseAfterWindowsResumeInterval) : DateTime.MinValue;
    }

    internal Task PollAsync()
    {
        if (stopping || installation || Suspended || manualRequests > 0 || !polling.IsCompleted) return polling;
        if (DateTime.Now < pausedUntil && forced.Count == 0) return Task.CompletedTask;
        var requested = Folders.Where(item => DateTime.Now >= pausedUntil && item.Enabled && item.NextCheckTime(Active) <= DateTime.Now).ToList();
        polling = PollCoreAsync(requested);
        return polling;
    }

    /// <summary>Bewahrt Reservierungen, Wiederholzeiten und Erfolgsauslöser der bestehenden SVN-Strecke.</summary>
    private async Task PollCoreAsync(List<SvnItem> requested)
    {
        // Veröffentlicht den laufenden Auftrag vor möglichen erneuten Auslösern aus UI-Ereignissen.
        await Task.Yield();
        var incoming = new List<SvnItem>();
        try
        {
            // Check commit and update processes for finishing, auch bei entfernten oder deaktivierten Einträgen.
            foreach (var completed in SvnTools.TakeCompleted())
            {
                if (stopping || installation) { completed.folder._IsAutoUpdating = false; continue; }
                await OnUiAsync(() =>
                {
                    if (completed.isUpdateCommand && completed.updateError)
                    {
                        completed.folder._IsAutoUpdating = false;
                        UpdateLogRequested?.Invoke(completed);
                    }
                    RequestCheck(completed.folder);
                });
            }
            while (true)
            {
                SvnItem? item = null;
                CancellationTokenSource? cancellation = null;
                bool manual = false;
                DateTime began = default;
                await OnUiAsync(() =>
                {
                    if (stopping || installation || Suspended || manualRequests > 0) return;
                    forced.RemoveAll(candidate => !Folders.Contains(candidate));
                    item = forced.FirstOrDefault(candidate => candidate.RetryReady);
                    manual = item is not null;
                    item ??= requested.FirstOrDefault(candidate => Folders.Contains(candidate) && candidate.Enabled && candidate.RetryReady && candidate.NextCheckTime(Active) <= DateTime.Now);
                    if (item is null) return;
                    requested.Remove(item);
                    checking = item;
                    checkCancellation = cancellation = new CancellationTokenSource();
                    began = DateTime.Now;
                    StatusChanged?.Invoke(Loc.Status_Checking(item.Path));
                });
                if (item is null) break;
                bool autoUpdate = false;
                try
                {
                    SvnStatus? status = await Task.Run(() => SvnTools.GetSvnFolderStatus(item, cancellation!.Token));
                    await OnUiAsync(() =>
                    {
                        if (cancellation!.IsCancellationRequested)
                        {
                            // Die unterbrochene automatische Prüfung folgt nach den manuellen Aufträgen.
                            if (!manual && !requested.Contains(item)) requested.Insert(0, item);
                            return;
                        }
                        if (!status.HasValue) return;
                        forced.Remove(item);
                        if (stopping || installation || !Folders.Contains(item) || began < item.StatusTime) return;
                        bool changed = item.Status != status.Value;
                        item.Status = status.Value;
                        if (changed && NeedsUpdate(item)) incoming.Add(item);
                        else if (changed && item._IsAutoUpdating && status is SvnStatus.UpToDate or SvnStatus.UpToDate_Modified)
                        {
                            // If auto-update was in progress and is now complete, show toast.
                            item._IsAutoUpdating = false;
                            Notify(new(NotificationKind.AutoUpdateCompleted, new[] { item.Path },
                                Loc.Tooltip_AutoUpdate, Loc.Toast_AutoUpdateCompleted(item.VisiblePath)));
                        }
                        // Nach belegter Koordination auch bei unverändertem Änderungsstatus erneut versuchen.
                        autoUpdate = NeedsUpdate(item) && item.AutoUpdate && !item._IsAutoUpdating && item.RetryReady;
                        Changed?.Invoke();
                    });
                    if (autoUpdate && !cancellation!.IsCancellationRequested)
                    {
                        // Revisionsabfragen dürfen den UI-Thread und damit neue manuelle Aktionen nicht blockieren.
                        int revision = await Task.Run(() => SvnTools.GetRepositoryCommitedRevision(item, cancellation.Token));
                        await OnUiAsync(() =>
                        {
                            if (revision < 0 || cancellation.IsCancellationRequested || stopping || installation || Suspended || manualRequests > 0 ||
                                forced.Count > 0 || !Folders.Contains(item) || !item.AutoUpdate) return;
                            item.updateRevision = revision;
                            if (SvnTools.BeginPreparedUpdateSilently(item))
                            {
                                item.Status = SvnStatus.Unknown;
                                item._IsAutoUpdating = true;
                                Changed?.Invoke();
                            }
                        });
                    }
                }
                finally
                {
                    await OnUiAsync(() =>
                    {
                        checking = null;
                        checkCancellation = null;
                        cancellation!.Dispose();
                    });
                }
            }
            await OnUiAsync(() =>
            {
                if (stopping || installation) return;
                if (incoming.Count > 0)
                    Notify(new(NotificationKind.IncomingChanges, incoming.Select(item => item.Path).ToArray(),
                        Loc.Balloon_UpdateNeeded, string.Join(Environment.NewLine, incoming.Select(item => item.VisiblePath))));
            });
        }
        finally { await OnUiAsync(() => StatusChanged?.Invoke(string.Empty)); }
    }

    internal static bool NeedsUpdate(SvnItem item) => item.Status is SvnStatus.NeedUpdate or SvnStatus.NeedUpdate_Modified;

    internal Task UpdateAsync(SvnItem item, bool all = false) => RunManualAsync(item, () => UpdateCoreAsync(item, all));

    /// <summary>Führt ein bereits priorisiertes manuelles Update mit der vorhandenen Protokollvoraussetzung aus.</summary>
    private async Task UpdateCoreAsync(SvnItem item, bool all)
    {
        if (stopping || installation) return;
        if (Config.ChangeLogBeforeUpdate && !all)
        {
            int head = await Task.Run(() => SvnTools.GetRepositoryHeadRevision(item));
            if (head < 0) return;
            if (item.reviewedRevision < head)
                throw new InvalidOperationException(Config.Language == 1 ? "Bitte zuerst das Änderungsprotokoll ansehen." : "You need to see ChangeLog first!");
        }
        if (stopping || installation) return;
        StatusChanged?.Invoke(Loc.Status_Updating(item.Path));
        if (await Task.Run(() => SvnTools.Update(item, all)))
        {
            item.Status = SvnStatus.Unknown;
            RequestCheck(item);
            Changed?.Invoke();
        }
    }

    internal Task UpdateAllAsync() => RunManualAsync(UpdateAllCoreAsync);

    /// <summary>Behält den Vorrang der gesamten manuell angeforderten Aktualisierungsfolge.</summary>
    private async Task UpdateAllCoreAsync()
    {
        if (Config.ChangeLogBeforeUpdate || stopping || installation) return;
        foreach (var item in Folders.Where(NeedsUpdate).ToArray())
        {
            if (stopping || installation) break;
            await SvnTools.WaitForWorkingCopyAsync(item);
            if (stopping || installation) break;
            if (Config.UpdateAllSilently)
            {
                if (await Task.Run(() => SvnTools.BeginUpdateSilently(item))) item.Status = SvnStatus.Unknown;
            }
            else await UpdateCoreAsync(item, true);
        }
        Changed?.Invoke();
    }

    /// <summary>Gibt die Verarbeitung bei fehlgeschlagenem Installationsstart wieder frei.</summary>
    internal async Task PrepareInstallationAsync(Func<Task> startInstaller)
    {
        if (stopping || installation) throw new InvalidOperationException(Loc.AppUpdate_RepositoryBusy);
        installation = true;
        try
        {
            await SvnTools.PauseForApplicationUpdateAsync();
            await polling;
            await manualCompletion;
            foreach (var completed in SvnTools.TakeCompleted()) completed.folder._IsAutoUpdating = false;
            Config.SaveSettings();
            Config.SaveSvnFolders(Folders);
            await startInstaller();
        }
        catch
        {
            SvnTools.ResumeAfterApplicationUpdate();
            installation = false;
            throw;
        }
    }

    internal async Task StopAsync()
    {
        stopping = true;
        await SvnTools.StopAsync();
        await polling;
        await manualCompletion;
    }

    private void ShortenPaths()
    {
        int common = 0;
        if (Folders.Count > 1)
        {
            string first = Folders[0].Path;
            for (int index = 0; index < first.Length; index++)
            {
                if (Folders.Any(item => item.Path.Length <= index + 1 || item.Path[index] != first[index])) break;
                if (first[index] == '\\') common = index + 1;
            }
        }
        foreach (var item in Folders) item.VisiblePath = (common > 0 ? "…" : string.Empty) + item.Path[common..];
    }

    public void Dispose() => SvnTools.ErrorAdded -= OnError;
}
