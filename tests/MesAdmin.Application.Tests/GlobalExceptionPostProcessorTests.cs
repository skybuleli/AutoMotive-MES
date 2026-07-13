using System.Net;
using System.Runtime.ExceptionServices;
using FastEndpoints;
using FluentValidation.Results;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace MesAdmin.Application.Tests;

/// <summary>
/// GlobalExceptionPostProcessor 单元测试。
/// 验证领域异常到 HTTP 状态码的映射，特别是 OrderNotFoundException → 404。
/// </summary>
public class GlobalExceptionPostProcessorTests
{
    private readonly GlobalExceptionPostProcessor _processor;

    public GlobalExceptionPostProcessorTests()
    {
        var logger = NullLogger<GlobalExceptionPostProcessor>.Instance;
        _processor = new GlobalExceptionPostProcessor(logger);
    }

    [Fact]
    public async Task PostProcessAsync_NoException_ShouldNotModifyResponse()
    {
        var httpContext = CreateHttpContext();
        var ctx = new TestPostProcessorContext(httpContext, exception: null);

        await _processor.PostProcessAsync(ctx, CancellationToken.None);

        Assert.Equal(200, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task PostProcessAsync_OrderNotFoundException_ShouldReturn404()
    {
        var httpContext = CreateHttpContext();
        var orderId = Ulid.NewUlid();
        var ex = new OrderNotFoundException(orderId);
        var ctx = new TestPostProcessorContext(httpContext, ex);

        await _processor.PostProcessAsync(ctx, CancellationToken.None);

        Assert.Equal((int)HttpStatusCode.NotFound, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task PostProcessAsync_OrderNotFoundException_ShouldReturnProblemDetails()
    {
        var httpContext = CreateHttpContext();
        var orderId = Ulid.NewUlid();
        var ex = new OrderNotFoundException(orderId);
        var ctx = new TestPostProcessorContext(httpContext, ex);

        await _processor.PostProcessAsync(ctx, CancellationToken.None);

        Assert.True(httpContext.Response.ContentType?.StartsWith("application/json") == true);

        var body = await ReadResponseBody(httpContext);
        Assert.Contains("404", body);
        Assert.Contains(orderId.ToString(), body);
    }

    [Fact]
    public async Task PostProcessAsync_OrderNotFoundException_ShouldMarkExceptionAsHandled()
    {
        var httpContext = CreateHttpContext();
        var ex = new OrderNotFoundException(Ulid.NewUlid());
        var ctx = new TestPostProcessorContext(httpContext, ex);

        await _processor.PostProcessAsync(ctx, CancellationToken.None);

        Assert.True(ctx.ExceptionHandled);
    }

    [Fact]
    public async Task PostProcessAsync_KeyNotFoundException_ShouldReturn404()
    {
        var httpContext = CreateHttpContext();
        var ex = new KeyNotFoundException("资源未找到");
        var ctx = new TestPostProcessorContext(httpContext, ex);

        await _processor.PostProcessAsync(ctx, CancellationToken.None);

        Assert.Equal((int)HttpStatusCode.NotFound, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task PostProcessAsync_ArgumentException_ShouldReturn400()
    {
        var httpContext = CreateHttpContext();
        var ex = new ArgumentException("无效参数");
        var ctx = new TestPostProcessorContext(httpContext, ex);

        await _processor.PostProcessAsync(ctx, CancellationToken.None);

        Assert.Equal((int)HttpStatusCode.BadRequest, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task PostProcessAsync_InvalidOperationException_ShouldReturn409()
    {
        var httpContext = CreateHttpContext();
        var ex = new InvalidOperationException("状态冲突");
        var ctx = new TestPostProcessorContext(httpContext, ex);

        await _processor.PostProcessAsync(ctx, CancellationToken.None);

        Assert.Equal((int)HttpStatusCode.Conflict, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task PostProcessAsync_UnknownException_ShouldReturn500()
    {
        var httpContext = CreateHttpContext();
        var ex = new TimeoutException("未知错误");
        var ctx = new TestPostProcessorContext(httpContext, ex);

        await _processor.PostProcessAsync(ctx, CancellationToken.None);

        Assert.Equal((int)HttpStatusCode.InternalServerError, httpContext.Response.StatusCode);
    }

    // ── ResponseAlreadyStarted 测试：由于 DefaultHttpContext 不支持在单元测试中设置 HasStarted，
    //    该分支的正确性由集成测试覆盖。ProcessPostAsync 中的早期返回逻辑（响应已开始时
    //    重新抛出异常）属于框架防御性编程，单元测试不做覆盖。
    // ──
    public async Task PostProcessAsync_500Response_ShouldNotLeakInternalDetails()
    {
        var httpContext = CreateHttpContext();
        var ex = new TimeoutException("不应该泄漏的内部错误细节");
        var ctx = new TestPostProcessorContext(httpContext, ex);

        await _processor.PostProcessAsync(ctx, CancellationToken.None);

        var body = await ReadResponseBody(httpContext);
        Assert.Contains("服务器内部错误", body);
        Assert.DoesNotContain("不应该泄漏", body);
    }

    // ═══════════════════════════════════════════════════════════
    //  Test Helpers
    // ═══════════════════════════════════════════════════════════

    private static DefaultHttpContext CreateHttpContext()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        return httpContext;
    }

    private static async Task<string> ReadResponseBody(DefaultHttpContext httpContext)
    {
        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(httpContext.Response.Body);
        return await reader.ReadToEndAsync();
    }

    /// <summary>
    /// IPostProcessorContext 测试替身。
    /// 仅实现 PostProcessAsync 中使用的成员。
    /// </summary>
    private sealed class TestPostProcessorContext : IPostProcessorContext
    {
        private readonly HttpContext _httpContext;
        private readonly bool _hasException;

        public TestPostProcessorContext(HttpContext httpContext, Exception? exception)
        {
            _httpContext = httpContext;
            _hasException = exception is not null;
            if (exception is not null)
            {
                ExceptionDispatchInfo = ExceptionDispatchInfo.Capture(exception);
            }
        }

    public bool HasExceptionOccurred => _hasException;
        public ExceptionDispatchInfo? ExceptionDispatchInfo { get; }
        public HttpContext HttpContext => _httpContext;
        public bool ExceptionHandled { get; private set; }

        public void MarkExceptionAsHandled() => ExceptionHandled = true;

        // ── 未使用成员（仅实现接口，默认值）──
        public object? Request { get; set; }
        public object? Response { get; set; }
        public IReadOnlyCollection<IEndpointFilter> Filters => Array.Empty<IEndpointFilter>();
        public IReadOnlyCollection<object> PreProcessors => Array.Empty<object>();
        public IReadOnlyCollection<object> PostProcessors => Array.Empty<object>();
        public CancellationToken CancellationToken => CancellationToken.None;
        public IEndpoint? Endpoint { get; set; }
        public IReadOnlyCollection<ValidationFailure> ValidationFailures => Array.Empty<ValidationFailure>();
        public bool IsValidationFailed => false;
    }
}
