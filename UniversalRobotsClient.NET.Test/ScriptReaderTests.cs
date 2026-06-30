// Tests for ScriptReader template engine

using System.Collections.Generic;
using System.IO;
using UniversalRobotics.Control;
using UniversalRobotics.Models;

namespace UniversalRobotics.Tests
{
    public class ScriptReaderTests
    {
        private readonly ScriptReader _reader = new ScriptReader();

        [Fact]
        public void ProcessContent_SimpleVariable_IsSubstituted()
        {
            var content = "port = {{REVERSE_PORT}}";
            var vars = new Dictionary<string, object> { ["REVERSE_PORT"] = 50001 };
            var result = _reader.ProcessContent(content, vars);
            Assert.Contains("port = 50001", result);
        }

        [Fact]
        public void ProcessContent_MissingVariable_KeepsPlaceholder()
        {
            var content = "host = {{HOST_IP}}";
            var vars = new Dictionary<string, object>();
            var result = _reader.ProcessContent(content, vars);
            Assert.Contains("{{HOST_IP}}", result);
        }

        [Fact]
        public void ProcessContent_BoolVariable_FormattedAsTrue()
        {
            var content = "flag = {{ENABLED}}";
            var vars = new Dictionary<string, object> { ["ENABLED"] = true };
            var result = _reader.ProcessContent(content, vars);
            Assert.Contains("flag = True", result);
        }

        [Fact]
        public void ProcessContent_VersionCondition_TrueBlock_Included()
        {
            var content =
                "{% if SOFTWARE_VERSION >= v5.23.0 %}\n" +
                "torque_enabled = True\n" +
                "{% endif %}\n";
            var vars = new Dictionary<string, object>
            {
                ["SOFTWARE_VERSION"] = VersionInformation.Parse("5.23.0")
            };
            var result = _reader.ProcessContent(content, vars);
            Assert.Contains("torque_enabled = True", result);
        }

        [Fact]
        public void ProcessContent_VersionCondition_FalseBlock_Excluded()
        {
            var content =
                "{% if SOFTWARE_VERSION >= v5.23.0 %}\n" +
                "torque_enabled = True\n" +
                "{% endif %}\n";
            var vars = new Dictionary<string, object>
            {
                ["SOFTWARE_VERSION"] = VersionInformation.Parse("5.21.0")
            };
            var result = _reader.ProcessContent(content, vars);
            Assert.DoesNotContain("torque_enabled = True", result);
        }

        [Fact]
        public void ProcessContent_MultipleVariables_AllSubstituted()
        {
            var content = "{{A}} {{B}} {{C}}";
            var vars = new Dictionary<string, object> { ["A"] = "x", ["B"] = "y", ["C"] = "z" };
            var result = _reader.ProcessContent(content, vars);
            Assert.Equal("x y z\n", result);
        }

        [Fact]
        public void ProcessContent_DoubleVariable_FormattedInvariant()
        {
            var content = "gain = {{GAIN}}";
            var vars = new Dictionary<string, object> { ["GAIN"] = 3.14 };
            var result = _reader.ProcessContent(content, vars);
            Assert.Contains("gain = 3.14", result);
        }
    }
}
