using MesAdmin.Application.Observability;
using MesAdmin.Infrastructure.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace MesAdmin.Api.DependencyInjection;

/// <summary>
/// 可观察性（日志 + 指标）DI 注册扩展。
/// </summary>
public static class ObservabilityExtensions
{
    public static IServiceCollection AddMesObservability(this IServiceCollection services, IConfiguration configuration)
    {
        // ── ZLogger 结构化日志 ──
        services.AddLogging(builder => builder.AddZLogger());

        // ── OpenTelemetry Metrics -> GreptimeDB OTLP ──
        var otlpEndpoint = NormalizeGreptimeMetricsEndpoint(
            configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4000/v1/otlp");
        var otelServiceName = configuration["OTEL_SERVICE_NAME"] ?? "MesAdmin.Api";
        var otelServiceNamespace = configuration["OTEL_SERVICE_NAMESPACE"] ?? "AutoMES";
        var greptimeDbName = configuration["Observability:GreptimeDbName"] ?? "public";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: otelServiceName, serviceNamespace: otelServiceNamespace))
            .WithMetrics(metrics => metrics
                .AddMeter(AutoMesMetrics.MeterName)
                .AddOtlpExporter((exporter, reader) =>
                {
                    exporter.Endpoint = new Uri(otlpEndpoint);
                    exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                    exporter.Headers = $"X-Greptime-DB-Name={greptimeDbName}";
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 5000;
                }));

        return services;
    }

    private static string NormalizeGreptimeMetricsEndpoint(string configuredEndpoint)
    {
        var endpoint = configuredEndpoint.TrimEnd('/');

        if (endpoint.EndsWith("/v1/otlp/v1/metrics", StringComparison.OrdinalIgnoreCase))
            return endpoint;

        if (endpoint.EndsWith("/v1/otlp", StringComparison.OrdinalIgnoreCase))
            return endpoint + "/v1/metrics";

        return endpoint + "/v1/otlp/v1/metrics";
    }
}
