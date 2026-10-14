// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Leash;

internal static class SyntaxInfo
{
    internal static bool IsMemberInitializer(EqualsValueClauseSyntax node) =>
        node.Parent is PropertyDeclarationSyntax ||
        node.Parent is VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: BaseFieldDeclarationSyntax } };

    internal static SyntaxNode UnitOwner(SyntaxNode node) => node switch
    {
        EqualsValueClauseSyntax { Parent: { } owner } => owner,
        PrimaryConstructorBaseTypeSyntax { Parent.Parent: { } type } => type,
        _ => node,
    };

    internal static bool InheritsExemption(SyntaxNode parent, SyntaxNode child) => (parent, child) switch
    {
        (BaseFieldDeclarationSyntax, VariableDeclarationSyntax) => true,
        (VariableDeclarationSyntax, VariableDeclaratorSyntax) => true,
        (VariableDeclaratorSyntax, EqualsValueClauseSyntax) => true,
        (PropertyDeclarationSyntax, AccessorListSyntax or EqualsValueClauseSyntax) => true,
        (IndexerDeclarationSyntax, AccessorListSyntax) => true,
        (AccessorListSyntax, AccessorDeclarationSyntax) => true,
        (TypeDeclarationSyntax, BaseListSyntax) => true,
        (BaseListSyntax, PrimaryConstructorBaseTypeSyntax) => true,
        _ => false,
    };

    internal static string Name(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax method => method.Identifier.ValueText,
        ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
        DestructorDeclarationSyntax destructor => "~" + destructor.Identifier.ValueText,
        LocalFunctionStatementSyntax local => local.Identifier.ValueText,
        OperatorDeclarationSyntax op => "operator " + Checked(op.CheckedKeyword) + op.OperatorToken.Text,
        ConversionOperatorDeclarationSyntax op => op.ImplicitOrExplicitKeyword.Text + " operator " + Checked(op.CheckedKeyword) + WrittenText(op.Type),
        PropertyDeclarationSyntax property => property.Identifier.ValueText,
        IndexerDeclarationSyntax => "this",
        EventDeclarationSyntax @event => @event.Identifier.ValueText,
        AccessorDeclarationSyntax { Parent.Parent: { } owner } accessor => Name(owner) + "." + accessor.Keyword.ValueText,
        VariableDeclaratorSyntax variable => variable.Identifier.ValueText,
        BaseFieldDeclarationSyntax field => field.Declaration.Variables.Count == 0 ? "field" : field.Declaration.Variables[0].Identifier.ValueText,
        BaseTypeDeclarationSyntax type when type.RawKind == AnalysisEngine.ExtensionBlockDeclarationKind => "extension",
        BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
        EnumMemberDeclarationSyntax member => member.Identifier.ValueText,
        DelegateDeclarationSyntax declaration => declaration.Identifier.ValueText,
        LambdaExpressionSyntax => "lambda",
        AnonymousMethodExpressionSyntax => "anonymous method",
        CompilationUnitSyntax => "top-level statements",
        ParameterSyntax parameter => parameter.Identifier.ValueText,
        TypeParameterSyntax parameter => parameter.Identifier.ValueText,
        _ => node.Kind().ToString(),
    };

    internal static SyntaxToken LocationToken(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax method => method.Identifier,
        ConstructorDeclarationSyntax constructor => constructor.Identifier,
        DestructorDeclarationSyntax destructor => destructor.Identifier,
        LocalFunctionStatementSyntax local => local.Identifier,
        OperatorDeclarationSyntax op => op.OperatorToken,
        ConversionOperatorDeclarationSyntax op => op.ImplicitOrExplicitKeyword,
        PropertyDeclarationSyntax property => property.Identifier,
        IndexerDeclarationSyntax indexer => indexer.ThisKeyword,
        AccessorDeclarationSyntax accessor => accessor.Keyword,
        VariableDeclaratorSyntax variable => variable.Identifier,
        TypeDeclarationSyntax type => type.Identifier,
        DelegateDeclarationSyntax declaration => declaration.Identifier,
        LambdaExpressionSyntax lambda => lambda.ArrowToken,
        AnonymousMethodExpressionSyntax anonymous => anonymous.DelegateKeyword,
        _ => node.GetFirstToken(),
    };

    // Checked operators are written as "operator checked +" and "explicit operator checked T".
    private static string Checked(SyntaxToken keyword) => keyword.RawKind == 0 ? string.Empty : "checked ";

    // The written tokens with each run of trivia reduced to one space, so that line breaks and comments
    // inside a written type cannot reach a single-line diagnostic message.
    private static string WrittenText(SyntaxNode node)
    {
        var builder = new StringBuilder();
        var separated = false;
        foreach (var token in node.DescendantTokens())
        {
            if (builder.Length != 0 && (separated || token.HasLeadingTrivia))
            {
                builder.Append(' ');
            }

            builder.Append(token.Text);
            separated = token.HasTrailingTrivia;
        }

        return builder.ToString();
    }
}
