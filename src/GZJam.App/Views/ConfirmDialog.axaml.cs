using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

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
        ConfirmButton.Click += (_, _) =>
        {
            Confirmed = true;
            Close(true);
        };
        CancelButton.Click += (_, _) => Close(false);
    }

    /// <summary>Closed with the confirm button (for a dialog shown on its own, not awaited).</summary>
    public bool Confirmed { get; private set; }

    /// <summary>
    /// The confirm button in the app's accent color rather than red: for a choice that loses
    /// nothing and stops nothing (red says "careful": delete, stop the music).
    /// </summary>
    public void ConfirmIsHarmless()
    {
        if (Application.Current?.TryFindResource("AccentBrush", out object? accent) == true && accent is IBrush brush)
            ConfirmButton.Background = brush;
        ConfirmButton.Foreground = Brushes.Black;
    }
}
