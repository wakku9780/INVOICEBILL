using System.Diagnostics;
using SareeBilling.Application.Updates;
using SareeBilling.Desktop.Navigation;
using SareeBilling.Desktop.ViewModels;
using SareeBilling.Desktop.Views;

namespace SareeBilling.Desktop.Services;

/// <summary>
/// Ties the update service to the UI: quiet start-up check, the banner, the manual
/// "Check for updates" button, and safely handing over to the installer.
/// </summary>
public interface IUpdateCoordinator
{
    /// <summary>A newer version the user has not postponed; null when none.</summary>
    ReleaseInfo? AvailableUpdate { get; }

    event EventHandler? AvailableUpdateChanged;

    string CurrentVersionText { get; }

    /// <summary>Start-up check. Silent: no message if offline or up to date.</summary>
    Task CheckQuietlyAsync();

    /// <returns>A short status line for the Settings page.</returns>
    Task<string> CheckNowAsync();

    void ShowUpdateDetails();

    /// <summary>Tells the user how the update started last time ended.</summary>
    void ReportPreviousUpdate();

    /// <summary>
    /// Downloads (already done by the caller), backs up and starts the installer, then closes the app.
    /// </summary>
    /// <returns>An error message, or null when the installer was started.</returns>
    Task<string?> InstallAsync(ReleaseInfo release, string installerPath);

    /// <returns>A message when the user must finish their work first; otherwise null.</returns>
    string? GetBlockingWorkMessage();

    void RemindLater(ReleaseInfo release);
}

public sealed class UpdateCoordinator : IUpdateCoordinator
{
    private static readonly TimeSpan RemindLaterDelay = TimeSpan.FromDays(1);

    private readonly IUpdateService _updates;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly ErrorLog _errorLog;
    private ReleaseInfo? _availableUpdate;
    private bool _dialogOpen;

    public UpdateCoordinator(IUpdateService updates, INavigationService navigation, IDialogService dialogs, ErrorLog errorLog)
    {
        _updates = updates;
        _navigation = navigation;
        _dialogs = dialogs;
        _errorLog = errorLog;
    }

    public ReleaseInfo? AvailableUpdate
    {
        get => _availableUpdate;
        private set
        {
            _availableUpdate = value;
            AvailableUpdateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? AvailableUpdateChanged;

    public string CurrentVersionText => _updates.CurrentVersion.ToString();

    public async Task CheckQuietlyAsync()
    {
        try
        {
            var release = await _updates.FindNewerReleaseAsync();
            AvailableUpdate = release is not null && !_updates.IsSnoozed(release) ? release : null;
        }
        catch (Exception ex)
        {
            // Offline or server trouble: the app simply carries on. Logged for support only.
            _errorLog.Write("Background update check failed (app continues normally)", ex);
        }
    }

    public async Task<string> CheckNowAsync()
    {
        try
        {
            var release = await _updates.FindNewerReleaseAsync();
            if (release is null)
            {
                AvailableUpdate = null;
                return $"You have the latest version ({CurrentVersionText}).";
            }

            AvailableUpdate = release;
            ShowUpdateDetails();
            return $"Version {release.Version} is available.";
        }
        catch (UpdateException ex)
        {
            return ex.Message;
        }
    }

    public void ShowUpdateDetails()
    {
        if (AvailableUpdate is not { } release || _dialogOpen)
        {
            return;
        }

        _dialogOpen = true;
        try
        {
            var viewModel = new UpdateDialogViewModel(release, _updates, this);
            new UpdateWindow(viewModel) { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog();
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    public void ReportPreviousUpdate()
    {
        var result = _updates.TakePreviousUpdateResult();
        if (result is null)
        {
            return;
        }

        if (result.Succeeded)
        {
            _dialogs.ShowInfo($"Saree Billing was updated to version {CurrentVersionText}.\n\nAll your invoices and customers are unchanged.");
        }
        else
        {
            _dialogs.ShowError(
                $"The update to version {result.TargetVersion} did not finish, so you are still using version {CurrentVersionText}.\n\n" +
                "Your data is safe and was backed up before the update. You can try again from Company Settings → Check for updates.");
        }
    }

    public string? GetBlockingWorkMessage() =>
        _navigation.CurrentPage is { HasUnsavedWork: true } page
            ? $"Please finish your work on \"{page.Title}\" first (save or clear the invoice), then click Update now."
            : null;

    public async Task<string?> InstallAsync(ReleaseInfo release, string installerPath)
    {
        if (GetBlockingWorkMessage() is { } blocked)
        {
            return blocked;
        }

        try
        {
            await _updates.PrepareForInstallAsync(release);
        }
        catch (Exception ex)
        {
            _errorLog.Write("Safety backup before update failed", ex);
            return "Your data could not be backed up, so the update was not installed. Please try again.";
        }

        try
        {
            // /SILENT shows a progress window only. The installer waits for this app to close,
            // keeps all data, and starts the new version when it has finished.
            var arguments = $"/SILENT /SUPPRESSMSGBOXES /NORESTART /LOG=\"{_updates.GetInstallerLogPath(release)}\"";
            Process.Start(new ProcessStartInfo(installerPath, arguments) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _updates.ClearPendingUpdate();
            _errorLog.Write("Could not start the update installer", ex);
            return "The installer could not be started (it may have been blocked by Windows or antivirus). " +
                   "Your data is unchanged. Please try again, or ask for the setup file to be sent to you.";
        }

        System.Windows.Application.Current.Shutdown();
        return null;
    }

    public void RemindLater(ReleaseInfo release)
    {
        _updates.Snooze(release, RemindLaterDelay);
        AvailableUpdate = null;
    }
}
