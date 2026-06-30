using MemoryPack;
using MemoryPack.AspNetCoreMvcFormatter;
using Microsoft.EntityFrameworkCore;
using MesAdmin.Application.Security;
using MesAdmin.Infrastructure.Data;
using MesAdmin.Infrastructure.Logging;
using MesAdmin.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

// ── REST API（Avalonia 工位终端用）──
builder.Services.AddControllers(options =>
{
    // 支持 Accept: application/x-memorypack 二进制双协议
    options.InputFormatters.Insert(0, new MemoryPackInputFormatter());
    options.OutputFormatters.Insert(0, new MemoryPackOutputFormatter());
});

builder.Services.AddDbContext<MesDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSQL")));

// ── JWT 认证 + 6 角色 RBAC（T0.8）──
builder.Services.AddMesJwtAuthentication(builder.Configuration);

// ── ZLogger 结构化日志 ──
builder.Logging.AddZLogger();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// ── 登录端点：验证凭据并签发 JWT（骨架版，正式用户存储待后续）──
app.MapPost("/api/auth/login", (LoginRequest req, ITokenService tokenService) =>
{
    // 骨架版：硬编码演示用户，正式实现应查数据库 + 密码哈希校验
    var demoUsers = new Dictionary<string, (string Name, string[] Roles)>
    {
        ["manager"] = ("张经理", [MesRoles.ProductionManager]),
        ["leader"] = ("李班长", [MesRoles.ShiftLeader]),
        ["qe"] = ("王质量", [MesRoles.QualityEngineer]),
        ["ee"] = ("赵设备", [MesRoles.EquipmentEngineer]),
        ["warehouse"] = ("孙仓库", [MesRoles.WarehouseClerk]),
        ["sqe"] = ("周SQE", [MesRoles.SupplierQualityEngineer])
    };

    if (!demoUsers.TryGetValue(req.Username, out var user))
        return Results.Unauthorized();

    var token = tokenService.GenerateToken(req.Username, user.Name, user.Roles);
    return Results.Ok(new LoginResponse(token, user.Name, user.Roles));
})
.WithTags("Auth");

app.Run();

/// <summary>登录请求</summary>
[MemoryPackable]
public partial record LoginRequest(string Username, string Password);

/// <summary>登录响应（record，MemoryPack 可序列化）</summary>
[MemoryPackable]
public partial record LoginResponse(string Token, string User, string[] Roles);
