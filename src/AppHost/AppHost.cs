using AppHost;

var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions()
{
    Args = args,
    DisableDashboard = false
});

builder.AddDockerComposeEnvironment("env");

// builder.GenerateDockerfile();

var webapi = builder
    .AddProject<Projects.WebApi>("webapi");

builder.AddViteApp("vue-app", workingDirectory: "../../vue-app", packageManager: "pnpm")
    .WithReference(webapi)
    .WaitFor(webapi);

builder.Build().Run();