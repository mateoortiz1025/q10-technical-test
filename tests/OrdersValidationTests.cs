using Orders.Api.Contracts;
using Orders.Api.Endpoints;
using Xunit;

namespace OrderFlow.Tests;

/// <summary>
/// Pruebas unitarias para las reglas de validación del endpoint POST /orders.
/// </summary>
public sealed class OrdersValidationTests
{
    /// <summary>
    /// Verifica que un pedido con cliente, SKU y cantidad dentro del rango (1-100) no genere errores.
    /// </summary>
    [Fact]
    public void ValidateRequest_ValidPayload_ReturnsNoErrors()
    {
        var request = new CreateOrderRequest("Ana Gómez", "LAPTOP-PRO-15", 2);

        var errors = OrdersEndpoints.ValidateRequest(request);

        Assert.Empty(errors);
    }

    /// <summary>
    /// Verifica que los campos obligatorios vacíos o cantidades fuera de rango (1-100) retornen el error esperado.
    /// </summary>
    [Theory]
    [InlineData(null, "LAPTOP-PRO-15", 1, "clienteNombre")]
    [InlineData("   ", "LAPTOP-PRO-15", 1, "clienteNombre")]
    [InlineData("Ana", "", 1, "sku")]
    [InlineData("Ana", "LAPTOP-PRO-15", 0, "cantidad")]
    [InlineData("Ana", "LAPTOP-PRO-15", 101, "cantidad")]
    public void ValidateRequest_InvalidFields_ReturnsExpectedErrorKey(
        string? clienteNombre,
        string? sku,
        int cantidad,
        string expectedKey)
    {
        var request = new CreateOrderRequest(clienteNombre, sku, cantidad);

        var errors = OrdersEndpoints.ValidateRequest(request);

        Assert.True(errors.ContainsKey(expectedKey));
    }
}
