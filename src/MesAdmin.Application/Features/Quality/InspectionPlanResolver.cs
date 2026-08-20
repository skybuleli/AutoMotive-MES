using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.Quality;

/// <summary>
/// 检验计划解析：根据请求中的计划 Id 解析计划并生成特性集。
/// 规则：
/// - 提供有效计划 Id → 复制计划特性；
/// - 无有效计划（空/无效）→ 返回 Ulid.Empty 哨兵 + 内置回退模板。
/// 禁止生成随机 Ulid.NewUlid()：会伪造不存在的计划引用（幽灵 planId）。
/// </summary>
public static class InspectionPlanResolver
{
    public static async Task<(Ulid PlanId, List<MeasuredCharacteristic> Characteristics)> ResolveAsync(
        string? rawPlanId,
        IInspectionPlanRepository planRepo,
        Func<List<MeasuredCharacteristic>> fallbackTemplate,
        CancellationToken ct = default)
    {
        List<MeasuredCharacteristic> characteristics = [];

        if (!string.IsNullOrWhiteSpace(rawPlanId) && Ulid.TryParse(rawPlanId, out var parsed))
        {
            var plan = await planRepo.GetByIdAsync(parsed, ct);
            if (plan is not null)
            {
                characteristics = plan.Characteristics.Select(c => MeasuredCharacteristic.Create(
                    c.CharacteristicCode, c.CharacteristicName, c.StandardValue, c.Unit,
                    c.UpperSpecLimit, c.LowerSpecLimit)).ToList();
                return (parsed, characteristics);
            }
        }

        return (Ulid.Empty, fallbackTemplate());
    }
}