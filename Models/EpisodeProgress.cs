namespace StremioMPVRelay.Models;

public sealed class EpisodeProgress
{
    public double PositionSeconds { get; set; }

    public double DurationSeconds { get; set; }

    public bool Completed { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}