using Microsoft.AspNetCore.SignalR.Client;

namespace DmCompanionLive.Api.IntegrationTests.Infrastructure;

/// <summary>Records every message a hub connection receives for one method, and waits for them.</summary>
public sealed class Inbox<T>
{
    private readonly List<T> _items = [];
    private readonly SemaphoreSlim _arrived = new(0);

    public Inbox(HubConnection connection, string method) =>
        connection.On<T>(method, item =>
        {
            lock (_items)
            {
                _items.Add(item);
            }

            _arrived.Release();
        });

    public IReadOnlyList<T> All
    {
        get
        {
            lock (_items)
            {
                return [.. _items];
            }
        }
    }

    /// <summary>The latest message matching <paramref name="match"/>, waiting for it if needed.</summary>
    public async Task<T> WaitForAsync(Func<T, bool> match, TimeSpan? timeout = null)
    {
        T found = default!;
        await WaitUntilAsync(
            items =>
            {
                for (var i = items.Count - 1; i >= 0; i--)
                {
                    if (match(items[i]))
                    {
                        found = items[i];
                        return true;
                    }
                }

                return false;
            },
            timeout);
        return found;
    }

    public async Task WaitUntilAsync(Func<IReadOnlyList<T>, bool> condition, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        while (true)
        {
            lock (_items)
            {
                if (condition(_items))
                {
                    return;
                }
            }

            try
            {
                await _arrived.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"Timed out waiting for {typeof(T).Name} messages.");
            }
        }
    }
}
