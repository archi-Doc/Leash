// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Leash;

internal static class Exemptions
{
    // Flags combine across a declaration's attributes: any invalid attribute vetoes its exemption.
    internal const byte Valid = 1;
    internal const byte Invalid = 2;

    internal static byte Evaluate(AttributeSyntax attribute, bool? allowed, in DiagnosticSink report)
    {
        // A generic name is matched by its identifier, whether or not it is qualified.
        var identifier = attribute.Name switch
        {
            SimpleNameSyntax name => name.Identifier.ValueText,
            QualifiedNameSyntax name => name.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax name => name.Name.Identifier.ValueText,
            _ => string.Empty,
        };
        if (identifier != "Exempt" && identifier != "ExemptAttribute")
        {
            return 0;
        }

        var list = (AttributeListSyntax)attribute.Parent!;
        var owner = list.Parent!;
        string? error = null;
        if (allowed == false)
        {
            error = "exemptions are disabled by leash.allow_exempt; remove this attribute";
        }
        else if (!IsSupportedTarget(owner, list.Target?.Identifier.ValueText))
        {
            error = "unsupported target; place the attribute on a function, field, property, indexer, or delegate";
        }
        else if (!HasValidReason(attribute))
        {
            error = "expected exactly one positional, non-blank string literal reason";
        }

        if (error is not null)
        {
            var name = owner is CompilationUnitSyntax && list.Target is { } target ? target.Identifier.ValueText : SyntaxInfo.Name(owner);
            report.Report(Diagnostic.Create(DiagnosticRules.Exemption, attribute.GetLocation(), name, error));
            return Invalid;
        }

        return allowed == true ? Valid : (byte)0;
    }

    private static bool HasValidReason(AttributeSyntax attribute)
    {
        if (attribute.ArgumentList is not { Arguments.Count: 1 } arguments)
        {
            return false;
        }

        var argument = arguments.Arguments[0];
        return argument.NameColon is null && argument.NameEquals is null &&
            argument.Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression) &&
            !string.IsNullOrWhiteSpace(literal.Token.ValueText);
    }

    private static bool IsSupportedTarget(SyntaxNode owner, string? target) => owner switch
    {
        BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or AccessorDeclarationSyntax or LambdaExpressionSyntax
            => target is null or "method",
        BaseFieldDeclarationSyntax => target is null or "field",
        PropertyDeclarationSyntax or IndexerDeclarationSyntax => target is null or "property",
        DelegateDeclarationSyntax => target is null or "type",

        // An extension block's receiver is not a primary constructor.
        TypeDeclarationSyntax { ParameterList: not null } type when type.RawKind != AnalysisEngine.ExtensionBlockDeclarationKind
            => target == "method",
        _ => false,
    };
}
