using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace MesAdmin.Application.Observability;

public static class AutoMesMetrics
{
    public const string MeterName = "AutoMES";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> SagaRunsTotal = Meter.CreateCounter<long>("automes_saga_runs_total");
    private static readonly Counter<long> SagaCompletionsTotal = Meter.CreateCounter<long>("automes_saga_completions_total");
    private static readonly Counter<long> SagaEffectFailuresTotal = Meter.CreateCounter<long>("automes_saga_effect_failures_total");

    private static readonly Counter<long> PlcSnapshotsTotal = Meter.CreateCounter<long>("automes_plc_snapshots_total");
    private static readonly Counter<long> PlcFrameParseErrorsTotal = Meter.CreateCounter<long>("automes_plc_frame_parse_errors_total");
    private static readonly Counter<long> PlcReadLoopErrorsTotal = Meter.CreateCounter<long>("automes_plc_read_loop_errors_total");

    private static readonly Counter<long> AndonEventsTotal = Meter.CreateCounter<long>("automes_andon_events_total");
    private static readonly Counter<long> AndonEscalationsTotal = Meter.CreateCounter<long>("automes_andon_escalations_total");
    private static readonly Histogram<double> AndonResponseDurationObserved = Meter.CreateHistogram<double>("automes_andon_response_duration_observed_seconds");

    private static readonly Counter<long> SignalRPushFailuresTotal = Meter.CreateCounter<long>("automes_signalr_push_failures_total");
    private static readonly UpDownCounter<long> SignalRActiveConnections = Meter.CreateUpDownCounter<long>("automes_signalr_active_connections");

    private static readonly ConcurrentDictionary<string, double> OeeValues = new(StringComparer.Ordinal);
    private static readonly ObservableGauge<double> OeeValueGauge = Meter.CreateObservableGauge(
        "automes_oee_value",
        ObserveOeeValues);

    private static readonly Counter<long> FeishuNotificationsTotal = Meter.CreateCounter<long>("automes_feishu_notifications_total");

    private static long _plcChannelBacklog;
    private static readonly ObservableGauge<long> PlcChannelBacklogGauge = Meter.CreateObservableGauge(
        "automes_plc_channel_backlog",
        () => new Measurement<long>(Interlocked.Read(ref _plcChannelBacklog)));

    private static double _andonResponseDurationSeconds;
    private static readonly ObservableGauge<double> AndonResponseDurationGauge = Meter.CreateObservableGauge(
        "automes_andon_response_duration_seconds",
        () => new Measurement<double>(Volatile.Read(ref _andonResponseDurationSeconds)));

    public static void RecordSagaStarted() => SagaRunsTotal.Add(1);

    public static void RecordSagaCompleted() => SagaCompletionsTotal.Add(1);

    public static void RecordSagaEffectFailure(string stage, string effectId)
        => SagaEffectFailuresTotal.Add(1,
            new KeyValuePair<string, object?>("stage", stage),
            new KeyValuePair<string, object?>("effect_id", effectId));

    public static void RecordPlcSnapshot(string equipmentCode)
        => PlcSnapshotsTotal.Add(1, new KeyValuePair<string, object?>("equipment_code", equipmentCode));

    public static void RecordPlcFrameParseError()
        => PlcFrameParseErrorsTotal.Add(1);

    public static void RecordPlcReadLoopError(string transport)
        => PlcReadLoopErrorsTotal.Add(1, new KeyValuePair<string, object?>("transport", transport));

    public static void SetPlcChannelBacklog(long backlog)
        => Interlocked.Exchange(ref _plcChannelBacklog, Math.Max(0, backlog));

    public static void SetOeeValue(string equipmentCode, double value)
        => OeeValues[equipmentCode] = value;

    public static void RecordAndonEventCreated(string alarmType, string severity)
        => AndonEventsTotal.Add(1,
            new KeyValuePair<string, object?>("alarm_type", alarmType),
            new KeyValuePair<string, object?>("severity", severity));

    public static void RecordAndonEscalation(int level)
        => AndonEscalationsTotal.Add(1, new KeyValuePair<string, object?>("level", level));

    public static void SetAndonResponseDurationSeconds(double seconds)
        => Volatile.Write(ref _andonResponseDurationSeconds, Math.Max(0, seconds));

    public static void RecordAndonResponseObserved(double seconds, string station)
        => AndonResponseDurationObserved.Record(seconds, new KeyValuePair<string, object?>("station", station));

    public static void RecordFeishuNotificationSent(bool success)
        => FeishuNotificationsTotal.Add(1, new KeyValuePair<string, object?>("success", success));

    public static void RecordSignalRPushFailure(string hub, string messageType)
        => SignalRPushFailuresTotal.Add(1,
            new KeyValuePair<string, object?>("hub", hub),
            new KeyValuePair<string, object?>("message_type", messageType));

    public static void RecordSignalRConnected(string hub)
        => SignalRActiveConnections.Add(1, new KeyValuePair<string, object?>("hub", hub));

    public static void RecordSignalRDisconnected(string hub)
        => SignalRActiveConnections.Add(-1, new KeyValuePair<string, object?>("hub", hub));

    private static IEnumerable<Measurement<double>> ObserveOeeValues()
    {
        if (OeeValues.IsEmpty)
            return [];

        return OeeValues
            .Select(kv => new Measurement<double>(
                kv.Value,
                new KeyValuePair<string, object?>("equipment_code", kv.Key)))
            .ToArray();
    }
}
