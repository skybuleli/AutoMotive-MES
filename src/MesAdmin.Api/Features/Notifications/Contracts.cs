using FastEndpoints;
using MemoryPack;

namespace MesAdmin.Api.Features.Notifications;

/// <summary>
/// 飞书通知端点组（api/v1/feishu）。
/// </summary>
public class FeishuGroup : Group
{
    public FeishuGroup() => Configure("api/v1/feishu", ep => { });
}

// ═══════════════════════════════════════════
//  请求 DTO
// ═══════════════════════════════════════════

[MemoryPackable]
public partial class UpdateFeishuSettingsRequest
{
    public string WebhookUrl { get; set; } = string.Empty;
}

// ═══════════════════════════════════════════
//  响应 DTO
// ═══════════════════════════════════════════

[MemoryPackable]
public partial record FeishuSettingsResponse(
    string? WebhookUrl);

[MemoryPackable]
public partial record FeishuTestResponse(
    bool Success,
    string? Message);
