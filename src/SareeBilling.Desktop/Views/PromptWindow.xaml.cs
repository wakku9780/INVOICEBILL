using System.Windows;

namespace SareeBilling.Desktop.Views;

/// <summary>Asks the user for a short piece of text, e.g. a reason for cancelling.</summary>
public partial class PromptWindow : Window
{
    public PromptWindow(string title, string message, string okText)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        OkButton.Content = okText;
    }

    public string Answer => AnswerBox.Text.Trim();

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Answer.Length == 0)
        {
            MessageBox.Show(this, "Please type a reason.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            AnswerBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
