using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SareeBilling.Desktop.ViewModels;
using SareeBilling.Desktop.Views;

namespace SareeBilling.Desktop.Services;

/// <summary>
/// Message boxes, pop-up windows and file pickers, behind an interface so view models
/// never touch WPF windows directly.
/// </summary>
public interface IDialogService
{
    void ShowInfo(string message);

    void ShowError(string message);

    /// <returns>True if the user clicked Yes.</returns>
    bool Confirm(string message, string title);

    /// <returns>The chosen file path, or null if cancelled.</returns>
    string? PickImageFile();

    /// <returns>The typed text, or null if the user went back.</returns>
    string? PromptForText(string title, string message, string okText);

    /// <returns>The new customer's Id, or null if cancelled.</returns>
    int? ShowNewCustomerDialog();
}

public sealed class DialogService : IDialogService
{
    private const string AppTitle = "Saree Billing";

    private readonly IServiceProvider _services;

    public DialogService(IServiceProvider services)
    {
        _services = services;
    }

    public void ShowInfo(string message) =>
        Show(message, AppTitle, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowError(string message) =>
        Show(message, AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);

    public bool Confirm(string message, string title) =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public string? PickImageFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose logo image",
            Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            CheckFileExists = true
        };

        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? PromptForText(string title, string message, string okText)
    {
        var window = new PromptWindow(title, message, okText) { Owner = Owner };
        return window.ShowDialog() == true ? window.Answer : null;
    }

    public int? ShowNewCustomerDialog()
    {
        var viewModel = _services.GetRequiredService<NewCustomerDialogViewModel>();
        var window = new NewCustomerWindow(viewModel) { Owner = Owner };
        return window.ShowDialog() == true ? viewModel.SavedCustomerId : null;
    }

    private static Window? Owner => System.Windows.Application.Current?.MainWindow is { IsVisible: true } w ? w : null;

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage icon) =>
        Owner is { } owner
            ? MessageBox.Show(owner, message, title, buttons, icon)
            : MessageBox.Show(message, title, buttons, icon);
}
