using FastEndpoints;
using MesAdmin.Application.Security;
using MesAdmin.Api.Infrastructure;

namespace MesAdmin.Api.Features.Users;

/// <summary>/api/v1/users 路由组：用户管理（仅生产经理）。</summary>
public class UsersGroup : Group
{
    public UsersGroup()
    {
        Configure("api/v1/users", ep => ep.Roles(MesRoles.ProductionManager));
    }
}
