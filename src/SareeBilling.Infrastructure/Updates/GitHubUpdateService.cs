using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SareeBilling.Application.Abstractions;
using SareeBilling.Application.Backups;
using SareeBilling.Application.Updates;

namespace SareeBilling.Infrastructure.Updates;

/// <summary>
/// Reads the latest release of a public GitHub repository (no token needed) and downloads
/// its installer. State such as "remind me later" is kept in update-state.json next to the
/// data folder, not in the database.
/// </summary>
internal sealed class GitHubUpdateService : IUpdateService
{
    private const string InstallerPrefix = "SareeBilling-Setup-";
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly UpdateSettings _settings;
    private readonly IAppPaths _paths;
    private readonly IBackupService _backups;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GitHubUpdateService> _logger;
    private readonly HttpClient _http;
    private readonly object _stateLock = new();

    public GitHubUpdateService(
        UpdateSettings settings,
        IAppPaths paths,
        IBackupService backups,
        TimeProvider timeProvider,
        ILogger<GitHubUpdateService> logger)
        : this(settings, paths, backups, timeProvider, logger, new HttpClientHandler())
    {
    }

    /// <summary>Test constructor: lets tests replace the network.</summary>
    internal GitHubUpdateService(
        UpdateSettings settings,
        IAppPaths paths,
        IBackupService backups,
        TimeProvider timeProvider,
        ILogger<GitHubUpdateService> logger,
        HttpMessageHandler handler)
    {
        _settings = settings;
        _paths = paths;
        _backups = backups;
        _timeProvider = timeProvider;
        _logger = logger;

        // Timeouts are applied per request (short for checks, long for downloads).
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        // GitHub requires a User-Agent; it identifies the app only, never the user.
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SareeBilling-Updater", Normalize(settings.CurrentVersion).ToString()));
    }

    public Version CurrentVersion => Normalize(_settings.CurrentVersion);

    private string UpdatesDirectory => Path.Combine(_paths.RootDirectory, "Updates");
    private string StatePath => Path.Combine(_paths.RootDirectory, "update-state.json");

    // ------------------------------------------------------------------ check

    public async Task<ReleaseInfo?> FindNewerReleaseAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CheckTimeout);

        var url = $"https://api.github.com/repos/{_settings.Owner}/{_settings.Repository}/releases/latest";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new UpdateException("Could not reach the update server. Please check the internet connection.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // No release published yet (or the repository does not exist).
                _logger.LogInformation("No releases found at {Url}", url);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateException($"The update server answered with an error ({(int)response.StatusCode}). Please try again later.");
            }

            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            var release = ParseRelease(json);

            if (release is null || release.Version <= CurrentVersion)
            {
                return null;
            }

            _logger.LogInformation("Update available: {Latest} (running {Current})", release.Version, CurrentVersion);
            return release;
        }
    }

    /// <summary>Turns the GitHub "latest release" JSON into a ReleaseInfo; null if it has no usable installer.</summary>
    internal ReleaseInfo? ParseRelease(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (GetBool(root, "draft") || GetBool(root, "prerelease"))
            {
                return null;
            }

            var tag = GetString(root, "tag_name") ?? string.Empty;
            if (!TryParseVersion(tag, out var version))
            {
                _logger.LogWarning("Ignoring release with unrecognised tag {Tag}", tag);
                return null;
            }

            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var asset in assets.EnumerateArray())
            {
                var name = GetString(asset, "name") ?? string.Empty;
                var download = GetString(asset, "browser_download_url");
                if (!name.StartsWith(InstallerPrefix, StringComparison.OrdinalIgnoreCase)
                    || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || !Uri.TryCreate(download, UriKind.Absolute, out var downloadUri)
                    || !IsTrustedDownloadHost(downloadUri))
                {
                    continue;
                }

                var digest = GetString(asset, "digest");
                var sha256 = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                    ? digest["sha256:".Length..].ToLowerInvariant()
                    : null;

                return new ReleaseInfo(
                    version,
                    tag,
                    GetString(root, "name") is { Length: > 0 } title ? title : $"Saree Billing {version}",
                    (GetString(root, "body") ?? string.Empty).Trim(),
                    DateTimeOffset.TryParse(GetString(root, "published_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var published) ? published : null,
                    Path.GetFileName(name),
                    downloadUri,
                    asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0,
                    sha256,
                    Uri.TryCreate(GetString(root, "html_url"), UriKind.Absolute, out var page) ? page : null);
            }

            _logger.LogWarning("Release {Tag} has no {Prefix}*.exe installer attached", tag, InstallerPrefix);
            return null;
        }
        catch (JsonException ex)
        {
            throw new UpdateException("The update server sent an unexpected reply.", ex);
        }
    }

    // ------------------------------------------------------------------ snooze

    public bool IsSnoozed(ReleaseInfo release)
    {
        var state = ReadState();
        return state.SnoozedVersion == release.Version.ToString()
               && state.SnoozedUntilUtc is { } until
               && until > _timeProvider.GetUtcNow();
    }

    public void Snooze(ReleaseInfo release, TimeSpan duration) =>
        ChangeState(s => s with
        {
            SnoozedVersion = release.Version.ToString(),
            SnoozedUntilUtc = _timeProvider.GetUtcNow() + duration
        });

    // ------------------------------------------------------------------ download

    public async Task<string> DownloadInstallerAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        if (!IsTrustedDownloadHost(release.InstallerUrl))
        {
            throw new UpdateException("The update download address is not trusted.");
        }

        Directory.CreateDirectory(UpdatesDirectory);
        var target = Path.Combine(UpdatesDirectory, Path.GetFileName(release.InstallerFileName));
        var partial = target + ".download";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DownloadTimeout);

        try
        {
            using (var response = await _http.GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
            {
                response.EnsureSuccessStatusCode();
                var total = release.InstallerSizeBytes > 0 ? release.InstallerSizeBytes : response.Content.Headers.ContentLength ?? 0;

                await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
                await using var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

                var buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                    written += read;
                    if (total > 0)
                    {
                        progress?.Report(Math.Min(100, written * 100.0 / total));
                    }
                }

                if (release.InstallerSizeBytes > 0 && written != release.InstallerSizeBytes)
                {
                    throw new UpdateException("The download was incomplete. Please try again.");
                }
            }

            await VerifyInstallerAsync(partial, release, timeout.Token);
            File.Move(partial, target, overwrite: true);
            progress?.Report(100);
            _logger.LogInformation("Downloaded and verified {Installer}", target);
            return target;
        }
        catch (Exception ex) when (ex is not UpdateException)
        {
            TryDelete(partial);
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Download cancelled.", ex, cancellationToken);
            }

            throw new UpdateException("The update could not be downloaded. Please check the internet connection and try again.", ex);
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    private static async Task VerifyInstallerAsync(string path, ReleaseInfo release, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);

        // Every Windows program starts with "MZ"; anything else (an error page, a broken file) is rejected.
        var header = new byte[2];
        if (await stream.ReadAsync(header, cancellationToken) != 2 || header[0] != (byte)'M' || header[1] != (byte)'Z')
        {
            throw new UpdateException("The downloaded file is not a valid installer.");
        }

        if (release.InstallerSha256 is { } expected)
        {
            stream.Position = 0;
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateException("The downloaded installer failed its security check (fingerprint mismatch). It was not installed.");
            }
        }
    }

    // ------------------------------------------------------------------ install bookkeeping

    public async Task<string> PrepareForInstallAsync(ReleaseInfo release, CancellationToken cancellationToken = default)
    {
        var backup = await _backups.CreateSafetyCopyAsync($"before-update-{release.Version}", cancellationToken);
        ChangeState(s => s with { PendingVersion = release.Version.ToString(), PendingStartedUtc = _timeProvider.GetUtcNow() });
        return backup ?? string.Empty;
    }

    public void ClearPendingUpdate() =>
        ChangeState(s => s with { PendingVersion = null, PendingStartedUtc = null });

    public string GetInstallerLogPath(ReleaseInfo release) =>
        Path.Combine(_paths.LogDirectory, $"update-{release.Version}.log");

    public PreviousUpdateResult? TakePreviousUpdateResult()
    {
        var state = ReadState();
        if (state.PendingVersion is null || !Version.TryParse(state.PendingVersion, out var target))
        {
            return null;
        }

        ClearPendingUpdate();
        var succeeded = CurrentVersion >= Normalize(target);

        if (succeeded)
        {
            // The installers are no longer needed once the update is in place.
            foreach (var file in Directory.Exists(UpdatesDirectory) ? Directory.GetFiles(UpdatesDirectory) : [])
            {
                TryDelete(file);
            }
        }

        return new PreviousUpdateResult(succeeded, Normalize(target));
    }

    // ------------------------------------------------------------------ helpers

    private sealed record StateFile
    {
        public string? SnoozedVersion { get; init; }
        public DateTimeOffset? SnoozedUntilUtc { get; init; }
        public string? PendingVersion { get; init; }
        public DateTimeOffset? PendingStartedUtc { get; init; }
    }

    private StateFile ReadState()
    {
        lock (_stateLock)
        {
            try
            {
                return File.Exists(StatePath)
                    ? JsonSerializer.Deserialize<StateFile>(File.ReadAllText(StatePath)) ?? new StateFile()
                    : new StateFile();
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // A damaged state file only affects reminders; start fresh.
                return new StateFile();
            }
        }
    }

    private void ChangeState(Func<StateFile, StateFile> change)
    {
        lock (_stateLock)
        {
            var next = change(ReadState());
            Directory.CreateDirectory(_paths.RootDirectory);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(next, JsonOptions));
        }
    }

    /// <summary>Only GitHub's own servers, over HTTPS.</summary>
    internal static bool IsTrustedDownloadHost(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>"v1.0.2" / "1.0.2" → 1.0.2.</summary>
    internal static bool TryParseVersion(string tag, out Version version)
    {
        var text = tag.Trim().TrimStart('v', 'V');
        if (Version.TryParse(text, out var parsed))
        {
            version = Normalize(parsed);
            return true;
        }

        version = new Version(0, 0, 0);
        return false;
    }

    /// <summary>Compares as major.minor.build (1.0.1 == 1.0.1.0).</summary>
    private static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
