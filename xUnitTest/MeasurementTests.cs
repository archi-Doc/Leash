using System;
using System.Linq;
using System.Threading;
using Leash;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace xUnitTest;

public sealed class MeasurementTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("; {} label: ;", 0)]
    [InlineData("Run();", 1)]
    [InlineData("Prepare(); Run(); Finish();", 3)]
    [InlineData("int x = 0;", 1)]
    [InlineData("int p = 1, q = 2;", 2)]
    [InlineData("x = y = 0;", 2)]
    [InlineData("x += y++;", 2)]
    [InlineData("var (a, b) = GetPair();", 2)]
    [InlineData("(int a, int b) = GetPair();", 2)]
    [InlineData("(a, _) = GetPair();", 2)]
    [InlineData("(a, (_, c)) = GetTuple();", 3)]
    [InlineData("var (a, (_, c)) = GetTuple();", 3)]
    [InlineData("(int a, b) = GetPair();", 2)]
    [InlineData("var c = new C { A = 1 };", 2)]
    [InlineData("var c = new C { A = x = 1, B = 2 };", 4)]
    [InlineData("var c = old with { A = 1, B = 2 };", 3)]
    [InlineData("var c = new D { [x++] = 1 };", 3)]
    [InlineData("return x = Calculate();", 1)]
    [InlineData("return x = y = Calculate();", 2)]
    [InlineData("F(out var a, out var b);", 2)]
    [InlineData("F(out a, ref b);", 1)]
    [InlineData("if (ok) { A(); B(); }", 3)]
    [InlineData("if (x is int a) A(); else if (y is int b) B(); else C();", 7)]
    [InlineData("for (int i = 0; i < 3; i++) { Run(); }", 4)]
    [InlineData("for (i = j = 0; Test(out var k); i++, j++) Run();", 7)]
    [InlineData("foreach (var item in source) Run();", 3)]
    [InlineData("foreach (var (a, b) in source) Run();", 4)]
    [InlineData("foreach ((a, b) in source) Run();", 4)]
    [InlineData("while (Test(out var x)) Run();", 3)]
    [InlineData("do { Run(); } while (x++ < 10);", 3)]
    [InlineData("using (var a = A()) Run();", 3)]
    [InlineData("using var a = A();", 1)]
    [InlineData("lock (x = GetLock()) Run();", 3)]
    [InlineData("fixed (int* p = &x) Run();", 3)]
    [InlineData("checked { x++; } unchecked { x--; } unsafe { Run(); }", 6)]
    [InlineData("try { A(); } catch (Exception ex) when (F(out var x)) { B(); } finally { C(); }", 8)]
    [InlineData("try {} catch {}", 2)]
    [InlineData("switch (x) { case 1: case 2: break; default: return; }", 5)]
    [InlineData("switch (x) { case int a when F(out var b): break; }", 5)]
    [InlineData("return x switch { int a when F(out var b) => a, _ => 0 };", 2)]
    [InlineData("return ok ? F() : G();", 1)]
    [InlineData("throw new Exception();", 1)]
    [InlineData("yield return 1; yield break;", 2)]
    [InlineData("goto label; label: return;", 2)]
    public void CountsWeightedStatements(string source, long expected) => Assert.Equal(expected, AnalyzerTest.Method(source).Statements);

    [Theory]
    [InlineData("", 0, 0)]
    [InlineData("x = a + 1;", 6, 1)]
    [InlineData("return F(1, 2, 3);", 10, 1)]
    [InlineData("return F(-1, 2, 3);", 11, 2)]
    [InlineData("return \"a very long literal with + = ; symbols\";", 3, 1)]
    [InlineData("if (ok) { A(); B(); }", 14, 3)]
    [InlineData("return this;", 3, 1)]
    [InlineData("x = base.y;", 6, 1)]
    public void CountsElementsAndRoundsOnce(string source, long elements, long lines)
    {
        var measurement = AnalyzerTest.Method(source);
        Assert.Equal(elements, measurement.Elements);
        Assert.Equal(lines, measurement.LogicalLines);
    }

    [Theory]
    [InlineData("class C { int M() => 1; }", 1, 1)]
    [InlineData("class C { int M() { return 1; } }", 1, 3)]
    [InlineData("class C { int value = Load(); }", 1, 3)]
    [InlineData("class C { int Value { get; } = Load(); }", 1, 3)]
    [InlineData("class C { C() : this(0) {} }", 1, 4)]
    [InlineData("class C { C() : base(x = y = 0) { Run(); } }", 3, 12)]
    [InlineData("class C(int x) : Base<int>(x) {}", 1, 7)]
    [InlineData("class C { int value = other; }", 1, 1)]
    [InlineData("class C { object M() => this; }", 1, 1)]
    public void UsesTheCorrectUnitRange(string source, long statements, long elements)
    {
        var measurement = Assert.Single(AnalyzerTest.Measure(source));
        Assert.Equal(statements, measurement.Statements);
        Assert.Equal(elements, measurement.Elements);
    }

    [Fact]
    public void NestedBodiesAreCountedOnceAndWrappersBelongToParents()
    {
        var measurements = AnalyzerTest.Measure("class C { void Outer() { void Inner() { Run(); } Inner(); } }");
        var inner = Assert.Single(measurements, m => m.Node is LocalFunctionStatementSyntax);
        var outer = Assert.Single(measurements, m => m.Node is MethodDeclarationSyntax);
        Assert.Equal((1L, 4L, 1L), (inner.Statements, inner.Elements, inner.LogicalLines));
        Assert.Equal((3L, 14L, 3L), (outer.Statements, outer.Elements, outer.LogicalLines));
    }

    [Fact]
    public void NestedInclusionIsTransitive()
    {
        var measurements = AnalyzerTest.Measure("class C { void M() { void A() { void B() { Run(); } } } }");
        Assert.Equal(new long[] { 1, 2, 3 }, measurements.Select(m => m.Statements));
        Assert.Equal(new long[] { 4, 10, 16 }, measurements.Select(m => m.Elements));
    }

    [Theory]
    [InlineData("x => F(x)", 4)]
    [InlineData("x => x", 1)]
    [InlineData("x => { return F(x); }", 6)]
    [InlineData("delegate(int x) { return F(x); }", 6)]
    public void LambdaAndAnonymousBodiesHaveTheSameStatementWeight(string expression, long elements)
    {
        var measurements = AnalyzerTest.Measure("class C { void M() { var f = " + expression + "; } }");
        var nested = Assert.Single(measurements, m => m.Node is AnonymousFunctionExpressionSyntax);
        Assert.Equal(1, nested.Statements);
        Assert.Equal(elements, nested.Elements);
        Assert.Equal(2, Assert.Single(measurements, m => m.Node is MethodDeclarationSyntax).Statements);
    }

    [Fact]
    public void SignaturesAndAttributesAreOnlyElementsOfTheParent()
    {
        var source = "class C { [Some(1, 2)] int M(int p = 0) { [A(3)] int L(int x = 1) => x; return L(); } }";
        var measurements = AnalyzerTest.Measure(source);
        var local = Assert.Single(measurements, m => m.Node is LocalFunctionStatementSyntax);
        Assert.Equal((1L, 1L), (local.Statements, local.Elements));
        var method = Assert.Single(measurements, m => m.Node is MethodDeclarationSyntax);
        var syntax = (MethodDeclarationSyntax)method.Node;
        Assert.Equal(syntax.Body!.DescendantTokens().LongCount(t => !t.IsMissing) - 2, method.Elements);
    }

    [Fact]
    public void AllExecutableMemberKindsHaveUnitsButAutoAccessorsDoNot()
    {
        const string source = """
            interface I { void M() { F(); } int P => F(); }
            class C {
                static C() { F(); }
                C() { F(); }
                ~C() { F(); }
                public static C operator +(C a, C b) => a;
                public static implicit operator int(C c) => 1;
                int P => 1;
                int this[int i] => i;
                int Q { get { return 1; } set { F(); } }
                int Auto { get; set; }
                event Action E { add { F(); } remove { F(); } }
                int x = F(), y = G();
            }
            """;
        var measurements = AnalyzerTest.Measure(source);
        Assert.Equal(15, measurements.Count);
        Assert.All(measurements, m => Assert.Equal(1, m.Statements));
    }

    [Fact]
    public void TopLevelCodeExcludesOtherMembersAndUsings()
    {
        var measurements = AnalyzerTest.Measure("using System; F(); void L() { G(); } L(); class C { void M() { H(); } }");
        var top = Assert.Single(measurements, m => m.Node is CompilationUnitSyntax);
        Assert.Equal((4L, 18L), (top.Statements, top.Elements));
        Assert.Equal(3, measurements.Count);
    }

    [Fact]
    public void MemberInitializersRemainSeparateFromAllConstructors()
    {
        var measurements = AnalyzerTest.Measure("class C(int x) : B(x) { int f = F(), g = G(); int P {get;} = H(); C() : this(0) {} }");
        Assert.Equal(5, measurements.Count);
        Assert.All(measurements, m => Assert.Equal(1, m.Statements));
    }

    [Fact]
    public void TriviaAndDisabledTextDoNotChangeMeasurements()
    {
        var plain = AnalyzerTest.Method("int x=0; F(x);");
        var changed = AnalyzerTest.Method("""
            // comment
            int /* more */ x = 0;
            #if NEVER
            F(); F(); F();
            #endif
            F (
                x );
            """);
        Assert.Equal((plain.Statements, plain.Elements), (changed.Statements, changed.Elements));
    }

    [Theory]
    [InlineData("var x = $\"value: {F(a + 1)}\";")]
    [InlineData("var x = \"\"\"a raw literal\"\"\";")]
    [InlineData("var x = \"bytes\"u8;")]
    [InlineData("return F( ;")]
    public void ElementsFollowActualNonMissingTokens(string source)
    {
        var measurement = AnalyzerTest.Method(source);
        var body = ((MethodDeclarationSyntax)measurement.Node).Body!;
        var expected = body.DescendantTokens().Count(t => !t.IsMissing) - (body.OpenBraceToken.IsMissing ? 0 : 1) - (body.CloseBraceToken.IsMissing ? 0 : 1);
        Assert.Equal(expected, measurement.Elements);
    }

    [Theory]
    [InlineData("class C { int M() { return 1; } => 2; }", 2, 4)]
    [InlineData("class C { C() : base(1) { F(); } => G(); }", 3, 11)]
    [InlineData("class C { int P { get { return 1; } => 2; } }", 2, 4)]
    public void ErrorRecoveryKeepsBothBlockAndExpressionBodies(string source, long statements, long elements)
    {
        var measurement = Assert.Single(AnalyzerTest.Measure(source));
        Assert.Equal((statements, elements), (measurement.Statements, measurement.Elements));
    }

    [Theory]
    [InlineData("(a, ) = F();", 1)]
    [InlineData("(a, b, ) = F();", 2)]
    [InlineData("( , ) = F();", 1)]
    public void MissingIdentifiersAreNotTargets(string source, long statements) =>
        Assert.Equal(statements, AnalyzerTest.Method(source).Statements);

    [Fact]
    public void TraversingARetainedTreeDoesNotAllocate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = "class C(int p) : B(p) { int f = 1; int P => f; void M(int a) { var (x, y) = (a, 2); foreach (var i in L) { if (i is int k) x += k; } " +
            "F(z => z + 1, delegate { return 0; }, $\"{x}\"); void L2() => G(); } }";
        var root = AnalyzerTest.Parse(source).GetRoot(cancellationToken);

        // Roslyn holds some red nodes weakly; retaining them keeps the second traversal free of node materialization.
        var retained = root.DescendantNodesAndTokens().ToArray();
        Action<Diagnostic> report = static _ => { };
        var expected = AnalysisEngine.Analyze(root, AnalyzerTest.Unlimited, report, cancellationToken);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var actual = AnalysisEngine.Analyze(root, AnalyzerTest.Unlimited, report, cancellationToken);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(retained);
        Assert.Equal(expected, actual);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ExplicitStackHandlesDeepTrees()
    {
        StatementSyntax body = SyntaxFactory.ParseStatement("F();");
        for (var i = 0; i < 10000; i++)
            body = SyntaxFactory.Block(body);
        var method = SyntaxFactory.MethodDeclaration(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)), "M")
            .WithBody((BlockSyntax)body);
        Measurement actual = default;
        AnalysisEngine.Analyze(method, AnalyzerTest.Unlimited, _ => Assert.Fail(), TestContext.Current.CancellationToken, observe: m => actual = m);
        Assert.Equal(1, actual.Statements);
        Assert.Equal(20002, actual.Elements);
    }

    [Fact]
    public void CancellationDuringTraversalReturnsItsBuffers()
    {
        var root = AnalyzerTest.Parse("class C {" + string.Concat(Enumerable.Range(0, 1000).Select(i => $"void M{i}() {{ F(); }}")) + "}").GetRoot(TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => AnalysisEngine.Analyze(root, AnalyzerTest.Unlimited, _ => { },
            cancellation.Token, _ => cancellation.Cancel()));
        Assert.Equal(1000, AnalysisEngine.Analyze(root, AnalyzerTest.Unlimited, _ => { }, TestContext.Current.CancellationToken));
        Assert.Throws<OperationCanceledException>(() => AnalysisEngine.Analyze(root, AnalyzerTest.Unlimited, _ => { }, cancellation.Token));
    }

    [Fact]
    public void ReentrantAnalysisFromCallbacksIsIsolated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var outer = AnalyzerTest.Parse("class C { void M() { F(); G(); } }").GetRoot(cancellationToken);
        var inner = AnalyzerTest.Parse("class D { void N() { H(); } int x = 1; }").GetRoot(cancellationToken);
        long innerChecksum = 0;
        var outerChecksum = AnalysisEngine.Analyze(outer, AnalyzerTest.Unlimited, _ => Assert.Fail(), cancellationToken,
            _ => innerChecksum = AnalysisEngine.Analyze(inner, AnalyzerTest.Unlimited, _ => Assert.Fail(), cancellationToken));
        Assert.Equal(2, outerChecksum);
        Assert.Equal(2, innerChecksum);
    }
}
