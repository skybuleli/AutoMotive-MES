using MemoryPack;

namespace MesAdmin.Api.Features.ProductionOrders.Create;

[MemoryPackable]
public partial class CreateOrderRequest
{
    public string ProductCode { get; set; } = string.Empty;
    public string BomVersion { get; set; } = string.Empty;
    public string RoutingId { get; set; } = string.Empty;
    public int PlannedQuantity { get; set; }
    public short Priority { get; set; } = 1;
}
