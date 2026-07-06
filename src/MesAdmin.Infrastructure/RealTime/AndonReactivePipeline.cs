using MesAdmin.Application.Observability;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Plc;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using R3;
using ZLogger;

namespace MesAdmin.Infrastructure.RealTime;

public sealed class AndonReactivePipeline : IHostedService, IAsyncDisposable
{
    private readonly PlcDataAcquisitionPipeline _pipeline;
    private readonly IAsyncPublisher<AndonEventCreatedMessage> _publisher;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AndonReactivePipeline> _logger;
    private IDisposable? _subscription;

    private readonly Dictionary<string, DateTimeOffset> _recentAlarms = new();
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(30);
    private DateTimeOffset _lastCleanup = DateTimeOffset.UtcNow;
    private readonly object _lock = new();

    public AndonReactivePipeline(
        PlcDataAcquisitionPipeline pipeline,
        IAsyncPublisher<AndonEventCreatedMessage> publisher,
        IServiceScopeFactory scopeFactory,
        ILogger<AndonReactivePipeline> logger)
    {
        _pipeline = pipeline;
        _publisher = publisher;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscription = _pipeline.PlcStream
            .SubscribeAwait(async (snapshot, ct) =>
            {
                try
                {
                    var alarm = DetectAlarm(snapshot);
                    if (alarm is null) return;

                    var dedupKey = snapshot.EquipmentCode + ":" + alarm.Value.Type;
                    lock (_lock)
                    {
                        if (_recentAlarms.TryGetValue(dedupKey, out var lastTime)
                            && (DateTimeOffset.UtcNow - lastTime) < DebounceWindow)
                        {
                            return;
                        }
                        _recentAlarms[dedupKey] = DateTimeOffset.UtcNow;

                        // Periodic cleanup of stale entries (every 30s)
                        if ((DateTimeOffset.UtcNow - _lastCleanup) > CleanupInterval)
                        {
                            var cutoff = DateTimeOffset.UtcNow - DebounceWindow;
                            var stale = _recentAlarms.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList();
                            foreach (var k in stale) _recentAlarms.Remove(k);
                            _lastCleanup = DateTimeOffset.UtcNow;
                        }
                    }

                    using var scope = _scopeFactory.CreateScope();
                    var repo = scope.ServiceProvider.GetRequiredService<IAndonEventRepository>();
                    var andonEvent = AndonEvent.Create(
                        snapshot.EquipmentCode,
                        GetStationFromCode(snapshot.EquipmentCode),
                        alarm.Value.Type,
                        alarm.Value.Severity,
                        alarm.Value.Description,
                        snapshot.ProcessValue,
                        snapshot.ProcessTag,
                        alarm.Value.UpperLimit,
                        alarm.Value.LowerLimit,
                        null);

                    await repo.AddAsync(andonEvent, ct);
                    AutoMesMetrics.RecordAndonEventCreated(
                        andonEvent.AlarmType.ToString(),
                        andonEvent.Severity.ToString());

                    await _publisher.PublishAsync(new AndonEventCreatedMessage(
                        andonEvent.Id.ToString(),
                        andonEvent.EventNumber,
                        andonEvent.EquipmentCode,
                        andonEvent.Station,
                        andonEvent.AlarmType,
                        andonEvent.Severity,
                        andonEvent.Status,
                        andonEvent.Description,
                        andonEvent.ProcessValue,
                        andonEvent.ProcessTag,
                        andonEvent.UpperLimit,
                        andonEvent.LowerLimit,
                        andonEvent.OccurredAt), ct);

                    _logger.LogInformation("Andon alarm triggered: {EventNumber} {Description}",
                        andonEvent.EventNumber, andonEvent.Description);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Andon pipeline error");
                }
            });

        _logger.LogInformation("Andon reactive pipeline started");
        return Task.CompletedTask;
    }

    private (AndonAlarmType Type, AndonSeverity Severity, string Description, double? UpperLimit, double? LowerLimit)?
        DetectAlarm(PlcSnapshot snapshot)
    {
        if (snapshot.Status == EquipmentStatus.Alarm)
        {
            return (
                AndonAlarmType.EquipmentAlarm,
                AndonSeverity.Major,
                "Equipment alarm: " + snapshot.EquipmentCode,
                null, null);
        }

        var tag = snapshot.ProcessTag;
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var value = snapshot.ProcessValue;

        // Torque detection
        if (tag.StartsWith("Torque-"))
        {
            var torqueUpper = tag.Contains("M8") ? 47.0 : 23.0;
            var torqueLower = tag.Contains("M8") ? 43.0 : 21.0;
            var name = tag.Contains("M8") ? "M8" : "M6";

            if (value > torqueUpper)
            {
                var severity = value > torqueUpper * 1.05 ? AndonSeverity.Critical : AndonSeverity.Major;
                return (AndonAlarmType.TorqueExceeded, severity,
                    name + " torque " + value.ToString("F1") + "Nm exceeds upper limit " + torqueUpper + "Nm (station " + GetStationFromCode(snapshot.EquipmentCode) + ")",
                    torqueUpper, torqueLower);
            }
            if (value < torqueLower)
            {
                return (AndonAlarmType.TorqueExceeded, AndonSeverity.Major,
                    name + " torque " + value.ToString("F1") + "Nm below lower limit " + torqueLower + "Nm",
                    torqueUpper, torqueLower);
            }
        }

        // Leak detection
        if (tag == "HydraulicPressure")
        {
            const double leakUpper = 5.0;
            if (value > leakUpper)
            {
                var severity = value > leakUpper * 1.5 ? AndonSeverity.Critical : AndonSeverity.Major;
                return (AndonAlarmType.LeakRateHigh, severity,
                    "Leak rate " + value.ToString("F1") + " CC/hr exceeds limit " + leakUpper + " CC/hr",
                    leakUpper, null);
            }
        }

        // CAN detection
        if (tag == "CanLatency")
        {
            const double canUpper = 50.0;
            if (value > canUpper)
            {
                return (AndonAlarmType.CanCommunicationError, AndonSeverity.Major,
                    "CAN latency " + value.ToString("F1") + "ms exceeds limit " + canUpper + "ms",
                    canUpper, null);
            }
        }

        // Generic deviation
        if (value > 1000 || value < -100)
        {
            return (AndonAlarmType.ProcessDeviation, AndonSeverity.Minor,
                "Process deviation: " + tag + "=" + value.ToString("F2"),
                null, null);
        }

        return null;
    }

    private static int GetStationFromCode(string equipmentCode)
        => equipmentCode switch
        {
            "EQ-ASM-01" or "EQ-ASM-02" => 2,
            "EQ-TQ-01" or "EQ-TQ-02" => 3,
            "EQ-HYD-01" => 4,
            "EQ-FLS-01" => 5,
            "EQ-FT-01" => 6,
            "EQ-VN-01" => 7,
            _ => 0
        };

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _logger.LogInformation("Andon reactive pipeline stopped");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _subscription?.Dispose();
        return ValueTask.CompletedTask;
    }
}
