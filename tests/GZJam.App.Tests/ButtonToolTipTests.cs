using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace GZJam.App.Tests;

/// <summary>
/// A button's tooltip goes below it (above, with no room below), never over it. At the pointer,
/// near the bottom of the screen it slid up over the pointer, and "+ New" under PLAYLISTS took
/// two or three clicks. Where the system puts it is checked by hand: headless tooltips don't move.
/// </summary>
public sealed class ButtonToolTipTests
{
    [AvaloniaFact]
    public void Every_buttons_tooltip_is_placed_against_the_button_not_the_pointer()
    {
        using var app = new AppHarness();
        var buttons = app.Window.GetVisualDescendants().OfType<Button>().Where(b => ToolTip.GetTip(b) is not null).ToList();
        Assert.Contains(buttons, b => b.Name == "NewPlaylistButton");
        Assert.All(buttons, b =>
        {
            Assert.Equal(PlacementMode.Bottom, ToolTip.GetPlacement(b));
            Assert.Equal(0, ToolTip.GetVerticalOffset(b));
        });
    }
}
