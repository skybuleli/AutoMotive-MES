using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.Logging;
using S7.Net;
using S7Plc = S7.Net.Plc;
using ZLogger;

namespace MesAdmin.Infrastructure.Plc;

/// <summary>
/// Profinet 传输层（T2.16 多协议驱动）。
/// 适用于压装机和合装工作站（Siemens S7-1200/1500 PLC 控制）。
/// 使用 S7netplus 库经 S7 协议（ISO-COTP over TCP 端口 102）通信。
///
/// 设备映射：
///   - EQ-ASM-01 (合装工作站) — Profinet / S7
///   - EQ-ASM-02 (辅助合装台)  — Profinet / S7
///
/// DB 块映射（DB1）：
///   - DB1.DBW0:   设备状态 (WORD: 0=Idle, 1=Run, 2=Alarm, 3=Offline)
///   - DB1.DBD2:   循环计数 (DINT)
///   - DB1.DBD6:   合格件数 (DINT)
///   - DB1.DBD10:  不良件数 (DINT)
///   - DB1.DBD14:  运行时长毫秒低32位 (DINT)
///   - DB1.DBD18:  运行时长毫秒高32位 (DINT)
///   - DB1.DBD22:  压装力 (REAL, kN)
///   - DB1.DBD26:  压装位移 (REAL, mm)
///
/// 真实模式（Plc:Drivers:Profinet:Enabled=true）：连接 S7 PLC 周期读 DB 块 → 写 PlcSnapshot 帧。
/// 连接失败按退避重连，不降级到模拟。
/// 开发模式（默认）：模拟数据生成。
/// </summary>
public sealed class ProfinetPlcTransport : IPlcTransport
{
    private readonly Pipe _pipe = new(System.IO.Pipelines.PipeOptions.Default);
    private readonly IReadOnlyList<Equipment> _allEquipment;
    private readonly ILogger<ProfinetPlcTransport> _logger;
    private readonly bool _useRealClient;
    private readonly TimeSpan _pollInterval;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private bool _isConnected;

    private static readonly HashSet<string> ProfinetEquipmentCodes =
    [
        "EQ-ASM-01",  // 合装工作站 — Profinet (Siemens S7)
        "EQ-ASM-02",  // 辅助合装台 — Profinet (Siemens S7)
    ];

    public IReadOnlySet<string> SupportedEquipmentCodes => ProfinetEquipmentCodes;
    public PipeReader Reader => _pipe.Reader;
    public string TransportName => "Profinet";
    public bool IsConnected => _isConnected;

    public ProfinetPlcTransport(
        IReadOnlyList<Equipment> equipment,
        ILogger<ProfinetPlcTransport> logger,
        bool useRealClient = false,
        int pollIntervalMs = 500)
    {
        _allEquipment = equipment;
        _logger = logger;
        _useRealClient = useRealClient;
        _pollInterval = TimeSpan.FromMilliseconds(pollIntervalMs);
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        if (_useRealClient)
            _runTask = RunRealAsync(_cts.Token);
        else
            _runTask = SimulateFramesAsync(_cts.Token);

        _isConnected = _useRealClient;
        _logger.ZLogInformation($"Profinet 传输层启动（{(_useRealClient ? "生产" : "模拟")}模式）");
        return Task.CompletedTask;
    }

    private async Task RunRealAsync(CancellationToken ct)
    {
        var devices = _allEquipment.Where(e => ProfinetEquipmentCodes.Contains(e.EquipmentCode)).ToList();
        var tasks = devices.Select(dev => PollDeviceAsync(dev, ct));
        await Task.WhenAll(tasks);
    }

    private static string ParseHost(Equipment eq)
    {
        var s = eq.PlcAddress;
        foreach (var scheme in new[] { "opc.tcp://", "tcp://", "profinet://", "s7://" })
            if (s.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
                s = s[scheme.Length..];
        var host = s.Split(':')[0];
        return host;
    }

    private async Task PollDeviceAsync(Equipment eq, CancellationToken ct)
    {
        var host = ParseHost(eq);
        while (!ct.IsCancellationRequested)
        {
            S7Plc? plc = null;
            try
            {
                plc = new S7Plc(CpuType.S71200, host, 0, 1);
                await plc.OpenAsync();
                if (!plc.IsConnected)
                    throw new InvalidOperationException($"S7 连接 {host}:102 失败");

                _isConnected = true;
                _logger.ZLogInformation($"Profinet 已连接 {eq.EquipmentCode} {host}:102");

                while (!ct.IsCancellationRequested && plc.IsConnected)
                {
                    var snapshot = ReadSnapshot(plc, eq.EquipmentCode);
                    await WriteSnapshotFrameAsync(snapshot, ct);
                    await Task.Delay(_pollInterval, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _isConnected = false;
                _logger.ZLogError($"Profinet 读取 {eq.EquipmentCode} 失败：{ex.Message}（2s 后重连）");
                try { await Task.Delay(TimeSpan.FromSeconds(2), ct); }
                catch (OperationCanceledException) { break; }
            }
            finally
            {
                try { plc?.Close(); } catch { }
            }
        }
    }

    /// <summary>从 S7 PLC 读取 DB1 并映射为 PlcSnapshot（与连接解耦，便于单元测试）。</summary>
    internal static PlcSnapshot ReadSnapshot(S7Plc plc, string equipmentCode)
    {
        var statusWord = Convert.ToInt32(plc.Read(DataType.DataBlock, 1, 0, VarType.Word, 1));
        var cycle = Convert.ToInt64(plc.Read(DataType.DataBlock, 1, 2, VarType.DInt, 1));
        var good = Convert.ToInt64(plc.Read(DataType.DataBlock, 1, 6, VarType.DInt, 1));
        var defect = Convert.ToInt64(plc.Read(DataType.DataBlock, 1, 10, VarType.DInt, 1));
        var runLow = Convert.ToInt64(plc.Read(DataType.DataBlock, 1, 14, VarType.DInt, 1));
        var runHigh = Convert.ToInt64(plc.Read(DataType.DataBlock, 1, 18, VarType.DInt, 1));
        var pressForce = Convert.ToDouble(plc.Read(DataType.DataBlock, 1, 22, VarType.Real, 1));
        var displacement = Convert.ToDouble(plc.Read(DataType.DataBlock, 1, 26, VarType.Real, 1));
        var runMs = (runHigh << 32) | (runLow & 0xFFFFFFFF);

        var status = statusWord switch
        {
            0 => EquipmentStatus.Idle,
            1 => EquipmentStatus.Running,
            2 => EquipmentStatus.Alarm,
            3 => EquipmentStatus.Offline,
            _ => EquipmentStatus.Running,
        };

        var isEven = (cycle & 1) == 0;
        return PlcSnapshot.Create(
            equipmentCode, DateTimeOffset.UtcNow, status,
            cycle, good, defect, runMs,
            isEven ? pressForce : displacement,
            isEven ? "PressForce" : "Displacement");
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

    /// <summary>开发模式：模拟 Profinet 设备数据帧（压装力和位移）</summary>
    private async Task SimulateFramesAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var eq in _allEquipment.Where(e => ProfinetEquipmentCodes.Contains(e.EquipmentCode)))
            {
                var cycle = Random.Shared.Next(1, 100000);
                var pressForce = 20.0 + Random.Shared.NextDouble() * 4 - 2;
                var displacement = 50.0 + Random.Shared.NextDouble() * 2 - 1;
                var (processValue, processTag) = (cycle & 1) == 0 ? (pressForce, "PressForce") : (displacement, "Displacement");

                var snapshot = PlcSnapshot.Create(
                    eq.EquipmentCode, now, EquipmentStatus.Running,
                    cycle, (long)(cycle * 0.97), (long)(cycle * 0.03),
                    cycle * 500L, processValue, processTag);

                await WriteSnapshotFrameAsync(snapshot, ct);
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

    public Task<object> ReadRegisterAsync(string address, string tag, CancellationToken ct = default)
    {
        if (_useRealClient)
            _logger.ZLogWarning($"Profinet 直接读寄存器 {address}.{tag} 需在活动 S7 连接中执行（建议通过实时订阅）");
        return Task.FromResult<object>(0);
    }

    public Task WriteRegisterAsync(string address, string tag, object value, CancellationToken ct = default)
    {
        if (_useRealClient)
            _logger.ZLogWarning($"Profinet 直接写寄存器 {address}.{tag} 需在活动 S7 连接中执行");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
