using Cleipnir.ResilientFunctions;
using Cleipnir.ResilientFunctions.CoreRuntime;
using Cleipnir.ResilientFunctions.Domain;
using Cleipnir.ResilientFunctions.PostgreSQL;
using Cleipnir.ResilientFunctions.Storage;
using MesAdmin.Application.Sagas;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MesAdmin.Infrastructure.Workflows;

/// <summary>
/// Cleipnir ResilientFunctions DI 注册扩展。
/// Store 与 Registry 均注册为 Singleton，确保崩溃恢复和后台轮询生命周期正确。
/// Store 初始化通过 IHostedService 异步完成，Registry 延迟创建避免启动时阻塞。
/// </summary>
public static class CleipnirSetup
{
    public static IServiceCollection AddCleipnirSagas(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("缺少 PostgreSQL 连接字符串");

        // ── IFunctionStore 单例（延迟初始化，由 HostedService 触发）──
        services.AddSingleton<IFunctionStore>(_ => new PostgreSqlFunctionStore(connectionString));

        // ── Cleipnir Saga Registry 单例：延迟创建 FunctionsRegistry ──
        services.AddSingleton<CleipnirSagaRegistry>(sp =>
            new CleipnirSagaRegistry(sp.GetRequiredService<IFunctionStore>(), sp));

        // ── 启动时异步初始化 Store 表结构（避免 Singleton 工厂中同步阻塞）──
        services.AddHostedService<CleipnirInitializationHostedService>();

        return services;
    }
}

/// <summary>
/// 应用启动时异步初始化 Cleipnir Store 表结构。
/// </summary>
public sealed class CleipnirInitializationHostedService(IFunctionStore store) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
        => await store.Initialize();

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Cleipnir FunctionsRegistry 包装器。
/// FunctionsRegistry 延迟创建，确保 Store 已初始化完成后再启动后台 watchdog。
/// Action 执行时内部创建 Service Scope 解析 Scoped 依赖（Repository、PLC 等）。
/// 消息类型为 Ulid orderId —— Saga 内部从 DB 重新读取最新状态，不依赖传入聚合。
/// </summary>
public sealed class CleipnirSagaRegistry(IFunctionStore store, IServiceProvider serviceProvider)
{
    private readonly Lazy<ActionRegistration<Ulid>> _productionOrderAction = new(
        () => CreateAction(store, serviceProvider),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static ActionRegistration<Ulid> CreateAction(IFunctionStore store, IServiceProvider serviceProvider)
    {
        var registry = new FunctionsRegistry(store, new Settings());
        return registry.RegisterAction<Ulid>(
            "ProductionOrderSaga",
            async (orderId, workflow) =>
            {
                await using var scope = serviceProvider.CreateAsyncScope();
                var saga = scope.ServiceProvider.GetRequiredService<ProductionOrderSaga>();
                await saga.Execute(orderId, workflow);
            });
    }

    /// <summary>调用 ProductionOrderSaga，触发指定工单的 31 工序编排。</summary>
    public Task InvokeProductionOrderSagaAsync(string instanceId, Ulid orderId)
        => _productionOrderAction.Value.Invoke.Invoke(instanceId, orderId);
}
