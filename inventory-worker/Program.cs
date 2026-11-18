using Inventory.Worker.Data;
using Inventory.Worker.Messaging;
using Inventory.Worker.Services;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("InventoryDb")));

builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection(RabbitMqOptions.SectionName));

builder.Services.AddScoped<StockReservationService>();
builder.Services.AddHostedService<OrderCreatedConsumer>();

var host = builder.Build();

await EnsureDatabaseAndSeedAsync(host);

host.Run();

static async Task EnsureDatabaseAndSeedAsync(IHost host)
{
    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    for (var attempt = 1; attempt <= 10; attempt++)
    {
        try
        {
            await db.Database.EnsureCreatedAsync();
            await InventorySeeder.SeedAsync(db);
            logger.LogInformation("Inventory database ready and seeded.");
            return;
        }
        catch (Exception ex) when (attempt < 10)
        {
            logger.LogWarning(ex, "Postgres not ready (attempt {Attempt}/10). Retrying in 2s...", attempt);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}
