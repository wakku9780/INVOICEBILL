using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SareeBilling.Application.Abstractions;
using SareeBilling.Application.Backups;
using SareeBilling.Application.Updates;
using SareeBilling.Infrastructure.Updates;

namespace SareeBilling.Tests.Infrastructure;

/// <summary>Update checks against a fake GitHub — no real network is used.</summary>
public sealed class GitHubUpdateServiceTests : IAsyncLifetime
{
    private static readonly byte[] FakeInstaller = Encoding.ASCII.GetBytes("MZ" + new string('x', 5000));
    private const string DownloadUrl = "https://github.com/wakku9780/SareeBilling-Releases/releases/download/v1.0.2/SareeBilling-Setup-1.0.2.exe";

    private TestHost _host = null!;
    private FakeGitHub _github = null!;

    public async Task InitializeAsync()
    {
        _host = await TestHost.StartAsync();
        _github = new FakeGitHub();
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private GitHubUpdateService CreateService(string currentVersion = "1.0.1") =>
        new(new UpdateSettings("wakku9780", "SareeBilling-Releases", Version.Parse(currentVersion)),
            _host.Get<IAppPaths>(),
            _host.Get<IBackupService>(),
            TimeProvider.System,
            NullLogger<GitHubUpdateService>.Instance,
            _github);

    private static string ReleaseJson(string tag = "v1.0.2", string? digest = null, long? size = null, bool prerelease = false,
        string assetName = "SareeBilling-Setup-1.0.2.exe", string url = DownloadUrl) => $$"""
        {
          "tag_name": "{{tag}}",
          "name": "Saree Billing {{tag}}",
          "body": "- Plain invoice numbers\n- Faster PDF",
          "draft": false,
          "prerelease": {{(prerelease ? "true" : "false")}},
          "published_at": "2026-11-20T10:00:00Z",
          "html_url": "https://github.com/wakku9780/SareeBilling-Releases/releases/tag/{{tag}}",
          "assets": [
            { "name": "notes.txt", "browser_download_url": "https://github.com/x/notes.txt", "size": 10 },
            { "name": "{{assetName}}", "browser_download_url": "{{url}}", "size": {{size ?? FakeInstaller.Length}}
              {{(digest is null ? "" : $", \"digest\": \"{digest}\"")}} }
          ]
        }
        """;

    private static string Sha256(byte[] bytes) => "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    [Fact]
    public async Task Newer_release_is_found_with_notes_and_installer()
    {
        _github.LatestJson = ReleaseJson(digest: Sha256(FakeInstaller));

        var release = await CreateService().FindNewerReleaseAsync();

        Assert.NotNull(release);
        Assert.Equal(new Version(1, 0, 2), release.Version);
        Assert.Contains("Plain invoice numbers", release.Notes);
        Assert.Equal("SareeBilling-Setup-1.0.2.exe", release.InstallerFileName);
        Assert.NotNull(release.InstallerSha256);
        Assert.Equal("/repos/wakku9780/SareeBilling-Releases/releases/latest", _github.LastApiPath);
    }

    [Theory]
    [InlineData("v1.0.1")] // same version
    [InlineData("v1.0.0")] // older
    [InlineData("1.0.1.0")] // same, four parts
    public async Task Same_or_older_release_is_not_offered(string tag)
    {
        _github.LatestJson = ReleaseJson(tag: tag);
        Assert.Null(await CreateService().FindNewerReleaseAsync());
    }

    [Fact]
    public async Task Prerelease_wrong_asset_or_untrusted_host_is_ignored()
    {
        _github.LatestJson = ReleaseJson(prerelease: true);
        Assert.Null(await CreateService().FindNewerReleaseAsync());

        _github.LatestJson = ReleaseJson(assetName: "Something.zip");
        Assert.Null(await CreateService().FindNewerReleaseAsync());

        _github.LatestJson = ReleaseJson(url: "http://evil.example.com/SareeBilling-Setup-1.0.2.exe");
        Assert.Null(await CreateService().FindNewerReleaseAsync());
    }

    [Fact]
    public async Task No_releases_yet_is_not_an_error()
    {
        _github.ApiStatus = HttpStatusCode.NotFound;
        Assert.Null(await CreateService().FindNewerReleaseAsync());
    }

    [Fact]
    public async Task No_internet_gives_friendly_error()
    {
        _github.ThrowNetworkError = true;
        var ex = await Assert.ThrowsAsync<UpdateException>(() => CreateService().FindNewerReleaseAsync());
        Assert.Contains("internet", ex.Message);
    }

    [Fact]
    public async Task Download_is_verified_and_saved()
    {
        _github.LatestJson = ReleaseJson(digest: Sha256(FakeInstaller));
        _github.InstallerBytes = FakeInstaller;
        var service = CreateService();
        var release = (await service.FindNewerReleaseAsync())!;
        var reports = new List<double>();

        var path = await service.DownloadInstallerAsync(release, new Progress<double>(reports.Add));

        Assert.Equal(FakeInstaller, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".download"));
    }

    [Fact]
    public async Task Tampered_download_is_rejected_and_removed()
    {
        _github.LatestJson = ReleaseJson(digest: Sha256(FakeInstaller));
        var tampered = (byte[])FakeInstaller.Clone();
        tampered[100] = (byte)'!';
        _github.InstallerBytes = tampered;
        var service = CreateService();
        var release = (await service.FindNewerReleaseAsync())!;

        var ex = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadInstallerAsync(release, null));

        Assert.Contains("security check", ex.Message);
        var updates = Path.Combine(_host.Get<IAppPaths>().RootDirectory, "Updates");
        Assert.Empty(Directory.GetFiles(updates));
    }

    [Fact]
    public async Task Non_program_or_truncated_download_is_rejected()
    {
        _github.LatestJson = ReleaseJson();
        _github.InstallerBytes = Encoding.ASCII.GetBytes("<html>error page</html>" + new string('x', FakeInstaller.Length - 23));
        var service = CreateService();
        var release = (await service.FindNewerReleaseAsync())!;
        await Assert.ThrowsAsync<UpdateException>(() => service.DownloadInstallerAsync(release, null));

        _github.InstallerBytes = FakeInstaller[..1000];
        await Assert.ThrowsAsync<UpdateException>(() => service.DownloadInstallerAsync(release, null));
    }

    [Fact]
    public async Task Remind_me_later_hides_only_that_version()
    {
        _github.LatestJson = ReleaseJson();
        var service = CreateService();
        var release = (await service.FindNewerReleaseAsync())!;

        Assert.False(service.IsSnoozed(release));
        service.Snooze(release, TimeSpan.FromDays(1));
        Assert.True(service.IsSnoozed(release));
        Assert.False(service.IsSnoozed(release with { Version = new Version(1, 0, 3) }));
    }

    [Fact]
    public async Task Prepare_backs_up_database_and_next_start_reports_result()
    {
        _github.LatestJson = ReleaseJson();
        var release = (await CreateService().FindNewerReleaseAsync())!;

        var backup = await CreateService().PrepareForInstallAsync(release);
        Assert.True(File.Exists(backup));
        Assert.Contains("before-update-1.0.2", backup);

        // Installer failed: the app still runs the old version.
        var failed = CreateService("1.0.1").TakePreviousUpdateResult();
        Assert.NotNull(failed);
        Assert.False(failed.Succeeded);
        Assert.Null(CreateService("1.0.1").TakePreviousUpdateResult()); // reported only once

        // Installer succeeded: the app now runs the new version.
        await CreateService().PrepareForInstallAsync(release);
        var succeeded = CreateService("1.0.2").TakePreviousUpdateResult();
        Assert.True(succeeded!.Succeeded);
        Assert.Equal(new Version(1, 0, 2), succeeded.TargetVersion);
    }

    /// <summary>Plays GitHub: the "latest release" API and the installer download.</summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        public string LatestJson { get; set; } = "{}";
        public HttpStatusCode ApiStatus { get; set; } = HttpStatusCode.OK;
        public byte[] InstallerBytes { get; set; } = [];
        public bool ThrowNetworkError { get; set; }
        public string? LastApiPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (ThrowNetworkError)
            {
                throw new HttpRequestException("No such host is known.");
            }

            Assert.False(request.Headers.UserAgent.Count == 0, "GitHub requires a User-Agent header.");

            if (request.RequestUri!.Host == "api.github.com")
            {
                LastApiPath = request.RequestUri.AbsolutePath;
                return Task.FromResult(new HttpResponseMessage(ApiStatus) { Content = new StringContent(LatestJson) });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(InstallerBytes) });
        }
    }
}
