using FastEndpoints;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Interfaces;

namespace MesAdmin.Api.Features.SapWebhooks.Writeback;

public class WritebackRejectionEndpoint : MesEndpointWithoutRequest<WritebackResultResponse>
{
    private readonly ISapRejectionRepository _rejectionRepo;
    private readonly ISapClient _sapClient;
    private readonly ILogger<WritebackRejectionEndpoint> _logger;

    public WritebackRejectionEndpoint(
        ISapRejectionRepository rejectionRepo,
        ISapClient sapClient,
        ILogger<WritebackRejectionEndpoint> logger)
    {
        _rejectionRepo = rejectionRepo;
        _sapClient = sapClient;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("/rejections/{RejectionId:regex(^[a-fA-F0-9]{{26}}$)}/writeback");
        Group<SapWebhookGroup>();
        Summary(s =>
        {
            s.Summary = "手动触发 SAP 拒单回写";
            s.Description = "将指定拒单记录回写 SAP，用于重试失败的回写操作。";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var rejectionIdStr = Route<string>("RejectionId")
            ?? throw new InvalidOperationException("缺少拒单 ID");
        var rejectionId = Ulid.Parse(rejectionIdStr);

        var rejection = await _rejectionRepo.GetByIdAsync(rejectionId, ct);
        if (rejection is null)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            await HttpContext.Response.WriteAsync("{\"error\":\"拒单记录不存在\"}", ct);
            return;
        }

        if (rejection.WritebackStatus == Domain.Models.RejectionWritebackStatus.WrittenBack)
        {
            Response = new WritebackResultResponse
            {
                Success = true,
                Message = "该拒单已回写 SAP",
            };
            return;
        }

        var result = await _sapClient.WritebackRejectionAsync(rejection, ct);

        if (result.Success)
        {
            rejection.MarkWrittenBack(DateTimeOffset.UtcNow);
            _logger.LogInformation("手动触发拒单回写成功：{RejectionId}", rejectionIdStr);
        }
        else
        {
            rejection.MarkFailed(result.ErrorMessage ?? "手动触发失败", DateTimeOffset.UtcNow);
            _logger.LogWarning("手动触发拒单回写失败：{RejectionId}，{Error}", rejectionIdStr, result.ErrorMessage);
        }

        await _rejectionRepo.SaveChangesAsync(ct);

        Response = new WritebackResultResponse
        {
            Success = result.Success,
            Message = result.Success ? "SAP 拒单回写成功" : $"SAP 拒单回写失败：{result.ErrorMessage}",
            DocumentNumber = result.DocumentNumber,
        };
    }
}

public class WritebackResultResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? DocumentNumber { get; set; }
}
