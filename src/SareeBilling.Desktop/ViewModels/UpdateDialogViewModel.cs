using System.Globalization;
using System.Windows.Input;
using SareeBilling.Application.Updates;
using SareeBilling.Desktop.Mvvm;
using SareeBilling.Desktop.Services;

namespace SareeBilling.Desktop.ViewModels;

/// <summary>"A new version is available" window: details, Update now, Remind me later.</summary>
public sealed class UpdateDialogViewModel : ObservableObject
{
    private readonly ReleaseInfo _release;
    private readonly IUpdateService _updates;
    private readonly IUpdateCoordinator _coordinator;
    private CancellationTokenSource? _download;
    private bool _isBusy;
    private double _progress;
    private string? _statusText;
    private string? _errorText;

    public UpdateDialogViewModel(ReleaseInfo release, IUpdateService updates, IUpdateCoordinator coordinator)
    {
        _release = release;
        _updates = updates;
        _coordinator = coordinator;

        UpdateNowCommand = new AsyncRelayCommand(UpdateNowAsync, () => !IsBusy);
        RemindLaterCommand = new RelayCommand(RemindLater);
    }

    /// <summary>Asks the window to close.</summary>
    public event EventHandler? CloseRequested;

    public string CurrentVersionText => _updates.CurrentVersion.ToString();
    public string LatestVersionText => _release.Version.ToString();
    public string Title => _release.Title;

    public string PublishedText => _release.PublishedAt is { } published
        ? "Released on " + published.LocalDateTime.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)
        : string.Empty;

    public string ReleaseNotes => string.IsNullOrWhiteSpace(_release.Notes) ? "No details were provided for this version." : _release.Notes;

    public string SizeText => _release.InstallerSizeBytes > 0 ? $"Download size: {_release.InstallerSizeBytes / (1024.0 * 1024.0):0.0} MB" : string.Empty;

    public ICommand UpdateNowCommand { get; }
    public ICommand RemindLaterCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(RemindLaterText));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public string? StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string? ErrorText { get => _errorText; private set => SetProperty(ref _errorText, value); }

    /// <summary>While downloading, the second button cancels the download.</summary>
    public string RemindLaterText => IsBusy ? "Cancel" : "Remind me later";

    /// <summary>Called when the window closes for any reason.</summary>
    public void OnClosed() => _download?.Cancel();

    private async Task UpdateNowAsync()
    {
        ErrorText = null;

        if (_coordinator.GetBlockingWorkMessage() is { } blocked)
        {
            ErrorText = blocked;
            return;
        }

        IsBusy = true;
        _download = new CancellationTokenSource();
        try
        {
            StatusText = "Downloading the update…";
            var progress = new Progress<double>(p =>
            {
                Progress = p;
                StatusText = $"Downloading the update… {p:0}%";
            });
            var installer = await _updates.DownloadInstallerAsync(_release, progress, _download.Token);

            StatusText = "Backing up your data and starting the installer…";
            var error = await _coordinator.InstallAsync(_release, installer);
            if (error is not null)
            {
                ErrorText = error;
                StatusText = null;
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Download cancelled. Nothing was changed.";
        }
        catch (UpdateException ex)
        {
            ErrorText = ex.Message + "\n\nNothing was changed; you can keep working and try again later.";
            StatusText = null;
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
            _download.Dispose();
            _download = null;
        }
    }

    private void RemindLater()
    {
        if (IsBusy)
        {
            _download?.Cancel();
            return;
        }

        _coordinator.RemindLater(_release);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
