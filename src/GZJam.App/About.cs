using System.Reflection;

namespace GZJam.App;

/// <summary>What Settings › About shows: the version, who made GZJam, and what it's built with.</summary>
internal static class About
{
    public const string Maker = "Jesse Gearhart";
    public const string ProjectUrl = "https://github.com/GemZombie/GZJam";

    /// <summary>The build's version, e.g. "0.1.0" (a release) or "0.1.0-dev" (built here).</summary>
    public static string Version { get; } =
        typeof(About).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0]   // leave out the commit the build adds
        ?? "?";

    /// <summary>The licences of everything GZJam ships with, beside the program.</summary>
    public static string NoticesPath => Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");

    /// <summary>The notices' opening list (who made what, under which licence), without the full texts.</summary>
    public static string BuiltWith()
    {
        try
        {
            string text = File.ReadAllText(NoticesPath);
            int texts = text.IndexOf("\n####", StringComparison.Ordinal);
            return (texts < 0 ? text : text[..texts]).Trim();
        }
        catch (IOException)
        {
            return "The list of the software GZJam is built with is missing from this copy. It's at " + ProjectUrl + ".";
        }
    }
}
