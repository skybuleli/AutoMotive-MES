using System.IO.Pipelines;
using MesAdmin.Infrastructure.Plc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MesAdmin.Application.Tests;

/// <summary>
/// P0-2 回归：生产模式下 PlcDriverFactory 禁止静默降级到 Simulated。
/// RuntimeSafetyGuards.ValidateNoSimulationInProduction 承诺生产环境"不降级为模拟"，
/// 但 GetTransport 此前对未匹配设备静默返回 SimulatedPlcTransport，
/// 导致真实设备被伪造数据覆盖（OEE/SPC/Andon 数据完整性被破坏）。
/// </summary>
public class PlcDriverFactoryFallbackTests
{
    private sealed class FakeRealTransport : IPlcTransport
    {
        public FakeRealTransport(params string[] codes)
            => SupportedEquipmentCodes = new HashSet<string>(codes, StringComparer.Ordinal);

        public PipeReader Reader => PipeReader.Create(Stream.Null);
        public bool IsConnected => false;
        public string TransportName => "Fake-Real";
        public IReadOnlySet<string> SupportedEquipmentCodes { get; }
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public Task<object> ReadRegisterAsync(string address, string tag, CancellationToken ct = default) => Task.FromResult<object>(0);
        public Task WriteRegisterAsync(string address, string tag, object value, CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static PlcDriverFactory CreateFactory(bool allowSimulatedFallback, params IPlcTransport[] realTransports)
    {
        var simulated = new SimulatedPlcTransport([], NullLogger<SimulatedPlcTransport>.Instance);
        var transports = new List<IPlcTransport> { simulated };
        transports.AddRange(realTransports);
        return new PlcDriverFactory(transports, NullLogger<PlcDriverFactory>.Instance, allowSimulatedFallback);
    }

    [Fact]
    public void GetTransport_ProductionMode_UnmatchedEquipment_ShouldThrow()
    {
        var factory = CreateFactory(allowSimulatedFallback: false, new FakeRealTransport("EQ-FLS-01"));

        var ex = Assert.Throws<InvalidOperationException>(() => factory.GetTransport("EQ-UNKNOWN-99"));
        Assert.Contains("EQ-UNKNOWN-99", ex.Message, StringComparison.Ordinal);
        Assert.Contains("模拟", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetTransport_ProductionMode_MatchedEquipment_ShouldReturnRealTransport()
    {
        var real = new FakeRealTransport("EQ-FLS-01");
        var factory = CreateFactory(allowSimulatedFallback: false, real);

        var transport = factory.GetTransport("EQ-FLS-01");
        Assert.Same(real, transport);
    }

    [Fact]
    public void GetTransport_DevMode_UnmatchedEquipment_ShouldFallbackToSimulated()
    {
        var factory = CreateFactory(allowSimulatedFallback: true, new FakeRealTransport("EQ-FLS-01"));

        var transport = factory.GetTransport("EQ-UNKNOWN-99");
        Assert.IsType<SimulatedPlcTransport>(transport);
    }
}