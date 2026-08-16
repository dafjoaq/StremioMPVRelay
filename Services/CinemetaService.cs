using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StremioMPVRelay.Services;

public sealed class CinemetaService : IDisposable
{
    private const string BaseUrl =
        "https://v3-cinemeta.strem.io/meta/series/";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public CinemetaService(
        HttpClient? httpClient = null)
    {
        _ownsHttpClient =
            httpClient is null;

        _httpClient =
            httpClient ??
            new HttpClient
            {
                Timeout =
                    TimeSpan.FromSeconds(20)
            };

        if (!_httpClient
                .DefaultRequestHeaders
                .UserAgent
                .Any())
        {
            _httpClient
                .DefaultRequestHeaders
                .UserAgent
                .ParseAdd(
                    "StremioMPVRelay/1.0");
        }
    }

    public async Task<CinemetaSeriesMetadata>
        GetSeriesMetadataAsync(
            string imdbId,
            CancellationToken cancellationToken = default)
    {
        imdbId =
            imdbId.Trim();

        if (!Regex.IsMatch(
                imdbId,
                @"^tt\d+$",
                RegexOptions.IgnoreCase))
        {
            throw new ArgumentException(
                "IMDb ID must look like tt0202430.",
                nameof(imdbId));
        }

        string url =
            BaseUrl +
            Uri.EscapeDataString(imdbId) +
            ".json";

        using HttpResponseMessage response =
            await _httpClient.GetAsync(
                url,
                cancellationToken);

        if (response.StatusCode ==
            HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                "Cinemeta could not find series " +
                imdbId +
                ".");
        }

        response.EnsureSuccessStatusCode();

        await using Stream stream =
            await response.Content
                .ReadAsStreamAsync(
                    cancellationToken);

        using JsonDocument document =
            await JsonDocument.ParseAsync(
                stream,
                cancellationToken:
                    cancellationToken);

        if (!document.RootElement.TryGetProperty(
                "meta",
                out JsonElement meta))
        {
            throw new InvalidOperationException(
                "Cinemeta returned no series metadata.");
        }

        string title =
            meta.TryGetProperty(
                "name",
                out JsonElement nameElement)
                ? nameElement.GetString() ??
                  imdbId
                : imdbId;

        var episodeCounts =
            new Dictionary<int, int>();

        if (meta.TryGetProperty(
                "videos",
                out JsonElement videos) &&
            videos.ValueKind ==
            JsonValueKind.Array)
        {
            foreach (JsonElement video in
                     videos.EnumerateArray())
            {
                if (!TryGetPositiveInt(
                        video,
                        "season",
                        out int season) ||
                    !TryGetPositiveInt(
                        video,
                        "episode",
                        out int episode))
                {
                    continue;
                }

                if (!episodeCounts.TryGetValue(
                        season,
                        out int lastEpisode) ||
                    episode > lastEpisode)
                {
                    episodeCounts[season] =
                        episode;
                }
            }
        }

        if (episodeCounts.Count == 0)
        {
            throw new InvalidOperationException(
                "Cinemeta returned no episode information for " +
                title +
                ".");
        }

        return new CinemetaSeriesMetadata
        {
            ImdbId = imdbId,
            Title = title,
            EpisodeCounts =
                episodeCounts
        };
    }

    private static bool TryGetPositiveInt(
        JsonElement element,
        string propertyName,
        out int value)
    {
        value = 0;

        if (!element.TryGetProperty(
                propertyName,
                out JsonElement property))
        {
            return false;
        }

        if (property.ValueKind ==
                JsonValueKind.Number &&
            property.TryGetInt32(
                out value))
        {
            return value > 0;
        }

        if (property.ValueKind ==
                JsonValueKind.String &&
            int.TryParse(
                property.GetString(),
                out value))
        {
            return value > 0;
        }

        return false;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}

public sealed class CinemetaSeriesMetadata
{
    public string ImdbId { get; init; } =
        string.Empty;

    public string Title { get; init; } =
        string.Empty;

    public IReadOnlyDictionary<int, int>
        EpisodeCounts
    {
        get;
        init;
    } = new Dictionary<int, int>();
}
