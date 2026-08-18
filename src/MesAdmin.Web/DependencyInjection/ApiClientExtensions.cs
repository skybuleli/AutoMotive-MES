using MesAdmin.Web.Services;

namespace MesAdmin.Web.DependencyInjection;

/// <summary>
/// Web 端 API 客户端与 SignalR Hub 客户端 DI 注册扩展。
/// </summary>
public static class ApiClientExtensions
{
    public static IServiceCollection AddMesWebApiClients(this IServiceCollection services, IConfiguration configuration)
    {
        // API 客户端（所有页面通过 MesApiClient 调用后端 API，不再直接注入 Application 服务）
        services.AddScoped<MesApiClient>();

        // OEE SignalR Hub 客户端（连接 Api 的 /hubs/dashboard）
        services.AddSingleton<OeeHubClient>();

        // Andon SignalR Hub 客户端（连接 Api 的 /hubs/andon）
        services.AddSingleton<AndonHubClient>();

        services.AddHttpClient("MesApi", client =>
        {
            client.BaseAddress = new Uri(configuration["Api:BaseUrl"] ?? "http://localhost:5040/");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        return services;
    }
}
