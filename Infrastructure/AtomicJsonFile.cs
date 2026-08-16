using System.Text.Json;

namespace StremioMPVRelay.Infrastructure;

public static class AtomicJsonFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static async Task<T?> ReadAsync<T>(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            return default;

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous);

        return await JsonSerializer.DeserializeAsync<T>(
            stream,
            JsonOptions,
            cancellationToken);
    }

    public static async Task WriteAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";

        await using (var stream = new FileStream(
                         tempPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         4096,
                         FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                value,
                JsonOptions,
                cancellationToken);

            await stream.FlushAsync(cancellationToken);
        }

        File.Move(
            tempPath,
            path,
            overwrite: true);
    }
}