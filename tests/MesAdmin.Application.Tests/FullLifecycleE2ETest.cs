using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MesAdmin.Application.Tests;

/// <summary>
/// T1.x 全链路 E2E 集成测试：真实 PostgreSQL + 种子数据，验证工单完整生命周期。
/// 创建工单 → T1.4 齐套检查（BOM 展开 + 库存校验）→ 自动 Release
/// → T1.2 开工（Saga 模拟）→ InProgress → 工序执行
/// → T1.8 完工确认 + T1.17 物料消耗反冲（BOM 扣减 + 差异检查 + SAP 同步记录）
/// → T1.3 关闭
/// </summary>
[Collection("DatabaseIntegration")]
public class FullLifecycleE2ETest
{
    private readonly DatabaseFixture _fixture;

    public FullLifecycleE2ETest(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Create_KitCheck_StartSimulate_Complete_Backflush_Close_FullLifecycle()
    {
        using var scope = _fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;

        // ── 解析全部仓储 ──
        var orderRepo = sp.GetRequiredService<IProductionOrderRepository>();
        var opRepo = sp.GetRequiredService<IWorkOrderOperationRepository>();
        var batchRepo = sp.GetRequiredService<IMaterialBatchRepository>();
        var bomRepo = sp.GetRequiredService<IBomRepository>();
        var jitRepo = sp.GetRequiredService<IJitPullSignalRepository>();
        var goodsReceiptRepo = sp.GetRequiredService<IGoodsReceiptRepository>();
        var bindingRepo = sp.GetRequiredService<IMaterialBindingRepository>();
        var consumptionRepo = sp.GetRequiredService<IMaterialConsumptionRepository>();
        var varianceRepo = sp.GetRequiredService<IConsumptionVarianceRepository>();
        var sapSyncRepo = sp.GetRequiredService<ISapInventorySyncRecordRepository>();

        var routingRepo = sp.GetRequiredService<IRoutingRepository>();

        // ── Step 1: 创建工单 (T1.1) ──
        var createHandler = new CreateOrderHandler(orderRepo, opRepo, routingRepo);
        var order = await createHandler.ExecuteAsync(
            new CreateOrderCommand("ESP-9.0", MesDataSeeder.Esp90BomVersion, Ulid.NewUlid(), 100, (short)1),
            default);

        Assert.Equal(OrderStatus.Created, order.Status);
        Assert.Equal("ESP-9.0", order.ProductCode);
        Assert.Equal(100, order.PlannedQuantity);
        Assert.StartsWith($"WO-{DateTimeOffset.Now:yyyyMMdd}-", order.OrderNumber);

        // 验证 31 道工序已初始化
        var ops = await opRepo.GetByOrderIdAsync(order.Id, default);
        Assert.Equal(31, ops.Count);
        Assert.Contains(ops, o => o.OperationCode == "LOAD-01" && o.Station == 1);  // 站1 上料

        var kitLogger = sp.GetRequiredService<ILogger<KitCheckHandler>>();

        // ── Step 2: 齐套检查 (T1.4) ──
        // 种子数据包含足量 Qualified 物料库存 → 应通过
        var kitHandler = new KitCheckHandler(orderRepo, batchRepo, bomRepo, jitRepo, kitLogger);
        var kitResult = await kitHandler.ExecuteAsync(new KitCheckCommand(order.Id), default);

        Assert.True(kitResult.IsPassed, "齐套检查应通过（种子数据库存充足）");

        // 验证工单已自动 Release
        var released = await orderRepo.GetByIdAsync(order.Id, default);
        Assert.NotNull(released);
        Assert.Equal(OrderStatus.Released, released.Status);

        // 验证未生成 JIT 拉动信号（库存充足）
        var jitSignals = await jitRepo.GetByOrderIdAsync(order.Id, default);
        Assert.Empty(jitSignals);

        // ── Step 3: 开工 (T1.2) — 手动模拟 Saga 状态推进 ──
        // StartOrderHandler 发布 OrderStartedEvent → Saga 监听后调用 order.Start()
        // 测试中无 Cleipnir 运行时，直接模拟 Saga 行为
        var orderForStart = await orderRepo.GetByIdTrackedAsync(order.Id, default);
        Assert.NotNull(orderForStart);
        orderForStart!.Start();
        await orderRepo.SaveChangesAsync(default);

        var inProgress = await orderRepo.GetByIdAsync(order.Id, default);
        Assert.Equal(OrderStatus.InProgress, inProgress!.Status);

        // ── Step 4: 完工确认 (T1.8) + 物料反冲 (T1.17) ──
        // 4a. 状态推进：InProgress → Completed
        var orderForComplete = await orderRepo.GetByIdTrackedAsync(order.Id, default);
        Assert.NotNull(orderForComplete);
        orderForComplete!.Complete(100, 0, DateTimeOffset.UtcNow);
        await orderRepo.SaveChangesAsync(default);

        var completed = await orderRepo.GetByIdAsync(order.Id, default);
        Assert.Equal(OrderStatus.Completed, completed!.Status);
        Assert.Equal(100, completed.QualifiedQuantity);
        Assert.Equal(0, completed.DefectiveQuantity);
        Assert.NotNull(completed.CompletedAt);

        // 4b. 创建成品入库单 + 追溯标签
        var receipt = GoodsReceipt.Create(
            order.Id, order.OrderNumber, order.ProductCode,
            100, "E2E-TEST-REVIEWER", DateTimeOffset.UtcNow);
        await goodsReceiptRepo.AddAsync(receipt, default);
        await goodsReceiptRepo.SaveChangesAsync(default);

        var savedReceipt = await goodsReceiptRepo.GetByOrderIdAsync(order.Id, default);
        Assert.NotNull(savedReceipt);
        Assert.Equal(100, savedReceipt.ReceivedQuantity);
        Assert.Equal("E2E-TEST-REVIEWER", savedReceipt.ReviewerId);
        Assert.NotNull(savedReceipt.TraceabilityLabelCode);
        Assert.StartsWith("ESP9-", savedReceipt.TraceabilityLabelCode);

        // 4c. 物料消耗反冲 (T1.17) — 直接构造 Handler
        var backflushLogger = sp.GetRequiredService<ILogger<BackflushMaterialsHandler>>();
        var backflushHandler = new BackflushMaterialsHandler(
            orderRepo, bomRepo, batchRepo, bindingRepo,
            consumptionRepo, varianceRepo, sapSyncRepo, backflushLogger);
        var backflushResult = await backflushHandler.ExecuteAsync(
            new BackflushMaterialsCommand(order.Id), default);

        Assert.True(backflushResult.Success);
        Assert.True(backflushResult.ConsumedCount > 0, "应消耗至少 1 种物料");

        // 验证消耗记录
        var consumptions = await consumptionRepo.GetByOrderIdAsync(order.Id, default);
        Assert.NotEmpty(consumptions);
        // 验证关键物料（ECU/HCU/电机等）均有消耗记录
        var criticalConsumptions = consumptions.Where(c => c.IsCritical).ToList();
        Assert.Contains(criticalConsumptions, c => c.MaterialCode == "ECU-ESP9-001");
        Assert.Contains(criticalConsumptions, c => c.MaterialCode == "HCU-ESP9-001");
        Assert.Contains(criticalConsumptions, c => c.MaterialCode == "MOT-ESP9-001");

        // 验证库存已扣减
        var availableAfter = await batchRepo.GetAvailableQuantitiesAsync(
            ["ECU-ESP9-001", "HCU-ESP9-001", "MOT-ESP9-001"], default);
        // 种子库存 1000 件，消耗 ≥100 件（BOM 标准用量 1/件 × 100 件）
        Assert.True(availableAfter["ECU-ESP9-001"] < 1000, "ECU 库存应已扣减");
        Assert.True(availableAfter["HCU-ESP9-001"] < 1000, "HCU 库存应已扣减");
        Assert.True(availableAfter["MOT-ESP9-001"] < 1000, "MOT 库存应已扣减");

        // 验证 SAP 同步记录已生成
        var sapSyncRecords = await sapSyncRepo.GetPendingSyncAsync(default);
        var orderSapRecords = sapSyncRecords.Where(s => s.OrderId == order.Id).ToList();
        Assert.NotEmpty(orderSapRecords);
        Assert.All(orderSapRecords, s => Assert.Equal("261", s.MovementType));

        // ── Step 5: 关闭工单 (T1.3) ──
        var closeHandler = new CloseOrderHandler(orderRepo, sp.GetRequiredService<ISapOrderSyncRecordRepository>());
        var closed = await closeHandler.ExecuteAsync(new CloseOrderCommand(order.Id), default);

        Assert.Equal(OrderStatus.Closed, closed.Status);

        // ── Final: 验证最终状态 − 全生命周期总检 ──
        var final = await orderRepo.GetByIdAsync(order.Id, default);
        Assert.NotNull(final);
        Assert.Equal(OrderStatus.Closed, final.Status);
        Assert.Equal(100, final.PlannedQuantity);
        Assert.Equal(100, final.QualifiedQuantity);
        Assert.Equal(0, final.DefectiveQuantity);
    }

    [Fact]
    public async Task KitCheck_ShouldFail_WhenStockInsufficient()
    {
        using var scope = _fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var orderRepo = sp.GetRequiredService<IProductionOrderRepository>();
        var opRepo = sp.GetRequiredService<IWorkOrderOperationRepository>();
        var batchRepo = sp.GetRequiredService<IMaterialBatchRepository>();
        var bomRepo = sp.GetRequiredService<IBomRepository>();
        var jitRepo = sp.GetRequiredService<IJitPullSignalRepository>();
        var kitLogger = sp.GetRequiredService<ILogger<KitCheckHandler>>();

        // 创建一个使用 ESP-9.1 BOM 的工单
        // ESP-9.1 BOM 含关键物料 ECU-ESP9-002（ECU V4 增强型），
        // 但种子数据中没有该物料的库存批次 → 齐套检查应失败
        var routingRepo = sp.GetRequiredService<IRoutingRepository>();
        var createHandler = new CreateOrderHandler(orderRepo, opRepo, routingRepo);
        var order = await createHandler.ExecuteAsync(
            new CreateOrderCommand("ESP-9.1", MesDataSeeder.Esp91BomVersion, Ulid.NewUlid(), 100, (short)1),
            default);

        Assert.Equal(OrderStatus.Created, order.Status);

        // 执行齐套检查
        var kitHandler = new KitCheckHandler(orderRepo, batchRepo, bomRepo, jitRepo, kitLogger);
        var kitResult = await kitHandler.ExecuteAsync(new KitCheckCommand(order.Id), default);

        // ESP-9.1 BOM 有关键物料（ECU-ESP9-002）但无库存 → 齐套检查应失败
        Assert.False(kitResult.IsPassed, "ESP-9.1 BOM 含关键物料 ECU-ESP9-002 但无库存，齐套检查应失败");

        // 验证 JIT 拉动信号已生成
        var jitSignals = await jitRepo.GetByOrderIdAsync(order.Id, default);
        Assert.NotEmpty(jitSignals);
        Assert.Contains(jitSignals, s => s.MaterialCode == "ECU-ESP9-002");

        // 工单保持 Created 状态（未放行）
        var stillCreated = await orderRepo.GetByIdAsync(order.Id, default);
        Assert.Equal(OrderStatus.Created, stillCreated!.Status);
    }
}
