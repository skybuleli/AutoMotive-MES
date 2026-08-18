using MesAdmin.Application.Sagas;
using Microsoft.Extensions.Options;

namespace MesAdmin.Application.Tests;

/// <summary>
/// ProductionOrderSagaOptionsValidator 测试。
/// </summary>
public class ProductionOrderSagaOptionsValidatorTests
{
    private readonly ProductionOrderSagaOptionsValidator _validator = new();

    [Fact]
    public void Validate_DefaultOptions_ShouldPass()
    {
        var options = new ProductionOrderSagaOptions();

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
        Assert.True(result.Failures is null);
    }

    [Fact]
    public void Validate_InvalidStationEquipment_ShouldFail()
    {
        var options = new ProductionOrderSagaOptions
        {
            FallbackStationEquipment = new Dictionary<int, string>
            {
                { 1, "EQ-TEST-01" },
                { 2, "" },
                { 3, "INVALID" }
            }
        };

        var result = _validator.Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures ?? [], f => f.Contains("站号必须大于 1"));
        Assert.Contains(result.Failures ?? [], f => f.Contains("设备码不能为空"));
        Assert.Contains(result.Failures ?? [], f => f.Contains("应以 'EQ-' 开头"));
    }

    [Fact]
    public void Validate_InvalidStationLastSeq_ShouldFail()
    {
        var options = new ProductionOrderSagaOptions
        {
            FallbackStationLastSeq = new Dictionary<int, int>
            {
                { 1, 5 },
                { 2, -1 }
            }
        };

        var result = _validator.Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures ?? [], f => f.Contains("站号必须大于 1"));
        Assert.Contains(result.Failures ?? [], f => f.Contains("最后一道工序序号必须大于 0"));
    }

    [Fact]
    public void Validate_InvalidBoltSequence_ShouldFail()
    {
        var options = new ProductionOrderSagaOptions
        {
            FallbackBoltSequence = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "", 6 },
                { "M6-FL", -1 }
            }
        };

        var result = _validator.Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures ?? [], f => f.Contains("螺栓编码不能为空"));
        Assert.Contains(result.Failures ?? [], f => f.Contains("工序序号必须大于 0"));
    }

    [Fact]
    public void Validate_EmptyDictionaries_ShouldFail()
    {
        var options = new ProductionOrderSagaOptions
        {
            FallbackStationEquipment = new Dictionary<int, string>(),
            FallbackStationLastSeq = new Dictionary<int, int>(),
            FallbackBoltSequence = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        };

        var result = _validator.Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures ?? [], f => f.Contains("FallbackStationEquipment 不能为空"));
        Assert.Contains(result.Failures ?? [], f => f.Contains("FallbackStationLastSeq 不能为空"));
        Assert.Contains(result.Failures ?? [], f => f.Contains("FallbackBoltSequence 不能为空"));
    }

    [Fact]
    public void Validate_ValidCustomOptions_ShouldPass()
    {
        var options = new ProductionOrderSagaOptions
        {
            FallbackStationEquipment = new Dictionary<int, string>
            {
                { 2, "EQ-ASM-01" },
                { 3, "EQ-TQ-01" }
            },
            FallbackStationLastSeq = new Dictionary<int, int>
            {
                { 2, 5 },
                { 3, 10 }
            },
            FallbackBoltSequence = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "M6-FL", 6 },
                { "M6-FR", 7 }
            }
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
        Assert.True(result.Failures is null);
    }
}
