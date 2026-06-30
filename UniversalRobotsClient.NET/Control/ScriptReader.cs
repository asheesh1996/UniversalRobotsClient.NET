// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
        private static readonly Regex s_varRegex = new Regex(@"\{\{(\w+)\}\}", RegexOptions.Compiled);
        private static readonly Regex s_includeRegex = new Regex(@"\{%\s*include\s+(\S+)\s*%\}", RegexOptions.Compiled);
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
            var content = File.ReadAllText(filePath, Encoding.UTF8);
            var baseDir = Path.GetDirectoryName(filePath) ?? string.Empty;
            return ProcessContent(content, variables, baseDir, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Processes URScript template content from a string (without file I/O).
        /// </summary>
        public string ProcessContent(string content, IDictionary<string, object> variables, string baseDir = "")
            => ProcessContent(content, variables, baseDir, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        private string ProcessContent(string content, IDictionary<string, object> variables, string baseDir, HashSet<string> visitedIncludes)
        {
            content = ProcessIncludes(content, variables, baseDir, visitedIncludes);
            content = ProcessConditionals(content, variables);
            content = ProcessVariables(content, variables);
            return content;
        }

        // ─── Include processing ───────────────────────────────────────────────────

        private string ProcessIncludes(string content, IDictionary<string, object> vars, string baseDir, HashSet<string> visited)
        {
            return s_includeRegex.Replace(content, m =>
            {
                var includeFile = m.Groups[1].Value;
                var fullPath = Path.Combine(baseDir, includeFile);

                if (visited.Contains(fullPath))
                    return string.Empty; // Prevent infinite recursion

                visited.Add(fullPath);
                if (!File.Exists(fullPath)) return string.Empty;

                var included = File.ReadAllText(fullPath, Encoding.UTF8);
                return ProcessContent(included, vars, baseDir, visited);
            });
        }

        // ─── Conditional processing ───────────────────────────────────────────────

        private string ProcessConditionals(string content, IDictionary<string, object> vars)
        {
            var result = new StringBuilder();
            var lines = content.Split('\n');
            var stack = new Stack<bool>();
            bool active = true;

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');

                var ifMatch = s_ifRegex.Match(line);
                if (ifMatch.Success)
                {
                    var condition = EvaluateCondition(ifMatch.Groups[1].Value.Trim(), vars);
                    stack.Push(active);
                    active = active && condition;
                    continue;
                }

                if (s_endifRegex.IsMatch(line))
                {
                    active = stack.Count > 0 ? stack.Pop() : true;
                    continue;
                }

                if (active)
                {
                    result.Append(line);
                    result.Append('\n');
                }
            }

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

        private static string ProcessVariables(string content, IDictionary<string, object> vars)
        {
            return s_varRegex.Replace(content, m =>
            {
                var name = m.Groups[1].Value;
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
