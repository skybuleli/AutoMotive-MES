using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Security;

namespace MesAdmin.Api.Features.Auth.Login;

public class LoginEndpoint : Endpoint<LoginRequest, LoginResponse>
{
    public required ITokenService TokenService { get; set; }

    private static readonly Dictionary<string, (string Name, string[] Roles)> DemoUsers = new()
    {
        ["manager"] = ("张经理", [MesRoles.ProductionManager]),
        ["leader"] = ("李班长", [MesRoles.ShiftLeader]),
        ["qe"] = ("王质量", [MesRoles.QualityEngineer]),
        ["ee"] = ("赵设备", [MesRoles.EquipmentEngineer]),
        ["warehouse"] = ("孙仓库", [MesRoles.WarehouseClerk]),
        ["sqe"] = ("周SQE", [MesRoles.SupplierQualityEngineer]),
    };

    public override void Configure()
    {
        Post("/api/auth/login");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "用户登录";
            s.Description = "验证凭据并签发 JWT（骨架版：硬编码演示用户）";
        });
    }

    public override async Task HandleAsync(LoginRequest req, CancellationToken ct)
    {
        if (!DemoUsers.TryGetValue(req.Username, out var user))
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var token = TokenService.GenerateToken(req.Username, user.Name, user.Roles);
        Response = new LoginResponse(token, user.Name, user.Roles);
        await Send.OkAsync(Response, ct);
    }
}

[MemoryPackable]
public partial record LoginRequest(string Username, string Password);

[MemoryPackable]
public partial record LoginResponse(string Token, string User, string[] Roles);
