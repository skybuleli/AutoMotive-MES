using System.Threading.Channels;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using R3;
using ZLogger;

namespace MesAdmin.Infrastructure.Plc;

/// <summary>
/// PLC 数据采集管道（T2.13）。
/// BoundedChannel&lt;PlcSnapshot&gt; 容量 10000 + FullMode=Wait 背压（禁止 BlockingCollection，AGENTS.md 4.3）。
/// 8 设备 100Hz 读取循环 → Channel → R3 Subject 推送。
/// ReadAllAsync 喂 R3 管道，供 OeeReactivePipeline 订阅。
/// </summary>
public sealed class PlcDataAcquisitionPipeline : IHostedService, IAsyncDisposable
{
    private readonly OpcUaPlcClient _plcClient;
    private readonly ILogger<PlcDataAcquisitionPipeline> _logger;
    private readonly IReadOnlyList<Equipment> _equipment;
    private readonly int _channelCapacity;
    private readonly TimeSpan _readInterval; // 100Hz = 10ms

    private Channel<PlcSnapshot> _channel = null!;
    private readonly Subject<PlcSnapshot> _plcStream = new();
    private CancellationTokenSource? _cts;
    private Task? _producerTask;
    private Task? _consumerTask;

    /// <summary>PLC 数据流（R3 Observable），供 OeeReactivePipeline 订阅</summary>
    public Observable<PlcSnapshot> PlcStream => _plcStream;

    /// <summary>Channel 健康度（供 DashboardHub 10s 推送）</summary>
    public ChannelHealth Health { get; } = new();

    public PlcDataAcquisitionPipeline(
        OpcUaPlcClient plcClient,
        ILogger<PlcDataAcquisitionPipeline> logger,
        int channelCapacity = 10000,
        int readIntervalMs = 10)
    {
        _plcClient = plcClient;
        _logger = logger;
        _equipment = Equipment.DefaultEquipment;
        _channelCapacity = channelCapacity;
        _readInterval = TimeSpan.FromMilliseconds(readIntervalMs);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _channel = Channel.CreateBounded<PlcSnapshot>(new BoundedChannelOptions(_channelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait, // 背压：满了等待消费，禁止丢弃
            SingleReader = false,
            SingleWriter = false,
        });

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // 启动 PLC 客户端（开始帧生成 + PipeReader 读取）
        _ = _plcClient.StartAsync(_cts.Token);

        // 生产者：100Hz 轮询 8 设备最新快照写入 Channel
        _producerTask = Task.Run(() => ProducerLoopAsync(_cts.Token), _cts.Token);

        // 消费者：ReadAllAsync 喂 R3 Subject
        _consumerTask = Task.Run(() => ConsumerLoopAsync(_cts.Token), _cts.Token);

        _logger.ZLogInformation($"PLC 数据采集管道启动：{_equipment.Count} 设备 × 100Hz，Channel 容量 {_channelCapacity}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 生产者循环：100Hz（Task.Delay(10)）轮询 8 设备最新快照。
    /// 从 OpcUaPlcClient 缓存读取最新帧，写入 BoundedChannel。
    /// </summary>
    private async Task ProducerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var snapshots = _plcClient.GetAllSnapshots();
            foreach (var snapshot in snapshots)
            {
                try
                {
                    await _channel.Writer.WriteAsync(snapshot, ct);
                    Health.IncrementWritten();
                }
                catch (ChannelClosedException) { return; }
            }

            try { await Task.Delay(_readInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// 消费者循环：ReadAllAsync 从 Channel 读取，推送到 R3 Subject。
    /// </summary>
    private async Task ConsumerLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var snapshot in _channel.Reader.ReadAllAsync(ct))
            {
                _plcStream.OnNext(snapshot);
                Health.IncrementRead();
            }
        }
        catch (OperationCanceledException) { /* 正常关闭 */ }
        catch (Exception ex)
        {
            _logger.ZLogError($"PLC Channel 消费循环异常：{ex.Message}");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        _channel.Writer.TryComplete();

        if (_producerTask is not null)
        {
            try { await _producerTask; } catch { }
        }
        if (_consumerTask is not null)
        {
            try { await _consumerTask; } catch { }
        }

        _plcStream.OnCompleted();
        _logger.ZLogInformation($"PLC 数据采集管道已停止");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _plcStream.Dispose();
        _cts?.Dispose();
    }
}

/// <summary>
/// Channel 健康度（供 DashboardHub 10s 推送）。
/// 记录写入/读取计数、当前队列深度、使用率。
/// </summary>
public sealed class ChannelHealth
{
    private long _written;
    private long _read;
    private long _dropped;

    public long Written => Interlocked.Read(ref _written);
    public long Read => Interlocked.Read(ref _read);
    public long Dropped => Interlocked.Read(ref _dropped);

    public void IncrementWritten() => Interlocked.Increment(ref _written);
    public void IncrementRead() => Interlocked.Increment(ref _read);
    public void IncrementDropped() => Interlocked.Increment(ref _dropped);

    /// <summary>计算 Channel 使用率（需传入容量）</summary>
    public double GetUtilization(int capacity)
    {
        var pending = Written - Read;
        return capacity > 0 ? Math.Clamp((double)pending / capacity, 0, 1) : 0;
    }
}
