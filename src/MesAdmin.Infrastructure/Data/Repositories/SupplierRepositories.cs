using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace MesAdmin.Infrastructure.Data.Repositories;

/// <summary>供应商仓储</summary>
public class SupplierRepository(MesDbContext db) : ISupplierRepository
{
    public Task<Supplier?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<List<Supplier>> GetAllAsync(CancellationToken ct = default)
        => db.Suppliers.AsNoTracking().OrderBy(s => s.SupplierCode).ToListAsync(ct);

    public Task<List<Supplier>> GetByMaterialCategoryAsync(string category, CancellationToken ct = default)
        => db.Suppliers.AsNoTracking()
            .Where(s => s.MaterialCategory == category)
            .OrderBy(s => s.SupplierCode)
            .ToListAsync(ct);

    public Task<List<Supplier>> GetCriticalAsync(CancellationToken ct = default)
        => db.Suppliers.AsNoTracking()
            .Where(s => s.IsCritical)
            .OrderBy(s => s.SupplierCode)
            .ToListAsync(ct);

    public Task<Supplier?> GetByCodeAsync(string supplierCode, CancellationToken ct = default)
        => db.Suppliers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SupplierCode == supplierCode, ct);

    public Task AddAsync(Supplier supplier, CancellationToken ct = default)
        => db.Suppliers.AddAsync(supplier, ct).AsTask();

    public void Update(Supplier supplier) => db.Suppliers.Update(supplier);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

/// <summary>供应商评分卡仓储</summary>
public class SupplierScoreCardRepository(MesDbContext db) : ISupplierScoreCardRepository
{
    public Task<SupplierScoreCard?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.SupplierScoreCards.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<List<SupplierScoreCard>> GetBySupplierAsync(Ulid supplierId, CancellationToken ct = default)
        => db.SupplierScoreCards.AsNoTracking()
            .Where(c => c.SupplierId == supplierId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

    public Task<List<SupplierScoreCard>> GetByPeriodAsync(string period, CancellationToken ct = default)
        => db.SupplierScoreCards.AsNoTracking()
            .Where(c => c.Period == period)
            .OrderBy(c => c.SupplierCode)
            .ToListAsync(ct);

    public Task<SupplierScoreCard?> GetLatestBySupplierAsync(Ulid supplierId, CancellationToken ct = default)
        => db.SupplierScoreCards.AsNoTracking()
            .Where(c => c.SupplierId == supplierId)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public Task AddAsync(SupplierScoreCard card, CancellationToken ct = default)
        => db.SupplierScoreCards.AddAsync(card, ct).AsTask();

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

/// <summary>PPAP 文档仓储</summary>
public class PpapDocumentRepository(MesDbContext db) : IPpapDocumentRepository
{
    public Task<PpapDocument?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.PpapDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<List<PpapDocument>> GetBySupplierAsync(Ulid supplierId, CancellationToken ct = default)
        => db.PpapDocuments.AsNoTracking()
            .Where(d => d.SupplierId == supplierId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

    public Task<List<PpapDocument>> GetByMaterialAsync(string materialCode, CancellationToken ct = default)
        => db.PpapDocuments.AsNoTracking()
            .Where(d => d.MaterialCode == materialCode)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

    public Task<List<PpapDocument>> GetExpiringAsync(int daysWithin = 30, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(daysWithin);
        return db.PpapDocuments.AsNoTracking()
            .Where(d => d.ExpiryDate != null && d.ExpiryDate <= cutoff && d.ExpiryDate > DateTimeOffset.UtcNow)
            .OrderBy(d => d.ExpiryDate)
            .ToListAsync(ct);
    }

    public Task<List<PpapDocument>> GetByStatusAsync(PpapStatus status, CancellationToken ct = default)
        => db.PpapDocuments.AsNoTracking()
            .Where(d => d.Status == status)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

    public Task AddAsync(PpapDocument document, CancellationToken ct = default)
        => db.PpapDocuments.AddAsync(document, ct).AsTask();

    public void Update(PpapDocument document) => db.PpapDocuments.Update(document);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

/// <summary>关键供应商管控设置仓储</summary>
public class CriticalSupplierSettingRepository(MesDbContext db) : ICriticalSupplierSettingRepository
{
    public Task<CriticalSupplierSetting?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.CriticalSupplierSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<List<CriticalSupplierSetting>> GetAllAsync(CancellationToken ct = default)
        => db.CriticalSupplierSettings.AsNoTracking()
            .OrderBy(s => s.MaterialCode)
            .ToListAsync(ct);

    public Task<CriticalSupplierSetting?> GetByMaterialCodeAsync(string materialCode, CancellationToken ct = default)
        => db.CriticalSupplierSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.MaterialCode == materialCode, ct);

    public Task AddAsync(CriticalSupplierSetting setting, CancellationToken ct = default)
        => db.CriticalSupplierSettings.AddAsync(setting, ct).AsTask();

    public void Update(CriticalSupplierSetting setting) => db.CriticalSupplierSettings.Update(setting);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
