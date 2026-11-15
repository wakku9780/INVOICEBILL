using System.Windows;
using System.Windows.Controls;
using SareeBilling.Desktop.ViewModels;

namespace SareeBilling.Desktop.Views;

public partial class CreateInvoiceView : UserControl
{
    public CreateInvoiceView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is CreateInvoiceViewModel oldViewModel)
        {
            oldViewModel.LineAdded -= OnLineAdded;
        }

        if (e.NewValue is CreateInvoiceViewModel newViewModel)
        {
            newViewModel.LineAdded += OnLineAdded;
        }
    }

    // Purely visual: put the cursor back in Description so the next item can be typed at once.
    private void OnLineAdded(object? sender, EventArgs e) => DescriptionBox.Focus();
}
