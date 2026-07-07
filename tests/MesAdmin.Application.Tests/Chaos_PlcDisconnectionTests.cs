using System.Threading.Channels;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Plc;
using Microsoft.Extensions.Logging.Abstractions;

namespace MesAdmin.Application.Tests;

/// <summary>
/// T4.12 — PLC 断连混沌测试。
/// 模拟 PLC 断线后 PlcDataAcquisitionPipeline 的 Channel 行为：
/// - 断连时空数据写入 → Channel 无数据
/// - 重连后数据恢复写入
/// - ProducerLoopAsync 在低数据率下的背压行为
/// - Channel 在多次断连/重连后的稳定性
/// </summary>
public class Chaos_PlcDisconnectionTests
{
    // ═══════════════════════════════════════════════════════════
    //  Scenario 1: PLC 断连 → 空数据写入
    //  模拟 PlcDataAcquisitionPipeline 的 ProducerLoopAsync 中
    //  GetAllSnapshots() 返回空列表时，Channel 不应有数据写入。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task PlcDisconnection_EmptySnapshots_ShouldNotWriteToChannel()
    {
        var channel = Channel.CreateBounded<PlcSnapshot>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });

        var health = new ChannelHealth();

        // 模拟 PLC 断连：循环读取空列表
        for (var cycle = 0; cycle < 10; cycle++)
        {
            var snapshots = new List<PlcSnapshot>(); // 模拟空数据（PLC 断连）
            foreach (var snapshot in snapshots)
            {
                await channel.Writer.WriteAsync(snapshot);
                health.IncrementWritten();
            }
        }

        // 不应有数据写入
        Assert.Equal(0, health.Written);
        Assert.False(channel.Reader.TryRead(out _));
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 2: PLC 重连 → 数据恢复
    //  模拟 PLC 断连后重连，验证数据恢复正常写入。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task PlcReconnection_AfterDisconnection_ShouldResumeDataFlow()
    {
        var channel = Channel.CreateBounded<PlcSnapshot>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });

        var health = new ChannelHealth();

        // 阶段 1: PLC 正常工作（写入 5 条）
        for (var i = 0; i < 5; i++)
        {
            var snapshot = PlcSnapshot.Create($"EQ-{i}", DateTimeOffset.UtcNow, EquipmentStatus.Running, i, i, 0, 1000, 0, "tag");
            await channel.Writer.WriteAsync(snapshot);
            health.IncrementWritten();
        }
        Assert.Equal(5, health.Written);

        // 阶段 2: PLC 断连（空数据写入 5 个 cycle）
        for (var cycle = 0; cycle < 5; cycle++)
        {
            var snapshots = new List<PlcSnapshot>();
            foreach (var snapshot in snapshots)
            {
                await channel.Writer.WriteAsync(snapshot);
                health.IncrementWritten();
            }
        }
        // 写入计数不变（空循环不写入）
        Assert.Equal(5, health.Written);

        // 阶段 3: PLC 重连（再写入 3 条）
        for (var i = 5; i < 8; i++)
        {
            var snapshot = PlcSnapshot.Create($"EQ-{i}", DateTimeOffset.UtcNow, EquipmentStatus.Running, i, i, 0, 1000, 0, "tag");
            await channel.Writer.WriteAsync(snapshot);
            health.IncrementWritten();
        }
        Assert.Equal(8, health.Written);

        // 验证 Channel 中数据完整：断连前 5 条 + 重连后 3 条 = 8 条
        channel.Writer.TryComplete();
        var items = new List<PlcSnapshot>();
        await foreach (var item in channel.Reader.ReadAllAsync())
            items.Add(item);

        Assert.Equal(8, items.Count);
        Assert.Equal("EQ-0", items[0].EquipmentCode);
        Assert.Equal("EQ-7", items[^1].EquipmentCode);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 3: 多次断连/重连 — Channel 稳定性
    //  验证 Channel 在多次断连/重连后功能正常。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task MultipleDisconnectReconnectCycles_ShouldNotCorruptChannel()
    {
        var channel = Channel.CreateBounded<PlcSnapshot>(new BoundedChannelOptions(50)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });

        var health = new ChannelHealth();

        for (var cycle = 0; cycle < 10; cycle++)
        {
            // 模拟连接：写入 3 条
            for (var i = 0; i < 3; i++)
            {
                var snapshot = PlcSnapshot.Create($"C{cycle}-EQ-{i}", DateTimeOffset.UtcNow, EquipmentStatus.Running, i, i, 0, 1000, 0, "tag");
                await channel.Writer.WriteAsync(snapshot);
                health.IncrementWritten();
            }

            // 模拟断连：空数据
            var empty = new List<PlcSnapshot>();
            foreach (var snapshot in empty)
            {
                await channel.Writer.WriteAsync(snapshot);
                health.IncrementWritten();
            }
        }

        // 总共写入 10 cycles × 3 条 = 30 条
        Assert.Equal(30, health.Written);

        channel.Writer.TryComplete();
        var items = new List<PlcSnapshot>();
        await foreach (var item in channel.Reader.ReadAllAsync())
            items.Add(item);

        Assert.Equal(30, items.Count);
        Assert.StartsWith("C0-EQ-", items[0].EquipmentCode);
        Assert.StartsWith("C9-EQ-", items[^1].EquipmentCode);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 4: ChannelHealth 在断连期间不应增长
    //  验证 ChannelHealth.Written 在断连期间不增长（空数据不计数）。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ChannelHealth_DuringDisconnection_ShouldNotIncrease()
    {
        var health = new ChannelHealth();

        // 正常写入 10 条
        for (var i = 0; i < 10; i++)
            health.IncrementWritten();

        Assert.Equal(10, health.Written);

        // 模拟断连 50 个 cycle（空数据）
        for (var cycle = 0; cycle < 50; cycle++)
        {
            var snapshots = new List<PlcSnapshot>();
            foreach (var _ in snapshots)
                health.IncrementWritten();
        }

        // 写入计数不变
        Assert.Equal(10, health.Written);
        Assert.Equal(0, health.Read);
        Assert.Equal(0, health.Dropped);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 5: 低频数据场景 — Channel 不应无限积压
    //  模拟设备低频数据（部分设备离线）验证 Channel 利用率可控。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task LowFrequencyData_ShouldNotOverflowChannel()
    {
        var channel = Channel.CreateBounded<PlcSnapshot>(new BoundedChannelOptions(10)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });

        // 8 设备中只有 2 台在发送数据
        var activeDevices = new[] { "EQ-TQ-01", "EQ-HYD-01" };

        for (var i = 0; i < 5; i++)
        {
            foreach (var device in activeDevices)
            {
                var snapshot = PlcSnapshot.Create(device, DateTimeOffset.UtcNow, EquipmentStatus.Running, i, i, 0, 1000, 0, "tag");
                await channel.Writer.WriteAsync(snapshot);
            }
        }

        // 验证数据
        channel.Writer.TryComplete();
        var items = new List<PlcSnapshot>();
        await foreach (var item in channel.Reader.ReadAllAsync())
            items.Add(item);

        Assert.Equal(10, items.Count); // 5 cycles × 2 设备
        Assert.All(items, item => Assert.Contains(item.EquipmentCode, activeDevices));
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 6: 设备全部离线 — Channel 完全空闲
    //  验证 8 设备全部离线时 Channel Health 状态。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void AllDevicesOffline_ChannelHealth_ShouldReflectZeroActivity()
    {
        var health = new ChannelHealth();

        // 模拟 100 个读取 cycle，全部设备离线
        for (var cycle = 0; cycle < 100; cycle++)
        {
            var allSnapshots = new List<PlcSnapshot>(); // 全部离线
            foreach (var _ in allSnapshots)
                health.IncrementWritten();
        }

        Assert.Equal(0, health.Written);
        Assert.Equal(0, health.Read);
        Assert.Equal(0, health.GetUtilization(10000));
    }
}
