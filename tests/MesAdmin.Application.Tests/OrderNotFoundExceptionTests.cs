using MesAdmin.Application.Common;

namespace MesAdmin.Application.Tests;

/// <summary>
/// OrderNotFoundException 单元测试。
/// 验证异常消息包含订单 ID、OrderId 属性正确存储、继承自 Exception。
/// </summary>
public class OrderNotFoundExceptionTests
{
    [Fact]
    public void Constructor_ShouldStoreOrderId()
    {
        var orderId = Ulid.NewUlid();

        var ex = new OrderNotFoundException(orderId);

        Assert.Equal(orderId, ex.OrderId);
    }

    [Fact]
    public void Constructor_ShouldIncludeOrderIdInMessage()
    {
        var orderId = Ulid.NewUlid();

        var ex = new OrderNotFoundException(orderId);

        Assert.Contains(orderId.ToString(), ex.Message);
    }

    [Fact]
    public void Constructor_ShouldSetMessageInChinese()
    {
        var orderId = Ulid.NewUlid();

        var ex = new OrderNotFoundException(orderId);

        Assert.StartsWith("工单", ex.Message);
        Assert.EndsWith("不存在", ex.Message);
    }

    [Fact]
    public void ShouldBeSubclassOfException()
    {
        var orderId = Ulid.NewUlid();

        var ex = new OrderNotFoundException(orderId);

        Assert.IsAssignableFrom<Exception>(ex);
    }

    [Fact]
    public void ShouldBeSealed()
    {
        Assert.True(typeof(OrderNotFoundException).IsSealed);
    }

    [Fact]
    public void DifferentOrders_ShouldHaveDifferentMessages()
    {
        var orderA = Ulid.NewUlid();
        var orderB = Ulid.NewUlid();

        var exA = new OrderNotFoundException(orderA);
        var exB = new OrderNotFoundException(orderB);

        Assert.NotEqual(exA.Message, exB.Message);
        Assert.Equal(orderA, exA.OrderId);
        Assert.Equal(orderB, exB.OrderId);
    }
}
