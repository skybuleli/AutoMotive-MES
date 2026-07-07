using BenchmarkDotNet.Running;
using MesAdmin.Benchmarks;

// ── 性能基准套件（T4.6-T4.9）──
// 运行全部基准：dotnet run -c Release
// 运行特定基准：dotnet run -c Release --filter *ZeroAllocation*
// 运行多个：    dotnet run -c Release --filter *PlcThroughput*|*ZeroAllocation*

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

// 默认运行所有基准（无参数时）
// 可通过 --filter 参数选择特定基准
