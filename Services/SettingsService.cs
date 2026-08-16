using System.Security.Cryptography;
using System.Text;
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
            var settings =
                await AtomicJsonFile.ReadAsync<AppSettings>(
                    _settingsPath,
                    cancellationToken)
                ?? new AppSettings();

            if (!settings.RememberManifest)
            {
                settings.ManifestUrl =
                    string.Empty;

                return settings;
            }

            if (!string.IsNullOrWhiteSpace(
                    settings.ManifestProtected))
            {
                try
                {
                    settings.ManifestUrl =
                        UnprotectManifest(
                            settings.ManifestProtected);
                }
                catch
                {
                    // Keep the rest of the settings even if an old
                    // protected manifest cannot be decrypted.
                    settings.ManifestUrl =
                        string.Empty;
                }
            }

            // If ManifestProtected is empty, leave ManifestUrl alone.
            // This lets older plaintext settings migrate on the next save.
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string manifestUrl =
            settings.ManifestUrl;

        if (settings.RememberManifest &&
            !string.IsNullOrWhiteSpace(
                manifestUrl))
        {
            settings.ManifestProtected =
                ProtectManifest(
                    manifestUrl);
        }
        else
        {
            settings.ManifestProtected =
                string.Empty;
        }

        // Never write the manifest URL to disk in plaintext.
        settings.ManifestUrl =
            string.Empty;

        try
        {
            await AtomicJsonFile.WriteAsync(
                _settingsPath,
                settings,
                cancellationToken);
        }
        finally
        {
            // Keep it available to the running UI.
            settings.ManifestUrl =
                manifestUrl;
        }
    }

    public bool Exists()
    {
        return File.Exists(
            _settingsPath);
    }

    public bool MpvExists(
        AppSettings settings)
    {
        return !string.IsNullOrWhiteSpace(
                   settings.MpvPath)
               && File.Exists(
                   settings.MpvPath);
    }

    private static string ProtectManifest(
        string manifestUrl)
    {
        byte[] plainBytes =
            Encoding.UTF8.GetBytes(
                manifestUrl);

        byte[] protectedBytes =
            ProtectedData.Protect(
                plainBytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);

        return Convert.ToBase64String(
            protectedBytes);
    }

    private static string UnprotectManifest(
        string protectedManifest)
    {
        byte[] protectedBytes =
            Convert.FromBase64String(
                protectedManifest);

        byte[] plainBytes =
            ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);

        return Encoding.UTF8.GetString(
            plainBytes);
    }
}