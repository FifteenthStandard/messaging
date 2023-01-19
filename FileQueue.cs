using System.Text.Json;

namespace FifteenthStandard.Messaging;

public class FileQueue<T> : IQueue<T>
{
    private readonly string _root;
    private readonly string _inflight;
    private readonly object _lock = new object();

    public FileQueue(string root)
    {
        _root = root;
        _inflight = Path.Join(root, "inflight");
    }

    public async Task SendAsync(T message)
    {
        Directory.CreateDirectory(_root);

        var filename = NewFilename();
        var tmpFilename = $"{filename}.tmp";

        await File.WriteAllTextAsync(
            tmpFilename,
            JsonSerializer.Serialize(message));

        lock(_lock) File.Move(tmpFilename, filename);
    }

    public async Task<IEnumerable<T>> SendBatchAsync(IEnumerable<T> messages)
    {
        foreach (var message in messages)
        {
            await SendAsync(message);
            await Task.Delay(1);
        }

        return Enumerable.Empty<T>();
    }

    public async Task<(string Id, T Message)> ReceiveAsync(
        int visibilityTimeoutSeconds, int waitTimeoutSeconds)
    {
        FlushInflight();

        var waitTimeout = new CancellationTokenSource(waitTimeoutSeconds * 1_000);

        Directory.CreateDirectory(_inflight);

        while (!waitTimeout.IsCancellationRequested)
        {
            var files = Directory.GetFiles(_root, "*.json");
            if (!files.Any())
            {
                await Task.Delay(1000, waitTimeout.Token);
                continue;
            }

            var timeout = DateTime.UtcNow.AddSeconds(visibilityTimeoutSeconds).Ticks;
            var id = Guid.NewGuid().ToString();

            var newPath = Path.Join(
                _inflight,
                $"{timeout}-{id}.json");

            try
            {
                lock (_lock) File.Move(files[0], newPath);
                var text = await File.ReadAllTextAsync(newPath);
                var message = JsonSerializer.Deserialize<T>(text)
                    ?? throw new Exception("Invalid message");
                return (id, message);
            }
            catch (System.IO.FileNotFoundException)
            {
                await Task.Delay(1000, waitTimeout.Token);
                continue;
            }
        }

        throw new OperationCanceledException("Wait timeout exceeded");
    }

    public async Task<IEnumerable<(string Id, T Message)>> ReceiveBatchAsync(
        int count, int visibilityTimeoutSeconds, int waitTimeoutSeconds)
    {
        var waitTimeout = new CancellationTokenSource(waitTimeoutSeconds * 1_000);

        var messages = new List<(string Id, T message)>();

        while (!waitTimeout.IsCancellationRequested && messages.Count < count)
        {
            FlushInflight();

            var files = Directory.GetFiles(_root, "*.json");
            if (!files.Any())
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

            var timeout = DateTime.UtcNow.AddSeconds(visibilityTimeoutSeconds).Ticks;
            var id = Guid.NewGuid().ToString();

            var newPath = Path.Join(
                _inflight,
                $"{timeout}-{id}.json");

            try
            {
                lock (_lock) File.Move(files[0], newPath);
                var text = await File.ReadAllTextAsync(newPath);
                var message = JsonSerializer.Deserialize<T>(text)
                    ?? throw new Exception("Invalid message");
                messages.Add((id, message));
            }
            catch (System.IO.FileNotFoundException)
            {
                await Task.Delay(1000, waitTimeout.Token);
                continue;
            }
        }

        return messages;
    }

    public Task RemoveAsync(string id)
    {
        Directory.CreateDirectory(_inflight);

        var files = Directory.GetFiles(_inflight, $"*-{id}.json");

        if (!files.Any()) return Task.CompletedTask;

        if (files.Length == 1) File.Delete(files[0]);

        return Task.CompletedTask;
    }

    public Task<IEnumerable<string>> RemoveBatchAsync(IEnumerable<string> ids)
    {
        Directory.CreateDirectory(_inflight);

        var failed = new List<string>();

        var files = Directory.GetFiles(_inflight);

        foreach (var id in ids)
        {
            var matches = files.Where(file => file.Contains(id)).ToArray();
            if (matches.Length == 1)
            {
                File.Delete(matches[0]);
            }
            else
            {
                failed.Add(id);
            }
        }

        return Task.FromResult((IEnumerable<string>)failed);
    }

    private string NewFilename()
        => Path.Join(
            _root,
            $"{DateTime.UtcNow.Ticks}.json");

    private void FlushInflight()
    {
        Directory.CreateDirectory(_inflight);

        var files = Directory.GetFiles(_inflight);

        if (!files.Any()) return;

        var path = files[0];

        var parts = Path.GetFileNameWithoutExtension(path).Split('-', 2);
        var timeoutTicks = long.Parse(parts[0]);
        var timeout = DateTime.MinValue + TimeSpan.FromTicks(timeoutTicks);

        if (timeout > DateTime.UtcNow) return;

        try
        {
            lock (_lock) File.Move(path, NewFilename());
        }
        catch (System.IO.FileNotFoundException)
        {
        }
    }
}