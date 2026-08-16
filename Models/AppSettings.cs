namespace StremioMPVRelay.Models;

public sealed class AppSettings
{
    public int Version { get; set; } = 2;

    public bool RememberManifest { get; set; } = true;

    public string ManifestProtected { get; set; } = string.Empty;

    public string ManifestUrl { get; set; } = string.Empty;

    public string Quality { get; set; } = "1080p";

    public string Contains { get; set; } = string.Empty;

    public string Provider { get; set; } = "Any provider";

    public int MinimumSeeders { get; set; } = 0;

    public string Ranking { get; set; } = "Smart (recommended)";

    public int BufferAhead { get; set; } = 2;

    public string Series { get; set; } = string.Empty;

    public int Season { get; set; } = 1;

    public int FirstEpisode { get; set; } = 1;

    public int LastEpisode { get; set; } = 1;

    public string MpvPath { get; set; } = string.Empty;
}