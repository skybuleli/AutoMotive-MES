using MudBlazor;

namespace MesAdmin.Web.Components.Layout;

/// <summary>
/// 全站路由目录：导航分组、页题、图标、面包屑的唯一事实来源。
/// NavMenu 与 MesBreadcrumb 均从此目录取数，避免各自维护导致漂移。
/// </summary>
public static class AppRoutes
{
    public static readonly IReadOnlyList<AppGroup> Groups =
    [
        new("production", "生产执行", Icons.Material.Filled.Description),
        new("quality", "质量管理", Icons.Material.Filled.Analytics),
        new("material", "物料与追溯", Icons.Material.Filled.Inventory2),
        new("equipment", "设备与报警", Icons.Material.Filled.PrecisionManufacturing),
        new("master", "基础数据", Icons.Material.Filled.Schema),
        new("system", "系统管理", Icons.Material.Filled.AdminPanelSettings),
    ];

    public static readonly IReadOnlyList<AppRouteEntry> All =
    [
        new("", "仪表盘", null, Icons.Material.Filled.Dashboard),
        new("dashboard", "生产看板", null, Icons.Material.Filled.SpaceDashboard),
        new("production", "生产工单", "production", Icons.Material.Filled.ListAlt),
        new("production/kanban", "工单看板", "production", Icons.Material.Filled.ViewKanban),
        new("kit-check", "齐套检查", "production", Icons.Material.Filled.FactCheck),
        new("start-production", "开工管理", "production", Icons.Material.Filled.PlayArrow),
        new("quality-review", "完工审核", "production", Icons.Material.Filled.Verified),
        new("scheduling", "生产排程", "production", Icons.Material.Filled.Schedule),
        new("first-article", "首件检验", "quality", Icons.Material.Filled.FactCheck),
        new("inspection-plans", "检验计划", "quality", Icons.Material.Filled.ListAlt),
        new("spc", "SPC 质量管理", "quality", Icons.Material.Filled.QueryStats),
        new("sqe", "SQE 供应商质量", "quality", Icons.Material.Filled.Business),
        new("gauges", "计量器具台账", "quality", Icons.Material.Filled.Straighten),
        new("quality-reports", "质量报表", "quality", Icons.Material.Filled.Description),
        new("material", "物料管理", "material", Icons.Material.Filled.Inventory),
        new("inventory", "线边库存", "material", Icons.Material.Filled.LocalGroceryStore),
        new("jit-kanban", "JIT 看板", "material", Icons.Material.Filled.LocalShipping),
        new("traceability", "全链路追溯", "material", Icons.Material.Filled.Link),
        new("sync-monitor", "同步监控", "material", Icons.Material.Filled.Sync),
        new("spare-parts", "备件管理", "material", Icons.Material.Filled.Handyman),
        new("oee", "设备 OEE", "equipment", Icons.Material.Filled.Timeline),
        new("maintenance", "预防性维护", "equipment", Icons.Material.Filled.Build),
        new("hydraulic-test", "液压测试台", "equipment", Icons.Material.Filled.Speed),
        new("andon", "Andon 报警", "equipment", Icons.Material.Filled.Campaign),
        new("routing", "工艺管理", "master", Icons.Material.Filled.Route),
        new("users", "用户管理", "system", Icons.Material.Filled.People),
        new("audit-logs", "审计日志", "system", Icons.Material.Filled.ReceiptLong),
        new("login", "登录", null, Icons.Material.Filled.Login),
    ];

    public static AppRouteEntry? Find(string path)
    {
        var normalized = Normalize(path);
        return All.FirstOrDefault(r => r.Path == normalized);
    }

    public static string GetTitle(string path)
        => Find(path)?.Title ?? "制造执行系统";

    public static string GetSectionLabel(string path)
    {
        var entry = Find(path);
        if (entry is null || entry.GroupKey is null)
            return GetTitle(path);
        return $"{Groups.FirstOrDefault(g => g.Key == entry.GroupKey)?.Title} · {entry.Title}";
    }

    /// <summary>构建面包屑路径：首页 › 分组 › 当前页（末段为当前页）。</summary>
    public static IReadOnlyList<CrumbSegment> TrailFor(string path)
    {
        var entry = Find(path);
        if (entry is null)
            return [new CrumbSegment("首页", "/", Icons.Material.Filled.Home, false),
                    new CrumbSegment(GetTitle(path), null, Icons.Material.Filled.Public, true)];

        var segments = new List<CrumbSegment> { new("首页", "/", Icons.Material.Filled.Home, false) };

        if (entry.GroupKey is not null)
        {
            var group = Groups.FirstOrDefault(g => g.Key == entry.GroupKey);
            if (group is not null)
                segments.Add(new CrumbSegment(group.Title, null, group.Icon, false));
        }

        segments.Add(new CrumbSegment(entry.Title, entry.Path, entry.Icon, true));
        return segments;
    }

    private static string Normalize(string path)
        => (path ?? string.Empty).Trim().TrimStart('/');
}

/// <summary>业务导航分组。</summary>
public sealed record AppGroup(string Key, string Title, string Icon);

/// <summary>路由条目：Path 为无前导斜杠的相对路径（"" 表示首页）。</summary>
public sealed record AppRouteEntry(string Path, string Title, string? GroupKey, string Icon);

/// <summary>面包屑片段。</summary>
public sealed record CrumbSegment(string Label, string? Href, string Icon, bool IsCurrent);