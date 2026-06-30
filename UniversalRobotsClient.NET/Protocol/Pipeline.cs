// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace UniversalRobotics.Protocol
{
    /// <summary>
    /// Implements the producer/consumer pipeline from the C++ library.
    /// A background task runs the <see cref="IProducer{T}"/> in a loop, placing produced packages
    /// into a bounded queue. Registered <see cref="IConsumer{T}"/> instances drain the queue in
    /// a separate dispatch task.
    /// <para>
    /// The queue capacity defaults to 1, providing latest-value semantics for real-time data
    /// (older un-consumed packets are overwritten by newer ones).
    /// </para>
    /// </summary>
    /// <typeparam name="T">The package type flowing through this pipeline.</typeparam>
    public sealed class Pipeline<T> : IDisposable where T : class
    {
        private readonly IProducer<T> _producer;
        private readonly List<IConsumer<T>> _consumers = new List<IConsumer<T>>();
        private readonly ILogger _logger;
        private readonly int _queueCapacity;

        private BlockingCollection<T>? _queue;
        private CancellationTokenSource? _cts;
        private Task? _producerTask;
        private Task? _dispatchTask;
        private bool _disposed;

        /// <summary>
        /// Creates a new pipeline backed by the given producer.
        /// </summary>
        /// <param name="producer">The packet producer (reads from a socket).</param>
        /// <param name="queueCapacity">Maximum number of un-consumed packets buffered. Default 1 = latest-value only.</param>
        /// <param name="logger">Optional logger.</param>
        public Pipeline(IProducer<T> producer, int queueCapacity = 1, ILogger? logger = null)
        {
            _producer = producer ?? throw new ArgumentNullException(nameof(producer));
            _queueCapacity = queueCapacity;
            _logger = logger ?? NullLogger.Instance;
        }

        /// <summary>Registers a consumer to receive packages dispatched by this pipeline.</summary>
        public void AddConsumer(IConsumer<T> consumer)
        {
            lock (_consumers) _consumers.Add(consumer);
        }

        /// <summary>Removes a previously registered consumer.</summary>
        public bool RemoveConsumer(IConsumer<T> consumer)
        {
            lock (_consumers) return _consumers.Remove(consumer);
        }

        /// <summary>
        /// Starts the producer and dispatch background tasks.
        /// </summary>
        /// <param name="cancellationToken">Token that, when cancelled, stops the pipeline.</param>
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _queue = new BlockingCollection<T>(_queueCapacity);

            lock (_consumers)
                foreach (var c in _consumers) c.SetupConsumer();
            _producer.SetupProducer();

            _producerTask = Task.Run(() => ProducerLoopAsync(_cts.Token), _cts.Token);
            _dispatchTask = Task.Run(() => DispatchLoopAsync(_cts.Token), _cts.Token);

            return Task.CompletedTask;
        }

        /// <summary>Stops the pipeline and waits for both background tasks to finish.</summary>
        public async Task StopAsync()
        {
            _cts?.Cancel();
            _queue?.CompleteAdding();

            await AwaitTask(_producerTask).ConfigureAwait(false);
            await AwaitTask(_dispatchTask).ConfigureAwait(false);

            lock (_consumers)
                foreach (var c in _consumers) c.TeardownConsumer();
            _producer.TeardownProducer();

            _queue?.Dispose();
            _queue = null;
        }

        private async Task ProducerLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var (success, package) = await _producer.TryProduceAsync(ct).ConfigureAwait(false);
                    if (!success || package is null) continue;

                    if (_queue is null || _queue.IsAddingCompleted) break;

                    // For bounded capacity=1: drop oldest if full to keep latest-value semantics
                    if (_queue.Count >= _queueCapacity)
                    {
                        _queue.TryTake(out _);
                    }
                    _queue.Add(package, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Producer encountered an error.");
                    await Task.Delay(50, ct).ConfigureAwait(false);
                }
            }
        }

        private async Task DispatchLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                T? item;
                try
                {
                    item = _queue!.Take(ct);
                }
                catch (OperationCanceledException) { break; }
                catch (InvalidOperationException) { break; } // AddingCompleted

                IConsumer<T>[] snapshot;
                lock (_consumers) snapshot = _consumers.ToArray();

                foreach (var consumer in snapshot)
                {
                    try
                    {
                        await consumer.ConsumeAsync(item, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Consumer threw an exception.");
                    }
                }
            }
        }

        private static async Task AwaitTask(Task? task)
        {
            if (task == null) return;
            try { await task.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopAsync().GetAwaiter().GetResult();
            _cts?.Dispose();
        }
    }
}
