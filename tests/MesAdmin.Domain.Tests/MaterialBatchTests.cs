using MesAdmin.Domain.Models;

namespace MesAdmin.Domain.Tests;

/// <summary>
/// GS1-128 条码解析 + 物料批次领域模型测试（T1.12 / T1.16）。
/// 验证零分配解析 + 状态机 + Poka-Yoke 前置条件。
/// </summary>
public class MaterialBatchTests
{
    [Fact]
    public void Gs1Parse_ShouldParseStandardFormat()
    {
        //Arrange：(01)14位GTIN(10)批次号(11)生产日期(37)数量
        var barcode = "(01)12345678901234(10)BATCH001(11)260703(37)500";

        //Act：零分配解析（ReadOnlySpan，禁止 Substring）
        var gs1 = Gs1Barcode.Parse(barcode.AsSpan());

        //Assert
        Assert.Equal("12345678901234", gs1.MaterialCode);
        Assert.Equal("BATCH001", gs1.BatchNumber);
        Assert.Equal(500, gs1.Quantity);
        Assert.NotNull(gs1.ProductionDate);
        Assert.Equal(2026, gs1.ProductionDate!.Value.Year);
        Assert.Equal(7, gs1.ProductionDate!.Value.Month);
        Assert.Equal(3, gs1.ProductionDate!.Value.Day);
    }

    [Fact]
    public void Gs1Parse_ShouldRejectMissingMaterialCode()
    {
        var barcode = "(10)BATCH001(11)260703";
        Assert.Throws<ArgumentException>(() => Gs1Barcode.Parse(barcode.AsSpan()));
    }

    [Fact]
    public void Gs1Parse_ShouldRejectEmptyBarcode()
    {
        Assert.Throws<ArgumentException>(() => Gs1Barcode.Parse("".AsSpan()));
    }

    [Fact]
    public void Create_ShouldInitializeWithReceivedStatus()
    {
        var batch = MaterialBatch.Create(
            "ECU-ESP9", "ECU 控制单元", "BATCH-2026-001",
            "SUP-001", "博世苏州", 500, "PCS", isCritical: true,
            DateTimeOffset.UtcNow);

        Assert.Equal(MaterialBatchStatus.Received, batch.Status);
        Assert.Equal(500, batch.ReceivedQuantity);
        Assert.Equal(500, batch.RemainingQuantity);
        Assert.True(batch.IsCritical);
    }

    [Fact]
    public void Qualify_ShouldTransitionToQualified()
    {
        var batch = CreateTestBatch();
        batch.Qualify();
        Assert.Equal(MaterialBatchStatus.Qualified, batch.Status);
    }

    [Fact]
    public void Consume_ShouldRejectBeforeQualify()
    {
        //Arrange：未检验合格的批次
        var batch = CreateTestBatch();

        //Act + Assert：Poka-Yoke 前置——未合格不可消耗
        Assert.Throws<InvalidOperationException>(() => batch.Consume(10));
    }

    [Fact]
    public void Consume_ShouldDeductQuantity()
    {
        var batch = CreateTestBatch();
        batch.Qualify();

        batch.Consume(100);

        Assert.Equal(400, batch.RemainingQuantity);
        Assert.Equal(MaterialBatchStatus.Qualified, batch.Status);
    }

    [Fact]
    public void Consume_ShouldMarkConsumedWhenDepleted()
    {
        var batch = CreateTestBatch(receivedQuantity: 10);
        batch.Qualify();

        batch.Consume(10);

        Assert.Equal(0, batch.RemainingQuantity);
        Assert.Equal(MaterialBatchStatus.Consumed, batch.Status);
    }

    [Fact]
    public void Consume_ShouldRejectOverConsumption()
    {
        var batch = CreateTestBatch(receivedQuantity: 10);
        batch.Qualify();

        Assert.Throws<InvalidOperationException>(() => batch.Consume(11));
    }

    [Fact]
    public void Reject_ShouldTransitionToRejected()
    {
        var batch = CreateTestBatch();
        batch.Reject();
        Assert.Equal(MaterialBatchStatus.Rejected, batch.Status);
    }

    private static MaterialBatch CreateTestBatch(double receivedQuantity = 500)
        => MaterialBatch.Create(
            "VALVE-SOL", "电磁阀", "BATCH-TEST-001",
            "SUP-001", "测试供应商", receivedQuantity, "PCS", isCritical: true,
            DateTimeOffset.UtcNow);
}
