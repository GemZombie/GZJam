using Avalonia.Controls;

namespace GZJam.App.Views;

/// <summary>Asks for a short piece of text. Returns it trimmed, or null if canceled or left empty.</summary>
public partial class PromptDialog : Window
{
    public PromptDialog() : this("", "", "OK")
    {
    }

    public PromptDialog(string heading, string message, string okText, string initial = "")
    {
        InitializeComponent();
        Heading.Text = heading.ToUpperInvariant();
        Message.Text = message;
        Message.IsVisible = message.Length > 0;
        OkButton.Content = okText.ToUpperInvariant();
        Input.Text = initial;
        OkButton.Click += (_, _) => Close(string.IsNullOrWhiteSpace(Input.Text) ? null : Input.Text.Trim());
        CancelButton.Click += (_, _) => Close(null);
        Opened += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
    }
}
