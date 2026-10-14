// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Leash;

// Receives diagnostics from an analysis context or a callback. Holding the context by value avoids
// boxing it into a delegate for every syntax tree.
internal readonly struct DiagnosticSink
{
    private readonly SyntaxTreeAnalysisContext context;
    private readonly Action<Diagnostic>? callback;

    internal DiagnosticSink(SyntaxTreeAnalysisContext context)
    {
        this.context = context;
        this.callback = null;
    }

    internal DiagnosticSink(Action<Diagnostic> callback)
    {
        this.context = default;
        this.callback = callback;
    }

    internal void Report(Diagnostic diagnostic)
    {
        if (this.callback is { } callback)
        {
            callback(diagnostic);
        }
        else
        {
            this.context.ReportDiagnostic(diagnostic);
        }
    }
}
