using MesAdmin.Infrastructure.Logging;

namespace MesAdmin.Web.DependencyInjection;

/// <summary>
/// Web 端日志 DI 注册扩展。
/// </summary>
public static class LoggingExtensions
{
    public static ILoggingBuilder AddMesWebLogging(this ILoggingBuilder builder)
    {
        builder.AddZLogger();
        return builder;
    }
}
