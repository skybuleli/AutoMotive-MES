using System.Collections.Concurrent;
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
/// 订阅 PlcDataAcquisitionPipeline.PlcStream → 按设备 5s 时间门控采样 → ComputeOee → MessagePipe 发布。
/// ComputeOee 用 stackalloc Span&lt;double&gt; 零分配计算（AGENTS.md 5.1 铁律）。
/// </summary>
public sealed class OeeReactivePipeline : IHostedService, IAsyncDisposable
{
    private readonly PlcDataAcquisitionPipeline _pipeline;
    private readonly IAsyncPublisher<PlcDataChanged> _publisher;
    private readonly ILogger<OeeReactivePipeline> _logger;
    private readonly ConcurrentDictionary<string, OeeWindowState> _windowState = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSampleTime = new();
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
        // R3 无 GroupBy，按设备做时间门控采样：每台设备 5s 内只处理一次，
        // 既保留 ThrottleLast 的语义，又避免全局采样导致部分设备被漏采。
        _subscription = _pipeline.PlcStream
            .Where(ShouldSample)
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
                    _logger.ZLogError(ex, $"OEE 计算管道异常");
                }
            });

        _logger.ZLogInformation($"OEE 响应式管道启动：按设备 5s 时间门控 → ComputeOee → MessagePipe");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 按设备时间门控：同一设备两次采样间隔不小于 5s。
    /// R3 缺少 GroupBy，用 ConcurrentDictionary 的原子 AddOrUpdate 实现线程安全的 check-and-set。
    /// </summary>
    internal bool ShouldSample(PlcSnapshot snapshot)
    {
        var equipmentCode = snapshot.EquipmentCode;
        var timestamp = snapshot.Timestamp;

        var added = _lastSampleTime.AddOrUpdate(
            equipmentCode,
            timestamp,
            (_, lastTime) => (timestamp - lastTime).TotalSeconds >= 5 ? timestamp : lastTime);

        return added == timestamp;
    }

    /// <summary>
    /// 从 PLC 快照计算 OEE（零分配 stackalloc，AGENTS.md 5.1）。
    /// 采用滑动窗口 + 增量计算，避免全局锁与累积平均失真：
    /// 可用率 = 窗口内 Running 快照数 / 窗口总快照数
    /// 性能率 = 理想节拍 × 窗口内循环增量 / 窗口内运行时间增量
    /// 良品率 = 窗口内合格增量 / 窗口内总件数增量
    /// </summary>
    internal OeeRecord ComputeOeeFromSnapshot(PlcSnapshot snapshot)
    {
        // stackalloc 零分配中间计算（禁止 new double[]）
        Span<double> metrics = stackalloc double[3]; // [availability, performance, quality]

        var state = _windowState.GetOrAdd(snapshot.EquipmentCode, _ => new OeeWindowState());

        lock (state)
        {
            // 首次快照仅初始化上一快照累计值，不计算增量，避免把累计值误当作窗口增量
            if (!state.IsInitialized)
            {
                state.PreviousCycleCount = snapshot.CycleCount;
                state.PreviousGoodCount = snapshot.GoodCount;
                state.PreviousDefectCount = snapshot.DefectCount;
                state.PreviousRunTimeMs = snapshot.RunTimeMs;
                state.IsInitialized = true;

                // 可用率仍按当前状态计算；性能/良品率无增量，按默认值输出
                metrics[0] = snapshot.Status == EquipmentStatus.Running ? 1.0 : 0.0;
                metrics[1] = 0.0;
                metrics[2] = 1.0;
                return OeeRecord.Compute(
                    snapshot.EquipmentCode,
                    snapshot.Timestamp,
                    availability: metrics[0],
                    performance: metrics[1],
                    quality: metrics[2]);
            }

            // 计算与上一快照的增量（PLC 计数器为累计值）
            var deltaCycle = Math.Max(0, snapshot.CycleCount - state.PreviousCycleCount);
            var deltaGood = Math.Max(0, snapshot.GoodCount - state.PreviousGoodCount);
            var deltaDefect = Math.Max(0, snapshot.DefectCount - state.PreviousDefectCount);
            var deltaRunTime = Math.Max(0, snapshot.RunTimeMs - state.PreviousRunTimeMs);

            // 更新上一快照累计值（跨窗口保留，用于下一次增量）
            state.PreviousCycleCount = snapshot.CycleCount;
            state.PreviousGoodCount = snapshot.GoodCount;
            state.PreviousDefectCount = snapshot.DefectCount;
            state.PreviousRunTimeMs = snapshot.RunTimeMs;

            // 窗口满则重置，保证指标反映近期设备状态
            if (state.TotalSnapshots >= OeeWindowState.MaxWindowSnapshots)
            {
                state.TotalSnapshots = 0;
                state.RunningSnapshots = 0;
                state.DeltaCycleCount = 0;
                state.DeltaGoodCount = 0;
                state.DeltaDefectCount = 0;
                state.DeltaRunTimeMs = 0;
            }

            // 累积窗口数据
            state.TotalSnapshots++;
            if (snapshot.Status == EquipmentStatus.Running)
                state.RunningSnapshots++;

            state.DeltaCycleCount += deltaCycle;
            state.DeltaGoodCount += deltaGood;
            state.DeltaDefectCount += deltaDefect;
            state.DeltaRunTimeMs += deltaRunTime;

            // 可用率：窗口内 Running 时间占比
            metrics[0] = state.TotalSnapshots > 0
                ? (double)state.RunningSnapshots / state.TotalSnapshots
                : 0;

            // 性能率：理想节拍 10s/件，实际节拍 = 运行时间增量 / 循环增量
            const double idealCycleTimeMs = 10000; // 理想节拍 10s/件
            if (state.DeltaRunTimeMs > 0 && state.DeltaCycleCount > 0)
            {
                var actualCycleTimeMs = (double)state.DeltaRunTimeMs / state.DeltaCycleCount;
                metrics[1] = Math.Clamp(idealCycleTimeMs / actualCycleTimeMs, 0, 1);
            }
            else
            {
                metrics[1] = 0;
            }

            // 良品率：合格 / 总件数
            var totalParts = state.DeltaGoodCount + state.DeltaDefectCount;
            metrics[2] = totalParts > 0
                ? (double)state.DeltaGoodCount / totalParts
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
        /// <summary>窗口最大快照数（5s 采样间隔下约 5 分钟）</summary>
        public const int MaxWindowSnapshots = 60;

        // 是否已初始化上一快照累计值
        public bool IsInitialized;

        // 窗口计数
        public long TotalSnapshots;
        public long RunningSnapshots;

        // 上一快照累计值（用于计算增量）
        public long PreviousCycleCount;
        public long PreviousGoodCount;
        public long PreviousDefectCount;
        public long PreviousRunTimeMs;

        // 窗口内增量累计
        public long DeltaCycleCount;
        public long DeltaGoodCount;
        public long DeltaDefectCount;
        public long DeltaRunTimeMs;
    }
}
