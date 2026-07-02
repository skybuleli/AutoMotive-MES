using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.Inspections;

/// <summary>查询首件检验详情。</summary>
[MemoryPackable]
public sealed partial record GetInspectionByIdQuery(Ulid InspectionId) : ICommand<FirstArticleInspection?>;

internal sealed class GetInspectionByIdHandler(
    IFirstArticleInspectionRepository repo) : ICommandHandler<GetInspectionByIdQuery, FirstArticleInspection?>
{
    public Task<FirstArticleInspection?> ExecuteAsync(GetInspectionByIdQuery query, CancellationToken ct)
        => repo.GetByIdAsync(query.InspectionId, ct);
}
