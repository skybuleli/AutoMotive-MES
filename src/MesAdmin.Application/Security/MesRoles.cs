namespace MesAdmin.Application.Security;

/// <summary>
/// MES 系统角色定义（对应 PRD v2 第 3 节）。
/// 6 个角色，用于 JWT Claim 和 [Authorize] 权限控制。
/// </summary>
public static class MesRoles
{
    /// <summary>生产经理 — 实时 OEE 看板、工单进度、异常升级处理</summary>
    public const string ProductionManager = "ProductionManager";

    /// <summary>班组长 — 开班/报工/Andon、首件确认、换型操作</summary>
    public const string ShiftLeader = "ShiftLeader";

    /// <summary>质量工程师 — SPC 监控、Cpk 报告、8D/CAR 闭环</summary>
    public const string QualityEngineer = "QualityEngineer";

    /// <summary>设备工程师 — PLC 数据、TPM 计划、OEE 分析</summary>
    public const string EquipmentEngineer = "EquipmentEngineer";

    /// <summary>仓库员 — 批次扫码、线边库存、JIT 看板</summary>
    public const string WarehouseClerk = "WarehouseClerk";

    /// <summary>SQE — 来料合格率、PPAP、供应商评分</summary>
    public const string SupplierQualityEngineer = "SupplierQualityEngineer";

    /// <summary>全部角色列表</summary>
    public static readonly string[] All =
    [
        ProductionManager,
        ShiftLeader,
        QualityEngineer,
        EquipmentEngineer,
        WarehouseClerk,
        SupplierQualityEngineer
    ];
}
