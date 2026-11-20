// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UniversalRobotics.Exceptions;
using UniversalRobotics.Models;

namespace UniversalRobotics.Control
{
    /// <summary>
    /// Reads a URScript file and performs Jinja2-like template substitution.
    /// Supports variable replacement, file inclusion, and version-aware conditionals.
    /// Replaces the C++ <c>ScriptReader</c> class.
    /// </summary>
    public sealed class ScriptReader
    {
        private const int MaxIncludeDepth = 32;
        private readonly object _sync = new object();
        private readonly HashSet<string> _registeredVariables = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>When enabled, unresolved replacements fail instead of leaving a placeholder. Enabled by UrDriver.</summary>
        public bool StrictVariables { get; set; }
        private static readonly Regex s_varRegex = new Regex(@"\{\{\s*([\w-]+)\s*\}\}", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
        private static readonly Regex s_includeRegex = new Regex(@"\{%\s*include\s+(['""].+?['""]|\S+)\s*%\}", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
        private static readonly Regex s_ifRegex = new Regex(@"\{%\s*if\s+(.+?)\s*%\}", RegexOptions.Compiled);
        private static readonly Regex s_endifRegex = new Regex(@"\{%\s*endif\s*%\}", RegexOptions.Compiled);
        private static readonly Regex s_versionRegex = new Regex(@"^v(\d+\.\d+(?:\.\d+)?)$", RegexOptions.Compiled);
        private static readonly Regex s_conditionRegex = new Regex(
            @"(\w+)\s*(==|!=|>=|<=|>|<)\s*(v[\d.]+|\w+|""[^""]*"")", RegexOptions.Compiled);

        /// <summary>
        /// Reads a URScript file and applies template substitution using the provided variables.
        /// </summary>
        /// <param name="filePath">Path to the URScript file (.urscript).</param>
        /// <param name="variables">Dictionary of variable substitutions. Values may be string, int, double, bool, or <see cref="VersionInformation"/>.</param>
        /// <returns>The processed URScript content as a string.</returns>
        public string ReadScript(string filePath, IDictionary<string, object> variables)
        {
            lock (_sync)
            {
                _registeredVariables.Clear();
                var path = Path.GetFullPath(filePath);
                RejectLinkedPath(path);
                var root = Path.GetDirectoryName(path)!;
                return ReadIncludedScript(path, variables, root, new HashSet<string>(PathComparer));
            }
        }

        /// <summary>
        /// Processes URScript template content from a string (without file I/O).
        /// </summary>
        public string ProcessContent(string content, IDictionary<string, object> variables, string baseDir = "")
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (variables == null) throw new ArgumentNullException(nameof(variables));
            lock (_sync)
            {
                _registeredVariables.Clear();
                var root = Path.GetFullPath(string.IsNullOrEmpty(baseDir) ? "." : baseDir);
                return ProcessContent(content, variables, root, root, new HashSet<string>(PathComparer));
            }
        }

        /// <summary>Whether the last processed template used a given replacement variable.</summary>
        public bool IsVariableRegistered(string key)
        {
            lock (_sync) return _registeredVariables.Contains(key);
        }

        private static StringComparer PathComparer => Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        private string ProcessContent(string content, IDictionary<string, object> variables, string baseDir, string rootDir, HashSet<string> visitedIncludes)
        {
            content = ProcessVariables(content, variables);
            content = ProcessConditionals(content, variables);
            content = ProcessIncludes(content, variables, baseDir, rootDir, visitedIncludes);
            return content;
        }

        // ─── Include processing ───────────────────────────────────────────────────

        private string ProcessIncludes(string content, IDictionary<string, object> vars, string baseDir, string rootDir, HashSet<string> visited)
        {
            return s_includeRegex.Replace(content, m =>
            {
                var includeFile = m.Groups[1].Value.Trim('"', '\'');
                if (Path.IsPathRooted(includeFile) || includeFile.IndexOf(':') >= 0 || includeFile.StartsWith("\\", StringComparison.Ordinal))
                    throw new UrException($"Rooted include paths are not allowed: {includeFile}");
                var fullPath = Path.GetFullPath(Path.Combine(baseDir, includeFile));
                var prefix = rootDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!fullPath.StartsWith(prefix, comparison))
                    throw new UrException($"Include is outside the script root: {includeFile}");
                RejectLinkedPath(fullPath);
                return ReadIncludedScript(fullPath, vars, rootDir, visited);
            });
        }

        private string ReadIncludedScript(string path, IDictionary<string, object> variables, string rootDir, HashSet<string> visited)
        {
            if (visited.Count >= MaxIncludeDepth || !visited.Add(path))
                throw new UrException($"Circular include or maximum include depth exceeded: {path}");
            try
            {
                return ProcessContent(File.ReadAllText(path, Encoding.UTF8), variables, Path.GetDirectoryName(path)!, rootDir, visited);
            }
            finally
            {
                visited.Remove(path);
            }
        }

        private static void RejectLinkedPath(string path)
        {
            for (string? current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new UrException($"Linked script paths are not supported: {current}");
            }
        }

        // ─── Conditional processing ───────────────────────────────────────────────

        private string ProcessConditionals(string content, IDictionary<string, object> vars)
        {
            var result = new StringBuilder();
            var lines = content.Split('\n');
            var stack = new Stack<(bool Parent, bool Taken, bool HasElse)>();
            bool active = true;

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');

                var ifMatch = s_ifRegex.Match(line);
                if (ifMatch.Success)
                {
                    var condition = EvaluateCondition(ifMatch.Groups[1].Value.Trim(), vars);
                    stack.Push((active, condition, false));
                    active = active && condition;
                    continue;
                }

                var branch = Regex.Match(line, @"\{%\s*(elif\s+(.+?)|else)\s*%\}", RegexOptions.None, TimeSpan.FromSeconds(1));
                if (branch.Success)
                {
                    if (stack.Count == 0) throw new UrException("Unmatched template branch.");
                    var frame = stack.Pop();
                    if (frame.HasElse) throw new UrException("Template branch after else.");
                    bool isElse = branch.Groups[1].Value == "else";
                    bool selected = !frame.Taken && (isElse || EvaluateCondition(branch.Groups[2].Value, vars));
                    active = frame.Parent && selected;
                    stack.Push((frame.Parent, frame.Taken || selected, isElse));
                    continue;
                }

                if (s_endifRegex.IsMatch(line))
                {
                    if (stack.Count == 0) throw new UrException("Unmatched template endif.");
                    active = stack.Pop().Parent;
                    continue;
                }

                if (active)
                {
                    result.Append(line);
                    result.Append('\n');
                }
            }

            if (stack.Count != 0) throw new UrException("Unclosed template if.");
            return result.ToString();
        }

        private bool EvaluateCondition(string condition, IDictionary<string, object> vars)
        {
            var m = s_conditionRegex.Match(condition);
            if (!m.Success) return false;

            var lhsName = m.Groups[1].Value;
            var op = m.Groups[2].Value;
            var rhsRaw = m.Groups[3].Value.Trim('"');

            if (!vars.TryGetValue(lhsName, out var lhsVal)) return false;

            // Version comparison
            if (lhsVal is VersionInformation lhsVer && s_versionRegex.IsMatch(rhsRaw))
            {
                var rhsVer = VersionInformation.Parse(rhsRaw.TrimStart('v'));
                return op switch
                {
                    "==" => lhsVer == rhsVer,
                    "!=" => lhsVer != rhsVer,
                    ">=" => lhsVer >= rhsVer,
                    "<=" => lhsVer <= rhsVer,
                    ">" => lhsVer > rhsVer,
                    "<" => lhsVer < rhsVer,
                    _ => false
                };
            }

            // Boolean comparison
            if (lhsVal is bool lhsBool && bool.TryParse(rhsRaw, out var rhsBool))
                return op == "==" ? lhsBool == rhsBool : lhsBool != rhsBool;

            // String comparison
            var lhsStr = lhsVal?.ToString() ?? string.Empty;
            int cmp = string.Compare(lhsStr, rhsRaw, StringComparison.OrdinalIgnoreCase);
            return op switch
            {
                "==" => cmp == 0,
                "!=" => cmp != 0,
                ">=" => cmp >= 0,
                "<=" => cmp <= 0,
                ">" => cmp > 0,
                "<" => cmp < 0,
                _ => false
            };
        }

        // ─── Variable substitution ────────────────────────────────────────────────

        private string ProcessVariables(string content, IDictionary<string, object> vars)
        {
            return s_varRegex.Replace(content, m =>
            {
                var name = m.Groups[1].Value;
                _registeredVariables.Add(name);
                if (StrictVariables && !vars.ContainsKey(name)) throw new UrException($"Unknown script variable: {name}");
                return vars.TryGetValue(name, out var val) ? FormatValue(val) : m.Value;
            });
        }

        private static string FormatValue(object val) => val switch
        {
            bool b => b ? "True" : "False",
            double d => d.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
            float f => f.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
            int i => i.ToString(),
            long l => l.ToString(),
            VersionInformation v => v.ToString(),
            _ => val?.ToString() ?? string.Empty
        };
    }
}
