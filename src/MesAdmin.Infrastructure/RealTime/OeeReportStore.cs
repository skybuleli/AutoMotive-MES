using System.Collections.Concurrent;
using MesAdmin.Domain.Models;
using MessagePipe;
using MesAdmin.Infrastructure.RealTime;

namespace MesAdmin.Infrastructure.Reports;

/// <summary>
/// OEE 报表数据存储器（T4.2）。
/// 订阅 PlcDataChanged 消息（由 OeeReactivePipeline 发布），
/// 维护每设备最新 OEE 记录，供日报聚合使用。
/// 使用 ConcurrentDictionary + 每设备原子更新，避免全局锁竞争。
/// </summary>
public sealed class OeeReportStore : IDisposable
{
    private readonly ConcurrentDictionary<string, OeeDeviceState> _states;
    private IDisposable? _subscription;

    /// <summary>设备清单（用于初始化状态字典）</summary>
    public OeeReportStore(IAsyncSubscriber<PlcDataChanged> subscriber)
    {
        // 预初始化 8 台设备的状态
        _states = new ConcurrentDictionary<string, OeeDeviceState>(
            Equipment.DefaultEquipment.Select(eq =>
                new KeyValuePair<string, OeeDeviceState>(
                    eq.EquipmentCode,
                    new OeeDeviceState(eq.EquipmentCode, eq.Name))));

        // 订阅 OEE 实时推送（同步处理，零分配）
        _subscription = subscriber.Subscribe((msg, _) =>
        {
            var oee = msg.Oee;
            if (_states.TryGetValue(oee.EquipmentCode, out var state))
            {
                state.Update(oee);
            }
            return new ValueTask();
        });
    }

    /// <summary>获取所有设备的最新 OEE 快照</summary>
    public List<OeeDeviceSnapshot> GetAllSnapshots()
    {
        return _states.Values.Select(s => s.ToSnapshot()).ToList();
    }

    /// <summary>获取指定设备的最新 OEE 快照</summary>
    public OeeDeviceSnapshot? GetSnapshot(string equipmentCode)
    {
        return _states.TryGetValue(equipmentCode, out var state) ? state.ToSnapshot() : null;
    }

    /// <summary>获取整体 OEE 平均值</summary>
    public double GetAverageOee()
    {
        var snapshots = _states.Values
            .Select(s => s.ToSnapshot())
            .Where(s => s.TotalUpdates > 0)
            .ToList();
        return snapshots.Count > 0 ? snapshots.Average(s => s.Oee) : 0;
    }

    public void Dispose()
    {
        _subscription?.Dispose();
    }

    /// <summary>单设备 OEE 滚动状态</summary>
    private sealed class OeeDeviceState
    {
        private readonly string _code;
        private readonly string _name;
        private OeeRecord? _latest;
        private int _totalUpdates;

        public OeeDeviceState(string code, string name)
        {
            _code = code;
            _name = name;
        }

        public void Update(OeeRecord oee)
        {
            _latest = oee;
            Interlocked.Increment(ref _totalUpdates);
        }

        public OeeDeviceSnapshot ToSnapshot()
        {
            var latest = _latest;
            if (latest is null)
            {
                return new OeeDeviceSnapshot(
                    _code, _name,
                    0, 0, 0, 0,
                    _totalUpdates, DateTimeOffset.UtcNow);
            }

            return new OeeDeviceSnapshot(
                _code, _name,
                latest.Availability, latest.Performance, latest.Quality, latest.Oee,
                _totalUpdates, latest.Timestamp);
        }
    }
}

/// <summary>OEE 设备快照（报表数据）</summary>
public sealed record OeeDeviceSnapshot(
    string EquipmentCode,
    string EquipmentName,
    double Availability,
    double Performance,
    double Quality,
    double Oee,
    int TotalUpdates,
    DateTimeOffset LastUpdate
);
