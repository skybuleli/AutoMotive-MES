using Microsoft.EntityFrameworkCore;
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

    public Task<int> CountByOrderNumberPrefixAsync(string orderNumberPrefix, CancellationToken ct = default)
        => db.ProductionOrders.CountAsync(order => order.OrderNumber.StartsWith(orderNumberPrefix), ct);

    public Task AddAsync(ProductionOrder order, CancellationToken ct = default)
        => db.ProductionOrders.AddAsync(order, ct).AsTask();

    public void Update(ProductionOrder order)
        => db.ProductionOrders.Update(order);

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
