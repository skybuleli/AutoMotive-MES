using System.Collections.Concurrent;

namespace MesAdmin.Application.Tests.Infrastructure;

/// <summary>
/// TX.1 — MessagePipe 测试基础设施。
/// 提供内存中的 TestPublisher/TestSubscriber 用于验证消息发布-订阅逻辑，
/// 无需真实 MessagePipe DI 容器。支持同步和异步消息。
///
/// 使用方式：
/// <code>
/// var publisher = new TestPublisher&lt;OrderStatusChanged&gt;();
/// publisher.SetHandler((msg, ct) => { captured.Add(msg); return ValueTask.CompletedTask; });
///
/// await publisher.PublishAsync(new OrderStatusChanged(...));
/// Assert.Single(publisher.Published);
/// </code>
/// </summary>

/// <summary>
/// 内存测试消息发布器 — 捕获发布的消息同时转发给订阅者。
/// 不依赖 MessagePipe DI，适合单元测试。
/// </summary>
public sealed class TestPublisher<TMessage>
{
    private readonly List<TMessage> _published = [];
    private Func<TMessage, CancellationToken, ValueTask>? _handler;

    /// <summary>已发布的消息列表（可供断言验证）。</summary>
    public IReadOnlyList<TMessage> Published => _published.AsReadOnly();

    /// <summary>注册消息处理器（由 TestSubscriber 调用或测试直接注入）。</summary>
    public void SetHandler(Func<TMessage, CancellationToken, ValueTask> handler)
    {
        _handler = handler;
    }

    /// <summary>同步发布一条消息。</summary>
    public void Publish(TMessage message)
    {
        _published.Add(message);
        _handler?.Invoke(message, CancellationToken.None);
    }

    /// <summary>异步发布一条消息。</summary>
    public ValueTask PublishAsync(TMessage message, CancellationToken ct = default)
    {
        _published.Add(message);
        return _handler?.Invoke(message, ct) ?? ValueTask.CompletedTask;
    }

    /// <summary>清除已发布消息记录。</summary>
    public void Clear() => _published.Clear();
}

/// <summary>
/// 内存测试消息订阅器 — 捕获收到的消息，支持过滤和断言。
/// </summary>
public sealed class TestSubscriber<TMessage> : IDisposable
{
    private readonly ConcurrentBag<TMessage> _received = [];
    private readonly List<IDisposable> _subscriptions = [];
    private Func<TMessage, CancellationToken, ValueTask>? _handler;

    /// <summary>收到的消息列表。</summary>
    public IReadOnlyList<TMessage> Received => _received.ToArray();

    /// <summary>收到的消息数量。</summary>
    public int Count => _received.Count;

    /// <summary>
    /// 注册订阅处理器。所有接收的消息都会通过此处理器。
    /// 返回 IDisposable 用于取消订阅。
    /// </summary>
    public IDisposable Subscribe(Action<TMessage> onMessage)
    {
        _handler = (msg, _) =>
        {
            _received.Add(msg);
            onMessage(msg);
            return ValueTask.CompletedTask;
        };
        var disposable = new TestDisposable(() => _handler = null);
        _subscriptions.Add(disposable);
        return disposable;
    }

    /// <summary>等待直到收到 N 条消息（轮询最多 timeoutMs 毫秒）。</summary>
    public async Task<List<TMessage>> WaitForCountAsync(int count, int timeoutMs = 5000)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_received.Count >= count)
                return Received.ToList();
            await Task.Delay(50);
        }
        return Received.ToList();
    }

    /// <summary>清除接收记录。</summary>
    public void Clear()
    {
        while (_received.TryTake(out _)) { }
    }

    public void Dispose()
    {
        foreach (var sub in _subscriptions)
            sub.Dispose();
        _subscriptions.Clear();
    }
}

/// <summary>
/// 桥接 MessagePipe 的 IAsyncPublisher/IAsyncSubscriber 到 TestPublisher/TestSubscriber。
/// 用于测试那些直接注入 IAsyncPublisher&lt;T&gt; 的服务。
/// </summary>
public sealed class MessagePipeTestBridge
{
    private readonly ConcurrentDictionary<Type, object> _publishers = new();
    private readonly ConcurrentDictionary<Type, object> _subscribers = new();

    /// <summary>获取或创建指定消息类型的测试发布器。</summary>
    public TestPublisher<T> GetPublisher<T>()
    {
        return (TestPublisher<T>)_publishers.GetOrAdd(typeof(T), _ => new TestPublisher<T>());
    }

    /// <summary>获取或创建指定消息类型的测试订阅器。</summary>
    public TestSubscriber<T> GetSubscriber<T>()
    {
        return (TestSubscriber<T>)_subscribers.GetOrAdd(typeof(T), _ => new TestSubscriber<T>());
    }
}

/// <summary>空的 IDisposable 实现（用于模拟取消订阅）。</summary>
internal sealed class TestDisposable : IDisposable
{
    private readonly Action _onDispose;
    public TestDisposable(Action onDispose) => _onDispose = onDispose;
    public void Dispose() => _onDispose();
}
