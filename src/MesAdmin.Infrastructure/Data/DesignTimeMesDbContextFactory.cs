using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MesAdmin.Infrastructure.Data;

/// <summary>
/// 设计时 DbContext 工厂（供 EF Core 迁移工具使用）。
/// 迁移命令通过此工厂直接构造 MesDbContext，无需启动完整 DI 容器。
/// </summary>
public class DesignTimeMesDbContextFactory : IDesignTimeDbContextFactory<MesDbContext>
{
    public MesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MesDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=automes;Username=mes;Password=mes_dev_password")
            .Options;

        return new MesDbContext(options);
    }
}
