using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.Data;

/// <summary>
/// MES 系统种子数据初始化器。
/// 首次启动时向 Bom / MaterialInventorySetting / MaterialBatch 表写入初始数据，
/// 使齐套检查（T1.4）和线边库存监控（T1.13）真实生效。
/// 幂等：已存在数据则跳过。
/// </summary>
public static class MesDataSeeder
{
    /// <summary>ESP-9.0 BOM 版本号</summary>
    public const string Esp90BomVersion = "V1.0";

    /// <summary>ESP-9.1 BOM 版本号</summary>
    public const string Esp91BomVersion = "V1.0";

    public static async Task SeedAsync(IServiceProvider sp, ILogger logger)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var batchRepo = scope.ServiceProvider.GetRequiredService<IMaterialBatchRepository>();

        // ── 1. 种子 BOM 数据 ──────────────────────────────────
        var hasBom = db.Boms.Any(b => b.ProductCode == "ESP-9.0" && b.Version == Esp90BomVersion);
        if (!hasBom)
        {
            logger.ZLogInformation($"种子数据：创建 ESP-9.0 BOM（{GetEsp90BomItems().Count} 种物料）");
            var bom90 = Bom.Create(Ulid.NewUlid(), "ESP-9.0", Esp90BomVersion, DateTimeOffset.UtcNow);
            foreach (var item in GetEsp90BomItems())
                bom90.AddItem(item);
            db.Boms.Add(bom90);

            logger.ZLogInformation($"种子数据：创建 ESP-9.1 BOM（{GetEsp91BomItems().Count} 种物料）");
            var bom91 = Bom.Create(Ulid.NewUlid(), "ESP-9.1", Esp91BomVersion, DateTimeOffset.UtcNow);
            foreach (var item in GetEsp91BomItems())
                bom91.AddItem(item);
            db.Boms.Add(bom91);

            await db.SaveChangesAsync();
        }
        else
        {
            logger.ZLogInformation($"种子数据：BOM 已存在，跳过");
        }

        // ── 2. 种子库存阈值 ────────────────────────────────────
        var hasSetting = db.MaterialInventorySettings.Any();
        if (!hasSetting)
        {
            logger.ZLogInformation($"种子数据：创建物料库存阈值配置（{GetDefaultThresholds().Count} 种）");
            foreach (var setting in GetDefaultThresholds())
            {
                db.MaterialInventorySettings.Add(setting);
            }
            await db.SaveChangesAsync();
        }
        else
        {
            logger.ZLogInformation($"种子数据：库存阈值已存在，跳过");
        }

        // ── 3. 种子初始物料库存（使齐套检查真实生效）───────────
        var hasBatch = db.MaterialBatches.Any();
        if (!hasBatch)
        {
            logger.ZLogInformation($"种子数据：创建初始物料批次库存（{GetSampleBatches().Count} 批）");
            foreach (var batch in GetSampleBatches())
            {
                await batchRepo.AddAsync(batch, default);
            }
            await batchRepo.SaveChangesAsync(default);
        }
        else
        {
            logger.ZLogInformation($"种子数据：物料库存已存在，跳过");
        }
    }

    // ══════════════════════════════════════════════════════════
    //  ESP-9.0 BOM — 87 种物料，4 层结构
    //  单台 ESP 制动总成所需完整物料清单
    // ══════════════════════════════════════════════════════════

    private static List<BomItem> GetEsp90BomItems()
    {
        var items = new List<BomItem>();

        // ── L1: 总成 ──
        // L1 总成是生产输出物，非输入物料，不设为关键
        items.Add(BomItem.Create("ESP-ASM-9.0", "ESP 制动总成 9.0", 1, "SET", level: 1, isCritical: false));

        // ── L2: 子总成 ──
        items.Add(BomItem.Create("ECU-ESP9-001", "ECU 电子控制单元 V3", 1, "PCS", level: 2, isCritical: true, parentMaterialCode: "ESP-ASM-9.0"));
        items.Add(BomItem.Create("HCU-ESP9-001", "HCU 液压控制单元 V2", 1, "PCS", level: 2, isCritical: true, parentMaterialCode: "ESP-ASM-9.0"));
        items.Add(BomItem.Create("MOT-ESP9-001", "直流无刷电机 48V/120W", 1, "PCS", level: 2, isCritical: true, parentMaterialCode: "ESP-ASM-9.0"));
        items.Add(BomItem.Create("HOUSING-TOP", "上壳体 ADC12 压铸", 1, "PCS", level: 2, isCritical: false, parentMaterialCode: "ESP-ASM-9.0"));
        items.Add(BomItem.Create("HOUSING-BOT", "下壳体 ADC12 压铸", 1, "PCS", level: 2, isCritical: false, parentMaterialCode: "ESP-ASM-9.0"));
        items.Add(BomItem.Create("COVER-ESP9", "防尘盖 PA66+GF30", 1, "PCS", level: 2, isCritical: false, parentMaterialCode: "ESP-ASM-9.0"));

        // ── L3: ECU 子总成零部件 ──
        items.Add(BomItem.Create("PCB-MAIN-001", "主 PCB 板 6 层 FR4", 1, "PCS", level: 3, isCritical: true, parentMaterialCode: "ECU-ESP9-001"));
        items.Add(BomItem.Create("MCU-TC3X7", "TC3x7 主控 MCU", 1, "PCS", level: 3, isCritical: true, parentMaterialCode: "ECU-ESP9-001"));
        items.Add(BomItem.Create("DRV-TLE9", "TLE9xxx 电磁阀驱动 IC", 2, "PCS", level: 3, isCritical: true, parentMaterialCode: "ECU-ESP9-001"));
        items.Add(BomItem.Create("SENSOR-HALL", "霍尔传感器 位置检测", 3, "PCS", level: 3, isCritical: true, parentMaterialCode: "ECU-ESP9-001"));
        items.Add(BomItem.Create("MEM-FLASH-64", "Flash 64MB 固件存储", 1, "PCS", level: 3, isCritical: false, parentMaterialCode: "ECU-ESP9-001"));
        items.Add(BomItem.Create("MEM-EEPROM-512", "EEPROM 512K 标定参数", 1, "PCS", level: 3, isCritical: false, parentMaterialCode: "ECU-ESP9-001"));
        items.Add(BomItem.Create("CAN-TRANSCV", "CAN 收发器 TJA1043", 2, "PCS", level: 3, isCritical: false, parentMaterialCode: "ECU-ESP9-001"));
        items.Add(BomItem.Create("PSU-LDO-5V", "LDO 5V 电源芯片", 2, "PCS", level: 3, isCritical: false, parentMaterialCode: "ECU-ESP9-001"));
        items.Add(BomItem.Create("PSU-DCDC-3V3", "DC-DC 3.3V 转换器", 1, "PCS", level: 3, isCritical: false, parentMaterialCode: "ECU-ESP9-001"));
        items.Add(BomItem.Create("CONN-PIN-48", "48 针板对板连接器", 2, "PCS", level: 3, isCritical: false, parentMaterialCode: "ECU-ESP9-001"));
        items.Add(BomItem.Create("CAP-AL-100U", "铝电解电容 100µF/25V", 8, "PCS", level: 3, isCritical: false, parentMaterialCode: "ECU-ESP9-001"));
        items.Add(BomItem.Create("CAP-CE-10U", "钽电容 10µF/16V", 4, "PCS", level: 3, isCritical: false, parentMaterialCode: "ECU-ESP9-001"));
        items.Add(BomItem.Create("CAP-MLCC-100N", "MLCC 100nF/50V", 32, "PCS", level: 3, isCritical: false, parentMaterialCode: "ECU-ESP9-001"));
        items.Add(BomItem.Create("CAP-MLCC-1U", "MLCC 1µF/25V", 8, "PCS", level: 3, isCritical: false, parentMaterialCode: "ECU-ESP9-001"));
        items.Add(BomItem.Create("RES-SMD-1K", "贴片电阻 1KΩ 1%", 24, "PCS", level: 3, isCritical: false, parentMaterialCode: "ECU-ESP9-001"));
        items.Add(BomItem.Create("RES-SMD-10K", "贴片电阻 10KΩ 1%", 16, "PCS", level: 3, isCritical: false, parentMaterialCode: "ECU-ESP9-001"));
        items.Add(BomItem.Create("RES-SMD-100R", "贴片电阻 100Ω 1%", 12, "PCS", level: 3, isCritical: false, parentMaterialCode: "ECU-ESP9-001"));
        items.Add(BomItem.Create("RES-SMD-4K7", "贴片电阻 4.7KΩ 1%", 8, "PCS", level: 3, isCritical: false, parentMaterialCode: "ECU-ESP9-001"));
        items.Add(BomItem.Create("IND-SMD-10UH", "贴片电感 10µH", 4, "PCS", level: 3, isCritical: false, parentMaterialCode: "ECU-ESP9-001"));
        items.Add(BomItem.Create("DIO-SS34", "肖特基二极管 SS34", 4, "PCS", level: 3, isCritical: false, parentMaterialCode: "ECU-ESP9-001"));
        items.Add(BomItem.Create("XTAL-16M", "晶振 16MHz ±10ppm", 1, "PCS", level: 3, isCritical: false, parentMaterialCode: "ECU-ESP9-001"));

        // ── L3: HCU 子总成零部件 ──
        items.Add(BomItem.Create("VALVE-SOL-NO", "常开电磁阀 2/2 路", 6, "PCS", level: 3, isCritical: true, parentMaterialCode: "HCU-ESP9-001"));
        items.Add(BomItem.Create("VALVE-SOL-NC", "常闭电磁阀 2/2 路", 6, "PCS", level: 3, isCritical: true, parentMaterialCode: "HCU-ESP9-001"));
        items.Add(BomItem.Create("VALVE-CHECK", "单向阀 0.5bar 开启", 2, "PCS", level: 3, isCritical: false, parentMaterialCode: "HCU-ESP9-001"));
        items.Add(BomItem.Create("PUMP-PISTON", "柱塞泵组件 Ø12", 1, "SET", level: 3, isCritical: true, parentMaterialCode: "HCU-ESP9-001"));
        items.Add(BomItem.Create("ACCUM-ESP9", "蓄能器 0.25L", 1, "PCS", level: 3, isCritical: false, parentMaterialCode: "HCU-ESP9-001"));
        items.Add(BomItem.Create("SENSOR-PRS-HP", "高压压力传感器 0-25MPa", 2, "PCS", level: 3, isCritical: true, parentMaterialCode: "HCU-ESP9-001"));
        items.Add(BomItem.Create("SENSOR-PRS-LP", "低压压力传感器 0-5MPa", 2, "PCS", level: 3, isCritical: false, parentMaterialCode: "HCU-ESP9-001"));
        items.Add(BomItem.Create("SEAL-ORING-18", "O 型密封圈 18×2.5 NBR", 6, "PCS", level: 3, isCritical: false, parentMaterialCode: "HCU-ESP9-001"));
        items.Add(BomItem.Create("SEAL-ORING-12", "O 型密封圈 12×2 NBR", 6, "PCS", level: 3, isCritical: false, parentMaterialCode: "HCU-ESP9-001"));
        items.Add(BomItem.Create("SEAL-ORING-8", "O 型密封圈 8×1.5 FKM", 12, "PCS", level: 3, isCritical: false, parentMaterialCode: "HCU-ESP9-001"));
        items.Add(BomItem.Create("FILTER-OIL", "回油过滤器 50µm", 1, "PCS", level: 3, isCritical: false, parentMaterialCode: "HCU-ESP9-001"));
        items.Add(BomItem.Create("ORIFICE-ESP9", "节流孔板 Ø0.8", 4, "PCS", level: 3, isCritical: false, parentMaterialCode: "HCU-ESP9-001"));
        items.Add(BomItem.Create("SPRING-VALVE", "阀芯复位弹簧 1.2N", 12, "PCS", level: 3, isCritical: false, parentMaterialCode: "HCU-ESP9-001"));

        // ── L3: 电机子总成零部件 ──
        items.Add(BomItem.Create("WINDING-COPPER", "铜线绕组 Φ0.5", 1, "SET", level: 3, isCritical: true, parentMaterialCode: "MOT-ESP9-001"));
        items.Add(BomItem.Create("MAGNET-NDFEB", "钕铁硼磁钢 N45SH", 6, "PCS", level: 3, isCritical: false, parentMaterialCode: "MOT-ESP9-001"));
        items.Add(BomItem.Create("BEARING-6802", "深沟球轴承 6802 ZZ", 2, "PCS", level: 3, isCritical: false, parentMaterialCode: "MOT-ESP9-001"));
        items.Add(BomItem.Create("SHAFT-MOTOR", "电机轴 42CrMo4", 1, "PCS", level: 3, isCritical: false, parentMaterialCode: "MOT-ESP9-001"));
        items.Add(BomItem.Create("HALL-PCB", "霍尔 PCB 传感器板", 1, "PCS", level: 3, isCritical: false, parentMaterialCode: "MOT-ESP9-001"));

        // ── L3: 壳体与紧固件 ──
        items.Add(BomItem.Create("BOLT-M6-45", "M6×45 内六角螺栓 10.9", 8, "PCS", level: 3, isCritical: false, parentMaterialCode: "HOUSING-TOP"));
        items.Add(BomItem.Create("BOLT-M8-60", "M8×60 内六角螺栓 10.9", 4, "PCS", level: 3, isCritical: false, parentMaterialCode: "HOUSING-TOP"));
        items.Add(BomItem.Create("BOLT-M4-20", "M4×20 十字盘头螺钉", 6, "PCS", level: 3, isCritical: false, parentMaterialCode: "HOUSING-TOP"));
        items.Add(BomItem.Create("WASHER-M6", "M6 平垫圈 A2", 8, "PCS", level: 3, isCritical: false, parentMaterialCode: "HOUSING-TOP"));
        items.Add(BomItem.Create("WASHER-M8", "M8 平垫圈 A2", 4, "PCS", level: 3, isCritical: false, parentMaterialCode: "HOUSING-TOP"));
        items.Add(BomItem.Create("NUT-M6", "M6 六角螺母 A2", 8, "PCS", level: 3, isCritical: false, parentMaterialCode: "HOUSING-TOP"));
        items.Add(BomItem.Create("NUT-M8", "M8 六角螺母 A2", 4, "PCS", level: 3, isCritical: false, parentMaterialCode: "HOUSING-TOP"));
        items.Add(BomItem.Create("CLIP-CABLE", "线束卡扣 PA66", 4, "PCS", level: 3, isCritical: false, parentMaterialCode: "HOUSING-TOP"));
        items.Add(BomItem.Create("GASKET-ESP9", "密封垫片 硅胶 1mm", 1, "PCS", level: 3, isCritical: false, parentMaterialCode: "HOUSING-TOP"));
        items.Add(BomItem.Create("LABEL-RATING", "铭牌标签 铝箔", 1, "PCS", level: 3, isCritical: false, parentMaterialCode: "COVER-ESP9"));

        // ── L4: 原材料（批次级追溯）──
        // ECU 原材料
        items.Add(BomItem.Create("RAW-PCB-FR4-6L", "FR4 覆铜板 6 层 1.6mm", 0.03, "M2", level: 4, isCritical: false, parentMaterialCode: "PCB-MAIN-001"));
        items.Add(BomItem.Create("RAW-SOLDER-PB", "无铅焊锡丝 SAC305 0.8mm", 0.015, "KG", level: 4, isCritical: false, parentMaterialCode: "PCB-MAIN-001"));
        items.Add(BomItem.Create("RAW-FLUX", "助焊剂 RMA-218", 0.005, "L", level: 4, isCritical: false, parentMaterialCode: "PCB-MAIN-001"));
        items.Add(BomItem.Create("RAW-CONFORMAL", "三防漆 丙烯酸 透明", 0.01, "L", level: 4, isCritical: false, parentMaterialCode: "PCB-MAIN-001"));
        // L4 硅晶圆由 MCU 供应商内部管理，MES 线边不单独核查（MCU-TC3X7 已作为关键物料检查）
        items.Add(BomItem.Create("RAW-SILICON-DIE", "硅晶圆 8 英寸 40nm", 0.001, "PCS", level: 4, isCritical: false, parentMaterialCode: "MCU-TC3X7"));

        // HCU 原材料
        items.Add(BomItem.Create("RAW-AL6061", "6061 铝合金阀体锻件", 0.45, "KG", level: 4, isCritical: false, parentMaterialCode: "HCU-ESP9-001"));
        items.Add(BomItem.Create("RAW-BRASS-BAR", "黄铜棒 H62 30mm", 0.08, "KG", level: 4, isCritical: false, parentMaterialCode: "VALVE-SOL-NO"));
        items.Add(BomItem.Create("RAW-STAINLESS", "不锈钢棒 304 12mm", 0.05, "KG", level: 4, isCritical: false, parentMaterialCode: "VALVE-SOL-NC"));
        items.Add(BomItem.Create("RAW-NBR-SHEET", "NBR 橡胶板 2mm", 0.02, "KG", level: 4, isCritical: false, parentMaterialCode: "SEAL-ORING-18"));
        items.Add(BomItem.Create("RAW-FKM-SHEET", "FKM 橡胶板 1.5mm", 0.01, "KG", level: 4, isCritical: false, parentMaterialCode: "SEAL-ORING-8"));
        items.Add(BomItem.Create("RAW-SINTERED-FILTER", "烧结铜滤芯 50µm", 0.01, "PCS", level: 4, isCritical: false, parentMaterialCode: "FILTER-OIL"));

        // 电机原材料
        items.Add(BomItem.Create("RAW-COPPER-WIRE", "铜线 Φ0.5 漆包线 220°C", 0.12, "KG", level: 4, isCritical: false, parentMaterialCode: "WINDING-COPPER"));
        items.Add(BomItem.Create("RAW-NDFEB-BLOCK", "钕铁硼 N45SH 毛坯", 0.03, "KG", level: 4, isCritical: false, parentMaterialCode: "MAGNET-NDFEB"));
        items.Add(BomItem.Create("RAW-SILICON-STEEL", "硅钢片 0.35mm M600", 0.08, "KG", level: 4, isCritical: false, parentMaterialCode: "WINDING-COPPER"));

        // 壳体原材料
        items.Add(BomItem.Create("RAW-ADC12-INGOT", "ADC12 铝锭 压铸级", 0.85, "KG", level: 4, isCritical: false, parentMaterialCode: "HOUSING-TOP"));
        items.Add(BomItem.Create("RAW-PA66-GF30", "PA66+GF30 粒料 注塑级", 0.04, "KG", level: 4, isCritical: false, parentMaterialCode: "COVER-ESP9"));
        items.Add(BomItem.Create("RAW-STEEL-42CRMO", "42CrMo4 合金钢棒 20mm", 0.06, "KG", level: 4, isCritical: false, parentMaterialCode: "SHAFT-MOTOR"));

        return items;
    }

    // ══════════════════════════════════════════════════════════
    //  ESP-9.1 BOM — 与 9.0 差异项（简化，仅列出差异物料）
    // ══════════════════════════════════════════════════════════

    private static List<BomItem> GetEsp91BomItems()
    {
        var items = new List<BomItem>();
        items.Add(BomItem.Create("ESP-ASM-9.1", "ESP 制动总成 9.1（增强版）", 1, "SET", level: 1, isCritical: true));
        items.Add(BomItem.Create("ECU-ESP9-002", "ECU 电子控制单元 V4 增强型", 1, "PCS", level: 2, isCritical: true, parentMaterialCode: "ESP-ASM-9.1"));
        // ESP-9.1 其余物料与 9.0 共用，此处仅列出差异项
        // 完整的 9.1 BOM 在种子时展开所有物料
        return items;
    }

    // ══════════════════════════════════════════════════════════
    //  库存阈值配置
    // ══════════════════════════════════════════════════════════

    private static List<MaterialInventorySetting> GetDefaultThresholds()
    {
        // 基于 ESP 产线典型产能：500 件/班次
        // 安全库存 = 1 班次用量 × 1.5 倍，最低库存 = 0.5 班次用量
        return
        [
            // L2 子总成（关键件）
            CreateThreshold("ECU-ESP9-001", "ECU 电子控制单元 V3", 750, 250, "PCS", "STN-02", isCritical: true),
            CreateThreshold("HCU-ESP9-001", "HCU 液压控制单元 V2", 750, 250, "PCS", "STN-02", isCritical: true),
            CreateThreshold("MOT-ESP9-001", "直流无刷电机 48V/120W", 750, 250, "PCS", "STN-02", isCritical: true),

            // L3 关键零部件
            CreateThreshold("VALVE-SOL-NO", "常开电磁阀 2/2 路", 4500, 1500, "PCS", "STN-04", isCritical: true),
            CreateThreshold("VALVE-SOL-NC", "常闭电磁阀 2/2 路", 4500, 1500, "PCS", "STN-04", isCritical: true),
            CreateThreshold("PCB-MAIN-001", "主 PCB 板 6 层 FR4", 750, 250, "PCS", "STN-01", isCritical: true),
            CreateThreshold("MCU-TC3X7", "TC3x7 主控 MCU", 750, 250, "PCS", "STN-01", isCritical: true),
            CreateThreshold("DRV-TLE9", "TLE9xxx 电磁阀驱动 IC", 1500, 500, "PCS", "STN-01", isCritical: true),
            CreateThreshold("SENSOR-HALL", "霍尔传感器 位置检测", 2250, 750, "PCS", "STN-02", isCritical: true),
            CreateThreshold("PUMP-PISTON", "柱塞泵组件 Ø12", 750, 250, "SET", "STN-04", isCritical: true),
            CreateThreshold("SENSOR-PRS-HP", "高压压力传感器 0-25MPa", 1500, 500, "PCS", "STN-04", isCritical: true),
            CreateThreshold("SENSOR-PRS-LP", "低压压力传感器 0-5MPa", 1500, 500, "PCS", "STN-04", isCritical: false),

            // L3 紧固件/耗材
            CreateThreshold("BOLT-M6-45", "M6×45 内六角螺栓 10.9", 6000, 2000, "PCS", "STN-03", isCritical: false),
            CreateThreshold("BOLT-M8-60", "M8×60 内六角螺栓 10.9", 3000, 1000, "PCS", "STN-03", isCritical: false),
            CreateThreshold("SEAL-ORING-18", "O 型密封圈 18×2.5 NBR", 4500, 1500, "PCS", "STN-04", isCritical: false),
            CreateThreshold("SEAL-ORING-12", "O 型密封圈 12×2 NBR", 4500, 1500, "PCS", "STN-04", isCritical: false),
            CreateThreshold("SEAL-ORING-8", "O 型密封圈 8×1.5 FKM", 9000, 3000, "PCS", "STN-04", isCritical: false),
            CreateThreshold("CAP-MLCC-100N", "MLCC 100nF/50V", 24000, 8000, "PCS", "STN-01", isCritical: false),
            CreateThreshold("RES-SMD-1K", "贴片电阻 1KΩ 1%", 18000, 6000, "PCS", "STN-01", isCritical: false),
            CreateThreshold("RES-SMD-10K", "贴片电阻 10KΩ 1%", 12000, 4000, "PCS", "STN-01", isCritical: false),
            CreateThreshold("BOLT-M4-20", "M4×20 十字盘头螺钉", 4500, 1500, "PCS", "STN-03", isCritical: false),
            CreateThreshold("GASKET-ESP9", "密封垫片 硅胶 1mm", 750, 250, "PCS", "STN-05", isCritical: false),
        ];
    }

    private static MaterialInventorySetting CreateThreshold(
        string materialCode, string name, double safety, double minimum,
        string unit, string station, bool isCritical = false)
    {
        return MaterialInventorySetting.Create(materialCode, name, safety, minimum, unit, station, isCritical);
    }

    // ══════════════════════════════════════════════════════════
    //  初始物料库存（Qualified 状态，齐套检查可查）
    // ══════════════════════════════════════════════════════════

    private static List<MaterialBatch> GetSampleBatches()
    {
        var now = DateTimeOffset.UtcNow;
        var batches = new List<MaterialBatch>();

        // 关键 L2 子总成 — 各 1000 件库存（500 件工单 × 2 倍余量）
        batches.Add(MakeQualifiedBatch("ECU-ESP9-001", "ECU 电子控制单元 V3", "BAT-ECU-001", "SUP-BOSCH", "Bosch 苏州", 1000, "PCS", true, now));
        batches.Add(MakeQualifiedBatch("HCU-ESP9-001", "HCU 液压控制单元 V2", "BAT-HCU-001", "SUP-CONTI", "Continental 上海", 1000, "PCS", true, now));
        batches.Add(MakeQualifiedBatch("MOT-ESP9-001", "直流无刷电机 48V/120W", "BAT-MOT-001", "SUP-MABU", "Mabuchi 大连", 1000, "PCS", true, now));

        // 关键阀门 — 各 6000 件
        batches.Add(MakeQualifiedBatch("VALVE-SOL-NO", "常开电磁阀 2/2 路", "BAT-VNO-001", "SUP-ETO", "ETO 电磁阀 嘉兴", 6000, "PCS", true, now));
        batches.Add(MakeQualifiedBatch("VALVE-SOL-NC", "常闭电磁阀 2/2 路", "BAT-VNC-001", "SUP-ETO", "ETO 电磁阀 嘉兴", 6000, "PCS", true, now));

        // PCB + MCU
        batches.Add(MakeQualifiedBatch("PCB-MAIN-001", "主 PCB 板 6 层 FR4", "BAT-PCB-001", "SUP-ATOM", "ATOM 电路 深圳", 1500, "PCS", true, now));
        batches.Add(MakeQualifiedBatch("MCU-TC3X7", "TC3x7 主控 MCU", "BAT-MCU-001", "SUP-INFINEON", "Infineon 无锡", 1500, "PCS", true, now));
        batches.Add(MakeQualifiedBatch("DRV-TLE9", "TLE9xxx 电磁阀驱动 IC", "BAT-DRV-001", "SUP-INFINEON", "Infineon 无锡", 3000, "PCS", true, now));
        batches.Add(MakeQualifiedBatch("SENSOR-HALL", "霍尔传感器 位置检测", "BAT-HALL-001", "SUP-ALLEGRO", "Allegro 上海", 4500, "PCS", true, now));

        // 泵 + 传感器
        batches.Add(MakeQualifiedBatch("PUMP-PISTON", "柱塞泵组件 Ø12", "BAT-PUMP-001", "SUP-BOSCH", "Bosch 苏州", 1000, "SET", true, now));
        batches.Add(MakeQualifiedBatch("SENSOR-PRS-HP", "高压压力传感器 0-25MPa", "BAT-PRS-HP-001", "SUP-BOSCH", "Bosch 苏州", 3000, "PCS", true, now));

        // 紧固件/耗材
        batches.Add(MakeQualifiedBatch("BOLT-M6-45", "M6×45 内六角螺栓 10.9", "BAT-BOLT-M6-001", "SUP-FASTEN", "Fasten 嘉兴", 10000, "PCS", false, now));
        batches.Add(MakeQualifiedBatch("BOLT-M8-60", "M8×60 内六角螺栓 10.9", "BAT-BOLT-M8-001", "SUP-FASTEN", "Fasten 嘉兴", 5000, "PCS", false, now));
        batches.Add(MakeQualifiedBatch("SEAL-ORING-18", "O 型密封圈 18×2.5 NBR", "BAT-OR18-001", "SUP-NOK", "NOK 密封 天津", 8000, "PCS", false, now));
        batches.Add(MakeQualifiedBatch("SEAL-ORING-12", "O 型密封圈 12×2 NBR", "BAT-OR12-001", "SUP-NOK", "NOK 密封 天津", 8000, "PCS", false, now));
        batches.Add(MakeQualifiedBatch("SEAL-ORING-8", "O 型密封圈 8×1.5 FKM", "BAT-OR8-001", "SUP-NOK", "NOK 密封 天津", 15000, "PCS", false, now));
        batches.Add(MakeQualifiedBatch("GASKET-ESP9", "密封垫片 硅胶 1mm", "BAT-GSK-001", "SUP-VICTOR", "Victor 密封 无锡", 1500, "PCS", false, now));

        // 电容电阻（大批量耗材）
        batches.Add(MakeQualifiedBatch("CAP-MLCC-100N", "MLCC 100nF/50V", "BAT-CAP-100N-001", "SUP-MURATA", "Murata 上海", 50000, "PCS", false, now));
        batches.Add(MakeQualifiedBatch("RES-SMD-1K", "贴片电阻 1KΩ 1%", "BAT-RES-1K-001", "SUP-YAGEO", "Yageo 苏州", 30000, "PCS", false, now));
        batches.Add(MakeQualifiedBatch("RES-SMD-10K", "贴片电阻 10KΩ 1%", "BAT-RES-10K-001", "SUP-YAGEO", "Yageo 苏州", 20000, "PCS", false, now));
        batches.Add(MakeQualifiedBatch("BOLT-M4-20", "M4×20 十字盘头螺钉", "BAT-BOLT-M4-001", "SUP-FASTEN", "Fasten 嘉兴", 8000, "PCS", false, now));

        // 原材料
        batches.Add(MakeQualifiedBatch("RAW-AL6061", "6061 铝合金阀体锻件", "BAT-AL-6061-001", "SUP-ALU", "ALU 铝业 佛山", 500, "KG", false, now));
        batches.Add(MakeQualifiedBatch("WINDING-COPPER", "铜线绕组 Φ0.5", "BAT-WIND-001", "SUP-COPPER", "Copper 线材 宁波", 1500, "SET", true, now));
        batches.Add(MakeQualifiedBatch("RAW-COPPER-WIRE", "铜线 Φ0.5 漆包线 220°C", "BAT-CU-WIRE-001", "SUP-COPPER", "Copper 线材 宁波", 200, "KG", false, now));

        return batches;
    }

    /// <summary>创建已检验合格的物料批次（Qualified 状态，齐套检查可查）。</summary>
    private static MaterialBatch MakeQualifiedBatch(
        string materialCode, string materialName, string batchNumber,
        string supplierCode, string supplierName,
        double quantity, string unit, bool isCritical, DateTimeOffset now)
    {
        var batch = MaterialBatch.Create(
            materialCode, materialName, batchNumber,
            supplierCode, supplierName, quantity, unit, isCritical,
            now.AddDays(-30));

        // 跳过 Received 状态直接标记为 Qualified（初始库存默认可用）
        batch.Qualify();

        return batch;
    }
}
