// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UniversalRobotics.Protocol;
using UniversalRobotics.Rtde.Packets;

namespace UniversalRobotics.Rtde
{
    /// <summary>
    /// Sends data to the robot over the RTDE interface.
    /// A background task flushes the latest queued package at high frequency.
    /// Replaces the C++ <c>RTDEWriter</c> class.
    /// </summary>
    public sealed class RtdeWriter : IDisposable
    {
        private readonly UrStream<RtdePackage> _stream;
        private IReadOnlyList<string> _recipe;
        private DataPackage? _inputPackage;
        private byte _recipeId;
        private readonly ILogger _logger;

        private volatile DataPackage? _pendingPackage;
        private readonly SemaphoreSlim _writeSignal = new SemaphoreSlim(0, 1);
        private CancellationTokenSource? _cts;
        private Task? _writerTask;

        /// <summary>
        /// Creates a new writer for the given RTDE stream and input recipe.
        /// </summary>
        public RtdeWriter(UrStream<RtdePackage> stream, IReadOnlyList<string> inputRecipe, ILogger? logger = null)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _recipe = inputRecipe ?? Array.Empty<string>();
            _logger = logger ?? NullLogger.Instance;
        }

        /// <summary>Sets a new input recipe (callable before <see cref="StartAsync"/>).</summary>
        public void SetInputRecipe(IReadOnlyList<string> recipe)
        {
            _recipe = recipe;
            _inputPackage = _recipe.Count > 0 ? new DataPackage(_recipe) : null;
        }

        /// <summary>
        /// Starts the background writer task with the given recipe ID returned by the setup handshake.
        /// </summary>
        public Task StartAsync(byte recipeId, CancellationToken cancellationToken = default)
        {
            _recipeId = recipeId;
            if (_recipe.Count > 0)
                _inputPackage = new DataPackage(_recipe);

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _writerTask = Task.Run(() => WriterLoopAsync(_cts.Token), _cts.Token);
            return Task.CompletedTask;
        }

        /// <summary>Stops the writer task.</summary>
        public async Task StopAsync()
        {
            _cts?.Cancel();
            _writeSignal.Release(); // unblock waiting task
            if (_writerTask != null)
                try { await _writerTask.ConfigureAwait(false); } catch { }
        }

        // ─── High-level send helpers (mirror RTDEWriter C++ API) ─────────────────

        /// <summary>Queues a speed-slider fraction [0, 1] for transmission.</summary>
        public bool SendSpeedSlider(double fraction)
            => SendInputDouble("speed_slider_fraction", fraction)
               && SendInputUInt32("speed_slider_mask", 1);

        /// <summary>Queues a standard digital output mask and value.</summary>
        public bool SendStandardDigitalOutput(byte outputMask, byte outputValues)
            => SendInputUInt8("standard_digital_output_mask", outputMask)
               && SendInputUInt8("standard_digital_output", outputValues);

        /// <summary>Queues a configurable digital output mask and value.</summary>
        public bool SendConfigurableDigitalOutput(byte outputMask, byte outputValues)
            => SendInputUInt8("configurable_digital_output_mask", outputMask)
               && SendInputUInt8("configurable_digital_output", outputValues);

        /// <summary>Queues a standard analog output channel value.</summary>
        public bool SendStandardAnalogOutput(byte channel, double value)
        {
            string maskKey = $"standard_analog_output_mask";
            string typeKey = $"standard_analog_output_type";
            string dataKey = $"standard_analog_output_{channel}";
            byte mask = (byte)(1 << channel);
            return SendInputUInt8(maskKey, mask)
                   && SendInputUInt8(typeKey, 0)
                   && SendInputDouble(dataKey, value);
        }

        /// <summary>Queues a target force mode wrench for transmission.</summary>
        public bool SendForceModeTarget(double[] wrench)
            => SendInputVector6d("force_mode_target_wrench", wrench);

        // ─── Low-level typed setters ──────────────────────────────────────────────

        /// <summary>Sets a boolean input field.</summary>
        public bool SendInputBool(string fieldName, bool value)
        {
            if (_inputPackage == null) return false;
            var result = _inputPackage.SetData(fieldName, value);
            if (result) SignalWrite();
            return result;
        }

        /// <summary>Sets a uint8 input field.</summary>
        public bool SendInputUInt8(string fieldName, byte value)
        {
            if (_inputPackage == null) return false;
            var result = _inputPackage.SetData(fieldName, value);
            if (result) SignalWrite();
            return result;
        }

        /// <summary>Sets a uint32 input field.</summary>
        public bool SendInputUInt32(string fieldName, uint value)
        {
            if (_inputPackage == null) return false;
            var result = _inputPackage.SetData(fieldName, value);
            if (result) SignalWrite();
            return result;
        }

        /// <summary>Sets a double input field.</summary>
        public bool SendInputDouble(string fieldName, double value)
        {
            if (_inputPackage == null) return false;
            var result = _inputPackage.SetData(fieldName, value);
            if (result) SignalWrite();
            return result;
        }

        /// <summary>Sets a vector6d input field.</summary>
        public bool SendInputVector6d(string fieldName, double[] values)
        {
            if (_inputPackage == null) return false;
            var result = _inputPackage.SetData(fieldName, values);
            if (result) SignalWrite();
            return result;
        }

        /// <summary>Immediately sends the provided data package to the robot.</summary>
        public async Task<bool> SendPackageAsync(DataPackage package, CancellationToken cancellationToken = default)
        {
            try
            {
                var bytes = package.Serialize();
                await _stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send data package.");
                return false;
            }
        }

        private void SignalWrite()
        {
            Interlocked.Exchange(ref _pendingPackage, _inputPackage);
            if (_writeSignal.CurrentCount == 0)
                _writeSignal.Release();
        }

        private async Task WriterLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await _writeSignal.WaitAsync(ct).ConfigureAwait(false);
                    var pkg = Interlocked.Exchange(ref _pendingPackage, null);
                    if (pkg == null) continue;

                    await SendPackageAsync(pkg, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Writer loop error.");
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
            _writeSignal.Dispose();
            _cts?.Dispose();
        }
    }
}
