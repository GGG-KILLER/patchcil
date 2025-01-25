using BenchmarkDotNet.Running;
using PatchCil.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(AssemblyMarker).Assembly).Run(args);
