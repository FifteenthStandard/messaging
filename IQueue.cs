namespace FifteenthStandard.Messaging;

public interface IQueue<T>
{
    Task SendAsync(T message);
    Task<IEnumerable<T>> SendBatchAsync(IEnumerable<T> messages);
    Task<(string Id, T Message)> ReceiveAsync(int visibilityTimeoutSeconds, int waitTimeoutSeconds);
    Task<IEnumerable<(string Id, T Message)>> ReceiveBatchAsync(
        int count, int visibilityTimeoutSeconds, int waitTimeoutSeconds);
    Task RemoveAsync(string id);
    Task<IEnumerable<string>> RemoveBatchAsync(IEnumerable<string> ids);
}
