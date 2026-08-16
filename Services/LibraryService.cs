using StremioMPVRelay.Infrastructure;
using StremioMPVRelay.Models;

namespace StremioMPVRelay.Services;

public sealed class LibraryService
{
    private const string LibraryFileName =
        "StremioMpvLibrary.json";

    private readonly string _libraryPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LibraryService()
    {
        _libraryPath = Path.Combine(
            GetDataDirectory(),
            LibraryFileName);
    }

    public string LibraryPath => _libraryPath;

    public bool Exists()
    {
        return File.Exists(_libraryPath);
    }

    public async Task<LibraryFile> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            return await LoadUnlockedAsync(
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        LibraryFile library,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            await SaveUnlockedAsync(
                library,
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<SeriesEntry>> GetSeriesAsync(
        CancellationToken cancellationToken = default)
    {
        var library =
            await LoadAsync(cancellationToken);

        return library.Items
            .OrderByDescending(
                entry => entry.UpdatedAt)
            .ToList();
    }

    public async Task<SeriesEntry?> FindAsync(
        string imdbId,
        int season,
        CancellationToken cancellationToken = default)
    {
        var library =
            await LoadAsync(cancellationToken);

        return FindEntry(
            library,
            imdbId,
            season);
    }

    public async Task<SeriesEntry> GetOrCreateAsync(
        string imdbId,
        string title,
        int season,
        int currentEpisode,
        int lastEpisode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imdbId);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var library =
                await LoadUnlockedAsync(
                    cancellationToken);

            var entry =
                FindEntry(
                    library,
                    imdbId,
                    season);

            if (entry is null)
            {
                entry = new SeriesEntry
                {
                    ImdbId = imdbId,
                    Title = title,
                    Season = season,
                    CurrentEpisode = currentEpisode,
                    LastEpisode = lastEpisode,
                    UpdatedAt = DateTimeOffset.Now
                };

                library.Items.Add(entry);
            }
            else
            {
                UpdateSeriesMetadata(
                    entry,
                    title,
                    currentEpisode,
                    lastEpisode);
            }

            await SaveUnlockedAsync(
                library,
                cancellationToken);

            return entry;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task UpdateCurrentEpisodeAsync(
        string imdbId,
        string title,
        int season,
        int currentEpisode,
        int lastEpisode,
        CancellationToken cancellationToken = default)
    {
        return GetOrCreateAsync(
            imdbId,
            title,
            season,
            currentEpisode,
            lastEpisode,
            cancellationToken);
    }

    public async Task UpdateProgressAsync(
        string imdbId,
        string title,
        int season,
        int episode,
        int lastEpisode,
        double positionSeconds,
        double durationSeconds,
        bool completed,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imdbId);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var library =
                await LoadUnlockedAsync(
                    cancellationToken);

            var entry =
                FindEntry(
                    library,
                    imdbId,
                    season);

            if (entry is null)
            {
                entry = new SeriesEntry
                {
                    ImdbId = imdbId,
                    Title = title,
                    Season = season,
                    CurrentEpisode = episode,
                    LastEpisode = lastEpisode,
                    UpdatedAt = DateTimeOffset.Now
                };

                library.Items.Add(entry);
            }

            if (!string.IsNullOrWhiteSpace(title))
            {
                entry.Title = title;
            }

            entry.LastEpisode =
                lastEpisode;

            string progressKey =
                BuildProgressKey(
                    season,
                    episode);

            entry.Progress[progressKey] =
                new EpisodeProgress
                {
                    PositionSeconds =
                        completed
                            ? 0
                            : Math.Max(
                                0,
                                positionSeconds),

                    DurationSeconds =
                        Math.Max(
                            0,
                            durationSeconds),

                    Completed =
                        completed,

                    UpdatedAt =
                        DateTimeOffset.Now
                };

            entry.CurrentEpisode =
                completed && episode < lastEpisode
                    ? episode + 1
                    : episode;

            entry.UpdatedAt =
                DateTimeOffset.Now;

            await SaveUnlockedAsync(
                library,
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<EpisodeProgress?> GetProgressAsync(
        string imdbId,
        int season,
        int episode,
        CancellationToken cancellationToken = default)
    {
        var entry =
            await FindAsync(
                imdbId,
                season,
                cancellationToken);

        if (entry is null)
        {
            return null;
        }

        string progressKey =
            BuildProgressKey(
                season,
                episode);

        return entry.Progress.TryGetValue(
            progressKey,
            out var progress)
            ? progress
            : null;
    }

    private async Task<LibraryFile> LoadUnlockedAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_libraryPath))
        {
            return new LibraryFile();
        }

        var library =
            await AtomicJsonFile.ReadAsync<LibraryFile>(
                _libraryPath,
                cancellationToken);

        return library
               ?? new LibraryFile();
    }

    private Task SaveUnlockedAsync(
        LibraryFile library,
        CancellationToken cancellationToken)
    {
        return AtomicJsonFile.WriteAsync(
            _libraryPath,
            library,
            cancellationToken);
    }

    private static SeriesEntry? FindEntry(
        LibraryFile library,
        string imdbId,
        int season)
    {
        return library.Items.FirstOrDefault(
            entry =>
                string.Equals(
                    entry.ImdbId,
                    imdbId,
                    StringComparison.OrdinalIgnoreCase)
                && entry.Season == season);
    }

    private static void UpdateSeriesMetadata(
        SeriesEntry entry,
        string title,
        int currentEpisode,
        int lastEpisode)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            entry.Title = title;
        }

        entry.CurrentEpisode =
            currentEpisode;

        entry.LastEpisode =
            lastEpisode;

        entry.UpdatedAt =
            DateTimeOffset.Now;
    }

    private static string BuildProgressKey(
        int season,
        int episode)
    {
        return $"S{season}:E{episode}";
    }

    private static string GetDataDirectory()
    {
#if DEBUG
        // Rider/Visual Studio Debug build:
        // bin\Debug\net8.0-windows -> project root.
        return Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                ".."));
#else

#endif
    }
}