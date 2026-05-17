using Microsoft.Extensions.Options;

using Scalar.AspNetCore;

using WebApi.Options;
using WebApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services
    .Configure<RescuePollingOptions>(
        builder.Configuration.GetSection(RescuePollingOptions.SectionName));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IRescueSnapshotStore, RescueSnapshotStore>();

builder.Services.AddHttpClient<RescueDataFetcher>((sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<RescuePollingOptions>>().Value;
    client.Timeout = opts.RequestTimeout;
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ntpc-new-info-backend/0.1");
});

builder.Services.AddHostedService<RescuePollingService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
