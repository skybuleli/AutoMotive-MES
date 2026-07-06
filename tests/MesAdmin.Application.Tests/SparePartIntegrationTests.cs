using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MesAdmin.Application.Tests;

/// <summary>
/// 备件管理集成测试（T2.18）：使用真实 PostgreSQL + MesDbContext 验证全链路。
/// 覆盖备件 CRUD → 库存操作（盘点/补货/消耗）→ 库存水平判定 → 采购申请全流程。
/// 注意：所有测试使用 $\"SP-{Ulid.NewUlid()}\" 确保物料编码全局唯一，避免唯一约束冲突。
/// </summary>
[Collection("DatabaseIntegration")]
public class SparePartIntegrationTests
{
    private readonly DatabaseFixture _fixture;

    public SparePartIntegrationTests(DatabaseFixture fixture) => _fixture = fixture;

    private static string UniqueMatCode(string prefix) => $"{prefix}-{Random.Shared.Next():x8}";

    // ═══════════════════════════════════════════════════════════
    //  SparePart CRUD
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Create_AndGetById_ShouldPersistAndRetrieve()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISparePartRepository>();

        var part = SparePart.Create(
            Ulid.NewUlid(), UniqueMatCode("SP-TQS"), "扭矩传感器",
            "0-100Nm / 精度 0.5%", "个", 5, 2, "EQ-TQ-01", "专用");

        await repo.AddAsync(part, default);

        var loaded = await repo.GetByIdAsync(part.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal(part.MaterialCode, loaded.MaterialCode);
        Assert.Equal("扭矩传感器", loaded.MaterialName);
        Assert.Equal("个", loaded.Unit);
        Assert.Equal(0, loaded.CurrentQuantity);
        Assert.Equal(5, loaded.SafetyStock);
        Assert.Equal(2, loaded.MinimumStock);
        Assert.Equal("EQ-TQ-01", loaded.EquipmentCode);
    }

    [Fact]
    public async Task GetAll_ShouldReturnAllParts()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISparePartRepository>();

        var p1 = SparePart.Create(Ulid.NewUlid(), UniqueMatCode("SP-GA"), "A", "Spec", "个", 5, 2);
        var p2 = SparePart.Create(Ulid.NewUlid(), UniqueMatCode("SP-GB"), "B", "Spec", "个", 5, 2);
        await repo.AddAsync(p1, default);
        await repo.AddAsync(p2, default);

        var all = await repo.GetAllAsync(default);
        Assert.Contains(all, p => p.Id == p1.Id);
        Assert.Contains(all, p => p.Id == p2.Id);
    }

    [Fact]
    public async Task GetByEquipment_ShouldReturnMatchingParts()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISparePartRepository>();

        var part = SparePart.Create(
            Ulid.NewUlid(), UniqueMatCode("SP-GE"), "O 型密封圈", "12×2mm",
            "个", 50, 10, "EQ-HYD-01");

        await repo.AddAsync(part, default);

        var results = await repo.GetByEquipmentAsync("EQ-HYD-01", default);
        Assert.Contains(results, p => p.Id == part.Id);
    }

    // ═══════════════════════════════════════════════════════════
    //  库存操作
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateStock_ShouldPersistNewQuantity()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISparePartRepository>();

        var part = SparePart.Create(
            Ulid.NewUlid(), UniqueMatCode("SP-US"), "名称", "Spec", "个", 5, 2);
        await repo.AddAsync(part, default);

        part.UpdateStock(42);
        await repo.UpdateAsync(part, default);

        var loaded = await repo.GetByIdAsync(part.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal(42, loaded.CurrentQuantity);
    }

    [Fact]
    public async Task Restock_ShouldIncreaseQuantity()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISparePartRepository>();

        var part = SparePart.Create(
            Ulid.NewUlid(), UniqueMatCode("SP-RS"), "名称", "Spec", "个", 5, 2);
        await repo.AddAsync(part, default);

        part.Restock(30);
        await repo.UpdateAsync(part, default);

        var loaded = await repo.GetByIdAsync(part.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal(30, loaded.CurrentQuantity);
    }

    [Fact]
    public async Task Consume_ShouldDecreaseQuantity()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISparePartRepository>();

        var part = SparePart.Create(
            Ulid.NewUlid(), UniqueMatCode("SP-CS"), "名称", "Spec", "个", 5, 2);
        part.Restock(20);
        await repo.AddAsync(part, default);

        part.Consume(8);
        await repo.UpdateAsync(part, default);

        var loaded = await repo.GetByIdAsync(part.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal(12, loaded.CurrentQuantity);
    }

    // ═══════════════════════════════════════════════════════════
    //  低库存查询
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task GetLowStock_ShouldReturnPartsBelowSafety()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISparePartRepository>();

        var enough = SparePart.Create(
            Ulid.NewUlid(), UniqueMatCode("SP-LS-E"), "充足", "Spec", "个", 5, 2);
        enough.Restock(10);

        var low = SparePart.Create(
            Ulid.NewUlid(), UniqueMatCode("SP-LS-L"), "过低", "Spec", "个", 5, 2);
        low.Restock(3);

        await repo.AddAsync(enough, default);
        await repo.AddAsync(low, default);

        var lowStock = await repo.GetLowStockAsync(default);
        Assert.Contains(lowStock, p => p.Id == low.Id);
        Assert.DoesNotContain(lowStock, p => p.Id == enough.Id);
    }

    [Fact]
    public async Task GetNeedsRestock_ShouldReturnPartsBelowMinimum()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISparePartRepository>();

        var ok = SparePart.Create(
            Ulid.NewUlid(), UniqueMatCode("SP-NR-O"), "正常", "Spec", "个", 10, 5);
        ok.Restock(10);

        var critical = SparePart.Create(
            Ulid.NewUlid(), UniqueMatCode("SP-NR-C"), "临界", "Spec", "个", 10, 5);
        critical.Restock(3);

        await repo.AddAsync(ok, default);
        await repo.AddAsync(critical, default);

        var needs = await repo.GetNeedsRestockAsync(default);
        Assert.Contains(needs, p => p.Id == critical.Id);
        Assert.DoesNotContain(needs, p => p.Id == ok.Id);
    }

    // ═══════════════════════════════════════════════════════════
    //  SparePartUsage — 工单关联备件
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task SparePartUsage_AddAndGetByWorkOrder_ShouldPersistAndRetrieve()
    {
        using var scope = _fixture.Services.CreateScope();
        var spareRepo = scope.ServiceProvider.GetRequiredService<ISparePartRepository>();
        var usageRepo = scope.ServiceProvider.GetRequiredService<ISparePartUsageRepository>();

        var part = SparePart.Create(
            Ulid.NewUlid(), UniqueMatCode("SP-SPU"), "O 型密封圈", "12×2mm", "个", 50, 10);
        part.Restock(100);
        await spareRepo.AddAsync(part, default);

        var orderId = Ulid.NewUlid();
        var usage = SparePartUsage.Create(
            Ulid.NewUlid(), part.Id, orderId, 5, 2.5, "更换密封圈");

        await usageRepo.AddAsync(usage, default);

        var usages = await usageRepo.GetByWorkOrderAsync(orderId, default);
        var loaded = Assert.Single(usages);
        Assert.Equal(part.Id, loaded.SparePartId);
        Assert.Equal(5, loaded.Quantity);
        Assert.Equal(2.5, loaded.UnitPrice);
        Assert.Equal("更换密封圈", loaded.Remarks);
    }

    [Fact]
    public async Task SparePartUsage_GetBySparePart_ShouldReturnAllUsages()
    {
        using var scope = _fixture.Services.CreateScope();
        var usageRepo = scope.ServiceProvider.GetRequiredService<ISparePartUsageRepository>();

        var partId = Ulid.NewUlid();
        var u1 = SparePartUsage.Create(Ulid.NewUlid(), partId, Ulid.NewUlid(), 2);
        var u2 = SparePartUsage.Create(Ulid.NewUlid(), partId, Ulid.NewUlid(), 3);

        await usageRepo.AddAsync(u1, default);
        await usageRepo.AddAsync(u2, default);

        var usages = await usageRepo.GetBySparePartAsync(partId, default);
        Assert.Contains(usages, u => u.Id == u1.Id);
        Assert.Contains(usages, u => u.Id == u2.Id);
    }

    // ═══════════════════════════════════════════════════════════
    //  PurchaseRequest — 采购申请 CRUD
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task PurchaseRequest_CreateAndGet_ShouldPersistAndRetrieve()
    {
        using var scope = _fixture.Services.CreateScope();
        var prRepo = scope.ServiceProvider.GetRequiredService<IPurchaseRequestRepository>();

        var partId = Ulid.NewUlid();
        var pr = PurchaseRequest.Create(
            Ulid.NewUlid(), partId, 10, "库存不足", "OP-001");

        await prRepo.AddAsync(pr, default);

        var loaded = await prRepo.GetByIdAsync(pr.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal("Pending", loaded.Status);
        Assert.Equal(partId, loaded.SparePartId);
        Assert.Equal(10, loaded.Quantity);
        Assert.Equal("库存不足", loaded.Reason);
        Assert.Equal("OP-001", loaded.RequestedBy);
        Assert.StartsWith("PR-", loaded.RequestNumber);
    }

    [Fact]
    public async Task PurchaseRequest_GetList_ShouldSupportStatusFilter()
    {
        using var scope = _fixture.Services.CreateScope();
        var prRepo = scope.ServiceProvider.GetRequiredService<IPurchaseRequestRepository>();

        var partId = Ulid.NewUlid();
        var pending = PurchaseRequest.Create(Ulid.NewUlid(), partId, 5, "缺货", "OP-001");
        var approved = PurchaseRequest.Create(Ulid.NewUlid(), partId, 3, "备货", "OP-002");
        approved.Approve("MGR-001");

        await prRepo.AddAsync(pending, default);
        await prRepo.AddAsync(approved, default);

        var pendings = await prRepo.GetListAsync("Pending", ct: default);
        Assert.Contains(pendings, r => r.Id == pending.Id);
        Assert.DoesNotContain(pendings, r => r.Id == approved.Id);

        var approveds = await prRepo.GetListAsync("Approved", ct: default);
        Assert.Contains(approveds, r => r.Id == approved.Id);
    }

    // ═══════════════════════════════════════════════════════════
    //  PurchaseRequest — 状态机持久化
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task PurchaseRequest_Approve_ShouldPersistStatus()
    {
        using var scope = _fixture.Services.CreateScope();
        var prRepo = scope.ServiceProvider.GetRequiredService<IPurchaseRequestRepository>();

        var pr = PurchaseRequest.Create(
            Ulid.NewUlid(), Ulid.NewUlid(), 10, "缺货", "OP-001");
        await prRepo.AddAsync(pr, default);

        // 使用已跟踪的 pr 实例（AddAsync 后已处于 tracking），避免用 GetByIdAsync 加载游离副本
        pr.Approve("MGR-001");
        await prRepo.UpdateAsync(pr, default);

        // 用新查询验证持久化状态
        var loaded = await prRepo.GetByIdAsync(pr.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal("Approved", loaded.Status);
        Assert.Equal("MGR-001", loaded.ApprovedBy);
        Assert.NotNull(loaded.ApprovedAt);
    }

    [Fact]
    public async Task PurchaseRequest_Cancel_ShouldPersistStatus()
    {
        using var scope = _fixture.Services.CreateScope();
        var prRepo = scope.ServiceProvider.GetRequiredService<IPurchaseRequestRepository>();

        var pr = PurchaseRequest.Create(
            Ulid.NewUlid(), Ulid.NewUlid(), 10, "缺货", "OP-001");
        await prRepo.AddAsync(pr, default);

        pr.Cancel("计划变更");
        await prRepo.UpdateAsync(pr, default);

        var loaded = await prRepo.GetByIdAsync(pr.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal("Cancelled", loaded.Status);
    }

    [Fact]
    public async Task PurchaseRequest_GetPendingBySparePart_ShouldReturnActiveOnly()
    {
        using var scope = _fixture.Services.CreateScope();
        var prRepo = scope.ServiceProvider.GetRequiredService<IPurchaseRequestRepository>();

        var partId = Ulid.NewUlid();
        var active = PurchaseRequest.Create(Ulid.NewUlid(), partId, 5, "缺货", "OP-001");
        var cancelled = PurchaseRequest.Create(Ulid.NewUlid(), partId, 3, "备货", "OP-002");
        cancelled.Cancel("不需要");

        await prRepo.AddAsync(active, default);
        await prRepo.AddAsync(cancelled, default);

        var pending = await prRepo.GetPendingBySparePartAsync(partId, default);
        Assert.Contains(pending, r => r.Id == active.Id);
        Assert.DoesNotContain(pending, r => r.Id == cancelled.Id);
    }

    // ═══════════════════════════════════════════════════════════
    //  全流程场景：创建备件 → 补货 → 消耗 → 库存检查 → 采购申请
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task FullLifecycle_Restock_Consume_Check_Request_ShouldWork()
    {
        using var scope = _fixture.Services.CreateScope();
        var spareRepo = scope.ServiceProvider.GetRequiredService<ISparePartRepository>();
        var prRepo = scope.ServiceProvider.GetRequiredService<IPurchaseRequestRepository>();

        // 创建备件（安全库存=10，最低库存=5）
        var part = SparePart.Create(
            Ulid.NewUlid(), UniqueMatCode("SP-FLC"), "全流程测试备件",
            "Spec", "个", 10, 5);
        await spareRepo.AddAsync(part, default);

        // 初始 Red
        Assert.Equal(InventoryAlertLevel.Red, part.GetStockLevel());
        Assert.True(part.NeedsPurchaseRequest);

        // 补货
        part.Restock(20);
        await spareRepo.UpdateAsync(part, default);

        var afterRestock = await spareRepo.GetByIdAsync(part.Id, default);
        Assert.NotNull(afterRestock);
        Assert.Equal(20, afterRestock.CurrentQuantity);
        Assert.Equal(InventoryAlertLevel.Normal, afterRestock.GetStockLevel());

        // 消耗到低于安全库存（Yellow）
        part.Consume(11);
        await spareRepo.UpdateAsync(part, default);

        var afterConsume = await spareRepo.GetByIdAsync(part.Id, default);
        Assert.NotNull(afterConsume);
        Assert.Equal(9, afterConsume.CurrentQuantity);
        Assert.Equal(InventoryAlertLevel.Yellow, afterConsume.GetStockLevel());

        // 消耗到低于最低库存（Red）→ 触发采购
        part.Consume(5);
        await spareRepo.UpdateAsync(part, default);

        var afterCritical = await spareRepo.GetByIdAsync(part.Id, default);
        Assert.NotNull(afterCritical);
        Assert.Equal(4, afterCritical.CurrentQuantity);
        Assert.Equal(InventoryAlertLevel.Red, afterCritical.GetStockLevel());
        Assert.True(afterCritical.NeedsPurchaseRequest);

        // 创建采购申请 — 使用已跟踪的 pr 实例，避免 Entity Tracking Conflict
        var pr = PurchaseRequest.Create(
            Ulid.NewUlid(), part.Id, afterCritical.SuggestedPurchaseQuantity,
            $"库存不足（当前 {afterCritical.CurrentQuantity}）", "OP-001");
        await prRepo.AddAsync(pr, default);

        // 直接操作 pr（已由 AddAsync 跟踪），不加载游离副本
        Assert.Equal("Pending", pr.Status);
        Assert.Equal(part.Id, pr.SparePartId);

        pr.Approve("MGR-001");
        await prRepo.UpdateAsync(pr, default);

        // 用新查询验证持久化状态
        var approvedPr = await prRepo.GetByIdAsync(pr.Id, default);
        Assert.NotNull(approvedPr);
        Assert.Equal("Approved", approvedPr.Status);
        Assert.Equal("MGR-001", approvedPr.ApprovedBy);
    }

    // ═══════════════════════════════════════════════════════════
    //  Edge cases
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateStock_ToZero_ShouldPersist()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISparePartRepository>();

        var part = SparePart.Create(
            Ulid.NewUlid(), UniqueMatCode("SP-ZERO"), "零库存", "Spec", "个", 5, 2);
        part.Restock(10);
        await repo.AddAsync(part, default);

        part.UpdateStock(0);
        await repo.UpdateAsync(part, default);

        var loaded = await repo.GetByIdAsync(part.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal(0, loaded.CurrentQuantity);
        Assert.Equal(InventoryAlertLevel.Red, loaded.GetStockLevel());
    }

    [Fact]
    public async Task Consume_ExactStock_ShouldReachZero()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISparePartRepository>();

        var part = SparePart.Create(
            Ulid.NewUlid(), UniqueMatCode("SP-EXACT"), "精确消耗", "Spec", "个", 5, 2);
        part.Restock(10);
        await repo.AddAsync(part, default);

        part.Consume(10);
        await repo.UpdateAsync(part, default);

        var loaded = await repo.GetByIdAsync(part.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal(0, loaded.CurrentQuantity);
        Assert.Equal(InventoryAlertLevel.Red, loaded.GetStockLevel());
    }

    [Fact]
    public async Task Consume_Insufficient_ShouldNotChangeStock()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISparePartRepository>();

        var part = SparePart.Create(
            Ulid.NewUlid(), UniqueMatCode("SP-INSUFF"), "不足", "Spec", "个", 5, 2);
        part.Restock(3);
        await repo.AddAsync(part, default);

        var result = part.Consume(10);
        Assert.False(result);

        var loaded = await repo.GetByIdAsync(part.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal(3, loaded.CurrentQuantity);
    }

    [Fact]
    public async Task GetByEquipment_WithNullEquipmentCode_ShouldReturn()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISparePartRepository>();

        var universal = SparePart.Create(
            Ulid.NewUlid(), UniqueMatCode("SP-UNIV"), "通用备件", "Spec", "个", 5, 2);
        await repo.AddAsync(universal, default);

        // GetByEquipment 应返回通用备件（EquipmentCode=null）和专用备件
        var results = await repo.GetByEquipmentAsync("EQ-TQ-01", default);
        Assert.Contains(results, p => p.Id == universal.Id);
    }
}
