// Licensed under the Apache License, Version 2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UniversalRobotics.Models;
using UniversalRobotics.Protocol;
using UniversalRobotics.Transport;

namespace UniversalRobotics.Control
{
    /// <summary>
    /// Sends script commands to the robot for immediate execution in the external control script's
    /// dedicated command thread. Commands are encoded as 28 × int32 big-endian messages.
    /// Replaces the C++ <c>ScriptCommandInterface</c> class.
    /// </summary>
    public sealed class ScriptCommandInterface : ReverseInterface
    {
        private const int CommandMessageSize = 28 * 4; // 28 int32 fields × 4 bytes

        private volatile TaskCompletionSource<ToolContactResult>? _toolContactTcs;

        /// <summary>Raised when the robot reports a tool contact result.</summary>
        public event EventHandler<ToolContactResult>? ToolContactResultReceived;

        /// <summary>
        /// Creates a script command interface using the specified configuration.
        /// </summary>
        public ScriptCommandInterface(ReverseInterfaceConfig config, ILogger? logger = null)
            : base(config, logger) { }

        // ─── Command implementations ──────────────────────────────────────────────

        /// <summary>Commands the robot to zero its force/torque sensor.</summary>
        public Task<bool> ZeroFtSensorAsync(CancellationToken ct = default)
            => SendCommandAsync(0, ct);

        /// <summary>Sets the active payload mass and center of gravity.</summary>
        public async Task<bool> SetPayloadAsync(double mass, double[] cog, CancellationToken ct = default)
        {
            var payload = new int[28];
            payload[0] = 1; // command id: setPayload
            payload[1] = (int)(mass * ReverseInterface.MultJointState);
            if (cog.Length >= 3)
            {
                payload[2] = (int)(cog[0] * ReverseInterface.MultJointState);
                payload[3] = (int)(cog[1] * ReverseInterface.MultJointState);
                payload[4] = (int)(cog[2] * ReverseInterface.MultJointState);
            }
            return await SendRawCommandAsync(payload, ct).ConfigureAwait(false);
        }

        /// <summary>Sets the tool output voltage (0, 12, or 24 V).</summary>
        public async Task<bool> SetToolVoltageAsync(int voltage, CancellationToken ct = default)
        {
            var payload = new int[28];
            payload[0] = 2; // command id: setToolVoltage
            payload[1] = voltage;
            return await SendRawCommandAsync(payload, ct).ConfigureAwait(false);
        }

        /// <summary>Starts force mode with the given task frame and selection vector.</summary>
        public async Task<bool> StartForceModeAsync(
            double[] taskFrame, int[] selectionVector, double[] wrench,
            int forceType, double[] limits, CancellationToken ct = default)
        {
            var payload = new int[28];
            payload[0] = 3; // command id: startForceMode
            int i = 1;
            foreach (var v in taskFrame)  payload[i++] = (int)(v * ReverseInterface.MultJointState);
            foreach (var v in selectionVector) payload[i++] = v;
            foreach (var v in wrench) payload[i++] = (int)(v * ReverseInterface.MultJointState);
            payload[i++] = forceType;
            foreach (var v in limits) { if (i < 28) payload[i++] = (int)(v * ReverseInterface.MultJointState); }
            return await SendRawCommandAsync(payload, ct).ConfigureAwait(false);
        }

        /// <summary>Ends force mode.</summary>
        public Task<bool> EndForceModeAsync(CancellationToken ct = default)
            => SendCommandAsync(4, ct);

        /// <summary>Starts tool contact monitoring.</summary>
        public Task<bool> StartToolContactAsync(CancellationToken ct = default)
        {
            _toolContactTcs = new TaskCompletionSource<ToolContactResult>();
            return SendCommandAsync(5, ct);
        }

        /// <summary>Ends tool contact monitoring.</summary>
        public Task<bool> EndToolContactAsync(CancellationToken ct = default)
        {
            _toolContactTcs?.TrySetCanceled();
            _toolContactTcs = null;
            return SendCommandAsync(6, ct);
        }

        /// <summary>Enables or disables friction compensation for direct torque control.</summary>
        public async Task<bool> SetFrictionCompensationAsync(bool enabled, CancellationToken ct = default)
        {
            var payload = new int[28];
            payload[0] = 7;
            payload[1] = enabled ? 1 : 0;
            return await SendRawCommandAsync(payload, ct).ConfigureAwait(false);
        }

        /// <summary>Enables or disables force/torque RTDE input processing.</summary>
        public async Task<bool> FtRtdeInputEnableAsync(bool enabled, CancellationToken ct = default)
        {
            var payload = new int[28];
            payload[0] = 8;
            payload[1] = enabled ? 1 : 0;
            return await SendRawCommandAsync(payload, ct).ConfigureAwait(false);
        }

        /// <summary>Sets the gravity vector used by the robot controller.</summary>
        public async Task<bool> SetGravityAsync(double[] gravity, CancellationToken ct = default)
        {
            var payload = new int[28];
            payload[0] = 9;
            for (int i = 0; i < Math.Min(3, gravity.Length); i++)
                payload[i + 1] = (int)(gravity[i] * ReverseInterface.MultJointState);
            return await SendRawCommandAsync(payload, ct).ConfigureAwait(false);
        }

        /// <summary>Sets the TCP offset (tool frame transform).</summary>
        public async Task<bool> SetTcpOffsetAsync(double[] offset, CancellationToken ct = default)
        {
            var payload = new int[28];
            payload[0] = 10;
            for (int i = 0; i < Math.Min(6, offset.Length); i++)
                payload[i + 1] = (int)(offset[i] * ReverseInterface.MultJointState);
            return await SendRawCommandAsync(payload, ct).ConfigureAwait(false);
        }

        // ─── Message handling ─────────────────────────────────────────────────────

        protected override void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
        {
            // Robot sends back a single int32 for tool contact result
            if (e.Length >= 4)
            {
                var parser = new BinaryParser(e.Data, 0, 4);
                var result = (ToolContactResult)parser.ReadInt32();
                ToolContactResultReceived?.Invoke(this, result);
                _toolContactTcs?.TrySetResult(result);
                _toolContactTcs = null;
            }
        }

        // ─── Low-level helpers ────────────────────────────────────────────────────

        private async Task<bool> SendCommandAsync(int commandId, CancellationToken ct)
        {
            var payload = new int[28];
            payload[0] = commandId;
            return await SendRawCommandAsync(payload, ct).ConfigureAwait(false);
        }

        private async Task<bool> SendRawCommandAsync(int[] fields, CancellationToken ct)
        {
            var buf = new byte[CommandMessageSize];
            int offset = 0;
            foreach (var f in fields) PackageSerializer.WriteInt32(buf, ref offset, f);
            return await SendToRobotAsync(buf, ct).ConfigureAwait(false);
        }
    }
}
