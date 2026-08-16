namespace StremioMPVRelay.Models;

public sealed class LibraryFile
{
    public int Version { get; set; } = 2;

    public List<SeriesEntry> Items { get; set; } = [];
}