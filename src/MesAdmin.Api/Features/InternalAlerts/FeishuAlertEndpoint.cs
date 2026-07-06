using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FastEndpoints;
using ZLogger;

namespace MesAdmin.Api.Features.InternalAlerts;

public class FeishuAlertEndpoint : Endpoint<AlertmanagerWebhookRequest>
{
    private static readonly HttpClient Client = new();
    private readonly IConfiguration _configuration;
    private readonly ILogger<FeishuAlertEndpoint> _alertLogger;

    public FeishuAlertEndpoint(IConfiguration configuration, ILogger<FeishuAlertEndpoint> alertLogger)
    {
        _configuration = configuration;
        _alertLogger = alertLogger;
    }

    public override void Configure()
    {
        Post("/internal/alerts/feishu");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "Alertmanager 飞书告警适配器";
            s.Description = "接收 Alertmanager webhook，格式化后转发到飞书自定义机器人。";
        });
    }

    public override async Task HandleAsync(AlertmanagerWebhookRequest req, CancellationToken ct)
    {
        var webhookUrl = _configuration["Alerts:Feishu:WebhookUrl"];
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            _alertLogger.ZLogError($"飞书告警 webhook 未配置，Alertmanager payload 已拒绝");
            await Send.StringAsync("Alerts:Feishu:WebhookUrl is not configured", statusCode: StatusCodes.Status503ServiceUnavailable, cancellation: ct);
            return;
        }

        var payload = FeishuBotMessage.Text(FormatMessage(req));
        var secret = _configuration["Alerts:Feishu:Secret"];
        if (!string.IsNullOrWhiteSpace(secret))
        {
            payload = payload.WithSignature(secret);
        }

        HttpResponseMessage response;
        try
        {
            response = await Client.PostAsJsonAsync(webhookUrl, payload, ct);
        }
        catch (HttpRequestException ex)
        {
            _alertLogger.ZLogError(ex, $"飞书告警发送异常");
            await Send.StringAsync("Feishu webhook failed", statusCode: StatusCodes.Status502BadGateway, cancellation: ct);
            return;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _alertLogger.ZLogError(ex, $"飞书告警发送超时");
            await Send.StringAsync("Feishu webhook timed out", statusCode: StatusCodes.Status502BadGateway, cancellation: ct);
            return;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _alertLogger.ZLogError($"飞书告警发送失败：{(int)response.StatusCode}");
                await Send.StringAsync("Feishu webhook failed", statusCode: StatusCodes.Status502BadGateway, cancellation: ct);
                return;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            if (!FeishuAccepted(body, out var rejectionReason))
            {
                _alertLogger.ZLogError($"飞书告警被拒绝：{rejectionReason}");
                await Send.StringAsync("Feishu webhook rejected", statusCode: StatusCodes.Status502BadGateway, cancellation: ct);
                return;
            }
        }

        _alertLogger.ZLogInformation($"飞书告警已发送：status={req.Status}, alerts={req.Alerts.Count}");
        await Send.NoContentAsync(ct);
    }

    private static string FormatMessage(AlertmanagerWebhookRequest req)
    {
        var severity = Get(req.CommonLabels, "severity", "unknown");
        var category = Get(req.CommonLabels, "category", "general");
        var title = Get(req.CommonAnnotations, "summary", $"AutoMES alert {req.Status}");
        var description = Get(req.CommonAnnotations, "description", "");

        var lines = new List<string>
        {
            $"AutoMES 告警：{title}",
            $"状态：{req.Status}",
            $"级别：{severity}",
            $"类别：{category}",
            $"数量：{req.Alerts.Count}"
        };

        if (!string.IsNullOrWhiteSpace(description))
            lines.Add($"说明：{description}");

        foreach (var alert in req.Alerts.Take(5))
        {
            var alertName = Get(alert.Labels, "alertname", "unknown");
            var line = Get(alert.Labels, "line", "");
            var station = Get(alert.Labels, "station", "");
            var equipment = Get(alert.Labels, "equipment_code", "");
            lines.Add($"- {alertName} [{alert.Status}] {JoinNonEmpty(line, station, equipment)}");
        }

        if (req.Alerts.Count > 5)
            lines.Add($"... 其余 {req.Alerts.Count - 5} 条已省略");

        return string.Join('\n', lines);
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string key, string fallback)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static string JoinNonEmpty(params string[] values)
        => string.Join(" / ", values.Where(v => !string.IsNullOrWhiteSpace(v)));

    private static bool FeishuAccepted(string body, out string rejectionReason)
    {
        rejectionReason = "";

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var code = root.TryGetProperty("code", out var codeElement)
                ? codeElement.GetInt32()
                : root.TryGetProperty("StatusCode", out var statusCodeElement)
                    ? statusCodeElement.GetInt32()
                    : -1;

            if (code == 0)
                return true;

            var message = root.TryGetProperty("msg", out var msgElement)
                ? msgElement.GetString()
                : root.TryGetProperty("StatusMessage", out var statusMessageElement)
                    ? statusMessageElement.GetString()
                    : "unknown";

            rejectionReason = $"code={code}, message={message}";
            return false;
        }
        catch (JsonException)
        {
            rejectionReason = "invalid json response";
            return false;
        }
    }
}

public class AlertmanagerWebhookRequest
{
    public string Status { get; set; } = "";
    public string Receiver { get; set; } = "";
    public string GroupKey { get; set; } = "";
    public Dictionary<string, string> GroupLabels { get; set; } = [];
    public Dictionary<string, string> CommonLabels { get; set; } = [];
    public Dictionary<string, string> CommonAnnotations { get; set; } = [];
    public List<AlertmanagerAlert> Alerts { get; set; } = [];
}

public class AlertmanagerAlert
{
    public string Status { get; set; } = "";
    public Dictionary<string, string> Labels { get; set; } = [];
    public Dictionary<string, string> Annotations { get; set; } = [];
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string GeneratorUrl { get; set; } = "";
    public string Fingerprint { get; set; } = "";
}

public sealed record FeishuBotMessage(
    [property: JsonPropertyName("msg_type")]
    string MsgType,
    [property: JsonPropertyName("content")]
    FeishuTextContent Content,
    [property: JsonPropertyName("timestamp")]
    string? Timestamp = null,
    [property: JsonPropertyName("sign")]
    string? Sign = null)
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

public sealed record FeishuTextContent([property: JsonPropertyName("text")] string Text);
