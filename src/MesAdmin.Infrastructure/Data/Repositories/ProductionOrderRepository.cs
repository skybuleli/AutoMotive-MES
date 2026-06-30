using Microsoft.EntityFrameworkCore;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Infrastructure.Data.Repositories;

/// <summary>
/// 工单仓储实现（骨架版，委托 MesDbContext）。
/// T1.2 需完善：Repository 包装 + 查询优化。
/// </summary>
public class ProductionOrderRepository(MesDbContext db) : IProductionOrderRepository
{
    public async Task<ProductionOrder?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => await db.ProductionOrders.FindAsync([id], ct);

    public async Task<List<ProductionOrder>> GetAllAsync(CancellationToken ct = default)
        => await db.ProductionOrders.ToListAsync(ct);

    public async Task AddAsync(ProductionOrder order, CancellationToken ct = default)
        => await db.ProductionOrders.AddAsync(order, ct);

    public Task UpdateAsync(ProductionOrder order, CancellationToken ct = default)
    {
        db.ProductionOrders.Update(order);
        return Task.CompletedTask;
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        => await db.SaveChangesAsync(ct);
}
