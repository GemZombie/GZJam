using Avalonia.Controls;

namespace GZJam.App.Views;

/// <summary>A yes/no question with a clearly worded confirm button. Returns true when confirmed.</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog() : this("", "", "OK")
    {
    }

    public ConfirmDialog(string heading, string message, string confirmText)
    {
        InitializeComponent();
        Heading.Text = heading;
        Message.Text = message;
        ConfirmButton.Content = confirmText;
        ConfirmButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }
}
