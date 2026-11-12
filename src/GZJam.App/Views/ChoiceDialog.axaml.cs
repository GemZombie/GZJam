using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GZJam.App.Views;

/// <summary>
/// A question with a few answers, none of them harmful, and Cancel (#396). Returns the index of the
/// answer chosen; null for Cancel or Escape. The first answer is the default (Enter), in the app's
/// accent color.
/// </summary>
public partial class ChoiceDialog : Window
{
    public ChoiceDialog() : this("", "", ["OK"])
    {
    }

    public ChoiceDialog(string heading, string message, IReadOnlyList<string> answers)
    {
        InitializeComponent();
        Heading.Text = heading;
        Message.Text = message;
        CancelButton.Click += (_, _) => Close(null);
        // Right to left from the default, so the first answer is at the far right, as a confirm is.
        for (int i = answers.Count - 1; i >= 0; i--)
        {
            int index = i;
            var button = new Button
            {
                Content = answers[i],
                MinWidth = 96,
                MinHeight = 38,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                IsDefault = i == 0,
            };
            if (i == 0 && Application.Current?.TryFindResource("AccentBrush", out object? accent) == true && accent is IBrush brush)
            {
                button.Background = brush;
                button.Foreground = Brushes.Black;
            }
            button.Click += (_, _) => Close(index);
            Buttons.Children.Add(button);
        }
    }
}
