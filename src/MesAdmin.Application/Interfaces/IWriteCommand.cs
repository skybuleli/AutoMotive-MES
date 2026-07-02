using FastEndpoints;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// 标记接口：标识会产生写入副作用的命令。
/// <see cref="TransactionBehavior{TCommand, TResult}"/> 仅包裹此类型，
/// 查询命令（纯 ICommand&lt;TResult&gt;）不开启事务，避免无谓开销。
/// </summary>
public interface IWriteCommand<TResult> : ICommand<TResult> { }
