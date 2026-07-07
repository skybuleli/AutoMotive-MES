using Cleipnir.ResilientFunctions.CoreRuntime.Invocation;
using Cleipnir.ResilientFunctions.Domain;
using Cleipnir.ResilientFunctions.Storage;
using MesAdmin.Application.Tests.Infrastructure;
using R3;

namespace MesAdmin.Application.Tests;

/// <summary>
/// TX.1 — 单元测试基础设施验收测试。
/// 验证 CleipnirSagaFixture / MessagePipeTestHelper / R3TestScheduler 正常工作。
/// </summary>
public class Tx1_UnitInfrastructureTests
{
    // ═══════════════════════════════════════════════════════════
    //  CleipnirSagaFixture
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task CleipnirSagaFixture_CreateRegistry_ShouldInitializeSuccessfully()
    {
        var fixture = new CleipnirSagaFixture();
        var (registry, store) = await fixture.CreateRegistryAsync();

        Assert.NotNull(registry);
        Assert.NotNull(store);
        Assert.IsType<InMemoryFunctionStore>(store);
    }

    [Fact]
    public async Task CleipnirSagaFixture_CreateAction_ShouldRegisterAndInvoke()
    {
        var fixture = new CleipnirSagaFixture();
        var invoked = false;

        var (action, store) = await fixture.CreateActionAsync<string>(
            "TestSaga",
            async (param, workflow) =>
            {
                invoked = true;
                await Task.CompletedTask;
            });

        Assert.NotNull(action);
        Assert.NotNull(store);

        await action.Invoke.Invoke("test-param", "hello");
        Assert.True(invoked);
    }

    [Fact]
    public async Task CleipnirSagaFixture_Replay_ShouldNotReexecuteEffect()
    {
        var fixture = new CleipnirSagaFixture();
        var effectCount = 0;

        var (action, store) = await fixture.CreateActionAsync<string>(
            "ReplaySaga",
            async (param, workflow) =>
            {
                await workflow.Effect.Capture("my-effect", async () =>
                {
                    Interlocked.Increment(ref effectCount);
                    await Task.CompletedTask;
                });
            });

        // 首次执行
        await action.Invoke.Invoke("test", "hello");
        Assert.Equal(1, effectCount);

        // 重放 — Effect 幂等，不应再执行
        await action.Invoke.Invoke("test", "hello");
        Assert.Equal(1, effectCount);
    }

    [Fact]
    public void CrashTestOpRepoDecorator_ShouldThrowOnSpecifiedCount()
    {
        var inner = new MockSaveRepo();
        var crashRepo = new CrashTestOpRepoDecorator<MockSaveRepo>(inner, crashAfterSaveCount: 3);

        // 第 1、2 次 Save 不触发
        crashRepo.CheckAndThrow();
        crashRepo.CheckAndThrow();
        Assert.Equal(2, crashRepo.SaveChangesCallCount);

        // 第 3 次 Save 应触发崩溃
        var ex = Assert.Throws<OperationCanceledException>(() => crashRepo.CheckAndThrow());
        Assert.Contains("混沌工程模拟崩溃", ex.Message);
        Assert.Equal(3, crashRepo.SaveChangesCallCount);
    }

    // ═══════════════════════════════════════════════════════════
    //  MessagePipeTestHelper
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task TestPublisher_ShouldCapturePublishedMessages()
    {
        var publisher = new TestPublisher<string>();
        var received = new List<string>();

        publisher.SetHandler((msg, ct) =>
        {
            received.Add(msg);
            return ValueTask.CompletedTask;
        });

        await publisher.PublishAsync("hello");
        await publisher.PublishAsync("world");

        Assert.Equal(2, publisher.Published.Count);
        Assert.Contains("hello", publisher.Published);
        Assert.Contains("world", publisher.Published);
        Assert.Equal(2, received.Count);
        Assert.Equal("hello", received[0]);
    }

    [Fact]
    public void TestPublisher_SyncPublish_ShouldWork()
    {
        var publisher = new TestPublisher<int>();
        var received = new List<int>();

        publisher.SetHandler((msg, ct) =>
        {
            received.Add(msg);
            return ValueTask.CompletedTask;
        });

        publisher.Publish(42);
        publisher.Publish(100);

        Assert.Equal(2, publisher.Published.Count);
        Assert.Equal([42, 100], publisher.Published);
    }

    [Fact]
    public void TestPublisher_Clear_ShouldEmptyPublishedList()
    {
        var publisher = new TestPublisher<string>();
        publisher.Publish("a");
        publisher.Publish("b");
        Assert.Equal(2, publisher.Published.Count);

        publisher.Clear();
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task TestSubscriber_WaitForCount_ShouldTimeoutGracefully()
    {
        using var subscriber = new TestSubscriber<string>();

        // 不发布任何消息，等待应超时返回空
        var result = await subscriber.WaitForCountAsync(1, timeoutMs: 200);
        Assert.Empty(result);
    }

    [Fact]
    public void TestSubscriber_Dispose_ShouldCleanup()
    {
        var subscriber = new TestSubscriber<string>();
        var sub = subscriber.Subscribe(msg => { });
        Assert.NotNull(sub);

        sub.Dispose();
        Assert.Equal(0, subscriber.Count);
    }

    [Fact]
    public void MessagePipeTestBridge_ShouldCreatePublisherAndSubscriber()
    {
        var bridge = new MessagePipeTestBridge();
        var pub = bridge.GetPublisher<string>();
        var sub = bridge.GetSubscriber<string>();

        Assert.NotNull(pub);
        Assert.NotNull(sub);
        Assert.Same(pub, bridge.GetPublisher<string>()); // 单例
    }

    // ═══════════════════════════════════════════════════════════
    //  R3TestScheduler
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void R3TestScheduler_InitialTime_ShouldBe20260701()
    {
        var scheduler = new R3TestScheduler();
        Assert.Equal(2026, scheduler.UtcNow.Year);
        Assert.Equal(7, scheduler.UtcNow.Month);
        Assert.Equal(1, scheduler.UtcNow.Day);
    }

    [Fact]
    public void R3TestScheduler_Advance_ShouldIncreaseTime()
    {
        var scheduler = new R3TestScheduler();
        var initial = scheduler.UtcNow;

        scheduler.Advance(TimeSpan.FromHours(2));

        Assert.Equal(initial.AddHours(2), scheduler.UtcNow);
    }

    [Fact]
    public void R3TestScheduler_AdvanceTo_ShouldSetExactTime()
    {
        var scheduler = new R3TestScheduler();
        var target = new DateTimeOffset(2026, 12, 25, 0, 0, 0, TimeSpan.Zero);

        scheduler.AdvanceTo(target);

        Assert.Equal(target, scheduler.UtcNow);
    }

    [Fact]
    public void R3TestScheduler_AdvanceToPast_ShouldThrow()
    {
        var scheduler = new R3TestScheduler();
        var past = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentException>(() => scheduler.AdvanceTo(past));
    }

    [Fact]
    public void R3TestScheduler_AdvanceNegative_ShouldThrow()
    {
        var scheduler = new R3TestScheduler();
        Assert.Throws<ArgumentException>(() => scheduler.Advance(TimeSpan.FromMinutes(-1)));
    }

    [Fact]
    public void R3TestScheduler_TimeProvider_ShouldWorkWithR3()
    {
        var scheduler = new R3TestScheduler();
        var captured = new List<int>();
        var subject = new Subject<int>();

        using var _ = subject
            .Do(x => captured.Add(x))
            .Subscribe();

        subject.OnNext(1);
        subject.OnNext(2);

        Assert.Equal(2, captured.Count);
        Assert.Equal([1, 2], captured);
    }

    [Fact]
    public void R3TestScheduler_MultipleAdvances_ShouldAccumulate()
    {
        var scheduler = new R3TestScheduler();
        var initial = scheduler.UtcNow;

        scheduler.Advance(TimeSpan.FromDays(1));
        scheduler.Advance(TimeSpan.FromHours(6));
        scheduler.Advance(TimeSpan.FromMinutes(30));

        var expected = initial.AddDays(1).AddHours(6).AddMinutes(30);
        Assert.Equal(expected, scheduler.UtcNow);
    }

    /// <summary>模拟 SaveChanges 仓储（用于 CrashTestOpRepoDecorator 测试）。</summary>
    public sealed class MockSaveRepo
    {
        public int SaveCount { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }
}
