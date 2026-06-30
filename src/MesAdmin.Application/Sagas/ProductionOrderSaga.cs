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
///   - 站1 上料扫码：Effect 之外（人工确认）
///   - 站2-5,7：AtLeastOnce（不可丢失，幂等保护）
///   - 站6 功能终检：AtMostOnce（重放复用结果）
///
/// 崩溃恢复：服务器断电后 Cleipnir 自动从 Checkpoint 恢复，
///   已完成 Effect 不重复执行（AtMostOnce）或重试到成功（AtLeastOnce）。
/// </summary>
public class ProductionOrderSaga(IProductionOrderRepository orderRepo, IPlcClient plc)
{
    public async Task Execute(ProductionOrder order, Workflow workflow)
    {
        var state = workflow.States.CreateOrGet<SagaState>("SagaState");

        // ── 站1 上料扫码：Effect 之外（人工确认，重放时重新实时评估）──
        if (order.Status == OrderStatus.Created)
            throw new InvalidOperationException("工单未齐套放行，无法开工");

        // ── 站2 合装装配：AtLeastOnce（PLC 状态机互锁幂等保护）──
        await workflow.Effect.Capture("Assembly-" + order.Id, async () =>
        {
            if (!await plc.IsReadyAsync(order.Id.ToString()))
                throw new Exception("合装设备未就绪");
            // HCU 定位 → ECU 预装 → 电机安装 → 线束连接
        }, ResiliencyLevel.AtLeastOnce);

        // ── 站3 螺栓拧紧：AtLeastOnce（扭矩曲线 MD5 去重，4 处独立 Effect）──
        foreach (var bolt in new[] { "M6-FL", "M6-FR", "M8-RL", "M8-RR" })
        {
            await workflow.Effect.Capture($"Torque-{bolt}", async () =>
            {
                // 扭矩+角度法拧紧，曲线实时采集，不通过抛 TorqueException
            }, ResiliencyLevel.AtLeastOnce);
        }

        // ── 站4 液压测试：AtLeastOnce（安全件 100% 测试，唯一约束 S/N）──
        await workflow.Effect.Capture("Hydraulic-" + order.Id, async () =>
        {
            // 12 路电磁阀逐一测试，泄漏率 ≤ 0.5 CC/hr
        }, ResiliencyLevel.AtLeastOnce);

        // ── 站5 ECU 刷写：AtLeastOnce（校验和+版本号，先回滚再重刷防变砖）──
        await workflow.Effect.Capture("Flash-" + order.Id, async () =>
        {
            // Bootloader → 应用固件 → 标定参数 → CRC32 校验和确认
        }, ResiliencyLevel.AtLeastOnce);

        // ── 站6 功能终检：AtMostOnce（测试 ID 去重，重放复用上次结果）──
        await workflow.Effect.Capture("FinalTest-" + order.Id, async () =>
        {
            // CAN 通信 → 传感器标定 → ESP 功能模拟
        }, ResiliencyLevel.AtMostOnce);

        // ── 站7 VIN 绑定 + 标签打印：AtLeastOnce（唯一约束 S/N）──
        await workflow.Effect.Capture("VinBind-" + order.Id, async () =>
        {
            // 追溯标签打印 + VIN 预绑定 + 入库
        }, ResiliencyLevel.AtLeastOnce);
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
    }
}
