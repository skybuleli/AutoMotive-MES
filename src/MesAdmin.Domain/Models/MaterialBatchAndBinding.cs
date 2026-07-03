using System.Globalization;
using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// GS1-128 条码解析结果（T1.12 来料扫码入库）。
/// GS1-128 格式：(01)物料编码14位(10)批次号(11)生产日期YYMMDD(37)数量。
/// 使用 ReadOnlySpan&lt;char&gt; 零分配切片，禁止 Substring（AGENTS.md 零分配铁律）。
/// </summary>
[MemoryPackable]
public partial record Gs1Barcode(
    string MaterialCode,
    string BatchNumber,
    int Quantity,
    DateTimeOffset? ProductionDate)
{
    /// <summary>
    /// 解析 GS1-128 条码（零分配，使用 ReadOnlySpan&lt;char&gt;）。
    /// 支持带 AI 标识符的标准格式：(01)12345678901234(10)BATCH001(11)260703(37)500。
    /// 也支持纯连格式：01-12345678901234-10-BATCH001-11-260703-37-500。
    /// </summary>
    public static Gs1Barcode Parse(ReadOnlySpan<char> barcode)
    {
        if (barcode.IsEmpty)
            throw new ArgumentException("条码不能为空");

        string materialCode = string.Empty;
        string batchNumber = string.Empty;
        int quantity = 0;
        DateTimeOffset? productionDate = null;

        // GS1-128 AI 标识符：01=GTIN(14位) / 10=批次号 / 11=生产日期YYMMDD / 37=数量
        var span = barcode.Trim();
        var pos = 0;

        while (pos < span.Length)
        {
            // 跳过分隔符 ( ) -
            while (pos < span.Length && (span[pos] == '(' || span[pos] == ')' || span[pos] == '-'))
                pos++;

            if (pos >= span.Length) break;

            // 读取 AI 标识符（2位数字）
            if (pos + 2 > span.Length) break;
            var ai = span.Slice(pos, 2);
            pos += 2;

            // 跳过分隔符（AI 后的 ) 或 - ）
            while (pos < span.Length && (span[pos] == ')' || span[pos] == '-' ))
                pos++;

            if (ai.SequenceEqual("01")) // GTIN 物料编码（14位）
            {
                if (pos + 14 > span.Length) break;
                materialCode = new string(span.Slice(pos, 14));
                pos += 14;
            }
            else if (ai.SequenceEqual("10")) // 批次号（变长，到下一个 ( 或 ) 或末尾）
            {
                var start = pos;
                while (pos < span.Length && span[pos] != '(' && span[pos] != ')' && span[pos] != '-')
                    pos++;
                batchNumber = new string(span.Slice(start, pos - start));
            }
            else if (ai.SequenceEqual("11")) // 生产日期 YYMMDD
            {
                if (pos + 6 <= span.Length)
                {
                    var dateStr = span.Slice(pos, 6);
                    if (TryParseGs1Date(dateStr, out var date))
                        productionDate = date;
                    pos += 6;
                }
            }
            else if (ai.SequenceEqual("37")) // 数量
            {
                var start = pos;
                while (pos < span.Length && char.IsDigit(span[pos]))
                    pos++;
                if (pos > start && int.TryParse(span.Slice(start, pos - start), NumberStyles.None, CultureInfo.InvariantCulture, out var q))
                    quantity = q;
            }
            else
            {
                // 未知 AI，跳过到下一个分隔符
                while (pos < span.Length && span[pos] != '(' && span[pos] != ')' && span[pos] != '-')
                    pos++;
            }
        }

        if (string.IsNullOrWhiteSpace(materialCode))
            throw new ArgumentException("GS1-128 条码缺少物料编码(AI 01)");

        if (string.IsNullOrWhiteSpace(batchNumber))
            throw new ArgumentException("GS1-128 条码缺少批次号(AI 10)");

        return new Gs1Barcode(materialCode, batchNumber, quantity, productionDate);
    }

    /// <summary>解析 GS1-128 日期 YYMMDD（年份取 20YY）</summary>
    private static bool TryParseGs1Date(ReadOnlySpan<char> dateStr, out DateTimeOffset result)
    {
        result = default;
        if (dateStr.Length != 6) return false;

        if (!int.TryParse(dateStr.Slice(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var yy)) return false;
        if (!int.TryParse(dateStr.Slice(2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var mm)) return false;
        if (!int.TryParse(dateStr.Slice(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var dd)) return false;

        if (mm is < 1 or > 12 || dd is < 1 or > 31) return false;

        try
        {
            result = new DateTimeOffset(2000 + yy, mm, dd, 0, 0, 0, TimeSpan.Zero);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// 物料投料绑定（T1.15 投料批次绑定）。
/// 操作员扫码绑定：物料编码+批次号 → 工单号 → 产品 S/N。
/// 写入 material_bindings 表，关联追溯链 traceability_links。
/// </summary>
[MemoryPackable]
public partial class MaterialBinding
{
    public Ulid Id { get; set; }

    /// <summary>所属工单 Id</summary>
    public Ulid OrderId { get; set; }

    /// <summary>物料批次 Id</summary>
    public Ulid MaterialBatchId { get; set; }

    /// <summary>物料编码</summary>
    public string MaterialCode { get; set; } = string.Empty;

    /// <summary>批次号</summary>
    public string BatchNumber { get; set; } = string.Empty;

    /// <summary>产品序列号（ESP 总成 S/N）</summary>
    public string ProductSerial { get; set; } = string.Empty;

    /// <summary>投料数量</summary>
    public double Quantity { get; set; }

    /// <summary>是否 Poka-Yoke 校验通过（BOM 比对）</summary>
    public bool PokaYokePassed { get; set; }

    /// <summary>操作员工号</summary>
    public string OperatorId { get; set; } = string.Empty;

    /// <summary>绑定时间</summary>
    public DateTimeOffset BoundAt { get; set; }

    public static MaterialBinding Create(
        Ulid orderId,
        Ulid materialBatchId,
        string materialCode,
        string batchNumber,
        string productSerial,
        double quantity,
        bool pokaYokePassed,
        string operatorId)
    {
        if (string.IsNullOrWhiteSpace(materialCode))
            throw new ArgumentException("物料编码不能为空", nameof(materialCode));
        if (string.IsNullOrWhiteSpace(batchNumber))
            throw new ArgumentException("批次号不能为空", nameof(batchNumber));
        if (string.IsNullOrWhiteSpace(productSerial))
            throw new ArgumentException("产品序列号不能为空", nameof(productSerial));
        if (string.IsNullOrWhiteSpace(operatorId))
            throw new ArgumentException("操作员工号不能为空", nameof(operatorId));
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "投料数量必须大于 0");

        return new MaterialBinding
        {
            Id = Ulid.NewUlid(),
            OrderId = orderId,
            MaterialBatchId = materialBatchId,
            MaterialCode = materialCode.Trim(),
            BatchNumber = batchNumber.Trim(),
            ProductSerial = productSerial.Trim(),
            Quantity = quantity,
            PokaYokePassed = pokaYokePassed,
            OperatorId = operatorId.Trim(),
            BoundAt = DateTimeOffset.UtcNow,
        };
    }
}
