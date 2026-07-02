using FastEndpoints;
using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Text;

namespace MesAdmin.Api.Infrastructure;

/// <summary>
/// SAP Webhook HMAC-SHA256 签名验证预处理器。
/// 客户端在请求头 X-Sap-Signature 中提供 HMAC-SHA256(请求体, 共享密钥)。
/// </summary>
/// <typeparam name="TRequest">请求 DTO</typeparam>
public class SapSignaturePreProcessor<TRequest> : IPreProcessor<TRequest>
{
    public async Task PreProcessAsync(IPreProcessorContext<TRequest> ctx, CancellationToken ct)
    {
        var httpContext = ctx.HttpContext;
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var sharedSecret = config["Sap:WebhookSecret"];

        // 开发环境可跳过签名验证
        if (string.IsNullOrWhiteSpace(sharedSecret))
        {
            var env = httpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
            if (env.IsDevelopment())
                return; // 开发环境无密钥时跳过

            await SendError(httpContext, "SAP Webhook 密钥未配置");
            return;
        }

        var signatureHeader = httpContext.Request.Headers["X-Sap-Signature"].ToString();
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            await SendError(httpContext, "缺少 X-Sap-Signature 签名头");
            return;
        }

        // 读取请求体计算 HMAC
        httpContext.Request.EnableBuffering();
        httpContext.Request.Body.Position = 0;
        using var reader = new StreamReader(httpContext.Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(ct);
        httpContext.Request.Body.Position = 0;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(sharedSecret));
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        var computedSignature = Convert.ToHexString(computedHash).ToLowerInvariant();

        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSignature),
            Encoding.UTF8.GetBytes(signatureHeader.ToLowerInvariant())))
        {
            await SendError(httpContext, "SAP Webhook 签名验证失败");
        }
    }

    private static async Task SendError(HttpContext ctx, string message)
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync($"{{\"error\":\"{message}\"}}");
    }
}
