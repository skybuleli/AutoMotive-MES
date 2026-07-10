using MesAdmin.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace MesAdmin.Application.Tests;

public class RuntimeSafetyGuardsTests
{
    [Fact]
    public void ValidateNoSimulationInProduction_ShouldRejectMockSap()
    {
        var config = Config(("Sap:UseRealClient", "false"), ("Plc:UseRealClients", "true"));

        var ex = Assert.Throws<InvalidOperationException>(
            () => RuntimeSafetyGuards.ValidateNoSimulationInProduction(config, "Production"));

        Assert.Contains("Sap:UseRealClient=true", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateNoSimulationInProduction_ShouldRejectSimulatedPlc()
    {
        var config = Config(("Sap:UseRealClient", "true"), ("Plc:UseRealClients", "false"));

        var ex = Assert.Throws<InvalidOperationException>(
            () => RuntimeSafetyGuards.ValidateNoSimulationInProduction(config, "Production"));

        Assert.Contains("Plc:UseRealClients=true", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateNoSimulationInProduction_ShouldRejectWhenNoRealDriverEnabled()
    {
        var config = Config(
            ("Sap:UseRealClient", "true"),
            ("Plc:UseRealClients", "true"));

        var ex = Assert.Throws<InvalidOperationException>(
            () => RuntimeSafetyGuards.ValidateNoSimulationInProduction(config, "Production"));

        Assert.Contains("Plc:Drivers", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateNoSimulationInProduction_ShouldAllowWhenRealDriverEnabled()
    {
        var config = Config(
            ("Sap:UseRealClient", "true"),
            ("Plc:UseRealClients", "true"),
            ("Plc:Drivers:OpcUa:Enabled", "true"));

        RuntimeSafetyGuards.ValidateNoSimulationInProduction(config, "Production");
    }

    [Fact]
    public void ValidateNoSimulationInProduction_ShouldAllowSimulatorEnvironment()
    {
        var config = Config(("Sap:UseRealClient", "false"), ("Plc:UseRealClients", "false"));

        RuntimeSafetyGuards.ValidateNoSimulationInProduction(config, "Simulator");
    }

    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();
}
