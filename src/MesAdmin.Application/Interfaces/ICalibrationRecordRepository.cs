using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// 校准记录仓储接口（S01）。
/// </summary>
public interface ICalibrationRecordRepository
{
    /// <summary>某量具的校准历史（新→旧）</summary>
    Task<List<CalibrationRecord>> GetByGaugeIdAsync(Ulid gaugeId, CancellationToken ct = default);

    Task AddAsync(CalibrationRecord record, CancellationToken ct = default);
}

/// <summary>
/// 飞书群机器人通知接口（Infrastructure 实现，读 Alerts:Feishu 配置）。
/// 未配置 webhook 时静默跳过。后续切片（点检异常、NCR 待办）复用。
/// </summary>
public interface IFeishuNotifier
{
    /// <summary>发送纯文本消息。返回 false 表示未配置或发送失败（不抛异常）。</summary>
    Task<bool> SendTextAsync(string text, CancellationToken ct = default);
}
