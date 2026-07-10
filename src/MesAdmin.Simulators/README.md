# AutoMES Simulators

Lightweight local simulator for manual MES integration demos.

```bash
dotnet run --project src/MesAdmin.Simulators
dotnet run --project src/MesAdmin.Simulators -- --self-test
ASPNETCORE_ENVIRONMENT=Simulator dotnet run --project src/MesAdmin.Api
```

HTTP endpoints:

- `GET /health`
- `GET /scenario`
- `POST /scenario/normal`
- `POST /scenario/plc-down`
- `POST /scenario/torque-ng`
- `POST /scenario/hydraulic-leak`
- `POST /scenario/sap-timeout`
- `POST /scenario/sap-fail`
- `POST /scenario/sap-recover`
- `GET /api/sap/health`
- `POST /api/sap/order/status`
- `POST /api/sap/inventory/sync`
- `POST /api/sap/material/movement`
- `POST /api/sap/order/rejection`

Modbus TCP:

- `EQ-FLS-01`: `127.0.0.1:15021`
- `EQ-VN-01`: `127.0.0.1:15022`

Holding registers:

- `40001` status: `0=Running`, `1=Idle`, `2=Alarm`, `3=Offline`
- `40002` cycle count low
- `40003` good count low
- `40004` defect count low
- `40005` run time ms low
- `40006` run time ms high
- `40007` process value scaled by 10
- `40008` process tag index
