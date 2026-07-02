using FastEndpoints;
using MemoryPack;
using Microsoft.AspNetCore.Http;

namespace MesAdmin.Api.Infrastructure;

/// <summary>
/// MemoryPack 双协议端点基类。
/// 响应端：根据 Accept 头选择 JSON（默认）或 MemoryPack 二进制输出。
/// </summary>
/// <typeparam name="TRequest">请求 DTO</typeparam>
/// <typeparam name="TResponse">响应 DTO（需标注 [MemoryPackable]）</typeparam>
public abstract class MesEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull, new()
    where TResponse : class
{
    private const string MemoryPackContentType = "application/x-memorypack";

    /// <summary>
    /// 发送响应，自动根据 Accept 头切换 JSON/MemoryPack。
    /// 端点设置 Response 属性后调用此方法。
    /// </summary>
    protected async Task SendDualAsync(CancellationToken ct)
    {
        if (IsMemoryPackRequested())
        {
            var bytes = MemoryPackSerializer.Serialize(Response);
            HttpContext.Response.ContentType = MemoryPackContentType;
            await HttpContext.Response.Body.WriteAsync(bytes, ct);
            return;
        }

        await Send.OkAsync(Response, ct);
    }

    /// <summary>
    /// 发送 201 Created 响应，自动根据 Accept 头切换 JSON/MemoryPack。
    /// </summary>
    protected async Task SendCreatedDualAsync<TOtherEndpoint>(object routeValues, CancellationToken ct)
        where TOtherEndpoint : class, IEndpoint
    {
        if (IsMemoryPackRequested())
        {
            var bytes = MemoryPackSerializer.Serialize(Response);
            HttpContext.Response.StatusCode = StatusCodes.Status201Created;
            HttpContext.Response.ContentType = MemoryPackContentType;
            await HttpContext.Response.Body.WriteAsync(bytes, ct);
            return;
        }

        await Send.CreatedAtAsync<TOtherEndpoint>(routeValues, Response, cancellation: ct);
    }

    private bool IsMemoryPackRequested()
    {
        var accept = HttpContext.Request.Headers.Accept.ToString();
        return accept.Contains(MemoryPackContentType, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// 无请求体的 MemoryPack 双协议端点。
/// </summary>
public abstract class MesEndpointWithoutRequest<TResponse> : EndpointWithoutRequest<TResponse>
    where TResponse : class
{
    private const string MemoryPackContentType = "application/x-memorypack";

    protected async Task SendDualAsync(CancellationToken ct)
    {
        if (IsMemoryPackRequested())
        {
            var bytes = MemoryPackSerializer.Serialize(Response);
            HttpContext.Response.ContentType = MemoryPackContentType;
            await HttpContext.Response.Body.WriteAsync(bytes, ct);
            return;
        }

        await Send.OkAsync(Response, ct);
    }

    private bool IsMemoryPackRequested()
    {
        var accept = HttpContext.Request.Headers.Accept.ToString();
        return accept.Contains(MemoryPackContentType, StringComparison.OrdinalIgnoreCase);
    }
}
