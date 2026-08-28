using MesAdmin.Application.Interfaces;
using MesAdmin.Infrastructure.Data.Repositories;
using MesAdmin.Infrastructure.Storage;

namespace MesAdmin.Api.DependencyInjection;

/// <summary>
/// 受控文档中心 DI 注册（S03 · IATF 16949 文件控制）。
/// </summary>
public static class DocumentExtensions
{
    public static IServiceCollection AddMesDocuments(this IServiceCollection services)
    {
        // IFileStorage 单例：本地文件系统（配置 DocumentStorage:BasePath，未配置落 Temp）
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        // 仓储合并为单实现，双接口复用同一实例
        services.AddScoped<DocumentRepository>();
        services.AddScoped<IControlledDocumentRepository>(sp => sp.GetRequiredService<DocumentRepository>());
        services.AddScoped<IDocumentVersionRepository>(sp => sp.GetRequiredService<DocumentRepository>());

        return services;
    }
}
