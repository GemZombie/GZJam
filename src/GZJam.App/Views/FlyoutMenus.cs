using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace GZJam.App.Views;

/// <summary>
/// Menus whose choices change, so they're filled as they're wanted. A menu shows the choices it
/// held when it began to open, sized for them: filled in its Opening event, a menu on a button
/// opened empty, a thin sliver of border under the button, and a right-click menu filled again
/// once showing came out cut off on the right. So a button's menu is filled as the button is
/// pressed (finger, pen or mouse) or activated by the keyboard, before it opens; a right-click
/// menu once as it opens.
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
    /// code, which raises no Opening. Never both: changed once showing, its rows outgrew the size
    /// it opened at, and a menu could come out cut off on the right, its key hints and arrows
    /// beyond its edge.
    /// </summary>
    public static void FillAsItOpens(ContextMenu menu, Action<CancelEventArgs?> fill)
    {
        bool filled = false;
        menu.Opening += (_, e) =>
        {
            fill(e);
            filled = !e.Cancel;
        };
        menu.Opened += (_, _) =>
        {
            if (!filled)
                fill(null);
            filled = false;
        };
        menu.Closed += (_, _) => filled = false;
    }
}
