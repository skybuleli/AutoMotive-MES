using System.Net;
using FastEndpoints;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Api.Infrastructure;

/// <summary>
/// 全局异常后处理器：在 FastEndpoints 管道内捕获 HandleAsync 抛出的异常，
/// 映射领域异常为 HTTP 状态码（与原 GlobalExceptionHandler 中间件行为一致）。
/// 比 IMiddleware 更精准 —— 在 FE 管道内运行，auth 层异常由 ASP.NET Core 自行返回 401/403。
/// </summary>
public sealed class GlobalExceptionPostProcessor(
    ILogger<GlobalExceptionPostProcessor> logger) : IGlobalPostProcessor
{
    public async Task PostProcessAsync(IPostProcessorContext ctx, CancellationToken ct)
    {
        if (!ctx.HasExceptionOccurred)
            return;

        var ex = ctx.ExceptionDispatchInfo.SourceException;
        if (ctx.HttpContext.Response.HasStarted)
        {
            ctx.ExceptionDispatchInfo.Throw();
            return;
        }

        var (statusCode, message) = ex switch
        {
            KeyNotFoundException => (HttpStatusCode.NotFound, ex.Message),
            ArgumentException => (HttpStatusCode.BadRequest, ex.Message),
            InvalidOperationException => (HttpStatusCode.Conflict, ex.Message),
            _ => (HttpStatusCode.InternalServerError, "服务器内部错误"),
        };

        logger.ZLogError($"请求 {ctx.HttpContext.Request.Path} 异常: {ex.Message}");

        ctx.MarkExceptionAsHandled();

        ctx.HttpContext.Response.StatusCode = (int)statusCode;
        ctx.HttpContext.Response.ContentType = "application/problem+json";

        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = (int)statusCode,
            Title = statusCode.ToString(),
            Detail = message,
            Instance = ctx.HttpContext.Request.Path,
        };

        await ctx.HttpContext.Response.WriteAsJsonAsync(problem, ct);
    }
}
