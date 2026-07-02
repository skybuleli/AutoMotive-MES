using FastEndpoints;
using MesAdmin.Application.Interfaces;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Application.Behaviors;

/// <summary>
/// 事务中间件：为写入命令提供显式事务边界。
/// 通过 <see cref="IWriteCommand{TResult}"/> 标记接口约束，仅包裹写命令不包裹查询。
/// 异常时自动回滚；正常完成时提交。使用 EF Core ExecutionStrategy 支持瞬时故障重试。
/// </summary>
public sealed class TransactionMiddleware<TCommand, TResult>(
    IUnitOfWork unitOfWork,
    ILogger<TransactionMiddleware<TCommand, TResult>> logger)
    : ICommandMiddleware<TCommand, TResult>
    where TCommand : IWriteCommand<TResult>
{
    public async Task<TResult> ExecuteAsync(
        TCommand command, CommandDelegate<TResult> next, CancellationToken ct)
    {
        await using var tx = await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var result = await next();
            await tx.CommitAsync(ct);
            return result;
        }
        catch (Exception ex)
        {
            logger.ZLogWarning($"事务回滚: {typeof(TCommand).Name} - {ex.Message}");
            throw;
        }
    }
}
