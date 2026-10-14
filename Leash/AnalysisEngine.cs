// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Globalization;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Leash;

internal static class AnalysisEngine
{
    // SyntaxKind.ExtensionBlockDeclaration in Roslyn 5.0 and later; verified by a test against the current Roslyn.
    internal const int ExtensionBlockDeclarationKind = 9079;

    private const int InitialDepth = 64;
    private const int MaxCachedDepth = 1024;
    private const int MaxClassTableLength = 1 << 16;

    // Node classes indexed by syntax kind, so that each node is classified by one array read.
    private static readonly NodeClass[] ClassTable = CreateClassTable();

    // One reusable traversal stack per thread. A re-entrant call from a callback allocates its own.
    [ThreadStatic]
    private static Frame[]? cachedStack;

    internal static long Analyze(
        SyntaxNode root,
        AnalyzerSettings settings,
        Action<Diagnostic> report,
        CancellationToken cancellationToken = default,
        Action<Measurement>? observe = null) =>
        Analyze(root, settings, new DiagnosticSink(report), cancellationToken, observe);

    // Each edge/token is visited once. Completed child summaries are folded into their parent.
    // An explicit stack avoids recursion and retains only O(depth) traversal state.
    internal static long Analyze(
        SyntaxNode root,
        AnalyzerSettings settings,
        in DiagnosticSink report,
        CancellationToken cancellationToken = default,
        Action<Measurement>? observe = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stack = cachedStack ?? new Frame[InitialDepth];
        cachedStack = null;
        var count = 1;
        var depth = 1;
        var steps = 0;
        long checksum = 0;
        stack[0].Reset(root, Classify(root), inheritedExemption: false);
        try
        {
            while (count != 0)
            {
                ref var frame = ref stack[count - 1];
                if (!frame.Children.MoveNext())
                {
                    Finish(ref frame, in settings, report, observe, ref checksum);
                    if (--count != 0)
                    {
                        stack[count - 1].Add(frame.Node, frame.Class, in frame.Total);
                    }

                    continue;
                }

                var child = frame.Children.Current;
                if (child.IsToken)
                {
                    frame.Total.Elements += Count(child.AsToken());
                    continue;
                }

                var node = child.AsNode()!;
                var nodeClass = Classify(node);
                if (nodeClass >= NodeClass.Operand)
                {
                    // A single-token node is folded into its parent without a frame. It is missing exactly when its token is.
                    frame.AddLeaf(node, nodeClass, node.IsMissing ? 0 : 1);
                    continue;
                }

                if ((++steps & 255) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var inherited = frame.IsExempt && SyntaxInfo.InheritsExemption(frame.Node, node);
                if (count == stack.Length)
                {
                    var expanded = new Frame[checked(count * 2)];
                    Array.Copy(stack, expanded, count);
                    stack = expanded;
                }

                stack[count++].Reset(node, nodeClass, inherited);
                if (count > depth)
                {
                    depth = count;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return checksum;
        }
        finally
        {
            // Drop node references before caching; this also runs when cancellation or a callback interrupted traversal.
            Array.Clear(stack, 0, depth);
            if (stack.Length <= MaxCachedDepth)
            {
                cachedStack = stack;
            }
        }
    }

    private static NodeClass Classify(SyntaxNode node)
    {
        var kind = node.RawKind;
        var table = ClassTable;
        var nodeClass = (uint)kind < (uint)table.Length ? table[kind] : NodeClass.Unresolved;
        if (nodeClass < NodeClass.EqualsValueClause)
        {
            return nodeClass;
        }

        if (nodeClass == NodeClass.EqualsValueClause)
        {
            return SyntaxInfo.IsMemberInitializer((EqualsValueClauseSyntax)node) ? NodeClass.MemberInitializer : NodeClass.Other;
        }

        // Each kind has one syntax type, so the first resolution holds for all nodes of the kind.
        // Concurrent analyses can only store the same value.
        nodeClass = node is ExpressionSyntax ? NodeClass.Expression : node is StatementSyntax ? NodeClass.Statement : NodeClass.Other;
        if ((uint)kind < (uint)table.Length)
        {
            table[kind] = nodeClass;
        }

        return nodeClass;
    }

    private static NodeClass[] CreateClassTable()
    {
        // Size the table for the host's Roslyn, which can define newer kinds than the compile-time baseline.
        var length = ExtensionBlockDeclarationKind + 1;
        foreach (SyntaxKind kind in Enum.GetValues(typeof(SyntaxKind)))
        {
            length = Math.Max(length, (int)kind + 1);
        }

        var table = new NodeClass[Math.Min(length, MaxClassTableLength)];
        for (var kind = 0; kind < table.Length; kind++)
        {
            table[kind] = FixedClass((SyntaxKind)kind);
        }

        return table;
    }

    private static NodeClass FixedClass(SyntaxKind kind)
    {
        switch (kind)
        {
            case SyntaxKind.IdentifierName:
            case SyntaxKind.PredefinedType:
            case SyntaxKind.ArgListExpression:
            case SyntaxKind.NumericLiteralExpression:
            case SyntaxKind.StringLiteralExpression:
            case SyntaxKind.Utf8StringLiteralExpression:
            case SyntaxKind.CharacterLiteralExpression:
            case SyntaxKind.TrueLiteralExpression:
            case SyntaxKind.FalseLiteralExpression:
            case SyntaxKind.NullLiteralExpression:
            case SyntaxKind.DefaultLiteralExpression:
            case SyntaxKind.ThisExpression:
            case SyntaxKind.BaseExpression:
            case SyntaxKind.OmittedArraySizeExpression:
            case SyntaxKind.OmittedTypeArgument:
                return NodeClass.Operand;
            case SyntaxKind.SingleVariableDesignation:
            case SyntaxKind.DiscardDesignation:
                return NodeClass.Target;
            case SyntaxKind.InterpolatedStringText:
            case SyntaxKind.DiscardPattern:
                return NodeClass.Text;
            case SyntaxKind.InvocationExpression:
            case SyntaxKind.SimpleMemberAccessExpression:
            case SyntaxKind.PointerMemberAccessExpression:
            case SyntaxKind.ElementAccessExpression:
            case SyntaxKind.ConditionalAccessExpression:
            case SyntaxKind.MemberBindingExpression:
            case SyntaxKind.ElementBindingExpression:
            case SyntaxKind.ObjectCreationExpression:
            case SyntaxKind.ImplicitObjectCreationExpression:
            case SyntaxKind.ArrayCreationExpression:
            case SyntaxKind.ImplicitArrayCreationExpression:
            case SyntaxKind.CollectionExpression:
            case SyntaxKind.ObjectInitializerExpression:
            case SyntaxKind.CollectionInitializerExpression:
            case SyntaxKind.ArrayInitializerExpression:
            case SyntaxKind.ComplexElementInitializerExpression:
            case SyntaxKind.WithInitializerExpression:
            case SyntaxKind.WithExpression:
            case SyntaxKind.ConditionalExpression:
            case SyntaxKind.CastExpression:
            case SyntaxKind.AwaitExpression:
            case SyntaxKind.IsExpression:
            case SyntaxKind.AsExpression:
            case SyntaxKind.IsPatternExpression:
            case SyntaxKind.SwitchExpression:
            case SyntaxKind.InterpolatedStringExpression:
            case SyntaxKind.AddExpression:
            case SyntaxKind.SubtractExpression:
            case SyntaxKind.MultiplyExpression:
            case SyntaxKind.DivideExpression:
            case SyntaxKind.ModuloExpression:
            case SyntaxKind.LeftShiftExpression:
            case SyntaxKind.RightShiftExpression:
            case SyntaxKind.UnsignedRightShiftExpression:
            case SyntaxKind.LogicalOrExpression:
            case SyntaxKind.LogicalAndExpression:
            case SyntaxKind.BitwiseOrExpression:
            case SyntaxKind.BitwiseAndExpression:
            case SyntaxKind.ExclusiveOrExpression:
            case SyntaxKind.EqualsExpression:
            case SyntaxKind.NotEqualsExpression:
            case SyntaxKind.LessThanExpression:
            case SyntaxKind.LessThanOrEqualExpression:
            case SyntaxKind.GreaterThanExpression:
            case SyntaxKind.GreaterThanOrEqualExpression:
            case SyntaxKind.CoalesceExpression:
            case SyntaxKind.UnaryPlusExpression:
            case SyntaxKind.UnaryMinusExpression:
            case SyntaxKind.BitwiseNotExpression:
            case SyntaxKind.LogicalNotExpression:
            case SyntaxKind.AddressOfExpression:
            case SyntaxKind.PointerIndirectionExpression:
            case SyntaxKind.IndexExpression:
            case SyntaxKind.SuppressNullableWarningExpression:
            case SyntaxKind.TypeOfExpression:
            case SyntaxKind.DefaultExpression:
            case SyntaxKind.ThrowExpression:
            case SyntaxKind.GenericName:
            case SyntaxKind.QualifiedName:
            case SyntaxKind.AliasQualifiedName:
            case SyntaxKind.ArrayType:
            case SyntaxKind.NullableType:
            case SyntaxKind.PointerType:
            case SyntaxKind.TupleType:
            case SyntaxKind.RefType:
                return NodeClass.Expression;
            case SyntaxKind.TupleExpression:
            case SyntaxKind.ParenthesizedExpression:
            case SyntaxKind.Argument:
                return NodeClass.PassThrough;
            case SyntaxKind.DeclarationExpression:
                return NodeClass.DeclarationExpression;
            case SyntaxKind.SimpleAssignmentExpression:
            case SyntaxKind.AddAssignmentExpression:
            case SyntaxKind.SubtractAssignmentExpression:
            case SyntaxKind.MultiplyAssignmentExpression:
            case SyntaxKind.DivideAssignmentExpression:
            case SyntaxKind.ModuloAssignmentExpression:
            case SyntaxKind.AndAssignmentExpression:
            case SyntaxKind.ExclusiveOrAssignmentExpression:
            case SyntaxKind.OrAssignmentExpression:
            case SyntaxKind.LeftShiftAssignmentExpression:
            case SyntaxKind.RightShiftAssignmentExpression:
            case SyntaxKind.UnsignedRightShiftAssignmentExpression:
            case SyntaxKind.CoalesceAssignmentExpression:
                return NodeClass.Assignment;
            case SyntaxKind.PreIncrementExpression:
            case SyntaxKind.PreDecrementExpression:
            case SyntaxKind.PostIncrementExpression:
            case SyntaxKind.PostDecrementExpression:
                return NodeClass.IncrementOrDecrement;
            case SyntaxKind.SimpleLambdaExpression:
                return NodeClass.SimpleLambda;
            case SyntaxKind.ParenthesizedLambdaExpression:
                return NodeClass.ParenthesizedLambda;
            case SyntaxKind.AnonymousMethodExpression:
                return NodeClass.AnonymousMethod;
            case SyntaxKind.VariableDeclarator:
                return NodeClass.Declarator;
            case SyntaxKind.CatchDeclaration:
                return NodeClass.CatchDeclaration;
            case SyntaxKind.Attribute:
                return NodeClass.Attribute;
            case SyntaxKind.AttributeList:
                return NodeClass.AttributeList;
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.DestructorDeclaration:
            case SyntaxKind.OperatorDeclaration:
            case SyntaxKind.ConversionOperatorDeclaration:
                return NodeClass.Method;
            case SyntaxKind.ConstructorDeclaration:
                return NodeClass.Constructor;
            case SyntaxKind.GetAccessorDeclaration:
            case SyntaxKind.SetAccessorDeclaration:
            case SyntaxKind.InitAccessorDeclaration:
            case SyntaxKind.AddAccessorDeclaration:
            case SyntaxKind.RemoveAccessorDeclaration:
            case SyntaxKind.UnknownAccessorDeclaration:
                return NodeClass.Accessor;
            case SyntaxKind.LocalFunctionStatement:
                return NodeClass.LocalFunction;
            case SyntaxKind.EqualsValueClause:
                return NodeClass.EqualsValueClause;
            case SyntaxKind.PrimaryConstructorBaseType:
                return NodeClass.PrimaryConstructorBaseType;
            case SyntaxKind.PropertyDeclaration:
                return NodeClass.Property;
            case SyntaxKind.IndexerDeclaration:
                return NodeClass.Indexer;
            case SyntaxKind.CompilationUnit:
                return NodeClass.CompilationUnit;
            case SyntaxKind.GlobalStatement:
                return NodeClass.GlobalStatement;
            case SyntaxKind.BaseConstructorInitializer:
            case SyntaxKind.ThisConstructorInitializer:
                return NodeClass.ConstructorInitializer;
            case SyntaxKind.ArrowExpressionClause:
                return NodeClass.ArrowExpressionClause;
            case SyntaxKind.IfStatement:
            case SyntaxKind.ForStatement:
            case SyntaxKind.WhileStatement:
            case SyntaxKind.DoStatement:
            case SyntaxKind.SwitchStatement:
            case SyntaxKind.TryStatement:
            case SyntaxKind.CatchClause:
            case SyntaxKind.FinallyClause:
            case SyntaxKind.UsingStatement:
            case SyntaxKind.LockStatement:
            case SyntaxKind.FixedStatement:
            case SyntaxKind.CheckedStatement:
            case SyntaxKind.UncheckedStatement:
            case SyntaxKind.UnsafeStatement:
            case SyntaxKind.SwitchSection:
                return NodeClass.Control;
            case SyntaxKind.ForEachStatement:
                return NodeClass.ForEach;
            case SyntaxKind.ForEachVariableStatement:
                return NodeClass.ForEachVariable;
            case SyntaxKind.ExpressionStatement:
            case SyntaxKind.LocalDeclarationStatement:
            case SyntaxKind.ReturnStatement:
            case SyntaxKind.ThrowStatement:
            case SyntaxKind.BreakStatement:
            case SyntaxKind.ContinueStatement:
            case SyntaxKind.GotoStatement:
            case SyntaxKind.GotoCaseStatement:
            case SyntaxKind.GotoDefaultStatement:
            case SyntaxKind.YieldReturnStatement:
            case SyntaxKind.YieldBreakStatement:
                return NodeClass.Statement;
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.StructDeclaration:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.RecordDeclaration:
            case SyntaxKind.RecordStructDeclaration:
                return NodeClass.TypeDeclaration;
            case SyntaxKind.DelegateDeclaration:
                return NodeClass.Delegate;
            case SyntaxKind.Block:
                return NodeClass.Block;
            case SyntaxKind.EmptyStatement:
            case SyntaxKind.LabeledStatement:
            case SyntaxKind.ElseClause:
            case SyntaxKind.CatchFilterClause:
            case SyntaxKind.ArgumentList:
            case SyntaxKind.BracketedArgumentList:
            case SyntaxKind.TypeArgumentList:
            case SyntaxKind.ParameterList:
            case SyntaxKind.BracketedParameterList:
            case SyntaxKind.Parameter:
            case SyntaxKind.VariableDeclaration:
            case SyntaxKind.NameColon:
            case SyntaxKind.NameEquals:
            case SyntaxKind.Interpolation:
            case SyntaxKind.AttributeArgumentList:
            case SyntaxKind.AttributeArgument:
            case SyntaxKind.SwitchExpressionArm:
            case SyntaxKind.WhenClause:
            case SyntaxKind.CaseSwitchLabel:
            case SyntaxKind.CasePatternSwitchLabel:
            case SyntaxKind.DefaultSwitchLabel:
            case SyntaxKind.FieldDeclaration:
            case SyntaxKind.EventFieldDeclaration:
            case SyntaxKind.EventDeclaration:
            case SyntaxKind.AccessorList:
            case SyntaxKind.BaseList:
            case SyntaxKind.SimpleBaseType:
            case SyntaxKind.NamespaceDeclaration:
            case SyntaxKind.FileScopedNamespaceDeclaration:
            case SyntaxKind.UsingDirective:
            case SyntaxKind.EnumDeclaration:
            case SyntaxKind.EnumMemberDeclaration:
            case SyntaxKind.TypeParameterList:
            case SyntaxKind.TypeParameter:
            // An extension block (C# 14) declares a receiver, not a callable; its kind is newer than the Roslyn 4.14 baseline.
            case (SyntaxKind)ExtensionBlockDeclarationKind:
                return NodeClass.Other;
            default:
                return NodeClass.Unresolved;
        }
    }

    private static void Finish(
        ref Frame frame,
        in AnalyzerSettings settings,
        in DiagnosticSink report,
        Action<Measurement>? observe,
        ref long checksum)
    {
        var node = frame.Node;
        ref var total = ref frame.Total;
        switch (frame.Class)
        {
            case NodeClass.Expression:
                total.Leaves = 1;
                return;
            case NodeClass.PassThrough:
                return;
            case NodeClass.Assignment:
                total.Leaves = 1;
                total.Targets += frame.LeftLeaves;
                return;
            case NodeClass.IncrementOrDecrement:
                total.Leaves = 1;
                total.Targets++;
                return;
            case NodeClass.Declarator:
                total.Leaves = 0;
                if (!((VariableDeclaratorSyntax)node).Identifier.IsMissing)
                {
                    total.Targets++;
                }

                return;
            case NodeClass.CatchDeclaration:
                total.Leaves = 0;
                var identifier = ((CatchDeclarationSyntax)node).Identifier;
                if (identifier.RawKind != 0 && !identifier.IsMissing)
                {
                    total.Targets++;
                }

                return;
            case NodeClass.Attribute:
                total.Leaves = 0;
                total.Exemptions = Exemptions.Evaluate((AttributeSyntax)node, settings.AllowExempt, report);
                return;
            case NodeClass.Method:
            case NodeClass.Constructor:
            case NodeClass.Accessor:
            case NodeClass.LocalFunction:
            case NodeClass.SimpleLambda:
            case NodeClass.ParenthesizedLambda:
            case NodeClass.AnonymousMethod:
                if (frame.HasUnit)
                {
                    ReportUnit(node, frame.UnitStatements, frame.UnitElements, frame.IsExempt, in settings, report, observe, ref checksum);
                }

                total.Statements = frame.UnitStatements + (frame.Class == NodeClass.LocalFunction ? 1 : 0);
                total.Targets = 0;
                total.Leaves = frame.Class >= NodeClass.SimpleLambda ? 1 : 0;
                CheckParameters(node, frame.Class, frame.IsExempt, in settings, report);
                return;
            case NodeClass.MemberInitializer:
                ReportUnit(node, frame.UnitStatements, frame.UnitElements, frame.IsExempt, in settings, report, observe, ref checksum);
                total.Statements = frame.UnitStatements;
                total.Targets = 0;
                total.Leaves = 0;
                return;
            case NodeClass.PrimaryConstructorBaseType:
                total.Leaves = 0;
                MakeStatement(ref total);
                ReportUnit(node, total.Statements, total.Elements, frame.IsExempt, in settings, report, observe, ref checksum);
                return;
            case NodeClass.Property:
            case NodeClass.Indexer:
                if (frame.HasUnit)
                {
                    ReportUnit(node, frame.UnitStatements, frame.UnitElements, frame.IsExempt, in settings, report, observe, ref checksum);
                }

                total.Statements = 0;
                total.Targets = 0;
                total.Leaves = 0;
                CheckParameters(node, frame.Class, frame.IsExempt, in settings, report);
                return;
            case NodeClass.CompilationUnit:
                total.Leaves = 0;
                if (frame.HasUnit)
                {
                    ReportUnit(node, frame.UnitStatements, frame.UnitElements, exempt: false, in settings, report, observe, ref checksum);
                }

                return;
            case NodeClass.ConstructorInitializer:
            case NodeClass.ArrowExpressionClause:
            case NodeClass.Statement:
                total.Leaves = 0;
                MakeStatement(ref total);
                return;
            case NodeClass.ForEach:
                total.Leaves = 0;
                if (!((ForEachStatementSyntax)node).Identifier.IsMissing)
                {
                    total.Targets++;
                }

                total.Statements += 1 + total.Targets;
                total.Targets = 0;
                return;
            case NodeClass.ForEachVariable:
                total.Leaves = 0;
                total.Statements += 1 + total.Targets + frame.LeftLeaves;
                total.Targets = 0;
                return;
            case NodeClass.Control:
                total.Leaves = 0;
                total.Statements += 1 + total.Targets;
                total.Targets = 0;
                return;
            case NodeClass.TypeDeclaration:
            case NodeClass.Delegate:
                total.Leaves = 0;
                CheckParameters(node, frame.Class, frame.IsExempt, in settings, report);
                return;
            default:
                total.Leaves = 0;
                return;
        }
    }

    private static void MakeStatement(ref Metrics metrics)
    {
        metrics.Statements += Math.Max(1, metrics.Targets);
        metrics.Targets = 0;
    }

    private static int Count(SyntaxToken token) => token.RawKind == 0 || token.IsMissing || token.IsKind(SyntaxKind.EndOfFileToken) ? 0 : 1;

    private static void ReportUnit(
        SyntaxNode node,
        long statements,
        long elements,
        bool exempt,
        in AnalyzerSettings settings,
        in DiagnosticSink report,
        Action<Measurement>? observe,
        ref long checksum)
    {
        var measurement = new Measurement(node, statements, elements);
        checksum += measurement.LogicalLines;
        observe?.Invoke(measurement);
        if (exempt || settings.MaxLines is not int limit || measurement.LogicalLines <= limit)
        {
            return;
        }

        var owner = SyntaxInfo.UnitOwner(node);
        var name = SyntaxInfo.Name(owner);
        if (node is EqualsValueClauseSyntax)
        {
            name += " initializer";
        }

        report.Report(Diagnostic.Create(
            DiagnosticRules.Size,
            UnitLocationToken(owner).GetLocation(),
            name,
            Number(measurement.LogicalLines),
            Number(limit),
            Number(statements),
            Number(elements)));
    }

    private static SyntaxToken UnitLocationToken(SyntaxNode owner)
    {
        if (owner is CompilationUnitSyntax root)
        {
            foreach (var member in root.Members)
            {
                if (member is GlobalStatementSyntax global)
                {
                    return global.GetFirstToken();
                }
            }
        }

        return SyntaxInfo.LocationToken(owner);
    }

    private static void CheckParameters(SyntaxNode node, NodeClass nodeClass, bool exempt, in AnalyzerSettings settings, in DiagnosticSink report)
    {
        if (exempt)
        {
            return;
        }

        int count;
        var outCount = 0;
        if (nodeClass == NodeClass.SimpleLambda)
        {
            count = 1;
            if (((SimpleLambdaExpressionSyntax)node).Parameter.Modifiers.Any(SyntaxKind.OutKeyword))
            {
                outCount = 1;
            }
        }
        else if (ParameterList(node, nodeClass) is { } list)
        {
            var parameters = list.Parameters;
            count = parameters.Count;
            foreach (var parameter in parameters)
            {
                if (parameter.Modifiers.Any(SyntaxKind.OutKeyword))
                {
                    outCount++;
                }
            }
        }
        else
        {
            return;
        }

        if (settings.MaxParameters is int maximum && count > maximum)
        {
            report.Report(Diagnostic.Create(
                DiagnosticRules.Parameters,
                SyntaxInfo.LocationToken(node).GetLocation(),
                SyntaxInfo.Name(node),
                Number(count),
                Number(maximum)));
        }

        if (settings.MaxOutParameters is int outMaximum && outCount > outMaximum)
        {
            report.Report(Diagnostic.Create(
                DiagnosticRules.OutParameters,
                SyntaxInfo.LocationToken(node).GetLocation(),
                SyntaxInfo.Name(node),
                Number(outCount),
                Number(outMaximum)));
        }
    }

    private static BaseParameterListSyntax? ParameterList(SyntaxNode node, NodeClass nodeClass) => nodeClass switch
    {
        NodeClass.Method or NodeClass.Constructor => ((BaseMethodDeclarationSyntax)node).ParameterList,
        NodeClass.LocalFunction => ((LocalFunctionStatementSyntax)node).ParameterList,
        NodeClass.ParenthesizedLambda => ((ParenthesizedLambdaExpressionSyntax)node).ParameterList,
        NodeClass.AnonymousMethod => ((AnonymousMethodExpressionSyntax)node).ParameterList,
        NodeClass.Delegate => ((DelegateDeclarationSyntax)node).ParameterList,
        NodeClass.Indexer => ((IndexerDeclarationSyntax)node).ParameterList,
        NodeClass.TypeDeclaration => ((TypeDeclarationSyntax)node).ParameterList,
        _ => null,
    };

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    // Classes that are compared by range keep their relative order:
    // functions end with the anonymous functions, and single-token classes follow all frame classes.
    private enum NodeClass : byte
    {
        Other,
        Block,
        Expression,
        PassThrough,
        DeclarationExpression,
        Assignment,
        IncrementOrDecrement,
        Declarator,
        CatchDeclaration,
        Attribute,
        AttributeList,
        MemberInitializer,
        PrimaryConstructorBaseType,
        Property,
        Indexer,
        CompilationUnit,
        GlobalStatement,
        ConstructorInitializer,
        ArrowExpressionClause,
        Statement,
        Control,
        ForEach,
        ForEachVariable,
        TypeDeclaration,
        Delegate,
        Method,
        Constructor,
        Accessor,
        LocalFunction,
        SimpleLambda,
        ParenthesizedLambda,
        AnonymousMethod,

        // Single-token nodes folded into their parent: an operand that can be an assignment target leaf,
        // a declared variable, or text with no target role.
        Operand,
        Target,
        Text,

        // Table entries that are resolved per node and are never frame classes.
        EqualsValueClause,
        Unresolved,
    }

    private struct Metrics
    {
        internal long Statements;
        internal long Targets;
        internal long Elements;
        internal int Leaves;
        internal byte Exemptions;
    }

    private struct Frame
    {
        internal SyntaxNode Node;

        // The child with a special role: an assignment target, a deconstructing foreach variable,
        // a lambda expression body, or a member initializer value.
        internal SyntaxNode? KeyNode;
        internal ChildSyntaxList.Enumerator Children;
        internal Metrics Total;

        // The frame's own size unit, combined from its block or expression bodies, constructor initializer,
        // initializer value, or top-level statements.
        internal long UnitStatements;
        internal long UnitElements;
        internal int LeftLeaves;
        internal NodeClass Class;
        internal bool InheritedExemption;
        internal bool HasUnit;

        internal readonly bool IsExempt => this.InheritedExemption || this.Total.Exemptions == Exemptions.Valid;

        internal void Reset(SyntaxNode node, NodeClass nodeClass, bool inheritedExemption)
        {
            this = default;
            this.Node = node;
            this.Class = nodeClass;
            this.InheritedExemption = inheritedExemption;
            this.KeyNode = nodeClass switch
            {
                NodeClass.Assignment => ((AssignmentExpressionSyntax)node).Left,
                NodeClass.ForEachVariable => ((ForEachVariableStatementSyntax)node).Variable,
                NodeClass.SimpleLambda or NodeClass.ParenthesizedLambda => ((LambdaExpressionSyntax)node).ExpressionBody,
                NodeClass.MemberInitializer => ((EqualsValueClauseSyntax)node).Value,
                _ => null,
            };
            this.Children = node.ChildNodesAndTokens().GetEnumerator();
        }

        internal void Add(SyntaxNode child, NodeClass childClass, in Metrics result)
        {
            this.Total.Statements += result.Statements;
            this.Total.Targets += result.Targets;
            this.Total.Elements += result.Elements;
            this.Total.Leaves += result.Leaves;
            switch (childClass)
            {
                case NodeClass.Attribute:
                case NodeClass.AttributeList:
                    this.Total.Exemptions |= result.Exemptions;
                    break;
                case NodeClass.Block:
                    // A function's block is its body. Error recovery can also attach an expression body; both are measured.
                    if (this.Class is >= NodeClass.Method and <= NodeClass.AnonymousMethod)
                    {
                        var block = (BlockSyntax)child;
                        this.AddUnit(result.Statements, result.Elements - Count(block.OpenBraceToken) - Count(block.CloseBraceToken));
                    }

                    break;
                case NodeClass.ArrowExpressionClause:
                    // The clause already formed its synthetic statement; its arrow belongs to the wrapper.
                    this.AddUnit(result.Statements, result.Elements - Count(((ArrowExpressionClauseSyntax)child).ArrowToken));
                    break;
                case NodeClass.ConstructorInitializer:
                    this.AddUnit(result.Statements, result.Elements - Count(((ConstructorInitializerSyntax)child).ColonToken));
                    break;
                case NodeClass.GlobalStatement:
                    this.AddUnit(result.Statements, result.Elements);
                    break;
                default:
                    if (ReferenceEquals(child, this.KeyNode))
                    {
                        this.AddKey(result.Statements, result.Targets, result.Elements, result.Leaves);
                    }

                    break;
            }
        }

        internal void AddLeaf(SyntaxNode child, NodeClass childClass, int present)
        {
            var leaves = childClass == NodeClass.Operand ? present : 0;
            var targets = childClass == NodeClass.Target ? present : 0;
            this.Total.Targets += targets;
            this.Total.Elements += present;
            this.Total.Leaves += leaves;
            if (ReferenceEquals(child, this.KeyNode))
            {
                this.AddKey(0, targets, present, leaves);
            }
        }

        private void AddKey(long statements, long targets, long elements, int leaves)
        {
            if (this.Class is NodeClass.Assignment or NodeClass.ForEachVariable)
            {
                this.LeftLeaves = leaves;
            }
            else
            {
                // A lambda expression body or a member initializer value forms one synthetic ordinary statement.
                this.AddUnit(statements + Math.Max(1, targets), elements);
            }
        }

        private void AddUnit(long statements, long elements)
        {
            this.UnitStatements += statements;
            this.UnitElements += elements;
            this.HasUnit = true;
        }
    }
}
