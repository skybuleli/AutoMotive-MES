using BenchmarkDotNet.Attributes;
using MesAdmin.Domain.Models;

namespace MesAdmin.Benchmarks;

/// <summary>
/// T4.8 追溯查询性能基准测试。
/// - 正向追溯（VIN → 所有组件）
/// - 反向追溯（物料批次 → 所有 VIN）
/// - TraceabilityLink 创建 + 哈希链计算
///
/// ⚠ 这些基准测试测量内存对象性能（不涉及真实数据库）。
/// 真实 DB 查询延迟取决于 PostgreSQL 索引 + 数据量。
/// 目标：正向 ≤30s，反向 ≤60s（生产环境大数据量）。
/// </summary>
[MemoryDiagnoser]
[MinColumn, MaxColumn]
public class TraceabilityQueryBenchmarks
{
    private List<TraceabilityLink> _allLinks = null!;
    private const int BatchSize = 1247; // 单批次最大追溯件数

    [GlobalSetup]
    public void Setup()
    {
        _allLinks = new List<TraceabilityLink>(BatchSize);
        var now = DateTimeOffset.UtcNow;
        string? prevHash = null;

        for (int i = 0; i < BatchSize; i++)
        {
            var link = TraceabilityLink.Create(
                orderId: Ulid.NewUlid(),
                level: (TraceabilityLevel)((i % 4) + 1), // L1-L4 循环
                vinOrSerial: i == 0
                    ? $"VIN-{i:D17}"
                    : $"ESP9-20260705-{i:D6}",
                componentBatch: $"BATCH-{i % 100:D4}",    // 100 种不同组件批次
                materialBatch: $"MAT-{i % 50:D4}",         // 50 种不同物料批次
                previousHash: prevHash ?? string.Empty,
                createdAt: now.AddMilliseconds(i));

            prevHash = link.Hash;
            _allLinks.Add(link);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  正向追溯查询（VIN → 所有组件）
    // ═══════════════════════════════════════════════════════════

    [Benchmark(Description = "正向追溯: VIN 精确查询")]
    public List<TraceabilityLink> ForwardTrace_ByVin()
        => _allLinks.Where(l => l.VinOrSerial == "VIN-00000000000000000").ToList();

    [Benchmark(Description = "正向追溯: VIN 前缀模糊查询")]
    public List<TraceabilityLink> ForwardTrace_ByVinPrefix()
        => _allLinks.Where(l => l.VinOrSerial!.StartsWith("ESP9-20260705-")).ToList();

    // ═══════════════════════════════════════════════════════════
    //  反向追溯查询（物料批次 → 所有 VIN）
    // ═══════════════════════════════════════════════════════════

    [Benchmark(Description = "反向追溯: 组件批次查询 (~12 条)")]
    public List<TraceabilityLink> ReverseTrace_ByComponentBatch()
        => _allLinks.Where(l => l.ComponentBatch == "BATCH-0001").ToList();

    [Benchmark(Description = "反向追溯: 物料批次查询 (~25 条)")]
    public List<TraceabilityLink> ReverseTrace_ByMaterialBatch()
        => _allLinks.Where(l => l.MaterialBatch == "MAT-0001").ToList();

    [Benchmark(Description = "反向追溯: 全量组件批次分组统计")]
    public Dictionary<string, int> ReverseTrace_BatchGrouping()
        => _allLinks.GroupBy(l => l.ComponentBatch)
                     .ToDictionary(g => g.Key, g => g.Count());

    // ═══════════════════════════════════════════════════════════
    //  TraceabilityLink 创建 + 哈希链
    // ═══════════════════════════════════════════════════════════

    [Benchmark(Description = "TraceabilityLink.Create: 单条 (含 SHA-256)")]
    public TraceabilityLink TraceLink_Create()
        => TraceabilityLink.Create(
            orderId: Ulid.NewUlid(),
            level: TraceabilityLevel.Assembly,
            vinOrSerial: "ESP9-20260705-999999",
            componentBatch: "BATCH-9999",
            materialBatch: "MAT-9999",
            previousHash: "abc123def456",
            createdAt: DateTimeOffset.UtcNow);

    [Benchmark(Description = "TraceabilityLink.Create: 批量 100 条 (哈希链)")]
    public List<TraceabilityLink> TraceLink_CreateBatch100()
    {
        var list = new List<TraceabilityLink>(100);
        string? prevHash = null;
        for (int i = 0; i < 100; i++)
        {
            var link = TraceabilityLink.Create(
                Ulid.NewUlid(),
                TraceabilityLevel.Component,
                $"ESP9-20260705-{i:D6}",
                $"BATCH-{i:D4}",
                $"MAT-{i:D4}",
                prevHash ?? string.Empty,
                DateTimeOffset.UtcNow);
            prevHash = link.Hash;
            list.Add(link);
        }
        return list;
    }

    // ═══════════════════════════════════════════════════════════
    //  哈希链验证性能
    // ═══════════════════════════════════════════════════════════

    [Benchmark(Description = "TraceabilityLink: SHA-256 哈希计算")]
    public string TraceLink_ComputeHash()
    {
        var link = _allLinks[0];
        return link.ComputeHash();
    }

    [Benchmark(Description = "TraceabilityLink: 哈希链完整性验证")]
    public bool TraceLink_VerifyHash()
    {
        // 验证批量创建的前 10 条记录的哈希链完整性
        bool allValid = true;
        for (int i = 0; i < 10 && i < _allLinks.Count; i++)
            allValid &= _allLinks[i].VerifyHash();
        return allValid;
    }

    [Benchmark(Description = "TraceabilityLink: MemoryPack 序列化")]
    public byte[] TraceLink_Serialize()
        => MemoryPack.MemoryPackSerializer.Serialize(_allLinks[0]);

    [Benchmark(Description = "TraceabilityLink: MemoryPack 反序列化")]
    public TraceabilityLink? TraceLink_Deserialize()
    {
        var data = MemoryPack.MemoryPackSerializer.Serialize(_allLinks[0]);
        return MemoryPack.MemoryPackSerializer.Deserialize<TraceabilityLink>(data);
    }
}
