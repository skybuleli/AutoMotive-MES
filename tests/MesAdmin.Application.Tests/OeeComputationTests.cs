using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Tests;

/// <summary>
/// OEE 计算测试（T2.14）。
/// 验证 OeeRecord.Compute 公式 + 等级判定 + 钳制边界。
/// </summary>
public class OeeComputationTests
{
    [Fact]
    public void Compute_ShouldMultiplyThreeRates()
    {
        //Arrange：可用率 90% × 性能率 95% × 良品率 98% = 83.79%
        var oee = OeeRecord.Compute("EQ-01", DateTimeOffset.UtcNow, 0.90, 0.95, 0.98);

        Assert.Equal(0.90, oee.Availability);
        Assert.Equal(0.95, oee.Performance);
        Assert.Equal(0.98, oee.Quality);
        Assert.Equal(0.8379, oee.Oee, precision: 4);
    }

    [Fact]
    public void Compute_GradeS_WhenOeeAbove85Percent()
    {
        var oee = OeeRecord.Compute("EQ-01", DateTimeOffset.UtcNow, 1.0, 0.95, 0.90);
        Assert.Equal(0.855, oee.Oee, precision: 3);
        Assert.Equal(OeeGrade.S, oee.Grade);
    }

    [Fact]
    public void Compute_GradeA_WhenOeeBetween70And85()
    {
        var oee = OeeRecord.Compute("EQ-01", DateTimeOffset.UtcNow, 0.90, 0.90, 0.90);
        Assert.Equal(0.729, oee.Oee, precision: 3);
        Assert.Equal(OeeGrade.A, oee.Grade);
    }

    [Fact]
    public void Compute_GradeB_WhenOeeBelow70Percent()
    {
        var oee = OeeRecord.Compute("EQ-01", DateTimeOffset.UtcNow, 0.50, 0.80, 0.90);
        Assert.Equal(0.36, oee.Oee, precision: 2);
        Assert.Equal(OeeGrade.B, oee.Grade);
    }

    [Fact]
    public void Compute_ShouldClampValuesAbove1()
    {
        // PLC 异常数据 >100% 应钳制到 1
        var oee = OeeRecord.Compute("EQ-01", DateTimeOffset.UtcNow, 1.5, 1.2, 1.1);
        Assert.Equal(1.0, oee.Availability);
        Assert.Equal(1.0, oee.Performance);
        Assert.Equal(1.0, oee.Quality);
        Assert.Equal(1.0, oee.Oee);
    }

    [Fact]
    public void Compute_ShouldClampNegativeValues()
    {
        var oee = OeeRecord.Compute("EQ-01", DateTimeOffset.UtcNow, -0.5, -0.1, 0.8);
        Assert.Equal(0, oee.Availability);
        Assert.Equal(0, oee.Performance);
        Assert.Equal(0.8, oee.Quality);
        Assert.Equal(0, oee.Oee);
    }

    [Fact]
    public void Compute_PerfectOee_ShouldBe100Percent()
    {
        var oee = OeeRecord.Compute("EQ-01", DateTimeOffset.UtcNow, 1.0, 1.0, 1.0);
        Assert.Equal(1.0, oee.Oee);
        Assert.Equal(OeeGrade.S, oee.Grade);
    }

    [Fact]
    public void Compute_ZeroDowntime_ShouldBeZeroOee()
    {
        var oee = OeeRecord.Compute("EQ-01", DateTimeOffset.UtcNow, 0, 1.0, 1.0);
        Assert.Equal(0, oee.Oee);
        Assert.Equal(OeeGrade.B, oee.Grade);
    }
}
