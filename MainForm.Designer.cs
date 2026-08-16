#nullable enable

using System.Drawing;
using System.Windows.Forms;

namespace StremioMPVRelay;
partial class MainForm
{
    private System.ComponentModel.IContainer? components = null;

    private TextBox txtManifest = null!;
    private TextBox txtMpvPath = null!;
    private TextBox txtImdbId = null!;
    private TextBox txtTitle = null!;
    private TextBox txtContains = null!;

    private ComboBox cmbQuality = null!;
    private ComboBox cmbProvider = null!;
    private ComboBox cmbRanking = null!;
    private ComboBox cmbLibrary = null!;

    private NumericUpDown numSeason = null!;
    private NumericUpDown numFirstEpisode = null!;
    private NumericUpDown numLastEpisode = null!;
    private NumericUpDown numMinimumSeeders = null!;
    private NumericUpDown numBufferAhead = null!;

    private Button btnBrowseMpv = null!;
    private Button btnConnectMpv = null!;
    private Button btnStart = null!;
    private Button btnStop = null!;
    private Button btnRetry = null!;
    private Button btnRefreshLibrary = null!;

    private Label lblMpvStatus = null!;
    private Label lblQueueStatus = null!;
    private Label lblNowPlaying = null!;

    private ListBox lstLog = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        txtManifest = new TextBox();
        txtMpvPath = new TextBox();
        txtImdbId = new TextBox();
        txtTitle = new TextBox();
        txtContains = new TextBox();

        cmbQuality = new ComboBox();
        cmbProvider = new ComboBox();
        cmbRanking = new ComboBox();
        cmbLibrary = new ComboBox();

        numSeason = new NumericUpDown();
        numFirstEpisode = new NumericUpDown();
        numLastEpisode = new NumericUpDown();
        numMinimumSeeders = new NumericUpDown();
        numBufferAhead = new NumericUpDown();

        btnBrowseMpv = new Button();
        btnConnectMpv = new Button();
        btnStart = new Button();
        btnStop = new Button();
        btnRetry = new Button();
        btnRefreshLibrary = new Button();

        lblMpvStatus = new Label();
        lblQueueStatus = new Label();
        lblNowPlaying = new Label();

        lstLog = new ListBox();

        var lblManifest = new Label();
        var lblMpvPath = new Label();
        var lblImdbId = new Label();
        var lblTitle = new Label();
        var lblSeason = new Label();
        var lblFirstEpisode = new Label();
        var lblLastEpisode = new Label();
        var lblQuality = new Label();
        var lblProvider = new Label();
        var lblSeeders = new Label();
        var lblRanking = new Label();
        var lblContains = new Label();
        var lblBufferAhead = new Label();
        var lblLibrary = new Label();
        var lblLog = new Label();

        ((System.ComponentModel.ISupportInitialize)numSeason).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numFirstEpisode).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numLastEpisode).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numMinimumSeeders).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numBufferAhead).BeginInit();

        SuspendLayout();

        // Manifest
        lblManifest.AutoSize = true;
        lblManifest.Location = new Point(20, 23);
        lblManifest.Text = "Manifest URL";

        txtManifest.Anchor =
            AnchorStyles.Top |
            AnchorStyles.Left |
            AnchorStyles.Right;

        txtManifest.Location = new Point(125, 20);
        txtManifest.Size = new Size(720, 23);

        // MPV path
        lblMpvPath.AutoSize = true;
        lblMpvPath.Location = new Point(20, 58);
        lblMpvPath.Text = "MPV path";

        txtMpvPath.Anchor =
            AnchorStyles.Top |
            AnchorStyles.Left |
            AnchorStyles.Right;

        txtMpvPath.Location = new Point(125, 55);
        txtMpvPath.Size = new Size(590, 23);

        btnBrowseMpv.Anchor =
            AnchorStyles.Top |
            AnchorStyles.Right;

        btnBrowseMpv.Location = new Point(725, 54);
        btnBrowseMpv.Size = new Size(120, 25);
        btnBrowseMpv.Text = "Browse...";
        btnBrowseMpv.UseVisualStyleBackColor = true;
        btnBrowseMpv.Click += btnBrowseMpv_Click;

        // IMDb
        lblImdbId.AutoSize = true;
        lblImdbId.Location = new Point(20, 98);
        lblImdbId.Text = "IMDb ID";

        txtImdbId.Location = new Point(125, 95);
        txtImdbId.Size = new Size(180, 23);
        txtImdbId.PlaceholderText = "tt0202430";

        // Title
        lblTitle.AutoSize = true;
        lblTitle.Location = new Point(325, 98);
        lblTitle.Text = "Title";

        txtTitle.Anchor =
            AnchorStyles.Top |
            AnchorStyles.Left |
            AnchorStyles.Right;

        txtTitle.Location = new Point(370, 95);
        txtTitle.Size = new Size(475, 23);

        // Season
        lblSeason.AutoSize = true;
        lblSeason.Location = new Point(20, 138);
        lblSeason.Text = "Season";

        numSeason.Location = new Point(125, 135);
        numSeason.Minimum = 1;
        numSeason.Maximum = 999;
        numSeason.Value = 1;
        numSeason.Size = new Size(100, 23);

        // First episode
        lblFirstEpisode.AutoSize = true;
        lblFirstEpisode.Location = new Point(245, 138);
        lblFirstEpisode.Text = "First episode";

        numFirstEpisode.Location = new Point(335, 135);
        numFirstEpisode.Minimum = 1;
        numFirstEpisode.Maximum = 9999;
        numFirstEpisode.Value = 1;
        numFirstEpisode.Size = new Size(100, 23);

        // Last episode
        lblLastEpisode.AutoSize = true;
        lblLastEpisode.Location = new Point(455, 138);
        lblLastEpisode.Text = "Last episode";

        numLastEpisode.Location = new Point(545, 135);
        numLastEpisode.Minimum = 1;
        numLastEpisode.Maximum = 9999;
        numLastEpisode.Value = 1;
        numLastEpisode.Size = new Size(100, 23);

        // Buffer ahead
        lblBufferAhead.AutoSize = true;
        lblBufferAhead.Location = new Point(665, 138);
        lblBufferAhead.Text = "Buffer";

        numBufferAhead.Anchor =
            AnchorStyles.Top |
            AnchorStyles.Right;

        numBufferAhead.Location = new Point(725, 135);
        numBufferAhead.Minimum = 0;
        numBufferAhead.Maximum = 20;
        numBufferAhead.Value = 2;
        numBufferAhead.Size = new Size(120, 23);

        // Quality
        lblQuality.AutoSize = true;
        lblQuality.Location = new Point(20, 178);
        lblQuality.Text = "Quality";

        cmbQuality.DropDownStyle =
            ComboBoxStyle.DropDownList;

        cmbQuality.Items.AddRange(
        [
            "First result",
            "2160p / 4K",
            "1080p",
            "720p"
        ]);

        cmbQuality.Location = new Point(125, 175);
        cmbQuality.Size = new Size(180, 23);
        cmbQuality.SelectedIndex = 2;

        // Provider
        lblProvider.AutoSize = true;
        lblProvider.Location = new Point(325, 178);
        lblProvider.Text = "Provider";

        cmbProvider.DropDownStyle =
            ComboBoxStyle.DropDown;

        cmbProvider.Items.AddRange(
        [
            "Any provider"
        ]);

        cmbProvider.Location = new Point(390, 175);
        cmbProvider.Size = new Size(180, 23);
        cmbProvider.Text = "Any provider";

        // Minimum seeders
        lblSeeders.AutoSize = true;
        lblSeeders.Location = new Point(590, 178);
        lblSeeders.Text = "Min seeders";

        numMinimumSeeders.Anchor =
            AnchorStyles.Top |
            AnchorStyles.Right;

        numMinimumSeeders.Location = new Point(725, 175);
        numMinimumSeeders.Minimum = 0;
        numMinimumSeeders.Maximum = 1000000;
        numMinimumSeeders.Size = new Size(120, 23);

        // Ranking
        lblRanking.AutoSize = true;
        lblRanking.Location = new Point(20, 218);
        lblRanking.Text = "Ranking";

        cmbRanking.DropDownStyle =
            ComboBoxStyle.DropDownList;

        cmbRanking.Items.AddRange(
        [
            "Smart (recommended)",
            "Highest seeders",
            "First matching result"
        ]);

        cmbRanking.Location = new Point(125, 215);
        cmbRanking.Size = new Size(230, 23);
        cmbRanking.SelectedIndex = 0;

        // Contains
        lblContains.AutoSize = true;
        lblContains.Location = new Point(375, 218);
        lblContains.Text = "Contains";

        txtContains.Anchor =
            AnchorStyles.Top |
            AnchorStyles.Left |
            AnchorStyles.Right;

        txtContains.Location = new Point(440, 215);
        txtContains.Size = new Size(405, 23);
        txtContains.PlaceholderText =
            "Optional comma-separated required words";

        // Library / History
        lblLibrary.AutoSize = true;
        lblLibrary.Location = new Point(20, 258);
        lblLibrary.Text = "History";

        cmbLibrary.Anchor =
            AnchorStyles.Top |
            AnchorStyles.Left |
            AnchorStyles.Right;

        cmbLibrary.DropDownStyle =
            ComboBoxStyle.DropDownList;

        cmbLibrary.Location = new Point(125, 255);
        cmbLibrary.Size = new Size(590, 23);

        btnRefreshLibrary.Anchor =
            AnchorStyles.Top |
            AnchorStyles.Right;

        btnRefreshLibrary.Location = new Point(725, 254);
        btnRefreshLibrary.Size = new Size(120, 25);
        btnRefreshLibrary.Text = "Refresh";
        btnRefreshLibrary.UseVisualStyleBackColor = true;

        // MPV connect
        btnConnectMpv.Location = new Point(20, 300);
        btnConnectMpv.Size = new Size(150, 32);
        btnConnectMpv.Text = "Connect MPV";
        btnConnectMpv.UseVisualStyleBackColor = true;
        btnConnectMpv.Click += btnConnectMpv_Click;

        // Start
        btnStart.Location = new Point(185, 300);
        btnStart.Size = new Size(150, 32);
        btnStart.Text = "Start";
        btnStart.UseVisualStyleBackColor = true;
        btnStart.Click += btnStart_Click;

        // Stop
        btnStop.Location = new Point(350, 300);
        btnStop.Size = new Size(150, 32);
        btnStop.Text = "Stop";
        btnStop.Enabled = false;
        btnStop.UseVisualStyleBackColor = true;
        btnStop.Click += btnStop_Click;

        // Retry
        btnRetry.Location = new Point(515, 300);
        btnRetry.Size = new Size(150, 32);
        btnRetry.Text = "Retry Now";
        btnRetry.Enabled = false;
        btnRetry.UseVisualStyleBackColor = true;
        btnRetry.Click += btnRetry_Click;

        // MPV status
        lblMpvStatus.AutoSize = true;
        lblMpvStatus.Location = new Point(20, 355);
        lblMpvStatus.Text = "MPV: Not connected";

        // Queue status
        lblQueueStatus.Anchor =
            AnchorStyles.Top |
            AnchorStyles.Left |
            AnchorStyles.Right;

        lblQueueStatus.AutoEllipsis = true;
        lblQueueStatus.Location = new Point(20, 380);
        lblQueueStatus.Size = new Size(825, 20);
        lblQueueStatus.Text = "Queue: Ready";

        // Now playing
        lblNowPlaying.Anchor =
            AnchorStyles.Top |
            AnchorStyles.Left |
            AnchorStyles.Right;

        lblNowPlaying.AutoEllipsis = true;
        lblNowPlaying.Font =
            new Font(
                Font,
                FontStyle.Bold);

        lblNowPlaying.Location = new Point(20, 405);
        lblNowPlaying.Size = new Size(825, 22);
        lblNowPlaying.Text = "Now playing: Nothing";

        // Log label
        lblLog.AutoSize = true;
        lblLog.Location = new Point(20, 440);
        lblLog.Text = "Log";

        // Log
        lstLog.Anchor =
            AnchorStyles.Top |
            AnchorStyles.Bottom |
            AnchorStyles.Left |
            AnchorStyles.Right;

        lstLog.Font =
            new Font(
                "Consolas",
                9F,
                FontStyle.Regular);

        lstLog.HorizontalScrollbar = true;
        lstLog.Location = new Point(20, 465);
        lstLog.Size = new Size(825, 230);

        // MainForm
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;

        ClientSize = new Size(865, 715);

        Controls.Add(lblManifest);
        Controls.Add(txtManifest);

        Controls.Add(lblMpvPath);
        Controls.Add(txtMpvPath);
        Controls.Add(btnBrowseMpv);

        Controls.Add(lblImdbId);
        Controls.Add(txtImdbId);

        Controls.Add(lblTitle);
        Controls.Add(txtTitle);

        Controls.Add(lblSeason);
        Controls.Add(numSeason);

        Controls.Add(lblFirstEpisode);
        Controls.Add(numFirstEpisode);

        Controls.Add(lblLastEpisode);
        Controls.Add(numLastEpisode);

        Controls.Add(lblBufferAhead);
        Controls.Add(numBufferAhead);

        Controls.Add(lblQuality);
        Controls.Add(cmbQuality);

        Controls.Add(lblProvider);
        Controls.Add(cmbProvider);

        Controls.Add(lblSeeders);
        Controls.Add(numMinimumSeeders);

        Controls.Add(lblRanking);
        Controls.Add(cmbRanking);

        Controls.Add(lblContains);
        Controls.Add(txtContains);

        Controls.Add(lblLibrary);
        Controls.Add(cmbLibrary);
        Controls.Add(btnRefreshLibrary);

        Controls.Add(btnConnectMpv);
        Controls.Add(btnStart);
        Controls.Add(btnStop);
        Controls.Add(btnRetry);

        Controls.Add(lblMpvStatus);
        Controls.Add(lblQueueStatus);
        Controls.Add(lblNowPlaying);

        Controls.Add(lblLog);
        Controls.Add(lstLog);

        MinimumSize = new Size(880, 640);

        StartPosition =
            FormStartPosition.CenterScreen;

        Text = "StremioMPVRelay";

        ((System.ComponentModel.ISupportInitialize)numSeason).EndInit();
        ((System.ComponentModel.ISupportInitialize)numFirstEpisode).EndInit();
        ((System.ComponentModel.ISupportInitialize)numLastEpisode).EndInit();
        ((System.ComponentModel.ISupportInitialize)numMinimumSeeders).EndInit();
        ((System.ComponentModel.ISupportInitialize)numBufferAhead).EndInit();

        ResumeLayout(false);
        PerformLayout();
    }
}