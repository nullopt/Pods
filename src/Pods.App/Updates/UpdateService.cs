using System.Diagnostics;
using System.Net.Http.Headers;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Pods.Core.Settings;
using Pods.Core.Updates;
using Serilog;

namespace Pods.App.Updates;

/// <summary>
/// Offers signed updates from GitHub Releases: about a minute after start and then daily (installed
/// copies only, while the setting is on) it looks for a newer release and shows a notification once
/// per version. Only when the user clicks Update does it download the manifest, its signature and the
/// installer, verify them against <see cref="TrustedKeys"/>, run the installer silently (it starts
/// Pods again) and exit. Any failure installs nothing.
/// </summary>
internal sealed class UpdateService(SettingsStore settings, DispatcherQueue ui, Action<AppNotification> notify, Action exitApp) : IDisposable
{
    const string LatestReleaseApi = "https://api.github.com/repos/nullopt/Pods/releases/latest";
    const long SmallFileCap = 64 * 1024;
    const long InstallerCap = 200L * 1024 * 1024;
    static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);
    static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    static readonly TimeSpan SmallTimeout = TimeSpan.FromSeconds(30);
    static readonly TimeSpan InstallerTimeout = TimeSpan.FromMinutes(10);
    static readonly string InstallFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Pods");

    static readonly HttpClient Http = CreateClient();

    readonly Version running = ReleaseParser.Normalize(typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0));
    readonly HashSet<Version> notified = [];
    DispatcherQueueTimer? timer;
    UpdateOffer? lastOffer;
    bool installing;

    /// <summary>True when this process runs from the installer's folder, so dev and Debug builds never update.</summary>
    public static bool IsInstalledCopy() =>
        string.Equals(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), InstallFolder, StringComparison.OrdinalIgnoreCase);

    public void Start()
    {
        if (!IsInstalledCopy())
        {
            Log.Information("Update checks off: not running from {Folder}", InstallFolder);
            return;
        }
        timer = ui.CreateTimer();
        timer.Interval = FirstCheckDelay;
        timer.Tick += (t, _) =>
        {
            t.Interval = CheckInterval;
            if (settings.Get(SettingKeys.CheckForUpdates)) _ = CheckAsync();
        };
        timer.Start();
    }

    public void Dispose()
    {
        timer?.Stop();
        timer = null;
    }

    /// <summary>Looks for a newer signed release and offers it once per version. Failures are logged only.</summary>
    public async Task CheckAsync()
    {
        var offer = await LatestOfferAsync();
        if (offer is null) return;
        ui.TryEnqueue(() =>
        {
            lastOffer = offer;
            if (!notified.Add(offer.Version)) return;
            Log.Information("Update available: {Version}", offer.Version.ToString(3));
            notify(new AppNotificationBuilder()
                .AddText(Strings.Get("UpdateAvailableTitle"))
                .AddText(string.Format(Strings.Get("UpdateAvailableBody"), offer.Version.ToString(3)))
                .AddArgument("action", "open")
                .AddButton(new AppNotificationButton(Strings.Get("UpdateButton"))
                    .AddArgument("action", "update").AddArgument("version", offer.Version.ToString(3)))
                .AddButton(new AppNotificationButton(Strings.Get("UpdateNotesButton")).AddArgument("action", "notes"))
                .BuildNotification());
        });
    }

    public void OpenReleasePage()
    {
        if (lastOffer is { } offer) _ = Windows.System.Launcher.LaunchUriAsync(offer.ReleasePage);
    }

    /// <summary>The user clicked Update for <paramref name="clicked"/>. Call on the UI thread.</summary>
    public async Task InstallAsync(Version clicked)
    {
        if (installing) return;
        installing = true;
        try
        {
            if (await LatestOfferAsync() is not { } offer || offer.Version != clicked)
                throw new UpdateException($"{clicked.ToString(3)} is no longer the latest signed release");
            notify(new AppNotificationBuilder()
                .AddText(string.Format(Strings.Get("UpdateDownloadingBody"), offer.Version.ToString(3)))
                .BuildNotification());

            var manifestBytes = await DownloadBytesAsync(offer.Manifest, SmallFileCap, SmallTimeout);
            var signature = await DownloadBytesAsync(offer.Signature, SmallFileCap, SmallTimeout);
            var manifest = UpdateVerifier.VerifyManifest(manifestBytes, signature, offer, running, TrustedKeys.All)
                ?? throw new UpdateException("the manifest is not signed by a trusted key or does not match the release");

            var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Pods", "update-" + Guid.NewGuid().ToString("N")));
            var path = Path.Combine(folder.FullName, manifest.Installer);
            await DownloadFileAsync(offer.Installer, path);

            // Held open without write or delete sharing from the hash check until the installer has started.
            using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!UpdateVerifier.InstallerMatches(held, manifest))
                throw new UpdateException("the installer does not match the signed SHA-256");
            Log.Information("Installing update {Version}", manifest.Version.ToString(3));
            using var process = Process.Start(new ProcessStartInfo(path, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LAUNCH=1")
            {
                UseShellExecute = false,
                WorkingDirectory = folder.FullName,
            }) ?? throw new UpdateException("the installer did not start");
            exitApp();
        }
        catch (Exception e) when (e is UpdateException or HttpRequestException or IOException or UnauthorizedAccessException
            or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            installing = false;
            Log.Warning(e, "Update to {Version} failed", clicked.ToString(3));
            notify(new AppNotificationBuilder()
                .AddText(Strings.Get("UpdateFailedTitle"))
                .AddText(string.Format(Strings.Get("UpdateFailedBody"), clicked.ToString(3)))
                .BuildNotification());
        }
    }

    async Task<UpdateOffer?> LatestOfferAsync()
    {
        try
        {
            var json = await DownloadBytesAsync(new Uri(LatestReleaseApi), SmallFileCap * 16, SmallTimeout);
            var offer = ReleaseParser.Parse(System.Text.Encoding.UTF8.GetString(json), running);
            if (offer is null) Log.Information("No newer signed release than {Version}", running.ToString(3));
            return offer;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException or UpdateException)
        {
            Log.Information("Update check failed: {Reason}", e.Message);
            return null;
        }
    }

    static async Task<byte[]> DownloadBytesAsync(Uri uri, long cap, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > cap) throw new UpdateException($"{uri} is larger than {cap} bytes");
        await using var source = await response.Content.ReadAsStreamAsync(cts.Token);
        using var buffer = new MemoryStream();
        await CopyCappedAsync(source, buffer, cap, cts.Token);
        return buffer.ToArray();
    }

    static async Task DownloadFileAsync(Uri uri, string path)
    {
        using var cts = new CancellationTokenSource(InstallerTimeout);
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > InstallerCap) throw new UpdateException("the installer is too large");
        await using var source = await response.Content.ReadAsStreamAsync(cts.Token);
        await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await CopyCappedAsync(source, target, InstallerCap, cts.Token);
    }

    static async Task CopyCappedAsync(Stream source, Stream target, long cap, CancellationToken token)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, token)) > 0)
        {
            total += read;
            if (total > cap) throw new UpdateException($"download is larger than {cap} bytes");
            await target.WriteAsync(buffer.AsMemory(0, read), token);
        }
    }

    static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Pods-updater", null));
        return client;
    }

    sealed class UpdateException(string message) : Exception(message);
}
