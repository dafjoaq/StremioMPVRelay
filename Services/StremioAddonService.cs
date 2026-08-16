using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using StremioMPVRelay.Models;

namespace StremioMPVRelay.Services;

public sealed class StremioAddonService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public StremioAddonService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(50)
        };

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StremioMpvQueue/5.1");

        _ownsHttpClient = true;
    }

    public StremioAddonService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _ownsHttpClient = false;
    }

    public async Task<IReadOnlyList<StreamInfo>> GetStreamsAsync(
        string manifestUrl,
        string imdbId,
        int season,
        int episode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imdbId))
            throw new ArgumentException(
                "IMDb ID cannot be empty.",
                nameof(imdbId));

        if (season < 1)
            throw new ArgumentOutOfRangeException(nameof(season));

        if (episode < 1)
            throw new ArgumentOutOfRangeException(nameof(episode));

        manifestUrl = NormalizeManifestUrl(manifestUrl);

        string endpoint = BuildStreamEndpoint(
            manifestUrl,
            imdbId,
            season,
            episode);

        using var response = await _httpClient.GetAsync(
            endpoint,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream =
            await response.Content.ReadAsStreamAsync(cancellationToken);

        var addonResponse =
            await JsonSerializer.DeserializeAsync<AddonStreamResponse>(
                stream,
                JsonOptions,
                cancellationToken);

        if (addonResponse?.Streams is null ||
            addonResponse.Streams.Count == 0)
        {
            throw new InvalidOperationException(
                "The addon returned no streams.");
        }

        var result = new List<StreamInfo>(
            addonResponse.Streams.Count);

        for (int i = 0; i < addonResponse.Streams.Count; i++)
        {
            AddonStream source = addonResponse.Streams[i];

            string text = BuildStreamText(source);

            result.Add(new StreamInfo
            {
                Name = source.Name ?? string.Empty,
                Title = source.Title ?? string.Empty,
                Description = source.Description ?? string.Empty,
                Url = source.Url ?? string.Empty,

                Provider = GetProvider(source),
                Quality = GetQuality(text),
                Seeders = GetSeeders(text),

                BingeGroup =
                    source.BehaviorHints?.BingeGroup
                    ?? string.Empty,

                InfoHash = source.InfoHash ?? string.Empty,
                FileIndex = source.FileIdx ?? -1,

                OriginalPosition = i,
                IsCached = IsCached(text)
            });
        }

        return result;
    }

    public static string NormalizeManifestUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(
                "Manifest URL cannot be empty.",
                nameof(value));

        string url = value.Trim();

        if (url.StartsWith(
                "stremio://",
                StringComparison.OrdinalIgnoreCase))
        {
            url =
                "https://" +
                url["stremio://".Length..];
        }

        if (!url.StartsWith(
                "https://",
                StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith(
                "http://",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The addon URL must begin with https:// or stremio://.");
        }

        if (!Regex.IsMatch(
                url,
                @"/manifest\.json(?:\?.*)?$",
                RegexOptions.IgnoreCase))
        {
            throw new ArgumentException(
                "Paste the full addon URL ending in manifest.json.");
        }

        return url;
    }

    public static string BuildStreamEndpoint(
        string manifestUrl,
        string imdbId,
        int season,
        int episode)
    {
        string normalized =
            NormalizeManifestUrl(manifestUrl);

        string baseUrl = Regex.Replace(
            normalized,
            @"manifest\.json(?:\?.*)?$",
            string.Empty,
            RegexOptions.IgnoreCase);

        string episodeId =
            $"{imdbId}:{season}:{episode}";

        string escapedId =
            Uri.EscapeDataString(episodeId);

        return $"{baseUrl}stream/series/{escapedId}.json";
    }

    private static string BuildStreamText(AddonStream stream)
    {
        return string.Join(
            " ",
            new[]
            {
                stream.Name,
                stream.Title,
                stream.Description
            }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static int GetSeeders(string text)
    {
        string[] patterns =
        {
            @"👤\s*([0-9][0-9,]*)",
            @"\bseeders?\s*[:=]?\s*([0-9][0-9,]*)",
            @"\bS\s*[:=]\s*([0-9][0-9,]*)"
        };

        foreach (string pattern in patterns)
        {
            Match match = Regex.Match(
                text,
                pattern,
                RegexOptions.IgnoreCase);

            if (!match.Success)
                continue;

            string number =
                match.Groups[1].Value.Replace(",", "");

            if (int.TryParse(number, out int seeders))
                return seeders;
        }

        return -1;
    }

    private static string GetProvider(AddonStream stream)
    {
        string text =
            !string.IsNullOrWhiteSpace(stream.Title)
                ? stream.Title
                : stream.Description ?? string.Empty;

        Match match = Regex.Match(
            text,
            @"⚙️?\s*([^\r\n]+)");

        if (match.Success)
            return match.Groups[1].Value.Trim();

        return "Unknown";
    }

    private static string GetQuality(string text)
    {
        if (Regex.IsMatch(
                text,
                @"\b2160p\b|\b4k\b",
                RegexOptions.IgnoreCase))
        {
            return "2160p / 4K";
        }

        if (Regex.IsMatch(
                text,
                @"\b1080p\b",
                RegexOptions.IgnoreCase))
        {
            return "1080p";
        }

        if (Regex.IsMatch(
                text,
                @"\b720p\b",
                RegexOptions.IgnoreCase))
        {
            return "720p";
        }

        return string.Empty;
    }

    private static bool IsCached(string text)
    {
        return Regex.IsMatch(
            text,
            @"\[(?:TB|RD|AD|PM)\+\]|\bcached\b|\binstant(?:ly)?\b",
            RegexOptions.IgnoreCase);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    private sealed class AddonStreamResponse
    {
        [JsonPropertyName("streams")]
        public List<AddonStream> Streams { get; set; } = [];
    }

    private sealed class AddonStream
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("infoHash")]
        public string? InfoHash { get; set; }

        [JsonPropertyName("fileIdx")]
        public int? FileIdx { get; set; }

        [JsonPropertyName("behaviorHints")]
        public BehaviorHints? BehaviorHints { get; set; }
    }

    private sealed class BehaviorHints
    {
        [JsonPropertyName("bingeGroup")]
        public string? BingeGroup { get; set; }
    }
}