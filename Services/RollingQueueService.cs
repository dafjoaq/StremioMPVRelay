using System.Text.RegularExpressions;
using StremioMPVRelay.Models;

namespace StremioMPVRelay.Services;

public sealed class RollingQueueService : IAsyncDisposable
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(7),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30)
    ];

    private readonly StremioAddonService _addon;
    private readonly StreamSelector _selector;
    private readonly MpvService _mpv;

    private readonly SemaphoreSlim _maintenanceLock = new(1, 1);
    private readonly object _stateLock = new();

    private readonly List<RollingQueueEntry> _queue = [];

    private readonly Dictionary<long, int> _entryIdToEpisode = [];

    private CancellationTokenSource? _sessionCts;
    private CancellationTokenSource? _resolutionCts;
    private CancellationTokenSource? _retryDelayCts;

    private AppSettings? _settings;
    private PendingRecovery? _pendingRecovery;

    private bool _disposed;
    private bool _clearingPlaylist;

    public bool IsActive { get; private set; }

    public string ManifestUrl { get; private set; } = string.Empty;

    public string ImdbId { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public int Season { get; private set; }

    public int CurrentEpisode { get; private set; }

    public int NextEpisode { get; private set; }

    public int LastEpisode { get; private set; }

    public int CurrentPlaylistIndex { get; private set; } = -1;

    public int Added { get; private set; }

    public int Failed { get; private set; }

    public int RetryAttempt { get; private set; }

    public DateTimeOffset? NextRetryAt { get; private set; }

    public string LastError { get; private set; } = string.Empty;

    public string PreferredBingeGroup { get; private set; } =
        string.Empty;

    public IReadOnlyList<RollingQueueEntry> Queue
    {
        get
        {
            lock (_stateLock)
            {
                return _queue.ToArray();
            }
        }
    }

    public event EventHandler<RollingQueueStatusEventArgs>? StatusChanged;

    public event EventHandler<RollingQueueLogEventArgs>? LogAdded;

    public event EventHandler<RollingQueueEntryEventArgs>? EpisodeAdded;

    public event EventHandler<RollingQueueEntryEventArgs>? EpisodeChanged;

    public event EventHandler<RollingQueueEntryEventArgs>? EpisodeRecovered;

    public event EventHandler<RollingQueueFinishedEventArgs>? QueueFinished;

    public RollingQueueService(
        StremioAddonService addon,
        StreamSelector selector,
        MpvService mpv)
    {
        _addon = addon;
        _selector = selector;
        _mpv = mpv;

        _mpv.PropertyChanged += OnMpvPropertyChanged;
        _mpv.StartFile += OnMpvStartFile;
        _mpv.FileLoaded += OnMpvFileLoaded;
        _mpv.PlaybackRestart += OnMpvPlaybackRestart;
        _mpv.EndFile += OnMpvEndFile;
        _mpv.Shutdown += OnMpvShutdown;
        _mpv.Disconnected += OnMpvDisconnected;
    }

    public async Task StartAsync(
        string manifestUrl,
        string imdbId,
        string title,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        ArgumentNullException.ThrowIfNull(settings);

        if (!_mpv.IsConnected)
            throw new InvalidOperationException("MPV is not connected.");

        if (settings.Season < 1)
            throw new ArgumentOutOfRangeException(nameof(settings.Season));

        if (settings.FirstEpisode < 1)
            throw new ArgumentOutOfRangeException(nameof(settings.FirstEpisode));

        if (settings.LastEpisode < settings.FirstEpisode)
        {
            throw new ArgumentException(
                "Last episode cannot be before first episode.");
        }

        string normalizedManifest =
            StremioAddonService.NormalizeManifestUrl(
                manifestUrl);

        if (string.IsNullOrWhiteSpace(imdbId))
        {
            throw new ArgumentException(
                "IMDb ID cannot be empty.",
                nameof(imdbId));
        }

        await StopInternalAsync();

        await _maintenanceLock.WaitAsync(cancellationToken);

        try
        {
            ResetQueueState();

            _settings = settings;

            ManifestUrl = normalizedManifest;
            ImdbId = imdbId.Trim();

            Title = string.IsNullOrWhiteSpace(title)
                ? ImdbId
                : title.Trim();

            Season = settings.Season;

            CurrentEpisode = settings.FirstEpisode;
            NextEpisode = settings.FirstEpisode;
            LastEpisode = settings.LastEpisode;

            _sessionCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            _clearingPlaylist = true;

            try
            {
                await _mpv.ClearPlaylistAsync(cancellationToken);
            }
            finally
            {
                _clearingPlaylist = false;
            }

            IsActive = true;
        }
        finally
        {
            _maintenanceLock.Release();
        }

        WriteLog(
            "Started " +
            Title +
            " S" +
            Season +
            " E" +
            settings.FirstEpisode +
            "-" +
            settings.LastEpisode +
            ".");

        SetStatus("Rolling queue started.");

        ScheduleMaintenance();
    }

    public async Task StopAsync()
    {
        ThrowIfDisposed();

        await StopInternalAsync();

        SetStatus("Rolling queue stopped.");
    }

    public Task RetryNowAsync()
    {
        ThrowIfDisposed();

        if (!IsActive)
            return Task.CompletedTask;

        NextRetryAt = DateTimeOffset.Now;

        _retryDelayCts?.Cancel();

        WriteLog("Immediate stream retry requested.");

        return Task.CompletedTask;
    }

    private void ScheduleMaintenance()
    {
        if (!IsActive)
            return;

        _ = MaintainQueueSafelyAsync();
    }

    private async Task MaintainQueueSafelyAsync()
    {
        try
        {
            await MaintainQueueAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LastError = ex.Message;

            WriteLog(
                "Rolling queue error: " +
                ex.Message);

            SetStatus(
                "Rolling queue error: " +
                ex.Message);
        }
    }

    private async Task MaintainQueueAsync()
    {
        if (!IsActive ||
            _settings is null ||
            _sessionCts is null)
        {
            return;
        }

        await _maintenanceLock.WaitAsync(
            _sessionCts.Token);

        try
        {
            while (IsActive &&
                   !_sessionCts.IsCancellationRequested)
            {
                PendingRecovery? recovery =
                    TakePendingRecovery();

                if (recovery is not null)
                {
                    await RecoverEpisodeAsync(
                        recovery,
                        _sessionCts.Token);

                    continue;
                }

                if (NextEpisode > LastEpisode)
                {
                    SetStatus(
                        "All requested episodes are queued.");

                    return;
                }

                int episodesAhead =
                    GetEpisodesAhead();

                int targetAhead =
                    Math.Max(
                        0,
                        _settings.BufferAhead);

                if (_queue.Count > 0 &&
                    episodesAhead >= targetAhead)
                {
                    SetStatus(
                        "Rolling queue active: " +
                        episodesAhead +
                        " episode(s) ahead.");

                    return;
                }

                int episode = NextEpisode;

                bool success =
                    await ResolveAndAppendAsync(
                        episode,
                        _sessionCts.Token);

                if (!success)
                    continue;
            }
        }
        finally
        {
            _maintenanceLock.Release();
        }
    }

    private async Task<bool> ResolveAndAppendAsync(
        int episode,
        CancellationToken sessionToken)
    {
        using var resolutionCts =
            CancellationTokenSource.CreateLinkedTokenSource(
                sessionToken);

        _resolutionCts = resolutionCts;

        try
        {
            StreamSelection? selection =
                await ResolveStreamAsync(
                    episode,
                    Array.Empty<string>(),
                    resolutionCts.Token);

            if (selection is null)
                return false;

            if (HasPendingRecovery())
                return false;

            StreamInfo stream =
                selection.Stream;

            string mediaTitle =
                BuildMediaTitle(episode);

            await _mpv.LoadFileAsync(
                stream.Url,
                mediaTitle,
                playImmediately: true,
                resolutionCts.Token);

            CommitEpisode(
                episode,
                selection);

            return true;
        }
        catch (OperationCanceledException)
        {
            if (HasPendingRecovery())
                return false;

            throw;
        }
        finally
        {
            if (ReferenceEquals(
                    _resolutionCts,
                    resolutionCts))
            {
                _resolutionCts = null;
            }
        }
    }

    private async Task<StreamSelection?> ResolveStreamAsync(
        int episode,
        IEnumerable<string> excludedUrls,
        CancellationToken cancellationToken)
    {
        if (_settings is null)
            return null;

        string[] excluded =
            excludedUrls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        int attempt = 0;

        while (IsActive)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SetStatus(
                "Resolving S" +
                Season +
                " E" +
                episode +
                "...");

            try
            {
                IReadOnlyList<StreamInfo> streams =
                    await _addon.GetStreamsAsync(
                        ManifestUrl,
                        ImdbId,
                        Season,
                        episode,
                        cancellationToken);

                StreamSelection? selection =
                    _selector.SelectWithFallback(
                        streams,
                        _settings,
                        PreferredBingeGroup,
                        excluded);

                if (selection is null)
                {
                    throw new InvalidOperationException(
                        "No suitable direct stream was found.");
                }

                RetryAttempt = 0;
                NextRetryAt = null;
                LastError = string.Empty;

                return selection;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                attempt++;

                RetryAttempt = attempt;
                LastError = ex.Message;

                TimeSpan delay =
                    GetRetryDelay(attempt);

                NextRetryAt =
                    DateTimeOffset.Now + delay;

                WriteLog(
                    "S" +
                    Season +
                    " E" +
                    episode +
                    " failed: " +
                    ex.Message +
                    ". Retry in " +
                    (int)delay.TotalSeconds +
                    "s.");

                using var delayCts =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);

                _retryDelayCts = delayCts;

                try
                {
                    await Task.Delay(
                        delay,
                        delayCts.Token);
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                finally
                {
                    if (ReferenceEquals(
                            _retryDelayCts,
                            delayCts))
                    {
                        _retryDelayCts = null;
                    }
                }
            }
        }

        return null;
    }

    private void CommitEpisode(
        int episode,
        StreamSelection selection)
    {
        StreamInfo stream =
            selection.Stream;

        var entry = new RollingQueueEntry
        {
            Episode = episode,
            Url = stream.Url,
            Provider = stream.Provider,
            Seeders = stream.Seeders,
            BingeGroup = stream.BingeGroup,
            Label = BuildLabel(stream),
            ResolvedAt = DateTimeOffset.Now
        };

        lock (_stateLock)
        {
            _queue.Add(entry);
        }

        if (string.IsNullOrWhiteSpace(PreferredBingeGroup) &&
            !string.IsNullOrWhiteSpace(stream.BingeGroup))
        {
            PreferredBingeGroup =
                stream.BingeGroup;
        }

        Added++;
        NextEpisode = episode + 1;

        RetryAttempt = 0;
        NextRetryAt = null;
        LastError = string.Empty;

        WriteLog(
            "Added S" +
            Season +
            " E" +
            episode +
            ": " +
            entry.Label);

        EpisodeAdded?.Invoke(
            this,
            new RollingQueueEntryEventArgs(entry));

        UpdateCurrentEpisode();
    }

    private async Task RecoverEpisodeAsync(
        PendingRecovery recovery,
        CancellationToken sessionToken)
    {
        using var resolutionCts =
            CancellationTokenSource.CreateLinkedTokenSource(
                sessionToken);

        _resolutionCts = resolutionCts;

        try
        {
            RollingQueueEntry? entry =
                FindEntryByEpisode(
                    recovery.Episode);

            var excluded =
                new HashSet<string>(
                    StringComparer.Ordinal);

            if (entry is not null)
            {
                foreach (string url in entry.FailedUrls)
                    excluded.Add(url);

                if (!string.IsNullOrWhiteSpace(entry.Url))
                    excluded.Add(entry.Url);
            }

            StreamSelection? selection =
                await ResolveStreamAsync(
                    recovery.Episode,
                    excluded,
                    resolutionCts.Token);

            if (selection is null)
                return;

            await ReplaceFailedStreamAsync(
                recovery,
                selection,
                resolutionCts.Token);
        }
        finally
        {
            if (ReferenceEquals(
                    _resolutionCts,
                    resolutionCts))
            {
                _resolutionCts = null;
            }
        }
    }

    private async Task ReplaceFailedStreamAsync(
        PendingRecovery recovery,
        StreamSelection selection,
        CancellationToken cancellationToken)
    {
        StreamInfo replacement =
            selection.Stream;

        int playlistIndex =
            recovery.PlaylistIndex;

        if (playlistIndex < 0)
        {
            playlistIndex =
                FindQueueIndexByEpisode(
                    recovery.Episode);
        }

        if (playlistIndex < 0)
            playlistIndex = 0;

        string mediaTitle =
            BuildMediaTitle(
                recovery.Episode);

        await _mpv.SendCommandAsync(
            [
                "loadfile",
                replacement.Url,
                "insert-at-play",
                playlistIndex,
                "force-media-title=" + mediaTitle
            ],
            cancellationToken);

        RollingQueueEntry? entry =
            FindEntryByEpisode(
                recovery.Episode);

        if (entry is not null)
        {
            if (!string.IsNullOrWhiteSpace(entry.Url))
                entry.FailedUrls.Add(entry.Url);

            entry.Url = replacement.Url;
            entry.Provider = replacement.Provider;
            entry.Seeders = replacement.Seeders;
            entry.BingeGroup = replacement.BingeGroup;
            entry.Label = BuildLabel(replacement);
            entry.ResolvedAt = DateTimeOffset.Now;

            entry.LastTime = 0;
            entry.LastDuration = 0;
            entry.Completed = false;
        }

        int oldFailedItemIndex =
            playlistIndex + 1;

        await RemoveOldFailedItemAsync(
            oldFailedItemIndex,
            cancellationToken);

        try
        {
            await _mpv.SetPropertyAsync(
                "pause",
                false,
                cancellationToken);
        }
        catch (Exception ex)
        {
            WriteLog(
                "Could not unpause MPV: " +
                ex.Message);
        }

        WriteLog(
            "Recovered S" +
            Season +
            " E" +
            recovery.Episode +
            ".");

        if (entry is not null)
        {
            EpisodeRecovered?.Invoke(
                this,
                new RollingQueueEntryEventArgs(entry));
        }

        ScheduleMaintenance();
    }

    private async Task RemoveOldFailedItemAsync(
        int index,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await _mpv.RemovePlaylistItemAsync(
                    index,
                    cancellationToken);

                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        WriteLog(
            "Could not remove old failed MPV item: " +
            (lastError?.Message ?? "Unknown error."));
    }

    private async Task BeginRecoveryAsync(
        int episode,
        int playlistIndex,
        string error)
    {
        if (!IsActive || episode < 1)
            return;

        RollingQueueEntry? entry =
            FindEntryByEpisode(
                episode);

        if (entry is not null &&
            !string.IsNullOrWhiteSpace(entry.Url))
        {
            entry.FailedUrls.Add(entry.Url);
        }

        try
        {
            await _mpv.SetPropertyAsync(
                "pause",
                true);
        }
        catch (Exception ex)
        {
            WriteLog(
                "Could not pause MPV for recovery: " +
                ex.Message);
        }

        lock (_stateLock)
        {
            _pendingRecovery =
                new PendingRecovery(
                    episode,
                    playlistIndex,
                    error);
        }

        _resolutionCts?.Cancel();
        _retryDelayCts?.Cancel();

        ScheduleMaintenance();
    }

    private void OnMpvEndFile(
        object? sender,
        MpvEndFileEvent e)
    {
        if (!IsActive || _clearingPlaylist)
            return;

        _ = HandleEndFileAsync(e);
    }

    private async Task HandleEndFileAsync(
        MpvEndFileEvent e)
    {
        try
        {
            int episode =
                ResolveEpisodeForEndFile(e);

            if (e.IsError)
            {
                string error =
                    string.IsNullOrWhiteSpace(e.FileError)
                        ? "Unknown playback error."
                        : e.FileError;

                await BeginRecoveryAsync(
                    episode,
                    CurrentPlaylistIndex,
                    error);

                return;
            }

            if (e.ReachedEnd)
            {
                RollingQueueEntry? entry =
                    FindEntryByEpisode(
                        episode);

                if (entry is not null)
                    entry.Completed = true;

                WriteLog(
                    "Completed S" +
                    Season +
                    " E" +
                    episode +
                    ".");

                if (episode >= LastEpisode &&
                    NextEpisode > LastEpisode)
                {
                    FinishQueue(
                        "Finished " +
                        Title +
                        " S" +
                        Season +
                        ".");
                }
                else
                {
                    ScheduleMaintenance();
                }
            }
        }
        catch (Exception ex)
        {
            WriteLog(
                "Could not process MPV end-file event: " +
                ex.Message);
        }
    }

    private void OnMpvPropertyChanged(
        object? sender,
        MpvPropertyChangedEvent e)
    {
        if (!IsActive)
            return;

        switch (e.Name)
        {
            case "playlist-pos":
            case "playlist-playing-pos":
                UpdateCurrentEpisode();
                ScheduleMaintenance();
                break;

            case "time-pos":
                UpdateProgress(
                    time: _mpv.State.TimePos,
                    duration: null);
                break;

            case "duration":
                UpdateProgress(
                    time: null,
                    duration: _mpv.State.Duration);
                break;
        }
    }

    private void OnMpvStartFile(
        object? sender,
        MpvEvent e)
    {
        if (!IsActive ||
            e.PlaylistEntryId is null ||
            e.PlaylistEntryId <= 0)
        {
            return;
        }

        RollingQueueEntry? entry =
            GetQueueEntry(
                _mpv.GetCurrentPlaylistIndex());

        if (entry is null)
            return;

        lock (_stateLock)
        {
            _entryIdToEpisode[
                e.PlaylistEntryId.Value] =
                entry.Episode;
        }
    }

    private void OnMpvFileLoaded(
        object? sender,
        MpvEvent e)
    {
        if (IsActive)
            UpdateCurrentEpisode();
    }

    private void OnMpvPlaybackRestart(
        object? sender,
        MpvEvent e)
    {
        if (IsActive)
            UpdateCurrentEpisode();
    }

    private void OnMpvShutdown(
        object? sender,
        EventArgs e)
    {
        if (!IsActive)
            return;

        IsActive = false;
        _sessionCts?.Cancel();

        WriteLog("MPV closed.");
        SetStatus("MPV closed.");
    }

    private void OnMpvDisconnected(
        object? sender,
        EventArgs e)
    {
        if (!IsActive)
            return;

        IsActive = false;
        _sessionCts?.Cancel();

        WriteLog("MPV IPC disconnected.");
        SetStatus("MPV IPC disconnected.");
    }

    private void UpdateCurrentEpisode()
    {
        int index =
            _mpv.GetCurrentPlaylistIndex();

        RollingQueueEntry? entry =
            GetQueueEntry(index);

        if (entry is null)
            return;

        if (CurrentPlaylistIndex == index &&
            CurrentEpisode == entry.Episode)
        {
            return;
        }

        CurrentPlaylistIndex = index;
        CurrentEpisode = entry.Episode;

        if (_mpv.State.CurrentPlaylistEntryId > 0)
        {
            lock (_stateLock)
            {
                _entryIdToEpisode[
                    _mpv.State.CurrentPlaylistEntryId] =
                    entry.Episode;
            }
        }

        WriteLog(
            "Now playing S" +
            Season +
            " E" +
            entry.Episode +
            ".");

        EpisodeChanged?.Invoke(
            this,
            new RollingQueueEntryEventArgs(entry));
    }

    private void UpdateProgress(
        double? time,
        double? duration)
    {
        RollingQueueEntry? entry =
            GetQueueEntry(
                _mpv.GetCurrentPlaylistIndex());

        if (entry is null)
            return;

        if (time.HasValue && time.Value >= 0)
            entry.LastTime = time.Value;

        if (duration.HasValue && duration.Value > 0)
            entry.LastDuration = duration.Value;
    }

    private int ResolveEpisodeForEndFile(
        MpvEndFileEvent e)
    {
        if (e.PlaylistEntryId is > 0)
        {
            lock (_stateLock)
            {
                if (_entryIdToEpisode.TryGetValue(
                        e.PlaylistEntryId.Value,
                        out int mappedEpisode))
                {
                    return mappedEpisode;
                }
            }
        }

        RollingQueueEntry? entry =
            GetQueueEntry(
                CurrentPlaylistIndex);

        return entry?.Episode ?? CurrentEpisode;
    }

    private PendingRecovery? TakePendingRecovery()
    {
        lock (_stateLock)
        {
            PendingRecovery? recovery =
                _pendingRecovery;

            _pendingRecovery = null;

            return recovery;
        }
    }

    private bool HasPendingRecovery()
    {
        lock (_stateLock)
        {
            return _pendingRecovery is not null;
        }
    }

    private int GetEpisodesAhead()
    {
        lock (_stateLock)
        {
            if (_queue.Count == 0)
                return 0;

            int index = CurrentPlaylistIndex;

            if (index < 0)
                index = 0;

            return Math.Max(
                0,
                _queue.Count - index - 1);
        }
    }

    private RollingQueueEntry? GetQueueEntry(
        int index)
    {
        lock (_stateLock)
        {
            if (index < 0 ||
                index >= _queue.Count)
            {
                return null;
            }

            return _queue[index];
        }
    }

    private RollingQueueEntry? FindEntryByEpisode(
        int episode)
    {
        lock (_stateLock)
        {
            return _queue.FirstOrDefault(
                entry =>
                    entry.Episode == episode);
        }
    }

    private int FindQueueIndexByEpisode(
        int episode)
    {
        lock (_stateLock)
        {
            return _queue.FindIndex(
                entry =>
                    entry.Episode == episode);
        }
    }

    private string BuildMediaTitle(
        int episode)
    {
        string title =
            Title +
            " - S" +
            Season.ToString("00") +
            "E" +
            episode.ToString("00");

        return MpvService.GetSafeMediaTitle(title);
    }

    private static string BuildLabel(
        StreamInfo stream)
    {
        string text =
            string.Join(
                " ",
                new[]
                {
                    stream.Name,
                    stream.Title,
                    stream.Description
                }
                .Where(value =>
                    !string.IsNullOrWhiteSpace(value)));

        text =
            Regex.Replace(
                text,
                @"\s+",
                " ")
            .Trim();

        if (text.Length > 110)
            text = text[..110] + "...";

        return text;
    }

    private static TimeSpan GetRetryDelay(
        int attempt)
    {
        int index =
            Math.Clamp(
                attempt - 1,
                0,
                RetryDelays.Length - 1);

        return RetryDelays[index];
    }

    private void FinishQueue(string message)
    {
        IsActive = false;

        _sessionCts?.Cancel();
        _resolutionCts?.Cancel();
        _retryDelayCts?.Cancel();

        WriteLog(message);
        SetStatus(message);

        QueueFinished?.Invoke(
            this,
            new RollingQueueFinishedEventArgs(message));
    }

    private async Task StopInternalAsync()
    {
        IsActive = false;

        _resolutionCts?.Cancel();
        _retryDelayCts?.Cancel();
        _sessionCts?.Cancel();

        await Task.Yield();
    }

    private void ResetQueueState()
    {
        lock (_stateLock)
        {
            _queue.Clear();
            _entryIdToEpisode.Clear();
            _pendingRecovery = null;
        }

        CurrentPlaylistIndex = -1;

        Added = 0;
        Failed = 0;

        RetryAttempt = 0;
        NextRetryAt = null;
        LastError = string.Empty;

        PreferredBingeGroup = string.Empty;

        _sessionCts?.Dispose();
        _sessionCts = null;

        _resolutionCts?.Dispose();
        _resolutionCts = null;

        _retryDelayCts?.Dispose();
        _retryDelayCts = null;
    }

    private void WriteLog(string message)
    {
        LogAdded?.Invoke(
            this,
            new RollingQueueLogEventArgs(
                DateTimeOffset.Now,
                message));
    }

    private void SetStatus(string message)
    {
        StatusChanged?.Invoke(
            this,
            new RollingQueueStatusEventArgs(message));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            nameof(RollingQueueService));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        _mpv.PropertyChanged -= OnMpvPropertyChanged;
        _mpv.StartFile -= OnMpvStartFile;
        _mpv.FileLoaded -= OnMpvFileLoaded;
        _mpv.PlaybackRestart -= OnMpvPlaybackRestart;
        _mpv.EndFile -= OnMpvEndFile;
        _mpv.Shutdown -= OnMpvShutdown;
        _mpv.Disconnected -= OnMpvDisconnected;

        await StopInternalAsync();

        _sessionCts?.Dispose();
        _resolutionCts?.Dispose();
        _retryDelayCts?.Dispose();

        _maintenanceLock.Dispose();
    }

    private sealed record PendingRecovery(
        int Episode,
        int PlaylistIndex,
        string Error);
}

public sealed class RollingQueueEntry
{
    public int Episode { get; set; }

    public string Url { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public int Seeders { get; set; }

    public string BingeGroup { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public DateTimeOffset ResolvedAt { get; set; }

    public HashSet<string> FailedUrls { get; } =
        new(StringComparer.Ordinal);

    public double LastTime { get; set; }

    public double LastDuration { get; set; }

    public bool Completed { get; set; }
}

public sealed class RollingQueueStatusEventArgs : EventArgs
{
    public RollingQueueStatusEventArgs(string message)
    {
        Message = message;
    }

    public string Message { get; }
}

public sealed class RollingQueueLogEventArgs : EventArgs
{
    public RollingQueueLogEventArgs(
        DateTimeOffset timestamp,
        string message)
    {
        Timestamp = timestamp;
        Message = message;
    }

    public DateTimeOffset Timestamp { get; }

    public string Message { get; }
}

public sealed class RollingQueueEntryEventArgs : EventArgs
{
    public RollingQueueEntryEventArgs(
        RollingQueueEntry entry)
    {
        Entry = entry;
    }

    public RollingQueueEntry Entry { get; }
}

public sealed class RollingQueueFinishedEventArgs : EventArgs
{
    public RollingQueueFinishedEventArgs(string reason)
    {
        Reason = reason;
    }

    public string Reason { get; }
}