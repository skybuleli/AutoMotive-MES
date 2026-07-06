using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Routing;

/// <summary>
/// ESP-9.0/9.1 默认工艺路线定义（T3.1/T3.2）。
/// 31 工序 × 7 站，含标准工时、工装夹具、参数模板。
/// 参数模板基于 ESP 控制计划：M6/M8 扭矩、液压测试、CAN 通信标准值。
/// </summary>
public static class EspDefaultRouting
{
    public static Domain.Models.Routing CreateDefault(string productCode)
    {
        var operations = new List<RoutingOperation>
        {
            // ═══ 站1: 上料扫码 (Seq 1) ═══
            new()
            {
                Sequence = 1, Station = 1, OperationCode = "LOAD-01", OperationName = "上料扫码",
                StandardTimeSeconds = 12, FixtureCode = "FIX-LOAD-01", FixtureName = "上料定位夹具",
                ParameterTemplates = []
            },

            // ═══ 站2: 合装装配 (Seq 2-5) ═══
            new()
            {
                Sequence = 2, Station = 2, OperationCode = "ASM-01", OperationName = "HCU 定位",
                StandardTimeSeconds = 18, FixtureCode = "FIX-ASM-01", FixtureName = "HCU 定位夹具",
                ParameterTemplates =
                [
                    new() { ParameterCode = "DIM-01", ParameterName = "HCU 定位销直径", StandardValue = 8.0, UpperSpecLimit = 8.05, LowerSpecLimit = 7.95, Unit = "mm", EnableSpc = true },
                    new() { ParameterCode = "DIM-02", ParameterName = "安装面平面度", StandardValue = 0.1, UpperSpecLimit = 0.15, Unit = "mm", EnableSpc = false },
                ]
            },
            new()
            {
                Sequence = 3, Station = 2, OperationCode = "ASM-02", OperationName = "ECU 预装",
                StandardTimeSeconds = 15, FixtureCode = "FIX-ASM-02", FixtureName = "ECU 安装夹具",
                ParameterTemplates =
                [
                    new() { ParameterCode = "DIM-03", ParameterName = "ECU 安装间隙", StandardValue = 0.5, UpperSpecLimit = 0.8, LowerSpecLimit = 0.2, Unit = "mm", EnableSpc = true },
                ]
            },
            new()
            {
                Sequence = 4, Station = 2, OperationCode = "ASM-03", OperationName = "电机安装",
                StandardTimeSeconds = 20, FixtureCode = "FIX-ASM-03", FixtureName = "电机压装夹具",
                ParameterTemplates =
                [
                    new() { ParameterCode = "FOR-01", ParameterName = "压装力", StandardValue = 500, UpperSpecLimit = 550, LowerSpecLimit = 450, Unit = "N", EnableSpc = true },
                    new() { ParameterCode = "DIM-04", ParameterName = "电机轴向位置", StandardValue = 12.5, UpperSpecLimit = 12.7, LowerSpecLimit = 12.3, Unit = "mm", EnableSpc = true },
                ]
            },
            new()
            {
                Sequence = 5, Station = 2, OperationCode = "ASM-04", OperationName = "线束连接",
                StandardTimeSeconds = 25, FixtureCode = null, FixtureName = null,
                ParameterTemplates =
                [
                    new() { ParameterCode = "ELE-03", ParameterName = "连接器插拔力", StandardValue = 30, UpperSpecLimit = 45, LowerSpecLimit = 15, Unit = "N", EnableSpc = true },
                ]
            },

            // ═══ 站3: 螺栓拧紧 (Seq 6-10) ═══
            new()
            {
                Sequence = 6, Station = 3, OperationCode = "TQ-01", OperationName = "M6-FL 螺栓拧紧",
                StandardTimeSeconds = 8, FixtureCode = "FIX-TQ-01", FixtureName = "拧紧反力臂 FL",
                ParameterTemplates =
                [
                    new() { ParameterCode = "TOR-M6", ParameterName = "M6 螺栓最终扭矩", StandardValue = 22, UpperSpecLimit = 23, LowerSpecLimit = 21, Unit = "Nm", EnableSpc = true, SpcSubgroupSize = 5 },
                    new() { ParameterCode = "ANG-M6", ParameterName = "M6 角度法角度", StandardValue = 180, UpperSpecLimit = 185, LowerSpecLimit = 175, Unit = "°", EnableSpc = true },
                ]
            },
            new()
            {
                Sequence = 7, Station = 3, OperationCode = "TQ-02", OperationName = "M6-FR 螺栓拧紧",
                StandardTimeSeconds = 8, FixtureCode = "FIX-TQ-02", FixtureName = "拧紧反力臂 FR",
                ParameterTemplates =
                [
                    new() { ParameterCode = "TOR-M6", ParameterName = "M6 螺栓最终扭矩", StandardValue = 22, UpperSpecLimit = 23, LowerSpecLimit = 21, Unit = "Nm", EnableSpc = true, SpcSubgroupSize = 5 },
                    new() { ParameterCode = "ANG-M6", ParameterName = "M6 角度法角度", StandardValue = 180, UpperSpecLimit = 185, LowerSpecLimit = 175, Unit = "°", EnableSpc = true },
                ]
            },
            new()
            {
                Sequence = 8, Station = 3, OperationCode = "TQ-03", OperationName = "M8-RL 螺栓拧紧",
                StandardTimeSeconds = 10, FixtureCode = "FIX-TQ-03", FixtureName = "拧紧反力臂 RL",
                ParameterTemplates =
                [
                    new() { ParameterCode = "TOR-M8", ParameterName = "M8 螺栓最终扭矩", StandardValue = 45, UpperSpecLimit = 47, LowerSpecLimit = 43, Unit = "Nm", EnableSpc = true, SpcSubgroupSize = 5 },
                    new() { ParameterCode = "ANG-M8", ParameterName = "M8 角度法角度", StandardValue = 270, UpperSpecLimit = 280, LowerSpecLimit = 260, Unit = "°", EnableSpc = true },
                ]
            },
            new()
            {
                Sequence = 9, Station = 3, OperationCode = "TQ-04", OperationName = "M8-RR 螺栓拧紧",
                StandardTimeSeconds = 10, FixtureCode = "FIX-TQ-04", FixtureName = "拧紧反力臂 RR",
                ParameterTemplates =
                [
                    new() { ParameterCode = "TOR-M8", ParameterName = "M8 螺栓最终扭矩", StandardValue = 45, UpperSpecLimit = 47, LowerSpecLimit = 43, Unit = "Nm", EnableSpc = true, SpcSubgroupSize = 5 },
                    new() { ParameterCode = "ANG-M8", ParameterName = "M8 角度法角度", StandardValue = 270, UpperSpecLimit = 280, LowerSpecLimit = 260, Unit = "°", EnableSpc = true },
                ]
            },
            new()
            {
                Sequence = 10, Station = 3, OperationCode = "TQ-05", OperationName = "扭矩复检",
                StandardTimeSeconds = 6, FixtureCode = null, FixtureName = null,
                ParameterTemplates =
                [
                    new() { ParameterCode = "TOR-M6-R", ParameterName = "M6 扭矩复检值", StandardValue = 22, UpperSpecLimit = 23, LowerSpecLimit = 20, Unit = "Nm", EnableSpc = true },
                    new() { ParameterCode = "TOR-M8-R", ParameterName = "M8 扭矩复检值", StandardValue = 45, UpperSpecLimit = 47, LowerSpecLimit = 42, Unit = "Nm", EnableSpc = true },
                ]
            },

            // ═══ 站4: 液压测试 (Seq 11-23) ═══
            new() { Sequence = 11, Station = 4, OperationCode = "HY-01", OperationName = "电磁阀 1 测试", StandardTimeSeconds = 3, FixtureCode = "FIX-HYD-01", FixtureName = "液压测试夹具", ParameterTemplates = CreateSolenoidParams(1) },
            new() { Sequence = 12, Station = 4, OperationCode = "HY-02", OperationName = "电磁阀 2 测试", StandardTimeSeconds = 3, FixtureCode = null, FixtureName = null, ParameterTemplates = CreateSolenoidParams(2) },
            new() { Sequence = 13, Station = 4, OperationCode = "HY-03", OperationName = "电磁阀 3 测试", StandardTimeSeconds = 3, FixtureCode = null, FixtureName = null, ParameterTemplates = CreateSolenoidParams(3) },
            new() { Sequence = 14, Station = 4, OperationCode = "HY-04", OperationName = "电磁阀 4 测试", StandardTimeSeconds = 3, FixtureCode = null, FixtureName = null, ParameterTemplates = CreateSolenoidParams(4) },
            new() { Sequence = 15, Station = 4, OperationCode = "HY-05", OperationName = "电磁阀 5 测试", StandardTimeSeconds = 3, FixtureCode = null, FixtureName = null, ParameterTemplates = CreateSolenoidParams(5) },
            new() { Sequence = 16, Station = 4, OperationCode = "HY-06", OperationName = "电磁阀 6 测试", StandardTimeSeconds = 3, FixtureCode = null, FixtureName = null, ParameterTemplates = CreateSolenoidParams(6) },
            new() { Sequence = 17, Station = 4, OperationCode = "HY-07", OperationName = "电磁阀 7 测试", StandardTimeSeconds = 3, FixtureCode = null, FixtureName = null, ParameterTemplates = CreateSolenoidParams(7) },
            new() { Sequence = 18, Station = 4, OperationCode = "HY-08", OperationName = "电磁阀 8 测试", StandardTimeSeconds = 3, FixtureCode = null, FixtureName = null, ParameterTemplates = CreateSolenoidParams(8) },
            new() { Sequence = 19, Station = 4, OperationCode = "HY-09", OperationName = "电磁阀 9 测试", StandardTimeSeconds = 3, FixtureCode = null, FixtureName = null, ParameterTemplates = CreateSolenoidParams(9) },
            new() { Sequence = 20, Station = 4, OperationCode = "HY-10", OperationName = "电磁阀 10 测试", StandardTimeSeconds = 3, FixtureCode = null, FixtureName = null, ParameterTemplates = CreateSolenoidParams(10) },
            new() { Sequence = 21, Station = 4, OperationCode = "HY-11", OperationName = "电磁阀 11 测试", StandardTimeSeconds = 3, FixtureCode = null, FixtureName = null, ParameterTemplates = CreateSolenoidParams(11) },
            new() { Sequence = 22, Station = 4, OperationCode = "HY-12", OperationName = "电磁阀 12 测试", StandardTimeSeconds = 3, FixtureCode = null, FixtureName = null, ParameterTemplates = CreateSolenoidParams(12) },
            new()
            {
                Sequence = 23, Station = 4, OperationCode = "HY-13", OperationName = "建压/保压/泄压循环",
                StandardTimeSeconds = 15, FixtureCode = null, FixtureName = null,
                ParameterTemplates =
                [
                    new() { ParameterCode = "HYD-01", ParameterName = "建压时间", StandardValue = 150, UpperSpecLimit = 200, LowerSpecLimit = 100, Unit = "ms", EnableSpc = true, SpcSubgroupSize = 5 },
                    new() { ParameterCode = "HYD-02", ParameterName = "保压压力", StandardValue = 100, UpperSpecLimit = 105, LowerSpecLimit = 95, Unit = "bar", EnableSpc = true, SpcSubgroupSize = 5 },
                    new() { ParameterCode = "HYD-03", ParameterName = "泄漏率", StandardValue = 0, UpperSpecLimit = 0.5, Unit = "CC/hr", EnableSpc = true, SpcSubgroupSize = 5 },
                ]
            },

            // ═══ 站5: ECU 刷写 (Seq 24-27) ═══
            new()
            {
                Sequence = 24, Station = 5, OperationCode = "FL-01", OperationName = "Bootloader 刷写",
                StandardTimeSeconds = 30, FixtureCode = "FIX-FLS-01", FixtureName = "刷写编程座",
                ParameterTemplates =
                [
                    new() { ParameterCode = "ELE-04", ParameterName = "刷写电压", StandardValue = 12.0, UpperSpecLimit = 12.5, LowerSpecLimit = 11.5, Unit = "V", EnableSpc = true },
                ]
            },
            new()
            {
                Sequence = 25, Station = 5, OperationCode = "FL-02", OperationName = "应用固件刷写",
                StandardTimeSeconds = 45, FixtureCode = null, FixtureName = null,
                ParameterTemplates =
                [
                    new() { ParameterCode = "ELE-05", ParameterName = "固件 CRC32 校验", StandardValue = 1, UpperSpecLimit = 1, LowerSpecLimit = 1, Unit = "", EnableSpc = false },
                    new() { ParameterCode = "ELE-06", ParameterName = "刷写速度", StandardValue = 512, UpperSpecLimit = null, LowerSpecLimit = 256, Unit = "Kbps", EnableSpc = false },
                ]
            },
            new()
            {
                Sequence = 26, Station = 5, OperationCode = "FL-03", OperationName = "标定参数写入",
                StandardTimeSeconds = 20, FixtureCode = null, FixtureName = null,
                ParameterTemplates = []
            },
            new()
            {
                Sequence = 27, Station = 5, OperationCode = "FL-04", OperationName = "CRC32 校验确认",
                StandardTimeSeconds = 5, FixtureCode = null, FixtureName = null,
                ParameterTemplates =
                [
                    new() { ParameterCode = "ELE-07", ParameterName = "整体 CRC32", StandardValue = 1, UpperSpecLimit = 1, LowerSpecLimit = 1, Unit = "", EnableSpc = false },
                ]
            },

            // ═══ 站6: 功能终检 (Seq 28-30) ═══
            new()
            {
                Sequence = 28, Station = 6, OperationCode = "FT-01", OperationName = "CAN 通信测试",
                StandardTimeSeconds = 12, FixtureCode = "FIX-FT-01", FixtureName = "功能测试台架",
                ParameterTemplates =
                [
                    new() { ParameterCode = "ELE-01", ParameterName = "ECU 供电电压", StandardValue = 12.0, UpperSpecLimit = 14.5, LowerSpecLimit = 9.0, Unit = "V", EnableSpc = true },
                    new() { ParameterCode = "ELE-02", ParameterName = "CAN 通信延迟", StandardValue = 50, UpperSpecLimit = 100, LowerSpecLimit = null, Unit = "μs", EnableSpc = true, SpcSubgroupSize = 5 },
                    new() { ParameterCode = "CAN-01", ParameterName = "CAN 总线错误帧率", StandardValue = 0, UpperSpecLimit = 0.1, Unit = "%", EnableSpc = true },
                ]
            },
            new()
            {
                Sequence = 29, Station = 6, OperationCode = "FT-02", OperationName = "传感器标定",
                StandardTimeSeconds = 18, FixtureCode = null, FixtureName = null,
                ParameterTemplates =
                [
                    new() { ParameterCode = "SEN-01", ParameterName = "压力传感器零点", StandardValue = 0.5, UpperSpecLimit = 1.0, LowerSpecLimit = 0.0, Unit = "V", EnableSpc = true },
                    new() { ParameterCode = "SEN-02", ParameterName = "加速度传感器偏置", StandardValue = 0, UpperSpecLimit = 0.5, LowerSpecLimit = -0.5, Unit = "g", EnableSpc = true },
                ]
            },
            new()
            {
                Sequence = 30, Station = 6, OperationCode = "FT-03", OperationName = "ESP 功能模拟",
                StandardTimeSeconds = 25, FixtureCode = null, FixtureName = null,
                ParameterTemplates =
                [
                    new() { ParameterCode = "ESP-01", ParameterName = "制动建压时间", StandardValue = 200, UpperSpecLimit = 300, LowerSpecLimit = 100, Unit = "ms", EnableSpc = true },
                    new() { ParameterCode = "ESP-02", ParameterName = "横摆角速度偏差", StandardValue = 0, UpperSpecLimit = 2, LowerSpecLimit = -2, Unit = "°/s", EnableSpc = true },
                ]
            },

            // ═══ 站7: VIN 绑定 + 标签打印 (Seq 31) ═══
            new()
            {
                Sequence = 31, Station = 7, OperationCode = "VN-01", OperationName = "VIN 绑定 + 标签打印",
                StandardTimeSeconds = 10, FixtureCode = "FIX-VN-01", FixtureName = "标签打印定位夹具",
                ParameterTemplates = []
            },
        };

        var now = DateTimeOffset.UtcNow;
        return new Domain.Models.Routing
        {
            Id = Ulid.NewUlid(),
            ProductCode = productCode.Trim().ToUpperInvariant(),
            Name = $"{productCode} 标准工艺路线 V1.0",
            Version = "1.0",
            EcoNumber = null,
            EcoStatus = EcoStatus.Released,
            OperationCount = 31,
            IsActive = true,
            ChangeDescription = "初始版本 — ESP 标准工艺路线",
            CreatedBy = "system",
            ApprovedBy = "system",
            ApprovedAt = now,
            Operations = operations,
            CreatedAt = now,
            UpdatedAt = now,
            EffectiveDate = now,
        };
    }

    private static List<ParameterTemplate> CreateSolenoidParams(int solenoidNumber)
    {
        var code = solenoidNumber switch
        {
            1 => "SOL-IN-01", 2 => "SOL-IN-02", 3 => "SOL-IN-03", 4 => "SOL-IN-04",
            5 => "SOL-OUT-01", 6 => "SOL-OUT-02", 7 => "SOL-OUT-03", 8 => "SOL-OUT-04",
            9 => "SOL-DUMP-01", 10 => "SOL-DUMP-02", 11 => "SOL-DUMP-03", 12 => "SOL-DUMP-04",
            _ => $"SOL-{solenoidNumber:D2}"
        };

        // 入口电磁阀: 常开 12Ω, 出口电磁阀: 常开 8Ω, 泄放电磁阀: 常闭 6Ω
        var (resistance, resLower, resUpper) = solenoidNumber switch
        {
            <= 4 => (12.0, 11.0, 13.0),   // 入口阀
            <= 8 => (8.0, 7.0, 9.0),     // 出口阀
            _ => (6.0, 5.0, 7.0),         // 泄放阀
        };

        return
        [
            new() { ParameterCode = code, ParameterName = $"电磁阀 {solenoidNumber} 线圈电阻", StandardValue = resistance, UpperSpecLimit = resUpper, LowerSpecLimit = resLower, Unit = "Ω", EnableSpc = true },
            new() { ParameterCode = $"{code}-T", ParameterName = $"电磁阀 {solenoidNumber} 响应时间", StandardValue = 5, UpperSpecLimit = 8, LowerSpecLimit = 2, Unit = "ms", EnableSpc = true },
        ];
    }
}
