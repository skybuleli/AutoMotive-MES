using FastEndpoints;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Application.Behaviors;

/// <summary>
/// 命令日志中间件：记录每个命令的进入、耗时与异常。
/// 使用 ZLogger 结构化日志，符合项目日志铁律。
/// 通过 <see cref="CommandMiddlewareConfig.Register"/> 注册为开放泛型，自动包裹所有命令。
/// </summary>
public sealed class LoggingCommandMiddleware<TCommand, TResult>(
    ILogger<LoggingCommandMiddleware<TCommand, TResult>> logger)
    : ICommandMiddleware<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public async Task<TResult> ExecuteAsync(
        TCommand command, CommandDelegate<TResult> next, CancellationToken ct)
    {
        var name = typeof(TCommand).Name;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            logger.ZLogInformation($"→ {name}");
            var result = await next();
            logger.ZLogInformation($"← {name} ({sw.ElapsedMilliseconds}ms)");
            return result;
        }
        catch (Exception ex)
        {
            logger.ZLogError($"✗ {name}: {ex.Message}");
            throw;
        }
    }
}
