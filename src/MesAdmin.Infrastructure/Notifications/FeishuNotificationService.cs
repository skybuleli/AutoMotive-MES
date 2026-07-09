using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MesAdmin.Application.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.Notifications;

/// <summary>
/// Feishu (Lark) custom bot webhook notification service.
/// Sends Andon L2/L3 escalation alerts as interactive card messages to Feishu group chat.
/// Thread-safe: volatile + lock for webhook URL writes.
/// </summary>
public sealed class FeishuNotificationService : IFeishuNotificationService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FeishuNotificationService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };
    private volatile string? _webhookUrl;
    private readonly object _lock = new();

    private static readonly SeverityColors Colors = new();

    private sealed class SeverityColors
    {
        public string ForSeverity(string severity) => severity switch
        {
            "Critical" => "red",
            "Major" => "orange",
            _ => "blue"
        };
    }

    public FeishuNotificationService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<FeishuNotificationService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("FeishuBot");
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _logger = logger;
        _webhookUrl = configuration["FeishuBot:WebhookUrl"];
    }

    public string? GetWebhookUrl()
    {
        var url = _webhookUrl;
        if (string.IsNullOrEmpty(url))
            return null;

        try
        {
            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 0)
            {
                var token = segments[^1];
                var masked = token.Length > 8
                    ? token[..4] + "****" + token[^4..]
                    : "****";
                return $"{uri.Scheme}://{uri.Host}/.../{masked}";
            }
            return url;
        }
        catch
        {
            return url;
        }
    }

    public void SetWebhookUrl(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        lock (_lock)
        {
            _webhookUrl = url;
        }
        _logger.ZLogInformation($"Feishu webhook URL updated");
    }

    public async Task SendTextAsync(string text, CancellationToken ct = default)
    {
        var url = _webhookUrl;
        if (string.IsNullOrEmpty(url))
        {
            _logger.ZLogWarning($"Feishu webhook URL not configured, skipping text message");
            return;
        }

        try
        {
            var payload = new JsonObject
            {
                ["msg_type"] = "text",
                ["content"] = new JsonObject
                {
                    ["text"] = text
                }
            };

            using var content = JsonContent.Create(payload, options: JsonOptions);
            var response = await _httpClient.PostAsync(url, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                AutoMesMetrics.RecordFeishuNotificationSent(true);
                _logger.ZLogDebug($"Feishu text message sent successfully");
            }
            else
            {
                AutoMesMetrics.RecordFeishuNotificationSent(false);
                _logger.ZLogError($"Feishu text message failed: HTTP {response.StatusCode} {body}");
            }
        }
        catch (Exception ex)
        {
            AutoMesMetrics.RecordFeishuNotificationSent(false);
            _logger.ZLogError($"Feishu text message error: {ex.Message}");
        }
    }

    public async Task SendAndonAlertCardAsync(
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
        CancellationToken ct = default)
    {
        var url = _webhookUrl;
        if (string.IsNullOrEmpty(url))
        {
            _logger.ZLogWarning($"Feishu webhook URL not configured, skipping Andon alert card");
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - occurredAt;
        var elapsedStr = elapsed.TotalHours >= 1
            ? $"{elapsed.Hours}h{elapsed.Minutes}min"
            : elapsed.TotalMinutes >= 1
                ? $"{elapsed.Minutes}min{elapsed.Seconds}s"
                : $"{elapsed.Seconds}s";

        try
        {
            var severityColor = Colors.ForSeverity(severity);
            var levelLabel = escalationLevel switch
            {
                1 => "L2 Shift Leader",
                2 => "L3 Production Manager",
                _ => $"L{escalationLevel + 1}"
            };
            var severityLabel = severity switch
            {
                "Critical" => "CRITICAL",
                "Major" => "MAJOR",
                _ => "MINOR"
            };

            // Build markdown content for card body (use \n for line breaks in lark_md)
            var infoLines = new List<string>
            {
                "**Equipment:** " + equipmentCode,
                "**Station:** " + station.ToString(),
                "**Alarm Type:** " + alarmType,
                "**Severity:** " + severityLabel
            };
            var infoContent = string.Join("\n", infoLines);

            var valueLines = new List<string>
            {
                "**Process Value:** " + processValue.ToString("F1") + (upperLimit.HasValue ? " (limit: " + upperLimit.Value.ToString("F1") + ")" : ""),
                "**Elapsed:** " + elapsedStr,
                "**Occurred:** " + occurredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            };
            var valueContent = string.Join("\n", valueLines);

            var payload = new JsonObject
            {
                ["msg_type"] = "interactive",
                ["card"] = new JsonObject
                {
                    ["config"] = new JsonObject
                    {
                        ["wide_screen_mode"] = true
                    },
                    ["header"] = new JsonObject
                    {
                        ["title"] = new JsonObject
                        {
                            ["tag"] = "plain_text",
                            ["content"] = "[AutoMES] Andon " + levelLabel + " Escalation - " + eventNumber
                        },
                        ["template"] = severityColor
                    },
                    ["elements"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["tag"] = "markdown",
                            ["content"] = "**Description:** " + description
                        },
                        new JsonObject { ["tag"] = "hr" },
                        new JsonObject
                        {
                            ["tag"] = "div",
                            ["text"] = new JsonObject
                            {
                                ["tag"] = "lark_md",
                                ["content"] = infoContent
                            }
                        },
                        new JsonObject { ["tag"] = "hr" },
                        new JsonObject
                        {
                            ["tag"] = "div",
                            ["text"] = new JsonObject
                            {
                                ["tag"] = "lark_md",
                                ["content"] = valueContent
                            }
                        }
                    }
                }
            };

            using var content = JsonContent.Create(payload, options: JsonOptions);
            var response = await _httpClient.PostAsync(url, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                AutoMesMetrics.RecordFeishuNotificationSent(true);
                _logger.ZLogInformation($"Feishu Andon alert card sent: {eventNumber} L{escalationLevel}+");
            }
            else
            {
                AutoMesMetrics.RecordFeishuNotificationSent(false);
                _logger.ZLogError($"Feishu Andon alert card failed: HTTP {response.StatusCode} {body}");
            }
        }
        catch (Exception ex)
        {
            AutoMesMetrics.RecordFeishuNotificationSent(false);
            _logger.ZLogError($"Feishu Andon alert card error: {ex.Message}");
        }
    }

    public async Task<bool> SendTestAsync(CancellationToken ct = default)
    {
        var url = _webhookUrl;
        if (string.IsNullOrEmpty(url))
        {
            _logger.ZLogWarning($"Feishu webhook URL not configured, cannot send test");
            return false;
        }

        try
        {
            var now = DateTimeOffset.UtcNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
            var lines = new List<string>
            {
                "**AutoMES Feishu Notification Test**",
                "",
                "Time: " + now,
                "Service: MesAdmin.Api",
                "Environment: " + env
            };
            var contentStr = string.Join("\n", lines);

            var payload = new JsonObject
            {
                ["msg_type"] = "interactive",
                ["card"] = new JsonObject
                {
                    ["config"] = new JsonObject
                    {
                        ["wide_screen_mode"] = true
                    },
                    ["header"] = new JsonObject
                    {
                        ["title"] = new JsonObject
                        {
                            ["tag"] = "plain_text",
                            ["content"] = "AutoMES Feishu Notification Test"
                        },
                        ["template"] = "green"
                    },
                    ["elements"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["tag"] = "markdown",
                            ["content"] = contentStr
                        }
                    }
                }
            };

            using var content = JsonContent.Create(payload, options: JsonOptions);
            var response = await _httpClient.PostAsync(url, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.ZLogInformation($"Feishu test message sent successfully");
                return true;
            }
            else
            {
                _logger.ZLogError($"Feishu test message failed: HTTP {response.StatusCode} {body}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.ZLogError($"Feishu test message error: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
