// Runs every test, the app's UI tests split across several processes at once.
//
//   dotnet build GZJam.slnx -c Release
//   dotnet run tools/test.cs                 (or: dotnet run tools/test.cs -- --shards 6)
//
// The app's tests drive one window on Avalonia's single UI thread, so in one process they can only
// run one at a time; most of their time is each test starting GZJam. Here they're shared out, by
// class, between a few processes (GZJAM_TEST_SHARDS or --shards; by default one per CPU, 2 to 4),
// alongside the library and audio tests. Each process uses its own temporary folders, as every
// test already does. `dotnet test` still runs them all too, one at a time.
//
// Results: tests/<project>/bin/<configuration>/net10.0/TestResults/, an .xml and a .log for each
// process. Any failure is printed at the end, and the exit code is 1.

using System.Diagnostics;
using System.Xml.Linq;

string configuration = "Release";
int shards = int.TryParse(Environment.GetEnvironmentVariable("GZJAM_TEST_SHARDS"), out int fromEnv) ? fromEnv
    : Math.Clamp(Environment.ProcessorCount, 2, 4);
for (int i = 0; i < args.Length; i++)
{
    if (args[i] is "--shards" && i + 1 < args.Length && int.TryParse(args[++i], out int n))
        shards = Math.Max(1, n);
    else if (args[i] is "-c" or "--configuration" && i + 1 < args.Length)
        configuration = args[++i];
    else
    {
        Console.Error.WriteLine($"Unknown option {args[i]}. Options: --shards N, -c Configuration");
        return 2;
    }
}

string root = FindRoot();
string Dll(string project) => Path.Combine(root, "tests", project, "bin", configuration, "net10.0", project + ".dll");
string[] projects = ["GZJam.Library.Tests", "GZJam.Audio.Tests", "GZJam.App.Tests"];
foreach (string project in projects)
{
    if (!File.Exists(Dll(project)))
    {
        Console.Error.WriteLine($"{Dll(project)} isn't built. First: dotnet build GZJam.slnx -c {configuration}");
        return 2;
    }
}

// The app's test classes, in groups of about the same number of tests: biggest classes first,
// each into the group with the fewest so far.
var appClasses = (await Run(Dll("GZJam.App.Tests"), ["-list", "tests", "-noLogo"]))
    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Where(line => line.StartsWith("GZJam.", StringComparison.Ordinal))
    .GroupBy(test => test[..test.LastIndexOf('.')])
    .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
    .ToList();
var groups = Enumerable.Range(0, Math.Min(shards, Math.Max(1, appClasses.Count))).Select(_ => new List<string>()).ToList();
var sizes = new int[groups.Count];
foreach (var cls in appClasses)
{
    int smallest = Array.IndexOf(sizes, sizes.Min());
    groups[smallest].Add(cls.Key);
    sizes[smallest] += cls.Count();
}

var runs = new List<(string Name, string Project, string[] Args)>
{
    ("Library tests", "GZJam.Library.Tests", []),
    ("Audio tests", "GZJam.Audio.Tests", []),
};
for (int g = 0; g < groups.Count; g++)
    runs.Add(($"App tests {g + 1}/{groups.Count}", "GZJam.App.Tests", [.. groups[g].SelectMany(c => new[] { "-class", c })]));

Console.WriteLine($"Running {runs.Count} test processes at once ({groups.Count} for the app's {appClasses.Count} test classes)...");
var clock = Stopwatch.StartNew();
var results = await Task.WhenAll(runs.Select(async run =>
{
    string results = Path.Combine(Path.GetDirectoryName(Dll(run.Project))!, "TestResults");
    Directory.CreateDirectory(results);
    string file = Path.Combine(results, run.Name.Replace(' ', '-').Replace('/', '-').ToLowerInvariant());
    var watch = Stopwatch.StartNew();
    var (exit, output) = await RunWithExit(Dll(run.Project), [.. run.Args, "-noLogo", "-noColor", "-xml", file + ".xml"]);
    await File.WriteAllTextAsync(file + ".log", output);
    var summary = Summarize(file + ".xml");
    Console.WriteLine($"  {run.Name}: {summary.Passed} passed, {summary.Failed} failed, {summary.Skipped} skipped ({watch.Elapsed:m\\:ss})");
    return (run.Name, Exit: exit, Summary: summary, Log: file + ".log");
}));

int failed = 0;
foreach (var r in results.Where(r => r.Exit != 0 || r.Summary.Failed > 0))
{
    failed++;
    Console.WriteLine();
    Console.WriteLine($"{r.Name} failed (exit code {r.Exit}); the whole output: {r.Log}");
    foreach (string failure in r.Summary.Failures)
        Console.WriteLine(failure);
    if (r.Summary.Failures.Count == 0)   // crashed or hung before reporting: show the end of what it said
        Console.WriteLine(string.Join(Environment.NewLine, File.ReadLines(r.Log).TakeLast(30)));
}
Console.WriteLine();
Console.WriteLine($"{results.Sum(r => r.Summary.Passed)} passed, {results.Sum(r => r.Summary.Failed)} failed, " +
    $"{results.Sum(r => r.Summary.Skipped)} skipped, in {clock.Elapsed:m\\:ss}.");
return failed == 0 ? 0 : 1;

static (int Passed, int Failed, int Skipped, List<string> Failures) Summarize(string xml)
{
    if (!File.Exists(xml))
        return (0, 0, 0, []);
    var tests = XDocument.Load(xml).Descendants("test").ToList();
    var failures = tests.Where(t => (string?)t.Attribute("result") == "Fail")
        .Select(t => $"  [FAIL] {t.Attribute("name")?.Value}{Environment.NewLine}" +
            Indent(t.Element("failure")?.Element("message")?.Value) + Indent(FirstLines(t.Element("failure")?.Element("stack-trace")?.Value, 6)))
        .ToList();
    return (tests.Count(t => (string?)t.Attribute("result") == "Pass"), failures.Count,
        tests.Count(t => (string?)t.Attribute("result") == "Skip"), failures);
}

static string FirstLines(string? text, int count) =>
    text is null ? "" : string.Join('\n', text.Split('\n').Take(count));

static string Indent(string? text) =>
    string.IsNullOrWhiteSpace(text) ? "" : string.Concat(text.TrimEnd().Split('\n').Select(l => "      " + l.TrimEnd() + Environment.NewLine));

static async Task<string> Run(string dll, string[] arguments) => (await RunWithExit(dll, arguments)).Output;

static async Task<(int Exit, string Output)> RunWithExit(string dll, string[] arguments)
{
    var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    start.ArgumentList.Add(dll);
    foreach (string a in arguments)
        start.ArgumentList.Add(a);
    using var process = Process.Start(start)!;
    var output = process.StandardOutput.ReadToEndAsync();
    var error = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    return (process.ExitCode, await output + await error);
}

static string FindRoot()
{
    for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "GZJam.slnx")))
            return dir.FullName;
    throw new InvalidOperationException("Run this from inside the GZJam repository.");
}
