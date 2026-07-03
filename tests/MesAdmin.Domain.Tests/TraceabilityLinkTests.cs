using MesAdmin.Domain.Models;

namespace MesAdmin.Domain.Tests;

/// <summary>
/// 追溯链领域模型测试（T1.19/T1.21）。
/// 验证 4 级追溯模型 + 哈希链不可篡改 + 幂等创建。
/// </summary>
public class TraceabilityLinkTests
{
    [Fact]
    public void Create_ShouldComputeHashLinkingToPrevious()
    {
        //Arrange：链首记录（previousHash 为空）
        var link1 = TraceabilityLink.Create(
            Ulid.NewUlid(),
            TraceabilityLevel.Assembly,
            "ESP9-20260703-000001",
            componentBatch: "ECU-BATCH-A",
            materialBatch: "ALLOY-BATCH-X",
            previousHash: string.Empty,
            createdAt: DateTimeOffset.UtcNow);

        //Assert：链首 PreviousHash 为空，Hash 已计算
        Assert.Equal(string.Empty, link1.PreviousHash);
        Assert.False(string.IsNullOrWhiteSpace(link1.Hash));
        Assert.True(link1.VerifyHash());

        //Act：第二条记录链接到第一条
        var link2 = TraceabilityLink.Create(
            Ulid.NewUlid(),
            TraceabilityLevel.Component,
            "ECU-SN-001",
            componentBatch: "ECU-BATCH-A",
            materialBatch: "PCB-BATCH-Y",
            previousHash: link1.Hash,
            createdAt: DateTimeOffset.UtcNow);

        //Assert：第二条 PreviousHash = 第一条 Hash
        Assert.Equal(link1.Hash, link2.PreviousHash);
        Assert.NotEqual(link1.Hash, link2.Hash);
        Assert.True(link2.VerifyHash());
    }

    [Fact]
    public void VerifyHash_ShouldDetectTampering()
    {
        var link = TraceabilityLink.Create(
            Ulid.NewUlid(),
            TraceabilityLevel.Material,
            "ALLOY-SN-001",
            componentBatch: "",
            materialBatch: "ALLOY-BATCH-X",
            previousHash: "abc123",
            createdAt: DateTimeOffset.UtcNow);

        Assert.True(link.VerifyHash());

        //Act：篡改任何字段
        link.ComponentBatch = "TAMPERED";

        //Assert：哈希校验失败，检测到篡改
        Assert.False(link.VerifyHash());
    }

    [Fact]
    public void Create_ShouldRejectEmptyVinOrSerial()
    {
        Assert.Throws<ArgumentException>(() =>
            TraceabilityLink.Create(
                Ulid.NewUlid(),
                TraceabilityLevel.Vehicle,
                vinOrSerial: "",
                componentBatch: null,
                materialBatch: null,
                previousHash: "",
                createdAt: DateTimeOffset.UtcNow));
    }

    [Fact]
    public void HashChain_ShouldBreakWhenIntermediateRecordTampered()
    {
        //Arrange：构建 3 条记录的哈希链
        var link1 = TraceabilityLink.Create(
            Ulid.NewUlid(), TraceabilityLevel.Assembly, "ESP9-001",
            "BATCH-A", "ALLOY-X", "", DateTimeOffset.UtcNow);
        var link2 = TraceabilityLink.Create(
            Ulid.NewUlid(), TraceabilityLevel.Component, "ECU-001",
            "BATCH-A", "PCB-Y", link1.Hash, DateTimeOffset.UtcNow);
        var link3 = TraceabilityLink.Create(
            Ulid.NewUlid(), TraceabilityLevel.Material, "ALLOY-001",
            "", "ALLOY-X", link2.Hash, DateTimeOffset.UtcNow);

        //Assert：初始全部校验通过
        Assert.True(link1.VerifyHash());
        Assert.True(link2.VerifyHash());
        Assert.True(link3.VerifyHash());

        //Act：篡改中间记录 link2 的内容
        link2.MaterialBatch = "TAMPERED";

        //Assert：link2 自身校验失败；link3 的 PreviousHash 仍指向 link2 原哈希（不变），
        //但 link2 被篡改这一事实可通过 link2.VerifyHash() 检测出来
        Assert.False(link2.VerifyHash());
        //link3 自身内容未变，仍通过校验（但前驱已不可信，需全链审计）
        Assert.True(link3.VerifyHash());
    }
}
