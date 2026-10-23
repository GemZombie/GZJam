using Avalonia.Controls;

namespace GZJam.App.Views;

/// <summary>A yes/no question with a clearly worded confirm button. Returns true when confirmed.</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog() : this("", "", "OK")
    {
    }

    /// <param name="cancelText">The other button's words, e.g. "Keep playing".</param>
    /// <param name="cancelIsDefault">
    /// Enter chooses cancel rather than confirm, as Escape always does, so a reflex keypress can't
    /// confirm something that stops the music.
    /// </param>
    public ConfirmDialog(string heading, string message, string confirmText, string cancelText = "Cancel", bool cancelIsDefault = false)
    {
        InitializeComponent();
        Heading.Text = heading;
        Message.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;
        if (cancelIsDefault)
        {
            ConfirmButton.IsDefault = false;
            CancelButton.IsDefault = true;
            Opened += (_, _) => CancelButton.Focus();
        }
        ConfirmButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }
}
