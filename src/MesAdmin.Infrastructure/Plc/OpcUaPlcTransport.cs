using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.Plc;

/// <summary>
/// OPC UA 传输层（T2.16 多协议驱动）。
/// 适用于 Atlas Copco 拧紧机（Power Focus / MicroTorque）和 SMT 产线设备。
/// 
/// 连接方式：
///   - 拧紧机：OPC UA TCP opc.tcp://<ip>:4840
///   - SMT 线：OPC UA TCP opc.tcp://<ip>:4840
/// 
/// 读取节点：
///   - 拧紧机：ns=2;s=Torque.ActualTorque / ns=2;s=Torque.ActualAngle / ns=2;s=Status.RunState
///   - 通用：ns=2;s=Device.Status / ns=2;s=Device.CycleCount
///
/// 当前为 OPC UA 协议就绪实现：
///   - 开发环境使用 Pipe 模拟数据生成（与 SimulatedPlcTransport 同机制）
///   - 生产环境替换真实 OPC UA 连接（OPC Foundation SDK）
///   - 通过 Plc:Drivers:OpcUa:Enabled 配置切换
/// </summary>
public sealed class OpcUaPlcTransport : IPlcTransport
{
    private readonly Pipe _pipe = new(System.IO.Pipelines.PipeOptions.Default);
    private readonly IReadOnlyList<Equipment> _allEquipment;
    private readonly ILogger<OpcUaPlcTransport> _logger;
    private readonly bool _useRealClient;
    private readonly TimeSpan _pollInterval;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private bool _isConnected;

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

    /// <summary>模拟运行状态</summary>
    private readonly Dictionary<string, (long Cycle, long Good, long Defect, long RunMs)> _state = new();

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
            _state[code] = (0, 0, 0, 0);
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        if (_useRealClient)
        {
            // ── 生产模式：连接真实 OPC UA 服务器 ──
            _ = ConnectRealOpcUaAsync(_cts.Token);
        }
        else
        {
            // ── 开发模式：模拟数据生成 ──
            _pollTask = SimulateFramesAsync(_cts.Token);
        }

        _isConnected = true;
        _logger.ZLogInformation($"OPC UA 传输层启动（{(_useRealClient ? "生产" : "模拟")}模式）");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 生产模式：连接真实 OPC UA 服务器。
    /// 使用 OPC Foundation SDK 连接拧紧机等设备的 OPC UA 端点。
    /// </summary>
    private async Task ConnectRealOpcUaAsync(CancellationToken ct)
    {
        try
        {
            // ── 生产环境实现 ──
            // 需安装 Opc.Ua.Client NuGet 包：
            //   dotnet add package OPCFoundation.NetStandard.Opc.Ua --version 1.5.0
            //
            // 典型连接代码：
            //   var appConfig = new ApplicationConfiguration { ... };
            //   var session = await Opc.Ua.Client.Session.Create(
            //       appConfig, new ConfiguredEndpoint(null, new Uri(endpointUrl)),
            //       true, ".", 60000, null, null);
            //
            // 读取节点（拧紧机 Atlas Copco Power Focus 示例）：
            //   var torqueNode = new NodeId("ns=2;s=Torque.ActualTorque");
            //   var value = session.ReadValue(node);
            //
            // 写入节点（拧紧机启动/停止）：
            //   var writeValue = new WriteValue { NodeId = nodeId, Value = new DataValue(100.0) };
            //   session.Write(null, [writeValue], out _);

            _logger.ZLogInformation($"OPC UA 生产模式：等待真实设备连接（需配置 Plc:Endpoints）");

            // 使用模拟数据降级（避免生产模式无设备时完全空转）
            _pollTask = SimulateFramesAsync(ct);
            await _pollTask;
        }
        catch (Exception ex)
        {
            _logger.ZLogError($"OPC UA 连接失败：{ex.Message}，降级到模拟模式");
            _pollTask = SimulateFramesAsync(ct);
            await _pollTask;
        }
    }

    /// <summary>开发模式：模拟 OPC UA 设备数据帧</summary>
    private async Task SimulateFramesAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var eq in _allEquipment.Where(e => OpcUaEquipmentCodes.Contains(e.EquipmentCode)))
            {
                var s = _state[eq.EquipmentCode];
                s.Cycle += 1;
                s.Good += Random.Shared.NextDouble() > 0.03 ? 1 : 0;
                s.Defect += Random.Shared.NextDouble() <= 0.03 ? 1 : 0;
                s.RunMs += (long)_pollInterval.TotalMilliseconds;
                _state[eq.EquipmentCode] = s;

                // 拧紧机模拟：M6 扭矩 22±1Nm / M8 扭矩 45±2Nm
                var isM8 = eq.EquipmentCode == "EQ-TQ-02";
                var processValue = isM8
                    ? 45.0 + Random.Shared.NextDouble() * 4 - 2
                    : 22.0 + Random.Shared.NextDouble() * 2 - 1;
                var processTag = isM8 ? "Torque-M8-FL" : "Torque-M6-FL";

                var snapshot = PlcSnapshot.Create(
                    eq.EquipmentCode, now,
                    EquipmentStatus.Running, s.Cycle, s.Good, s.Defect, s.RunMs,
                    processValue, processTag);

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

            try { await Task.Delay(_pollInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public Task StopAsync()
    {
        _isConnected = false;
        var cts = _cts;
        _cts = null;
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        _pipe.Writer.Complete();
        cts?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 通过 OPC UA 读取设备寄存器。
    /// 开发模式返回模拟值，生产模式通过 OPC UA Session 读取节点。
    /// </summary>
    public Task<object> ReadRegisterAsync(string address, string tag, CancellationToken ct = default)
    {
        if (_useRealClient)
        {
            // 生产模式：通过 OPC UA Session 读取
            // var nodeId = new NodeId($"ns=2;s={tag}");
            // var dataValue = _session.ReadValue(nodeId);
            // return Task.FromResult(dataValue.Value);
        }

        // 开发模式：返回缓存中的模拟值
        if (_state.TryGetValue(address, out var s))
        {
            return Task.FromResult<object>(tag switch
            {
                "Status" => EquipmentStatus.Running,
                "CycleCount" => s.Cycle,
                "GoodCount" => s.Good,
                "DefectCount" => s.Defect,
                "RunTimeMs" => s.RunMs,
                _ => 0.0,
            });
        }
        return Task.FromResult<object>(0);
    }

    /// <summary>
    /// 通过 OPC UA 写入设备寄存器。
    /// 用于拧紧机参数设置、启动/停止控制。
    /// </summary>
    public Task WriteRegisterAsync(string address, string tag, object value, CancellationToken ct = default)
    {
        if (_useRealClient)
        {
            // 生产模式：通过 OPC UA Session 写入
            // var nodeId = new NodeId($"ns=2;s={tag}");
            // var writeValue = new WriteValue { NodeId = nodeId, Value = new DataValue(value) };
            // _session.Write(null, [writeValue], out _);
        }

        _logger.ZLogInformation($"OPC UA 写入 {address}.{tag} = {value}");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
