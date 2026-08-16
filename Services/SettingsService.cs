using StremioMPVRelay.Infrastructure;
using StremioMPVRelay.Models;

namespace StremioMPVRelay.Services;

public sealed class SettingsService
{
    private readonly string _settingsPath;

    public SettingsService()
    {
        _settingsPath = Path.Combine(
            AppContext.BaseDirectory,
            "StremioMPVRelay.settings.json");
    }

    public string SettingsPath => _settingsPath;

    public async Task<AppSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = await AtomicJsonFile.ReadAsync<AppSettings>(
                _settingsPath,
                cancellationToken);

            return settings ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return AtomicJsonFile.WriteAsync(
            _settingsPath,
            settings,
            cancellationToken);
    }

    public bool Exists()
    {
        return File.Exists(_settingsPath);
    }

    public bool MpvExists(AppSettings settings)
    {
        return !string.IsNullOrWhiteSpace(settings.MpvPath)
               && File.Exists(settings.MpvPath);
    }
}