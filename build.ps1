Write-Host "Building the Vue.js application using Podman..."
podman build -t ntpc-news-vue-app:latest ./vue-app

Write-Host "Building the .NET API using Podman..."
podman build -t ntpc-news-webapi:latest -f src/WebApi/Dockerfile .
