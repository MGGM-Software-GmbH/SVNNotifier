using System.ComponentModel;

namespace CHD.SVN_Notifier;

internal enum NotificationKind { IncomingChanges, AutoUpdateCompleted, ApplicationUpdateCheckFailed }

internal sealed record NotificationRequest(NotificationKind Kind, IReadOnlyList<string> Paths, string Title, string Text);

/// <summary>Steuert Repository-Prüfungen und SVN-Aufträge unabhängig vom verwendeten Fensterframework.</summary>
internal sealed class ApplicationCoordinator : IDisposable
{
    private readonly Action<Action> dispatch;
    private readonly Queue<SvnItem> forced = new();
    private Task polling = Task.CompletedTask;
    private bool stopping;
    private bool installation;
    private DateTime pausedUntil;

    internal BindingList<SvnItem> Folders { get; }
    internal Dictionary<string, string> Errors { get; } = new();
    internal bool Active { get; set; }
    internal bool Suspended { get; set; }
    internal bool IsBusy => !polling.IsCompleted || installation;
    internal event Action? Changed;
    internal event Action<NotificationRequest>? NotificationRequested;
    internal event Action<SvnFolderProcess>? UpdateLogRequested;
    internal event Action<string>? StatusChanged;

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

    internal void CheckNow(SvnItem item)
    {
        if (stopping || installation) return;
        forced.Enqueue(item);
        _ = PollAsync();
    }

    internal void Resume()
    {
        Suspended = false;
        pausedUntil = Config.DoPauseAfterWindowsResume
            ? DateTime.Now.AddSeconds(Config.PauseAfterWindowsResumeInterval) : DateTime.MinValue;
    }

    internal Task PollAsync()
    {
        if (stopping || installation || Suspended || !polling.IsCompleted) return polling;
        if (DateTime.Now < pausedUntil && forced.Count == 0) return Task.CompletedTask;
        SvnItem[] snapshot = Folders.ToArray();
        var requested = new HashSet<SvnItem>();
        while (forced.TryDequeue(out var item)) if (Folders.Contains(item)) requested.Add(item);
        foreach (var item in snapshot)
            if (item.Enabled && item.NextCheckTime(Active) <= DateTime.Now) requested.Add(item);
        polling = PollCoreAsync(requested);
        return polling;
    }

    /// <summary>Bewahrt Reservierungen, Wiederholzeiten und Erfolgsauslöser der bestehenden SVN-Strecke.</summary>
    private async Task PollCoreAsync(IEnumerable<SvnItem> requested)
    {
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
                    if (Folders.Contains(completed.folder)) forced.Enqueue(completed.folder);
                });
            }
            foreach (var item in requested)
            {
                if (stopping || installation || Suspended) break;
                if (!item.RetryReady) continue;
                await OnUiAsync(() => StatusChanged?.Invoke(Loc.Status_Checking(item.Path)));
                DateTime began = DateTime.Now;
                SvnStatus? status = await Task.Run(() => SvnTools.GetSvnFolderStatus(item));
                if (!status.HasValue) continue;
                await OnUiAsync(() =>
                {
                    if (stopping || installation || !Folders.Contains(item) || began < item.StatusTime) return;
                    bool changed = item.Status != status.Value;
                    item.Status = status.Value;
                    if (changed && NeedsUpdate(item)) incoming.Add(item);
                    else if (changed && item._IsAutoUpdating && status is SvnStatus.UpToDate or SvnStatus.UpToDate_Modified)
                    {
                        // If auto-update was in progress and is now complete, show toast.
                        item._IsAutoUpdating = false;
                        NotificationRequested?.Invoke(new(NotificationKind.AutoUpdateCompleted, new[] { item.Path },
                            Loc.Tooltip_AutoUpdate, Loc.Toast_AutoUpdateCompleted(item.VisiblePath)));
                    }
                    // Nach belegter Koordination auch bei unverändertem Änderungsstatus erneut versuchen.
                    if (NeedsUpdate(item) && item.AutoUpdate && !item._IsAutoUpdating && item.RetryReady && SvnTools.BeginUpdateSilently(item))
                    {
                        item.Status = SvnStatus.Unknown;
                        item._IsAutoUpdating = true;
                    }
                    Changed?.Invoke();
                });
            }
            await OnUiAsync(() =>
            {
                if (stopping || installation) return;
                if (incoming.Count > 0)
                    NotificationRequested?.Invoke(new(NotificationKind.IncomingChanges, incoming.Select(item => item.Path).ToArray(),
                        Loc.Balloon_UpdateNeeded, string.Join(Environment.NewLine, incoming.Select(item => item.VisiblePath))));
            });
        }
        finally { await OnUiAsync(() => StatusChanged?.Invoke(string.Empty)); }
    }

    internal static bool NeedsUpdate(SvnItem item) => item.Status is SvnStatus.NeedUpdate or SvnStatus.NeedUpdate_Modified;

    internal async Task UpdateAsync(SvnItem item, bool all = false)
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
            forced.Enqueue(item);
            Changed?.Invoke();
        }
    }

    internal async Task UpdateAllAsync()
    {
        if (Config.ChangeLogBeforeUpdate || stopping || installation) return;
        foreach (var item in Folders.Where(NeedsUpdate).ToArray())
        {
            if (stopping || installation) break;
            if (Config.UpdateAllSilently)
            {
                if (SvnTools.BeginUpdateSilently(item)) item.Status = SvnStatus.Unknown;
            }
            else await UpdateAsync(item, true);
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
