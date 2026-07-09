using System.Net;
using System.Text.Json;
using MesAdmin.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MesAdmin.Application.Tests;

/// <summary>
/// 飞书通知服务单元测试（P2 外部通知集成）。
/// 使用假 HttpMessageHandler 拦截出站请求，验证载荷结构、脱敏、失败容错。
/// 不依赖数据库，纯内存测试。
/// </summary>
public class FeishuNotificationServiceTests
{
    private const string TestWebhook =
        "https://open.feishu.cn/open-apis/bot/v2/hook/abcd1234efgh5678";

    // ── 测试替身 ─────────────────────────────────────────────

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _responseBody;

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public int CallCount { get; private set; }

        public CapturingHandler(HttpStatusCode status = HttpStatusCode.OK, string responseBody = "{\"code\":0}")
        {
            _status = status;
            _responseBody = responseBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_responseBody)
            };
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static FeishuNotificationService CreateService(
        HttpMessageHandler handler, string? webhookUrl = TestWebhook)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeishuBot:WebhookUrl"] = webhookUrl
            })
            .Build();

        return new FeishuNotificationService(
            new StubHttpClientFactory(handler),
            config,
            NullLogger<FeishuNotificationService>.Instance);
    }

    // ── GetWebhookUrl 脱敏 ──────────────────────────────────

    [Fact]
    public void GetWebhookUrl_ShouldMaskToken()
    {
        using var handler = new CapturingHandler();
        var service = CreateService(handler);

        var masked = service.GetWebhookUrl();

        Assert.NotNull(masked);
        Assert.DoesNotContain("abcd1234efgh5678", masked);
        Assert.Contains("abcd", masked);
        Assert.Contains("5678", masked);
        Assert.Contains("****", masked);
    }

    [Fact]
    public void GetWebhookUrl_WhenUnconfigured_ShouldReturnNull()
    {
        using var handler = new CapturingHandler();
        var service = CreateService(handler, webhookUrl: null);

        Assert.Null(service.GetWebhookUrl());
    }

    // ── SetWebhookUrl 运行时更新 ────────────────────────────

    [Fact]
    public void SetWebhookUrl_ShouldUpdateConfiguration()
    {
        using var handler = new CapturingHandler();
        var service = CreateService(handler, webhookUrl: null);
        Assert.Null(service.GetWebhookUrl());

        service.SetWebhookUrl(TestWebhook);

        Assert.NotNull(service.GetWebhookUrl());
    }

    [Fact]
    public void SetWebhookUrl_WithBlank_ShouldThrow()
    {
        using var handler = new CapturingHandler();
        var service = CreateService(handler);

        Assert.Throws<ArgumentException>(() => service.SetWebhookUrl("  "));
    }

    // ── SendTextAsync ───────────────────────────────────────

    [Fact]
    public async Task SendTextAsync_ShouldPostTextPayload()
    {
        using var handler = new CapturingHandler();
        var service = CreateService(handler);

        await service.SendTextAsync("hello factory");

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        using var doc = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("text", doc.RootElement.GetProperty("msg_type").GetString());
        Assert.Equal("hello factory",
            doc.RootElement.GetProperty("content").GetProperty("text").GetString());
    }

    [Fact]
    public async Task SendTextAsync_WhenUnconfigured_ShouldSkipHttpCall()
    {
        using var handler = new CapturingHandler();
        var service = CreateService(handler, webhookUrl: null);

        await service.SendTextAsync("ignored");

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SendTextAsync_WhenServerErrors_ShouldNotThrow()
    {
        using var handler = new CapturingHandler(HttpStatusCode.InternalServerError, "boom");
        var service = CreateService(handler);

        await service.SendTextAsync("hello");

        Assert.Equal(1, handler.CallCount);
    }

    // ── SendAndonAlertCardAsync ─────────────────────────────

    [Theory]
    [InlineData("Critical", "red")]
    [InlineData("Major", "orange")]
    [InlineData("Minor", "blue")]
    public async Task SendAndonAlertCard_ShouldUseSeverityColor(string severity, string expectedColor)
    {
        using var handler = new CapturingHandler();
        var service = CreateService(handler);

        await service.SendAndonAlertCardAsync(
            eventNumber: "AE-20260709-0001",
            equipmentCode: "EQ-TQ-01",
            station: 3,
            alarmType: "TorqueExceeded",
            severity: severity,
            description: "Torque out of range",
            escalationLevel: 1,
            processValue: 24.5,
            upperLimit: 23.0,
            occurredAt: DateTimeOffset.UtcNow.AddMinutes(-2));

        using var doc = JsonDocument.Parse(handler.LastBody!);
        var root = doc.RootElement;
        Assert.Equal("interactive", root.GetProperty("msg_type").GetString());
        var card = root.GetProperty("card");
        Assert.Equal(expectedColor,
            card.GetProperty("header").GetProperty("template").GetString());
        var title = card.GetProperty("header").GetProperty("title")
            .GetProperty("content").GetString();
        Assert.Contains("AE-20260709-0001", title);
    }

    [Fact]
    public async Task SendAndonAlertCard_ShouldMapEscalationLevelLabel()
    {
        using var handler = new CapturingHandler();
        var service = CreateService(handler);

        await service.SendAndonAlertCardAsync(
            "AE-20260709-0002", "EQ-HYD-01", 5, "LeakExceeded", "Critical",
            "Leak rate too high", escalationLevel: 2, processValue: 0.8,
            upperLimit: 0.5, occurredAt: DateTimeOffset.UtcNow.AddMinutes(-90));

        using var doc = JsonDocument.Parse(handler.LastBody!);
        var title = doc.RootElement.GetProperty("card")
            .GetProperty("header").GetProperty("title")
            .GetProperty("content").GetString();
        Assert.Contains("L3", title);
    }

    [Fact]
    public async Task SendAndonAlertCard_WhenUnconfigured_ShouldSkip()
    {
        using var handler = new CapturingHandler();
        var service = CreateService(handler, webhookUrl: null);

        await service.SendAndonAlertCardAsync(
            "AE-1", "EQ-1", 1, "TorqueExceeded", "Major", "x",
            1, 1.0, 1.0, DateTimeOffset.UtcNow);

        Assert.Equal(0, handler.CallCount);
    }

    // ── SendTestAsync ───────────────────────────────────────

    [Fact]
    public async Task SendTestAsync_OnSuccess_ShouldReturnTrue()
    {
        using var handler = new CapturingHandler(HttpStatusCode.OK);
        var service = CreateService(handler);

        var result = await service.SendTestAsync();

        Assert.True(result);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SendTestAsync_OnServerError_ShouldReturnFalse()
    {
        using var handler = new CapturingHandler(HttpStatusCode.BadGateway, "nope");
        var service = CreateService(handler);

        Assert.False(await service.SendTestAsync());
    }

    [Fact]
    public async Task SendTestAsync_WhenUnconfigured_ShouldReturnFalse()
    {
        using var handler = new CapturingHandler();
        var service = CreateService(handler, webhookUrl: null);

        Assert.False(await service.SendTestAsync());
        Assert.Equal(0, handler.CallCount);
    }
}
