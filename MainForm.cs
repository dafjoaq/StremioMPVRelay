using System.Text.RegularExpressions;
using StremioMPVRelay.Models;
using StremioMPVRelay.Services;

namespace StremioMPVRelay;

public partial class MainForm : Form
{
    
    private readonly SettingsService _settingsService;
    private readonly LibraryService _libraryService;
    private readonly CinemetaService _cinemetaService;
    private readonly MpvService _mpvService;
    private readonly RollingQueueService _rollingQueueService;
    private readonly SemaphoreSlim _metadataLookupGate = new(1, 1);

    private AppSettings _settings = new();
    private CinemetaSeriesMetadata? _cinemetaMetadata;

    private bool _loading;
    private bool _loadingLibrary;
    private bool _applyingLibrary;
    private bool _applyingMetadata;
    private bool _closing;

    public MainForm(
        SettingsService settingsService,
        LibraryService libraryService,
        CinemetaService cinemetaService,
        MpvService mpvService,
        RollingQueueService rollingQueueService)
    {
        _settingsService = settingsService;
        _libraryService = libraryService;
        _cinemetaService = cinemetaService;
        _mpvService = mpvService;
        _rollingQueueService = rollingQueueService;

        InitializeComponent();

        cmbLibrary.SelectedIndexChanged +=
            cmbLibrary_SelectedIndexChanged;

        btnRefreshLibrary.Click +=
            btnRefreshLibrary_Click;

        txtImdbId.Leave +=
            txtImdbId_Leave;

        txtImdbId.KeyDown +=
            txtImdbId_KeyDown;

        numSeason.ValueChanged +=
            numSeason_ValueChanged;

        SubscribeToEvents();

        Shown += MainForm_Shown;
        FormClosing += MainForm_FormClosing;
    }

    private async void MainForm_Shown(
        object? sender,
        EventArgs e)
    {
        try
        {
            _loading = true;

            _settings =
                await _settingsService.LoadAsync();

            LoadSettingsIntoControls();

            await RefreshLibraryAsync();

            SetMpvStatus(
                _mpvService.IsConnected
                    ? "Connected"
                    : "Not connected");

            SetQueueStatus("Ready");
        }
        catch (Exception ex)
        {
            ShowError(
                "Could not load settings.",
                ex);
        }
        finally
        {
            _loading = false;
        }
    }

    private void LoadSettingsIntoControls()
    {
        txtManifest.Text =
            _settings.ManifestUrl ?? string.Empty;

        txtMpvPath.Text =
            _settings.MpvPath ?? string.Empty;

        txtContains.Text =
            _settings.Contains ?? string.Empty;

        cmbQuality.Text =
            string.IsNullOrWhiteSpace(_settings.Quality)
                ? "1080p"
                : _settings.Quality;

        cmbProvider.Text =
            string.IsNullOrWhiteSpace(_settings.Provider)
                ? "Any provider"
                : _settings.Provider;

        cmbRanking.Text =
            string.IsNullOrWhiteSpace(_settings.Ranking)
                ? "Smart (recommended)"
                : _settings.Ranking;

        numMinimumSeeders.Value =
            ClampDecimal(
                _settings.MinimumSeeders,
                numMinimumSeeders.Minimum,
                numMinimumSeeders.Maximum);

        numBufferAhead.Value =
            ClampDecimal(
                _settings.BufferAhead,
                numBufferAhead.Minimum,
                numBufferAhead.Maximum);

        numSeason.Value =
            ClampDecimal(
                _settings.Season,
                numSeason.Minimum,
                numSeason.Maximum);

        numFirstEpisode.Value =
            ClampDecimal(
                _settings.FirstEpisode,
                numFirstEpisode.Minimum,
                numFirstEpisode.Maximum);

        numLastEpisode.Value =
            ClampDecimal(
                _settings.LastEpisode,
                numLastEpisode.Minimum,
                numLastEpisode.Maximum);

        txtImdbId.Text =
            ExtractImdbId(
                _settings.Series);

        txtTitle.Text =
            ExtractTitle(
                _settings.Series);
    }

    private void ReadControlsIntoSettings()
    {
        _settings.ManifestUrl =
            txtManifest.Text.Trim();

        _settings.MpvPath =
            txtMpvPath.Text.Trim();

        _settings.Quality =
            cmbQuality.Text.Trim();

        _settings.Contains =
            txtContains.Text.Trim();

        _settings.Provider =
            string.IsNullOrWhiteSpace(cmbProvider.Text)
                ? "Any provider"
                : cmbProvider.Text.Trim();

        _settings.MinimumSeeders =
            decimal.ToInt32(
                numMinimumSeeders.Value);

        _settings.Ranking =
            string.IsNullOrWhiteSpace(cmbRanking.Text)
                ? "Smart (recommended)"
                : cmbRanking.Text.Trim();

        _settings.BufferAhead =
            decimal.ToInt32(
                numBufferAhead.Value);

        _settings.Season =
            decimal.ToInt32(
                numSeason.Value);

        _settings.FirstEpisode =
            decimal.ToInt32(
                numFirstEpisode.Value);

        _settings.LastEpisode =
            decimal.ToInt32(
                numLastEpisode.Value);

        string title =
            txtTitle.Text.Trim();

        string imdbId =
            txtImdbId.Text.Trim();

        _settings.Series =
            BuildSeriesSetting(
                title,
                imdbId,
                _settings.Season,
                _settings.FirstEpisode);
    }

    private async void btnConnectMpv_Click(
        object? sender,
        EventArgs e)
    {
        try
        {
            ToggleBusy(true);

            ReadControlsIntoSettings();

            await _settingsService.SaveAsync(
                _settings);

            await EnsureMpvConnectedAsync();
        }
        catch (Exception ex)
        {
            SetMpvStatus("Connection failed");

            ShowError(
                "Could not connect to MPV.",
                ex);
        }
        finally
        {
            ToggleBusy(false);
        }
    }

    private async void btnStart_Click(
        object? sender,
        EventArgs e)
    {
        try
        {
            ToggleBusy(true);

            await LookupImdbMetadataAsync(
                showErrors: false);

            ReadControlsIntoSettings();

            ValidateStartSettings();

            await _settingsService.SaveAsync(
                _settings);

            await EnsureMpvConnectedAsync();

            ClearLog();

            string imdbId =
                txtImdbId.Text.Trim();

            string title =
                txtTitle.Text.Trim();

            await _libraryService.GetOrCreateAsync(
                imdbId,
                title,
                _settings.Season,
                _settings.FirstEpisode,
                _settings.LastEpisode);

            await RefreshLibraryAsync();

            await _rollingQueueService.StartAsync(
                txtManifest.Text.Trim(),
                imdbId,
                title,
                _settings);

            btnStart.Enabled = false;
            btnStop.Enabled = true;
            btnRetry.Enabled = true;
        }
        catch (Exception ex)
        {
            ShowError(
                "Could not start playback.",
                ex);
        }
        finally
        {
            ToggleBusy(false);
        }
    }

    private async void btnStop_Click(
        object? sender,
        EventArgs e)
    {
        try
        {
            btnStop.Enabled = false;

            await _rollingQueueService.StopAsync();

            btnStart.Enabled = true;
            btnRetry.Enabled = false;
        }
        catch (Exception ex)
        {
            ShowError(
                "Could not stop the rolling queue.",
                ex);
        }
    }

    private async void btnRetry_Click(
        object? sender,
        EventArgs e)
    {
        try
        {
            await _rollingQueueService.RetryNowAsync();
        }
        catch (Exception ex)
        {
            ShowError(
                "Could not retry the stream.",
                ex);
        }
    }

    private void btnBrowseMpv_Click(
        object? sender,
        EventArgs e)
    {
        using var dialog =
            new OpenFileDialog
            {
                Title = "Select mpv.exe",
                Filter =
                    "MPV executable|mpv.exe|" +
                    "Executable files|*.exe|" +
                    "All files|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

        if (!string.IsNullOrWhiteSpace(
                txtMpvPath.Text))
        {
            try
            {
                string? directory =
                    Path.GetDirectoryName(
                        txtMpvPath.Text);

                if (!string.IsNullOrWhiteSpace(directory) &&
                    Directory.Exists(directory))
                {
                    dialog.InitialDirectory =
                        directory;
                }
            }
            catch
            {
                // Ignore malformed existing paths.
            }
        }

        if (dialog.ShowDialog(this) ==
            DialogResult.OK)
        {
            txtMpvPath.Text =
                dialog.FileName;
        }
    }

    private async Task EnsureMpvConnectedAsync()
    {
        if (_mpvService.IsConnected)
        {
            SetMpvStatus("Connected");
            return;
        }

        string mpvPath =
            txtMpvPath.Text.Trim();

        if (string.IsNullOrWhiteSpace(mpvPath))
        {
            throw new InvalidOperationException(
                "Select mpv.exe first.");
        }

        if (!File.Exists(mpvPath))
        {
            throw new FileNotFoundException(
                "mpv.exe does not exist at the selected path.",
                mpvPath);
        }

        string luaPath =
            Path.Combine(
                AppContext.BaseDirectory,
                "Resources",
                "stremio-mpv-speed-controls.lua");

        if (!File.Exists(luaPath))
        {
            throw new FileNotFoundException(
                "The MPV speed-control Lua script was not copied to the output folder.",
                luaPath);
        }

        SetMpvStatus("Starting MPV...");

        await _mpvService.StartAndConnectAsync(
            mpvPath,
            luaPath);

        SetMpvStatus("Connected");

        AddLog(
            "MPV IPC connected.");
    }

    private void ValidateStartSettings()
    {
        string manifest =
            txtManifest.Text.Trim();

        string imdbId =
            txtImdbId.Text.Trim();

        if (string.IsNullOrWhiteSpace(manifest))
        {
            throw new InvalidOperationException(
                "Enter the Stremio addon manifest URL.");
        }

        StremioAddonService.NormalizeManifestUrl(
            manifest);

        if (!Regex.IsMatch(
                imdbId,
                @"^tt\d+$",
                RegexOptions.IgnoreCase))
        {
            throw new InvalidOperationException(
                "IMDb ID must look like tt0202430.");
        }

        if (numLastEpisode.Value <
            numFirstEpisode.Value)
        {
            throw new InvalidOperationException(
                "Last episode cannot be before first episode.");
        }

        if (string.IsNullOrWhiteSpace(
                cmbQuality.Text))
        {
            throw new InvalidOperationException(
                "Select a quality.");
        }

        if (string.IsNullOrWhiteSpace(
                cmbRanking.Text))
        {
            throw new InvalidOperationException(
                "Select a ranking mode.");
        }
    }

    private void SubscribeToEvents()
    {
        _rollingQueueService.StatusChanged +=
            RollingQueueService_StatusChanged;

        _rollingQueueService.LogAdded +=
            RollingQueueService_LogAdded;

        _rollingQueueService.EpisodeAdded +=
            RollingQueueService_EpisodeAdded;

        _rollingQueueService.EpisodeChanged +=
            RollingQueueService_EpisodeChanged;

        _rollingQueueService.EpisodeRecovered +=
            RollingQueueService_EpisodeRecovered;

        _rollingQueueService.QueueFinished +=
            RollingQueueService_QueueFinished;

        _mpvService.Disconnected +=
            MpvService_Disconnected;

        _mpvService.Shutdown +=
            MpvService_Shutdown;
    }

    private void UnsubscribeFromEvents()
    {
        _rollingQueueService.StatusChanged -=
            RollingQueueService_StatusChanged;

        _rollingQueueService.LogAdded -=
            RollingQueueService_LogAdded;

        _rollingQueueService.EpisodeAdded -=
            RollingQueueService_EpisodeAdded;

        _rollingQueueService.EpisodeChanged -=
            RollingQueueService_EpisodeChanged;

        _rollingQueueService.EpisodeRecovered -=
            RollingQueueService_EpisodeRecovered;

        _rollingQueueService.QueueFinished -=
            RollingQueueService_QueueFinished;

        _mpvService.Disconnected -=
            MpvService_Disconnected;

        _mpvService.Shutdown -=
            MpvService_Shutdown;
    }

    private void RollingQueueService_StatusChanged(
        object? sender,
        RollingQueueStatusEventArgs e)
    {
        RunOnUiThread(
            () => SetQueueStatus(
                e.Message));
    }

    private void RollingQueueService_LogAdded(
        object? sender,
        RollingQueueLogEventArgs e)
    {
        RunOnUiThread(
            () => AddLog(
                e.Timestamp.ToLocalTime()
                    .ToString("HH:mm:ss") +
                "  " +
                e.Message));
    }

    private void RollingQueueService_EpisodeAdded(
        object? sender,
        RollingQueueEntryEventArgs e)
    {
        RunOnUiThread(
            () =>
            {
                SetQueueStatus(
                    "Queued S" +
                    _settings.Season +
                    " E" +
                    e.Entry.Episode);

                UpdateWindowTitle();
            });
    }

    private void RollingQueueService_EpisodeChanged(
        object? sender,
        RollingQueueEntryEventArgs e)
    {
        RunOnUiThread(
            () =>
            {
                lblNowPlaying.Text =
                    "Now playing: S" +
                    _settings.Season +
                    " E" +
                    e.Entry.Episode;

                UpdateWindowTitle();

                _ = UpdateLibraryCurrentEpisodeSafeAsync(
                    e.Entry.Episode);
            });
    }

    private void RollingQueueService_EpisodeRecovered(
        object? sender,
        RollingQueueEntryEventArgs e)
    {
        RunOnUiThread(
            () =>
            {
                lblNowPlaying.Text =
                    "Recovered: S" +
                    _settings.Season +
                    " E" +
                    e.Entry.Episode;
            });
    }

    private void RollingQueueService_QueueFinished(
        object? sender,
        RollingQueueFinishedEventArgs e)
    {
        RunOnUiThread(
            () =>
            {
                SetQueueStatus(e.Reason);

                btnStart.Enabled = true;
                btnStop.Enabled = false;
                btnRetry.Enabled = false;

                _ = RefreshLibrarySafeAsync();
            });
    }

    private void MpvService_Disconnected(
        object? sender,
        EventArgs e)
    {
        RunOnUiThread(
            () =>
            {
                SetMpvStatus("Disconnected");

                btnStart.Enabled = true;
                btnStop.Enabled = false;
                btnRetry.Enabled = false;
            });
    }

    private void MpvService_Shutdown(
        object? sender,
        EventArgs e)
    {
        RunOnUiThread(
            () =>
            {
                SetMpvStatus("MPV closed");

                btnStart.Enabled = true;
                btnStop.Enabled = false;
                btnRetry.Enabled = false;
            });
    }

    private async void txtImdbId_Leave(
        object? sender,
        EventArgs e)
    {
        await LookupImdbMetadataAsync(
            showErrors: false);
    }

    private async void txtImdbId_KeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
            return;

        e.SuppressKeyPress = true;
        e.Handled = true;

        await LookupImdbMetadataAsync(
            showErrors: true);
    }

    private async void numSeason_ValueChanged(
        object? sender,
        EventArgs e)
    {
        if (_loading ||
            _loadingLibrary ||
            _applyingLibrary ||
            _applyingMetadata ||
            _closing)
        {
            return;
        }

        string imdbId =
            txtImdbId.Text.Trim();

        if (!Regex.IsMatch(
                imdbId,
                @"^tt\d+$",
                RegexOptions.IgnoreCase))
        {
            return;
        }

        if (_cinemetaMetadata is not null &&
            string.Equals(
                _cinemetaMetadata.ImdbId,
                imdbId,
                StringComparison.OrdinalIgnoreCase))
        {
            await ApplyMetadataForCurrentSeasonAsync(
                _cinemetaMetadata);

            return;
        }

        await LookupImdbMetadataAsync(
            showErrors: false);
    }

    private async Task LookupImdbMetadataAsync(
        bool showErrors)
    {
        if (_loading ||
            _loadingLibrary ||
            _applyingLibrary ||
            _closing)
        {
            return;
        }

        string requestedImdbId =
            txtImdbId.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                requestedImdbId))
        {
            return;
        }

        if (!Regex.IsMatch(
                requestedImdbId,
                @"^tt\d+$",
                RegexOptions.IgnoreCase))
        {
            if (showErrors)
            {
                MessageBox.Show(
                    this,
                    "IMDb ID must look like tt0202430.",
                    "StremioMPVRelay",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            return;
        }

        await _metadataLookupGate.WaitAsync();

        try
        {
            if (_closing)
                return;

            string currentImdbId =
                txtImdbId.Text.Trim();

            if (!string.Equals(
                    requestedImdbId,
                    currentImdbId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_cinemetaMetadata is null ||
                !string.Equals(
                    _cinemetaMetadata.ImdbId,
                    currentImdbId,
                    StringComparison.OrdinalIgnoreCase))
            {
                AddLog(
                    "Looking up " +
                    currentImdbId +
                    "...");

                _cinemetaMetadata =
                    await _cinemetaService
                        .GetSeriesMetadataAsync(
                            currentImdbId);
            }

            _applyingMetadata = true;

            try
            {
                txtTitle.Text =
                    _cinemetaMetadata.Title;

                int selectedSeason =
                    decimal.ToInt32(
                        numSeason.Value);

                if (!_cinemetaMetadata.EpisodeCounts.ContainsKey(
                        selectedSeason))
                {
                    int firstAvailableSeason =
                        _cinemetaMetadata.EpisodeCounts.Keys
                            .OrderBy(x => x)
                            .First();

                    numSeason.Value =
                        ClampDecimal(
                            firstAvailableSeason,
                            numSeason.Minimum,
                            numSeason.Maximum);
                }

                await ApplyMetadataForCurrentSeasonAsync(
                    _cinemetaMetadata);
            }
            finally
            {
                _applyingMetadata = false;
            }

            AddLog(
                "Detected: " +
                _cinemetaMetadata.Title);
        }
        catch (Exception ex)
        {
            if (showErrors)
            {
                ShowError(
                    "Could not look up the IMDb ID.",
                    ex);
            }
            else
            {
                AddLog(
                    "IMDb lookup failed: " +
                    ex.Message);
            }
        }
        finally
        {
            _metadataLookupGate.Release();
        }
    }

    private async Task ApplyMetadataForCurrentSeasonAsync(
        CinemetaSeriesMetadata metadata)
    {
        int season =
            decimal.ToInt32(
                numSeason.Value);

        if (!metadata.EpisodeCounts.TryGetValue(
                season,
                out int lastEpisode))
        {
            return;
        }

        SeriesEntry? libraryEntry =
            await _libraryService.FindAsync(
                metadata.ImdbId,
                season);

        int firstEpisode =
            libraryEntry?.CurrentEpisode ?? 1;

        firstEpisode =
            Math.Clamp(
                firstEpisode,
                1,
                lastEpisode);

        txtTitle.Text =
            metadata.Title;

        numFirstEpisode.Value =
            ClampDecimal(
                firstEpisode,
                numFirstEpisode.Minimum,
                numFirstEpisode.Maximum);

        numLastEpisode.Value =
            ClampDecimal(
                lastEpisode,
                numLastEpisode.Minimum,
                numLastEpisode.Maximum);
    }

    private async Task RefreshLibraryAsync()
    {
        _loadingLibrary = true;

        try
        {
            string currentImdbId =
                txtImdbId.Text.Trim();

            int currentSeason =
                decimal.ToInt32(
                    numSeason.Value);

            var series =
                await _libraryService.GetSeriesAsync();

            cmbLibrary.BeginUpdate();

            try
            {
                cmbLibrary.Items.Clear();

                int selectedIndex = -1;

                for (int i = 0; i < series.Count; i++)
                {
                    var item =
                        new LibraryComboItem(
                            series[i]);

                    cmbLibrary.Items.Add(item);

                    if (selectedIndex < 0 &&
                        string.Equals(
                            series[i].ImdbId,
                            currentImdbId,
                            StringComparison.OrdinalIgnoreCase) &&
                        series[i].Season == currentSeason)
                    {
                        selectedIndex = i;
                    }
                }

                if (selectedIndex >= 0)
                {
                    cmbLibrary.SelectedIndex =
                        selectedIndex;
                }
                else
                {
                    cmbLibrary.SelectedIndex = -1;
                }
            }
            finally
            {
                cmbLibrary.EndUpdate();
            }
        }
        finally
        {
            _loadingLibrary = false;
        }
    }

    private async Task RefreshLibrarySafeAsync()
    {
        try
        {
            await RefreshLibraryAsync();
        }
        catch (Exception ex)
        {
            AddLog(
                "Could not refresh library: " +
                ex.Message);
        }
    }

    private void cmbLibrary_SelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        if (_loading ||
            _loadingLibrary)
        {
            return;
        }

        if (cmbLibrary.SelectedItem is not
            LibraryComboItem item)
        {
            return;
        }

        ApplyLibraryEntry(
            item.Entry);
    }

    private async void btnRefreshLibrary_Click(
        object? sender,
        EventArgs e)
    {
        try
        {
            btnRefreshLibrary.Enabled = false;

            await RefreshLibraryAsync();
        }
        catch (Exception ex)
        {
            ShowError(
                "Could not refresh the library.",
                ex);
        }
        finally
        {
            btnRefreshLibrary.Enabled = true;
        }
    }

    private void ApplyLibraryEntry(
        SeriesEntry entry)
    {
        _applyingLibrary = true;

        try
        {
            txtImdbId.Text =
                entry.ImdbId;

            txtTitle.Text =
                entry.Title;

            numSeason.Value =
                ClampDecimal(
                    entry.Season,
                    numSeason.Minimum,
                    numSeason.Maximum);

            numFirstEpisode.Value =
                ClampDecimal(
                    entry.CurrentEpisode,
                    numFirstEpisode.Minimum,
                    numFirstEpisode.Maximum);

            numLastEpisode.Value =
                ClampDecimal(
                    entry.LastEpisode,
                    numLastEpisode.Minimum,
                    numLastEpisode.Maximum);

            AddLog(
                "Loaded from history: " +
                entry.Title +
                " S" +
                entry.Season +
                " E" +
                entry.CurrentEpisode);
        }
        finally
        {
            _applyingLibrary = false;
        }
    }

    private async Task UpdateLibraryCurrentEpisodeSafeAsync(
        int episode)
    {
        try
        {
            string imdbId =
                txtImdbId.Text.Trim();

            if (string.IsNullOrWhiteSpace(imdbId))
                return;

            string title =
                txtTitle.Text.Trim();

            await _libraryService.UpdateCurrentEpisodeAsync(
                imdbId,
                title,
                _settings.Season,
                episode,
                _settings.LastEpisode);

            await RefreshLibraryAsync();
        }
        catch (Exception ex)
        {
            AddLog(
                "Could not update library: " +
                ex.Message);
        }
    }

    private sealed class LibraryComboItem
    {
        public LibraryComboItem(
            SeriesEntry entry)
        {
            Entry = entry;
        }

        public SeriesEntry Entry { get; }

        public override string ToString()
        {
            return
                Entry.Title +
                " | S" +
                Entry.Season +
                " E" +
                Entry.CurrentEpisode +
                "/" +
                Entry.LastEpisode;
        }
    }

    private void ToggleBusy(bool busy)
    {
        UseWaitCursor = busy;

        btnConnectMpv.Enabled =
            !busy;

        btnBrowseMpv.Enabled =
            !busy;

        cmbLibrary.Enabled =
            !busy;

        btnRefreshLibrary.Enabled =
            !busy;

        if (!_rollingQueueService.IsActive)
        {
            btnStart.Enabled =
                !busy;
        }
    }

    private void SetMpvStatus(
        string status)
    {
        lblMpvStatus.Text =
            "MPV: " +
            status;
    }

    private void SetQueueStatus(
        string status)
    {
        lblQueueStatus.Text =
            "Queue: " +
            status;
    }

    private void AddLog(
        string message)
    {
        if (lstLog.Items.Count >= 1000)
        {
            lstLog.Items.RemoveAt(0);
        }

        lstLog.Items.Add(message);

        if (lstLog.Items.Count > 0)
        {
            lstLog.TopIndex =
                lstLog.Items.Count - 1;
        }
    }

    private void ClearLog()
    {
        lstLog.Items.Clear();
    }

    private void UpdateWindowTitle()
    {
        if (!_rollingQueueService.IsActive)
        {
            Text = "StremioMPVRelay";
            return;
        }

        Text =
            "StremioMPVRelay - S" +
            _rollingQueueService.Season +
            " E" +
            _rollingQueueService.CurrentEpisode;
    }

    private void RunOnUiThread(
        Action action)
    {
        if (_closing ||
            IsDisposed ||
            Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        action();
    }

    private static decimal ClampDecimal(
        int value,
        decimal minimum,
        decimal maximum)
    {
        return Math.Clamp(
            (decimal)value,
            minimum,
            maximum);
    }

    private static string ExtractImdbId(
        string? series)
    {
        if (string.IsNullOrWhiteSpace(series))
            return string.Empty;

        Match match =
            Regex.Match(
                series,
                @"\btt\d+\b",
                RegexOptions.IgnoreCase);

        return match.Success
            ? match.Value
            : string.Empty;
    }

    private static string ExtractTitle(
        string? series)
    {
        if (string.IsNullOrWhiteSpace(series))
            return string.Empty;

        string value =
            Regex.Replace(
                series,
                @"\s*-\s*S\d+E\d+\s*-\s*tt\d+\s*$",
                string.Empty,
                RegexOptions.IgnoreCase);

        return value.Trim();
    }

    private static string BuildSeriesSetting(
        string title,
        string imdbId,
        int season,
        int episode)
    {
        if (string.IsNullOrWhiteSpace(title))
            title = imdbId;

        return
            title +
            " - S" +
            season.ToString("00") +
            "E" +
            episode.ToString("00") +
            " - " +
            imdbId;
    }

    private void ShowError(
        string message,
        Exception exception)
    {
        MessageBox.Show(
            this,
            message +
            Environment.NewLine +
            Environment.NewLine +
            exception.Message,
            "StremioMPVRelay",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private async void MainForm_FormClosing(
        object? sender,
        FormClosingEventArgs e)
    {
        if (_closing)
            return;

        _closing = true;

        try
        {
            if (!_loading)
            {
                ReadControlsIntoSettings();

                await _settingsService.SaveAsync(
                    _settings);
            }
        }
        catch
        {
            // Do not prevent the application from closing
            // because settings could not be saved.
        }

        UnsubscribeFromEvents();
    }
}
