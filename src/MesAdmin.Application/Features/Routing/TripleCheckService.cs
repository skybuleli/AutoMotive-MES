using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.Routing;

// 消除 Routing 命名空间歧义（当前命名空间与 Domain.Models.Routing 冲突）
using DomainRouting = MesAdmin.Domain.Models.Routing;

/// <summary>
/// 三重校验结果
/// </summary>
public sealed record TripleCheckResult(
    bool Passed,
    CheckResult MaterialScanCheck,
    CheckResult BomComparisonCheck,
    CheckResult EquipmentParamCheck);

/// <summary>
/// 单项校验结果
/// </summary>
public sealed record CheckResult(
    string CheckName,
    bool Passed,
    string? Message);

/// <summary>
/// 防错三重校验服务（T3.3）。
/// 操作员启动工序前必须通过三重校验，全部通过方可启动：
/// 1. 物料扫码校验 — 扫码物料编码存在于工单 BOM 中
/// 2. BOM 比对校验 — 物料批次状态合格、关键物料必须 Qualified
/// 3. 设备参数比对 — 设备当前参数在工艺路线参数模板规格范围内
/// </summary>
public sealed class TripleCheckService(
    IProductionOrderRepository orders,
    IMaterialBatchRepository batches,
    IRoutingRepository routings,
    IBomRepository boms)
{
    /// <summary>
    /// 执行三重校验。
    /// </summary>
    public async Task<TripleCheckResult> ExecuteAsync(
        Ulid orderId,
        string materialCode,
        Ulid materialBatchId,
        string equipmentCode,
        IReadOnlyDictionary<string, double> currentParameters,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(materialCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(equipmentCode);

        // ── 获取工单信息 ──
        var order = await orders.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            var err = new CheckResult("工单查询", false, $"工单 {orderId} 不存在");
            return new TripleCheckResult(false, err, err, err);
        }

        // ── 获取当前生效工艺路线 ──
        // RoutingId 为非可空 Ulid（struct），默认值为 Ulid.Empty
        DomainRouting? routing = order.RoutingId != default
            ? await routings.GetByIdAsync(order.RoutingId, ct)
            : null;

        routing ??= await routings.GetActiveByProductAsync(order.ProductCode, ct);

        // ── 获取 BOM ──
        var bom = !string.IsNullOrWhiteSpace(order.BomVersion)
            ? await boms.GetByProductAndVersionAsync(order.ProductCode, order.BomVersion, ct)
            : null;

        // ── 执行三重校验 ──
        var check1 = VerifyMaterialScan(materialCode, bom);
        var check2 = await VerifyBomComparisonAsync(materialBatchId, materialCode, ct);
        var check3 = VerifyEquipmentParameters(equipmentCode, currentParameters, routing);

        var overallPassed = check1.Passed && check2.Passed && check3.Passed;
        return new TripleCheckResult(overallPassed, check1, check2, check3);
    }

    /// <summary>
    /// 步骤 1：物料扫码校验 — 扫码物料编码存在于工单 BOM 中
    /// </summary>
    private static CheckResult VerifyMaterialScan(string materialCode, Bom? bom)
    {
        if (bom is null)
            return new CheckResult("物料扫码校验", false, "未找到工单对应的 BOM");

        var matched = bom.Items.Any(i =>
            i.MaterialCode.Equals(materialCode, StringComparison.OrdinalIgnoreCase));

        return matched
            ? new CheckResult("物料扫码校验", true, $"物料 {materialCode} 存在于 BOM 中")
            : new CheckResult("物料扫码校验", false,
                $"物料 {materialCode} 不在工单 BOM 中，可能扫描了错误物料");
    }

    /// <summary>
    /// 步骤 2：BOM 比对校验 — 物料批次状态合格
    /// </summary>
    private async Task<CheckResult> VerifyBomComparisonAsync(
        Ulid materialBatchId,
        string materialCode,
        CancellationToken ct)
    {
        var batch = await batches.GetByIdAsync(materialBatchId, ct);

        if (batch is null)
            return new CheckResult("BOM 比对校验", false, $"物料批次 {materialBatchId} 不存在");

        if (!batch.MaterialCode.Equals(materialCode, StringComparison.OrdinalIgnoreCase))
            return new CheckResult("BOM 比对校验", false,
                $"扫码物料编码 {materialCode} 与批次 {batch.BatchNumber} 的物料编码 {batch.MaterialCode} 不匹配");

        if (batch.RemainingQuantity <= 0)
            return new CheckResult("BOM 比对校验", false,
                $"物料批次 {batch.BatchNumber} 可用库存为 0，无法投料");

        if (batch.IsCritical && batch.Status != MaterialBatchStatus.Qualified)
            return new CheckResult("BOM 比对校验", false,
                $"关键物料 {materialCode} 批次 {batch.BatchNumber} 状态为 {batch.Status}，必须检验合格");

        if (batch.IsCritical)
            return new CheckResult("BOM 比对校验", true,
                $"关键物料 {materialCode} 批次 {batch.BatchNumber} 检验合格，库存 {batch.RemainingQuantity}");

        return new CheckResult("BOM 比对校验", true,
            $"物料 {materialCode} 批次 {batch.BatchNumber} 可用，库存 {batch.RemainingQuantity}");
    }

    /// <summary>
    /// 步骤 3：设备参数比对校验 — 当前设备参数在工艺路线参数模板规格范围内
    /// </summary>
    private static CheckResult VerifyEquipmentParameters(
        string equipmentCode,
        IReadOnlyDictionary<string, double> currentParameters,
        DomainRouting? routing)
    {
        if (routing is null)
            return new CheckResult("设备参数比对", false, "未找到工艺路线定义");

        int station;
        try
        {
            station = InferStationFromEquipment(equipmentCode);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new CheckResult("设备参数比对", false, $"无法识别设备 {equipmentCode} 所属工站");
        }
        var stationOps = routing.GetOperationsByStation(station);

        if (stationOps.Count == 0)
            return new CheckResult("设备参数比对", false,
                $"工站 {station} 在工艺路线中未定义工序");

        // 收集该工站所有工序的参数模板
        var allTemplates = stationOps
            .SelectMany(op => op.ParameterTemplates)
            .DistinctBy(pt => pt.ParameterCode)
            .ToList();

        if (allTemplates.Count == 0)
            return new CheckResult("设备参数比对", true,
                $"工站 {station} 无比对参数要求，跳过");

        var failures = new List<string>();

        foreach (var param in allTemplates)
        {
            if (!currentParameters.TryGetValue(param.ParameterCode, out var actualValue))
            {
                failures.Add($"缺少参数 {param.ParameterCode}（{param.ParameterName}）");
                continue;
            }

            if (param.LowerSpecLimit.HasValue && actualValue < param.LowerSpecLimit.Value)
                failures.Add($"{param.ParameterCode} 当前值 {actualValue}{param.Unit}，低于下限 {param.LowerSpecLimit.Value}");

            if (param.UpperSpecLimit.HasValue && actualValue > param.UpperSpecLimit.Value)
                failures.Add($"{param.ParameterCode} 当前值 {actualValue}{param.Unit}，超过上限 {param.UpperSpecLimit.Value}");
        }

        if (failures.Count == 0)
        {
            var paramCount = allTemplates.Count;
            return new CheckResult("设备参数比对", true,
                $"{equipmentCode} 共 {paramCount} 个参数全部在规格范围内");
        }

        var msg = $"{equipmentCode} 参数校验不通过：{string.Join("；", failures)}";
        return new CheckResult("设备参数比对", false, msg);
    }

    /// <summary>
    /// 从设备编码推断所属工站编号。
    /// EQ-ASM → 站2, EQ-TQ → 站3, EQ-HYD → 站4,
    /// EQ-FLS → 站5, EQ-FT → 站6, EQ-VN → 站7
    /// </summary>
    public static int InferStationFromEquipment(string equipmentCode)
    {
        return equipmentCode.ToUpperInvariant() switch
        {
            var c when c.Contains("ASM") => 2,
            var c when c.Contains("TQ") => 3,
            var c when c.Contains("HYD") => 4,
            var c when c.Contains("FLS") => 5,
            var c when c.Contains("FT") => 6,
            var c when c.Contains("VN") => 7,
            _ => throw new ArgumentOutOfRangeException(nameof(equipmentCode), $"设备编码 {equipmentCode} 无法识别所属工站")
        };
    }
}
