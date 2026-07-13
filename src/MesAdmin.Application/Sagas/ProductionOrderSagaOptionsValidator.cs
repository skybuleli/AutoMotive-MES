using Microsoft.Extensions.Options;

namespace MesAdmin.Application.Sagas;

/// <summary>
/// ProductionOrderSagaOptions 配置验证器。
/// 验证回退映射字典的键、值是否符合工艺路线约定。
/// </summary>
public sealed class ProductionOrderSagaOptionsValidator : IValidateOptions<ProductionOrderSagaOptions>
{
    public ValidateOptionsResult Validate(string? name, ProductionOrderSagaOptions options)
    {
        var errors = new List<string>();

        ValidateNonEmpty(options, errors);
        ValidateStationEquipment(options, errors);
        ValidateStationLastSeq(options, errors);
        ValidateBoltSequence(options, errors);

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }

    private static void ValidateNonEmpty(ProductionOrderSagaOptions options, List<string> errors)
    {
        if (options.FallbackStationEquipment.Count == 0)
            errors.Add("FallbackStationEquipment 不能为空");

        if (options.FallbackStationLastSeq.Count == 0)
            errors.Add("FallbackStationLastSeq 不能为空");

        if (options.FallbackBoltSequence.Count == 0)
            errors.Add("FallbackBoltSequence 不能为空");
    }

    private static void ValidateStationEquipment(ProductionOrderSagaOptions options, List<string> errors)
    {
        foreach (var (station, equipmentCode) in options.FallbackStationEquipment)
        {
            if (station <= 1)
                errors.Add($"FallbackStationEquipment 的站号必须大于 1（站1为人工上料），实际：{station}");

            if (string.IsNullOrWhiteSpace(equipmentCode))
            {
                errors.Add($"FallbackStationEquipment[{station}] 设备码不能为空");
                continue;
            }

            if (!equipmentCode.StartsWith("EQ-", StringComparison.Ordinal))
                errors.Add($"FallbackStationEquipment[{station}] 设备码应以 'EQ-' 开头，实际：{equipmentCode}");
        }
    }

    private static void ValidateStationLastSeq(ProductionOrderSagaOptions options, List<string> errors)
    {
        foreach (var (station, lastSeq) in options.FallbackStationLastSeq)
        {
            if (station <= 1)
                errors.Add($"FallbackStationLastSeq 的站号必须大于 1（站1为人工上料），实际：{station}");

            if (lastSeq <= 0)
                errors.Add($"FallbackStationLastSeq[{station}] 最后一道工序序号必须大于 0，实际：{lastSeq}");
        }
    }

    private static void ValidateBoltSequence(ProductionOrderSagaOptions options, List<string> errors)
    {
        foreach (var (bolt, sequence) in options.FallbackBoltSequence)
        {
            if (string.IsNullOrWhiteSpace(bolt))
            {
                errors.Add("FallbackBoltSequence 的螺栓编码不能为空");
                continue;
            }

            if (sequence <= 0)
                errors.Add($"FallbackBoltSequence[{bolt}] 工序序号必须大于 0，实际：{sequence}");
        }
    }
}
