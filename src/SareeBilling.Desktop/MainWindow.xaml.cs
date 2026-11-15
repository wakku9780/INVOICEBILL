using System.ComponentModel;
using System.Windows;
using SareeBilling.Desktop.ViewModels;

namespace SareeBilling.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Gives an open, unsaved invoice the chance to keep the app open.
        e.Cancel = !_viewModel.ConfirmClose();
        base.OnClosing(e);
    }
}
