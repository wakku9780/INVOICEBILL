using System.Windows;
using SareeBilling.Desktop.ViewModels;

namespace SareeBilling.Desktop.Views;

public partial class NewCustomerWindow : Window
{
    public NewCustomerWindow(NewCustomerDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += (_, saved) => DialogResult = saved;
    }
}
