using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// BOM 内存缓存接口（T1.11 MasterMemory 缓存优化）。
/// 提供 BOM 数据的只读查询，避免工单创建时的高频数据库访问。
/// 缓存由 BomCacheInitializationService 在应用启动时预加载，
/// 支持通过 InvalidateAsync 触发缓存刷新。
/// </summary>
public interface IBomCache
{
    /// <summary>按产品编码 + BOM 版本查询缓存中的 BOM，未命中返回 null。</summary>
    Task<Bom?> GetByProductAndVersionAsync(string productCode, string version, CancellationToken ct = default);

    /// <summary>获取缓存中所有 BOM 的列表（用于管理/监控）。</summary>
    Task<List<Bom>> GetAllAsync(CancellationToken ct = default);

    /// <summary>使用指定的 BOM 列表初始化缓存（启动时预热）。</summary>
    Task InitializeAsync(IReadOnlyList<Bom> boms, CancellationToken ct = default);

    /// <summary>刷新整个缓存（从数据库重新加载）。</summary>
    Task InvalidateAsync(CancellationToken ct = default);

    /// <summary>缓存是否已初始化完成。</summary>
    bool IsInitialized { get; }
}
