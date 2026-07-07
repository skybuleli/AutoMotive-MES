using FastEndpoints;

namespace MesAdmin.Api.Features.Scheduling;

/// <summary>排程管理端点组（api/v1）</summary>
public class SchedulingGroup : Group
{
    public SchedulingGroup() => Configure("api/v1", ep => { });
}
