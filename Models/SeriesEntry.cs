namespace StremioMPVRelay.Models;

public sealed class SeriesEntry
{
    public string ImdbId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int Season { get; set; } = 1;

    public int CurrentEpisode { get; set; } = 1;

    public int LastEpisode { get; set; } = 1;

    public Dictionary<string, EpisodeProgress> Progress { get; set; } = [];

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}