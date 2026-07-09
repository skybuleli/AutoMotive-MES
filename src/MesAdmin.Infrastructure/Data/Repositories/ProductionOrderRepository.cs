using Microsoft.EntityFrameworkCore;
using Npgsql;
using MesAdmin.Application.Common;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Infrastructure.Data.Repositories;

/// <summary>
/// 工单仓储实现（委托同一 Scoped MesDbContext，UnitOfWork 统一提交）。
/// </summary>
public class ProductionOrderRepository(MesDbContext db) : IProductionOrderRepository
{
    public Task<ProductionOrder?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.ProductionOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(order => order.Id == id, ct);

    public Task<ProductionOrder?> GetByIdTrackedAsync(Ulid id, CancellationToken ct = default)
        => db.ProductionOrders
            .FirstOrDefaultAsync(order => order.Id == id, ct);

    public Task<ProductionOrder?> GetByOrderNumberAsync(string orderNumber, CancellationToken ct = default)
        => db.ProductionOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(order => order.OrderNumber == orderNumber, ct);

    public Task<List<ProductionOrder>> GetAllAsync(CancellationToken ct = default)
        => db.ProductionOrders
            .AsNoTracking()
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id)
            .ToListAsync(ct);

    public Task<List<ProductionOrder>> GetPageAsync(OrderStatus? status, int skip, int take, CancellationToken ct = default)
    {
        var query = db.ProductionOrders.AsNoTracking().AsQueryable();

        if (status is not null)
            query = query.Where(order => order.Status == status);

        return query
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    public Task<int> CountAsync(OrderStatus? status, CancellationToken ct = default)
    {
        var query = db.ProductionOrders.AsQueryable();

        if (status is not null)
            query = query.Where(order => order.Status == status);

        return query.CountAsync(ct);
    }

    public Task<List<ProductionOrder>> GetPageAsync(OrderListFilter filter, int skip, int take, CancellationToken ct = default)
        => ApplyFilter(db.ProductionOrders.AsNoTracking(), filter)
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public Task<int> CountAsync(OrderListFilter filter, CancellationToken ct = default)
        => ApplyFilter(db.ProductionOrders.AsQueryable(), filter).CountAsync(ct);

    private static IQueryable<ProductionOrder> ApplyFilter(IQueryable<ProductionOrder> query, OrderListFilter filter)
    {
        if (filter.Status is not null)
            query = query.Where(order => order.Status == filter.Status);

        if (!string.IsNullOrWhiteSpace(filter.OrderNumberContains))
        {
            var term = filter.OrderNumberContains.Trim();
            query = query.Where(order => order.OrderNumber.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(filter.ProductCode))
        {
            var code = filter.ProductCode.Trim().ToUpperInvariant();
            query = query.Where(order => order.ProductCode == code);
        }

        if (filter.CreatedFrom is not null)
            query = query.Where(order => order.CreatedAt >= filter.CreatedFrom);

        if (filter.CreatedTo is not null)
            query = query.Where(order => order.CreatedAt <= filter.CreatedTo);

        return query;
    }

    public Task<int> CountByOrderNumberPrefixAsync(string orderNumberPrefix, CancellationToken ct = default)
        => db.ProductionOrders.CountAsync(order => order.OrderNumber.StartsWith(orderNumberPrefix), ct);

    public Task AddAsync(ProductionOrder order, CancellationToken ct = default)
        => db.ProductionOrders.AddAsync(order, ct).AsTask();

    public void Update(ProductionOrder order)
        => db.ProductionOrders.Update(order);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsOrderNumberConflict(ex))
        {
            var conflictingNumber = db.ChangeTracker
                .Entries<ProductionOrder>()
                .Select(e => e.Entity.OrderNumber)
                .FirstOrDefault() ?? string.Empty;
            throw new DuplicateOrderNumberException(conflictingNumber);
        }
    }

    /// <summary>判断是否为工单号唯一索引冲突（PostgreSQL SQLSTATE 23505）。</summary>
    private static bool IsOrderNumberConflict(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
           && pg.ConstraintName is not null
           && pg.ConstraintName.Contains("OrderNumber", StringComparison.OrdinalIgnoreCase);
}
