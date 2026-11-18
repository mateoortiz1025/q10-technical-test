using Microsoft.EntityFrameworkCore;
using Orders.Api.Contracts;
using Orders.Api.Data;
using Orders.Api.Domain;
using Orders.Api.Messaging;

namespace Orders.Api.Endpoints;

public static class OrdersEndpoints
{
    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/orders").WithTags("Orders");

        group.MapPost("/", CreateOrderAsync)
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/", GetOrdersAsync)
            .Produces<IReadOnlyList<OrderResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetOrderByIdAsync)
            .Produces<OrderResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    public static Dictionary<string, string[]> ValidateRequest(CreateOrderRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        var nombre = request.ClienteNombre?.Trim();
        if (string.IsNullOrWhiteSpace(nombre))
            errors["clienteNombre"] = ["El nombre del cliente es obligatorio."];
        else if (nombre.Length > 120)
            errors["clienteNombre"] = ["El nombre del cliente no puede superar los 120 caracteres."];

        var sku = request.Sku?.Trim();
        if (string.IsNullOrWhiteSpace(sku))
            errors["sku"] = ["El SKU es obligatorio."];
        else if (sku.Length > 64)
            errors["sku"] = ["El SKU no puede superar los 64 caracteres."];

        if (request.Cantidad is < 1 or > 100)
            errors["cantidad"] = ["La cantidad debe estar entre 1 y 100."];

        return errors;
    }

    private static async Task<IResult> CreateOrderAsync(
        CreateOrderRequest request,
        OrdersDbContext db,
        IOrderEventPublisher publisher,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var errors = ValidateRequest(request);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var now = DateTime.UtcNow;
        var order = new Order
        {
            Id = Guid.NewGuid(),
            ClienteNombre = request.ClienteNombre!.Trim(),
            Sku = request.Sku!.Trim().ToUpperInvariant(),
            Cantidad = request.Cantidad,
            Status = OrderStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        try
        {
            var evt = new OrderCreatedEvent(
                EventId: Guid.NewGuid(),
                OrderId: order.Id,
                Sku: order.Sku,
                Cantidad: order.Cantidad,
                OccurredAt: now);

            publisher.PublishOrderCreated(evt);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            var logger = loggerFactory.CreateLogger("OrdersEndpoints");
            logger.LogError(ex, "Failed to publish OrderCreatedEvent for order {OrderId}", order.Id);

            return Results.Problem(
                title: "Servicio de mensajería no disponible",
                detail: "No fue posible encolar el pedido en RabbitMQ. Intenta nuevamente.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var response = OrderResponse.FromEntity(order);
        return Results.Created($"/orders/{order.Id}", response);
    }

    private static async Task<IResult> GetOrdersAsync(OrdersDbContext db, CancellationToken ct)
    {
        var orders = await db.Orders
            .AsNoTracking()
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => OrderResponse.FromEntity(o))
            .ToListAsync(ct);

        return Results.Ok(orders);
    }

    private static async Task<IResult> GetOrderByIdAsync(Guid id, OrdersDbContext db, CancellationToken ct)
    {
        var order = await db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, ct);

        return order is null
            ? Results.NotFound(new { message = $"Pedido {id} no encontrado." })
            : Results.Ok(OrderResponse.FromEntity(order));
    }
}

