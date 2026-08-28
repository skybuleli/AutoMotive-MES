using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Data;
using MesAdmin.Infrastructure.RealTime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace MesAdmin.Application.Tests;

/// <summary>
/// 计量器具台账集成测试（S01）。
/// 真实 PostgreSQL 验证：建账查重、唯一编号约束兜底、校准登记持久化、
/// 校准历史查询排序、后台提醒服务的状态推进与飞书消息组装。
/// </summary>
[Collection("DatabaseIntegration")]
public class GaugeIntegrationTests
{
    private readonly DatabaseFixture _fixture;

    public GaugeIntegrationTests(DatabaseFixture fixture) => _fixture = fixture;

    // ═══════════════════════════════════════════════════════════
    //  仓储 CRUD + 唯一约束
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Add_Then_GetByNumber_ShouldRoundTrip()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IGaugeRepository>();

        var gauge = CreateGauge("GT-IT-001");
        await repo.AddAsync(gauge);

        var loaded = await repo.GetByNumberAsync("GT-IT-001");

        Assert.NotNull(loaded);
        Assert.Equal(gauge.Id, loaded.Id);
        Assert.Equal(GaugeType.TorqueWrench, loaded.Type);
        Assert.Equal(gauge.NextDueAt, loaded.NextDueAt);
        Assert.Equal(GaugeStatus.InService, loaded.Status);
    }

    [Fact]
    public async Task Add_DuplicateGaugeNumber_ShouldThrowUniqueConstraint()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IGaugeRepository>();

        await repo.AddAsync(CreateGauge("GT-IT-DUP"));

        // DB 唯一索引兜底（端点层已前置查重，此处验证最后一道防线）
        await Assert.ThrowsAsync<DbUpdateException>(
            () => repo.AddAsync(CreateGauge("GT-IT-DUP")));
    }

    [Fact]
    public async Task GetAllAsync_WithStatusFilter_ShouldReturnOnlyMatching()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IGaugeRepository>();

        var scrapped = CreateGauge($"GT-IT-S-{Ulid.NewUlid().ToString()[..8]}");
        scrapped.Scrap("测试报废");
        await repo.AddAsync(scrapped);
        await repo.AddAsync(CreateGauge($"GT-IT-A-{Ulid.NewUlid().ToString()[..8]}"));

        var scrappedList = await repo.GetAllAsync(GaugeStatus.Scrapped);

        Assert.Contains(scrappedList, g => g.Id == scrapped.Id);
        Assert.All(scrappedList, g => Assert.Equal(GaugeStatus.Scrapped, g.Status));
    }

    // ═══════════════════════════════════════════════════════════
    //  校准登记 + 历史（S02 将引用的完整闭环）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task RecordCalibration_ShouldPersistGaugeAndRecord()
    {
        using var scope = _fixture.Services.CreateScope();
        var gaugeRepo = scope.ServiceProvider.GetRequiredService<IGaugeRepository>();
        var recordRepo = scope.ServiceProvider.GetRequiredService<ICalibrationRecordRepository>();

        var gauge = CreateGauge("GT-IT-CAL");
        await gaugeRepo.AddAsync(gauge);

        var calibratedAt = DateTimeOffset.UtcNow;
        var record = CalibrationRecord.Create(
            Ulid.NewUlid(), gauge.Id, calibratedAt, CalibrationResult.Pass,
            "CERT-IT-001", "QE-IT", calibratedAt.AddDays(gauge.CalibrationCycleDays));

        Assert.True(gauge.RecordCalibration(calibratedAt));

        await recordRepo.AddAsync(record);
        await gaugeRepo.UpdateAsync(gauge);

        // 新 scope 模拟新请求读取
        using var scope2 = _fixture.Services.CreateScope();
        var gauge2 = await scope2.ServiceProvider.GetRequiredService<IGaugeRepository>()
            .GetByIdAsync(gauge.Id);
        var records = await scope2.ServiceProvider.GetRequiredService<ICalibrationRecordRepository>()
            .GetByGaugeIdAsync(gauge.Id);

        Assert.NotNull(gauge2);
        Assert.Equal(calibratedAt, gauge2!.LastCalibratedAt!.Value);
        Assert.True(gauge2.IsWithinCalibration(DateTimeOffset.UtcNow));

        var mine = Assert.Single(records);
        Assert.Equal(record.Id, mine.Id);
        Assert.Equal("CERT-IT-001", mine.CertificateNo);
    }

    [Fact]
    public async Task CalibrationRecords_ShouldOrderByCalibratedAtDescending()
    {
        using var scope = _fixture.Services.CreateScope();
        var gaugeRepo = scope.ServiceProvider.GetRequiredService<IGaugeRepository>();
        var recordRepo = scope.ServiceProvider.GetRequiredService<ICalibrationRecordRepository>();

        var gauge = CreateGauge("GT-IT-HIST");
        await gaugeRepo.AddAsync(gauge);

        foreach (var daysAgo in new[] { 90, 30, 5 })
        {
            await recordRepo.AddAsync(CalibrationRecord.Create(
                Ulid.NewUlid(), gauge.Id, DateTimeOffset.UtcNow.AddDays(-daysAgo),
                CalibrationResult.Pass, $"C-{daysAgo}", "QE", DateTimeOffset.UtcNow));
        }

        var records = await recordRepo.GetByGaugeIdAsync(gauge.Id);

        Assert.Equal(3, records.Count);
        Assert.Equal("C-5", records[0].CertificateNo);   // 最新在前
        Assert.Equal("C-90", records[^1].CertificateNo);
    }

    // ═══════════════════════════════════════════════════════════
    //  GaugeDueReminderService — 状态推进 + 提醒消息
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task ReminderRunCheck_ShouldPersistOverdue_AndNotifyFeishu()
    {
        // 准备：一台已过期 + 一台远期在用（独立编号避免与其他用例串扰）
        var overdueNumber = $"GT-RM-O-{Ulid.NewUlid().ToString()[..8]}";
        var okNumber = $"GT-RM-K-{Ulid.NewUlid().ToString()[..8]}";

        using (var scope = _fixture.Services.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IGaugeRepository>();

            var overdue = Gauge.Create(
                Ulid.NewUlid(), overdueNumber, "过期样例", GaugeType.Caliper,
                "150mm", "0.02mm", "1 级", 30, DateTimeOffset.UtcNow.AddDays(-60)); // 已过期 30 天
            var healthy = CreateGauge(okNumber);
            await repo.AddAsync(overdue);
            await repo.AddAsync(healthy);
        }

        // 构造提醒服务：共享 fixture 的 scope factory + 捕获型通知器
        var notifier = new CapturingFeishuNotifier();
        var service = new GaugeDueReminderService(
            _fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            notifier,
            NullLogger<GaugeDueReminderService>.Instance);

        var (overdueCount, dueSoonCount) = await service.RunCheckAsync();

        Assert.True(overdueCount >= 1);
        Assert.True(notifier.Messages.Count >= 1);

        var message = string.Join('\n', notifier.Messages);
        Assert.Contains(overdueNumber, message);
        Assert.Contains("已过期", message);

        // 状态流转已持久化：重新读取应为 Overdue
        using var verify = _fixture.Services.CreateScope();
        var persisted = await verify.ServiceProvider.GetRequiredService<IGaugeRepository>()
            .GetByNumberAsync(overdueNumber);
        Assert.NotNull(persisted);
        Assert.Equal(GaugeStatus.Overdue, persisted!.Status);
    }

    // ── 辅助 ──

    private static Gauge CreateGauge(string number)
        => Gauge.Create(
            Ulid.NewUlid(), number, "集成测试量具", GaugeType.TorqueWrench,
            "0-100 Nm", "0.01 Nm", "0.5 级", 365,
            DateTimeOffset.UtcNow.AddDays(-30));

    /// <summary>捕获消息的飞书通知桩。</summary>
    private sealed class CapturingFeishuNotifier : IFeishuNotifier
    {
        public List<string> Messages { get; } = [];

        public Task<bool> SendTextAsync(string text, CancellationToken ct = default)
        {
            Messages.Add(text);
            return Task.FromResult(true);
        }
    }
}
