using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// 设备运行状态。
/// </summary>
public enum EquipmentStatus
{
    /// <summary>运行中</summary>
    Running = 0,

    /// <summary>待机</summary>
    Idle = 1,

    /// <summary>报警</summary>
    Alarm = 2,

    /// <summary>离线</summary>
    Offline = 3
}

/// <summary>
/// 设备实体（T2.11）。
/// ESP 总成产线 7 站 8 台核心设备。
/// 当前作为内存常量清单（8 台），T2.17 预防性维护时建表落库。
/// </summary>
[MemoryPackable]
public partial class Equipment
{
    /// <summary>主键（Ulid）</summary>
    public Ulid Id { get; set; }

    /// <summary>设备编码（唯一，如 EQ-TQ-01）</summary>
    public string EquipmentCode { get; set; } = string.Empty;

    /// <summary>设备名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>所属工站编号（1-7）</summary>
    public int Station { get; set; }

    /// <summary>设备类型（拧紧机/液压台/刷写台/压装机...）</summary>
    public string EquipmentType { get; set; } = string.Empty;

    /// <summary>PLC 地址（OPC UA endpoint 或 IP:Port）</summary>
    public string PlcAddress { get; set; } = string.Empty;

    /// <summary>当前状态</summary>
    public EquipmentStatus Status { get; set; } = EquipmentStatus.Offline;

    /// <summary>OEE 目标值（0~1，默认 0.85）</summary>
    public double OeeTarget { get; set; } = 0.85;

    public static Equipment Create(
        Ulid id,
        string equipmentCode,
        string name,
        int station,
        string equipmentType,
        string plcAddress)
    {
        if (string.IsNullOrWhiteSpace(equipmentCode))
            throw new ArgumentException("设备编码不能为空", nameof(equipmentCode));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("设备名称不能为空", nameof(name));
        if (station is < 1 or > 7)
            throw new ArgumentOutOfRangeException(nameof(station), "工站编号仅支持 1-7");

        return new Equipment
        {
            Id = id,
            EquipmentCode = equipmentCode.Trim(),
            Name = name.Trim(),
            Station = station,
            EquipmentType = equipmentType.Trim(),
            PlcAddress = plcAddress.Trim(),
            Status = EquipmentStatus.Offline,
            OeeTarget = 0.85,
        };
    }

    /// <summary>
    /// ESP 总成产线 8 台核心设备清单（内存常量）。
    /// 对应 7 工站：站2 合装 / 站3 拧紧 / 站4 液压 / 站5 刷写 / 站6 终检 / 站7 VIN 绑定 + 1 台备用。
    /// </summary>
    public static readonly IReadOnlyList<Equipment> DefaultEquipment =
    [
        Create(Ulid.NewUlid(), "EQ-ASM-01", "合装工作站", 2, "装配台", "opc.tcp://station2:4840"),
        Create(Ulid.NewUlid(), "EQ-TQ-01", "螺栓拧紧机", 3, "拧紧机", "opc.tcp://station3:4840"),
        Create(Ulid.NewUlid(), "EQ-HYD-01", "液压测试台", 4, "液压台", "opc.tcp://station4:4840"),
        Create(Ulid.NewUlid(), "EQ-FLS-01", "ECU 刷写台", 5, "刷写台", "opc.tcp://station5:4840"),
        Create(Ulid.NewUlid(), "EQ-FT-01", "功能终检台", 6, "测试台", "opc.tcp://station6:4840"),
        Create(Ulid.NewUlid(), "EQ-VN-01", "VIN 绑定台", 7, "标刻机", "opc.tcp://station7:4840"),
        Create(Ulid.NewUlid(), "EQ-ASM-02", "辅助合装台", 2, "装配台", "opc.tcp://station2b:4840"),
        Create(Ulid.NewUlid(), "EQ-TQ-02", "备用拧紧机", 3, "拧紧机", "opc.tcp://station3b:4840"),
    ];
}
