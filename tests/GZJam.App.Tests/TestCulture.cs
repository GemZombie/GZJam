using System.Globalization;
using System.Runtime.CompilerServices;

namespace GZJam.App.Tests;

/// <summary>
/// The UI tests check text as an American user sees it ("7:42 PM", "1,234 songs"), whatever the
/// machine's own settings: CI machines run with 24-hour, invariant formats.
/// </summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void UseUsEnglish()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
