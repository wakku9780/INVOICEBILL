using System.Windows;
using SareeBilling.Desktop.ViewModels;

namespace SareeBilling.Desktop.Views;

public partial class UpdateWindow : Window
{
    public UpdateWindow(UpdateDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
        Closed += (_, _) => viewModel.OnClosed();
    }
}
