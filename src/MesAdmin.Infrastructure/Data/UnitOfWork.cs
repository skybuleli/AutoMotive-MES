using MesAdmin.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MesAdmin.Infrastructure.Data;

/// <summary>
/// 基于 EF Core DbContext 的工作单元实现。
/// Scoped 生命周期：与 MesDbContext 同一请求共享事务。
/// </summary>
public sealed class UnitOfWork(MesDbContext db) : IUnitOfWork
{
    private IDbContextTransaction? _transaction;

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction is not null)
            return new NoopTransaction();  // 嵌套调用复用外层事务

        _transaction = await db.Database.BeginTransactionAsync(ct);
        return new EfCoreTransaction(_transaction, this);
    }

    internal Task RollbackAsync(CancellationToken ct = default)
        => _transaction?.RollbackAsync(ct) ?? Task.CompletedTask;

    /// <summary>EF Core 事务句柄：提交后标记完成。</summary>
    private sealed class EfCoreTransaction(IDbContextTransaction tx, UnitOfWork owner) : IUnitOfWorkTransaction
    {
        public async Task CommitAsync(CancellationToken ct = default)
        {
            await tx.CommitAsync(ct);
            owner._transaction = null;
        }

        public async ValueTask DisposeAsync()
        {
            if (owner._transaction is not null)
            {
                await owner.RollbackAsync();
                await tx.DisposeAsync();
                owner._transaction = null;
            }
        }
    }

    /// <summary>嵌套事务占位：不执行任何操作，由最外层事务统一提交。</summary>
    private sealed class NoopTransaction : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
