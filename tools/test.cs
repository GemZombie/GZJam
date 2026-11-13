// Runs every test, the app's UI tests split across several processes at once.
//
//   dotnet build GZJam.slnx -c Release
//   dotnet run tools/test.cs                 (or: dotnet run tools/test.cs -- --shards 6)
//
// The app's tests drive one window on Avalonia's single UI thread, so in one process they can only
// run one at a time; most of their time is each test starting GZJam. Here they're shared out, by
// class, between a few processes (GZJAM_TEST_SHARDS or --shards; by default one fewer than the
// CPUs, or two fewer from 8 up, 2 to 6, leaving room for the rest), alongside the library and
// audio tests. Each process uses its own temporary folders, as every test already does.
// `dotnet test` still runs them all too, one at a time.
//
// Some UI tests time what they do (a double-click, a hint that shows for a few seconds), and a
// machine with every CPU busy can make one miss. So a test that fails is run again afterwards, on
// its own: if it passes then, it's named at the end (worth a look if it keeps happening) but
// doesn't fail the run; if it fails again, it does.
//
// Results: tests/<project>/bin/<configuration>/net10.0/TestResults/, an .xml and a .log for each
// process (and for the second runs). Any failure is printed at the end, and the exit code is 1.

using System.Diagnostics;
using System.Xml.Linq;

string configuration = "Release";
int shards = int.TryParse(Environment.GetEnvironmentVariable("GZJAM_TEST_SHARDS"), out int fromEnv) ? fromEnv
    : Math.Clamp(Environment.ProcessorCount >= 8 ? Environment.ProcessorCount - 2 : Environment.ProcessorCount - 1, 2, 6);
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
    var result = await RunTests(run.Name, run.Project, run.Args);
    Console.WriteLine($"  {run.Name}: {result.Summary.Passed} passed, {result.Summary.Failed} failed, {result.Summary.Skipped} skipped ({result.Time:m\\:ss})");
    return result;
}));

// The tests that failed, run again one project at a time with nothing else running. A process that
// crashed or hung (no failures reported) isn't run again: that's a real failure.
var again = new List<(string[] Methods, Result Result)>();
foreach (var project in results.Where(r => r.Exit != 0 && r.Summary.Failures.Count > 0).GroupBy(r => r.Project))
{
    string[] methods = [.. project.SelectMany(r => r.Summary.Failures).Select(f => f.Method).Distinct()];
    Console.WriteLine($"Running {methods.Length} failed {(methods.Length == 1 ? "test" : "tests")} again, alone...");
    string name = $"{project.First().Name.Split(' ')[0]} tests, again";
    again.Add((methods, await RunTests(name, project.Key, [.. methods.SelectMany(m => new[] { "-method", m })])));
}
// Passed alone: run again in a process that reported its results, and not among its failures.
var passedAlone = again.Where(a => a.Result.Exit == 0 || a.Result.Summary.Failures.Count > 0)
    .SelectMany(a => a.Methods.Except(a.Result.Summary.Failures.Select(f => f.Method))).ToList();
int stillFailing = results.SelectMany(r => r.Summary.Failures).Select(f => f.Method).Distinct().Count() - passedAlone.Count;

int failed = 0;
foreach (var r in results.Where(r => r.Exit != 0 && r.Summary.Failures.Count == 0)
    .Concat(again.Select(a => a.Result).Where(r => r.Exit != 0 || r.Summary.Failed > 0)))
{
    failed++;
    Console.WriteLine();
    Console.WriteLine($"{r.Name} failed (exit code {r.Exit}); the whole output: {r.Log}");
    foreach (var failure in r.Summary.Failures)
        Console.WriteLine(failure.Text);
    if (r.Summary.Failures.Count == 0)   // crashed or hung before reporting: show the end of what it said
        Console.WriteLine(string.Join(Environment.NewLine, File.ReadLines(r.Log).TakeLast(30)));
}
if (passedAlone.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Failed with the other tests running, then passed alone (timing, with every CPU busy; worth a look if it keeps happening):");
    foreach (var failure in results.SelectMany(r => r.Summary.Failures).Where(f => passedAlone.Contains(f.Method)).DistinctBy(f => f.Method))
        Console.WriteLine(failure.Text);
}
Console.WriteLine();
int passed = results.Sum(r => r.Summary.Passed) + passedAlone.Count;
Console.WriteLine($"{passed} passed{(passedAlone.Count > 0 ? $" ({passedAlone.Count} on a second run)" : "")}, {stillFailing} failed, " +
    $"{results.Sum(r => r.Summary.Skipped)} skipped, in {clock.Elapsed:m\\:ss}.");
return failed == 0 ? 0 : 1;

async Task<Result> RunTests(string name, string project, string[] arguments)
{
    string folder = Path.Combine(Path.GetDirectoryName(Dll(project))!, "TestResults");
    Directory.CreateDirectory(folder);
    string file = Path.Combine(folder, name.Replace(", ", "-").Replace(' ', '-').Replace('/', '-').ToLowerInvariant());
    var watch = Stopwatch.StartNew();
    var (exit, output) = await RunWithExit(Dll(project), [.. arguments, "-noLogo", "-noColor", "-xml", file + ".xml"]);
    await File.WriteAllTextAsync(file + ".log", output);
    return new Result(name, project, exit, Summarize(file + ".xml"), file + ".log", watch.Elapsed);
}

static Summary Summarize(string xml)
{
    if (!File.Exists(xml))
        return new Summary(0, 0, 0, []);
    var tests = XDocument.Load(xml).Descendants("test").ToList();
    var failures = tests.Where(t => (string?)t.Attribute("result") == "Fail")
        .Select(t => new Failure($"{t.Attribute("type")?.Value}.{t.Attribute("method")?.Value}",
            $"  [FAIL] {t.Attribute("name")?.Value}{Environment.NewLine}" +
            Indent(t.Element("failure")?.Element("message")?.Value) + Indent(FirstLines(t.Element("failure")?.Element("stack-trace")?.Value, 6))))
        .ToList();
    return new Summary(tests.Count(t => (string?)t.Attribute("result") == "Pass"), failures.Count,
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

record Failure(string Method, string Text);
record Summary(int Passed, int Failed, int Skipped, List<Failure> Failures);
record Result(string Name, string Project, int Exit, Summary Summary, string Log, TimeSpan Time);
