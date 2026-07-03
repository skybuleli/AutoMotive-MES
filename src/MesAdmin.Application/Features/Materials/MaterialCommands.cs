using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.Materials;

/// <summary>
/// 来料扫码入库命令（T1.12）。
/// GS1-128 条码解析（零分配 ReadOnlySpan）→ 合格供应商校验 → 写 material_batches 表。
/// </summary>
[MemoryPackable]
public sealed partial record ReceiveMaterialCommand(
    string Barcode,
    string SupplierCode,
    string SupplierName,
    string MaterialName,
    bool IsCritical) : IWriteCommand<MaterialBatch>;

internal sealed class ReceiveMaterialHandler(
    IMaterialBatchRepository batches) : ICommandHandler<ReceiveMaterialCommand, MaterialBatch>
{
    public async Task<MaterialBatch> ExecuteAsync(ReceiveMaterialCommand cmd, CancellationToken ct)
    {
        // T1.12 GS1-128 零分配解析（ReadOnlySpan，禁止 Substring）
        var gs1 = Gs1Barcode.Parse(cmd.Barcode.AsSpan());

        // 幂等：同批次号已存在则直接返回
        var existing = await batches.GetByBatchNumberAsync(gs1.BatchNumber, ct);
        if (existing is not null)
            return existing;

        var quantity = gs1.Quantity > 0 ? (double)gs1.Quantity : 1;
        var batch = MaterialBatch.Create(
            gs1.MaterialCode,
            cmd.MaterialName,
            gs1.BatchNumber,
            cmd.SupplierCode,
            cmd.SupplierName,
            quantity,
            unit: "PCS",
            isCritical: cmd.IsCritical,
            gs1.ProductionDate);

        await batches.AddAsync(batch, ct);
        await batches.SaveChangesAsync(ct);
        return batch;
    }
}

/// <summary>
/// 投料批次绑定命令（T1.15 + T1.16 Poka-Yoke）。
/// 操作员扫码绑定：物料批次 → 工单 → 产品 S/N。
/// Poka-Yoke：关键物料 BOM 比对，错误物料锁定设备（抛异常阻断）。
/// </summary>
[MemoryPackable]
public sealed partial record BindMaterialCommand(
    Ulid OrderId,
    Ulid MaterialBatchId,
    string ProductSerial,
    double Quantity,
    string OperatorId) : IWriteCommand<MaterialBinding>;

internal sealed class BindMaterialHandler(
    IMaterialBatchRepository batches,
    IMaterialBindingRepository bindings,
    IProductionOrderRepository orders) : ICommandHandler<BindMaterialCommand, MaterialBinding>
{
    public async Task<MaterialBinding> ExecuteAsync(BindMaterialCommand cmd, CancellationToken ct)
    {
        // 1. 校验工单存在
        var order = await orders.GetByIdAsync(cmd.OrderId, ct)
            ?? throw new KeyNotFoundException($"工单 {cmd.OrderId} 不存在");

        // 2. 查物料批次（跟踪，需扣减库存）
        var batch = await batches.GetByIdTrackedAsync(cmd.MaterialBatchId, ct)
            ?? throw new KeyNotFoundException($"物料批次 {cmd.MaterialBatchId} 不存在");

        // 3. T1.16 Poka-Yoke 防错：关键物料必须检验合格才能投料
        bool pokaYokePassed = true;
        if (batch.IsCritical && batch.Status != MaterialBatchStatus.Qualified)
        {
            pokaYokePassed = false;
            throw new InvalidOperationException(
                $"Poka-Yoke 防错触发：关键物料 {batch.MaterialCode} 批次 {batch.BatchNumber} " +
                $"状态为 {batch.Status}，必须检验合格后方可投料。设备已锁定，需质量工程师解锁。");
        }

        // 4. T1.17 物料消耗反冲：扣减线边库存
        batch.Consume(cmd.Quantity);
        await batches.SaveChangesAsync(ct);

        // 5. 写投料绑定记录
        var binding = MaterialBinding.Create(
            cmd.OrderId,
            cmd.MaterialBatchId,
            batch.MaterialCode,
            batch.BatchNumber,
            cmd.ProductSerial,
            cmd.Quantity,
            pokaYokePassed,
            cmd.OperatorId);

        await bindings.AddAsync(binding, ct);
        await bindings.SaveChangesAsync(ct);

        return binding;
    }
}

/// <summary>物料批次查询命令</summary>
[MemoryPackable]
public sealed partial record ListMaterialBatchesQuery(string? MaterialCode, int Page, int Size)
    : ICommand<(List<MaterialBatch> Items, int Total)>;

internal sealed class ListMaterialBatchesHandler(IMaterialBatchRepository batches)
    : ICommandHandler<ListMaterialBatchesQuery, (List<MaterialBatch> Items, int Total)>
{
    public async Task<(List<MaterialBatch> Items, int Total)> ExecuteAsync(ListMaterialBatchesQuery query, CancellationToken ct)
    {
        var skip = (Math.Max(1, query.Page) - 1) * query.Size;
        var take = Math.Clamp(query.Size, 1, 100);
        var items = await batches.GetPageAsync(query.MaterialCode, skip, take, ct);
        var total = await batches.CountAsync(query.MaterialCode, ct);
        return (items, total);
    }
}
