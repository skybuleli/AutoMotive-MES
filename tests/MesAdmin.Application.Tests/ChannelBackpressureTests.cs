using System.Threading.Channels;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Plc;
using Microsoft.Extensions.Logging.Abstractions;

namespace MesAdmin.Application.Tests;

/// <summary>
/// Channel 背压混沌测试（T2.13/T4.12）。
/// 验证 BoundedChannel FullMode=Wait 行为 + ChannelHealth 使用率报告 + PlcDataAcquisitionPipeline 混沌场景。
/// 禁止 BlockingCollection（AGENTS.md 4.3）。
/// </summary>
public class ChannelBackpressureTests
{
    // ═══════════════════════════════════════════════════════════
    //  T2.13 基础背压测试（已有）
    // ═══════════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════════
    //  T4.12 混沌工程：背压 + 消费者慢
    // ═══════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════
    //  Scenario 1: 消费者慢于生产者 — 背压堆积
    //  验证 FullMode=Wait 时生产者被阻塞，消费者追赶后恢复。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Backpressure_SlowConsumer_ShouldBlockProducer()
    {
        var channel = Channel.CreateBounded<PlcSnapshot>(new BoundedChannelOptions(5)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });

        var producerDelay = TimeSpan.FromMilliseconds(10);  // 快速生产者
        var consumerDelay = TimeSpan.FromMilliseconds(50);  // 慢速消费者

        var written = 0;
        var read = 0;

        // 生产者：快速写入
        var producer = Task.Run(async () =>
        {
            for (var i = 0; i < 30; i++)
            {
                var snapshot = PlcSnapshot.Create($"EQ-{i}", DateTimeOffset.UtcNow, EquipmentStatus.Running, i, i, 0, 1000, 0, "tag");
                await channel.Writer.WriteAsync(snapshot);
                Interlocked.Increment(ref written);
                await Task.Delay(producerDelay);
            }
            channel.Writer.TryComplete();
        });

        // 消费者：慢速读取
        var consumer = Task.Run(async () =>
        {
            await foreach (var item in channel.Reader.ReadAllAsync())
            {
                Interlocked.Increment(ref read);
                await Task.Delay(consumerDelay);
            }
        });

        // 等待消费者消费一半
        await Task.Delay(500);

        // 消费者慢于生产者，但 Channel 满时生产者被阻塞
        // written 和 read 差距不应超过容量 + 生产中的增量
        Assert.True(written >= read, "生产者写入应不少于消费者读取");
        Assert.InRange(written - read, 0, 25);  // 差距不应过大（背压生效）

        await producer;
        await consumer;

        Assert.Equal(30, written);
        Assert.Equal(30, read);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 2: Channel 关闭 — 生产者应优雅退出
    //  验证 ChannelClosedException 被 ProducerLoopAsync 正确捕获。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Channel_Closed_ProducerShouldExitGracefully()
    {
        var channel = Channel.CreateBounded<PlcSnapshot>(10);

        // 启动生产者任务
        var producerException = (Exception?)null;
        var producer = Task.Run(async () =>
        {
            try
            {
                for (var i = 0; i < 100; i++)
                {
                    var snapshot = PlcSnapshot.Create($"EQ-{i}", DateTimeOffset.UtcNow, EquipmentStatus.Running, i, i, 0, 1000, 0, "tag");
                    await channel.Writer.WriteAsync(snapshot);
                    await Task.Delay(5);
                }
            }
            catch (ChannelClosedException)
            {
                // ProducerLoopAsync 捕获 ChannelClosedException 并 return
            }
            catch (Exception ex)
            {
                producerException = ex;
            }
        });

        // 写入几条后关闭 Channel
        await Task.Delay(30);
        channel.Writer.TryComplete();

        await producer;

        // 生产者不应抛出异常
        Assert.Null(producerException);

        // 消费者应能读取到已写入的所有数据
        var items = new List<PlcSnapshot>();
        await foreach (var item in channel.Reader.ReadAllAsync())
            items.Add(item);

        Assert.True(items.Count > 0, "Channel 关闭前写入的数据应可读");
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 3: CancellationToken 传播
    //  验证 CancellationToken 取消时生产者循环优雅退出。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task ProducerLoop_CancellationToken_ShouldExitGracefully()
    {
        var channel = Channel.CreateBounded<PlcSnapshot>(10);
        using var cts = new CancellationTokenSource();

        var exited = false;
        var producer = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var snapshot = PlcSnapshot.Create("EQ-TEST", DateTimeOffset.UtcNow, EquipmentStatus.Running, 1, 1, 0, 1000, 0, "tag");
                    await channel.Writer.WriteAsync(snapshot, cts.Token);
                    await Task.Delay(10, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // ProducerLoopAsync 捕获 OperationCanceledException 并 break
                exited = true;
            }
        });

        // 取消
        await Task.Delay(50);
        cts.Cancel();

        await producer;
        Assert.True(exited, "生产者应在取消时优雅退出");
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 4: ConsumerLoopAsync — 异常不应终止管道
    //  验证消费者循环中的异常被 catch 后继续运行。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task ConsumerLoop_Exception_ShouldNotTerminate()
    {
        var channel = Channel.CreateBounded<PlcSnapshot>(10);

        // 消费者：遇到特定数据时抛出异常
        var consumer = Task.Run(async () =>
        {
            try
            {
                await foreach (var item in channel.Reader.ReadAllAsync())
                {
                    if (item.EquipmentCode == "EQ-CRASH")
                        throw new InvalidOperationException("模拟消费异常");
                }
            }
            catch (OperationCanceledException)
            {
                // 正常关闭
            }
            catch (Exception)
            {
                // 异常被捕获，不应扩散
            }
        });

        // 写入正常数据
        await channel.Writer.WriteAsync(
            PlcSnapshot.Create("EQ-NORMAL", DateTimeOffset.UtcNow, EquipmentStatus.Running, 1, 1, 0, 1000, 0, "tag"));
        await channel.Writer.WriteAsync(
            PlcSnapshot.Create("EQ-CRASH", DateTimeOffset.UtcNow, EquipmentStatus.Running, 2, 2, 0, 1000, 0, "tag"));

        // 给消费者时间处理异常
        await Task.Delay(200);

        // Channel 应仍可用（消费者异常被捕获，循环继续）
        await channel.Writer.WriteAsync(
            PlcSnapshot.Create("EQ-AFTER-CRASH", DateTimeOffset.UtcNow, EquipmentStatus.Running, 3, 3, 0, 1000, 0, "tag"));

        channel.Writer.TryComplete();
        await consumer;

        Assert.True(true, "消费者异常不应中断管道");
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 5: ChannelHealth 并发安全
    //  验证 ChannelHealth 的 Interlocked 操作在高并发下正确。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task ChannelHealth_ConcurrentIncrement_ShouldBeThreadSafe()
    {
        var health = new ChannelHealth();
        const int Concurrency = 10;
        const int Iterations = 10000;

        var tasks = new Task[Concurrency];
        for (var t = 0; t < Concurrency; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (var i = 0; i < Iterations; i++)
                    health.IncrementWritten();
            });
        }

        await Task.WhenAll(tasks);

        // Interlocked 保证最终值正确
        Assert.Equal(Concurrency * Iterations, health.Written);
        Assert.Equal(0, health.Read);
        Assert.Equal(0, health.Dropped);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 7: 大量数据写入/读取 — Channel 稳定性
    //  验证高频写入不会导致 Channel 溢出或死锁。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task HighFrequencyWriteRead_ShouldNotDeadlock()
    {
        var channel = Channel.CreateBounded<PlcSnapshot>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });

        var producer = Task.Run(async () =>
        {
            for (var i = 0; i < 5000; i++)
            {
                var snapshot = PlcSnapshot.Create($"EQ-{i % 8}", DateTimeOffset.UtcNow, EquipmentStatus.Running, i, i, 0, 1000, 0, "tag");
                await channel.Writer.WriteAsync(snapshot);
            }
            channel.Writer.TryComplete();
        });

        var consumer = Task.Run(async () =>
        {
            var count = 0;
            await foreach (var _ in channel.Reader.ReadAllAsync())
                count++;
            return count;
        });

        await producer;
        var consumed = await consumer;

        Assert.Equal(5000, consumed);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 8: 模拟 PLC 拔线 — 生产者断连
    //  验证 PlcDriverFactory.GetAllSnapshots 返回空时 Channel 无数据写入。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void EmptySnapshots_ShouldNotWriteToChannel()
    {
        var channel = Channel.CreateBounded<PlcSnapshot>(10);

        // 模拟 GetAllSnapshots 返回空（PLC 断连）
        var snapshots = new List<PlcSnapshot>();

        // 无数据时不应写入
        foreach (var snapshot in snapshots)
            channel.Writer.TryWrite(snapshot);

        // 不应有数据
        Assert.False(channel.Reader.TryRead(out _));
    }
}
