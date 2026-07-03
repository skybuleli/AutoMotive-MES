using System.Threading.Channels;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Plc;

namespace MesAdmin.Application.Tests;

/// <summary>
/// Channel 背压测试（T2.13）。
/// 验证 BoundedChannel FullMode=Wait 行为 + ChannelHealth 使用率报告。
/// 禁止 BlockingCollection（AGENTS.md 4.3）。
/// </summary>
public class ChannelBackpressureTests
{
    [Fact]
    public async Task BoundedChannel_FullModeWait_ShouldBlockUntilConsumed()
    {
        //Arrange：容量 3 的 BoundedChannel，FullMode=Wait
        var channel = Channel.CreateBounded<PlcSnapshot>(new BoundedChannelOptions(3)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
        });

        //Act：写满 3 条（不阻塞）
        for (var i = 0; i < 3; i++)
        {
            var snapshot = PlcSnapshot.Create($"EQ-{i}", DateTimeOffset.UtcNow, EquipmentStatus.Running, i, i, 0, 1000, 0, "tag");
            await channel.Writer.WriteAsync(snapshot);
        }

        // 第 4 条写入应阻塞（容量已满），用 Task.Run + 超时验证
        var writeTask = Task.Run(async () =>
        {
            var snapshot = PlcSnapshot.Create("EQ-3", DateTimeOffset.UtcNow, EquipmentStatus.Running, 3, 3, 0, 1000, 0, "tag");
            await channel.Writer.WriteAsync(snapshot);
        });

        //Assert：100ms 内第 4 条写入未完成（被阻塞）
        await Task.Delay(100);
        Assert.False(writeTask.IsCompleted);

        // 消费 1 条后，阻塞的写入应完成
        var consumed = await channel.Reader.ReadAsync();
        Assert.Equal("EQ-0", consumed.EquipmentCode);

        await writeTask; // 现在应完成
        Assert.True(writeTask.IsCompleted);

        channel.Writer.TryComplete();
    }

    [Fact]
    public void ChannelHealth_ShouldTrackWrittenAndRead()
    {
        var health = new ChannelHealth();

        for (var i = 0; i < 100; i++)
            health.IncrementWritten();

        for (var i = 0; i < 30; i++)
            health.IncrementRead();

        Assert.Equal(100, health.Written);
        Assert.Equal(30, health.Read);
    }

    [Fact]
    public void ChannelHealth_GetUtilization_ShouldReportPendingRatio()
    {
        var health = new ChannelHealth();
        var capacity = 10000;

        // 写入 5000，读取 1000 → 待处理 4000 → 使用率 40%
        for (var i = 0; i < 5000; i++)
            health.IncrementWritten();
        for (var i = 0; i < 1000; i++)
            health.IncrementRead();

        var utilization = health.GetUtilization(capacity);

        Assert.Equal(0.4, utilization, precision: 2);
    }

    [Fact]
    public void ChannelHealth_GetUtilization_ShouldClampAt100Percent()
    {
        var health = new ChannelHealth();
        var capacity = 100;

        // 写入 200（超过容量）→ 使用率应钳制到 100%
        for (var i = 0; i < 200; i++)
            health.IncrementWritten();

        Assert.Equal(1.0, health.GetUtilization(capacity));
    }

    [Fact]
    public async Task Channel_ReadAllAsync_ShouldStreamAllItems()
    {
        var channel = Channel.CreateBounded<PlcSnapshot>(10);

        // 写入 5 条
        for (var i = 0; i < 5; i++)
        {
            await channel.Writer.WriteAsync(
                PlcSnapshot.Create($"EQ-{i}", DateTimeOffset.UtcNow, EquipmentStatus.Running, i, i, 0, 1000, 0, "tag"));
        }
        channel.Writer.TryComplete();

        // ReadAllAsync 应读出全部 5 条
        var items = new List<PlcSnapshot>();
        await foreach (var item in channel.Reader.ReadAllAsync())
        {
            items.Add(item);
        }

        Assert.Equal(5, items.Count);
        Assert.Equal("EQ-0", items[0].EquipmentCode);
        Assert.Equal("EQ-4", items[4].EquipmentCode);
    }
}
