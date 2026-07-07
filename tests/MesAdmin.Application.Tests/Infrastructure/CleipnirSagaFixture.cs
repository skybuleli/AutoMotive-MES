using Cleipnir.ResilientFunctions;
using Cleipnir.ResilientFunctions.CoreRuntime.Invocation;
using Cleipnir.ResilientFunctions.Storage;

namespace MesAdmin.Application.Tests.Infrastructure;

/// <summary>
/// TX.1 — Cleipnir Saga 单元测试基础设施。
/// 提供 InMemoryFunctionStore 工厂 + FunctionsRegistry 创建 + Saga 注册辅助方法，
/// 让 Saga 测试类专注业务逻辑验证，不重复初始化 Cleipnir 基础设施。
///
/// 使用方式：
/// <code>
/// public class MySagaTests
/// {
///     private readonly CleipnirSagaFixture _saga = new();
///
///     [Fact]
///     public async Task MyTest()
///     {
///         var (action, store) = await _saga.CreateAction&lt;Ulid&gt;("MySaga", saga.Execute);
///         await action.Invoke.Invoke(order.Id.ToString(), order.Id);
///     }
/// }
/// </code>
/// </summary>
public sealed class CleipnirSagaFixture
{
    /// <summary>创建一个 InMemoryFunctionStore 并返回 FunctionsRegistry 和 Store 引用。</summary>
    public async Task<(FunctionsRegistry Registry, InMemoryFunctionStore Store)> CreateRegistryAsync()
    {
        var store = new InMemoryFunctionStore();
        await store.Initialize();
        var registry = new FunctionsRegistry(store);
        return (registry, store);
    }

    /// <summary>
    /// 注册一个 Action&lt;TParam&gt; Saga 并返回 ActionRegistration 和 Store。
    /// TParam 通常为 Ulid（工单 ID）。
    /// </summary>
    public async Task<(ActionRegistration<TParam> Action, InMemoryFunctionStore Store)> CreateActionAsync<TParam>(
        string functionTypeId,
        Func<TParam, Workflow, Task> sagaExecute,
        InMemoryFunctionStore? existingStore = null)
    {
        var store = existingStore ?? new InMemoryFunctionStore();
        await store.Initialize();
        var registry = new FunctionsRegistry(store);
        var action = registry.RegisterAction<TParam>(functionTypeId, sagaExecute);
        return (action, store);
    }
}

/// <summary>
/// 混沌工程测试辅助：在指定次数的 SaveChangesAsync 后抛出 OperationCanceledException。
/// 用于模拟 Saga 执行过程中随机进程崩溃，验证 Cleipnir Effect 正确恢复。
///
/// ⚠ Cleipnir 将 OperationCanceledException 包装为 FatalWorkflowException，
/// 崩溃后工作流实例无法重试。验证中间状态即可。
/// </summary>
public sealed class CrashTestOpRepoDecorator<TRepo> where TRepo : class
{
    private readonly TRepo _inner;
    private readonly int _crashAfterSaveCount;
    private int _saveCount;

    public CrashTestOpRepoDecorator(TRepo inner, int crashAfterSaveCount)
    {
        _inner = inner;
        _crashAfterSaveCount = crashAfterSaveCount;
    }

    public TRepo Inner => _inner;

    /// <summary>
    /// 调用 SaveChangesAsync 时按计数触发崩溃。
    /// 在调用内层 SaveChangesAsync 前检查计数器。
    /// </summary>
    public int SaveChangesCallCount => _saveCount;

    /// <summary>
    /// 检查是否应触发崩溃。如果已到达崩溃点，抛出 OperationCanceledException。
    /// 应在每层仓储的 SaveChangesAsync 前调用。
    /// </summary>
    public void CheckAndThrow()
    {
        _saveCount++;
        if (_saveCount >= _crashAfterSaveCount)
            throw new OperationCanceledException(
                $"混沌工程模拟崩溃：第 {_saveCount} 次 SaveChanges 后终止");
    }

    /// <summary>重置崩溃计数器。</summary>
    public void Reset()
    {
        _saveCount = 0;
    }
}
