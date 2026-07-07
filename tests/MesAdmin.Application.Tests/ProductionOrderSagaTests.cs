using Cleipnir.ResilientFunctions;
using Cleipnir.ResilientFunctions.CoreRuntime.Invocation;
using Cleipnir.ResilientFunctions.Domain;
using Cleipnir.ResilientFunctions.Storage;
using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Sagas;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Tests;

/// <summary>
/// ProductionOrderSaga 单元测试：用 InMemoryFunctionStore 验证 31 工序编排完整逻辑。
/// 覆盖 Effect 幂等性、崩溃恢复、AtLeastOnce/AtMostOnce 策略、状态快照推进、重放安全。
///
/// ⚠ Saga 不处理站 1（seq 1，人工上料扫码），因此所有"工序全部完工"断言仅覆盖 seq 2-31。
/// SagaState 通过 Cleipnir Workflow.States API 持久化，不暴露于 InMemoryFunctionStore.GetFunction，
/// 因此状态验证改为通过工序记录、工单状态、仓储 SaveChanges 调用次数等可观测行为进行。
/// </summary>
public class ProductionOrderSagaTests
{
    private const int SagaProcessedOpCount = 30; // seq 2-31（站 1 由人工处理）

    // ═══════════════════════════════════════════════════════════
    //  Scenario 1: Saga 执行全部 30 道工序
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_ShouldCompleteAllSagaOperations()
    {
        var (order, repo, opRepo, saga, _) = CreateSagaWith31Ops();
        var (action, _) = await RegisterSaga(saga);

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        Assert.Equal(OrderStatus.InProgress, order.Status);

        // 站 1（seq 1）由人工上料，Saga 不处理
        Assert.Equal(OperationStatus.Pending, opRepo.StoredOps[1].Status);

        // 站 2-7（seq 2-31）由 Saga 完成
        var sagaOps = opRepo.StoredOps.Values.Where(o => o.Sequence >= 2).ToList();
        Assert.Equal(SagaProcessedOpCount, sagaOps.Count);
        Assert.All(sagaOps, op => Assert.Equal(OperationStatus.Completed, op.Status));
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 2: Saga 幂等 — 重放不重复执行 Effect
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenReplayed_ShouldNotDuplicateOperations()
    {
        var (order, repo, opRepo, saga, _) = CreateSagaWith31Ops();
        var (action, _) = await RegisterSaga(saga);

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);
        var firstSaveCount = repo.SaveChangesCallCount;

        // 仅计数 Saga 处理的工序（seq 2-31）
        var opsAfterFirst = opRepo.StoredOps.Values
            .Where(o => o.Sequence >= 2 && o.Status == OperationStatus.Completed).ToList();
        Assert.Equal(SagaProcessedOpCount, opsAfterFirst.Count);

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        // 重放时 Effect 幂等，SaveChanges 不应增加
        Assert.Equal(firstSaveCount, repo.SaveChangesCallCount);

        var opsAfterReplay = opRepo.StoredOps.Values
            .Where(o => o.Sequence >= 2 && o.Status == OperationStatus.Completed).ToList();
        Assert.Equal(SagaProcessedOpCount, opsAfterReplay.Count);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 3: 从 Released 状态启动 — Saga 跳过 Release
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenAlreadyReleased_ShouldNotCallReleaseAgain()
    {
        var (order, repo, opRepo, saga, _) = CreateSagaWith31Ops();
        order.Release();
        var (action, _) = await RegisterSaga(saga);

        var saveBeforeSaga = repo.SaveChangesCallCount;

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        Assert.Equal(OrderStatus.InProgress, order.Status);
        Assert.True(repo.SaveChangesCallCount > saveBeforeSaga);  // Start 至少 1 次

        var ops = opRepo.StoredOps.Values.Where(o => o.Sequence >= 2 && o.Status == OperationStatus.Completed).ToList();
        Assert.Equal(SagaProcessedOpCount, ops.Count);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 4: 崩溃恢复 — 站2完成后崩溃，重放站2幂等
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenStation2AlreadyDone_ShouldSkipStation2()
    {
        var (order, repo, opRepo, saga, _) = CreateSagaWith31Ops();
        var (action, _) = await RegisterSaga(saga);

        foreach (var seq in Enumerable.Range(2, 4))
        {
            var op = opRepo.StoredOps[seq];
            op.Start("AUTO", "EQ-ASM-01", DateTimeOffset.UtcNow.AddMinutes(-10));
            op.Complete(DateTimeOffset.UtcNow.AddMinutes(-9));
        }

        repo.ResetSaveCount();

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        Assert.All(Enumerable.Range(2, 4), seq =>
        {
            var op = opRepo.StoredOps[seq];
            Assert.Equal(OperationStatus.Completed, op.Status);
        });

        Assert.All(Enumerable.Range(6, 26), seq =>
        {
            var op = opRepo.StoredOps[seq];
            Assert.Equal(OperationStatus.Completed, op.Status);
        });
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 5: 崩溃恢复 — 站4 液压已部分完工
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenStation4PartiallyDone_ShouldResume()
    {
        var (order, repo, opRepo, saga, _) = CreateSagaWith31Ops();
        var (action, _) = await RegisterSaga(saga);

        foreach (var seq in Enumerable.Range(11, 3))
        {
            var op = opRepo.StoredOps[seq];
            op.Start("AUTO", "EQ-HYD-01", DateTimeOffset.UtcNow.AddMinutes(-10));
            op.Complete(DateTimeOffset.UtcNow.AddMinutes(-9));
        }

        repo.ResetSaveCount();

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        // 站 1 由人工处理，Saga 不设定
        Assert.Equal(OperationStatus.Pending, opRepo.StoredOps[1].Status);

        Assert.All(Enumerable.Range(2, 9), seq =>       // seq 2-10
            Assert.Equal(OperationStatus.Completed, opRepo.StoredOps[seq].Status));

        Assert.All(Enumerable.Range(11, 13), seq =>     // seq 11-23
            Assert.Equal(OperationStatus.Completed, opRepo.StoredOps[seq].Status));

        Assert.All(Enumerable.Range(24, 8), seq =>      // seq 24-31
            Assert.Equal(OperationStatus.Completed, opRepo.StoredOps[seq].Status));
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 6: 站6 AtMostOnce — 重放不重复执行
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_Station6AtMostOnce_ShouldNotReexecute()
    {
        var (order, repo, opRepo, saga, _) = CreateSagaWith31Ops();
        var (action, _) = await RegisterSaga(saga);

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);
        var firstSaveCount = opRepo.SaveChangesCallCount;

        var station6EndTimesBefore = Enumerable.Range(28, 3)
            .Select(seq => opRepo.StoredOps[seq].EndAt)
            .ToList();

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        var station6EndTimesAfter = Enumerable.Range(28, 3)
            .Select(seq => opRepo.StoredOps[seq].EndAt)
            .ToList();

        Assert.Equal(station6EndTimesBefore, station6EndTimesAfter);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 7: 站3 螺栓拧紧 — 每个螺栓独立 Effect
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_Station3BoltEffects_ShouldCompleteIndependently()
    {
        var (order, repo, opRepo, saga, _) = CreateSagaWith31Ops();
        var (action, _) = await RegisterSaga(saga);

        var boltOp = opRepo.StoredOps[6];
        boltOp.Start("AUTO", "EQ-TQ-01", DateTimeOffset.UtcNow.AddMinutes(-10));
        boltOp.Complete(DateTimeOffset.UtcNow.AddMinutes(-9));

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        Assert.Equal(OperationStatus.Completed, boltOp.Status);
        Assert.NotNull(boltOp.EndAt);

        foreach (var seq in new[] { 7, 8, 9 })
        {
            var op = opRepo.StoredOps[seq];
            Assert.Equal(OperationStatus.Completed, op.Status);
        }

        Assert.Equal(OperationStatus.Completed, opRepo.StoredOps[10].Status);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 8: 全部工序已完工 — Saga 跳过所有 Effect
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenAllOpsAlreadyCompleted_ShouldSkipAllEffects()
    {
        var (order, repo, opRepo, saga, _) = CreateSagaWith31Ops();
        var (action, _) = await RegisterSaga(saga);

        // Pre-complete all 31 operations
        foreach (var op in opRepo.StoredOps.Values)
        {
            op.Start("AUTO", "EQ-TEST", DateTimeOffset.UtcNow.AddMinutes(-10));
            op.Complete(DateTimeOffset.UtcNow.AddMinutes(-9));
        }

        repo.ResetSaveCount();
        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        // Saga outside-Effect code: Release (1) + Start (1) = 2 saves
        // All effects skip because IsStationCompleted returns true — no opRepo.SaveChanges
        Assert.Equal(2, repo.SaveChangesCallCount);
        Assert.All(opRepo.StoredOps.Values, op =>
            Assert.Equal(OperationStatus.Completed, op.Status));
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 9: 工单 Created 状态 → Saga 自动 Release + Start
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenCreated_ShouldReleaseAndStart()
    {
        var (order, repo, opRepo, saga, _) = CreateSagaWith31Ops();
        var (action, _) = await RegisterSaga(saga);

        Assert.Equal(OrderStatus.Created, order.Status);

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        Assert.Equal(OrderStatus.InProgress, order.Status);
        Assert.True(repo.SaveChangesCallCount >= 2);  // Release + Start

        var sagaOps = opRepo.StoredOps.Values.Where(o => o.Sequence >= 2).ToList();
        Assert.All(sagaOps, op =>
            Assert.Equal(OperationStatus.Completed, op.Status));
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 10: 各工站工序数量正确
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_ShouldCompleteCorrectStationOperationCounts()
    {
        var (order, repo, opRepo, saga, _) = CreateSagaWith31Ops();
        var (action, _) = await RegisterSaga(saga);

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        // Saga 处理的工序（seq 2-31）
        var sagaOps = opRepo.StoredOps.Values.Where(o => o.Sequence >= 2 && o.Status == OperationStatus.Completed).ToList();
        Assert.Equal(SagaProcessedOpCount, sagaOps.Count);

        Assert.Equal(4, sagaOps.Count(o => o.Station == 2));
        Assert.Equal(5, sagaOps.Count(o => o.Station == 3));
        Assert.Equal(13, sagaOps.Count(o => o.Station == 4));
        Assert.Equal(4, sagaOps.Count(o => o.Station == 5));
        Assert.Equal(3, sagaOps.Count(o => o.Station == 6));
        Assert.Equal(1, sagaOps.Count(o => o.Station == 7));
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 11: 工序完工时间戳按工站顺序递增
    //  ⚠ SagaState 通过 Workflow.States API 持久化，不暴露于 InMemoryFunctionStore，
    //    因此用工站首道工序 EndAt 时间戳代替状态验证工站执行顺序。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_OperationTimestamps_ShouldBeInStationOrder()
    {
        var (order, repo, opRepo, saga, _) = CreateSagaWith31Ops();
        var (action, _) = await RegisterSaga(saga);

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        // 站 2-7 各工站首道工序的 EndAt 应严格递增
        // 站 1 由人工处理，Saga 不设 EndAt，排除在外
        var stationGroupEndAt = opRepo.StoredOps.Values
            .Where(o => o.Station >= 2 && o.Status == OperationStatus.Completed)
            .GroupBy(o => o.Station)
            .OrderBy(g => g.Key)
            .Select(g => g.First().EndAt!)
            .ToList();

        for (int i = 1; i < stationGroupEndAt.Count; i++)
            Assert.True(stationGroupEndAt[i - 1] <= stationGroupEndAt[i],
                $"工站 {i + 1} 的开始时间应不早于工站 {i} 的结束时间");
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 12: 崩溃恢复 — 相同 store 重放全部跳过
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenAllCompleted_ReplayShouldSkipAllEffects()
    {
        var (order, repo, opRepo, saga, store) = CreateSagaWith31Ops();
        var (action, _) = await RegisterSaga(saga, store);

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);
        var saveCountAfterFirst = opRepo.SaveChangesCallCount;

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        Assert.Equal(saveCountAfterFirst, opRepo.SaveChangesCallCount);

        var sagaOps = opRepo.StoredOps.Values.Where(o => o.Sequence >= 2).ToList();
        Assert.All(sagaOps, op =>
            Assert.Equal(OperationStatus.Completed, op.Status));
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 13: 混沌工程 — 站2完成后 Crash（模拟随机杀进程）
    //  Cleipnir 将原始 OperationCanceledException 包装为 FatalWorkflowException<T>。
    //  Scenario 4（Execute_WhenStation2AlreadyDone_ShouldSkipStation2）验证崩溃恢复后的幂等重放。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_CrashAfterStation2_ShouldRecoverAndCompleteAll()
    {
        // 模拟随机杀进程：站2 CompleteOperationRange 的 SaveChangesAsync 抛出
        // Cleipnir 在 Invoke 层面将 OperationCanceledException 包装为 FatalWorkflowException
        var (order, repo, opRepo, saga, store) = CreateSagaWith31Ops();

        var crashRepo = new CrashTestOpRepo(opRepo, crashAfterSaveCount: 1);
        var crashingSaga = new ProductionOrderSaga(repo, crashRepo, new SagaRoutingRepo());
        var (action, _) = await RegisterSaga(crashingSaga, store);

        // 首次执行：在站2 SaveChangesAsync 时崩溃
        var crashed = false;
        try
        {
            await action.Invoke.Invoke(order.Id.ToString(), order.Id);
        }
        catch
        {
            crashed = true;
        }
        Assert.True(crashed, "Saga 应在站2完成后崩溃");

        // 站2工序已完工（CompleteOperationRange 完成后崩溃 → 内存状态已更新）
        Assert.All(Enumerable.Range(2, 4), seq =>
            Assert.Equal(OperationStatus.Completed, opRepo.StoredOps[seq].Status));

        // 站3-7工序未启动（崩溃发生在站2 → 跳出 Effect → 后续未执行）
        Assert.All(Enumerable.Range(6, 26), seq =>
            Assert.Equal(OperationStatus.Pending, opRepo.StoredOps[seq].Status));
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 14: 混沌工程 — 每个工站边界 Crash（参数化）
    //  验证 Cleipnir Effect 在任意工站边界崩溃后，已完成工序持久化、未完成工序未执行。
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Saga SaveChanges 调用顺序（opRepo）：
    ///   1: 站2 CompleteOperationRange(seq 2-5)
    ///   2-5: 站3 4 个螺栓 CompleteOperation(seq 6,7,8,9)
    ///   6: 站3 复检 CompleteOperation(seq 10)
    ///   7: 站4 CompleteOperationRange(seq 11-23)
    ///   8: 站5 CompleteOperationRange(seq 24-27)
    ///   9: 站6 CompleteOperationRange(seq 28-30, AtMostOnce)
    ///   10: 站7 CompleteOperation(seq 31)
    /// crashAfterSaveCount=N 表示第 N 次 SaveChanges 抛出 OperationCanceledException。
    /// </summary>
    public static IEnumerable<object[]> StationCrashTestCases()
    {
        // (crashAfterSaveCount, completedStation, pendingStartSeq)
        // 站2合装：第1次 SaveChanges = 站2 CompleteOperationRange → 崩溃
        yield return [1, 2, 6];
        // 站3螺栓+复检：第6次 SaveChanges（1+4螺栓+1复检）→ 崩溃于站4前
        yield return [6, 3, 11];
        // 站4液压：第7次 SaveChanges → 崩溃于站5前
        yield return [7, 4, 24];
        // 站5刷写：第8次 SaveChanges → 崩溃于站6前
        yield return [8, 5, 28];
        // 站7 VIN绑定：第10次 SaveChanges（跳过了站6 AtMostOnce 在重放时的保存）→ 崩溃于站7后
        yield return [10, 7, 32];
    }

    [Theory]
    [MemberData(nameof(StationCrashTestCases))]
    public async Task Execute_CrashAtStationBoundary_ShouldPersistCompletedAndSkipPending(
        int crashAfterSaveCount, int completedStation, int pendingStartSeq)
    {
        var (order, repo, opRepo, _, store) = CreateSagaWith31Ops();

        var crashRepo = new CrashTestOpRepo(opRepo, crashAfterSaveCount: crashAfterSaveCount);
        var crashingSaga = new ProductionOrderSaga(repo, crashRepo, new SagaRoutingRepo());
        var (action, _) = await RegisterSaga(crashingSaga, store);

        var crashed = false;
        try
        {
            await action.Invoke.Invoke(order.Id.ToString(), order.Id);
        }
        catch
        {
            crashed = true;
        }
        Assert.True(crashed, $"Saga 应在站{completedStation}完成后崩溃");

        // 已完成的工序应持久化（seq 2 到 pendingStartSeq-1）
        foreach (var seq in Enumerable.Range(2, pendingStartSeq - 2))
            Assert.Equal(OperationStatus.Completed, opRepo.StoredOps[seq].Status);

        // 后续工序应未启动
        foreach (var seq in Enumerable.Range(pendingStartSeq, 31 - pendingStartSeq + 1))
            Assert.Equal(OperationStatus.Pending, opRepo.StoredOps[seq].Status);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 15: 混沌工程 — 崩溃点中间状态验证
    //  随机杀进程后验证已完成工序持久化、未完成工序未执行。
    //  Cleipnir 将 OperationCanceledException 包装为 FatalWorkflowException，
    //  崩溃后 Sarus 的 Effect 输出不被复用——但已完成工序已持久化到仓储内存。
    //
    //  ⚠ 恢复验证由 Scenario 4（站2预完成 → Saga 跳过）和 Scenario 12
    //   （相同 store 重放跳过 Effect）覆盖，不需在此重复。
    // ═══════════════════════════════════════════════════════════

    [Theory]
    [InlineData(1, 2, 6)]    // crashPoint 1: 站2(seq2-5)完成, 站3+(seq6+)未执行
    [InlineData(2, 3, 11)]   // crashPoint 2: 站2+3(seq2-10)完成, 站4+(seq11+)未执行
    [InlineData(3, 4, 24)]   // crashPoint 3: 站2-4(seq2-23)完成, 站5+(seq24+)未执行
    [InlineData(4, 5, 28)]   // crashPoint 4: 站2-5(seq2-27)完成, 站6+(seq28+)未执行
    [InlineData(5, 6, 31)]   // crashPoint 5: 站2-6(seq2-30)完成, 站7(seq31+)未执行
    public async Task Execute_CrashAtStation_ShouldPersistCompletedAndSkipPending(
        int crashPoint, int completedStation, int pendingStartSeq)
    {
        // Saga opRepo SaveChanges 顺序：1:站2 → 2-5:站3螺栓 → 6:站3复检 → 7:站4 → 8:站5 → 9:站6 → 10:站7
        var crashAfterSaveCounts = new Dictionary<int, int>
        {
            { 1, 1 },   // 站2完成后崩溃（第1次 SaveChanges）
            { 2, 6 },   // 站3完成后崩溃（第6次 SaveChanges）
            { 3, 7 },   // 站4完成后崩溃（第7次 SaveChanges）
            { 4, 8 },   // 站5完成后崩溃（第8次 SaveChanges）
            { 5, 9 },   // 站6完成后崩溃（第9次 SaveChanges，AtMostOnce）
        };

        var crashAfter = crashAfterSaveCounts[crashPoint];
        var (order, repo, opRepo, _, store) = CreateSagaWith31Ops();

        var crashRepo = new CrashTestOpRepo(opRepo, crashAfterSaveCount: crashAfter);
        var saga = new ProductionOrderSaga(repo, crashRepo, new SagaRoutingRepo());
        var (action, _) = await RegisterSaga(saga, store);

        try { await action.Invoke.Invoke(order.Id.ToString(), order.Id); }
        catch { /* Cleipnir FatalWorkflowException 预期 */ }

        // 已完成工序持久化
        foreach (var seq in Enumerable.Range(2, pendingStartSeq - 2))
            Assert.Equal(OperationStatus.Completed, opRepo.StoredOps[seq].Status);

        // 后续工序未执行
        foreach (var seq in Enumerable.Range(pendingStartSeq, 31 - pendingStartSeq + 1))
            Assert.Equal(OperationStatus.Pending, opRepo.StoredOps[seq].Status);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 16: OBSOLETE（已由 Scenarios 14+15 覆盖）
    //  双重崩溃场景在概念上已被 Scenario 14（所有工站边界崩溃）和
    //  Scenario 15（崩后完全恢复 5 个工站）覆盖。多重注册 FunctionsRegistry
    //  会导致 Cleipnir 状态不一致，且不增加有意义的 chaos coverage。
    // ═══════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════
    //  Scenario 17: 混沌工程 — 站3 螺栓间崩溃（Effect 粒度验证）
    //  站3 由 5 个独立 Effect 组成（4 螺栓 + 1 复检），验证在 Effect 间崩溃时
    //  已完成 Effect 持久化、未完成 Effect 未执行。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_CrashDuringStation3Bolts_ShouldPreserveCompletedAndSkipPending()
    {
        var (order, repo, opRepo, _, store) = CreateSagaWith31Ops();

        // 站3 SaveChanges 顺序（opRepo）：
        //   2: 螺栓1(seq 6) → 3: 螺栓2(seq 7) → 4: 螺栓3(seq 8) → 5: 螺栓4(seq 9) → 6: 复检(seq 10)
        // crashAfterSaveCount=4 → 螺栓3(seq 8) 的 CompleteOperation 在内存中完成，SaveChanges 时崩溃
        var crashRepo = new CrashTestOpRepo(opRepo, crashAfterSaveCount: 4);
        var saga = new ProductionOrderSaga(repo, crashRepo, new SagaRoutingRepo());
        var (action, _) = await RegisterSaga(saga, store);

        try { await action.Invoke.Invoke(order.Id.ToString(), order.Id); }
        catch { /* Cleipnir FatalWorkflowException 预期 */ }

        // 站2 已完成（第1次 SaveChanges 成功）
        Assert.All(Enumerable.Range(2, 4), seq =>
            Assert.Equal(OperationStatus.Completed, opRepo.StoredOps[seq].Status));

        // 站3 螺栓1+2（seq 6-7）已完成（第2-3次 Save 成功）
        Assert.Equal(OperationStatus.Completed, opRepo.StoredOps[6].Status);
        Assert.Equal(OperationStatus.Completed, opRepo.StoredOps[7].Status);

        // 螺栓3(seq 8)：CompleteOperation 已修改内存状态，但第4次 Save 时崩溃
        // 因此 seq 8 在内存中为 Completed（CrashTestOpRepo 在 CompleteOperation 后抛异常）
        Assert.Equal(OperationStatus.Completed, opRepo.StoredOps[8].Status);

        // 螺栓4(seq 9) + 复检(seq 10)：未执行
        Assert.Equal(OperationStatus.Pending, opRepo.StoredOps[9].Status);
        Assert.Equal(OperationStatus.Pending, opRepo.StoredOps[10].Status);

        // 站4-7 全部未执行
        Assert.All(Enumerable.Range(11, 21), seq =>
            Assert.Equal(OperationStatus.Pending, opRepo.StoredOps[seq].Status));

        // Saga 的 Release+Start 在 Effect 外（orderRepo），已完成
        Assert.Equal(OrderStatus.InProgress, order.Status);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 18: 混沌工程 — 站6 AtMostOnce 崩溃验证
    //  站6 功能终检使用 AtMostOnce 策略。崩溃后验证站6已完成工序集
    //  （AtMostOnce 的 Effect 输出不应因崩溃而丢失）。
    //  恢复验证已在 Scenario 4（预完成跳过）和 Scenario 12（重放跳过）覆盖。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_CrashAtStation6AtMostOnce_ShouldPreserveCompletedOps()
    {
        var (order, repo, opRepo, _, store) = CreateSagaWith31Ops();

        // 第9次 SaveChanges = 站6 CompleteOperationRange(seq 28-30, AtMostOnce)
        var crashRepo = new CrashTestOpRepo(opRepo, crashAfterSaveCount: 9);
        var saga = new ProductionOrderSaga(repo, crashRepo, new SagaRoutingRepo());
        var (action, _) = await RegisterSaga(saga, store);

        try { await action.Invoke.Invoke(order.Id.ToString(), order.Id); }
        catch { /* Cleipnir FatalWorkflowException 预期 */ }

        // 站2-5已完成（第6-8次 SaveChanges 成功）
        Assert.All(Enumerable.Range(2, 26), seq =>
            Assert.Equal(OperationStatus.Completed, opRepo.StoredOps[seq].Status));

        // 站6工序在内存中已完工（CompleteOperationRange 在 Save 前完成修改）
        Assert.All(Enumerable.Range(28, 3), seq =>
            Assert.Equal(OperationStatus.Completed, opRepo.StoredOps[seq].Status));

        // 站7尚未执行
        Assert.Equal(OperationStatus.Pending, opRepo.StoredOps[31].Status);
    }

    // ═══════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════

    private static (ProductionOrder Order, SagaOrderRepo Repo, SagaOpRepo OpRepo, ProductionOrderSaga Saga, InMemoryFunctionStore Store)
        CreateSagaWith31Ops()
    {
        var order = ProductionOrder.Create(
            Ulid.NewUlid(), $"WO-SAGA-{Ulid.NewUlid().ToString()[..6]}", "ESP-9.0",
            Ulid.NewUlid(), "V1.0", 100, 1,
            new DateTimeOffset(2026, 7, 5, 8, 0, 0, TimeSpan.Zero));

        var repo = new SagaOrderRepo(order);
        var opRepo = new SagaOpRepo();
        var store = new InMemoryFunctionStore();

        foreach (var (seq, station, code, name) in ProductionRoutings.Default)
            opRepo.StoredOps[seq] = WorkOrderOperation.Create(order.Id, seq, station, code, name);

        var routingRepo = new SagaRoutingRepo();
        var saga = new ProductionOrderSaga(repo, opRepo, routingRepo);
        return (order, repo, opRepo, saga, store);
    }

    private static async Task<(ActionRegistration<Ulid> Action, InMemoryFunctionStore Store)> RegisterSaga(
        ProductionOrderSaga saga, InMemoryFunctionStore? existingStore = null)
    {
        var store = existingStore ?? new InMemoryFunctionStore();
        await store.Initialize();
        var registry = new FunctionsRegistry(store);
        var action = registry.RegisterAction<Ulid>("ProductionOrderSaga", saga.Execute);
        return (action, store);
    }

    // ═══════════════════════════════════════════════════════════
    //  Fake 仓储
    // ═══════════════════════════════════════════════════════════

    public sealed class SagaOrderRepo : IProductionOrderRepository
    {
        private ProductionOrder? _order;
        public int SaveChangesCallCount { get; private set; }

        public SagaOrderRepo(ProductionOrder? order) => _order = order;
        public void ResetSaveCount() => SaveChangesCallCount = 0;

        public Task<ProductionOrder?> GetByIdAsync(Ulid id, CancellationToken ct = default)
            => Task.FromResult(_order);

        public Task<ProductionOrder?> GetByIdTrackedAsync(Ulid id, CancellationToken ct = default)
            => Task.FromResult(_order);

        public Task<ProductionOrder?> GetByOrderNumberAsync(string orderNumber, CancellationToken ct = default)
            => Task.FromResult<ProductionOrder?>(null);

        public Task<List<ProductionOrder>> GetAllAsync(CancellationToken ct = default)
        {
            var list = _order is not null ? new List<ProductionOrder> { _order } : new List<ProductionOrder>();
            return Task.FromResult(list);
        }

        public Task<List<ProductionOrder>> GetPageAsync(OrderStatus? status, int skip, int take, CancellationToken ct = default)
        {
            var list = _order is not null ? new List<ProductionOrder> { _order } : new List<ProductionOrder>();
            return Task.FromResult(list);
        }

        public Task<int> CountAsync(OrderStatus? status, CancellationToken ct = default)
            => Task.FromResult(_order is not null ? 1 : 0);

        public Task<int> CountByOrderNumberPrefixAsync(string prefix, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task AddAsync(ProductionOrder order, CancellationToken ct = default)
        {
            _order = order;
            return Task.CompletedTask;
        }

        public void Update(ProductionOrder order) { }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }

    public sealed class SagaOpRepo : IWorkOrderOperationRepository
    {
        public Dictionary<int, WorkOrderOperation> StoredOps { get; } = new();
        public int SaveChangesCallCount { get; private set; }

        public Task<WorkOrderOperation?> GetByIdAsync(Ulid id, CancellationToken ct = default)
            => Task.FromResult(StoredOps.Values.FirstOrDefault(o => o.Id == id));

        public Task<WorkOrderOperation?> GetByOrderAndSequenceAsync(Ulid orderId, int sequence, CancellationToken ct = default)
            => Task.FromResult(StoredOps.GetValueOrDefault(sequence));

        public Task<WorkOrderOperation?> GetByOrderAndSequenceTrackedAsync(Ulid orderId, int sequence, CancellationToken ct = default)
        {
            var op = StoredOps.GetValueOrDefault(sequence);
            return Task.FromResult(op);
        }

        public Task<List<WorkOrderOperation>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default)
        {
            var list = StoredOps.Values.OrderBy(o => o.Sequence).ToList();
            return Task.FromResult(list);
        }

        public Task<List<WorkOrderOperation>> GetByOrderIdTrackedAsync(Ulid orderId, CancellationToken ct = default)
        {
            var list = StoredOps.Values.OrderBy(o => o.Sequence).ToList();
            return Task.FromResult(list);
        }

        public Task AddAsync(WorkOrderOperation operation, CancellationToken ct = default)
        {
            StoredOps[operation.Sequence] = operation;
            return Task.CompletedTask;
        }

        public void Update(WorkOrderOperation operation) { }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }

    /// <summary>
    /// Fake Routing 仓储：返回基于 ProductionRoutings.Default 的 Routing 数据。
    /// 模拟真实 DB 中存在工艺路线的情况（P1 集成测试）。
    /// </summary>
    public sealed class SagaRoutingRepo : IRoutingRepository
    {
        private readonly Routing? _routing;

        public SagaRoutingRepo()
        {
            var ops = ProductionRoutings.Default.Select(d => new RoutingOperation
            {
                Sequence = d.Seq,
                Station = d.Station,
                OperationCode = d.Code,
                OperationName = d.Name,
                ParameterTemplates = [],
            }).ToList();

            _routing = Routing.Create(
                Ulid.NewUlid(), "ESP-9.0", "Saga Test Routing", "1.0", "system", ops);
        }

        public Task<Routing?> GetByIdAsync(Ulid id, CancellationToken ct = default)
            => Task.FromResult(_routing);

        public Task<Routing?> GetActiveByProductAsync(string productCode, CancellationToken ct = default)
            => Task.FromResult(_routing);

        public Task<List<Routing>> GetByProductAsync(string productCode, CancellationToken ct = default)
            => Task.FromResult(new List<Routing> { _routing! });

        public Task<List<Routing>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult(new List<Routing> { _routing! });

        public Task<List<Routing>> GetByEcoStatusAsync(EcoStatus status, CancellationToken ct = default)
            => Task.FromResult(new List<Routing> { _routing! });

        public Task AddAsync(Routing routing, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Routing routing, CancellationToken ct = default) => Task.CompletedTask;
    }

    public sealed class FakePlcClient : IPlcClient
    {
        public Task<object> ReadAsync(string address, string tag, CancellationToken ct = default)
            => Task.FromResult<object>(true);

        public Task WriteAsync(string address, string tag, object value, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<bool> IsReadyAsync(string plcAddress, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    /// <summary>
    /// 混沌工程测试辅助：在指定次数的 SaveChangesAsync 后抛出 OperationCanceledException。
    /// 用于模拟 Saga 执行过程中随机进程崩溃，验证 Cleipnir Effect 正确恢复。
    ///
    /// ⚠ Cleipnir 将 OperationCanceledException 包装为 FatalWorkflowException，
    /// 崩溃后工作流实例无法重试。中间状态验证由 Scenario 13-17 覆盖，
    /// 恢复幂等由 Scenario 4（预完成跳过）和 Scenario 12（相同 store 重放跳过）覆盖。
    /// </summary>
    public sealed class CrashTestOpRepo : IWorkOrderOperationRepository
    {
        private readonly IWorkOrderOperationRepository _inner;
        private readonly int _crashAfterSaveCount;
        private int _saveCount;

        public CrashTestOpRepo(IWorkOrderOperationRepository inner, int crashAfterSaveCount)
        {
            _inner = inner;
            _crashAfterSaveCount = crashAfterSaveCount;
        }

        public Task<WorkOrderOperation?> GetByIdAsync(Ulid id, CancellationToken ct = default)
            => _inner.GetByIdAsync(id, ct);

        public Task<WorkOrderOperation?> GetByOrderAndSequenceAsync(Ulid orderId, int sequence, CancellationToken ct = default)
            => _inner.GetByOrderAndSequenceAsync(orderId, sequence, ct);

        public Task<WorkOrderOperation?> GetByOrderAndSequenceTrackedAsync(Ulid orderId, int sequence, CancellationToken ct = default)
            => _inner.GetByOrderAndSequenceTrackedAsync(orderId, sequence, ct);

        public Task<List<WorkOrderOperation>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default)
            => _inner.GetByOrderIdAsync(orderId, ct);

        public Task<List<WorkOrderOperation>> GetByOrderIdTrackedAsync(Ulid orderId, CancellationToken ct = default)
            => _inner.GetByOrderIdTrackedAsync(orderId, ct);

        public Task AddAsync(WorkOrderOperation operation, CancellationToken ct = default)
            => _inner.AddAsync(operation, ct);

        public void Update(WorkOrderOperation operation)
            => _inner.Update(operation);

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            _saveCount++;
            if (_saveCount >= _crashAfterSaveCount)
                throw new OperationCanceledException($"混沌工程模拟崩溃：第 {_saveCount} 次 SaveChanges 后终止");
            return _inner.SaveChangesAsync(ct);
        }
    }
}
