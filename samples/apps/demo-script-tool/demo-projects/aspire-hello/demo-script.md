# Aspire hello-world demo

## Demo Prompt

This demo introduces .NET Aspire by building up a multi-service hello-world
app. Multi-file mode — each step is a folder `step-NN/` containing one or
more `.cs` files plus a `.csproj` so it can be built with `dotnet build`
and run with `dotnet run`. Target framework: net10.0. Each step should be
runnable on its own and visibly extend the previous step's behaviour.
Audience is .NET developers new to Aspire.

## Steps

1. **AppHost scaffold**
   Create a minimal Aspire `AppHost` project that adds nothing — just the
   `DistributedApplication.CreateBuilder` and `Build().Run()` sequence.
   Confirm `dotnet run` brings up the empty dashboard.

2. **Add a worker service**
   Add a small worker project under `Worker/` that prints "Hello from
   Worker" every second. Wire it into the AppHost via
   `builder.AddProject<Projects.Worker>()`.

3. **Add an API service**
   Add a minimal API project under `Api/` exposing `GET /hello` that
   returns the JSON `{ "message": "Hello, demo" }`. Wire it into the
   AppHost.

4. **Wire a service reference**
   Update the worker to call the API service every second and log the
   response. Use Aspire's service-discovery mechanism to inject the API
   base URL into the worker.
