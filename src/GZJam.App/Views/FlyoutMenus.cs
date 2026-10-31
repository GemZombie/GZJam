using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace GZJam.App.Views;

/// <summary>
/// Menus whose choices change, so they're filled as they're wanted. A menu's window takes its
/// size as it opens, and keeps it: filled in its Opening event, a menu on a button opened empty, a
/// thin sliver of border under the button, and a right-click menu could come out cut off on the
/// right. So a button's menu is filled as the button is pressed (finger, pen or mouse) or
/// activated by the keyboard, before it opens; a right-click menu once as it opens, and opened
/// again if it came out too small.
/// </summary>
internal static class FlyoutMenus
{
    public static void FillBeforeOpening(Button button, MenuFlyout menu, Action fill)
    {
        bool filled = false;
        void FillNow()
        {
            fill();
            filled = true;
        }
        button.AddHandler(InputElement.PointerPressedEvent, (_, _) => FillNow(), RoutingStrategies.Tunnel, handledEventsToo: true);
        button.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
                FillNow();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        menu.Opening += (_, _) =>
        {
            // Opened from code (ShowAt), nothing filled it: fill it now, for its items at least.
            // Filled already, it's left alone, so what shows is what's in it.
            if (!filled)
                fill();
            filled = false;
        };
    }

    /// <summary>
    /// A right-click menu, filled once as it opens: in its Opening event (right-click,
    /// long-press), which <paramref name="fill"/> may cancel, or in Opened for one opened from
    /// code, which raises no Opening. Opened smaller than its rows, it's opened again at once
    /// (<see cref="TooSmall"/>), without filling it again.
    /// </summary>
    public static void FillAsItOpens(ContextMenu menu, Action<CancelEventArgs?> fill)
    {
        bool filled = false, reopening = false;
        menu.Opening += (_, e) =>
        {
            fill(e);
            filled = !e.Cancel;
        };
        menu.Opened += (_, _) =>
        {
            if (!filled && !reopening)
                fill(null);
            filled = false;
            if (reopening)
            {
                reopening = false;
                return;
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (!TooSmall(menu))
                    return;
                reopening = true;
                menu.Close();
                menu.Open();
            }, DispatcherPriority.Background);
        };
        menu.Closed += (_, _) => filled = false;
    }

    /// <summary>
    /// Whether an open menu's window is smaller than its rows. On Windows, opened by a real
    /// right-click, the window takes its size before the rows have all been measured (agreeing on
    /// the columns of the key hints and arrows), and keeps it once they have: the menu came out cut
    /// off on the right, each row laid out at its own width, the first time or two it opened
    /// (#232). Opened again, its rows already measured, it's the right size.
    /// </summary>
    internal static bool TooSmall(ContextMenu menu) =>
        menu.IsOpen && TopLevel.GetTopLevel(menu) is { } window and not Window
        && (menu.DesiredSize.Width > window.ClientSize.Width + 0.5 || menu.DesiredSize.Height > window.ClientSize.Height + 0.5);
}
