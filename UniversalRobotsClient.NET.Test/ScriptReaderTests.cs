// Tests for ScriptReader template engine

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UniversalRobotics.Control;
using UniversalRobotics.Exceptions;
using UniversalRobotics.Models;

namespace UniversalRobotics.Tests
{
    public class ScriptReaderTests
    {
        private readonly ScriptReader _reader = new ScriptReader();

        [Theory]
        [InlineData("5.25.2", "legacy_servoj")]
        [InlineData("10.14.0", "modern_servoj")]
        public void ProcessContent_StrictVariables_NestedIfElifElseSelectsOnlyActiveBranch(string version, string expected)
        {
            _reader.StrictVariables = true;
            var content =
                "{% if ROBOT_SOFTWARE_VERSION < v6.0.0 %}\n" +
                "  {% if FEATURE == True %}\nlegacy_servoj\n  {% elif FEATURE == False %}\nlegacy_fallback\n  {% else %}\nlegacy_else\n  {% endif %}\n" +
                "{% elif ROBOT_SOFTWARE_VERSION >= v10.11.0 %}\nmodern_servoj\n{% else %}\nunsupported\n{% endif %}";

            var result = _reader.ProcessContent(content, new Dictionary<string, object>
            {
                ["ROBOT_SOFTWARE_VERSION"] = VersionInformation.Parse(version),
                ["FEATURE"] = true
            });

            Assert.Contains(expected, result);
            Assert.DoesNotContain("legacy_fallback", result);
            Assert.DoesNotContain("legacy_else", result);
            Assert.DoesNotContain("unsupported", result);
        }

        [Theory]
        [InlineData("5.25.2", "servoj(q, t=steptime, lookahead_time=0.1, gain=600)")]
        [InlineData("10.14.0", "servoj(q, t=steptime, lookahead_time=0.1, gain=600)")]
        public void ReadScript_PackagedExternalControl_RendersWithoutTemplateTokens(string version, string expectedLine)
        {
            _reader.StrictVariables = true;
            var scriptPath = FindExternalControlScript();
            var result = _reader.ReadScript(scriptPath, new Dictionary<string, object>
            {
                ["BEGIN_REPLACE"] = "begin",
                ["JOINT_STATE_REPLACE"] = 1,
                ["TIME_REPLACE"] = 0.002,
                ["VEL_ACC_REPLACE"] = 1,
                ["SERVO_J_REPLACE"] = "lookahead_time=0.1, gain=600",
                ["ROBOT_SOFTWARE_VERSION"] = VersionInformation.Parse(version),
                ["SERVER_IP_REPLACE"] = "127.0.0.1",
                ["TRAJECTORY_SERVER_PORT_REPLACE"] = 50002,
                ["SCRIPT_COMMAND_SERVER_PORT_REPLACE"] = 50003,
                ["SERVER_PORT_REPLACE"] = 50001
            });

            Assert.Contains(expectedLine, result);
            Assert.DoesNotContain("{{", result);
            Assert.DoesNotContain("{%", result);
            Assert.DoesNotContain("%}", result);
            Assert.Equal(9, new[]
            {
                "BEGIN_REPLACE", "JOINT_STATE_REPLACE", "TIME_REPLACE", "VEL_ACC_REPLACE",
                "SERVO_J_REPLACE", "ROBOT_SOFTWARE_VERSION", "SERVER_IP_REPLACE",
                "TRAJECTORY_SERVER_PORT_REPLACE", "SCRIPT_COMMAND_SERVER_PORT_REPLACE", "SERVER_PORT_REPLACE"
            }.Count(_reader.IsVariableRegistered));
        }

        private static string FindExternalControlScript()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            {
                var candidates = new[]
                {
                    Path.Combine(directory.FullName, "Resources", "external_control.urscript"),
                    Path.Combine(directory.FullName, "UniversalRobotsClient.NET", "Resources", "external_control.urscript")
                };
                var match = candidates.FirstOrDefault(File.Exists);
                if (match != null) return match;
            }
            throw new FileNotFoundException("Could not locate Resources/external_control.urscript from the test output directory.");
        }

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

        [Theory]
        [InlineData("../outside.urscript")]
        [InlineData("../scripts-sibling/outside.urscript")]
        [InlineData("nested/../../outside.urscript")]
        public void ReadScript_IncludeOutsideRoot_ThrowsUrException(string includePath)
        {
            using var files = new ScriptFiles();
            files.WriteScript(includePath, "{{OUTSIDE}}");
            var main = files.WriteScript("main.urscript", "{% include '" + includePath + "' %}");

            var exception = Assert.Throws<UrException>(() => _reader.ReadScript(main, new Dictionary<string, object>()));

            Assert.Equal("Include is outside the script root: " + includePath, exception.Message);
            Assert.False(_reader.IsVariableRegistered("OUTSIDE"));
        }

        [Fact]
        public void ProcessContent_IncludeOutsideBaseDirectory_ThrowsUrException()
        {
            using var files = new ScriptFiles();
            files.WriteScript("../outside.urscript", "{{OUTSIDE}}");

            var exception = Assert.Throws<UrException>(() => _reader.ProcessContent(
                "{% include '../outside.urscript' %}", new Dictionary<string, object>(), files.RootPath));

            Assert.Equal("Include is outside the script root: ../outside.urscript", exception.Message);
            Assert.False(_reader.IsVariableRegistered("OUTSIDE"));
        }

        [Theory]
        [InlineData("/outside.urscript")]
        [InlineData("C:\\outside.urscript")]
        [InlineData("C:outside.urscript")]
        [InlineData("\\outside.urscript")]
        [InlineData("\\\\server\\share\\outside.urscript")]
        public void ReadScript_RootedInclude_ThrowsUrException(string includePath)
        {
            using var files = new ScriptFiles();
            var main = files.WriteScript("main.urscript", "{% include '" + includePath + "' %}");

            var exception = Assert.Throws<UrException>(() => _reader.ReadScript(main, new Dictionary<string, object>()));

            Assert.Equal("Rooted include paths are not allowed: " + includePath, exception.Message);
        }

        [Fact]
        public void ReadScript_AbsoluteIncludeInsideRoot_ThrowsUrException()
        {
            using var files = new ScriptFiles();
            var includePath = files.WriteScript("inside.urscript", "{{INSIDE}}");
            var main = files.WriteScript("main.urscript", "{% include '" + includePath + "' %}");

            var exception = Assert.Throws<UrException>(() => _reader.ReadScript(main, new Dictionary<string, object>()));

            Assert.Equal("Rooted include paths are not allowed: " + includePath, exception.Message);
            Assert.False(_reader.IsVariableRegistered("INSIDE"));
        }

        [Fact]
        public void ReadScript_MissingInclude_ThrowsFileNotFoundException()
        {
            using var files = new ScriptFiles();
            var main = files.WriteScript("main.urscript", "{% include 'missing.urscript' %}");

            var exception = Assert.Throws<FileNotFoundException>(() => _reader.ReadScript(main, new Dictionary<string, object>()));

            Assert.Equal(Path.Combine(files.RootPath, "missing.urscript"), exception.FileName);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReadScript_CircularInclude_ThrowsUrException(bool indirect)
        {
            using var files = new ScriptFiles();
            var main = files.WriteScript("main.urscript", indirect
                ? "{% include 'nested/child.urscript' %}"
                : "{% include './main.urscript' %}");
            files.WriteScript("nested/child.urscript", "{% include '../main.urscript' %}");

            var exception = Assert.Throws<UrException>(() => _reader.ReadScript(main, new Dictionary<string, object>()));

            Assert.Equal("Circular include or maximum include depth exceeded: " + main, exception.Message);
        }

        [Fact]
        public void ReadScript_RepeatedNoncyclicInclude_ExpandsEveryOccurrence()
        {
            using var files = new ScriptFiles();
            var main = files.WriteScript("main.urscript", "{% include 'piece.urscript' %}{% include './piece.urscript' %}");
            files.WriteScript("piece.urscript", "value={{VALUE}}");

            var result = _reader.ReadScript(main, new Dictionary<string, object> { ["VALUE"] = 42 });

            Assert.Equal("value=42\nvalue=42\n\n", result);
            Assert.True(_reader.IsVariableRegistered("VALUE"));
        }

        [Fact]
        public void ReadScript_SharedNoncyclicInclude_ExpandsInEachBranch()
        {
            using var files = new ScriptFiles();
            var main = files.WriteScript("main.urscript", "{% include 'left.urscript' %}{% include 'right.urscript' %}");
            files.WriteScript("left.urscript", "left:{% include 'shared.urscript' %}");
            files.WriteScript("right.urscript", "right:{% include 'shared.urscript' %}");
            files.WriteScript("shared.urscript", "value={{VALUE}}");

            var result = _reader.ReadScript(main, new Dictionary<string, object> { ["VALUE"] = 42 });

            Assert.Equal("left:value=42\n\nright:value=42\n\n\n", result);
            Assert.True(_reader.IsVariableRegistered("VALUE"));
        }

        [Fact]
        public void ReadScript_NestedRelativeInclude_ResolvesFromContainingFile()
        {
            using var files = new ScriptFiles();
            var main = files.WriteScript("main.urscript", "{% include 'nested/child.urscript' %}");
            files.WriteScript("nested/child.urscript", "{% include 'leaf with space.urscript' %}{% include '../shared.urscript' %}");
            files.WriteScript("nested/leaf with space.urscript", "local={{LOCAL}}");
            files.WriteScript("leaf with space.urscript", "wrong={{WRONG}}");
            files.WriteScript("shared.urscript", "shared={{SHARED}}");
            var variables = new Dictionary<string, object> { ["LOCAL"] = 7, ["SHARED"] = 9 };

            var result = _reader.ReadScript(main, variables);

            Assert.Equal("local=7\nshared=9\n\n\n", result);
            Assert.True(_reader.IsVariableRegistered("LOCAL"));
            Assert.True(_reader.IsVariableRegistered("SHARED"));
            Assert.False(_reader.IsVariableRegistered("WRONG"));
        }

        [Theory]
        [InlineData(true, 31)]
        [InlineData(true, 32)]
        [InlineData(false, 31)]
        [InlineData(false, 32)]
        public void Include_WithinDepthLimit_ExpandsLeaf(bool readFromFile, int fileCount)
        {
            using var files = new ScriptFiles();
            var main = files.WriteChain(fileCount);
            var variables = new Dictionary<string, object> { ["LEAF"] = 42 };

            var result = readFromFile
                ? _reader.ReadScript(main, variables)
                : _reader.ProcessContent("{% include 'file-0.urscript' %}", variables, files.RootPath);

            Assert.Equal("leaf=42" + new string('\n', fileCount + (readFromFile ? 0 : 1)), result);
            Assert.True(_reader.IsVariableRegistered("LEAF"));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Include_AboveDepthLimit_ThrowsUrException(bool readFromFile)
        {
            using var files = new ScriptFiles();
            var main = files.WriteChain(33);
            var variables = new Dictionary<string, object> { ["LEAF"] = 42 };

            var exception = Assert.Throws<UrException>(() => readFromFile
                ? _reader.ReadScript(main, variables)
                : _reader.ProcessContent("{% include 'file-0.urscript' %}", variables, files.RootPath));

            Assert.Equal("Circular include or maximum include depth exceeded: " + Path.Combine(files.RootPath, "file-32.urscript"), exception.Message);
            Assert.False(_reader.IsVariableRegistered("LEAF"));
        }

        private sealed class ScriptFiles : IDisposable
        {
            private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("UniversalRobotics.ScriptReader.");

            public string RootPath { get; }

            public ScriptFiles()
            {
                RootPath = Path.Combine(_directory.FullName, "scripts");
                Directory.CreateDirectory(RootPath);
            }

            public string WriteScript(string relativePath, string content)
            {
                var path = Path.GetFullPath(Path.Combine(RootPath, relativePath));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content);
                return path;
            }

            public string WriteChain(int fileCount)
            {
                for (int index = 0; index < fileCount; index++)
                {
                    WriteScript($"file-{index}.urscript", index == fileCount - 1
                        ? "leaf={{LEAF}}"
                        : "{% include 'file-" + (index + 1) + ".urscript' %}");
                }
                return Path.Combine(RootPath, "file-0.urscript");
            }

            public void Dispose() => _directory.Delete(recursive: true);
        }
    }
}
