// Licensed under the Apache License, Version 2.0

using UniversalRobotics.Protocol;

namespace UniversalRobotics.Rtde.Packets
{
    /// <summary>
    /// Parses raw RTDE wire bytes into the correct <see cref="RtdePackage"/> subtype.
    /// Implements <see cref="IParser{THeader, TPackage}"/> for the RTDE protocol.
    /// </summary>
    public sealed class RtdeParser : IParser<RtdePackageHeader, RtdePackage>
    {
        private readonly System.Collections.Generic.IReadOnlyList<string> _outputRecipe;
        private DataPackage? _preallocatedDataPackage;

        /// <summary>
        /// Creates a new parser for the given output recipe.
        /// </summary>
        /// <param name="outputRecipe">The recipe used to construct <see cref="DataPackage"/> instances.</param>
        public RtdeParser(System.Collections.Generic.IReadOnlyList<string> outputRecipe)
        {
            _outputRecipe = outputRecipe;
        }

        /// <summary>Updates the field type metadata after the setup handshake is complete.</summary>
        internal void SetFieldTypes(System.Collections.Generic.IReadOnlyList<string> fieldTypes)
        {
            _preallocatedDataPackage ??= new DataPackage(_outputRecipe);
            _preallocatedDataPackage.SetFieldTypes(fieldTypes);
        }

        /// <inheritdoc />
        public bool TryParse(byte[] buffer, int length, out RtdePackage? package)
        {
            if (length < 3) { package = null; return false; }

            var header = RtdePackageHeader.FromBytes(buffer);
            // Parser receives the full packet; skip the 3-byte header to get the payload
            var parser = new BinaryParser(buffer, 3, length - 3);

            package = header.Type switch
            {
                RtdePackageType.RequestProtocolVersion => ParseAndFill(new RequestProtocolVersionPackage(), parser),
                RtdePackageType.GetUrControlVersion => ParseAndFill(new GetUrControlVersionPackage(), parser),
                RtdePackageType.ControlPackageSetupOutputs => ParseAndFill(new SetupOutputsPackage(System.Array.Empty<string>()), parser),
                RtdePackageType.ControlPackageSetupInputs => ParseAndFill(new SetupInputsPackage(System.Array.Empty<string>()), parser),
                RtdePackageType.ControlPackageStart => ParseAndFill(new ControlStartPackage(), parser),
                RtdePackageType.ControlPackagePause => ParseAndFill(new ControlPausePackage(), parser),
                RtdePackageType.TextMessage => ParseAndFill(new RtdeTextMessage(), parser),
                RtdePackageType.DataPackage => ParseDataPackage(parser),
                _ => null
            };

            return package != null;
        }

        private static T? ParseAndFill<T>(T pkg, BinaryParser parser) where T : RtdePackage
        {
            return pkg.ParseFrom(parser) ? pkg : null;
        }

        private DataPackage? ParseDataPackage(BinaryParser parser)
        {
            var pkg = _preallocatedDataPackage ?? new DataPackage(_outputRecipe);
            return pkg.ParseFrom(parser) ? pkg : null;
        }
    }
}
