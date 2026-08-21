namespace MesAdmin.Web.Services;

/// <summary>Web 端 API 响应 DTO（镜像 API 端 DTO，不依赖 API 项目）</summary>

public record OrderSummary(
    string Id, string OrderNumber, string ProductCode, string Status,
    short Priority, string RoutingId, string BomVersion,
    int PlannedQuantity, int QualifiedQuantity, int DefectiveQuantity,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

public record OrderDetail(
    OrderSummary Order, bool CanRelease, bool CanStart, bool CanComplete, bool CanClose, bool CanCancel = false);

public record OperationDto(
    string Id, string OrderId, int Sequence, int Station,
    string OperationCode, string OperationName, string Status,
    string? OperatorId, string? EquipmentId,
    DateTimeOffset? StartAt, DateTimeOffset? EndAt, string? FailureReason);

public record CreateOrderBody(string ProductCode, string BomVersion, string RoutingId, int PlannedQuantity, short Priority);
public record ChangeStatusBody(string Status);
public record CompleteOrderBody(int QualifiedQuantity, int DefectiveQuantity);
public record CancelOrderBody(string Reason);
public record ReportOperationBody(string OperatorId, string EquipmentId);

public record TraceabilityLinkDto(
    string Id,
    int Level,
    string LevelName,
    string VinOrSerial,
    string OrderId,
    string ComponentBatch,
    string MaterialBatch,
    string PreviousHash,
    string Hash,
    DateTimeOffset CreatedAt,
    bool HashVerified);

/// <summary>T1.4 齐套检查响应 DTO。</summary>
public record KitCheckResponse(
    bool IsPassed,
    List<KitCheckItemResponse> Items,
    List<string> JitPullSignalIds);

/// <summary>T1.4 齐套检查单项结果 DTO。</summary>
public record KitCheckItemResponse(
    string MaterialCode,
    string MaterialName,
    double RequiredQuantity,
    double AvailableQuantity,
    double ShortageQuantity,
    string Unit,
    bool IsCritical);

// ═══════════════════════════════════════════
// T1.12-T1.18 物料相关 DTO
// ═══════════════════════════════════════════

/// <summary>物料批次响应 DTO（镜像 API Materials.Contracts）</summary>
public record MaterialBatchDto(
    string Id,
    string MaterialCode,
    string MaterialName,
    string BatchNumber,
    string SupplierCode,
    string SupplierName,
    double ReceivedQuantity,
    double RemainingQuantity,
    string Unit,
    bool IsCritical,
    string Status,
    DateTimeOffset? ProductionDate,
    DateTimeOffset ReceivedAt);

/// <summary>物料投料绑定响应 DTO</summary>
public record MaterialBindingDto(
    string Id,
    string OrderId,
    string MaterialBatchId,
    string MaterialCode,
    string BatchNumber,
    string ProductSerial,
    double Quantity,
    bool PokaYokePassed,
    string OperatorId,
    DateTimeOffset BoundAt);

/// <summary>来料扫码入库请求体</summary>
public record ReceiveMaterialBody(
    string Barcode,
    string SupplierCode,
    string SupplierName,
    string MaterialName,
    bool IsCritical);

/// <summary>投料绑定请求体</summary>
public record BindMaterialBody(
    string OrderId,
    string MaterialBatchId,
    string ProductSerial,
    double Quantity,
    string OperatorId);

// ═══════════════════════════════════════════
// T1.8 完工确认相关 DTO
// ═══════════════════════════════════════════

/// <summary>成品入库单响应 DTO（镜像 GoodsReceipt 领域模型）</summary>
public record GoodsReceiptDto(
    string Id,
    string OrderId,
    string OrderNumber,
    string ProductCode,
    int ReceivedQuantity,
    string ReviewerId,
    string TraceabilityLabelCode,
    bool SapSynced,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? SapSyncedAt);

/// <summary>完工确认请求体（含质量审核人工号）</summary>
public record CompleteOrderWithReviewerBody(
    int QualifiedQuantity,
    int DefectiveQuantity,
    string ReviewerId);

/// <summary>工单状态枚举字符串（与 API 端 OrderStatus.ToString() 一致）</summary>
public static class OrderStatusNames
{
    public const string Created = "Created";
    public const string Released = "Released";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Closed = "Closed";
    public const string Cancelled = "Cancelled";
}

// ═══════════════════════════════════════════
// T2.1-T2.10 SPC 质量管理相关 DTO
// ═══════════════════════════════════════════

/// <summary>SPC 样本响应 DTO</summary>
public record SpcSampleDto(
    string Id,
    string CharacteristicCode,
    string? OrderId,
    string? EquipmentCode,
    int SubgroupIndex,
    int SubgroupSize,
    List<double> Values,
    double Mean,
    double Range,
    double StdDev,
    string Source,
    DateTimeOffset CollectedAt);

/// <summary>SPC 控制图数据 DTO（供 ECharts 渲染）</summary>
public record SpcChartDataDto(
    string CharacteristicCode,
    string CharacteristicName,
    double CenterLine,
    double UpperControlLimit,
    double LowerControlLimit,
    double UpperRangeLimit,
    double CenterRange,
    double? Cpk,
    string Unit,
    List<SpcChartSampleDto> Samples);

/// <summary>控制图子组数据点 DTO</summary>
public record SpcChartSampleDto(
    int SubgroupIndex,
    double Mean,
    double Range);

/// <summary>质量检验记录响应 DTO（镜像 API QualityRecordResponse）</summary>
public record QualityRecordDto(
    string Id,
    string Stage,
    string? OrderId,
    string? OrderNumber,
    string ProductCode,
    string ProductName,
    string? BatchNumber,
    string? SupplierCode,
    string? SupplierName,
    string InspectionPlanName,
    string? AqlScheme,
    int SampleSize,
    int AcceptNumber,
    int RejectNumber,
    string InspectorId,
    string Verdict,
    List<MeasuredCharDto> Characteristics,
    int DefectCount,
    string? Remarks,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

/// <summary>检验特性实测值 DTO</summary>
public record MeasuredCharDto(
    string CharacteristicCode,
    string CharacteristicName,
    double StandardValue,
    double? UpperSpecLimit,
    double? LowerSpecLimit,
    string Unit,
    double? ActualValue,
    bool IsFailed);

/// <summary>NCR 响应 DTO</summary>
public record NcrDto(
    string Id,
    string NcrNumber,
    string? OrderId,
    string? OrderNumber,
    string ProductCode,
    string ProductName,
    string? BatchNumber,
    string DiscoveredAt,
    string Description,
    int DefectQuantity,
    string Severity,
    string Status,
    string Disposition,
    string DiscoveredBy,
    string? ReviewerId,
    string? ReviewComments,
    string? CloseRemarks,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt);

/// <summary>8D 报告响应 DTO</summary>
public record EightDDto(
    string Id,
    string ReportNumber,
    string? NcrNumber,
    string Title,
    string ProductCode,
    string ProductName,
    string Status,
    string? TeamLeader,
    string? TeamMembers,
    string? ProblemDescription,
    string? ContainmentAction,
    string? RootCause,
    string? CorrectiveAction,
    string? VerificationResult,
    string? PreventiveAction,
    string? Summary,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt);

// ═══════════════════════════════════════════
// T2.20-T2.23 Andon 报警相关 DTO
// ═══════════════════════════════════════════

/// <summary>Andon 报警事件 DTO</summary>
public record AndonEventDto(
    string Id,
    string EventNumber,
    string EquipmentCode,
    int Station,
    string AlarmType,
    string Severity,
    string Status,
    int EscalationLevel,
    string Description,
    double ProcessValue,
    string? ProcessTag,
    double? UpperLimit,
    double? LowerLimit,
    string? OrderId,
    string? NonConformanceReportId,
    string? AcknowledgedBy,
    DateTimeOffset? AcknowledgedAt,
    string? ResolvedBy,
    string? Resolution,
    DateTimeOffset? ResolvedAt,
    string? CloseRemarks,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? EscalatedAt,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt);

/// <summary>Andon 统计 DTO</summary>
public record AndonStatsDto(
    int ActiveCount,
    int EscalatedL2Count,
    int EscalatedL3Count,
    int TodayCount);

/// <summary>SPC 判异告警 DTO</summary>
public record SpcAlertDto(
    string Id,
    string CharacteristicCode,
    int RuleType,
    string AlertLevel,
    string Description,
    bool IsAcknowledged,
    string? AcknowledgedBy,
    DateTimeOffset CreatedAt);

// ═══════════════════════════════════════════
// T2.18 备件管理相关 DTO
// ═══════════════════════════════════════════

/// <summary>备件响应 DTO</summary>
public record SparePartDto(
    string Id,
    string MaterialCode,
    string MaterialName,
    string Specification,
    string Unit,
    double CurrentQuantity,
    double SafetyStock,
    double MinimumStock,
    string? EquipmentCode,
    string? Remarks,
    string StockLevel,
    bool NeedsPurchaseRequest,
    double SuggestedPurchaseQuantity,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>备件使用记录 DTO</summary>
public record SparePartUsageDto(
    string Id,
    string SparePartId,
    string MaintenanceWorkOrderId,
    double Quantity,
    double? UnitPrice,
    string? Remarks,
    DateTimeOffset CreatedAt);

/// <summary>采购申请响应 DTO</summary>
public record PurchaseRequestDto(
    string Id,
    string RequestNumber,
    string SparePartId,
    double Quantity,
    string Reason,
    string Status,
    string RequestedBy,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>库存检查响应 DTO</summary>
public record StockCheckDto(
    string SparePartId,
    string MaterialCode,
    string MaterialName,
    double CurrentQuantity,
    double SafetyStock,
    double MinimumStock,
    string StockLevel,
    double SuggestedPurchaseQuantity,
    PurchaseRequestDto? ExistingPurchaseRequest);

/// <summary>创建备件请求体</summary>
public record CreateSparePartBody(
    string MaterialCode,
    string MaterialName,
    string Specification,
    string Unit,
    double SafetyStock,
    double MinimumStock,
    string? EquipmentCode,
    string? Remarks);

/// <summary>更新库存请求体</summary>
public record UpdateStockBody(double NewQuantity);

/// <summary>补货请求体</summary>
public record RestockBody(double Quantity);

/// <summary>消耗备件请求体</summary>
public record ConsumeSparePartBody(string SparePartId, double Quantity, double? UnitPrice, string? Remarks);

/// <summary>创建采购申请请求体</summary>
public record CreatePurchaseRequestBody(string SparePartId, double? Quantity, string Reason);

/// <summary>审批采购申请请求体</summary>
public record ApprovePurchaseRequestBody(string ApprovedBy);

/// <summary>库存检查请求体</summary>
public record CheckStockBody(string SparePartId);

// ═══════════════════════════════════════════
// T2.17 预防性维护相关 DTO
// ═══════════════════════════════════════════

/// <summary>维护计划响应 DTO（镜像 MaintenancePlanResponse）</summary>
public record MaintenancePlanDto(
    string Id,
    string EquipmentCode,
    string EquipmentName,
    string MaintenanceType,
    double ThresholdValue,
    string TaskDescription,
    string WorkContent,
    bool IsActive,
    DateTimeOffset? LastTriggeredAt,
    long? LastTriggeredCycleCount,
    DateTimeOffset CreatedAt);

/// <summary>维护工单响应 DTO（镜像 MaintenanceOrderResponse）</summary>
public record MaintenanceOrderDto(
    string Id,
    string OrderNumber,
    string MaintenancePlanId,
    string EquipmentCode,
    string EquipmentName,
    string MaintenanceType,
    string TriggerType,
    double TriggerValue,
    string Title,
    string Description,
    string Status,
    string? AssignedTo,
    string? CompletedBy,
    string? CompletionRemarks,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt);

/// <summary>创建维护计划请求体</summary>
public record CreateMaintenancePlanBody(
    string EquipmentCode,
    string EquipmentName,
    string MaintenanceType,
    double ThresholdValue,
    string TaskDescription,
    string WorkContent);

/// <summary>开始维护工单请求体</summary>
public record StartMaintenanceOrderBody(string AssignedTo);

/// <summary>完成维护工单请求体</summary>
public record CompleteMaintenanceOrderBody(string CompletedBy, string Remarks);

/// <summary>取消维护工单请求体</summary>
public record CancelMaintenanceOrderBody(string Reason);

// ═══════════════════════════════════════════
// T1.5 首件检验相关 DTO
// ═══════════════════════════════════════════

/// <summary>首件检验响应 DTO（镜像 InspectionResponse）</summary>
public record InspectionDto(
    string Id,
    string OrderId,
    string OrderNumber,
    string ProductCode,
    string InspectionType,
    string Status,
    string OperatorId,
    string? InspectorId,
    List<InspectionItemDto> Items,
    string? Conclusion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

/// <summary>首件检验检验项 DTO</summary>
public record InspectionItemDto(
    string CharacteristicCode,
    string CharacteristicName,
    double StandardValue,
    double? UpperLimit,
    double? LowerLimit,
    string Unit,
    double? ActualValue,
    bool IsPass);

/// <summary>创建首件检验请求体</summary>
public record CreateInspectionBody(string InspectionType, string OperatorId);

// ═══════════════════════════════════════════
// T4.1 报表模板引擎 DTO
// ═══════════════════════════════════════════

/// <summary>报表模板列表项 DTO（镜像 TemplateListItem）</summary>
public record ReportTemplateItemDto(
    string Id,
    string Name,
    string Description,
    string Type,
    int SectionCount,
    bool SupportsEmail,
    bool SupportsSchedule);

// ═══════════════════════════════════════════
// T4.4-T4.5 SAP / 离线同步监控 DTO
// ═══════════════════════════════════════════

/// <summary>离线同步统计（镜像 OfflineSyncStats）</summary>
public record SyncStatsDto(
    int PendingCount,
    int SyncedCount,
    int ConflictCount,
    int FailedCount,
    int TotalCount,
    int TerminalCount);

/// <summary>同步状态响应（镜像 SyncStatusResponse）</summary>
public record SyncStatusDto(
    SyncStatsDto Stats,
    long ChannelBacklog,
    long ChannelProcessed,
    long ChannelConflicts);

/// <summary>待同步/冲突记录项（镜像 SyncConflictItem）</summary>
public record SyncPendingItemDto(
    string Id,
    string TerminalId,
    string OperationType,
    string EntityType,
    string? EntityId,
    string Payload,
    string Status,
    string? ErrorMessage,
    DateTimeOffset CreatedAt);

/// <summary>SAP 同步状态（镜像 SapSyncStatusResponse）</summary>
public record SapSyncStatusDto(
    int PendingRejectionCount,
    int PendingInventorySyncCount,
    int PendingOrderSyncCount,
    int TotalPending);

/// <summary>SAP 待同步明细项</summary>
public record SapPendingItemDto(
    string Id,
    string Type,
    string TypeLabel,
    string? Reference,
    string Detail,
    string? Reason,
    string? Error,
    DateTimeOffset CreatedAt);

/// <summary>SAP 拒单回写结果（镜像 WritebackResultResponse）</summary>
public record SapWritebackResultDto(
    bool Success,
    string Message,
    string? DocumentNumber);

/// <summary>记录检验项实测值请求体</summary>
public record RecordInspectionValueBody(double ActualValue);

/// <summary>完成首件检验请求体</summary>
public record CompleteInspectionBody(string InspectorId);

// ═══════════════════════════════════════════
// T3.1-T3.5 M07 工艺管理 DTO
// ═══════════════════════════════════════════

/// <summary>工艺路线响应 DTO</summary>
public record RoutingResponseDto(
    string Id,
    string ProductCode,
    string Name,
    string Version,
    string? EcoNumber,
    string EcoStatus,
    int OperationCount,
    bool IsActive,
    string? ChangeDescription,
    string CreatedBy,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? EffectiveDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    List<RoutingOperationDto> Operations);

/// <summary>工艺路线工序 DTO</summary>
public record RoutingOperationDto(
    int Sequence,
    int Station,
    string OperationCode,
    string OperationName,
    double? StandardTimeSeconds,
    string? FixtureCode,
    string? FixtureName,
    List<ParameterTemplateDto> ParameterTemplates);

/// <summary>参数模板 DTO</summary>
public record ParameterTemplateDto(
    string ParameterCode,
    string ParameterName,
    double StandardValue,
    double? UpperSpecLimit,
    double? LowerSpecLimit,
    string Unit,
    bool EnableSpc,
    int SpcSubgroupSize);

/// <summary>创建工艺路线请求体</summary>
public record CreateRoutingBody(
    string ProductCode,
    string Name,
    string Version,
    string CreatedBy,
    string? EcoNumber,
    string? ChangeDescription,
    List<CreateRoutingOperationBody> Operations);

/// <summary>创建工序请求体</summary>
public record CreateRoutingOperationBody(
    int Sequence,
    int Station,
    string OperationCode,
    string OperationName,
    double? StandardTimeSeconds,
    string? FixtureCode,
    string? FixtureName,
    List<CreateParameterTemplateBody> ParameterTemplates);

/// <summary>创建参数模板请求体</summary>
public record CreateParameterTemplateBody(
    string ParameterCode,
    string ParameterName,
    double StandardValue,
    double? UpperSpecLimit,
    double? LowerSpecLimit,
    string Unit,
    bool EnableSpc,
    int SpcSubgroupSize);

/// <summary>审批工艺路线请求体</summary>
public record ApproveRoutingBody(string ApprovedBy);

// ═══════════════════════════════════════════
// M08 SQE 供应商质量相关 DTO (T3.6-T3.8)
// ═══════════════════════════════════════════

/// <summary>供应商响应 DTO</summary>
public record SupplierDto(
    string Id,
    string SupplierCode,
    string SupplierName,
    string? ShortName,
    string? ContactPerson,
    string? ContactPhone,
    string? ContactEmail,
    string MaterialCategory,
    string MaterialCodes,
    string Tier,
    bool IsCritical,
    double LatestScore,
    DateTimeOffset? LatestScoreAt,
    string? IsoCertification,
    DateTimeOffset? IsoExpiryDate,
    bool IsActive,
    string? Remarks,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>供应商评分卡响应 DTO</summary>
public record SupplierScoreCardDto(
    string Id,
    string SupplierId,
    string SupplierCode,
    string Period,
    double IncomingQualityScore,
    string IncomingQualityData,
    double OnTimeDeliveryScore,
    string OnTimeDeliveryData,
    double EightDResponseScore,
    string EightDResponseData,
    double PpapPassRateScore,
    string PpapPassRateData,
    double PriceCompetitivenessScore,
    string PriceCompetitivenessData,
    double WeightedTotal,
    string EvaluatedBy,
    string? Remarks,
    DateTimeOffset CreatedAt);

/// <summary>PPAP 文档响应 DTO</summary>
public record PpapDocumentDto(
    string Id,
    string SupplierId,
    string SupplierCode,
    string MaterialCode,
    string MaterialName,
    int PpapLevel,
    string Status,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? ExpiryDate,
    string Version,
    string? ApprovedBy,
    string? RejectionReason,
    string? Remarks,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>关键供应商管控设置响应 DTO</summary>
public record CriticalSupplierSettingDto(
    string Id,
    string MaterialCode,
    string MaterialName,
    int ControlLevel,
    bool RequiresFullInspection,
    bool RequiresOnSiteAudit,
    int AuditIntervalMonths,
    bool RequiresSpcDataSubmission,
    bool RequiresComplianceReport,
    bool IsActive,
    string? Remarks,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>创建供应商请求体</summary>
public record CreateSupplierBody(
    string SupplierCode,
    string SupplierName,
    string MaterialCategory,
    string MaterialCodes,
    bool IsCritical,
    string? ShortName,
    string? ContactPerson,
    string? ContactPhone,
    string? ContactEmail,
    string? IsoCertification,
    string? Remarks);

/// <summary>更新供应商请求体</summary>
public record UpdateSupplierBody(
    string? ShortName,
    string? ContactPerson,
    string? ContactPhone,
    string? ContactEmail,
    string? MaterialCategory,
    string? MaterialCodes,
    bool? IsCritical,
    string? IsoCertification,
    DateTimeOffset? IsoExpiryDate,
    string? Remarks);

/// <summary>创建评分卡请求体</summary>
public record CreateScoreCardBody(
    string Period,
    double IncomingQualityScore,
    string IncomingQualityData,
    double OnTimeDeliveryScore,
    string OnTimeDeliveryData,
    double EightDResponseScore,
    string EightDResponseData,
    double PpapPassRateScore,
    string PpapPassRateData,
    double PriceCompetitivenessScore,
    string PriceCompetitivenessData,
    string EvaluatedBy,
    string? Remarks);

/// <summary>创建 PPAP 文档请求体</summary>
public record CreatePpapBody(
    string MaterialCode,
    string MaterialName,
    int PpapLevel,
    string CreatedBy,
    DateTimeOffset? ExpiryDate,
    string? Remarks);

/// <summary>批准 PPAP 请求体</summary>
public record ApprovePpapBody(string ApprovedBy);

/// <summary>拒绝 PPAP 请求体</summary>
public record RejectPpapBody(string Reason);

/// <summary>创建关键管控设置请求体</summary>
public record CreateCriticalSettingBody(
    string MaterialCode,
    string MaterialName,
    int ControlLevel,
    bool RequiresFullInspection,
    bool RequiresOnSiteAudit,
    int AuditIntervalMonths,
    bool RequiresSpcDataSubmission,
    bool RequiresComplianceReport,
    string? Remarks);

/// <summary>更新供应商等级请求体</summary>
public record UpdateTierBody(string Tier);

// ═══════════════════════════════════════════
// M09 排程管理相关 DTO (T3.10-T3.13)
// ═══════════════════════════════════════════

/// <summary>排程响应 DTO</summary>
public record ScheduleDto(
    string Id,
    string OrderId,
    string OrderNumber,
    string ProductCode,
    int PlannedQuantity,
    string EquipmentCode,
    int Station,
    string Shift,
    string ScheduleDate,
    DateTimeOffset PlannedStartAt,
    DateTimeOffset PlannedEndAt,
    double StandardMinutes,
    double ChangeoverMinutes,
    string Status,
    string RushType,
    string? RushReason,
    short Priority,
    string? Remarks,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>甘特图任务 DTO</summary>
public record GanttTaskDto(
    string Id,
    string Name,
    string StartDate,
    string EndDate,
    double Duration,
    string DurationUnit,
    int PercentDone,
    string EquipmentCode,
    string AssignedTo,
    string Cls,
    string Status,
    short Priority);

/// <summary>甘特图依赖 DTO</summary>
public record GanttDependencyDto(string Id, string From, string To, string Type);

/// <summary>甘特图资源 DTO</summary>
public record GanttResourceDto(string Id, string Name, int Station);

/// <summary>甘特图数据响应 DTO</summary>
public record GanttDataDto(
    List<GanttTaskDto> Tasks,
    List<GanttDependencyDto> Dependencies,
    List<GanttResourceDto> Resources);

/// <summary>产能利用率 DTO</summary>
public record CapacityUtilizationDto(
    string EquipmentCode,
    string EquipmentName,
    double UtilizationPercent,
    int ScheduledCount);

/// <summary>排程冲突 DTO</summary>
public record ScheduleConflictDto(
    string Description,
    string Severity,
    string EquipmentCode,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? ConflictingScheduleId);

/// <summary>紧急插单结果 DTO</summary>
public record RushOrderResultDto(
    ScheduleDto Schedule,
    List<ScheduleConflictDto> Conflicts);

/// <summary>插单请求体</summary>
public record InsertRushOrderBody(
    string OrderId,
    string EquipmentCode,
    string RushType,
    double StandardMinutes,
    double ChangeoverMinutes,
    string? RushReason);

/// <summary>重新排程请求体</summary>
public record RescheduleBody(
    DateTimeOffset NewStartAt,
    string? NewEquipmentCode,
    double? NewChangeoverMinutes);

/// <summary>产能日历响应 DTO（镜像 CapacityCalendarResponse）</summary>
public record CapacityCalendarDto(
    string Id,
    string EquipmentCode,
    string EquipmentName,
    int Station,
    double StandardChangeoverMinutes,
    double CrossProductChangeoverMinutes,
    bool IsActive,
    DateTimeOffset CreatedAt);

/// <summary>创建产能日历请求体</summary>
public record CreateCalendarBody(
    string EquipmentCode,
    string EquipmentName,
    int Station,
    double StandardChangeoverMinutes,
    double CrossProductChangeoverMinutes);

// ═══════════════════════════════════════════
// 液压测试台相关 DTO（镜像 HydraulicTestResponse）
// ═══════════════════════════════════════════

/// <summary>液压测试结果响应 DTO</summary>
public record HydraulicTestDto(
    string Id,
    string EquipmentCode,
    string Status,
    int CycleNumber,
    double? PressureBuildTimeMs,
    bool? PressureBuildPass,
    double? HoldPressureBar,
    bool? HoldPressurePass,
    double? PressureReleaseTimeMs,
    bool? PressureReleasePass,
    double? LeakRateCcHr,
    bool? LeakRatePass,
    int SolenoidTestCount,
    int SolenoidPassCount,
    bool OverallPass,
    string? FailureReason,
    bool EquipmentLocked,
    string? UnlockedBy,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

/// <summary>液压设备解锁请求体</summary>
public record UnlockHydraulicBody(string UnlockedBy);

// ═══════════════════════════════════════════
// T2.2 IQC / T2.4 IPQC 检验执行 DTO
// ═══════════════════════════════════════════

/// <summary>创建 IQC 来料检验请求体</summary>
public record CreateIqcBody(
    string InspectionPlanId,
    string InspectionPlanName,
    string MaterialCode,
    string MaterialName,
    string BatchNumber,
    string SupplierCode,
    string SupplierName,
    string InspectorId,
    int SampleSize,
    int AcceptNumber,
    int RejectNumber,
    string? AqlScheme);

/// <summary>记录 IQC 实测值请求体</summary>
public record RecordIqcMeasurementBody(string CharacteristicCode, double ActualValue);

/// <summary>创建 IPQC 过程巡检请求体</summary>
public record CreateIpqcBody(
    string OrderId,
    string OrderNumber,
    string ProductCode,
    string ProductName,
    string InspectionPlanId,
    string InspectionPlanName,
    string InspectorId,
    int AcceptNumber,
    int RejectNumber,
    List<MeasuredCharacteristicBody> Characteristics);

/// <summary>IPQC 检验特性请求体</summary>
public record MeasuredCharacteristicBody(
    string CharacteristicCode,
    string CharacteristicName,
    double StandardValue,
    double? UpperSpecLimit,
    double? LowerSpecLimit,
    string Unit);

// ═══════════════════════════════════════════
// 8D 编辑 / SPC 告警确认 DTO
// ═══════════════════════════════════════════

/// <summary>更新 8D 报告请求体（D1-D7）</summary>
public record UpdateEightDBody(
    string? TeamLeader,
    string? TeamMembers,
    string? ProblemDescription,
    string? ContainmentAction,
    string? RootCauseAnalysis,
    string? RootCause,
    string? CorrectiveAction,
    string? CorrectiveActionOwner,
    DateTimeOffset? CorrectiveActionDueDate,
    string? VerificationMethod,
    string? VerificationResult,
    string? PreventiveAction,
    int CompletedStep);

/// <summary>确认 SPC 告警请求体</summary>
public record AcknowledgeSpcAlertBody(string AcknowledgedBy, string? ActionTaken);

// ═══════════════════════════════════════════
// 检验计划管理 DTO（镜像 InspectionPlanResponse）
// ═══════════════════════════════════════════

/// <summary>检验计划响应 DTO</summary>
public record InspectionPlanDto(
    string Id,
    string PlanName,
    string Version,
    string? ProductCode,
    string Stage,
    string SamplingFrequency,
    int SampleSize,
    bool EnableSpcChart,
    int SpcSubgroupSize,
    bool IsEnabled,
    List<PlanCharacteristicDto> Characteristics,
    DateTimeOffset EffectiveDate,
    DateTimeOffset? ExpirationDate);

/// <summary>计划检验特性 DTO</summary>
public record PlanCharacteristicDto(
    string CharacteristicCode,
    string CharacteristicName,
    string Type,
    double StandardValue,
    double? UpperSpecLimit,
    double? LowerSpecLimit,
    string Unit,
    bool IsCritical,
    bool EnableSpc,
    double? UpperControlLimit,
    double? LowerControlLimit,
    double? CenterLine,
    double? UpperRangeLimit,
    double? CenterRange);

/// <summary>创建检验计划请求体</summary>
public record CreateInspectionPlanBody(
    string PlanName,
    string Version,
    string Stage,
    string? ProductCode,
    int? Station,
    string SamplingFrequency,
    int SampleSize,
    double? AqlValue,
    string? InspectionLevel,
    int AcceptNumber,
    int RejectNumber,
    bool EnableSpcChart,
    int SpcSubgroupSize,
    DateTimeOffset EffectiveDate,
    DateTimeOffset? ExpirationDate,
    List<CreatePlanCharacteristicBody> Characteristics);

/// <summary>创建计划特性请求体</summary>
public record CreatePlanCharacteristicBody(
    string CharacteristicCode,
    string CharacteristicName,
    string Type,
    double StandardValue,
    double? UpperSpecLimit,
    double? LowerSpecLimit,
    string Unit,
    bool IsCritical,
    bool EnableSpc);

// ═══════════════════════════════════════════
// 系统管理：用户 + 审计日志
// ═══════════════════════════════════════════

/// <summary>用户账号 DTO</summary>
public record UserDto(
    string Id,
    string Username,
    string DisplayName,
    string[] Roles,
    bool IsActive,
    DateTimeOffset? LastLoginAt,
    bool IsLockedOut,
    DateTimeOffset CreatedAt);

/// <summary>审计日志条目</summary>
public record AuditLogItemDto(
    string Id,
    DateTimeOffset Timestamp,
    string Username,
    string Action,
    string Module,
    string Summary,
    int StatusCode,
    string RemoteIp);

/// <summary>审计日志分页结果</summary>
public record AuditLogPageDto(
    int Total,
    int PageIndex,
    int PageSize,
    List<AuditLogItemDto> Items);
