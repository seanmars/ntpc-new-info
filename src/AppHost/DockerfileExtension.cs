namespace AppHost;

public static class DockerfileExtension
{
    public static void GenerateDockerfile(this IDistributedApplicationBuilder builder)
    {
#pragma warning disable ASPIREDOCKERFILEBUILDER001
        builder.AddDockerfileBuilder("frontend", "../../vue-app", context =>
        {
            var build = context.Builder.From("node:24-alpine", "build");
            build.WorkDir("/app")
                .Copy("package*.json", "./")
                .Run("pnpm ci")
                .Copy(".", ".")
                .Run("pnpm build");

            var runtime = context.Builder.From("nginx:alpine", "runtime");
            runtime.CopyFrom("build", "/app/dist", "/usr/share/nginx/html")
                .Expose(80);

            return Task.CompletedTask;
        }, stage: "runtime");
#pragma warning restore ASPIREDOCKERFILEBUILDER001
    }
}