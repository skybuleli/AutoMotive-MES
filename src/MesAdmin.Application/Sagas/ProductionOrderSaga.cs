using Cleipnir.ResilientFunctions;
using Cleipnir.ResilientFunctions.CoreRuntime.Invocation;
using Cleipnir.ResilientFunctions.Domain;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Sagas;

/// <summary>
/// 生产工单 Saga（31 工序 × 7 站编排）。
/// 对应 PRD M01 / TAD v2 Effect 策略矩阵。
///
/// Effect 策略：
///   - 站1 上料扫码：Effect 之外（安全联锁、人工确认）
///   - 站2-5,7：AtLeastOnce（不可丢失，幂等保护：先读后写工序状态）
///   - 站6 功能终检：AtMostOnce（重放复用上次结果）
///
/// 工站→工序序号映射（来自 DefaultRouting）：
///   站1: seq 1     (上料扫码)
///   站2: seq 2-5   (合装装配: HCU定位/ECU预装/电机安装/线束连接)
///   站3: seq 6-10  (螺栓拧紧: M6-FL/M6-FR/M8-RL/M8-RR/扭矩复检)
///   站4: seq 11-23 (液压测试: 12路电磁阀+建压保压泄压)
///   站5: seq 24-27 (ECU刷写: Bootloader/固件/标定/CRC32)
///   站6: seq 28-30 (功能终检: CAN通信/传感器标定/ESP模拟)
///   站7: seq 31    (VIN绑定+标签打印)
/// </summary>
public class ProductionOrderSaga(
    IProductionOrderRepository orderRepo,
    IWorkOrderOperationRepository operationRepo)
{
    /// <summary>站号 → PLC 设备码映射（与 Equipment.DefaultEquipment 一致）。
    /// 站1 为人工上料扫码，无 PLC 设备，不在此映射中（跳过就绪检查）。</summary>
    private static readonly Dictionary<int, string> StationEquipmentCode = new()
    {
        { 2, "EQ-ASM-01" },  // 合装工作站
        { 3, "EQ-TQ-01" },   // 螺栓拧紧机
        { 4, "EQ-HYD-01" },  // 液压测试台
        { 5, "EQ-FLS-01" },  // ECU 刷写台
        { 6, "EQ-FT-01" },   // 功能终检台
        { 7, "EQ-VN-01" },   // VIN 绑定台
    };

    /// <summary>每个工站的代表性工序序号（用于幂等检查的"哨兵"工序）</summary>
    private static readonly Dictionary<int, int> StationLastSequence = new()
    {
        { 2, 5 },   // 线束连接（站2 最后一道）
        { 3, 10 },  // 扭矩复检（站3 最后一道）
        { 4, 23 },  // 建压保压泄压循环（站4 最后一道）
        { 5, 27 },  // CRC32 校验确认（站5 最后一道）
        { 7, 31 },  // VIN 绑定（站7）
    };

    /// <summary>站3 四个螺栓对应的工序序号</summary>
    private static readonly Dictionary<string, int> BoltSequence = new()
    {
        { "M6-FL", 6 },  // TQ-01
        { "M6-FR", 7 },  // TQ-02
        { "M8-RL", 8 },  // TQ-03
        { "M8-RR", 9 },  // TQ-04
    };

    public async Task Execute(Ulid orderId, Workflow workflow)
    {
        var state = await workflow.States.CreateOrGetDefault<SagaState>();

        // ── 安全联锁：Effect 之外（重放时重新实时评估）──
        // 站1 为人工上料扫码工位，无 PLC 设备，跳过设备就绪检查。
        // 站2 起的设备就绪检查在各工站 Effect 内进行。

        // Saga 拥有完整状态机控制权：从 DB 读取最新状态（跟踪），推进 Created→Released→InProgress
        var currentOrder = await orderRepo.GetByIdTrackedAsync(orderId)
            ?? throw new InvalidOperationException($"工单 {orderId} 不存在");

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

        // ── 站2 合装装配（seq 2-5）：AtLeastOnce ──
        await workflow.Effect.Capture($"station-2-assembly-{orderId}", async () =>
        {
            if (await IsStationCompleted(orderId, 2))
                return; // 幂等：哨兵工序 seq=5 已完工

            if (!await IsEquipmentReady(2))
                throw new Exception("合装设备未就绪");

            // 完成站2 全部工序（seq 2-5: HCU定位→ECU预装→电机安装→线束连接）
            await CompleteOperationRange(orderId, 2, 5, "AUTO", "EQ-ASM-01");

            state.AssemblyCompletedAt = DateTimeOffset.UtcNow;
            state.CurrentStation = 3;
            await state.Save();
        }, ResiliencyLevel.AtLeastOnce);

        // ── 站3 螺栓拧紧（seq 6-9 四个独立 Effect + seq 10 复检）：AtLeastOnce ──
        foreach (var bolt in new[] { "M6-FL", "M6-FR", "M8-RL", "M8-RR" })
        {
            var seq = BoltSequence[bolt];
            await workflow.Effect.Capture($"station-3-torque-{bolt}-{orderId}", async () =>
            {
                if (await IsOperationCompleted(orderId, seq))
                    return; // 幂等

                // 扭矩+角度法拧紧，曲线实时采集，不通过抛 TorqueException
                await CompleteOperation(orderId, seq, "AUTO", "EQ-TQ-01");
            }, ResiliencyLevel.AtLeastOnce);
        }

        // 站3 扭矩复检（seq 10）
        await workflow.Effect.Capture($"station-3-torque-recheck-{orderId}", async () =>
        {
            if (await IsOperationCompleted(orderId, 10))
                return;

            await CompleteOperation(orderId, 10, "AUTO", "EQ-TQ-01");
            state.TorqueCompletedAt = DateTimeOffset.UtcNow;
            state.CurrentStation = 4;
            await state.Save();
        }, ResiliencyLevel.AtLeastOnce);

        // ── 站4 液压测试（seq 11-23）：AtLeastOnce ──
        await workflow.Effect.Capture($"station-4-hydraulic-{orderId}", async () =>
        {
            if (await IsStationCompleted(orderId, 4))
                return; // 幂等：哨兵工序 seq=23 已完工

            // 12 路电磁阀逐一测试 + 建压/保压/泄压循环
            await CompleteOperationRange(orderId, 11, 23, "AUTO", "EQ-HYD-01");

            state.HydraulicCompletedAt = DateTimeOffset.UtcNow;
            state.CurrentStation = 5;
            await state.Save();
        }, ResiliencyLevel.AtLeastOnce);

        // ── 站5 ECU 刷写（seq 24-27）：AtLeastOnce ──
        await workflow.Effect.Capture($"station-5-flash-{orderId}", async () =>
        {
            if (await IsStationCompleted(orderId, 5))
                return; // 幂等：哨兵工序 seq=27 已完工

            // Bootloader → 应用固件 → 标定参数 → CRC32 校验和确认
            await CompleteOperationRange(orderId, 24, 27, "AUTO", "EQ-FLS-01");

            state.FlashCompletedAt = DateTimeOffset.UtcNow;
            state.CurrentStation = 6;
            await state.Save();
        }, ResiliencyLevel.AtLeastOnce);

        // ── 站6 功能终检（seq 28-30）：AtMostOnce ──
        await workflow.Effect.Capture($"station-6-final-test-{orderId}", async () =>
        {
            // AtMostOnce：重放时复用上次结果，不重复执行副作用
            // CAN 通信 → 传感器标定 → ESP 功能模拟
            await CompleteOperationRange(orderId, 28, 30, "AUTO", "EQ-FT-01");

            state.FinalTestCompletedAt = DateTimeOffset.UtcNow;
            state.CurrentStation = 7;
            await state.Save();
        }, ResiliencyLevel.AtMostOnce);

        // ── 站7 VIN 绑定 + 标签打印（seq 31）：AtLeastOnce ──
        await workflow.Effect.Capture($"station-7-vin-bind-{orderId}", async () =>
        {
            if (await IsStationCompleted(orderId, 7))
                return; // 幂等：seq=31 已完工

            // 追溯标签打印 + VIN 预绑定 + 入库
            await CompleteOperation(orderId, 31, "AUTO", "EQ-VN-01");

            state.VinBindCompletedAt = DateTimeOffset.UtcNow;
            state.CurrentStation = 8;
            await state.Save();
        }, ResiliencyLevel.AtLeastOnce);

        // 工单状态停留在 InProgress，等待 T1.8 人工完工确认接口提交良品/不良数量。
        // Saga 负责工序编排，不绕过质量审核直接把整单置为 Completed。
    }

    // ═══════════════════════════════════════════
    // 工序操作辅助方法
    // ═══════════════════════════════════════════

    /// <summary>
    /// 设备就绪检查。
    /// 当前为模拟环境占位实现：始终放行，由 Effect 的 AtLeastOnce 幂等保证重放安全。
    /// TODO(T2.16): 真实 OPC UA 驱动接入后，改为读取设备运行状态做硬联锁：
    /// <code>
    /// var snapshot = await plc.ReadSnapshotAsync(code);
    /// return snapshot?.Status == EquipmentStatus.Running;
    /// </code>
    /// </summary>
    private Task<bool> IsEquipmentReady(int station)
        => Task.FromResult(true); // 模拟环境占位：始终放行

    /// <summary>检查指定工序是否已完工（幂等哨兵）</summary>
    private async Task<bool> IsOperationCompleted(Ulid orderId, int sequence)
    {
        var op = await operationRepo.GetByOrderAndSequenceAsync(orderId, sequence);
        return op?.Status == OperationStatus.Completed;
    }

    /// <summary>检查指定工站是否已完工（检查该站最后一道工序）</summary>
    private async Task<bool> IsStationCompleted(Ulid orderId, int station)
    {
        if (!StationLastSequence.TryGetValue(station, out var lastSeq))
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
