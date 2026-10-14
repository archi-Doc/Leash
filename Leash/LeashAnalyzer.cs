// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Leash;

/// <summary>
/// Enforces syntax-based implementation size, parameter limits, and exemption rules for C# code.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LeashAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => DiagnosticRules.All;

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(static context =>
        {
            var report = new DiagnosticSink(context);
            var settings = AnalyzerSettings.Read(
                context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.Tree),
                context.Tree,
                in report);
            AnalysisEngine.Analyze(context.Tree.GetRoot(context.CancellationToken), settings, in report, context.CancellationToken);
        });
    }
}
