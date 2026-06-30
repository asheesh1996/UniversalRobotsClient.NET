// Licensed under the Apache License, Version 2.0

using System.Threading;
using System.Threading.Tasks;

namespace UniversalRobotics.Protocol
{
    /// <summary>
    /// Represents a parseable UR network package.
    /// </summary>
    /// <typeparam name="THeader">The header type that describes how to read the packet length prefix.</typeparam>
    public interface IUrPackage<THeader>
    {
        /// <summary>
        /// Populates this package's fields from the binary data in <paramref name="parser"/>.
        /// </summary>
        /// <param name="parser">Parser positioned at the start of the package payload (after the header).</param>
        /// <returns><see langword="true"/> if parsing succeeded; <see langword="false"/> otherwise.</returns>
        bool ParseFrom(BinaryParser parser);

        /// <summary>Returns a human-readable representation of this package for logging and diagnostics.</summary>
        string ToDebugString();
    }

    /// <summary>
    /// Receives and processes packages produced by an <see cref="IProducer{T}"/>.
    /// </summary>
    /// <typeparam name="T">The package type consumed.</typeparam>
    public interface IConsumer<T>
    {
        /// <summary>Called before the consumer begins receiving packages.</summary>
        void SetupConsumer();

        /// <summary>Called after the consumer has finished receiving packages.</summary>
        void TeardownConsumer();

        /// <summary>Processes a single package.</summary>
        /// <param name="package">The package to consume.</param>
        Task ConsumeAsync(T package, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Produces packages to be consumed by an <see cref="IConsumer{T}"/>.
    /// </summary>
    /// <typeparam name="T">The package type produced.</typeparam>
    public interface IProducer<T>
    {
        /// <summary>Called before the producer begins generating packages.</summary>
        void SetupProducer();

        /// <summary>Called after the producer has finished generating packages.</summary>
        void TeardownProducer();

        /// <summary>
        /// Attempts to produce the next package.
        /// </summary>
        /// <param name="package">On success, contains the produced package.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns><see langword="true"/> if a package was produced; <see langword="false"/> on transient failure (pipeline will retry).</returns>
        Task<(bool success, T? package)> TryProduceAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Parses a byte buffer containing a UR network packet into a typed package object.
    /// </summary>
    /// <typeparam name="THeader">The header type identifying the package format.</typeparam>
    /// <typeparam name="TPackage">The package type this parser produces.</typeparam>
    public interface IParser<THeader, TPackage>
    {
        /// <summary>
        /// Parses the given byte buffer into a package.
        /// </summary>
        /// <param name="buffer">Raw packet bytes (including header).</param>
        /// <param name="length">Number of valid bytes in <paramref name="buffer"/>.</param>
        /// <param name="package">On success, the parsed package.</param>
        /// <returns><see langword="true"/> if parsing succeeded.</returns>
        bool TryParse(byte[] buffer, int length, out TPackage? package);
    }

    /// <summary>
    /// Receives connection state change notifications from a pipeline component.
    /// </summary>
    public interface INotifier
    {
        /// <summary>Called when the underlying connection starts.</summary>
        void Started();

        /// <summary>Called when the underlying connection is lost or stopped.</summary>
        void Stopped();
    }

    /// <summary>
    /// A null (no-op) <see cref="INotifier"/> implementation used as default when no notifier is needed.
    /// </summary>
    public sealed class NullNotifier : INotifier
    {
        /// <summary>Gets the singleton instance of <see cref="NullNotifier"/>.</summary>
        public static readonly NullNotifier Instance = new NullNotifier();

        private NullNotifier() { }

        /// <inheritdoc />
        public void Started() { }

        /// <inheritdoc />
        public void Stopped() { }
    }
}
