namespace MesAdmin.Application.Common;

/// <summary>
/// 通用分页结果。替代元组返回，使命令/查询返回类型可被 FastEndpoints 正确解析。
/// </summary>
public sealed record PagedResult<T>(List<T> Items, int Total);
