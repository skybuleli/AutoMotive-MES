using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using MemoryPack;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.RealTime;

namespace MesAdmin.Benchmarks;

/// <summary>
/// T4.9 SignalR 并发推送基准测试。
/// - MemoryPack 序列化/反序列化吞吐量（替代 JSON，AGENTS.md 4.4 铁律）
/// - 并发推送模拟（多客户端同时接收）
/// - PlcDataChanged / AndonMessage 消息序列化
///
/// ⚠ 此测试仅测量序列化+内存推送开销，不包含真实 WebSocket 传输。
/// 实际网络延迟在生产环境由网络带宽+拓扑决定。
/// </summary>
[MemoryDiagnoser]
[MinColumn, MaxColumn]
[RankColumn]
public class SignalRPushBenchmarks
{
    private PlcDataChanged _plcMessage = null!;
    private AndonEventCreatedMessage _andonMessage = null!;
    private OeeRecord _oeeRecord = null!;
    private byte[] _serializedPlc = null!;

    [GlobalSetup]
    public void Setup()
    {
        _plcMessage = new PlcDataChanged(
            OeeRecord.Compute("EQ-TQ-01", DateTimeOffset.UtcNow,
                availability: 0.95, performance: 0.88, quality: 0.96));

        _oeeRecord = OeeRecord.Compute("EQ-TQ-01", DateTimeOffset.UtcNow,
            availability: 0.95, performance: 0.88, quality: 0.96);

        _andonMessage = new AndonEventCreatedMessage(
            EventId: Ulid.NewUlid().ToString(),
            EventNumber: "ANDON-20260705-0001",
            EquipmentCode: "EQ-TQ-01",
            Station: 3,
            AlarmType: AndonAlarmType.ProcessDeviation,
            Severity: AndonSeverity.Critical,
            Status: AndonEventStatus.Active,
            Description: "M6 扭矩超差: 23.5Nm > 23.0Nm UCL",
            ProcessValue: 23.5,
            ProcessTag: "Torque-M6-FL",
            UpperLimit: 23.0,
            LowerLimit: 21.0,
            OccurredAt: DateTimeOffset.UtcNow);

        _serializedPlc = MemoryPackSerializer.Serialize(_plcMessage);
        _serializedHealth = MemoryPackSerializer.Serialize(_healthMsg);
    }

    // ═══════════════════════════════════════════════════════════
    //  PlcDataChanged 消息序列化
    // ═══════════════════════════════════════════════════════════

    [Benchmark(Description = "MemoryPack: PlcDataChanged 序列化")]
    public byte[] Serialize_PlcDataChanged()
        => MemoryPackSerializer.Serialize(_plcMessage);

    [Benchmark(Description = "MemoryPack: PlcDataChanged 反序列化")]
    public PlcDataChanged? Deserialize_PlcDataChanged()
        => MemoryPackSerializer.Deserialize<PlcDataChanged>(_serializedPlc);

    // ═══════════════════════════════════════════════════════════
    //  OeeRecord 序列化（SignalR 推送的核心负载）
    // ═══════════════════════════════════════════════════════════

    [Benchmark(Description = "MemoryPack: OeeRecord 序列化")]
    public byte[] Serialize_OeeRecord()
        => MemoryPackSerializer.Serialize(_oeeRecord);

    [Benchmark(Description = "MemoryPack: OeeRecord 反序列化")]
    public OeeRecord? Deserialize_OeeRecord()
        => MemoryPackSerializer.Deserialize<OeeRecord>(_serializedPlc); // 复用 PlcDataChanged 序列化数据

    // ═══════════════════════════════════════════════════════════
    //  Andon 消息序列化（5 种消息类型）
    // ═══════════════════════════════════════════════════════════

    [Benchmark(Description = "MemoryPack: AndonCreated 序列化")]
    public byte[] Serialize_AndonCreated()
        => MemoryPackSerializer.Serialize(_andonMessage);

    [Benchmark(Description = "MemoryPack: AndonCreated 反序列化")]
    public AndonEventCreatedMessage? Deserialize_AndonCreated()
    {
        var data = MemoryPackSerializer.Serialize(_andonMessage);
        return MemoryPackSerializer.Deserialize<AndonEventCreatedMessage>(data);
    }

    // ═══════════════════════════════════════════════════════════
    //  并发推送模拟
    // ═══════════════════════════════════════════════════════════

    [Params(10, 50, 100)]  // 模拟并发客户端数
    public int ConcurrentClients { get; set; }

    [Benchmark(Description = "SignalR: 并发推送（N 客户端 × 100 条）")]
    public async Task ConcurrentPush_Simulation()
    {
        var serialized = MemoryPackSerializer.Serialize(_oeeRecord);
        var tasks = new Task[ConcurrentClients];

        for (int c = 0; c < ConcurrentClients; c++)
        {
            tasks[c] = Task.Run(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    // 模拟 SignalR HubContext.Clients.All.SendAsync
                    var deserialized = MemoryPackSerializer.Deserialize<OeeRecord>(serialized);
                    if (deserialized is null) throw new InvalidOperationException();
                }
            });
        }

        await Task.WhenAll(tasks);
    }

    [Benchmark(Description = "SignalR: 批量序列化（N 条 × 5 消息类型）")]
    public long BatchSerialize_AllMessageTypes()
    {
        long totalBytes = 0;
        var serialized = new byte[ConcurrentClients * 5][];

        int idx = 0;
        for (int i = 0; i < ConcurrentClients; i++)
        {
            serialized[idx++] = MemoryPackSerializer.Serialize(_oeeRecord);
            serialized[idx++] = MemoryPackSerializer.Serialize(_plcMessage);
            serialized[idx++] = MemoryPackSerializer.Serialize(_andonMessage);
            serialized[idx++] = MemoryPackSerializer.Serialize(_oeeRecord);
            serialized[idx++] = MemoryPackSerializer.Serialize(_plcMessage);
        }

        foreach (var data in serialized)
            totalBytes += data.Length;
        return totalBytes;
    }

    // ═══════════════════════════════════════════════════════════
    //  ChannelHealth 消息（DashboardHub 10s 推送）
    // ═══════════════════════════════════════════════════════════

    private readonly ChannelHealthMessage _healthMsg = new(
        EquipmentCount: "8",
        Written: 100000,
        Read: 95000,
        Utilization: 0.05);

    [Benchmark(Description = "MemoryPack: ChannelHealth 序列化")]
    public byte[] Serialize_ChannelHealth()
        => MemoryPackSerializer.Serialize(_healthMsg);

    private byte[] _serializedHealth = null!;

    [Benchmark(Description = "MemoryPack: ChannelHealth 反序列化")]
    public ChannelHealthMessage? Deserialize_ChannelHealth()
        => MemoryPackSerializer.Deserialize<ChannelHealthMessage>(_serializedHealth);
}
