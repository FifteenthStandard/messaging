using Azure.Storage.Queues;
using System.Text.Json;

namespace FifteenthStandard.Messaging;

public class AzureQueue<T> : IQueue<T>
{
    private readonly QueueClient _client;

    private const string IdSeparator = "|||";

    public AzureQueue(
        string connectionString,
        string queue)
    {
        _client = new QueueClient(connectionString, queue);
    }

    public async Task SendAsync(T message)
    {
        var response = await _client.SendMessageAsync(
            JsonSerializer.Serialize(message));

        var rawResponse = response.GetRawResponse();

        if (rawResponse.IsError) throw new Exception($"Send failed: {rawResponse.ReasonPhrase}");
    }

    public async Task<IEnumerable<T>> SendBatchAsync(IEnumerable<T> messages)
    {
        var failed = new List<T>();

        foreach (var message in messages)
        {
            try
            {
                var response = await _client.SendMessageAsync(
                    JsonSerializer.Serialize(message));

                if (response.GetRawResponse().IsError) failed.Add(message);
            }
            catch
            {
                failed.Add(message);
            }
        }

        return failed;
    }

    public async Task<(string Id, T Message)> ReceiveAsync(
        int visibilityTimeoutSeconds, int waitTimeoutSeconds)
    {
        var visibilityTimeout = TimeSpan.FromSeconds(visibilityTimeoutSeconds);
        var waitTimeout = new CancellationTokenSource(waitTimeoutSeconds * 1_000);
        
        var response = (await _client.ReceiveMessageAsync(
            visibilityTimeout,
            waitTimeout.Token)).Value;

        var id = $"{response.MessageId}{IdSeparator}{response.PopReceipt}";
        var message = JsonSerializer.Deserialize<T>(response.Body)
            ?? throw new Exception("Invalid message");

        return (id, message);
    }

    public async Task<IEnumerable<(string Id, T Message)>> ReceiveBatchAsync(
        int count, int visibilityTimeoutSeconds, int waitTimeoutSeconds)
    {
        var visibilityTimeout = TimeSpan.FromSeconds(visibilityTimeoutSeconds);
        var waitTimeout = new CancellationTokenSource(waitTimeoutSeconds * 1_000);
        var response = (await _client.ReceiveMessagesAsync(
            count,
            visibilityTimeout,
            waitTimeout.Token)).Value;

        var messages = new List<(string Id, T Message)>();

        foreach (var entry in response)
        {
            var id = $"{entry.MessageId}{IdSeparator}{entry.PopReceipt}";
            var message = JsonSerializer.Deserialize<T>(entry.Body);
            if (message != null) messages.Add((id, message));
        }

        return messages;
    }

    public async Task RemoveAsync(string id)
    {
        var parts = id.Split(IdSeparator, 2);

        if (parts.Length != 2) throw new Exception("Invalid id");

        var messageId = parts[0];
        var popReceipt = parts[1];

        await _client.DeleteMessageAsync(messageId, popReceipt);
    }

    public async Task<IEnumerable<string>> RemoveBatchAsync(IEnumerable<string> ids)
    {
        var failed = new List<string>();

        foreach (var id in ids)
        {
            var parts = id.Split("|||", 2);

            if (parts.Length != 2) throw new Exception("Invalid id");

            var messageId = parts[0];
            var popReceipt = parts[1];

            try
            {
                var response = await _client.DeleteMessageAsync(messageId, popReceipt);
                if (response.IsError) failed.Add(id);
            }
            catch
            {
                failed.Add(id);
            }
        }

        return failed;
    }
}