using StremioMPVRelay.Models;

namespace StremioMPVRelay.Services;

public sealed class StreamSelector
{
    public StreamInfo? Select(
        IEnumerable<StreamInfo> streams,
        string quality,
        string containsText,
        string provider,
        int minimumSeeders,
        string ranking,
        string preferredBingeGroup = "",
        IEnumerable<string>? excludedUrls = null)
    {
        ArgumentNullException.ThrowIfNull(streams);

        var excluded = new HashSet<string>(
            excludedUrls?.Where(x => !string.IsNullOrWhiteSpace(x))
            ?? [],
            StringComparer.Ordinal);

        string[] requiredWords = (containsText ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        var candidates = new List<Candidate>();

        int position = 0;

        foreach (StreamInfo stream in streams)
        {
            position++;

            // The original PowerShell only accepts direct URL streams.
            if (string.IsNullOrWhiteSpace(stream.Url))
                continue;

            if (excluded.Contains(stream.Url))
                continue;

            string text = BuildStreamText(stream);

            if (!ContainsRequiredWords(text, requiredWords))
                continue;

            if (!MatchesProvider(stream, provider))
                continue;

            if (minimumSeeders > 0 &&
                stream.Seeders < minimumSeeders)
            {
                continue;
            }

            if (!MatchesQuality(text, quality))
                continue;

            bool bingeMatch =
                !string.IsNullOrWhiteSpace(preferredBingeGroup) &&
                string.Equals(
                    stream.BingeGroup,
                    preferredBingeGroup,
                    StringComparison.Ordinal);

            candidates.Add(new Candidate
            {
                Stream = stream,
                Position = position,
                BingeMatch = bingeMatch ? 1 : 0,
                Cached = stream.IsCached ? 1 : 0
            });
        }

        if (candidates.Count == 0)
            return null;

        Candidate selected;

        switch (ranking)
        {
            case "First matching result":
                selected = candidates
                    .OrderBy(x => x.Position)
                    .First();
                break;

            case "Highest seeders":
                selected = candidates
                    .OrderByDescending(x => x.Stream.Seeders)
                    .ThenByDescending(x => x.Cached)
                    .ThenBy(x => x.Position)
                    .First();
                break;

            default:
                // Smart ranking:
                // 1. Keep the preferred binge group
                // 2. Prefer cached / instantly available
                // 3. Prefer more seeders
                // 4. Preserve the addon's original ordering
                selected = candidates
                    .OrderByDescending(x => x.BingeMatch)
                    .ThenByDescending(x => x.Cached)
                    .ThenByDescending(x => x.Stream.Seeders)
                    .ThenBy(x => x.Position)
                    .First();
                break;
        }

        return selected.Stream;
    }

    public StreamSelection? SelectWithFallback(
        IEnumerable<StreamInfo> streams,
        AppSettings settings,
        string preferredBingeGroup = "",
        IEnumerable<string>? excludedUrls = null)
    {
        ArgumentNullException.ThrowIfNull(streams);
        ArgumentNullException.ThrowIfNull(settings);

        // Materialize once because we may evaluate the streams
        // several times through the fallback tiers.
        var streamList = streams.ToList();

        var tiers = new[]
        {
            new SelectionTier(
                settings.Quality,
                settings.Contains,
                settings.Provider,
                settings.MinimumSeeders,
                "preferred settings"),

            new SelectionTier(
                settings.Quality,
                settings.Contains,
                "Any provider",
                settings.MinimumSeeders,
                "any provider"),

            new SelectionTier(
                settings.Quality,
                settings.Contains,
                "Any provider",
                0,
                "any seeder count"),

            new SelectionTier(
                "First result",
                settings.Contains,
                "Any provider",
                0,
                "any quality while preserving required words")
        };

        for (int i = 0; i < tiers.Length; i++)
        {
            SelectionTier tier = tiers[i];

            StreamInfo? selected = Select(
                streamList,
                tier.Quality,
                tier.Contains,
                tier.Provider,
                tier.MinimumSeeders,
                settings.Ranking,
                preferredBingeGroup,
                excludedUrls);

            if (selected is null)
                continue;

            return new StreamSelection(
                selected,
                FallbackTier: i + 1,
                FallbackName: tier.Name);
        }

        return null;
    }

    private static bool ContainsRequiredWords(
        string text,
        IEnumerable<string> requiredWords)
    {
        foreach (string word in requiredWords)
        {
            if (!text.Contains(
                    word,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesProvider(
        StreamInfo stream,
        string provider)
    {
        if (string.IsNullOrWhiteSpace(provider) ||
            string.Equals(
                provider,
                "Any provider",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(
            stream.Provider,
            provider,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesQuality(
        string text,
        string quality)
    {
        return quality switch
        {
            "2160p / 4K" =>
                ContainsWord(text, "2160p") ||
                ContainsWord(text, "4k"),

            "1080p" =>
                ContainsWord(text, "1080p"),

            "720p" =>
                ContainsWord(text, "720p"),

            // "First result" means no quality restriction.
            _ => true
        };
    }

    private static bool ContainsWord(
        string text,
        string word)
    {
        int index = 0;

        while ((index = text.IndexOf(
                   word,
                   index,
                   StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            bool validStart =
                index == 0 ||
                !char.IsLetterOrDigit(text[index - 1]);

            int end = index + word.Length;

            bool validEnd =
                end >= text.Length ||
                !char.IsLetterOrDigit(text[end]);

            if (validStart && validEnd)
                return true;

            index += word.Length;
        }

        return false;
    }

    private static string BuildStreamText(
        StreamInfo stream)
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

    private sealed class Candidate
    {
        public required StreamInfo Stream { get; init; }

        public int Position { get; init; }

        public int Cached { get; init; }

        public int BingeMatch { get; init; }
    }

    private sealed record SelectionTier(
        string Quality,
        string Contains,
        string Provider,
        int MinimumSeeders,
        string Name);
}

public sealed record StreamSelection(
    StreamInfo Stream,
    int FallbackTier,
    string FallbackName)
{
    public bool UsedFallback => FallbackTier > 1;
}