using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using MesAdmin.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.RealTime;

/// <summary>
/// 飞书群机器人通知实现（S01）。
/// 读配置 Alerts:Feishu:WebhookUrl / Alerts:Feishu:Secret（可选加签）。
/// 未配置 webhook 时静默跳过——通知失败不阻断业务流程。
/// </summary>
public sealed class FeishuNotifier : IFeishuNotifier
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(5) };

    private readonly IConfiguration _configuration;
    private readonly ILogger<FeishuNotifier> _logger;

    public FeishuNotifier(IConfiguration configuration, ILogger<FeishuNotifier> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> SendTextAsync(string text, CancellationToken ct = default)
    {
        var webhookUrl = _configuration["Alerts:Feishu:WebhookUrl"];
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            _logger.ZLogInformation($"飞书 webhook 未配置，通知已跳过：{text[..Math.Min(text.Length, 40)]}...");
            return false;
        }

        var payload = FeishuBotMessage.Text(text);
        var secret = _configuration["Alerts:Feishu:Secret"];
        if (!string.IsNullOrWhiteSpace(secret))
            payload = payload.WithSignature(secret);

        try
        {
            using var response = await Client.PostAsJsonAsync(webhookUrl, payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.ZLogError($"飞书通知发送失败：{(int)response.StatusCode}");
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.ZLogError(ex, $"飞书通知发送异常");
            return false;
        }
    }
}

/// <summary>飞书自定义机器人消息体（与 Api InternalAlerts 适配器同构；Infrastructure 不可反向引用 Api，故此处独立定义）。</summary>
internal sealed record FeishuBotMessage(
    [property: JsonPropertyName("msg_type")] string MsgType,
    [property: JsonPropertyName("content")] FeishuTextContent Content,
    [property: JsonPropertyName("timestamp")] string? Timestamp = null,
    [property: JsonPropertyName("sign")] string? Sign = null)
{
    public static FeishuBotMessage Text(string text) => new("text", new FeishuTextContent(text));

    public FeishuBotMessage WithSignature(string secret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var stringToSign = $"{timestamp}\n{secret}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(stringToSign));
        var sign = Convert.ToBase64String(hmac.ComputeHash([]));
        return this with { Timestamp = timestamp, Sign = sign };
    }
}

internal sealed record FeishuTextContent([property: JsonPropertyName("text")] string Text);
