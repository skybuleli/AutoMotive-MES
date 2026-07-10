using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
{
    SimulatorSelfTest.Run();
    return;
}

var builder = WebApplication.CreateBuilder(args);
var settings = builder.Configuration.GetSection("Simulator").Get<SimulatorSettings>() ?? new();
settings.EnsureDefaults();
builder.WebHost.UseUrls($"http://0.0.0.0:{settings.HttpPort}");
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<SimulatorState>();
builder.Services.AddHostedService<ModbusSimulatorService>();

var app = builder.Build();

app.MapGet("/health", (SimulatorState state, SimulatorSettings cfg) => Results.Ok(new
{
    status = "ok",
    scenario = state.CurrentScenario,
    sap = $"http://localhost:{cfg.HttpPort}",
    modbus = cfg.Devices.Select(d => new { d.EquipmentCode, d.Host, d.Port })
}));

app.MapGet("/scenario", (SimulatorState state) => Results.Ok(new { scenario = state.CurrentScenario }));
app.MapPost("/scenario/normal", (SimulatorState state) => SetScenario(state, Scenario.Normal));
app.MapPost("/scenario/plc-down", (SimulatorState state) => SetScenario(state, Scenario.PlcDown));
app.MapPost("/scenario/torque-ng", (SimulatorState state) => SetScenario(state, Scenario.TorqueNg));
app.MapPost("/scenario/hydraulic-leak", (SimulatorState state) => SetScenario(state, Scenario.HydraulicLeak));
app.MapPost("/scenario/sap-timeout", (SimulatorState state) => SetScenario(state, Scenario.SapTimeout));
app.MapPost("/scenario/sap-fail", (SimulatorState state) => SetScenario(state, Scenario.SapFail));
app.MapPost("/scenario/sap-recover", (SimulatorState state) => SetScenario(state, Scenario.Normal));

app.MapGet("/api/sap/health", (SimulatorState state) =>
    state.CurrentScenario == Scenario.SapFail
        ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
        : Results.Ok(new { status = "ok", scenario = state.CurrentScenario.ToString() }));

app.MapPost("/api/sap/order/status", HandleSapAsync);
app.MapPost("/api/sap/inventory/sync", HandleSapAsync);
app.MapPost("/api/sap/material/movement", HandleSapAsync);
app.MapPost("/api/sap/order/rejection", HandleSapAsync);

await app.RunAsync();

static IResult SetScenario(SimulatorState state, Scenario scenario)
{
    state.SetScenario(scenario);
    return Results.Ok(new { scenario = state.CurrentScenario });
}

static async Task<IResult> HandleSapAsync(HttpContext http, SimulatorState state, SimulatorSettings settings)
{
    if (state.CurrentScenario == Scenario.SapTimeout)
        await Task.Delay(settings.SapTimeoutDelayMs, http.RequestAborted);

    if (state.CurrentScenario == Scenario.SapFail)
        return Results.StatusCode(StatusCodes.Status500InternalServerError);

    return Results.Json(new { document_number = state.NextDocumentNumber() });
}

public sealed class SimulatorSettings
{
    public int HttpPort { get; set; } = 18080;
    public int SapTimeoutDelayMs { get; set; } = 35000;
    public int ModbusPollMs { get; set; } = 500;
    public List<ModbusDeviceSettings> Devices { get; set; } = [];

    public void EnsureDefaults()
    {
        if (Devices.Count > 0)
            return;

        Devices =
        [
            new() { EquipmentCode = "EQ-FLS-01", Host = "127.0.0.1", Port = 15021, UnitId = 1, ProcessTagIndex = 1 },
            new() { EquipmentCode = "EQ-VN-01", Host = "127.0.0.1", Port = 15022, UnitId = 1, ProcessTagIndex = 2 }
        ];
    }
}

public sealed class ModbusDeviceSettings
{
    public string EquipmentCode { get; set; } = "";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; }
    public byte UnitId { get; set; } = 1;
    public ushort ProcessTagIndex { get; set; }
}

public enum Scenario
{
    Normal,
    PlcDown,
    TorqueNg,
    HydraulicLeak,
    SapTimeout,
    SapFail
}

public sealed class SimulatorState
{
    private readonly object _lock = new();
    private readonly Dictionary<string, DeviceState> _devices;
    private long _documentSeq;
    private Scenario _scenario;

    public SimulatorState(SimulatorSettings settings)
    {
        _devices = settings.Devices.ToDictionary(
            d => d.EquipmentCode,
            d => new DeviceState(d.EquipmentCode, d.ProcessTagIndex),
            StringComparer.Ordinal);
    }

    public Scenario CurrentScenario
    {
        get { lock (_lock) return _scenario; }
    }

    public void SetScenario(Scenario scenario)
    {
        lock (_lock)
        {
            _scenario = scenario;
            foreach (var device in _devices.Values)
                device.ApplyScenario(scenario);
        }
    }

    public DeviceSnapshot Tick(string equipmentCode, int elapsedMs)
    {
        lock (_lock)
        {
            var device = _devices[equipmentCode];
            device.Tick(_scenario, elapsedMs);
            return device.ToSnapshot();
        }
    }

    public DeviceSnapshot Snapshot(string equipmentCode)
    {
        lock (_lock)
            return _devices[equipmentCode].ToSnapshot();
    }

    public void WriteRegister(string equipmentCode, ushort address, ushort value)
    {
        lock (_lock)
            _devices[equipmentCode].WriteRegister(address, value);
    }

    public string NextDocumentNumber()
    {
        var seq = Interlocked.Increment(ref _documentSeq);
        return $"SIM-{DateTime.UtcNow:yyyyMMdd}-{seq:000000}";
    }
}

public sealed class DeviceState(string equipmentCode, ushort processTagIndex)
{
    private long _cycle;
    private long _good;
    private long _defect;
    private long _runMs;
    private ushort _status;
    private double _processValue = equipmentCode == "EQ-VN-01" ? 90.0 : 20.0;

    public void ApplyScenario(Scenario scenario)
    {
        _status = scenario is Scenario.PlcDown ? (ushort)3 : scenario is Scenario.TorqueNg or Scenario.HydraulicLeak ? (ushort)2 : (ushort)0;
    }

    public void Tick(Scenario scenario, int elapsedMs)
    {
        ApplyScenario(scenario);
        if (scenario == Scenario.PlcDown)
            return;

        _cycle++;
        _runMs += elapsedMs;
        var failed = scenario is Scenario.TorqueNg or Scenario.HydraulicLeak || _cycle % 50 == 0;
        if (failed) _defect++;
        else _good++;

        _processValue = scenario switch
        {
            Scenario.TorqueNg => 99.9,
            Scenario.HydraulicLeak => 8.8,
            _ when equipmentCode == "EQ-VN-01" => 80.0 + _cycle % 20,
            _ => 10.0 + _cycle % 40
        };
    }

    public DeviceSnapshot ToSnapshot() => new(
        _status,
        LowWord(_cycle),
        LowWord(_good),
        LowWord(_defect),
        LowWord(_runMs),
        HighWord(_runMs),
        (ushort)Math.Clamp((int)Math.Round(_processValue * 10), 0, ushort.MaxValue),
        processTagIndex);

    public void WriteRegister(ushort address, ushort value)
    {
        if (address == 0)
            _status = value;
        else if (address == 6)
            _processValue = value / 10.0;
    }

    private static ushort LowWord(long value) => (ushort)(value & 0xFFFF);
    private static ushort HighWord(long value) => (ushort)((value >> 16) & 0xFFFF);
}

public readonly record struct DeviceSnapshot(
    ushort Status,
    ushort CycleLow,
    ushort GoodLow,
    ushort DefectLow,
    ushort RunMsLow,
    ushort RunMsHigh,
    ushort ProcessValueScaled,
    ushort ProcessTagIndex)
{
    public ushort Read(ushort address) => address switch
    {
        0 => Status,
        1 => CycleLow,
        2 => GoodLow,
        3 => DefectLow,
        4 => RunMsLow,
        5 => RunMsHigh,
        6 => ProcessValueScaled,
        7 => ProcessTagIndex,
        _ => 0
    };
}

public sealed class ModbusSimulatorService(
    SimulatorSettings settings,
    SimulatorState state,
    ILogger<ModbusSimulatorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tasks = settings.Devices.Select(d => RunDeviceAsync(d, stoppingToken)).ToArray();
        await Task.WhenAll(tasks);
    }

    private async Task RunDeviceAsync(ModbusDeviceSettings device, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Parse(device.Host), device.Port);
        listener.Start();
        logger.LogInformation("Modbus simulator listening for {EquipmentCode} on {Host}:{Port}", device.EquipmentCode, device.Host, device.Port);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(device, client, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleClientAsync(ModbusDeviceSettings device, TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        var stream = client.GetStream();
        var buffer = new byte[260];

        while (!ct.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
                return;

            if (state.CurrentScenario == Scenario.PlcDown)
                return;

            var response = ModbusProtocol.HandleRequest(buffer.AsSpan(0, read), device, state, settings.ModbusPollMs);
            if (response.Length > 0)
                await stream.WriteAsync(response, ct);
        }
    }
}

public static class ModbusProtocol
{
    public static byte[] HandleRequest(ReadOnlySpan<byte> request, ModbusDeviceSettings device, SimulatorState state, int elapsedMs)
    {
        if (request.Length < 8)
            return [];

        var transactionId = BinaryPrimitives.ReadUInt16BigEndian(request[0..2]);
        var unitId = request[6];
        var function = request[7];
        if (unitId != device.UnitId)
            return BuildException(transactionId, unitId, function, 0x0B);

        return function switch
        {
            0x03 or 0x04 => ReadRegisters(request, transactionId, unitId, function, device, state, elapsedMs),
            0x06 => WriteSingleRegister(request, transactionId, unitId, function, device, state),
            0x10 => WriteMultipleRegisters(request, transactionId, unitId, function, device, state),
            _ => BuildException(transactionId, unitId, function, 0x01)
        };
    }

    private static byte[] ReadRegisters(
        ReadOnlySpan<byte> request,
        ushort transactionId,
        byte unitId,
        byte function,
        ModbusDeviceSettings device,
        SimulatorState state,
        int elapsedMs)
    {
        if (request.Length < 12)
            return BuildException(transactionId, unitId, function, 0x03);

        var start = BinaryPrimitives.ReadUInt16BigEndian(request[8..10]);
        var quantity = BinaryPrimitives.ReadUInt16BigEndian(request[10..12]);
        if (quantity is 0 or > 64)
            return BuildException(transactionId, unitId, function, 0x03);

        var snapshot = state.Tick(device.EquipmentCode, elapsedMs);
        var response = new byte[9 + quantity * 2];
        WriteHeader(response, transactionId, unitId, function, (ushort)(3 + quantity * 2));
        response[8] = (byte)(quantity * 2);

        for (var i = 0; i < quantity; i++)
        {
            var value = snapshot.Read((ushort)(start + i));
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(9 + i * 2, 2), value);
        }

        return response;
    }

    private static byte[] WriteSingleRegister(
        ReadOnlySpan<byte> request,
        ushort transactionId,
        byte unitId,
        byte function,
        ModbusDeviceSettings device,
        SimulatorState state)
    {
        if (request.Length < 12)
            return BuildException(transactionId, unitId, function, 0x03);

        var address = BinaryPrimitives.ReadUInt16BigEndian(request[8..10]);
        var value = BinaryPrimitives.ReadUInt16BigEndian(request[10..12]);
        state.WriteRegister(device.EquipmentCode, address, value);
        return request[..12].ToArray();
    }

    private static byte[] WriteMultipleRegisters(
        ReadOnlySpan<byte> request,
        ushort transactionId,
        byte unitId,
        byte function,
        ModbusDeviceSettings device,
        SimulatorState state)
    {
        if (request.Length < 13)
            return BuildException(transactionId, unitId, function, 0x03);

        var start = BinaryPrimitives.ReadUInt16BigEndian(request[8..10]);
        var quantity = BinaryPrimitives.ReadUInt16BigEndian(request[10..12]);
        var byteCount = request[12];
        if (quantity is 0 or > 64 || byteCount != quantity * 2 || request.Length < 13 + byteCount)
            return BuildException(transactionId, unitId, function, 0x03);

        for (var i = 0; i < quantity; i++)
        {
            var value = BinaryPrimitives.ReadUInt16BigEndian(request.Slice(13 + i * 2, 2));
            state.WriteRegister(device.EquipmentCode, (ushort)(start + i), value);
        }

        var response = new byte[12];
        request[..12].CopyTo(response);
        return response;
    }

    private static byte[] BuildException(ushort transactionId, byte unitId, byte function, byte code)
    {
        var response = new byte[9];
        WriteHeader(response, transactionId, unitId, (byte)(function | 0x80), 3);
        response[8] = code;
        return response;
    }

    private static void WriteHeader(Span<byte> response, ushort transactionId, byte unitId, byte function, ushort length)
    {
        BinaryPrimitives.WriteUInt16BigEndian(response[0..2], transactionId);
        response[2] = 0;
        response[3] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(response[4..6], length);
        response[6] = unitId;
        response[7] = function;
    }
}

public static class SimulatorSelfTest
{
    public static void Run()
    {
        var settings = new SimulatorSettings();
        settings.EnsureDefaults();
        var state = new SimulatorState(settings);
        var device = settings.Devices[0];
        Span<byte> request = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(request[0..2], 7);
        BinaryPrimitives.WriteUInt16BigEndian(request[4..6], 6);
        request[6] = device.UnitId;
        request[7] = 0x03;
        BinaryPrimitives.WriteUInt16BigEndian(request[8..10], 0);
        BinaryPrimitives.WriteUInt16BigEndian(request[10..12], 8);

        var response = ModbusProtocol.HandleRequest(request, device, state, settings.ModbusPollMs);
        Assert(response.Length == 25, "read response length");
        Assert(response[0] == 0 && response[1] == 7, "transaction id echoes");
        Assert(response[7] == 0x03, "function echoes");
        Assert(response[8] == 16, "byte count");
        Assert(response[10] == 0, "normal status is running");

        state.SetScenario(Scenario.PlcDown);
        var down = state.Snapshot(device.EquipmentCode);
        Assert(down.Status == 3, "plc-down status");

        Console.WriteLine("Simulator self-test passed.");
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException($"Simulator self-test failed: {name}");
    }
}
