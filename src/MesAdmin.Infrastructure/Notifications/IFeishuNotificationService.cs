namespace MesAdmin.Infrastructure.Notifications;

/// <summary>
/// 飞书自定义机器人通知服务接口。
/// 用于在 Andon L2/L3 升级时自动推送报警消息到飞书群。
/// </summary>
public interface IFeishuNotificationService
{
    /// <summary>推送纯文本消息</summary>
    Task SendTextAsync(string text, CancellationToken ct = default);

    /// <summary>推送 Andon 报警卡片消息（L2/L3 升级专用）</summary>
    Task SendAndonAlertCardAsync(
        string eventNumber,
        string equipmentCode,
        int station,
        string alarmType,
        string severity,
        string description,
        int escalationLevel,
        double processValue,
        double? upperLimit,
        DateTimeOffset occurredAt,
        CancellationToken ct = default);

    /// <summary>发送测试消息（验证 webhook 配置可用性）</summary>
    Task<bool> SendTestAsync(CancellationToken ct = default);

    /// <summary>获取当前配置的 webhook URL（URL 部分脱敏）</summary>
    string? GetWebhookUrl();

    /// <summary>运行时更新 webhook URL</summary>
    void SetWebhookUrl(string url);
}
