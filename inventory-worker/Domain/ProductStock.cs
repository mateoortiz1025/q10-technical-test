namespace Inventory.Worker.Domain;

public sealed class ProductStock
{
    public string Sku { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int Disponible { get; set; }
    public DateTime UpdatedAt { get; set; }
}
