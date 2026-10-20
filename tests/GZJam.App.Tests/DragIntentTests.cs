using Avalonia;
using Avalonia.Input;
using static GZJam.App.DragIntent;

namespace GZJam.App.Tests;

public sealed class DragIntentTests
{
    [Theory]
    [InlineData(3, 2, Decision.Wait)]
    [InlineData(0, 8, Decision.Drag)]     // a mouse drags in any direction
    [InlineData(8, 0, Decision.Drag)]
    public void Mouse_drags_after_a_few_pixels(double dx, double dy, Decision expected) =>
        Assert.Equal(expected, Evaluate(PointerType.Mouse, new Vector(dx, dy), fromHandle: false));

    [Theory]
    [InlineData(5, 8, Decision.Wait)]      // not far enough to tell yet
    [InlineData(2, 30, Decision.Cancel)]   // vertical swipe = scroll the list
    [InlineData(20, 18, Decision.Cancel)]  // diagonal is ambiguous; prefer scrolling
    [InlineData(40, 6, Decision.Drag)]     // sideways swipe towards the queue = drag
    public void Touch_scrolls_vertically_and_drags_sideways(double dx, double dy, Decision expected) =>
        Assert.Equal(expected, Evaluate(PointerType.Touch, new Vector(dx, dy), fromHandle: false));

    [Fact]
    public void Touch_on_a_drag_handle_drags_in_any_direction() =>
        Assert.Equal(Decision.Drag, Evaluate(PointerType.Touch, new Vector(0, 30), fromHandle: true));

    [Fact]
    public void Pen_behaves_like_touch() =>
        Assert.Equal(Decision.Cancel, Evaluate(PointerType.Pen, new Vector(0, 30), fromHandle: false));
}
