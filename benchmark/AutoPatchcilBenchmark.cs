
using System.CommandLine;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using PatchCil.Commands;

namespace PatchCil.Benchmarks;

[Config(typeof(Config))]
public class AutoPatchcilBenchmark
{
    private ParseResult? _parseResult;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var libraryPaths = Environment.GetEnvironmentVariable("BENCH_LIBRARIES_PATHS")?.Split(Path.PathSeparator);
        if (libraryPaths is null || libraryPaths.Length < 1)
            throw new InvalidOperationException("No libraries provided through BENCH_LIBRARIES_PATHS env var.");

        var avaloniaIlspyPath = Environment.GetEnvironmentVariable("BENCH_AVALONIA_ILSPY_PATH");
        if (string.IsNullOrWhiteSpace(avaloniaIlspyPath))
            throw new InvalidOperationException("No AvaloniaILSpy provided through BENCH_AVALONIA_ILSPY_PATH env var.");

        var autoCommand = new AutoCommand();
        _parseResult = autoCommand.Command.Parse([
            "-r", "linux-x64",
            "--libs", ..libraryPaths,
            "--ignore-missing", "c", "libc", "gdi32", "kernel32", "ntdll", "shell32", "user32", "Windows.UI.Composition", "winspool.drv", "libAvaloniaNative", "clr",
            "--paths", avaloniaIlspyPath,
            "--dry-run"
        ]);
    }

    [Benchmark]
    public async Task Benchmark()
    {
        var exitCode = await _parseResult!.InvokeAsync(NullConsole.Instance);
        if (exitCode != 0)
            throw new InvalidOperationException("Non-zero exit code.");
    }

    /// <summary>
    /// Provides access to in-memory standard streams that are not attached to <see cref="System.Console"/>.
    /// </summary>
    public class NullConsole : IConsole
    {
        public static readonly NullConsole Instance = new();

        /// <summary>
        /// Initializes a new instance of <see cref="TestConsole"/>.
        /// </summary>
        public NullConsole()
        {
            Out = new NullStreamWriter();
            Error = new NullStreamWriter();
        }

        /// <inheritdoc />
        public IStandardStreamWriter Error { get; protected set; }

        /// <inheritdoc />
        public IStandardStreamWriter Out { get; protected set; }

        /// <inheritdoc />
        public bool IsOutputRedirected { get; protected set; }

        /// <inheritdoc />
        public bool IsErrorRedirected { get; protected set; }

        /// <inheritdoc />
        public bool IsInputRedirected { get; protected set; }

        internal class NullStreamWriter : TextWriter, IStandardStreamWriter
        {
            public override void Write(char value) { }

            public override void Write(string? value) { }

            public override Encoding Encoding { get; } = Encoding.Unicode;

            public override string ToString() => string.Empty;
        }
    }

    private sealed class Config : ManualConfig
    {
        public Config()
        {
            var job = Job.Dry.WithRuntime(CoreRuntime.Core90);

            AddJob(job.WithEnvironmentVariables([
                new EnvironmentVariable("BENCH_LIBRARIES_PATHS", string.Join(Path.PathSeparator, GetLibraryPaths())),
                new EnvironmentVariable("BENCH_AVALONIA_ILSPY_PATH", GetAvaloniaIlspyPath()),
            ]));

            AddDiagnoser(new EventPipeProfiler(EventPipeProfile.CpuSampling, performExtraBenchmarksRun: false));

            AddColumn(
                StatisticColumn.Min,
                StatisticColumn.Mean,
                StatisticColumn.Median,
                StatisticColumn.P95,
                StatisticColumn.Max);
        }

        private static string[] GetLibraryPaths()
        {
            var librariesBuilder = Process.Start(new ProcessStartInfo("nix", [
                "build", "--no-link", "--print-out-paths", "nixpkgs#glibc.out", "nixpkgs#xorg.libX11.out",
                "nixpkgs#xorg.libICE.out", "nixpkgs#xorg.libSM.out", "nixpkgs#xorg.libXrandr.out",
                "nixpkgs#xorg.libXi.out", "nixpkgs#xorg.libXcursor.out", "nixpkgs#glib.out",
                "nixpkgs#gtk3.out", "nixpkgs#libGL.out",
            ])
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
            }) ?? throw new InvalidOperationException("Unable to use nix build to get libraries.");

            var libraryPaths = Array.ConvertAll(
                librariesBuilder.StandardOutput.ReadToEnd()
                    .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                path => Path.Combine(path, "lib"));
            librariesBuilder.WaitForExit();

            if (librariesBuilder.ExitCode != 0)
                throw new InvalidOperationException("nix build for libraries had a nonn-zero exit code.");
            return libraryPaths;
        }

        private static string GetAvaloniaIlspyPath()
        {

            var avaloniaIlspyBuilder = Process.Start(new ProcessStartInfo("nix", [
                "build", "--no-link", "--print-out-paths", "nixpkgs#avalonia-ilspy",
            ])
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
            }) ?? throw new InvalidOperationException("Unable to use nix build to get avalonia-ilspy.");

            var avaloniaIlspyPath = avaloniaIlspyBuilder.StandardOutput.ReadToEnd().Trim();
            avaloniaIlspyBuilder.WaitForExit();

            if (avaloniaIlspyBuilder.ExitCode != 0)
                throw new InvalidOperationException("nix build for avalonia-ilspy had a nonn-zero exit code.");
            return avaloniaIlspyPath;
        }
    }
}
