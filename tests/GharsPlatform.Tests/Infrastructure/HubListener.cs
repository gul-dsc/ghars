using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace GharsPlatform.Tests.Infrastructure;

/// <summary>
/// A real SignalR client connected to the application's notification hub as one user, recording
/// every message the server pushes to it.
/// </summary>
public sealed class HubListener : IAsyncDisposable
{
    private readonly HubConnection _connection;

    public ConcurrentQueue<JsonElement> Received { get; } = new();

    public string UserId { get; }

    private HubListener(HubConnection connection, string userId)
    {
        _connection = connection;
        UserId = userId;
    }

    public static async Task<HubListener> ConnectAsync(GharsAppFactory factory, string userId)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/notifications"), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
                o.Headers[TestAuthHandler.Header] = userId;
            })
            .Build();

        var listener = new HubListener(connection, userId);
        // Both event names any producer has ever used, so a regression to the old name is caught too.
        connection.On<JsonElement>("notification", p => listener.Received.Enqueue(p));
        connection.On<JsonElement>("notificationReceived", p => listener.Received.Enqueue(p));
        await connection.StartAsync();
        return listener;
    }

    /// <summary>True once a message whose English title is <paramref name="titleEn"/> has arrived.</summary>
    public bool Has(string titleEn) => Received.Any(p => Title(p) == titleEn);

    public static string? Title(JsonElement p)
        => p.TryGetProperty("titleEn", out var t) ? t.GetString()
           : p.TryGetProperty("title", out var t2) ? t2.GetString() : null;

    public async Task<bool> WaitForAsync(string titleEn, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (Has(titleEn)) return true;
            await Task.Delay(50);
        }
        return Has(titleEn);
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}
