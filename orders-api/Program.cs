using Microsoft.EntityFrameworkCore;
using Orders.Api.Data;
using Orders.Api.Endpoints;
using Orders.Api.Hubs;
using Orders.Api.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .SetIsOriginAllowed(_ => true)
            .AllowCredentials();
    });
});

builder.Services.AddDbContext<OrdersDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("OrdersDb")));

builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection(RabbitMqOptions.SectionName));

builder.Services.AddSingleton<IOrderEventPublisher, RabbitMqOrderPublisher>();
builder.Services.AddHostedService<OrderEventsConsumer>();

var app = builder.Build();

await EnsureDatabaseCreatedAsync(app);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).WithTags("Health");
app.MapOrdersEndpoints();
app.MapHub<OrdersHub>("/hubs/orders");

app.Run();

static async Task EnsureDatabaseCreatedAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    for (var attempt = 1; attempt <= 10; attempt++)
    {
        try
        {
            await db.Database.EnsureCreatedAsync();
            return;
        }
        catch (Exception ex) when (attempt < 10)
        {
            logger.LogWarning(ex, "Postgres not ready (attempt {Attempt}/10). Retrying in 2s...", attempt);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}

public partial class Program;

