#!/usr/bin/env dotnet
#:sdk Microsoft.NET.Sdk.Web
#:property PublishAot=false

// .NET SDK 10: dotnet run --file TestServer.cs
// Параметры: dotnet run --file TestServer.cs -- --port=8081 --delay-ms=5000
// Тайм-аут клиента должен быть меньше delay-ms (по умолчанию 10000 мс).
// Get /reset сбрасывает очереди, но не отменяет уже принятые запросы.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);
int port = builder.Configuration.GetValue("port", 8080);
int delayMs = builder.Configuration.GetValue("delay-ms", 10000);
if (port is < 1 or > 65535 || delayMs < 1)
    throw new ArgumentException("Порт: 1–65535; delay-ms: положительное число миллисекунд.");

var app = builder.Build();
var gate = new object();

// null означает HTTP 200 после задержки, превышающей тайм-аут клиента.
var sequences = new Dictionary<string, int?[]>
{
    ["/success"] = [200],
    ["/temporary"] = [503, 503, 200],
    ["/timeout"] = [null, 200],
    ["/unavailable"] = [503, 503, 503, 503],
    ["/bad-request"] = [400, 200]
};
var queues = new Dictionary<string, Queue<int?>>();
Reset();

foreach (var path in sequences.Keys)
{
    string endpoint = path;
    app.MapGet(endpoint, async (HttpContext context) =>
    {
        int? result;
        // Сброс и получение результата защищены от параллельных запросов.
        lock (gate)
        {
            if (!queues[endpoint].TryDequeue(out result))
                return Results.Text("Sequence exhausted. Use POST /reset.\n", statusCode: 410);
        }

        if (result is null)
            await Task.Delay(delayMs, context.RequestAborted);

        int status = result ?? 200;
        return Results.Text($"HTTP {status}\n", statusCode: status);
    });
}

app.MapGet("/reset", () =>
{
    Reset();
    return Results.Ok(new { message = "All sequences reset." });
});

app.Run($"http://localhost:{port}");

void Reset()
{
    lock (gate)
    {
        foreach (var pair in sequences)
            queues[pair.Key] = new Queue<int?>(pair.Value);
    }
}
