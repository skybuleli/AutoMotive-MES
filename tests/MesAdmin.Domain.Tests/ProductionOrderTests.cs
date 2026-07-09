using MesAdmin.Domain.Models;

namespace MesAdmin.Domain.Tests;

public class ProductionOrderTests
{
    [Fact]
    public void Create_ShouldNormalizeAndInitializeOrder()
    {
        var createdAt = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);
        var order = ProductionOrder.Create(
            Ulid.NewUlid(),
            "wo-20260701-0001",
            "esp-9.0",
            Ulid.NewUlid(),
            " bom-a ",
            120,
            1,
            createdAt);

        Assert.Equal("wo-20260701-0001", order.OrderNumber);
        Assert.Equal("ESP-9.0", order.ProductCode);
        Assert.Equal("bom-a", order.BomVersion);
        Assert.Equal(OrderStatus.Created, order.Status);
        Assert.True(order.CanRelease);
        // Created 状态不可直接开工，必须先 Release（与 Start() 语义一致）
        Assert.False(order.CanStart);
        Assert.Equal(createdAt, order.CreatedAt);
    }

    [Fact]
    public void StateFlow_ShouldMoveThroughReleaseStartCompleteAndClose()
    {
        var order = CreateOrder();
        var completedAt = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

        order.Release();
        Assert.Equal(OrderStatus.Released, order.Status);
        Assert.True(order.CanStart);

        order.Start();
        Assert.Equal(OrderStatus.InProgress, order.Status);
        Assert.True(order.CanComplete);

        order.Complete(100, 2, completedAt);
        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(100, order.QualifiedQuantity);
        Assert.Equal(2, order.DefectiveQuantity);
        Assert.Equal(completedAt, order.CompletedAt);
        Assert.True(order.CanClose);

        order.Close();
        Assert.Equal(OrderStatus.Closed, order.Status);
    }

    [Fact]
    public void Start_ShouldRejectWhenNotReleased()
    {
        var order = CreateOrder();

        var ex = Assert.Throws<InvalidOperationException>(order.Start);

        Assert.Contains("Released", ex.Message);
    }

    [Fact]
    public void CanStart_ShouldMatchStartTransition()
    {
        var order = CreateOrder();

        // Created：不可开工（与 Start() 语义一致，Start() 会抛异常）
        Assert.False(order.CanStart);

        order.Release();
        // Released：可开工
        Assert.True(order.CanStart);

        order.Start();
        // InProgress：不可再开工
        Assert.False(order.CanStart);
    }

    [Theory]
    [InlineData(false)] // Created
    [InlineData(true)]  // Released
    public void Cancel_ShouldSucceedFromCreatedOrReleased(bool release)
    {
        var order = CreateOrder();
        if (release) order.Release();

        Assert.True(order.CanCancel);
        order.Cancel("客户撤单", new DateTimeOffset(2026, 7, 2, 9, 0, 0, TimeSpan.Zero));

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("客户撤单", order.CancelReason);
        Assert.NotNull(order.ActualEndAt);
        Assert.False(order.CanCancel);
    }

    [Fact]
    public void Cancel_ShouldRejectWhenInProgress()
    {
        var order = CreateOrder();
        order.Release();
        order.Start();

        Assert.False(order.CanCancel);
        var ex = Assert.Throws<InvalidOperationException>(
            () => order.Cancel("too late", DateTimeOffset.UtcNow));
        Assert.Contains("InProgress", ex.Message);
    }

    [Fact]
    public void Cancel_ShouldRejectEmptyReason()
    {
        var order = CreateOrder();
        Assert.Throws<ArgumentException>(() => order.Cancel("  ", DateTimeOffset.UtcNow));
    }

    private static ProductionOrder CreateOrder()
        => ProductionOrder.Create(
            Ulid.NewUlid(),
            "WO-20260701-0001",
            "ESP-9.0",
            Ulid.NewUlid(),
            "BOM-A",
            120,
            1,
            new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero));
}
