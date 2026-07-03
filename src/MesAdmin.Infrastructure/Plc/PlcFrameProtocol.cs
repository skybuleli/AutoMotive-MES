using System.Buffers;
using System.Runtime.InteropServices;
using MesAdmin.Domain.Models;

namespace MesAdmin.Infrastructure.Plc;

/// <summary>
/// PLC 帧协议常量（T2.12）。
/// 帧格式：[帧头 0x55 0xAA][设备码 8B][状态 1B][循环计数 8B][合格 8B][不良 8B][运行时长 8B][过程值 8B][标签 16B][帧尾 0x0D 0x0A]
/// 总长 = 2 + 8 + 1 + 8 + 8 + 8 + 8 + 8 + 16 + 2 = 69 字节
/// 零分配解析：SearchValues SIMD 帧头扫描 + MemoryMarshal.Read 原生读取，禁止 byte[] + BitConverter。
/// </summary>
public static class PlcFrameProtocol
{
    public const byte Header0 = 0x55;
    public const byte Header1 = 0xAA;
    public const byte Tail0 = 0x0D;
    public const byte Tail1 = 0x0A;

    /// <summary>帧总长度（字节）</summary>
    public const int FrameLength = 69;

    /// <summary>帧头偏移</summary>
    public const int HeaderOffset = 0;
    public const int EquipmentCodeOffset = 2;       // 8 字节 ASCII
    public const int StatusOffset = 10;              // 1 字节
    public const int CycleCountOffset = 11;          // 8 字节 little-endian
    public const int GoodCountOffset = 19;           // 8 字节
    public const int DefectCountOffset = 27;         // 8 字节
    public const int RunTimeOffset = 35;             // 8 字节
    public const int ProcessValueOffset = 43;        // 8 字节 double
    public const int ProcessTagOffset = 51;          // 16 字节 ASCII
    public const int TailOffset = 67;                // 2 字节

    /// <summary>
    /// SearchValues 帧头扫描器（SIMD 加速）。
    /// 用于在 PipeReader 缓冲区中定位帧起始位置。
    /// </summary>
    public static readonly SearchValues<byte> FrameHeaderSearch =
        SearchValues.Create([Header0, Header1]);

    /// <summary>
    /// 验证帧头是否匹配（0x55 0xAA）。
    /// </summary>
    public static bool IsHeader(ReadOnlySpan<byte> buffer)
        => buffer.Length >= 2 && buffer[0] == Header0 && buffer[1] == Header1;

    /// <summary>
    /// 验证帧尾是否匹配（0x0D 0x0A）。
    /// </summary>
    public static bool IsTail(ReadOnlySpan<byte> buffer)
        => buffer.Length >= 2 && buffer[0] == Tail0 && buffer[1] == Tail1;
}

/// <summary>
/// 零分配 PLC 帧解析器（ref struct，禁止装箱）。
/// 直接在 ReadOnlySpan&lt;byte&gt; 上读取，禁止 byte[] + BitConverter 分配版本（AGENTS.md 4.3）。
/// </summary>
public ref struct PlcFrameReader
{
    private readonly ReadOnlySpan<byte> _frame;

    /// <summary>从完整帧缓冲区构造读取器（长度必须 = FrameLength）</summary>
    public PlcFrameReader(ReadOnlySpan<byte> frame)
    {
        if (frame.Length != PlcFrameProtocol.FrameLength)
            throw new ArgumentException($"帧长度必须为 {PlcFrameProtocol.FrameLength}，实际 {frame.Length}", nameof(frame));
        _frame = frame;
    }

    /// <summary>设备编码（8 字节 ASCII，去除尾部 \0）</summary>
    public string EquipmentCode => ReadAscii(PlcFrameProtocol.EquipmentCodeOffset, 8);

    /// <summary>设备状态（1 字节）</summary>
    public EquipmentStatus Status => (EquipmentStatus)_frame[PlcFrameProtocol.StatusOffset];

    /// <summary>循环次数（8 字节 little-endian int64，MemoryMarshal 零分配读取）</summary>
    public long CycleCount => MemoryMarshal.Read<long>(
        _frame.Slice(PlcFrameProtocol.CycleCountOffset, 8));

    /// <summary>合格件数</summary>
    public long GoodCount => MemoryMarshal.Read<long>(
        _frame.Slice(PlcFrameProtocol.GoodCountOffset, 8));

    /// <summary>不良件数</summary>
    public long DefectCount => MemoryMarshal.Read<long>(
        _frame.Slice(PlcFrameProtocol.DefectCountOffset, 8));

    /// <summary>运行时长（毫秒）</summary>
    public long RunTimeMs => MemoryMarshal.Read<long>(
        _frame.Slice(PlcFrameProtocol.RunTimeOffset, 8));

    /// <summary>过程值（8 字节 double，MemoryMarshal 零分配读取）</summary>
    public double ProcessValue => MemoryMarshal.Read<double>(
        _frame.Slice(PlcFrameProtocol.ProcessValueOffset, 8));

    /// <summary>过程标签（16 字节 ASCII，去除尾部 \0）</summary>
    public string ProcessTag => ReadAscii(PlcFrameProtocol.ProcessTagOffset, 16);

    /// <summary>
    /// 验证帧头 + 帧尾完整性。
    /// </summary>
    public bool Validate()
        => PlcFrameProtocol.IsHeader(_frame)
           && _frame[PlcFrameProtocol.TailOffset] == PlcFrameProtocol.Tail0
           && _frame[PlcFrameProtocol.TailOffset + 1] == PlcFrameProtocol.Tail1;

    /// <summary>尝试解析帧。失败返回 false（帧头/帧尾不匹配）。</summary>
    public bool TryParse(out PlcSnapshot snapshot)
    {
        if (!Validate())
        {
            snapshot = default!;
            return false;
        }

        snapshot = PlcSnapshot.Create(
            EquipmentCode,
            DateTimeOffset.UtcNow,
            Status,
            CycleCount,
            GoodCount,
            DefectCount,
            RunTimeMs,
            ProcessValue,
            ProcessTag);
        return true;
    }

    /// <summary>读取 ASCII 字段并去除尾部 \0 填充</summary>
    private string ReadAscii(int offset, int length)
    {
        var slice = _frame.Slice(offset, length);
        var end = slice.IndexOf((byte)0);
        return end >= 0
            ? System.Text.Encoding.ASCII.GetString(slice[..end])
            : System.Text.Encoding.ASCII.GetString(slice);
    }
}

/// <summary>
/// 帧解析结果（非 ref struct，可跨方法传递）。
/// </summary>
public readonly record struct PlcFrameData(
    string EquipmentCode,
    EquipmentStatus Status,
    long CycleCount,
    long GoodCount,
    long DefectCount,
    long RunTimeMs,
    double ProcessValue,
    string ProcessTag);
