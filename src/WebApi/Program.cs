using Microsoft.Extensions.Options;

using Rebus.Config;
using Rebus.ServiceProvider;
using Rebus.Transport.InMem;

using Scalar.AspNetCore;

using WebApi.Discord;
using WebApi.Json;
using WebApi.Messaging;
using WebApi.Options;
using WebApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.

builder.Services
    .AddControllers()
    .AddJsonOptions(opt =>
    {
        opt.JsonSerializerOptions.Converters.Add(new UInt64StringJsonConverter());
    });
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services
    .Configure<RescuePollingOptions>(
        builder.Configuration.GetSection(RescuePollingOptions.SectionName));
builder.Services
    .Configure<MonitorPointStoreOptions>(
        builder.Configuration.GetSection(MonitorPointStoreOptions.SectionName));
builder.Services
    .Configure<NominatimOptions>(
        builder.Configuration.GetSection(NominatimOptions.SectionName));
builder.Services
    .Configure<DiscordSettingsStoreOptions>(
        builder.Configuration.GetSection(DiscordSettingsStoreOptions.SectionName));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IRescueSnapshotStore, RescueSnapshotStore>();
builder.Services.AddSingleton<IMonitorPointStore, MonitorPointStore>();
builder.Services.AddSingleton<IDiscordSettingsStore, DiscordSettingsStore>();
builder.Services.AddSingleton<DiscordBotState>();
builder.Services.AddSingleton<IDiscordNotifier, DiscordRestNotifier>();

builder.Services.AddHttpClient<RescueDataFetcher>((sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<RescuePollingOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(opts.RequestTimeoutSeconds);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ntpc-new-info-backend/0.1");
});

builder.Services.AddHttpClient<NominatimGeocoder>((sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<NominatimOptions>>().Value;
    client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(opts.RequestTimeoutSeconds);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(opts.UserAgent);
});

builder.Services.AddSingleton<IMonitorPointEventDetector, MonitorPointEventDetector>();
builder.Services.AddSingleton<IRescueAllAlertsDetector, RescueAllAlertsDetector>();
builder.Services.AutoRegisterHandlersFromAssemblyOf<LoggingMonitorPointAlertHandler>();
builder.Services.AddRebus(configure => configure
    .Transport(t => t.UseInMemoryTransport(new InMemNetwork(), "monitor-point-alerts")));

builder.Services.AddHostedService<RescuePollingService>();
builder.Services.AddHostedService<DiscordBotLifecycleService>();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.UseAuthorization();

app.MapControllers();

app.Run();