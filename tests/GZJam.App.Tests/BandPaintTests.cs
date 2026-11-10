using Avalonia.Media;
using Avalonia.Headless.XUnit;
using GZJam.App.Controls;

namespace GZJam.App.Tests;

/// <summary>The waveforms' bar brushes (#363): one set per color, shared and kept, so drawing makes none.</summary>
public sealed class BandPaintTests
{
    [AvaloniaFact]
    public void A_color_wanted_again_gets_the_brushes_made_the_first_time()
    {
        BandPaint first = BandPaint.For(Color.FromRgb(200, 100, 50));
        Assert.Same(first, BandPaint.For(Color.FromRgb(200, 100, 50)));
        Assert.Same(first, BandPaint.For(Color.FromRgb(201, 102, 51)));   // too close to tell apart
        Assert.NotSame(first, BandPaint.For(Color.FromRgb(200, 100, 90)));
    }

    [AvaloniaFact]
    public void Every_bar_color_is_kept_close_and_the_sets_are_few()
    {
        var made = new HashSet<BandPaint>(ReferenceEqualityComparer.Instance);
        for (int low = 0; low < 256; low += 5)
        {
            for (int mid = 0; mid < 256; mid += 5)
            {
                for (int high = 0; high < 256; high += 5)
                {
                    Color wanted = SeekBar.BandColor((byte)low, (byte)mid, (byte)high);
                    BandPaint paint = BandPaint.For((byte)low, (byte)mid, (byte)high);
                    made.Add(paint);
                    Color got = ((ISolidColorBrush)paint.Bright).Color;
                    Assert.InRange(got.R - wanted.R, -4, 4);
                    Assert.InRange(got.G - wanted.G, -4, 4);
                    Assert.InRange(got.B - wanted.B, -4, 4);
                }
            }
        }
        // 140,608 bars of different levels: a few thousand brush sets, made once, for good.
        Assert.InRange(made.Count, 1, 1 << 15);
        Assert.InRange(BandPaint.Count, made.Count, 1 << 15);
    }
}
