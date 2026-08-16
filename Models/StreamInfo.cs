namespace StremioMPVRelay.Models;

public sealed class StreamInfo
{
    public string Name { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string Quality { get; set; } = string.Empty;

    public int Seeders { get; set; }

    public string BingeGroup { get; set; } = string.Empty;

    public string InfoHash { get; set; } = string.Empty;

    public int FileIndex { get; set; } = -1;
    
    public int OriginalPosition { get; set; }

    public bool IsCached { get; set; }
}