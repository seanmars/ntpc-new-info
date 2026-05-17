var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions()
{
    Args = args,
    DisableDashboard = false
});

var webapi = builder
    .AddProject<Projects.WebApi>("webapi")
    .PublishAsDockerFile();

builder.AddViteApp("vue-app", workingDirectory: "../vue-app", packageManager: "pnpm")
    .WithReference(webapi)
    .WaitFor(webapi)
    .PublishAsDockerFile();

builder.Build().Run();