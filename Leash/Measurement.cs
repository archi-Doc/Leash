// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using Microsoft.CodeAnalysis;

namespace Leash;

internal readonly struct Measurement
{
    internal Measurement(SyntaxNode node, long statements, long elements)
    {
        this.Node = node;
        this.Statements = statements;
        this.Elements = elements;
    }

    internal SyntaxNode Node { get; }

    internal long Statements { get; }

    internal long Elements { get; }

    internal long LogicalLines => Math.Max(this.Statements, (this.Elements / 10) + (this.Elements % 10 == 0 ? 0 : 1));
}
