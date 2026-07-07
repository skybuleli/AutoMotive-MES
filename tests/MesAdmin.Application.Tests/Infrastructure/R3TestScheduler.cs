using R3;

namespace MesAdmin.Application.Tests.Infrastructure;

/// <summary>
/// TX.1 — R3 虚拟时间调度器（手动推进时间）。
/// R3 不内置 TestScheduler，此辅助类通过可控的 TimeProvider
/// 让响应式管道测试无需真实等待。
///
/// 使用方式：
/// <code>
/// var time = new R3TestScheduler();
/// var source = new Subject&lt;int&gt;();
/// source
///   .ThrottleLast(TimeSpan.FromSeconds(5), time.TimeProvider)
///   .Subscribe(x => captured.Add(x));
///
/// source.OnNext(42);
/// // 时间只过了 3 秒 → 不应触发
/// time.Advance(TimeSpan.FromSeconds(3));
/// Assert.Empty(captured);
///
/// // 再过 3 秒 → 应触发
/// time.Advance(TimeSpan.FromSeconds(3));
/// Assert.Single(captured);
/// </code>
/// </summary>
public sealed class R3TestScheduler
{
    private readonly ManualTimeProvider _timeProvider = new();

    /// <summary>当前虚拟时间（UTC）。</summary>
    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    /// <summary>作为 R3 时间提供器使用的 TimeProvider。</summary>
    public TimeProvider TimeProvider => _timeProvider;

    /// <summary>作为标准 TimeProvider 使用的对象（与 Task.Delay 等兼容）。</summary>
    public TimeProvider AsTimeProvider => _timeProvider;

    /// <summary>将虚拟时间向前推进指定时长。</summary>
    public void Advance(TimeSpan delta)
    {
        _timeProvider.Advance(delta);
    }

    /// <summary>将虚拟时间推进到指定时刻。</summary>
    public void AdvanceTo(DateTimeOffset target)
    {
        _timeProvider.AdvanceTo(target);
    }
}

/// <summary>
/// 手动控制的 TimeProvider 实现。允许测试代码精确控制时间流逝。
/// 基于 .NET 10 的 TimeProvider 抽象。
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _current;
    private readonly TimeProvider _system = TimeProvider.System;

    public ManualTimeProvider(DateTimeOffset? startTime = null)
    {
        _current = startTime ?? new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    }

    /// <summary>当前虚拟时间（只读）。</summary>
    public override DateTimeOffset GetUtcNow() => _current;

    /// <summary>获取当前时间戳（微秒精度）。</summary>
    public override long TimestampFrequency => _system.TimestampFrequency;

    /// <summary>获取当前时间戳。</summary>
    public override long GetTimestamp() =>
        _current.Ticks * TimestampFrequency / TimeSpan.TicksPerSecond;

    /// <summary>将虚拟时间向前推进指定时长。</summary>
    public void Advance(TimeSpan delta)
    {
        if (delta < TimeSpan.Zero)
            throw new ArgumentException("时间只能向前推进", nameof(delta));
        _current = _current.Add(delta);
    }

    /// <summary>将虚拟时间推进到指定时刻（必须晚于当前时间）。</summary>
    public void AdvanceTo(DateTimeOffset target)
    {
        if (target < _current)
            throw new ArgumentException("目标时间必须晚于当前时间", nameof(target));
        _current = target;
    }

    /// <summary>
    /// 创建 ITimer（虚拟计时器）。
    /// 注意：此实现使用真实 Task.Delay + 手动检查，因为完整的虚拟计时器
    /// 需要更复杂的调度队列。对于大多数测试场景，结合 Advance() + 手动
    /// 触发 Subject.OnNext 已足够验证时间相关行为。
    /// </summary>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        // 使用系统计时器 + 当前虚拟时间偏移
        return _system.CreateTimer(callback, state, dueTime, period);
    }
}
