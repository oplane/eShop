var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddRabbitMqEventBus("EventBus")
    .AddSubscription<OrderStatusChangedToStockConfirmedIntegrationEvent, OrderStatusChangedToStockConfirmedIntegrationEventHandler>();

builder.Services.AddOptions<PaymentOptions>()
    .BindConfiguration(nameof(PaymentOptions));

builder.Services.AddSingleton<StripeProcessor>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var apiKey = builder.Configuration["StripeKey"];
    return new StripeProcessor(apiKey);
});

var app = builder.Build();

app.MapDefaultEndpoints();

await app.RunAsync();
