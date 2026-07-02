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
        Assert.True(order.CanStart);
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
