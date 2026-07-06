using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// 8D 报告状态。
/// </summary>
public enum EightDStatus
{
    Open = 0,
    InProgress = 1,
    Verified = 2,
    Closed = 3,
}

/// <summary>
/// 8D 问题解决报告（T2.8）。
/// 覆盖 D1-D8 完整流程：成立团队 → 问题描述 → 围堵 → 根因分析 → 纠正措施 → 实施验证 → 预防措施 → 总结表彰。
/// 关联 NCR（不合格品报告）和 CAPA（纠正与预防措施）。
/// </summary>
[MemoryPackable]
public partial class EightDReport
{
    public Ulid Id { get; set; }

    /// <summary>8D 编号（如 8D-20260705-001）</summary>
    public string ReportNumber { get; set; } = string.Empty;

    /// <summary>关联 NCR Id</summary>
    public Ulid? NonConformanceReportId { get; set; }

    /// <summary>关联 NCR 编号</summary>
    public string? NcrNumber { get; set; }

    /// <summary>问题标题</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>产品/物料编码</summary>
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>产品/物料名称</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>当前状态</summary>
    public EightDStatus Status { get; set; } = EightDStatus.Open;

    // ── D1: 成立团队 ──
    /// <summary>团队负责人</summary>
    public string? TeamLeader { get; set; }
    /// <summary>团队成员（逗号分隔的员工号）</summary>
    public string? TeamMembers { get; set; }

    // ── D2: 问题描述 ──
    /// <summary>问题描述（5W2H）</summary>
    public string? ProblemDescription { get; set; }

    // ── D3: 围堵措施 ──
    /// <summary>围堵措施描述</summary>
    public string? ContainmentAction { get; set; }
    /// <summary>围堵措施有效日期</summary>
    public DateTimeOffset? ContainmentDate { get; set; }

    // ── D4: 根因分析 ──
    /// <summary>根因分析（鱼骨图/5Why）</summary>
    public string? RootCauseAnalysis { get; set; }
    /// <summary>根本原因</summary>
    public string? RootCause { get; set; }

    // ── D5: 纠正措施 ──
    /// <summary>永久纠正措施</summary>
    public string? CorrectiveAction { get; set; }
    /// <summary>责任人</summary>
    public string? CorrectiveActionOwner { get; set; }
    /// <summary>计划完成日期</summary>
    public DateTimeOffset? CorrectiveActionDueDate { get; set; }

    // ── D6: 实施验证 ──
    /// <summary>验证方法</summary>
    public string? VerificationMethod { get; set; }
    /// <summary>验证结果</summary>
    public string? VerificationResult { get; set; }
    /// <summary>验证日期</summary>
    public DateTimeOffset? VerificationDate { get; set; }

    // ── D7: 预防措施 ──
    /// <summary>预防措施（FMEA 更新/控制计划更新）</summary>
    public string? PreventiveAction { get; set; }

    // ── D8: 总结表彰 ──
    /// <summary>总结</summary>
    public string? Summary { get; set; }

    /// <summary>关闭日期</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>更新时间</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public static EightDReport Create(string title, string productCode, string productName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(productCode);

        return new EightDReport
        {
            Id = Ulid.NewUlid(),
            ReportNumber = GenerateReportNumber(),
            Title = title.Trim(),
            ProductCode = productCode.Trim(),
            ProductName = productName?.Trim() ?? string.Empty,
            Status = EightDStatus.Open,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>D1: 成立团队</summary>
    public void SetTeam(string leader, string members)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leader);
        TeamLeader = leader.Trim();
        TeamMembers = members?.Trim();
        ProgressToInProgress();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>D2: 问题描述</summary>
    public void DescribeProblem(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ProblemDescription = description.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>D3: 围堵措施</summary>
    public void SetContainment(string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ContainmentAction = action.Trim();
        ContainmentDate = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>D4: 根因分析</summary>
    public void SetRootCause(string analysis, string rootCause)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analysis);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootCause);
        RootCauseAnalysis = analysis.Trim();
        RootCause = rootCause.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>D5: 纠正措施</summary>
    public void SetCorrectiveAction(string action, string owner, DateTimeOffset dueDate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        CorrectiveAction = action.Trim();
        CorrectiveActionOwner = owner.Trim();
        CorrectiveActionDueDate = dueDate;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>D6: 验证</summary>
    public void Verify(string method, string result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(result);
        VerificationMethod = method.Trim();
        VerificationResult = result.Trim();
        VerificationDate = DateTimeOffset.UtcNow;
        Status = EightDStatus.Verified;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>D7: 预防措施</summary>
    public void SetPreventiveAction(string action)
    {
        PreventiveAction = action?.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>D8: 关闭</summary>
    public void Close(string summary)
    {
        Summary = summary?.Trim();
        Status = EightDStatus.Closed;
        ClosedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void ProgressToInProgress()
    {
        if (Status == EightDStatus.Open)
            Status = EightDStatus.InProgress;
    }

    private static int _sequence;
    private static string GenerateReportNumber()
    {
        var seq = Interlocked.Increment(ref _sequence);
        return $"8D-{DateTimeOffset.UtcNow:yyyyMMdd}-{seq:D4}";
    }
}
