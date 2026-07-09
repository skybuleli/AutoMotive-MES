using FastEndpoints;
using FluentValidation;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Security;
using MesAdmin.Infrastructure.Notifications;

namespace MesAdmin.Api.Features.Notifications;

// ═══════════════════════════════════════════
//  GET /api/v1/feishu/settings — get webhook config
// ═══════════════════════════════════════════

public class GetFeishuSettingsEndpoint : MesEndpointWithoutRequest<FeishuSettingsResponse>
{
    public override void Configure()
    {
        Get("/settings");
        Group<FeishuGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.EquipmentEngineer);
        Summary(s => s.Summary = "Get Feishu bot webhook settings");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var feishu = Resolve<IFeishuNotificationService>();
        Response = new FeishuSettingsResponse(feishu.GetWebhookUrl());
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  PUT /api/v1/feishu/settings — update webhook URL
// ═══════════════════════════════════════════

public class UpdateFeishuSettingsEndpoint : MesEndpoint<UpdateFeishuSettingsRequest, FeishuSettingsResponse>
{
    public override void Configure()
    {
        Put("/settings");
        Group<FeishuGroup>();
        Roles(MesRoles.ProductionManager);
        Summary(s => s.Summary = "Update Feishu webhook URL");
    }

    public override async Task HandleAsync(UpdateFeishuSettingsRequest req, CancellationToken ct)
    {
        var feishu = Resolve<IFeishuNotificationService>();
        feishu.SetWebhookUrl(req.WebhookUrl);
        Response = new FeishuSettingsResponse(feishu.GetWebhookUrl());
        await SendDualAsync(ct);
    }
}

public class UpdateFeishuSettingsValidator : Validator<UpdateFeishuSettingsRequest>
{
    public UpdateFeishuSettingsValidator()
    {
        RuleFor(x => x.WebhookUrl)
            .NotEmpty().WithMessage("Webhook URL is required")
            .Must(IsValidUrl).WithMessage("Must be a valid HTTP/HTTPS URL");
    }

    private static bool IsValidUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https";
}

// ═══════════════════════════════════════════
//  POST /api/v1/feishu/test — send test message
// ═══════════════════════════════════════════

public class SendFeishuTestEndpoint : MesEndpointWithoutRequest<FeishuTestResponse>
{
    public override void Configure()
    {
        Post("/test");
        Group<FeishuGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.EquipmentEngineer);
        Summary(s => s.Summary = "Send Feishu test message");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var feishu = Resolve<IFeishuNotificationService>();
        var success = await feishu.SendTestAsync(ct);
        Response = new FeishuTestResponse(
            success,
            success ? "Test message sent successfully" : "Failed to send test message, check webhook URL and network");
        await SendDualAsync(ct);
    }
}
