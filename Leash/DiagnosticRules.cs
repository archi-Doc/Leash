// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Leash;

internal static class DiagnosticRules
{
    internal const string ElementDefinition = "An element is a single syntax token: an identifier, keyword, operator, punctuation, or literal.";

    internal static readonly DiagnosticDescriptor Size = Create(
        "LEASH0001",
        "Implementation is too large",
        "'{0}' is {1} logical lines long (limit {2}). Statements: {3}, elements: {4}.",
        SizeDescription);

    internal static readonly DiagnosticDescriptor Parameters = Create(
        "LEASH0002",
        "Too many parameters",
        "'{0}' has {1} parameters (limit {2}).",
        "Review responsibilities. Group values only when they represent one coherent concept.");

    internal static readonly DiagnosticDescriptor OutParameters = Create(
        "LEASH0003",
        "Too many out parameters",
        "'{0}' has {1} out parameters (limit {2}).",
        "Review the result contract. Consider returning related results together.");

    internal static readonly DiagnosticDescriptor Configuration = Create(
        "LEASH0004",
        "Invalid configuration",
        "Invalid value '{0}' for '{1}'. Expected {2}.",
        "Correct the option using the allowed values. Invalid options never silently fall back to defaults.",
        "Configuration");

    internal static readonly DiagnosticDescriptor Exemption = Create(
        "LEASH0005",
        "Invalid exemption",
        "Invalid exemption on '{0}': {1}.",
        "Correct the attribute target or reason, or remove a disabled exemption.",
        "Configuration");

    internal static readonly ImmutableArray<DiagnosticDescriptor> All = [Size, Parameters, OutParameters, Configuration, Exemption];

    private const string HelpRoot = "https://github.com/archi-Doc/Leash/blob/main/docs/";
    private const string SizeDescription =
        "Logical lines = max(weighted statement count, ceiling(elements / 10)). Limits are inclusive. " +
        "Statements count declarations, assignments, increments, and control structures using the specification's weights. " +
        ElementDefinition + " Separate responsibilities and remove duplication.";

    private static DiagnosticDescriptor Create(
        string id,
        string title,
        string message,
        string description,
        string category = "Maintainability") =>
        new(
            id,
            title,
            message,
            category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: description,
            helpLinkUri: HelpRoot + id + ".md",
            customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });
}
