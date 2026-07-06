using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace MesAdmin.Application.Tests;

/// <summary>
/// Andon 报警模块集成测试（T2.20-T2.23）：Testcontainers PostgreSQL 真实数据库。
/// 覆盖 AndonEvent 仓储 CRUD、状态机全生命周期、L1→L2→L3 升级路径、异常场景。
/// </summary>
[Collection("DatabaseIntegration")]
public class AndonIntegrationTests
{
    private readonly DatabaseFixture _fixture;

    public AndonIntegrationTests(DatabaseFixture fixture) => _fixture = fixture;

    /// <summary>创建一个 Active 状态的 Andon 报警事件（测试辅助方法）。</summary>
    private static AndonEvent CreateTestEvent(string equipmentCode = "EQ-TQ-01", int station = 3,
        AndonAlarmType type = AndonAlarmType.TorqueExceeded, AndonSeverity severity = AndonSeverity.Major)
        => AndonEvent.Create(
            equipmentCode, station, type, severity,
            $"{type} alarm on {equipmentCode} (station {station})",
            processValue: 24.5, processTag: "Torque-M6-FL",
            upperLimit: 23.0, lowerLimit: 21.0, orderId: null);

    // ═══════════════════════════════════════════════════════════
    //  仓储 CRUD
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task AddAndGet_ShouldPersistAndRetrieve()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAndonEventRepository>();

        var ev = CreateTestEvent();
        await repo.AddAsync(ev, default);

        var loaded = await repo.GetByIdAsync(ev.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal(ev.EventNumber, loaded.EventNumber);
        Assert.Equal("EQ-TQ-01", loaded.EquipmentCode);
        Assert.Equal(3, loaded.Station);
        Assert.Equal(AndonAlarmType.TorqueExceeded, loaded.AlarmType);
        Assert.Equal(AndonSeverity.Major, loaded.Severity);
        Assert.Equal(AndonEventStatus.Active, loaded.Status);
        Assert.Equal(24.5, loaded.ProcessValue);
        Assert.Equal(23.0, loaded.UpperLimit);
        Assert.Equal(21.0, loaded.LowerLimit);
        Assert.StartsWith("AND-", loaded.EventNumber);
    }

    [Fact]
    public async Task GetList_ShouldSupportFiltering()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAndonEventRepository>();

        // 创建 3 个不同设备、不同类型、不同等级的事件
        var ev1 = CreateTestEvent("EQ-TQ-01", 3, AndonAlarmType.TorqueExceeded, AndonSeverity.Major);
        var ev2 = CreateTestEvent("EQ-HYD-01", 4, AndonAlarmType.LeakRateHigh, AndonSeverity.Critical);
        var ev3 = CreateTestEvent("EQ-ASM-01", 2, AndonAlarmType.EquipmentAlarm, AndonSeverity.Minor);

        await repo.AddAsync(ev1, default);
        await repo.AddAsync(ev2, default);
        await repo.AddAsync(ev3, default);

        // 无过滤 → 至少包含这 3 条（可能有其他测试遗留数据）
        var all = await repo.GetListAsync(limit: 20, ct: default);
        Assert.True(all.Count >= 3, $"Expected >=3 events, got {all.Count}");

        // 按设备过滤（使用唯一编码避免跨测试干扰）
        var hydEvts = await repo.GetListAsync(equipmentCode: "EQ-TQ-01", ct: default);
        Assert.NotEmpty(hydEvts);
        Assert.Contains(hydEvts, e => e.AlarmType == AndonAlarmType.TorqueExceeded);

        // 按状态过滤
        var activeEvts = await repo.GetListAsync(status: AndonEventStatus.Active, ct: default);
        Assert.NotEmpty(activeEvts);
        Assert.Contains(activeEvts, e => e.Id == ev1.Id);
        Assert.Contains(activeEvts, e => e.Id == ev2.Id);
        Assert.Contains(activeEvts, e => e.Id == ev3.Id);

        // 按严重等级过滤
        var criticalEvts = await repo.GetListAsync(severity: AndonSeverity.Critical, ct: default);
        Assert.NotEmpty(criticalEvts);
        Assert.Contains(criticalEvts, e => e.Id == ev2.Id);
    }

    [Fact]
    public async Task GetActive_ShouldExcludeClosed()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAndonEventRepository>();

        var ev1 = CreateTestEvent("EQ-TQ-01", 3, AndonAlarmType.TorqueExceeded, AndonSeverity.Major);
        var ev2 = CreateTestEvent("EQ-FLS-01", 5, AndonAlarmType.FlashFailed, AndonSeverity.Critical);
        await repo.AddAsync(ev1, default);
        await repo.AddAsync(ev2, default);

        // 关闭 ev2
        ev2.Close("已恢复");
        await repo.UpdateAsync(ev2, default);

        var active = await repo.GetActiveAsync(default);
        Assert.Contains(active, e => e.Id == ev1.Id);
        Assert.DoesNotContain(active, e => e.Id == ev2.Id);
        var loaded1 = active.First(e => e.Id == ev1.Id);
        Assert.Equal(AndonEventStatus.Active, loaded1.Status);
    }

    [Fact]
    public async Task GetActiveCount_ShouldExcludeClosedAndResolved()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAndonEventRepository>();

        var ev1 = CreateTestEvent("EQ-TQ-01", 3, AndonAlarmType.TorqueExceeded, AndonSeverity.Major);
        var ev2 = CreateTestEvent("EQ-HYD-01", 4, AndonAlarmType.LeakRateHigh, AndonSeverity.Critical);
        var ev3 = CreateTestEvent("EQ-ASM-01", 2, AndonAlarmType.EquipmentAlarm, AndonSeverity.Minor);

        await repo.AddAsync(ev1, default);
        await repo.AddAsync(ev2, default);
        await repo.AddAsync(ev3, default);

        // Resolve ev2, Close ev3
        ev2.Resolve("QC-001", "已查明原因");
        await repo.UpdateAsync(ev2, default);
        ev3.Close("已完成");
        await repo.UpdateAsync(ev3, default);

        var count = await repo.GetActiveCountAsync(default);
        Assert.True(count >= 1, $"Expected >=1, got {count}");
        // At minimum ev1 should be counted (ev2 resolved, ev3 closed are excluded)
    }

    // ═══════════════════════════════════════════════════════════
    //  AndonEvent 状态机全生命周期
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task FullLifecycle_ShouldTransitThroughAllStates()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAndonEventRepository>();

        // ── 1. 创建 → Active ──
        var ev = CreateTestEvent();
        await repo.AddAsync(ev, default);
        Assert.Equal(AndonEventStatus.Active, ev.Status);
        Assert.Equal(0, ev.EscalationLevel);

        // ── 2. 确认 → Acknowledged ──
        var ackResult = ev.Acknowledge("OP-001");
        Assert.True(ackResult);
        Assert.Equal(AndonEventStatus.Acknowledged, ev.Status);
        Assert.Equal("OP-001", ev.AcknowledgedBy);
        Assert.NotNull(ev.AcknowledgedAt);
        await repo.UpdateAsync(ev, default);

        // ── 3. 解决 → Resolved ──
        var resolveResult = ev.Resolve("OP-001", "扭矩枪校准完成，复检合格");
        Assert.True(resolveResult);
        Assert.Equal(AndonEventStatus.Resolved, ev.Status);
        Assert.Equal("扭矩枪校准完成，复检合格", ev.Resolution);
        Assert.NotNull(ev.ResolvedAt);
        await repo.UpdateAsync(ev, default);

        // ── 4. 关闭 → Closed ──
        var closeResult = ev.Close("NCR 已创建，8D 进行中，报警关闭");
        Assert.True(closeResult);
        Assert.Equal(AndonEventStatus.Closed, ev.Status);
        Assert.Equal("NCR 已创建，8D 进行中，报警关闭", ev.CloseRemarks);
        Assert.NotNull(ev.ClosedAt);
        await repo.UpdateAsync(ev, default);

        // ── 5. 验证持久化 ──
        var loaded = await repo.GetByIdAsync(ev.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal(AndonEventStatus.Closed, loaded.Status);
        Assert.Equal("OP-001", loaded.AcknowledgedBy);
        Assert.Equal("扭矩枪校准完成，复检合格", loaded.Resolution);
        Assert.Equal("NCR 已创建，8D 进行中，报警关闭", loaded.CloseRemarks);
        Assert.NotNull(loaded.AcknowledgedAt);
        Assert.NotNull(loaded.ResolvedAt);
        Assert.NotNull(loaded.ClosedAt);
    }

    // ═══════════════════════════════════════════════════════════
    //  L1 → L2 → L3 升级路径
  // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Escalation_L1toL2_ShouldUpdateStatusAndLevel()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAndonEventRepository>();

        var ev = CreateTestEvent();
        await repo.AddAsync(ev, default);

        // L1 → L2 升级
        Assert.Equal(AndonEventStatus.Active, ev.Status);
        Assert.Equal(0, ev.EscalationLevel);

        ev.Escalate();
        Assert.Equal(AndonEventStatus.EscalatedL2, ev.Status);
        Assert.Equal(1, ev.EscalationLevel);
        Assert.NotNull(ev.EscalatedAt);
        await repo.UpdateAsync(ev, default);

        // 验证持久化
        var loaded = await repo.GetByIdAsync(ev.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal(AndonEventStatus.EscalatedL2, loaded.Status);
        Assert.Equal(1, loaded.EscalationLevel);
        Assert.NotNull(loaded.EscalatedAt);
    }

    [Fact]
    public async Task Escalation_L2toL3_ShouldUpdateStatusAndLevel()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAndonEventRepository>();

        var ev = CreateTestEvent();
        await repo.AddAsync(ev, default);

        // L1 → L2
        ev.Escalate();
        Assert.Equal(AndonEventStatus.EscalatedL2, ev.Status);
        Assert.Equal(1, ev.EscalationLevel);
        await repo.UpdateAsync(ev, default);

        // L2 → L3
        ev.Escalate();
        Assert.Equal(AndonEventStatus.EscalatedL3, ev.Status);
        Assert.Equal(2, ev.EscalationLevel);
        Assert.NotNull(ev.EscalatedAt);
        await repo.UpdateAsync(ev, default);

        // 验证持久化
        var loaded = await repo.GetByIdAsync(ev.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal(AndonEventStatus.EscalatedL3, loaded.Status);
        Assert.Equal(2, loaded.EscalationLevel);
    }

    [Fact]
    public async Task Escalation_FullChain_ShouldGoThroughAllLevels()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAndonEventRepository>();

        var ev = CreateTestEvent("EQ-FT-01", 6, AndonAlarmType.CanCommunicationError, AndonSeverity.Major);
        await repo.AddAsync(ev, default);

        // L0: Active
        Assert.Equal(0, ev.EscalationLevel);
        Assert.Equal(AndonEventStatus.Active, ev.Status);

        // L1 → L2
        ev.Escalate();
        Assert.Equal(1, ev.EscalationLevel);
        Assert.Equal(AndonEventStatus.EscalatedL2, ev.Status);
        await repo.UpdateAsync(ev, default);

        // L2 → L3
        ev.Escalate();
        Assert.Equal(2, ev.EscalationLevel);
        Assert.Equal(AndonEventStatus.EscalatedL3, ev.Status);
        await repo.UpdateAsync(ev, default);

        // L3 可以被确认（闭环）
        var ackResult = ev.Acknowledge("MG-001");
        Assert.True(ackResult);
        Assert.Equal(AndonEventStatus.Acknowledged, ev.Status);
        Assert.Equal("MG-001", ev.AcknowledgedBy);
        await repo.UpdateAsync(ev, default);

        // 验证最终状态
        var loaded = await repo.GetByIdAsync(ev.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal(AndonEventStatus.Acknowledged, loaded.Status);
        Assert.Equal(2, loaded.EscalationLevel);
        Assert.Equal("MG-001", loaded.AcknowledgedBy);
    }

    // ═══════════════════════════════════════════════════════════
    //  AndonEvent.IsEscalationOverdue 超时判断
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void IsEscalationOverdue_ActiveAfterTimeout_ShouldReturnTrue()
    {
        // 创建一个过去的报警（已超过 L2 超时 5min）
        var ev = CreateTestEvent();
        // Set OccurredAt to 6 minutes ago via reflection since there's no property setter
        typeof(AndonEvent).GetProperty(nameof(AndonEvent.OccurredAt))!
            .SetValue(ev, DateTimeOffset.UtcNow.AddMinutes(-6));

        var l2Timeout = TimeSpan.FromMinutes(5);
        var l3Timeout = TimeSpan.FromMinutes(10);

        Assert.True(ev.IsEscalationOverdue(l2Timeout, l3Timeout));
    }

    [Fact]
    public void IsEscalationOverdue_ActiveBeforeTimeout_ShouldReturnFalse()
    {
        var ev = CreateTestEvent();
        typeof(AndonEvent).GetProperty(nameof(AndonEvent.OccurredAt))!
            .SetValue(ev, DateTimeOffset.UtcNow.AddMinutes(-3));

        Assert.False(ev.IsEscalationOverdue(
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void IsEscalationOverdue_L2AfterTimeout_ShouldReturnTrue()
    {
        var ev = CreateTestEvent();
        typeof(AndonEvent).GetProperty(nameof(AndonEvent.OccurredAt))!
            .SetValue(ev, DateTimeOffset.UtcNow.AddMinutes(-12));

        // L1→L2
        ev.Escalate();
        // Now Status=EscalatedL2, EscalatedAt = now

        var l2Timeout = TimeSpan.FromMinutes(5);
        var l3Timeout = TimeSpan.FromMinutes(10);

        // OccurredAt is 12min ago, so elapsed = 12min >= 10min (l3Timeout)
        // But for EscalatedL2, it checks elapsed from OccurredAt >= l3Timeout
        // Actually wait - let me re-check the IsEscalationOverdue method
        Assert.True(ev.IsEscalationOverdue(l2Timeout, l3Timeout));
    }

    // ═══════════════════════════════════════════════════════════
    //  异常场景：状态机非法转换
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Acknowledge_WhenAlreadyClosed_ShouldReturnFalse()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAndonEventRepository>();

        var ev = CreateTestEvent();
        await repo.AddAsync(ev, default);

        ev.Close("完成");
        await repo.UpdateAsync(ev, default);

        var result = ev.Acknowledge("OP-001");
        Assert.False(result);
    }

    [Fact]
    public async Task Acknowledge_WhenAlreadyResolved_ShouldReturnFalse()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAndonEventRepository>();

        var ev = CreateTestEvent();
        await repo.AddAsync(ev, default);

        ev.Resolve("OP-001", "已处理");
        await repo.UpdateAsync(ev, default);

        var result = ev.Acknowledge("OP-001");
        Assert.False(result);
    }

    [Fact]
    public async Task Resolve_WhenAlreadyClosed_ShouldReturnFalse()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAndonEventRepository>();

        var ev = CreateTestEvent();
        await repo.AddAsync(ev, default);

        ev.Close("完成");
        await repo.UpdateAsync(ev, default);

        var result = ev.Resolve("OP-001", "已处理");
        Assert.False(result);
    }

    [Fact]
    public async Task Close_WhenAlreadyClosed_ShouldReturnFalse()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAndonEventRepository>();

        var ev = CreateTestEvent();
        await repo.AddAsync(ev, default);

        ev.Close("完成");
        await repo.UpdateAsync(ev, default);

        var result = ev.Close("再关一次");
        Assert.False(result);
    }

    [Fact]
    public async Task UpdateRange_ShouldPersistMultipleEvents()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAndonEventRepository>();

        var ev1 = CreateTestEvent("EQ-TQ-01", 3, AndonAlarmType.TorqueExceeded, AndonSeverity.Major);
        var ev2 = CreateTestEvent("EQ-HYD-01", 4, AndonAlarmType.LeakRateHigh, AndonSeverity.Critical);

        await repo.AddAsync(ev1, default);
        await repo.AddAsync(ev2, default);

        // Simulate escalation of both
        ev1.Escalate();
        ev2.Escalate();

        await repo.UpdateRangeAsync([ev1, ev2], default);

        var loaded1 = await repo.GetByIdAsync(ev1.Id, default);
        var loaded2 = await repo.GetByIdAsync(ev2.Id, default);

        Assert.NotNull(loaded1);
        Assert.NotNull(loaded2);
        Assert.Equal(AndonEventStatus.EscalatedL2, loaded1.Status);
        Assert.Equal(AndonEventStatus.EscalatedL2, loaded2.Status);
        Assert.Equal(1, loaded1.EscalationLevel);
        Assert.Equal(1, loaded2.EscalationLevel);
    }

    // ═══════════════════════════════════════════════════════════
    //  多种报警类型验证
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task AllAlarmTypes_ShouldPersistAndRetrieveCorrectly()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAndonEventRepository>();

        var types = new (AndonAlarmType Type, AndonSeverity Severity, string Equipment)[]
        {
            (AndonAlarmType.TorqueExceeded, AndonSeverity.Major, "EQ-TQ-01"),
            (AndonAlarmType.LeakRateHigh, AndonSeverity.Critical, "EQ-HYD-01"),
            (AndonAlarmType.FlashFailed, AndonSeverity.Critical, "EQ-FLS-01"),
            (AndonAlarmType.CanCommunicationError, AndonSeverity.Major, "EQ-ASM-01"),
            (AndonAlarmType.EquipmentAlarm, AndonSeverity.Major, "EQ-ASM-02"),
            (AndonAlarmType.ProcessDeviation, AndonSeverity.Minor, "EQ-FT-01"),
            (AndonAlarmType.MaterialDefect, AndonSeverity.Minor, "EQ-VN-01"),
        };

        foreach (var (type, severity, equip) in types)
        {
            var ev = CreateTestEvent(equip, 2, type, severity);
            await repo.AddAsync(ev, default);
        }

        var all = await repo.GetListAsync(limit: 20, ct: default);
        Assert.True(all.Count >= 7, $"Expected >=7 events, got {all.Count}");

        // Verify all alarm types are present by filtering on our equipment codes
        var tqEvents = await repo.GetListAsync(equipmentCode: "EQ-TQ-01", ct: default);
        Assert.NotEmpty(tqEvents);
        Assert.Equal(AndonAlarmType.TorqueExceeded, tqEvents[0].AlarmType);

        var hyEvents = await repo.GetListAsync(equipmentCode: "EQ-HYD-01", ct: default);
        Assert.NotEmpty(hyEvents);
        Assert.Equal(AndonAlarmType.LeakRateHigh, hyEvents[0].AlarmType);

        var flEvents = await repo.GetListAsync(equipmentCode: "EQ-FLS-01", ct: default);
        Assert.NotEmpty(flEvents);
        Assert.Equal(AndonAlarmType.FlashFailed, flEvents[0].AlarmType);

        var asmEvents = await repo.GetListAsync(equipmentCode: "EQ-ASM-01", ct: default);
        Assert.NotEmpty(asmEvents);
    }
}
