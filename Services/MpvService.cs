using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StremioMPVRelay.Services;

public sealed class MpvService : IAsyncDisposable
{
    public const string PipeName = "stremio-mpv-rolling-v51-resilient";

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private readonly ConcurrentDictionary<
        long,
        TaskCompletionSource<MpvCommandResponse>> _pendingCommands = new();

    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    private CancellationTokenSource? _readCts;
    private Process? _process;

    private long _nextRequestId = 1000;
    private bool _disposed;

    public bool IsConnected => _pipe?.IsConnected == true;

    public MpvState State { get; } = new();

    public event EventHandler<MpvEvent>? EventReceived;
    public event EventHandler<MpvPropertyChangedEvent>? PropertyChanged;
    public event EventHandler<MpvEvent>? StartFile;
    public event EventHandler<MpvEvent>? FileLoaded;
    public event EventHandler<MpvEvent>? PlaybackRestart;
    public event EventHandler<MpvEndFileEvent>? EndFile;
    public event EventHandler? Shutdown;
    public event EventHandler? Disconnected;

    public async Task StartAndConnectAsync(
        string mpvPath,
        string luaScriptPath,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (IsConnected)
            return;

        if (string.IsNullOrWhiteSpace(mpvPath))
            throw new ArgumentException("MPV path is empty.", nameof(mpvPath));

        if (!File.Exists(mpvPath))
            throw new FileNotFoundException("mpv.exe was not found.", mpvPath);

        if (!File.Exists(luaScriptPath))
        {
            throw new FileNotFoundException(
                "MPV Lua script was not found.",
                luaScriptPath);
        }

        StartMpv(mpvPath, luaScriptPath);

        for (int attempt = 0; attempt < 50; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await ConnectAsync(250, cancellationToken))
                return;

            await Task.Delay(100, cancellationToken);
        }

        throw new IOException(
            "MPV started, but its IPC named pipe could not be reached.");
    }

    public void StartMpv(
        string mpvPath,
        string luaScriptPath)
    {
        ThrowIfDisposed();

        var startInfo = new ProcessStartInfo
        {
            FileName = mpvPath,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("--idle=yes");
        startInfo.ArgumentList.Add("--force-window=yes");
        startInfo.ArgumentList.Add($"--input-ipc-server={PipeName}");
        startInfo.ArgumentList.Add($"--script={luaScriptPath}");

        startInfo.ArgumentList.Add("--hwdec=auto");
        startInfo.ArgumentList.Add("--audio-pitch-correction=yes");

        startInfo.ArgumentList.Add("--cache=yes");
        startInfo.ArgumentList.Add("--demuxer-max-bytes=256MiB");
        startInfo.ArgumentList.Add("--demuxer-max-back-bytes=64MiB");
        startInfo.ArgumentList.Add("--demuxer-readahead-secs=60");
        startInfo.ArgumentList.Add("--cache-pause=yes");
        startInfo.ArgumentList.Add("--cache-pause-wait=1");
        startInfo.ArgumentList.Add("--prefetch-playlist=yes");
        startInfo.ArgumentList.Add("--save-position-on-quit=yes");
        startInfo.ArgumentList.Add("--reset-on-next-file=speed");
        startInfo.ArgumentList.Add("--osd-duration=1200");

        _process = Process.Start(startInfo)
                   ?? throw new InvalidOperationException(
                       "Windows could not start MPV.");
    }

    public async Task<bool> ConnectAsync(
        int timeoutMilliseconds = 500,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (IsConnected)
            return true;

        CleanupPipe();

        var pipe = new NamedPipeClientStream(
            ".",
            PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync(
                timeoutMilliseconds,
                cancellationToken);
        }
        catch
        {
            pipe.Dispose();
            return false;
        }

        var utf8 = new UTF8Encoding(false);

        _pipe = pipe;

        _reader = new StreamReader(
            pipe,
            utf8,
            false,
            8192,
            leaveOpen: true);

        _writer = new StreamWriter(
            pipe,
            utf8,
            8192,
            leaveOpen: true)
        {
            AutoFlush = true
        };

        ResetState();

        _readCts = new CancellationTokenSource();

        _ = ReadLoopAsync(_readCts.Token);

        try
        {
            await ObservePropertyAsync(101, "playlist-count", cancellationToken);
            await ObservePropertyAsync(102, "playlist-pos", cancellationToken);
            await ObservePropertyAsync(103, "playlist-playing-pos", cancellationToken);
            await ObservePropertyAsync(104, "time-pos", cancellationToken);
            await ObservePropertyAsync(105, "duration", cancellationToken);
            await ObservePropertyAsync(106, "path", cancellationToken);
            await ObservePropertyAsync(107, "idle-active", cancellationToken);
            await ObservePropertyAsync(108, "speed", cancellationToken);

            await SendCommandAsync(
                ["get_property", "mpv-version"],
                cancellationToken);

            return true;
        }
        catch
        {
            CleanupPipe();
            return false;
        }
    }

    public Task<MpvCommandResponse> ObservePropertyAsync(
        int observerId,
        string propertyName,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            [
                "observe_property",
                observerId,
                propertyName
            ],
            cancellationToken);
    }

    public async Task<MpvCommandResponse> SendCommandAsync(
        object?[] command,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!IsConnected || _writer is null)
            throw new InvalidOperationException("MPV IPC is not connected.");

        long requestId = Interlocked.Increment(ref _nextRequestId);

        var completion =
            new TaskCompletionSource<MpvCommandResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pendingCommands.TryAdd(requestId, completion))
            throw new InvalidOperationException("Could not create MPV request.");

        string json = JsonSerializer.Serialize(
            new
            {
                command,
                request_id = requestId
            });

        try
        {
            await _writeLock.WaitAsync(cancellationToken);

            try
            {
                if (_writer is null || !IsConnected)
                    throw new IOException("MPV IPC disconnected.");

                await _writer.WriteLineAsync(json);
                await _writer.FlushAsync(cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }

            return await completion.Task.WaitAsync(
                TimeSpan.FromSeconds(10),
                cancellationToken);
        }
        catch
        {
            _pendingCommands.TryRemove(requestId, out _);
            throw;
        }
    }

    public Task<MpvCommandResponse> LoadFileAsync(
        string url,
        string mediaTitle,
        bool playImmediately = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Stream URL is empty.", nameof(url));

        string mode = playImmediately
            ? "append-play"
            : "append";

        return SendCommandAsync(
            [
                "loadfile",
                url,
                mode,
                -1,
                "force-media-title=" + GetSafeMediaTitle(mediaTitle)
            ],
            cancellationToken);
    }

    public async Task ClearPlaylistAsync(
        CancellationToken cancellationToken = default)
    {
        await SendCommandAsync(
            ["stop"],
            cancellationToken);

        await SendCommandAsync(
            ["playlist-clear"],
            cancellationToken);

        State.PlaylistCount = 0;
        State.PlaylistPos = -1;
        State.PlaylistPlayingPos = -1;
    }

    public Task<MpvCommandResponse> RemovePlaylistItemAsync(
        int index,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            [
                "playlist-remove",
                index
            ],
            cancellationToken);
    }

    public Task<MpvCommandResponse> SetPropertyAsync(
        string propertyName,
        object? value,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            [
                "set_property",
                propertyName,
                value
            ],
            cancellationToken);
    }

    public Task<MpvCommandResponse> SetSpeedAsync(
        double speed,
        CancellationToken cancellationToken = default)
    {
        if (speed <= 0)
            throw new ArgumentOutOfRangeException(nameof(speed));

        return SetPropertyAsync(
            "speed",
            speed,
            cancellationToken);
    }

    public Task<MpvCommandResponse> SeekAbsoluteAsync(
        double seconds,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            [
                "seek",
                Math.Max(0, seconds),
                "absolute",
                "exact"
            ],
            cancellationToken);
    }

    public Task<MpvCommandResponse> StopAsync(
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            ["stop"],
            cancellationToken);
    }

    public int GetCurrentPlaylistIndex()
    {
        if (State.PlaylistPlayingPos >= 0)
            return State.PlaylistPlayingPos;

        return State.PlaylistPos;
    }

    public static string GetSafeMediaTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "Stremio episode";

        string result = Regex.Replace(
            title,
            @"[,\r\n\t]+",
            " ");

        result = Regex.Replace(
            result,
            @"\s{2,}",
            " ");

        return result.Trim();
    }

    private async Task ReadLoopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                StreamReader? reader = _reader;

                if (reader is null || !IsConnected)
                    break;

                string? line =
                    await reader.ReadLineAsync(cancellationToken);

                if (line is null)
                    break;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                ProcessMessage(line);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            bool wasConnected = _pipe is not null;

            FailPendingCommands(
                new IOException("MPV IPC disconnected."));

            CleanupPipe();

            if (wasConnected)
                RaiseEvent(Disconnected);
        }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            using JsonDocument document =
                JsonDocument.Parse(json);

            JsonElement root = document.RootElement;

            if (root.TryGetProperty(
                    "request_id",
                    out JsonElement requestIdElement) &&
                requestIdElement.TryGetInt64(out long requestId))
            {
                ProcessCommandResponse(root, requestId);
                return;
            }

            if (!root.TryGetProperty(
                    "event",
                    out JsonElement eventElement))
            {
                return;
            }

            string eventName =
                eventElement.GetString() ?? string.Empty;

            var mpvEvent = new MpvEvent
            {
                Event = eventName,
                Name = ReadString(root, "name"),
                Reason = ReadString(root, "reason"),
                FileError = ReadString(root, "file_error"),
                PlaylistEntryId = ReadInt64(root, "playlist_entry_id"),

                Data = root.TryGetProperty(
                    "data",
                    out JsonElement data)
                    ? data.Clone()
                    : null
            };

            State.LastEventAt = DateTimeOffset.Now;

            HandleMpvEvent(mpvEvent);

            RaiseEvent(EventReceived, mpvEvent);
        }
        catch (JsonException)
        {
            // Ignore malformed MPV IPC lines.
        }
    }

    private void ProcessCommandResponse(
        JsonElement root,
        long requestId)
    {
        if (!_pendingCommands.TryRemove(
                requestId,
                out TaskCompletionSource<MpvCommandResponse>? completion))
        {
            return;
        }

        string error =
            ReadString(root, "error") ?? string.Empty;

        JsonElement? data = null;

        if (root.TryGetProperty(
                "data",
                out JsonElement dataElement))
        {
            data = dataElement.Clone();
        }

        if (!string.IsNullOrWhiteSpace(error) &&
            !string.Equals(
                error,
                "success",
                StringComparison.OrdinalIgnoreCase))
        {
            completion.TrySetException(
                new MpvCommandException(
                    requestId,
                    error));

            return;
        }

        completion.TrySetResult(
            new MpvCommandResponse
            {
                RequestId = requestId,
                Error = error,
                Data = data
            });
    }

    private void HandleMpvEvent(MpvEvent mpvEvent)
    {
        switch (mpvEvent.Event)
        {
            case "property-change":
                HandlePropertyChange(mpvEvent);
                break;

            case "start-file":
                State.CurrentPlaylistEntryId =
                    mpvEvent.PlaylistEntryId ?? 0;

                RaiseEvent(StartFile, mpvEvent);
                break;

            case "file-loaded":
                RaiseEvent(FileLoaded, mpvEvent);
                break;

            case "playback-restart":
                RaiseEvent(PlaybackRestart, mpvEvent);
                break;

            case "end-file":
                RaiseEvent(
                    EndFile,
                    new MpvEndFileEvent
                    {
                        Reason = mpvEvent.Reason ?? string.Empty,
                        FileError = mpvEvent.FileError ?? string.Empty,
                        PlaylistEntryId = mpvEvent.PlaylistEntryId
                    });
                break;

            case "shutdown":
                RaiseEvent(Shutdown);
                break;
        }
    }

    private void HandlePropertyChange(MpvEvent mpvEvent)
    {
        if (string.IsNullOrWhiteSpace(mpvEvent.Name))
            return;

        JsonElement? data = mpvEvent.Data;

        switch (mpvEvent.Name)
        {
            case "playlist-count":
                State.PlaylistCount = ReadInt32(data, 0);
                break;

            case "playlist-pos":
                State.PlaylistPos = ReadInt32(data, -1);
                break;

            case "playlist-playing-pos":
                State.PlaylistPlayingPos = ReadInt32(data, -1);
                break;

            case "time-pos":
                State.TimePos = ReadDouble(data, 0);
                break;

            case "duration":
                State.Duration = ReadDouble(data, 0);
                break;

            case "path":
                State.Path = ReadString(data) ?? string.Empty;
                break;

            case "idle-active":
                State.IdleActive = ReadBoolean(data, true);
                break;

            case "speed":
                State.Speed = ReadDouble(data, 1.0);
                break;
        }

        RaiseEvent(
            PropertyChanged,
            new MpvPropertyChangedEvent
            {
                Name = mpvEvent.Name,
                Data = data
            });
    }

    private static string? ReadString(
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
            return null;

        if (value.ValueKind == JsonValueKind.Null)
            return null;

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ToString();
    }

    private static string? ReadString(JsonElement? value)
    {
        if (value is null ||
            value.Value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.Value.ValueKind == JsonValueKind.String
            ? value.Value.GetString()
            : value.Value.ToString();
    }

    private static long? ReadInt64(
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
            return null;

        return value.TryGetInt64(out long result)
            ? result
            : null;
    }

    private static int ReadInt32(
        JsonElement? value,
        int fallback)
    {
        if (value is null)
            return fallback;

        return value.Value.TryGetInt32(out int result)
            ? result
            : fallback;
    }

    private static double ReadDouble(
        JsonElement? value,
        double fallback)
    {
        if (value is null)
            return fallback;

        return value.Value.TryGetDouble(out double result)
            ? result
            : fallback;
    }

    private static bool ReadBoolean(
        JsonElement? value,
        bool fallback)
    {
        if (value is null)
            return fallback;

        return value.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }

    private void ResetState()
    {
        State.PlaylistCount = 0;
        State.PlaylistPos = -1;
        State.PlaylistPlayingPos = -1;

        State.TimePos = 0;
        State.Duration = 0;

        State.Path = string.Empty;

        State.IdleActive = true;
        State.Speed = 1.0;

        State.CurrentPlaylistEntryId = 0;
        State.LastEventAt = DateTimeOffset.Now;
    }

    private void FailPendingCommands(Exception exception)
    {
        foreach (KeyValuePair<
                     long,
                     TaskCompletionSource<MpvCommandResponse>> item
                 in _pendingCommands)
        {
            if (_pendingCommands.TryRemove(
                    item.Key,
                    out TaskCompletionSource<MpvCommandResponse>? completion))
            {
                completion.TrySetException(exception);
            }
        }
    }

    private void CleanupPipe()
    {
        try
        {
            _readCts?.Cancel();
        }
        catch
        {
        }

        _readCts?.Dispose();
        _readCts = null;

        try
        {
            _writer?.Dispose();
        }
        catch
        {
        }

        try
        {
            _reader?.Dispose();
        }
        catch
        {
        }

        try
        {
            _pipe?.Dispose();
        }
        catch
        {
        }

        _writer = null;
        _reader = null;
        _pipe = null;
    }

    private void RaiseEvent(
        EventHandler? handler)
    {
        if (handler is null)
            return;

        foreach (EventHandler subscriber in handler.GetInvocationList())
        {
            try
            {
                subscriber(this, EventArgs.Empty);
            }
            catch
            {
            }
        }
    }

    private void RaiseEvent<T>(
        EventHandler<T>? handler,
        T eventArgs)
        where T : EventArgs
    {
        if (handler is null)
            return;

        foreach (EventHandler<T> subscriber in handler.GetInvocationList())
        {
            try
            {
                subscriber(this, eventArgs);
            }
            catch
            {
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            nameof(MpvService));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _readCts?.Cancel();
        }
        catch
        {
        }

        await Task.Yield();

        FailPendingCommands(
            new ObjectDisposedException(
                nameof(MpvService)));

        CleanupPipe();

        _process?.Dispose();
        _process = null;

        _writeLock.Dispose();
    }
}

public sealed class MpvState
{
    public int PlaylistCount { get; internal set; }

    public int PlaylistPos { get; internal set; } = -1;

    public int PlaylistPlayingPos { get; internal set; } = -1;

    public double TimePos { get; internal set; }

    public double Duration { get; internal set; }

    public string Path { get; internal set; } = string.Empty;

    public bool IdleActive { get; internal set; } = true;

    public double Speed { get; internal set; } = 1.0;

    public long CurrentPlaylistEntryId { get; internal set; }

    public DateTimeOffset LastEventAt { get; internal set; }
}

public class MpvEvent : EventArgs
{
    public string Event { get; init; } = string.Empty;

    public string? Name { get; init; }

    public JsonElement? Data { get; init; }

    public string? Reason { get; init; }

    public string? FileError { get; init; }

    public long? PlaylistEntryId { get; init; }
}

public sealed class MpvPropertyChangedEvent : EventArgs
{
    public string Name { get; init; } = string.Empty;

    public JsonElement? Data { get; init; }
}

public sealed class MpvEndFileEvent : EventArgs
{
    public string Reason { get; init; } = string.Empty;

    public string FileError { get; init; } = string.Empty;

    public long? PlaylistEntryId { get; init; }

    public bool IsError =>
        string.Equals(
            Reason,
            "error",
            StringComparison.OrdinalIgnoreCase);

    public bool ReachedEnd =>
        string.Equals(
            Reason,
            "eof",
            StringComparison.OrdinalIgnoreCase);
}

public sealed class MpvCommandResponse
{
    public long RequestId { get; init; }

    public string Error { get; init; } = string.Empty;

    public JsonElement? Data { get; init; }
}

public sealed class MpvCommandException : Exception
{
    public MpvCommandException(
        long requestId,
        string error)
        : base(
            "MPV request " +
            requestId +
            " failed: " +
            error)
    {
        RequestId = requestId;
    }

    public long RequestId { get; }
}