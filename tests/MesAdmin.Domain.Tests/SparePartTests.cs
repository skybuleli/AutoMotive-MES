using MesAdmin.Domain.Models;

namespace MesAdmin.Domain.Tests;

/// <summary>
/// 备件主数据测试（T2.18）。
/// 覆盖 SparePart 创建、库存操作（盘点/补货/消耗）、库存水平判定、采购建议。
/// </summary>
public class SparePartTests
{
    // ═══════════════════════════════════════════════════════════
    //  Create
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Create_ShouldInitializeCorrectly()
    {
        var part = SparePart.Create(
            Ulid.NewUlid(),
            "SP-TQSENSOR-01",
            "扭矩传感器",
            "0-100Nm / 精度 0.5%",
            "个",
            5,   // safetyStock
            2);  // minimumStock

        Assert.Equal("SP-TQSENSOR-01", part.MaterialCode);
        Assert.Equal("扭矩传感器", part.MaterialName);
        Assert.Equal("0-100Nm / 精度 0.5%", part.Specification);
        Assert.Equal("个", part.Unit);
        Assert.Equal(0, part.CurrentQuantity);
        Assert.Equal(5, part.SafetyStock);
        Assert.Equal(2, part.MinimumStock);
        Assert.Null(part.EquipmentCode);
        Assert.Null(part.Remarks);
    }

    [Fact]
    public void Create_ShouldSetEquipmentCodeAndRemarks_WhenProvided()
    {
        var part = SparePart.Create(
            Ulid.NewUlid(),
            "SP-O-RING-01",
            "O 型密封圈",
            "12×2mm",
            "个",
            50, 10,
            "EQ-HYD-01",
            "液压测试台专用密封圈");

        Assert.Equal("EQ-HYD-01", part.EquipmentCode);
        Assert.Equal("液压测试台专用密封圈", part.Remarks);
    }

    [Fact]
    public void Create_ShouldThrowWhenMaterialCodeEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            SparePart.Create(Ulid.NewUlid(), "", "Name", "Spec", "个", 5, 2));
    }

    [Fact]
    public void Create_ShouldThrowWhenMaterialNameEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            SparePart.Create(Ulid.NewUlid(), "SP-01", "", "Spec", "个", 5, 2));
    }

    [Fact]
    public void Create_ShouldThrowWhenUnitEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            SparePart.Create(Ulid.NewUlid(), "SP-01", "Name", "Spec", "", 5, 2));
    }

    [Fact]
    public void Create_ShouldTrimValues()
    {
        var part = SparePart.Create(
            Ulid.NewUlid(),
            " SP-01 ",
            " 扭矩传感器 ",
            " 规格 ",
            " 个 ",
            5, 2,
            " EQ-HYD-01 ",
            " 备注 ");

        Assert.Equal("SP-01", part.MaterialCode);
        Assert.Equal("扭矩传感器", part.MaterialName);
        Assert.Equal("规格", part.Specification);
        Assert.Equal("个", part.Unit);
        Assert.Equal("EQ-HYD-01", part.EquipmentCode);
        Assert.Equal("备注", part.Remarks);
    }

    // ═══════════════════════════════════════════════════════════
    //  UpdateStock — 库存盘点更新
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void UpdateStock_ShouldSetExactQuantity()
    {
        var part = CreateDefaultPart();
        part.UpdateStock(42);

        Assert.Equal(42, part.CurrentQuantity);
    }

    [Fact]
    public void UpdateStock_ShouldNotGoBelowZero()
    {
        var part = CreateDefaultPart();
        part.UpdateStock(10);
        part.UpdateStock(-5);

        Assert.Equal(0, part.CurrentQuantity);
    }

    [Fact]
    public void UpdateStock_ShouldUpdateTimestamp()
    {
        var before = DateTimeOffset.UtcNow;
        var part = CreateDefaultPart();
        part.UpdateStock(100);
        var after = DateTimeOffset.UtcNow;

        Assert.True(part.UpdatedAt >= before && part.UpdatedAt <= after);
    }

    // ═══════════════════════════════════════════════════════════
    //  Restock — 补货入库
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Restock_ShouldIncreaseQuantity()
    {
        var part = CreateDefaultPart();
        part.Restock(50);

        Assert.Equal(50, part.CurrentQuantity);
    }

    [Fact]
    public void Restock_ShouldAccumulateWithExisting()
    {
        var part = CreateDefaultPart();
        part.Restock(30);
        part.Restock(20);

        Assert.Equal(50, part.CurrentQuantity);
    }

    [Fact]
    public void Restock_ZeroOrNegative_ShouldNotChange()
    {
        var part = CreateDefaultPart();
        part.Restock(0);
        Assert.Equal(0, part.CurrentQuantity);

        part.Restock(-10);
        Assert.Equal(0, part.CurrentQuantity);
    }

    // ═══════════════════════════════════════════════════════════
    //  Consume — 消耗扣减
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Consume_ShouldDecreaseQuantity()
    {
        var part = CreateDefaultPart();
        part.Restock(100);

        var result = part.Consume(30);

        Assert.True(result);
        Assert.Equal(70, part.CurrentQuantity);
    }

    [Fact]
    public void Consume_ShouldReturnFalse_WhenInsufficientStock()
    {
        var part = CreateDefaultPart();
        part.Restock(5);

        var result = part.Consume(10);

        Assert.False(result);
        Assert.Equal(5, part.CurrentQuantity);
    }

    [Fact]
    public void Consume_ShouldReturnFalse_WhenQuantityZeroOrNegative()
    {
        var part = CreateDefaultPart();
        part.Restock(10);

        Assert.False(part.Consume(0));
        Assert.False(part.Consume(-5));
        Assert.Equal(10, part.CurrentQuantity);
    }

    [Fact]
    public void Consume_ShouldSucceed_WhenExactlyEnough()
    {
        var part = CreateDefaultPart();
        part.Restock(10);

        var result = part.Consume(10);

        Assert.True(result);
        Assert.Equal(0, part.CurrentQuantity);
    }

    // ═══════════════════════════════════════════════════════════
    //  GetStockLevel — 库存水平判定
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void GetStockLevel_ShouldReturnRed_WhenBelowMinimum()
    {
        var part = CreateDefaultPart(); // safety=5, minimum=2
        part.Restock(1);

        Assert.Equal(InventoryAlertLevel.Red, part.GetStockLevel());
    }

    [Fact]
    public void GetStockLevel_ShouldReturnYellow_WhenEqualToMinimum()
    {
        var part = CreateDefaultPart(); // safety=5, minimum=2
        part.Restock(2);

        // Red only when strictly below minimum (< 2)
        Assert.Equal(InventoryAlertLevel.Yellow, part.GetStockLevel());
    }

    [Fact]
    public void GetStockLevel_ShouldReturnYellow_WhenBelowSafety()
    {
        var part = CreateDefaultPart(); // safety=5, minimum=2
        part.Restock(3);

        Assert.Equal(InventoryAlertLevel.Yellow, part.GetStockLevel());
    }

    [Fact]
    public void GetStockLevel_ShouldReturnNormal_WhenAboveSafety()
    {
        var part = CreateDefaultPart(); // safety=5, minimum=2
        part.Restock(10);

        Assert.Equal(InventoryAlertLevel.Normal, part.GetStockLevel());
    }

    [Fact]
    public void GetStockLevel_ShouldReturnNormal_WhenEqualToSafety()
    {
        var part = CreateDefaultPart(); // safety=5, minimum=2
        part.Restock(5);

        Assert.Equal(InventoryAlertLevel.Normal, part.GetStockLevel());
    }

    // ═══════════════════════════════════════════════════════════
    //  NeedsPurchaseRequest — 采购申请触发判定
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void NeedsPurchaseRequest_ShouldBeTrue_WhenBelowMinimum()
    {
        var part = CreateDefaultPart(); // safety=5, minimum=2
        part.Restock(1);

        Assert.True(part.NeedsPurchaseRequest);
    }

    [Fact]
    public void NeedsPurchaseRequest_ShouldBeFalse_WhenAboveMinimum()
    {
        var part = CreateDefaultPart(); // safety=5, minimum=2
        part.Restock(3);

        Assert.False(part.NeedsPurchaseRequest);
    }

    [Fact]
    public void NeedsPurchaseRequest_ShouldBeTrue_WhenStockZero()
    {
        var part = CreateDefaultPart(); // safety=5, minimum=2
        // CurrentQuantity = 0, 0 < 2 → true
        Assert.True(part.NeedsPurchaseRequest);
    }

    // ═══════════════════════════════════════════════════════════
    //  SuggestedPurchaseQuantity
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void SuggestedPurchaseQuantity_ShouldCalculateCorrectly()
    {
        var part = CreateDefaultPart(); // safety=5, minimum=2
        part.Restock(1);

        // safety*2 - current = 5*2 - 1 = 9
        Assert.Equal(9, part.SuggestedPurchaseQuantity);
    }

    [Fact]
    public void SuggestedPurchaseQuantity_ShouldBeAtLeastOne()
    {
        var part = CreateDefaultPart(); // safety=5, minimum=2
        part.Restock(10);

        // safety*2 - current = 10 - 10 = 0 → Math.Max(1, 0) = 1
        Assert.Equal(1, part.SuggestedPurchaseQuantity);
    }

    [Fact]
    public void SuggestedPurchaseQuantity_WhenSafetyStockIsZero()
    {
        var part = SparePart.Create(
            Ulid.NewUlid(), "SP-01", "Name", "Spec", "个", 0, 0);
        // safety*2 - current = 0 - 0 = 0 → Math.Max(1, 0) = 1
        Assert.Equal(1, part.SuggestedPurchaseQuantity);
    }

    // ═══════════════════════════════════════════════════════════
    //  完整操作场景
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void FullLifecycle_Restock_Consume_UpdateStock_ShouldWork()
    {
        var part = CreateDefaultPart();
        Assert.Equal(InventoryAlertLevel.Red, part.GetStockLevel());
        Assert.True(part.NeedsPurchaseRequest);

        // 补货
        part.Restock(20);
        Assert.Equal(InventoryAlertLevel.Normal, part.GetStockLevel());
        Assert.False(part.NeedsPurchaseRequest);
        Assert.Equal(20, part.CurrentQuantity);

        // 消耗
        Assert.True(part.Consume(8));
        Assert.Equal(12, part.CurrentQuantity);
        Assert.Equal(InventoryAlertLevel.Normal, part.GetStockLevel());

        // 继续消耗到黄色区域
        Assert.True(part.Consume(8));
        Assert.Equal(4, part.CurrentQuantity);
        Assert.Equal(InventoryAlertLevel.Yellow, part.GetStockLevel());

        // 消耗到最低阈值（等于 minimum=2 → Yellow，NeedsPurchaseRequest 使用 strict < ）
        Assert.True(part.Consume(2));
        Assert.Equal(2, part.CurrentQuantity);
        Assert.Equal(InventoryAlertLevel.Yellow, part.GetStockLevel());
        Assert.False(part.NeedsPurchaseRequest);

        // 继续消耗到低于最低阈值 → Red + 触发采购
        Assert.True(part.Consume(1));
        Assert.Equal(1, part.CurrentQuantity);
        Assert.Equal(InventoryAlertLevel.Red, part.GetStockLevel());
        Assert.True(part.NeedsPurchaseRequest);

        // 盘点更新
        part.UpdateStock(15);
        Assert.Equal(15, part.CurrentQuantity);
        Assert.Equal(InventoryAlertLevel.Normal, part.GetStockLevel());
    }

    // ═══════════════════════════════════════════════════════════
    //  测试辅助方法
    // ═══════════════════════════════════════════════════════════

    private static SparePart CreateDefaultPart()
        => SparePart.Create(
            Ulid.NewUlid(),
            "SP-TQSENSOR-01",
            "扭矩传感器",
            "0-100Nm / 精度 0.5%",
            "个",
            5,   // safetyStock
            2);  // minimumStock
}

/// <summary>
/// 备件使用记录测试（T2.18）。
/// </summary>
public class SparePartUsageTests
{
    [Fact]
    public void Create_ShouldInitializeCorrectly()
    {
        var partId = Ulid.NewUlid();
        var orderId = Ulid.NewUlid();

        var usage = SparePartUsage.Create(
            Ulid.NewUlid(), partId, orderId, 3, 150.0, "更换密封圈");

        Assert.Equal(partId, usage.SparePartId);
        Assert.Equal(orderId, usage.MaintenanceWorkOrderId);
        Assert.Equal(3, usage.Quantity);
        Assert.Equal(150.0, usage.UnitPrice);
        Assert.Equal("更换密封圈", usage.Remarks);
    }

    [Fact]
    public void Create_ShouldAllowNullUnitPriceAndRemarks()
    {
        var usage = SparePartUsage.Create(
            Ulid.NewUlid(), Ulid.NewUlid(), Ulid.NewUlid(), 1);

        Assert.Null(usage.UnitPrice);
        Assert.Null(usage.Remarks);
    }

    [Fact]
    public void Create_ShouldTrimRemarks()
    {
        var usage = SparePartUsage.Create(
            Ulid.NewUlid(), Ulid.NewUlid(), Ulid.NewUlid(), 1, remarks: " 备注 ");

        Assert.Equal("备注", usage.Remarks);
    }

    [Fact]
    public void Create_ShouldThrowWhenQuantityZeroOrNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SparePartUsage.Create(Ulid.NewUlid(), Ulid.NewUlid(), Ulid.NewUlid(), 0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SparePartUsage.Create(Ulid.NewUlid(), Ulid.NewUlid(), Ulid.NewUlid(), -1));
    }
}

/// <summary>
/// 采购申请状态机测试（T2.18）。
/// 覆盖 Pending → Approved / Cancelled 状态转移 + 前置条件校验。
/// </summary>
public class PurchaseRequestTests
{
    private static readonly Ulid TestPartId = Ulid.NewUlid();

    // ═══════════════════════════════════════════════════════════
    //  Create
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Create_ShouldInitializeWithPendingStatus()
    {
        var pr = CreateDefaultPR();

        Assert.Equal("Pending", pr.Status);
        Assert.StartsWith("PR-", pr.RequestNumber);
        Assert.Equal(TestPartId, pr.SparePartId);
        Assert.Equal(10, pr.Quantity);
        Assert.Equal("库存不足，申请补货", pr.Reason);
        Assert.Equal("OP-001", pr.RequestedBy);
        Assert.Null(pr.ApprovedBy);
        Assert.Null(pr.ApprovedAt);
    }

    [Fact]
    public void Create_ShouldThrowWhenReasonEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            PurchaseRequest.Create(Ulid.NewUlid(), TestPartId, 5, "", "OP-001"));
    }

    [Fact]
    public void Create_ShouldThrowWhenRequestedByEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            PurchaseRequest.Create(Ulid.NewUlid(), TestPartId, 5, "缺货", ""));
    }

    [Fact]
    public void Create_ShouldThrowWhenQuantityZeroOrNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PurchaseRequest.Create(Ulid.NewUlid(), TestPartId, 0, "缺货", "OP-001"));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PurchaseRequest.Create(Ulid.NewUlid(), TestPartId, -5, "缺货", "OP-001"));
    }

    [Fact]
    public void Create_ShouldTrimStrings()
    {
        var pr = PurchaseRequest.Create(
            Ulid.NewUlid(), TestPartId, 5, " 缺货 ", " OP-001 ");

        Assert.Equal("缺货", pr.Reason);
        Assert.Equal("OP-001", pr.RequestedBy);
    }

    // ═══════════════════════════════════════════════════════════
    //  Approve — 审批
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Approve_ShouldTransitionToApproved()
    {
        var pr = CreateDefaultPR();
        var before = DateTimeOffset.UtcNow;

        var result = pr.Approve("MGR-001");

        var after = DateTimeOffset.UtcNow;
        Assert.True(result);
        Assert.Equal("Approved", pr.Status);
        Assert.Equal("MGR-001", pr.ApprovedBy);
        Assert.NotNull(pr.ApprovedAt);
        Assert.True(pr.ApprovedAt >= before && pr.ApprovedAt <= after);
    }

    [Fact]
    public void Approve_ShouldReject_WhenAlreadyApproved()
    {
        var pr = CreateDefaultPR();
        pr.Approve("MGR-001");

        var result = pr.Approve("MGR-002");

        Assert.False(result);
        Assert.Equal("Approved", pr.Status);
        Assert.Equal("MGR-001", pr.ApprovedBy);
    }

    [Fact]
    public void Approve_ShouldReject_WhenCancelled()
    {
        var pr = CreateDefaultPR();
        pr.Cancel("不需要");

        var result = pr.Approve("MGR-001");

        Assert.False(result);
        Assert.Equal("Cancelled", pr.Status);
    }

    [Fact]
    public void Approve_ShouldThrowWhenApprovedByEmpty()
    {
        var pr = CreateDefaultPR();
        Assert.Throws<ArgumentException>(() => pr.Approve(""));
    }

    // ═══════════════════════════════════════════════════════════
    //  Cancel — 取消
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Cancel_ShouldTransitionToCancelled()
    {
        var pr = CreateDefaultPR();
        var result = pr.Cancel("计划变更，无需采购");

        Assert.True(result);
        Assert.Equal("Cancelled", pr.Status);
        Assert.Equal("计划变更，无需采购", pr.Reason);
    }

    [Fact]
    public void Cancel_ShouldWorkFromApproved()
    {
        var pr = CreateDefaultPR();
        pr.Approve("MGR-001");

        var result = pr.Cancel("供应商已停产");

        Assert.True(result);
        Assert.Equal("Cancelled", pr.Status);
    }

    [Fact]
    public void Cancel_ShouldReject_WhenAlreadyCancelled()
    {
        var pr = CreateDefaultPR();
        pr.Cancel("不需要");

        var result = pr.Cancel("再次取消");

        Assert.False(result);
        Assert.Equal("Cancelled", pr.Status);
    }

    // ═══════════════════════════════════════════════════════════
    //  完整生命周期
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void FullLifecycle_Create_Approve_ShouldSucceed()
    {
        var pr = CreateDefaultPR();

        Assert.Equal("Pending", pr.Status);
        Assert.True(pr.Approve("MGR-001"));
        Assert.Equal("Approved", pr.Status);
        Assert.Equal("MGR-001", pr.ApprovedBy);
    }

    [Fact]
    public void FullLifecycle_Create_Cancel_ShouldSucceed()
    {
        var pr = CreateDefaultPR();

        Assert.Equal("Pending", pr.Status);
        Assert.True(pr.Cancel("取消"));
        Assert.Equal("Cancelled", pr.Status);
    }

    [Fact]
    public void FullLifecycle_Create_Approve_Cancel_ShouldSucceed()
    {
        var pr = CreateDefaultPR();

        Assert.True(pr.Approve("MGR-001"));
        Assert.Equal("Approved", pr.Status);

        Assert.True(pr.Cancel("审批后取消"));
        Assert.Equal("Cancelled", pr.Status);
    }

    // ═══════════════════════════════════════════════════════════
    //  测试辅助方法
    // ═══════════════════════════════════════════════════════════

    private static PurchaseRequest CreateDefaultPR()
        => PurchaseRequest.Create(
            Ulid.NewUlid(),
            TestPartId,
            10,
            "库存不足，申请补货",
            "OP-001");
}
