using StremioMPVRelay.Services;

namespace StremioMPVRelay;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var settingsService =
            new SettingsService();

        var libraryService =
            new LibraryService();

        using var cinemetaService =
            new CinemetaService();

        using var addonService =
            new StremioAddonService();

        var streamSelector =
            new StreamSelector();

        var mpvService =
            new MpvService();

        var rollingQueueService =
            new RollingQueueService(
                addonService,
                streamSelector,
                mpvService);

        try
        {
            Application.Run(
                new MainForm(
                    settingsService,
                    libraryService,
                    cinemetaService,
                    mpvService,
                    rollingQueueService));
        }
        finally
        {
            rollingQueueService
                .DisposeAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult();

            mpvService
                .DisposeAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
    }
}
