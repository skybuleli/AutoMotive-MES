using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.RealTime;
using MesAdmin.Infrastructure.Reports;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

namespace MesAdmin.Application.Tests;

/// <summary>
/// OeeReportStore 测试。
/// 验证并发订阅更新、快照读取与平均值聚合。
/// </summary>
public class OeeReportStoreTests
{
    private readonly OeeReportStore _store;
    private readonly IAsyncPublisher<PlcDataChanged> _publisher;

    public OeeReportStoreTests()
    {
        var services = new ServiceCollection();
        services.AddMessagePipe();
        var provider = services.BuildServiceProvider();
        _publisher = provider.GetRequiredService<IAsyncPublisher<PlcDataChanged>>();
        var subscriber = provider.GetRequiredService<IAsyncSubscriber<PlcDataChanged>>();
        _store = new OeeReportStore(subscriber);
    }

    [Fact]
    public void GetSnapshot_BeforeAnyUpdate_ShouldReturnZeroOee()
    {
        var snapshot = _store.GetSnapshot("EQ-TQ-01");

        Assert.NotNull(snapshot);
        Assert.Equal("EQ-TQ-01", snapshot.EquipmentCode);
        Assert.Equal(0, snapshot.Oee);
        Assert.Equal(0, snapshot.TotalUpdates);
    }

    [Fact]
    public async Task GetSnapshot_AfterPublish_ShouldReturnLatestOee()
    {
        var oee = OeeRecord.Compute("EQ-TQ-01", DateTimeOffset.UtcNow, 0.9, 0.95, 0.98);
        await _publisher.PublishAsync(new PlcDataChanged(oee));

        var snapshot = _store.GetSnapshot("EQ-TQ-01");

        Assert.NotNull(snapshot);
        Assert.Equal(0.9, snapshot.Availability);
        Assert.Equal(0.95, snapshot.Performance);
        Assert.Equal(0.98, snapshot.Quality);
        Assert.Equal(oee.Oee, snapshot.Oee);
        Assert.Equal(1, snapshot.TotalUpdates);
    }

    [Fact]
    public async Task GetAverageOee_ShouldIgnoreUninitializedDevices()
    {
        var oee = OeeRecord.Compute("EQ-TQ-01", DateTimeOffset.UtcNow, 1.0, 1.0, 1.0);
        await _publisher.PublishAsync(new PlcDataChanged(oee));

        var avg = _store.GetAverageOee();

        Assert.Equal(1.0, avg);
    }

    [Fact]
    public async Task GetAllSnapshots_ShouldContainAllEquipment()
    {
        var oee = OeeRecord.Compute("EQ-TQ-01", DateTimeOffset.UtcNow, 1.0, 1.0, 1.0);
        await _publisher.PublishAsync(new PlcDataChanged(oee));

        var snapshots = _store.GetAllSnapshots();

        Assert.Equal(Equipment.DefaultEquipment.Count, snapshots.Count);
        var tq = snapshots.First(s => s.EquipmentCode == "EQ-TQ-01");
        Assert.Equal(1.0, tq.Oee);
    }

    [Fact]
    public async Task ConcurrentUpdates_ShouldNotThrow()
    {
        var tasks = Enumerable.Range(0, 100)
            .Select(i =>
            {
                var oee = OeeRecord.Compute("EQ-TQ-01", DateTimeOffset.UtcNow.AddMilliseconds(i), 1.0, 1.0, 1.0);
                return _publisher.PublishAsync(new PlcDataChanged(oee)).AsTask();
            })
            .ToArray();

        await Task.WhenAll(tasks);

        var snapshot = _store.GetSnapshot("EQ-TQ-01");
        Assert.NotNull(snapshot);
        Assert.Equal(100, snapshot.TotalUpdates);
        Assert.Equal(1.0, snapshot.Oee);
    }
}
