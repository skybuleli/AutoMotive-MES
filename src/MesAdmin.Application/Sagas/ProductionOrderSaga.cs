using Cleipnir.ResilientFunctions;
using Cleipnir.ResilientFunctions.CoreRuntime.Invocation;
using Cleipnir.ResilientFunctions.Domain;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Observability;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Sagas;

/// <summary>
/// 生产工单 Saga（31 工序 × 7 站编排）。
/// 对应 PRD M01 / TAD v2 Effect 策略矩阵。
///
/// P1 集成：注入 IRoutingRepository 动态加载工艺路线数据。
/// Execute() 启动时从 DB 读取 Routing，动态计算工站设备映射和哨兵工序。
/// 如果 Routing 未找到则回退到硬编码默认值（兼容旧数据）。
///
/// Effect 策略：
///   - 站1 上料扫码：Effect 之外（安全联锁、人工确认）
///   - 站2-5,7：AtLeastOnce（不可丢失，幂等保护：先读后写工序状态）
///   - 站6 功能终检：AtMostOnce（重放复用上次结果）
/// </summary>
public class ProductionOrderSaga(
    IProductionOrderRepository orderRepo,
    IWorkOrderOperationRepository operationRepo,
    IRoutingRepository routingRepo)
{
    /// <summary>站号 → PLC 设备码映射（回退默认值）</summary>
    private static readonly Dictionary<int, string> FallbackStationEquipment = new()
    {
        { 2, "EQ-ASM-01" }, { 3, "EQ-TQ-01" },
        { 4, "EQ-HYD-01" }, { 5, "EQ-FLS-01" },
        { 6, "EQ-FT-01" }, { 7, "EQ-VN-01" },
    };

    /// <summary>站号 → 哨兵工序序号（回退默认值）</summary>
    private static readonly Dictionary<int, int> FallbackStationLastSeq = new()
    {
        { 2, 5 }, { 3, 10 }, { 4, 23 }, { 5, 27 }, { 7, 31 },
    };

    /// <summary>站3 螺栓编码 → 工序序号（回退默认值）</summary>
    private static readonly Dictionary<string, int> FallbackBoltSequence = new()
    {
        { "M6-FL", 6 }, { "M6-FR", 7 },
        { "M8-RL", 8 }, { "M8-RR", 9 },
    };

    public async Task Execute(Ulid orderId, Workflow workflow)
    {
        AutoMesMetrics.RecordSagaStarted();

        try
        {
            var state = await workflow.States.CreateOrGetDefault<SagaState>();

            // ── 加载工艺路线（P1 集成：动态计算站映射）──
            var currentOrder = await orderRepo.GetByIdTrackedAsync(orderId)
                ?? throw new InvalidOperationException($"工单 {orderId} 不存在");

            var routing = currentOrder.RoutingId != default
                ? await routingRepo.GetByIdAsync(currentOrder.RoutingId)
                : null;
            routing ??= await routingRepo.GetActiveByProductAsync(currentOrder.ProductCode);

            // 从 Routing 动态计算映射（未找到则回退硬编码）
            var stationEquipment = routing is not null
                ? BuildStationEquipmentMap(routing)
                : FallbackStationEquipment;
            var stationLastSeq = routing is not null
                ? BuildStationLastSequenceMap(routing)
                : FallbackStationLastSeq;
            var boltSeq = routing is not null
                ? BuildBoltSequenceMap(routing)
                : FallbackBoltSequence;

            // ── 安全联锁：Effect 之外（重放时重新实时评估）──

            if (currentOrder.Status == OrderStatus.Created)
            {
                currentOrder.Release();
                await orderRepo.SaveChangesAsync();
            }

            if (currentOrder.Status == OrderStatus.Released)
            {
                currentOrder.Start();
                await orderRepo.SaveChangesAsync();
                state.CurrentStation = 2;
                await state.Save();
            }

            // ── 站2 合装装配：AtLeastOnce ──
            var eq2 = GetEquipment(stationEquipment, 2, "EQ-ASM-01");
            await CaptureEffectAsync(workflow, $"station-2-assembly-{orderId}", ResiliencyLevel.AtLeastOnce, "station-2", async () =>
            {
                if (await IsStationCompleted(orderId, 2, stationLastSeq))
                    return;

                if (!await IsEquipmentReady(2))
                    throw new Exception("合装设备未就绪");

                await CompleteOperationRange(orderId, 2, 5, "AUTO", eq2);
                state.AssemblyCompletedAt = DateTimeOffset.UtcNow;
                state.CurrentStation = 3;
                await state.Save();
            });

            // ── 站3 螺栓拧紧：AtLeastOnce（每个螺栓独立 Effect）──
            var eq3 = GetEquipment(stationEquipment, 3, "EQ-TQ-01");
            foreach (var bolt in new[] { "M6-FL", "M6-FR", "M8-RL", "M8-RR" })
            {
                var seq = boltSeq.GetValueOrDefault(bolt, 6);
                await CaptureEffectAsync(workflow, $"station-3-torque-{bolt}-{orderId}", ResiliencyLevel.AtLeastOnce, "station-3", async () =>
                {
                    if (await IsOperationCompleted(orderId, seq))
                        return;

                    await CompleteOperation(orderId, seq, "AUTO", eq3);
                });
            }

            // 站3 扭矩复检（seq 10）
            await CaptureEffectAsync(workflow, $"station-3-torque-recheck-{orderId}", ResiliencyLevel.AtLeastOnce, "station-3", async () =>
            {
                if (await IsOperationCompleted(orderId, 10))
                    return;

                await CompleteOperation(orderId, 10, "AUTO", eq3);
                state.TorqueCompletedAt = DateTimeOffset.UtcNow;
                state.CurrentStation = 4;
                await state.Save();
            });

            // ── 站4 液压测试：AtLeastOnce ──
            var eq4 = GetEquipment(stationEquipment, 4, "EQ-HYD-01");
            await CaptureEffectAsync(workflow, $"station-4-hydraulic-{orderId}", ResiliencyLevel.AtLeastOnce, "station-4", async () =>
            {
                if (await IsStationCompleted(orderId, 4, stationLastSeq))
                    return;

                await CompleteOperationRange(orderId, 11, 23, "AUTO", eq4);
                state.HydraulicCompletedAt = DateTimeOffset.UtcNow;
                state.CurrentStation = 5;
                await state.Save();
            });

            // ── 站5 ECU 刷写：AtLeastOnce ──
            var eq5 = GetEquipment(stationEquipment, 5, "EQ-FLS-01");
            await CaptureEffectAsync(workflow, $"station-5-flash-{orderId}", ResiliencyLevel.AtLeastOnce, "station-5", async () =>
            {
                if (await IsStationCompleted(orderId, 5, stationLastSeq))
                    return;

                await CompleteOperationRange(orderId, 24, 27, "AUTO", eq5);
                state.FlashCompletedAt = DateTimeOffset.UtcNow;
                state.CurrentStation = 6;
                await state.Save();
            });

            // ── 站6 功能终检：AtMostOnce ──
            var eq6 = GetEquipment(stationEquipment, 6, "EQ-FT-01");
            await CaptureEffectAsync(workflow, $"station-6-final-test-{orderId}", ResiliencyLevel.AtMostOnce, "station-6", async () =>
            {
                await CompleteOperationRange(orderId, 28, 30, "AUTO", eq6);
                state.FinalTestCompletedAt = DateTimeOffset.UtcNow;
                state.CurrentStation = 7;
                await state.Save();
            });

            // ── 站7 VIN 绑定：AtLeastOnce ──
            var eq7 = GetEquipment(stationEquipment, 7, "EQ-VN-01");
            await CaptureEffectAsync(workflow, $"station-7-vin-bind-{orderId}", ResiliencyLevel.AtLeastOnce, "station-7", async () =>
            {
                if (await IsStationCompleted(orderId, 7, stationLastSeq))
                    return;

                await CompleteOperation(orderId, 31, "AUTO", eq7);
                state.VinBindCompletedAt = DateTimeOffset.UtcNow;
                state.CurrentStation = 8;
                await state.Save();
            });

            // 工单状态停留在 InProgress，等待人工完工确认。
            AutoMesMetrics.RecordSagaCompleted();
        }
        catch
        {
            throw;
        }
    }

    // ═══════════════════════════════════════════
    //  动态映射构建（P1 集成）
    // ═══════════════════════════════════════════

    /// <summary>从工艺路线构建站号→设备码映射（取工站首道工序的 FixtureCode 或根据设备命名约定推断）</summary>
    private static Dictionary<int, string> BuildStationEquipmentMap(Routing routing)
    {
        var map = new Dictionary<int, string>();
        foreach (var station in routing.Operations.Select(o => o.Station).Distinct())
        {
            if (station == 1) continue; // 站1 人工上料，无设备

            var firstOp = routing.GetOperationsByStation(station).FirstOrDefault();
            if (firstOp?.FixtureCode is not null && firstOp.FixtureCode.StartsWith("EQ-"))
            {
                map[station] = firstOp.FixtureCode;
            }
            else
            {
                // 按设备命名约定推断
                map[station] = station switch
                {
                    2 => "EQ-ASM-01",
                    3 => "EQ-TQ-01",
                    4 => "EQ-HYD-01",
                    5 => "EQ-FLS-01",
                    6 => "EQ-FT-01",
                    7 => "EQ-VN-01",
                    _ => throw new ArgumentOutOfRangeException(nameof(station), $"未知工站 {station}")
                };
            }
        }
        return map;
    }

    /// <summary>从工艺路线构建站号→最后一道工序序号映射（幂等哨兵）</summary>
    private static Dictionary<int, int> BuildStationLastSequenceMap(Routing routing)
    {
        return routing.Operations
            .Where(o => o.Station > 1) // 站1 人工处理
            .GroupBy(o => o.Station)
            .ToDictionary(g => g.Key, g => g.Max(o => o.Sequence));
    }

    /// <summary>从工艺路线构建站3 螺栓编码→工序序号映射</summary>
    private static Dictionary<string, int> BuildBoltSequenceMap(Routing routing)
    {
        var station3Ops = routing.GetOperationsByStation(3);
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var op in station3Ops)
        {
            if (op.OperationName.Contains("M6-FL")) map["M6-FL"] = op.Sequence;
            else if (op.OperationName.Contains("M6-FR")) map["M6-FR"] = op.Sequence;
            else if (op.OperationName.Contains("M8-RL")) map["M8-RL"] = op.Sequence;
            else if (op.OperationName.Contains("M8-RR")) map["M8-RR"] = op.Sequence;
        }

        return map;
    }

    /// <summary>获取工站设备码，回退到默认值</summary>
    private static string GetEquipment(Dictionary<int, string> map, int station, string fallback)
        => map.GetValueOrDefault(station, fallback);

    // ═══════════════════════════════════════════
    //  工序操作辅助方法
    // ═══════════════════════════════════════════

    /// <summary>
    /// 设备就绪检查。
    /// 当前为模拟环境占位实现：始终放行，由 Effect 的 AtLeastOnce 幂等保证重放安全。
    /// TODO(T2.16): 真实 OPC UA 驱动接入后，改为读取设备运行状态做硬联锁。
    /// </summary>
    private static Task<bool> IsEquipmentReady(int station)
        => Task.FromResult(true);

    /// <summary>检查指定工序是否已完工（幂等哨兵）</summary>
    private async Task<bool> IsOperationCompleted(Ulid orderId, int sequence)
    {
        var op = await operationRepo.GetByOrderAndSequenceAsync(orderId, sequence);
        return op?.Status == OperationStatus.Completed;
    }

    /// <summary>检查指定工站是否已完工（检查该站最后一道工序，从动态映射读取）</summary>
    private async Task<bool> IsStationCompleted(Ulid orderId, int station, Dictionary<int, int> stationLastSeq)
    {
        if (!stationLastSeq.TryGetValue(station, out var lastSeq))
            return false;
        return await IsOperationCompleted(orderId, lastSeq);
    }

    /// <summary>完成单道工序：读取（跟踪）→启动→完工→保存</summary>
    private async Task CompleteOperation(Ulid orderId, int sequence, string operatorId, string equipmentId)
    {
        var op = await operationRepo.GetByOrderAndSequenceTrackedAsync(orderId, sequence);
        if (op is null || op.Status == OperationStatus.Completed)
            return;

        var now = DateTimeOffset.UtcNow;
        if (op.Status == OperationStatus.Pending)
            op.Start(operatorId, equipmentId, now);
        op.Complete(now);
        await operationRepo.SaveChangesAsync();
    }

    /// <summary>完成一个范围内的所有工序（同一工站，跟踪查询）</summary>
    private async Task CompleteOperationRange(Ulid orderId, int fromSeq, int toSeq, string operatorId, string equipmentId)
    {
        var ops = await operationRepo.GetByOrderIdTrackedAsync(orderId);
        var now = DateTimeOffset.UtcNow;

        foreach (var op in ops.Where(o => o.Sequence >= fromSeq && o.Sequence <= toSeq && o.Status == OperationStatus.Pending))
        {
            op.Start(operatorId, equipmentId, now);
            op.Complete(now);
        }

        await operationRepo.SaveChangesAsync();
    }

    private static async Task CaptureEffectAsync(
        Workflow workflow,
        string effectId,
        ResiliencyLevel resiliencyLevel,
        string stage,
        Func<Task> effect)
    {
        try
        {
            await workflow.Effect.Capture(effectId, effect, resiliencyLevel);
        }
        catch
        {
            AutoMesMetrics.RecordSagaEffectFailure(stage, effectId);
            throw;
        }
    }

    /// <summary>Saga 状态持久化（存 PostgreSQL）</summary>
    public class SagaState : FlowState
    {
        public int CurrentStation { get; set; } = 1;
        public DateTimeOffset? AssemblyCompletedAt { get; set; }
        public DateTimeOffset? TorqueCompletedAt { get; set; }
        public DateTimeOffset? HydraulicCompletedAt { get; set; }
        public DateTimeOffset? FlashCompletedAt { get; set; }
        public DateTimeOffset? FinalTestCompletedAt { get; set; }
        public DateTimeOffset? VinBindCompletedAt { get; set; }
    }
}

/// <summary>安全联锁异常（Effect 之外触发，重放时重新评估）</summary>
public class SafetyInterlockException(string message) : Exception(message);
