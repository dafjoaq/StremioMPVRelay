using StremioMPVRelay.Infrastructure;
using StremioMPVRelay.Models;

namespace StremioMPVRelay.Services;

public sealed class LibraryService
{
    private readonly string _libraryPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LibraryService()
    {
        _libraryPath = Path.Combine(
            AppContext.BaseDirectory,
            "StremioMpvLibrary.json");
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
            return await LoadUnlockedAsync(cancellationToken);
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
            await AtomicJsonFile.WriteAsync(
                _libraryPath,
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
        var library = await LoadAsync(cancellationToken);

        return library.Items
            .OrderByDescending(x => x.UpdatedAt)
            .ToList();
    }

    public async Task<SeriesEntry?> FindAsync(
        string imdbId,
        int season,
        CancellationToken cancellationToken = default)
    {
        var library = await LoadAsync(cancellationToken);

        return library.Items.FirstOrDefault(x =>
            string.Equals(
                x.ImdbId,
                imdbId,
                StringComparison.OrdinalIgnoreCase) &&
            x.Season == season);
    }

    public async Task<SeriesEntry> GetOrCreateAsync(
        string imdbId,
        string title,
        int season,
        int currentEpisode,
        int lastEpisode,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var library = await LoadUnlockedAsync(cancellationToken);

            var entry = library.Items.FirstOrDefault(x =>
                string.Equals(
                    x.ImdbId,
                    imdbId,
                    StringComparison.OrdinalIgnoreCase) &&
                x.Season == season);

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
                if (!string.IsNullOrWhiteSpace(title))
                    entry.Title = title;

                entry.CurrentEpisode = currentEpisode;
                entry.LastEpisode = lastEpisode;
                entry.UpdatedAt = DateTimeOffset.Now;
            }

            await AtomicJsonFile.WriteAsync(
                _libraryPath,
                library,
                cancellationToken);

            return entry;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateCurrentEpisodeAsync(
        string imdbId,
        string title,
        int season,
        int currentEpisode,
        int lastEpisode,
        CancellationToken cancellationToken = default)
    {
        await GetOrCreateAsync(
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
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var library = await LoadUnlockedAsync(cancellationToken);

            var entry = library.Items.FirstOrDefault(x =>
                string.Equals(
                    x.ImdbId,
                    imdbId,
                    StringComparison.OrdinalIgnoreCase) &&
                x.Season == season);

            if (entry is null)
            {
                entry = new SeriesEntry
                {
                    ImdbId = imdbId,
                    Title = title,
                    Season = season,
                    CurrentEpisode = episode,
                    LastEpisode = lastEpisode
                };

                library.Items.Add(entry);
            }

            if (!string.IsNullOrWhiteSpace(title))
                entry.Title = title;

            entry.LastEpisode = lastEpisode;

            var key = $"S{season}:E{episode}";

            entry.Progress[key] = new EpisodeProgress
            {
                PositionSeconds = completed
                    ? 0
                    : Math.Max(0, positionSeconds),

                DurationSeconds = Math.Max(0, durationSeconds),

                Completed = completed,

                UpdatedAt = DateTimeOffset.Now
            };

            if (completed && episode < lastEpisode)
                entry.CurrentEpisode = episode + 1;
            else
                entry.CurrentEpisode = episode;

            entry.UpdatedAt = DateTimeOffset.Now;

            await AtomicJsonFile.WriteAsync(
                _libraryPath,
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
        var entry = await FindAsync(
            imdbId,
            season,
            cancellationToken);

        if (entry is null)
            return null;

        var key = $"S{season}:E{episode}";

        return entry.Progress.TryGetValue(key, out var progress)
            ? progress
            : null;
    }

    private async Task<LibraryFile> LoadUnlockedAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_libraryPath))
            return new LibraryFile();

        var library =
            await AtomicJsonFile.ReadAsync<LibraryFile>(
                _libraryPath,
                cancellationToken);

        return library ?? new LibraryFile();
    }
}