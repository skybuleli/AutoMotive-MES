using MemoryPack.AspNetCoreMvcFormatter;
using Microsoft.EntityFrameworkCore;
using MesAdmin.Infrastructure.Data;
using MesAdmin.Infrastructure.Logging;

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

// ── ZLogger 结构化日志 ──
builder.Logging.AddZLogger();

// ── JWT 认证（T0.8 待完善：6 角色 RBAC）──
// builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
//     .AddJwtBearer(...);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
