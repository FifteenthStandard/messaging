using Amazon.SQS;
using Amazon.SQS.Model;
using System.Text.Json;

namespace FifteenthStandard.Messaging;

public class AwsQueue<T> : IQueue<T>
{
    private readonly string _queue;
    private readonly IAmazonSQS _client;
    private string? _queueUrl;

    public AwsQueue(
        string queue,
        string region)
    {
        _queue = queue;
        _client = new AmazonSQSClient(Amazon.RegionEndpoint.GetBySystemName(region));
    }

    private async Task<string> GetQueueUrlAsync()
        => _queueUrl
            ?? (_queueUrl = (await _client.GetQueueUrlAsync(_queue)).QueueUrl);

    public async Task SendAsync(T message)
        => await _client.SendMessageAsync(
            await GetQueueUrlAsync(),
            JsonSerializer.Serialize(message));

    public async Task<IEnumerable<T>> SendBatchAsync(IEnumerable<T> messages)
    {
        var messageArray = messages.ToArray();

        var batch = messageArray
            .Select((message, index) =>
                new SendMessageBatchRequestEntry(
                    index.ToString(),
                    JsonSerializer.Serialize(message)))
            .ToList();

        var response = await _client.SendMessageBatchAsync(
            await GetQueueUrlAsync(),
            batch);

        return response
            .Failed
            .Select(entry => messageArray[int.Parse(entry.Id)]);
    }

    public async Task<(string Id, T Message)> ReceiveAsync(
        int visibilityTimeoutSeconds, int waitTimeoutSeconds)
    {
        var response = await _client.ReceiveMessageAsync(
            new ReceiveMessageRequest
            {
                QueueUrl = await GetQueueUrlAsync(),
                MaxNumberOfMessages = 1,
                VisibilityTimeout = visibilityTimeoutSeconds,
                WaitTimeSeconds = waitTimeoutSeconds,
            });
        
        if (response.Messages.Count != 1)
        {
            throw new Exception("Failed to receive message");
        }

        var entry = response.Messages[0];

        var message = JsonSerializer.Deserialize<T>(entry.Body)
            ?? throw new Exception("Invalid message");

        return (entry.ReceiptHandle, message);
    }

    public async Task<IEnumerable<(string Id, T Message)>> ReceiveBatchAsync(
        int count, int visibilityTimeoutSeconds, int waitTimeoutSeconds)
    {
        var response = await _client.ReceiveMessageAsync(
            new ReceiveMessageRequest
            {
                QueueUrl = await GetQueueUrlAsync(),
                MaxNumberOfMessages = count,
                VisibilityTimeout = visibilityTimeoutSeconds,
                WaitTimeSeconds = waitTimeoutSeconds,
            });

        var messages = new List<(string Id, T Message)>();

        foreach (var entry in response.Messages)
        {
            var message = JsonSerializer.Deserialize<T>(entry.Body);
            if (message != null) messages.Add((entry.ReceiptHandle, message));
        }

        return messages;
    }

    public async Task RemoveAsync(string id)
        => await _client.DeleteMessageAsync(
            await GetQueueUrlAsync(),
            id);

    public async Task<IEnumerable<string>> RemoveBatchAsync(IEnumerable<string> ids)
    {
        var idArray = ids.ToArray();

        var batch = idArray
            .Select((id, index) =>
                new DeleteMessageBatchRequestEntry(
                    index.ToString(),
                    id))
            .ToList();

        var response = await _client.DeleteMessageBatchAsync(
            await GetQueueUrlAsync(),
            batch);
        
        return response
            .Failed
            .Select(entry => idArray[int.Parse(entry.Id)]);
    }
}