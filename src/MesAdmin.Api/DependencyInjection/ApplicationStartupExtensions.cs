using FastEndpoints;
using FastEndpoints.Swagger;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Infrastructure;
using MesAdmin.Infrastructure.Data;
using MesAdmin.Infrastructure.Hubs;
using Microsoft.EntityFrameworkCore;

namespace MesAdmin.Api.DependencyInjection;

/// <summary>
/// Api/Program.cs 启动阶段扩展方法：迁移/种子、健康检查、中间件管道、SignalR Hub 映射。
/// </summary>
public static class ApplicationStartupExtensions
{
    /// <summary>
    /// 启动时自动应用 EF Core Migration 并执行幂等种子数据。
    /// </summary>
    public static async Task UseMesMigrationsAndSeedAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        await db.Database.MigrateAsync();

        // 仅首次启动时写入，已存在数据则跳过（幂等）
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        await MesDataSeeder.SeedAsync(app.Services, logger);
    }

    /// <summary>
    /// 注册健康检查端点（用于 Docker HEALTHCHECK）。
    /// </summary>
    public static WebApplication UseMesHealthChecks(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "healthy",
            service = "MesAdmin.Api",
            timestamp = DateTime.UtcNow
        }))
            .ExcludeFromDescription();

        return app;
    }

    /// <summary>
    /// 注册认证、授权、FastEndpoints 路由及 Swagger（开发环境）。
    /// </summary>
    public static WebApplication UseMesMiddlewarePipeline(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseFastEndpoints(config =>
        {
            config.Errors.UseProblemDetails();
            config.Endpoints.Configurator = ep =>
            {
                ep.PostProcessor<GlobalExceptionPostProcessor>(Order.After);
            };
        });

        if (app.Environment.IsDevelopment())
            app.UseSwaggerGen();

        return app;
    }

    /// <summary>
    /// 注册 SignalR Hub 端点（MemoryPack 二进制协议）。
    /// </summary>
    public static WebApplication UseMesSignalRHubs(this WebApplication app)
    {
        app.MapHub<DashboardHub>("/hubs/dashboard");
        app.MapHub<AndonHub>("/hubs/andon");
        return app;
    }
}
