using MesAdmin.Application.Observability;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Plc;
using MessagePipe;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using R3;
using ZLogger;

namespace MesAdmin.Infrastructure.RealTime;

/// <summary>
/// OEE 响应式管道（T2.14）。
/// 订阅 PlcDataAcquisitionPipeline.PlcStream → R3 ThrottleLast(5s) 采样 → ComputeOee → MessagePipe 发布。
/// R3 无 Sample 算子，用 ThrottleLast（周期性取末值，等价 Rx Sample）。
/// ComputeOee 用 stackalloc Span&lt;double&gt; 零分配计算（AGENTS.md 5.1 铁律）。
/// </summary>
public sealed class OeeReactivePipeline : IHostedService, IAsyncDisposable
{
    private readonly PlcDataAcquisitionPipeline _pipeline;
    private readonly IAsyncPublisher<PlcDataChanged> _publisher;
    private readonly ILogger<OeeReactivePipeline> _logger;
    private readonly Dictionary<string, OeeWindowState> _windowState = new();
    private readonly object _lock = new();
    private IDisposable? _subscription;

    public OeeReactivePipeline(
        PlcDataAcquisitionPipeline pipeline,
        IAsyncPublisher<PlcDataChanged> publisher,
        ILogger<OeeReactivePipeline> logger)
    {
        _pipeline = pipeline;
        _publisher = publisher;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // R3 响应式管道：PlcStream → ThrottleLast(5s) → ComputeOee → Publish
        // ThrottleLast 替代 R3 缺失的 Sample，每 5s 取窗口末值
        _subscription = _pipeline.PlcStream
            .ThrottleLast(TimeSpan.FromSeconds(5))
            .SubscribeAwait(async (snapshot, ct) =>
            {
                try
                {
                    var oee = ComputeOeeFromSnapshot(snapshot);
                    AutoMesMetrics.SetOeeValue(snapshot.EquipmentCode, oee.Oee);
                    await _publisher.PublishAsync(new PlcDataChanged(oee), ct);
                }
                catch (Exception ex)
                {
                    _logger.ZLogError($"OEE 计算管道异常：{ex.Message}");
                }
            });

        _logger.ZLogInformation($"OEE 响应式管道启动：ThrottleLast(5s) → ComputeOee → MessagePipe");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 从 PLC 快照计算 OEE（零分配 stackalloc，AGENTS.md 5.1）。
    /// 可用率 = 运行中设备运行时长占比
    /// 性能率 = 实际节拍 / 理想节拍（简化：循环次数 / 运行时长 × 理想节拍）
    /// 良品率 = 合格件数 / 总件数
    /// </summary>
    private OeeRecord ComputeOeeFromSnapshot(PlcSnapshot snapshot)
    {
        // stackalloc 零分配中间计算（禁止 new double[]）
        Span<double> metrics = stackalloc double[3]; // [availability, performance, quality]

        lock (_lock)
        {
            if (!_windowState.TryGetValue(snapshot.EquipmentCode, out var state))
            {
                state = new OeeWindowState();
                _windowState[snapshot.EquipmentCode] = state;
            }

            // 累积窗口数据
            state.TotalSnapshots++;
            if (snapshot.Status == EquipmentStatus.Running)
                state.RunningSnapshots++;

            state.CycleCount = snapshot.CycleCount;
            state.GoodCount = snapshot.GoodCount;
            state.DefectCount = snapshot.DefectCount;
            state.RunTimeMs = snapshot.RunTimeMs;

            // 可用率：运行快照数 / 总快照数（窗口内设备运行时间占比）
            metrics[0] = state.TotalSnapshots > 0
                ? (double)state.RunningSnapshots / state.TotalSnapshots
                : 0;

            // 性能率：理想节拍 10s/件，实际节拍 = 运行时长 / 循环数
            const double idealCycleTimeMs = 10000; // 理想节拍 10s/件
            if (state.RunTimeMs > 0 && state.CycleCount > 0)
            {
                var actualCycleTimeMs = (double)state.RunTimeMs / state.CycleCount;
                metrics[1] = Math.Clamp(idealCycleTimeMs / actualCycleTimeMs, 0, 1);
            }
            else
            {
                metrics[1] = 0;
            }

            // 良品率：合格 / 总件数
            var totalParts = state.GoodCount + state.DefectCount;
            metrics[2] = totalParts > 0
                ? (double)state.GoodCount / totalParts
                : 1.0;
        }

        return OeeRecord.Compute(
            snapshot.EquipmentCode,
            snapshot.Timestamp,
            availability: metrics[0],
            performance: metrics[1],
            quality: metrics[2]);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _logger.ZLogInformation($"OEE 响应式管道已停止");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _subscription?.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>OEE 计算窗口状态（每设备）</summary>
    private sealed class OeeWindowState
    {
        public long TotalSnapshots;
        public long RunningSnapshots;
        public long CycleCount;
        public long GoodCount;
        public long DefectCount;
        public long RunTimeMs;
    }
}
