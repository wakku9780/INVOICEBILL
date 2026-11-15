using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using SareeBilling.Application.Abstractions;
using SareeBilling.Application.Backups;
using SareeBilling.Application.Common;
using SareeBilling.Desktop.Mvvm;
using SareeBilling.Desktop.Services;

namespace SareeBilling.Desktop.ViewModels;

public sealed class BackupFileRow
{
    public BackupFileRow(BackupFile file)
    {
        File = file;
    }

    public BackupFile File { get; }
    public string FileName => File.FileName;
    public string CreatedText => File.CreatedLocal.ToString("dd-MMM-yyyy  hh:mm tt", CultureInfo.InvariantCulture);
    public string SizeText => File.SizeBytes < 1024 * 1024
        ? $"{Math.Max(1, File.SizeBytes / 1024)} KB"
        : $"{File.SizeBytes / (1024.0 * 1024.0):0.0} MB";
}

public sealed class BackupRestoreViewModel : PageViewModel
{
    private readonly IBackupService _backups;
    private readonly IDialogService _dialogs;
    private readonly IAppPaths _paths;

    private string _backupFolder;
    private BackupFileRow? _selectedBackup;
    private string _lastBackupText = string.Empty;
    private string? _successMessage;
    private string? _errorMessage;

    public BackupRestoreViewModel(IBackupService backups, IDialogService dialogs, IAppPaths paths)
    {
        _backups = backups;
        _dialogs = dialogs;
        _paths = paths;
        _backupFolder = backups.DefaultBackupFolder;

        BackupNowCommand = new AsyncRelayCommand(BackupNowAsync);
        ChangeFolderCommand = new RelayCommand(ChangeFolder);
        OpenFolderCommand = new RelayCommand(() => OpenInExplorer(BackupFolder));
        RestoreSelectedCommand = new AsyncRelayCommand(() => RestoreAsync(SelectedBackup!.File.Path), () => SelectedBackup is not null);
        RestoreFromFileCommand = new AsyncRelayCommand(RestoreFromFileAsync);
        OpenAutoBackupsCommand = new RelayCommand(() => OpenInExplorer(_paths.AutoBackupDirectory));
    }

    public override string Title => "Backup & Restore";

    public ObservableCollection<BackupFileRow> Backups { get; } = [];

    public ICommand BackupNowCommand { get; }
    public ICommand ChangeFolderCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand RestoreSelectedCommand { get; }
    public ICommand RestoreFromFileCommand { get; }
    public ICommand OpenAutoBackupsCommand { get; }

    public string BackupFolder
    {
        get => _backupFolder;
        private set
        {
            if (SetProperty(ref _backupFolder, value))
            {
                RefreshList();
            }
        }
    }

    public BackupFileRow? SelectedBackup
    {
        get => _selectedBackup;
        set
        {
            if (SetProperty(ref _selectedBackup, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string LastBackupText { get => _lastBackupText; private set => SetProperty(ref _lastBackupText, value); }

    public string AutoBackupFolder => _paths.AutoBackupDirectory;

    public string? SuccessMessage { get => _successMessage; private set => SetProperty(ref _successMessage, value); }
    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }

    public override Task InitializeAsync()
    {
        RefreshList();
        return Task.CompletedTask;
    }

    private void RefreshList()
    {
        Backups.Clear();
        foreach (var file in _backups.ListBackups(BackupFolder))
        {
            Backups.Add(new BackupFileRow(file));
        }

        var last = _backups.GetLastManualBackupTime();
        LastBackupText = last is null
            ? "You have not made a backup yet."
            : $"Last backup: {last.Value.ToString("dd-MMM-yyyy hh:mm tt", CultureInfo.InvariantCulture)}";
    }

    private async Task BackupNowAsync()
    {
        ClearMessages();
        try
        {
            var path = await _backups.CreateBackupAsync(BackupFolder);
            RefreshList();
            SelectedBackup = Backups.FirstOrDefault(b => b.File.Path == path);
            SuccessMessage = $"Backup saved:\n{path}";
        }
        catch (ValidationException ex)
        {
            ErrorMessage = string.Join("\n", ex.Errors);
        }
    }

    private void ChangeFolder()
    {
        var folder = _dialogs.PickFolder("Choose where to save backups (e.g. a pen drive)", BackupFolder);
        if (folder is not null)
        {
            ClearMessages();
            BackupFolder = folder;
        }
    }

    private async Task RestoreFromFileAsync()
    {
        var file = _dialogs.PickBackupFile(BackupFolder);
        if (file is not null)
        {
            await RestoreAsync(file);
        }
    }

    private async Task RestoreAsync(string path)
    {
        ClearMessages();

        BackupContents contents;
        try
        {
            contents = await _backups.InspectAsync(path);
        }
        catch (ValidationException ex)
        {
            ErrorMessage = string.Join("\n", ex.Errors);
            return;
        }

        var lastInvoice = contents.LastInvoiceNumber is null
            ? "none"
            : $"{contents.LastInvoiceNumber} ({contents.LastInvoiceDate?.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)})";

        var question =
            "Restore this backup?\n\n" +
            $"File: {Path.GetFileName(path)}\n" +
            $"Saved on: {contents.FileDateLocal.ToString("dd-MMM-yyyy hh:mm tt", CultureInfo.InvariantCulture)}\n" +
            $"Company: {contents.CompanyName ?? "(not set up)"}\n" +
            $"Invoices: {contents.InvoiceCount}    Customers: {contents.CustomerCount}\n" +
            $"Last invoice: {lastInvoice}\n\n" +
            "ALL CURRENT DATA WILL BE REPLACED by this backup.\n" +
            "A safety copy of the current data is saved first, so this can be undone.";

        if (!_dialogs.Confirm(question, "Restore backup"))
        {
            return;
        }

        try
        {
            var safetyCopy = await _backups.RestoreAsync(path);
            RefreshList();
            SuccessMessage =
                "Backup restored. Customers, invoices and company details now come from the backup.\n" +
                $"Your previous data was saved to:\n{safetyCopy}";
        }
        catch (ValidationException ex)
        {
            ErrorMessage = string.Join("\n", ex.Errors);
        }
    }

    private void OpenInExplorer(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _dialogs.ShowError($"Could not open the folder.\n\n{ex.Message}");
        }
    }

    private void ClearMessages()
    {
        SuccessMessage = null;
        ErrorMessage = null;
    }
}
