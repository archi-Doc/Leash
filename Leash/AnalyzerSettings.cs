// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Leash;

internal readonly struct AnalyzerSettings
{
    private static readonly char[] LineBreaks = ['\r', '\n', '\u0085', (char)0x2028, (char)0x2029];

    internal AnalyzerSettings(int? maxLines = 50, int? maxParameters = 6, int? maxOutParameters = 1, bool? allowExempt = true)
    {
        this.MaxLines = maxLines;
        this.MaxParameters = maxParameters;
        this.MaxOutParameters = maxOutParameters;
        this.AllowExempt = allowExempt;
    }

    internal int? MaxLines { get; }

    internal int? MaxParameters { get; }

    internal int? MaxOutParameters { get; }

    internal bool? AllowExempt { get; }

    internal static AnalyzerSettings Read(AnalyzerConfigOptions options, SyntaxTree tree, in DiagnosticSink report)
    {
        var lines = ReadInteger(options, tree, report, "leash.max_lines", 1, 50);
        var parameters = ReadInteger(options, tree, report, "leash.max_parameters", 0, 6);
        var outParameters = ReadInteger(options, tree, report, "leash.max_out_parameters", 0, 1);
        bool? allow = true;
        const string key = "leash.allow_exempt";
        if (options.TryGetValue(key, out var raw))
        {
            if (bool.TryParse(raw.Trim(), out var value))
            {
                allow = value;
            }
            else
            {
                Invalid(tree, report, key, raw, "true or false");
                allow = null;
            }
        }

        return new AnalyzerSettings(lines, parameters, outParameters, allow);
    }

    private static int? ReadInteger(
        AnalyzerConfigOptions options,
        SyntaxTree tree,
        in DiagnosticSink report,
        string key,
        int minimum,
        int fallback)
    {
        if (!options.TryGetValue(key, out var raw))
        {
            return fallback;
        }

        if (int.TryParse(raw.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= minimum)
        {
            return value;
        }

        Invalid(tree, report, key, raw, minimum == 1 ? "an integer from 1 to 2147483647" : "an integer from 0 to 2147483647");
        return null;
    }

    private static void Invalid(SyntaxTree tree, in DiagnosticSink report, string key, string value, string allowed) =>
        report.Report(Diagnostic.Create(
            DiagnosticRules.Configuration,
            Location.Create(tree, new TextSpan(0, 0)),
            EscapeLineBreaks(value),
            key,
            allowed));

    // Escapes every character that C# and Unicode treat as a line break, so that the message stays on one line.
    private static string EscapeLineBreaks(string value)
    {
        if (value.IndexOfAny(LineBreaks) < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 8);
        foreach (var character in value)
        {
            switch (character)
            {
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\u0085':
                case (char)0x2028:
                case (char)0x2029:
                    builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }
}
