namespace MesAdmin.Application.Interfaces;

/// <summary>
/// 工作单元接口：为命令 handler 提供显式事务边界。
/// Infrastructure 层用 MesDbContext 实现，命令中间件包裹写操作。
/// </summary>
public interface IUnitOfWork
{
    /// <summary>开启事务（已开启则复用当前事务）。</summary>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default);
}

/// <summary>事务句柄：using 范围内提交或回滚。</summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
}
