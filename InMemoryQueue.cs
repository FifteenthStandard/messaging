using System.Collections.Concurrent;

namespace FifteenthStandard.Messaging;

public class InMemoryQueue<T> : IQueue<T>
{
    private readonly ConcurrentQueue<T> _queue;
    private readonly ConcurrentDictionary<string, T> _inFlight;
    private readonly ConcurrentQueue<(DateTime Timeout, string Id)> _timeouts;

    public InMemoryQueue()
    {
        _queue = new ConcurrentQueue<T>();
        _inFlight = new ConcurrentDictionary<string, T>();
        _timeouts = new ConcurrentQueue<(DateTime timeout, string Id)>();
    }

    public Task SendAsync(T message)
    {
        _queue.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<T>> SendBatchAsync(IEnumerable<T> messages)
    {
        foreach (var message in messages)
        {
            _queue.Enqueue(message);
        }
        return Task.FromResult(Enumerable.Empty<T>());
    }

    public async Task<(string Id, T Message)> ReceiveAsync(int visibilityTimeoutSeconds, int waitTimeoutSeconds)
    {
        FlushInflight();

        var waitTimeout = new CancellationTokenSource(waitTimeoutSeconds * 1_000);

        while (!waitTimeout.IsCancellationRequested)
        {
            if (!_queue.TryDequeue(out var message))
            {
                await Task.Delay(1000, waitTimeout.Token);
                continue;
            }

            var id = Guid.NewGuid().ToString();
            _inFlight[id] = message;
            _timeouts.Enqueue((DateTime.UtcNow.AddSeconds(visibilityTimeoutSeconds), id));

            return (id, message);
        }

        throw new OperationCanceledException("Wait timeout exceeded");
    }

    public async Task<IEnumerable<(string Id, T Message)>> ReceiveBatchAsync(int count, int visibilityTimeoutSeconds, int waitTimeoutSeconds)
    {
        var waitTimeout = new CancellationTokenSource(waitTimeoutSeconds * 1_000);

        var messages = new List<(string Id, T Message)>();

        while (!waitTimeout.IsCancellationRequested && messages.Count < count)
        {
            FlushInflight();

            if (!_queue.TryDequeue(out var message))
            {
                if (messages.Any())
                {
                    break;
                }
                else
                {
                    await Task.Delay(1000, waitTimeout.Token);
                    continue;
                }
            }

            var id = Guid.NewGuid().ToString();
            _inFlight[id] = message;
            _timeouts.Enqueue((DateTime.UtcNow.AddSeconds(visibilityTimeoutSeconds), id));

            messages.Add((id, message));
        }

        return messages;
    }

    public Task RemoveAsync(string id)
    {
        _inFlight.Remove(id, out var _);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<string>> RemoveBatchAsync(IEnumerable<string> ids)
    {
        var failed = new List<string>();

        foreach (var id in ids)
        {
            if (!_inFlight.Remove(id, out var _)) failed.Add(id);
        }

        return Task.FromResult((IEnumerable<string>)failed);
    }

    private void FlushInflight()
    {
        // Try to avoid dequeuing non-timed-out items
        if (!_timeouts.TryPeek(out var item) || item.Timeout > DateTime.UtcNow) return;

        // There is a chance another thread will have dequeued in the meantime
        if (!_timeouts.TryDequeue(out item)) return;
        if (item.Timeout > DateTime.UtcNow)
        {
            _timeouts.Enqueue(item);
            return;
        }

        if (_inFlight.Remove(item.Id, out var message))
            _queue.Enqueue(message);
    }
}