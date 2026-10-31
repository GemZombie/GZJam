using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>
/// Right-click menus are filled once as they open, never again once showing: changed then, a
/// menu's rows outgrew the size it opened at, and the song menu came out cut off on the right.
/// </summary>
public sealed class MenuFillTests
{
    [AvaloniaFact]
    public void A_right_click_menu_is_filled_once_whether_right_clicked_or_opened_from_code()
    {
        var target = new Border { Width = 200, Height = 200, Background = Avalonia.Media.Brushes.Gray };
        var menu = new ContextMenu { Items = { new MenuItem { Header = "One" } } };
        target.ContextMenu = menu;
        var window = new Window { Width = 400, Height = 400, Content = target };
        window.Show();
        int fills = 0;
        FlyoutMenus.FillAsItOpens(menu, _ => fills++);
        try
        {
            window.MouseDown(new Point(100, 100), MouseButton.Right, RawInputModifiers.None);
            window.MouseUp(new Point(100, 100), MouseButton.Right, RawInputModifiers.None);
            AppHarness.Settle(200);
            Assert.True(menu.IsOpen);
            Assert.Equal(1, fills);   // in Opening, not again in Opened
            menu.Close();

            menu.Open(target);        // from code: no Opening, so filled in Opened
            AppHarness.Settle(200);
            Assert.True(menu.IsOpen);
            Assert.Equal(2, fills);
            menu.Close();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_canceled_opening_leaves_the_next_menu_opened_from_code_filled()
    {
        var target = new Border { Width = 200, Height = 200, Background = Avalonia.Media.Brushes.Gray };
        var menu = new ContextMenu { Items = { new MenuItem { Header = "One" } } };
        target.ContextMenu = menu;
        var window = new Window { Width = 400, Height = 400, Content = target };
        window.Show();
        int fills = 0;
        bool cancel = true;
        FlyoutMenus.FillAsItOpens(menu, e =>
        {
            fills++;
            if (cancel && e is not null)
                e.Cancel = true;
        });
        try
        {
            window.MouseDown(new Point(100, 100), MouseButton.Right, RawInputModifiers.None);
            window.MouseUp(new Point(100, 100), MouseButton.Right, RawInputModifiers.None);
            AppHarness.Settle(200);
            Assert.False(menu.IsOpen);

            cancel = false;
            menu.Open(target);
            AppHarness.Settle(200);
            Assert.True(menu.IsOpen);
            Assert.Equal(2, fills);
            menu.Close();
        }
        finally
        {
            window.Close();
        }
    }
}
