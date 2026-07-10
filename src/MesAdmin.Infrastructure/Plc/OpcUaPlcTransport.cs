using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using ZLogger;

namespace MesAdmin.Infrastructure.Plc;

/// <summary>
/// OPC UA 传输层（T2.16 多协议驱动）。
/// 适用于 Atlas Copco 拧紧机（Power Focus / MicroTorque）和 SMT 产线设备。
///
/// 连接方式：
///   - 拧紧机：OPC UA TCP opc.tcp://&lt;ip&gt;:4840（取自 Equipment.PlcAddress）
///   - 终检台：OPC UA TCP opc.tcp://&lt;ip&gt;:4840
///
/// 真实模式（Plc:Drivers:OpcUa:Enabled=true）：
///   使用 OPC Foundation .NET Standard 栈建立 Session，通过 Subscription 订阅设备节点，
///   将节点值映射到 PlcSnapshot 帧写入 Pipe。连接失败按退避重连，不再降级到模拟。
///
/// 开发模式（默认）：
///   通过 Pipe 模拟数据生成（与 SimulatedPlcTransport 同机制）。
/// </summary>
public sealed class OpcUaPlcTransport : IPlcTransport
{
    private readonly Pipe _pipe = new(System.IO.Pipelines.PipeOptions.Default);
    private readonly IReadOnlyList<Equipment> _allEquipment;
    private readonly ILogger<OpcUaPlcTransport> _logger;
    private readonly bool _useRealClient;
    private readonly TimeSpan _pollInterval;
    private ApplicationConfiguration? _appConfig;
    private Session? _session;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private bool _isConnected;

    // 每台设备最新节点值缓存（tag → value），由订阅回调填充，发布循环消费
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, object?>> _values = new();

    // 支持 OPC UA 的设备编码
    private static readonly HashSet<string> OpcUaEquipmentCodes =
    [
        "EQ-TQ-01",  // 螺栓拧紧机 — Atlas Copco Power Focus (OPC UA)
        "EQ-TQ-02",  // 备用拧紧机 — Atlas Copco MicroTorque (OPC UA)
        "EQ-FT-01",  // 功能终检台 — OPC UA enabled tester
    ];

    public IReadOnlySet<string> SupportedEquipmentCodes => OpcUaEquipmentCodes;
    public PipeReader Reader => _pipe.Reader;
    public string TransportName => "OPC-UA";
    public bool IsConnected => _isConnected;

    /// <summary>OPC UA 节点映射（tag → NodeId 字符串）。可被配置覆盖。</summary>
    internal static readonly IReadOnlyDictionary<string, string> DefaultNodeMap = new Dictionary<string, string>
    {
        ["Status"] = "ns=2;s=Device.Status",
        ["CycleCount"] = "ns=2;s=Device.CycleCount",
        ["GoodCount"] = "ns=2;s=Device.GoodCount",
        ["DefectCount"] = "ns=2;s=Device.DefectCount",
        ["RunTimeMs"] = "ns=2;s=Device.RunTimeMs",
        ["ProcessValue"] = "ns=2;s=Device.ProcessValue",
        ["ProcessTag"] = "ns=2;s=Device.ProcessTag",
    };

    public OpcUaPlcTransport(
        IReadOnlyList<Equipment> equipment,
        ILogger<OpcUaPlcTransport> logger,
        bool useRealClient = false,
        int pollIntervalMs = 500)
    {
        _allEquipment = equipment;
        _logger = logger;
        _useRealClient = useRealClient;
        _pollInterval = TimeSpan.FromMilliseconds(pollIntervalMs);
        foreach (var code in OpcUaEquipmentCodes)
            _values[code] = new ConcurrentDictionary<string, object?>();
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        if (_useRealClient)
            _runTask = RunRealAsync(_cts.Token);
        else
            _runTask = SimulateFramesAsync(_cts.Token);

        _isConnected = _useRealClient;
        _logger.ZLogInformation($"OPC UA 传输层启动（{(_useRealClient ? "生产" : "模拟")}模式）");
        return Task.CompletedTask;
    }

    /// <summary>真实模式：为每个不同端点建立 Session + Subscription，并启动发布循环。</summary>
    private async Task RunRealAsync(CancellationToken ct)
    {
        _appConfig = BuildApplicationConfiguration();

        var groups = OpcUaEquipmentCodes
            .Select(code => _allEquipment.FirstOrDefault(e => e.EquipmentCode == code))
            .Where(e => e is not null)
            .GroupBy(e => e!.PlcAddress)
            .ToList();

        var connectTasks = groups.Select(g => ConnectEndpointAsync(g.Key, g.ToList()!, ct));
        var publishTask = PublishLoopAsync(ct);
        await Task.WhenAll(connectTasks.Append(publishTask));
    }

    private async Task ConnectEndpointAsync(string endpointUrl, IReadOnlyList<Equipment> devices, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var endpoint = new ConfiguredEndpoint(null, new EndpointDescription(endpointUrl)
                {
                    EndpointUrl = endpointUrl,
                    Server = new ApplicationDescription { ApplicationUri = endpointUrl, ApplicationType = ApplicationType.Server },
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                }, EndpointConfiguration.Create(_appConfig));

                var session = await Session.Create(
                    _appConfig!, endpoint, false, "AutoMES", 60000, null, null);
                _session ??= session;
                _isConnected = true;
                _logger.ZLogInformation($"OPC UA 已连接 {endpointUrl}（{devices.Count} 台设备）");

                var subscription = new Subscription(session.DefaultSubscription)
                {
                    PublishingInterval = (int)_pollInterval.TotalMilliseconds,
                    KeepAliveCount = 10,
                };

                foreach (var dev in devices)
                {
                    foreach (var (tag, nodeId) in DefaultNodeMap)
                    {
                        var item = new MonitoredItem(subscription.DefaultItem)
                        {
                            StartNodeId = new NodeId(nodeId),
                            AttributeId = Attributes.Value,
                            DisplayName = $"{dev.EquipmentCode}:{tag}",
                            MonitoringMode = MonitoringMode.Reporting,
                            SamplingInterval = (int)_pollInterval.TotalMilliseconds,
                            Handle = (dev.EquipmentCode, tag),
                        };
                        item.Notification += OnMonitoredItemNotification;
                        subscription.AddItem(item);
                    }
                }

                session.AddSubscription(subscription);
                subscription.Create();

                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { }

                subscription.Delete(false);
                session.Close();
                _isConnected = false;
                break;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _isConnected = false;
                _logger.ZLogError($"OPC UA 连接 {endpointUrl} 失败：{ex.Message}（5s 后重连）");
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private void OnMonitoredItemNotification(MonitoredItem item, MonitoredItemNotificationEventArgs e)
    {
        if (item.Handle is not (string code, string tag)) return;
        if (e.NotificationValue is not MonitoredItemNotificationCollection notifications) return;
        foreach (var change in notifications)
        {
            var bag = _values.GetOrAdd(code, _ => new ConcurrentDictionary<string, object?>());
            bag[tag] = change.Value?.Value;
        }
    }

    /// <summary>发布循环：将缓存节点值映射为 PlcSnapshot 帧写入 Pipe。</summary>
    private async Task PublishLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var code in OpcUaEquipmentCodes)
            {
                if (!_values.TryGetValue(code, out var bag) || bag.Count == 0) continue;
                var snapshot = ToSnapshot(code, bag);
                await WriteSnapshotFrameAsync(snapshot, ct);
            }

            try { await Task.Delay(_pollInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// 纯函数：将节点值字典映射为 PlcSnapshot（与协议栈解耦，便于单元测试）。
    /// </summary>
    public static PlcSnapshot ToSnapshot(string equipmentCode, IReadOnlyDictionary<string, object?> values)
    {
        static long ToLong(object? v) => v switch
        {
            null => 0,
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            uint u => u,
            double d => (long)d,
            float f => (long)f,
            _ => Convert.ToInt64(v),
        };
        static double ToDouble(object? v) => v switch
        {
            null => 0,
            double d => d,
            float f => f,
            long l => l,
            int i => i,
            _ => Convert.ToDouble(v),
        };
        static EquipmentStatus ToStatus(object? v) => (EquipmentStatus)(v is null ? 0 : Convert.ToInt32(v));

        return PlcSnapshot.Create(
            equipmentCode,
            DateTimeOffset.UtcNow,
            ToStatus(values.GetValueOrDefault("Status")),
            ToLong(values.GetValueOrDefault("CycleCount")),
            ToLong(values.GetValueOrDefault("GoodCount")),
            ToLong(values.GetValueOrDefault("DefectCount")),
            ToLong(values.GetValueOrDefault("RunTimeMs")),
            ToDouble(values.GetValueOrDefault("ProcessValue")),
            (values.GetValueOrDefault("ProcessTag") as string) ?? "Generic");
    }

    private static ApplicationConfiguration BuildApplicationConfiguration()
    {
        var config = new ApplicationConfiguration
        {
            ApplicationName = "AutoMES-OPC-UA-Client",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = "Directory",
                    StorePath = "OPC Foundation/CertificateStores/MachineDefault",
                    SubjectName = "CN=AutoMES-OPC-UA-Client",
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = "Directory",
                    StorePath = "OPC Foundation/CertificateStores/UA Applications",
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = "Directory",
                    StorePath = "OPC Foundation/CertificateStores/UA Certificate Authorities",
                },
                RejectedCertificateStore = new CertificateTrustList
                {
                    StoreType = "Directory",
                    StorePath = "OPC Foundation/CertificateStores/RejectedCertificates",
                },
                AutoAcceptUntrustedCertificates = true,
                RejectSHA1SignedCertificates = false,
                MinimumCertificateKeySize = 1024,
            },
            TransportConfigurations = new TransportConfigurationCollection(),
            TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
            ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 },
            TraceConfiguration = new TraceConfiguration(),
        };
        config.Validate(ApplicationType.Client);
        return config;
    }

    /// <summary>开发模式：模拟 OPC UA 设备数据帧</summary>
    private async Task SimulateFramesAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var eq in _allEquipment.Where(e => OpcUaEquipmentCodes.Contains(e.EquipmentCode)))
            {
                var isM8 = eq.EquipmentCode == "EQ-TQ-02";
                var processValue = isM8
                    ? 45.0 + Random.Shared.NextDouble() * 4 - 2
                    : 22.0 + Random.Shared.NextDouble() * 2 - 1;
                var processTag = isM8 ? "Torque-M8-FL" : "Torque-M6-FL";

                var snapshot = PlcSnapshot.Create(
                    eq.EquipmentCode, now,
                    EquipmentStatus.Running,
                    Random.Shared.Next(1, 100000),
                    Random.Shared.Next(1, 95000),
                    Random.Shared.Next(0, 5000),
                    Random.Shared.Next(1, 100000) * 500L,
                    processValue, processTag);

                await WriteSnapshotFrameAsync(snapshot, ct);
            }

            try { await Task.Delay(_pollInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task WriteSnapshotFrameAsync(PlcSnapshot snapshot, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(PlcFrameProtocol.FrameLength);
        try
        {
            var written = PlcFrameWriter.Write(buffer, in snapshot);
            var flushResult = await _pipe.Writer.WriteAsync(buffer.AsMemory(0, written), ct);
            if (flushResult.IsCompleted) return;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public Task StopAsync()
    {
        _isConnected = false;
        var cts = _cts;
        _cts = null;
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        _pipe.Writer.Complete();
        try { _session?.Close(); } catch { }
        _session = null;
        cts?.Dispose();
        return Task.CompletedTask;
    }

    public Task<object> ReadRegisterAsync(string address, string tag, CancellationToken ct = default)
    {
        if (_useRealClient && _session is not null && DefaultNodeMap.TryGetValue(tag, out var nodeId))
        {
            var value = _session.ReadValue(new NodeId(nodeId));
            return Task.FromResult(value.Value ?? 0);
        }

        if (_values.TryGetValue(address, out var bag) && bag.TryGetValue(tag, out var v))
            return Task.FromResult(v ?? 0);
        return Task.FromResult<object>(0);
    }

    public Task WriteRegisterAsync(string address, string tag, object value, CancellationToken ct = default)
    {
        if (_useRealClient && _session is not null && DefaultNodeMap.TryGetValue(tag, out var nodeId))
        {
            var writeValue = new WriteValue
            {
                NodeId = new NodeId(nodeId),
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(value)),
            };
            _session.Write(null, [writeValue], out _, out _);
            return Task.CompletedTask;
        }

        _logger.ZLogInformation($"OPC UA 写入 {address}.{tag} = {value}");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
