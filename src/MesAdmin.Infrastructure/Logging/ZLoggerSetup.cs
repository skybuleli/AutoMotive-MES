using ZLogger;
using Microsoft.Extensions.Logging;

namespace MesAdmin.Infrastructure.Logging;

/// <summary>
/// ZLogger 结构化日志配置。
/// 使用 IBufferWriter&lt;byte&gt; 直写，禁止字符串拼接 + 默认 ILogger。
/// 用法：_logger.ZLogInformation($"工单 {order.OrderNumber} 工序 {op.Sequence} 完成");
/// </summary>
public static class ZLoggerSetup
{
    /// <summary>
    /// 注册 ZLogger（零分配结构化日志，默认 PlainText 输出到控制台）。
    /// </summary>
    public static ILoggingBuilder AddZLogger(this ILoggingBuilder builder)
    {
        builder.AddZLoggerConsole();
        return builder;
    }
}
