using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MesAdmin.Infrastructure.Data.Converters;

/// <summary>
/// Ulid → Guid 转换器（ValueConverter）。
/// Ulid 128-bit 可排序 UUID，前 48 bit 时间戳 → B+Tree 友好。
/// 禁止 Guid.NewGuid / 自增 ID（分布式不安全 / 索引碎片）。
/// </summary>
public class UlidToGuidConverter : ValueConverter<Ulid, Guid>
{
    public UlidToGuidConverter()
        : base(
            ulid => ulid.ToGuid(),
            guid => new Ulid(guid))
    {
    }
}
