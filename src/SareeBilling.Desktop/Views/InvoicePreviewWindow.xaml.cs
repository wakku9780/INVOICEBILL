using System.Windows;
using SareeBilling.Desktop.ViewModels;

namespace SareeBilling.Desktop.Views;

public partial class InvoicePreviewWindow : Window
{
    public InvoicePreviewWindow(InvoicePreviewViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Never taller than the screen (minus the taskbar), so the title bar and buttons stay reachable
        // on small laptop screens and at 125%/150% display scaling.
        var workArea = SystemParameters.WorkArea;
        Height = Math.Min(Height, workArea.Height - 20);
        Width = Math.Min(Width, workArea.Width - 20);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
