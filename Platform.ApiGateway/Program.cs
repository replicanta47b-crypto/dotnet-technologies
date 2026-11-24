using System.Diagnostics;
using Platform.ServiceDefaults;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Model;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();   // Serilog, OpenTelemetry, health checks, service discovery

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();   // http://payments-api → реальна адреса від Aspire

var app = builder.Build();

// CorrelationId генерується тут (Gateway — перша точка входу) і ДО MapReverseProxy:
// YARP копіює заголовки запиту, тож X-Correlation-Id дійде до сервісу
app.UseMiddleware<CorrelationIdMiddleware>();

// Логування маршрутизації: метод, шлях, маршрут, кластер, статус, тривалість, розмір, IP клієнта, помилки сервісів
app.Use(async (context, next) =>
{
    var stopwatch = Stopwatch.StartNew();
    await next();
    stopwatch.Stop();

    var proxy = context.GetReverseProxyFeature();
    var route = proxy?.Route.Config.RouteId;
    var cluster = proxy?.Cluster.Config.ClusterId;

    app.Logger.LogInformation(
        "{Method} {Path} -> route {Route}, cluster {Cluster}: {StatusCode} in {Elapsed} ms, {ResponseBytes} bytes, client {ClientIp}",
        context.Request.Method, context.Request.Path.Value, route ?? "(no route)", cluster ?? "-",
        context.Response.StatusCode, stopwatch.ElapsedMilliseconds,
        context.Response.ContentLength ?? 0, context.Connection.RemoteIpAddress);

    // сервіс недоступний (timeout, connection refused, ...) — окремий запис з деталями
    var error = context.Features.Get<IForwarderErrorFeature>();
    if (error is not null)
        app.Logger.LogWarning(error.Exception,
            "Route {Route}: forwarding to cluster {Cluster} failed with {ForwarderError} ({StatusCode})",
            route, cluster, error.Error, context.Response.StatusCode);
});

app.MapReverseProxy();
app.MapGet("/", () => Results.Text("Platform API Gateway: /api/payments, /api/customers, /api/invoices, /api/billing/*, /api/merchants, /api/disputes, /api/aggregator/*"))
   .ExcludeFromDescription();
app.MapDefaultEndpoints();   // /health і /alive

app.Run();
