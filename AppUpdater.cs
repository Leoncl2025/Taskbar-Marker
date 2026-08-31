using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace TaskbarMarker;

internal sealed class AppUpdater
{
    private const string RepositoryUrl = "https://github.com/Leoncl2025/Taskbar-Marker";

    private readonly UpdateManager _manager;
    private UpdateInfo? _availableUpdate;

    private AppUpdater()
    {
        var source = new GithubSource(RepositoryUrl, accessToken: null!, prerelease: false);
        _manager = new UpdateManager(source);
    }

    public string? AvailableVersion => _availableUpdate?.TargetFullRelease.Version.ToString();

    public static AppUpdater? CreateIfSupported()
    {
        try
        {
            var updater = new AppUpdater();
            return updater._manager.IsInstalled || updater._manager.IsPortable ? updater : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> CheckForUpdatesAsync()
    {
        _availableUpdate = await _manager.CheckForUpdatesAsync();
        return _availableUpdate is not null;
    }

    public async Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken)
    {
        UpdateInfo update = _availableUpdate
            ?? throw new InvalidOperationException("No update has been selected.");
        await _manager.DownloadUpdatesAsync(update, progress.Report, cancellationToken);
    }

    public void ApplyAndRestart()
    {
        UpdateInfo update = _availableUpdate
            ?? throw new InvalidOperationException("No update has been downloaded.");
        _manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
    }
}