namespace MesAdmin.Application.Features.Inspections;

/// <summary>
/// ESP-9.0 控制计划默认检验项（23 个检验特性）。
/// </summary>
internal static class InspectionControlPlans
{
    public static readonly List<(string Code, string Name, double Std, string Unit, double? Upper, double? Lower)> Esp9 =
    [
        ("DIM-01", "阀体安装孔直径", 12.0, "mm", 12.05, 11.95),
        ("DIM-02", "ECU 定位销高度", 8.5, "mm", 8.55, 8.45),
        ("DIM-03", "电机轴长度", 45.0, "mm", 45.1, 44.9),
        ("TOR-01", "M6 螺栓扭矩", 22.0, "Nm", 23.0, 21.0),
        ("TOR-02", "M6 螺栓角度", 180.0, "°", 185.0, 175.0),
        ("TOR-03", "M8 螺栓扭矩", 45.0, "Nm", 47.0, 43.0),
        ("TOR-04", "M8 螺栓角度", 270.0, "°", 280.0, 260.0),
        ("HYD-01", "建压时间", 200.0, "ms", 250.0, 150.0),
        ("HYD-02", "保压压力", 180.0, "bar", 185.0, 175.0),
        ("HYD-03", "泄漏率", 0.5, "CC/hr", 0.5, null),
        ("ELE-01", "ECU 供电电压", 12.0, "V", 12.5, 11.5),
        ("ELE-02", "CAN 通信延迟", 100.0, "μs", 150.0, null),
        ("ELE-03", "传感器标定偏差", 0.5, "%", 1.0, null),
        ("FLS-01", "固件版本校验", 1.0, "-", null, null),
        ("FLS-02", "CRC32 校验和", 1.0, "-", null, null),
        ("VIS-01", "外观检查", 1.0, "-", null, null),
        ("VIS-02", "标签位置", 1.0, "-", null, null),
        ("VIS-03", "二维码可读性", 1.0, "-", null, null),
        ("FUN-01", "ESP 功能自检", 1.0, "-", null, null),
        ("FUN-02", "电磁阀响应", 10.0, "ms", 15.0, 5.0),
        ("FUN-03", "ABS 触发测试", 1.0, "-", null, null),
        ("FUN-04", "TCS 触发测试", 1.0, "-", null, null),
        ("FUN-05", "ESC 触发测试", 1.0, "-", null, null),
    ];
}
