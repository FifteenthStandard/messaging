# FifteenthStandard.Messaging

A library with several implementations of a generic queue client.

## `IQueue<T>`

A generic first-in-first-out queue.

---

### `SendAsync`

Send a message to the queue.

#### Parameters

Parameter   | Description
------------|------------
`T message` | The message to send to the queue.

#### Returns `Task`

A task which resolves once the message has been sent.

---

### `SendBatchAsync`

Send a batch of messages to the queue.

#### Parameters

Parameter                 | Description
--------------------------|------------
`IEnumerable<T> messages` | The messages to send to the queue.

#### Returns `Task<IEnumerable<T>>`

A task which resolves to an enumerable of messages which failed to send.

---

### `ReceiveAsync`

Receive a message from the queue. When a message is received, it is temporarily
hidden from other consumers so that it can be processed by the receiver. The
receiver must then remove the message from the queue when processing is
finished, otherwise the message will be automatically returned to the queue.

#### Parameters

Parameter                     | Description
------------------------------|------------
`int visibilityTimoutSeconds` | The time (in seconds) to hide messages while processing
`int waitTimeoutSeconds`      | The time (in seconds) to wait for a message

#### Returns `Task<(string Id, T Message)>`

A task which resolves to a pair containing an ID and a message. The ID can be
used to remove the message from the queue when processing is completed.

---

### `ReceiveBatchAsync`

Receive a batch of messages from the queue. If not enough messages are
available immediately, some implementations may immediately return with what is
available--they will not necessarily wait for the batch to fill up.

#### Parameters

Parameter                     | Description
------------------------------|------------
`int count`                   | The maximum number of messages to receive
`int visibilityTimoutSeconds` | The time (in seconds) to hide messages while processing
`int waitTimeoutSeconds`      | The time (in seconds) to wait for a message

#### Returns `Task<IEnumerable<(string Id, T Message)>>`

A task which resolves to an enumerable of pairs containing an ID and a message.
The IDs can be used to remove messages from the queue when processing is
completed.

---

### RemoveAsync

Remove a message from the queue, when processing is complete. If messages are
not removed, they will eventually be returned to the queue for other consumers
to receive.

#### Parameters

Parameter   | Description
------------|------------
`string id` | The ID of the message to remove

#### Returns `Task`

A task which resolves once the message has been removed.

---

### RemoveBatchAsync

Remove a batch of messages from the queue, when processing is complete. If
messages are not removed, they will eventually be returned to the queue for
other consumers to receive.

#### Parameters

Parameter                 | Description
--------------------------|------------
`IEnumerable<string> ids` | The IDs of the messages to remove

#### Returns `Task<IEnumerable<string>>`

A task which resolves to an enumerable of IDs of messages which failed to be
removed.

---

### Implementations

#### `InMemoryQueue<T>`

A fully in-memory queue. Contents are wiped when the application exits. Only
suitable for testing.

#### `FileQueue<T>`

Messages are persisted to the local file system. Suitable for local application
development.

#### `AwsQueue<T>`

Messages are persisted to [Amazon SQS][amazon-sqs]. Suitable for production
applications deployed to AWS.

#### `AzureQueue<T>`

Messages are persisted to [Azure Queue Storage][azure-queue-storage]. Suitable
for production applications deployed to Azure.

[amazon-sqs]: https://aws.amazon.com/sqs/
[azure-queue-storage]: https://azure.microsoft.com/en-us/products/storage/queues/