using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Leash;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace xUnitTest;

public sealed class DiagnosticTests
{
    [Fact]
    public async Task ProducesTheExactSpecifiedDiagnostic()
    {
        var body = string.Concat(Enumerable.Repeat("Run();", 41)) + "Run(" + string.Join("+", Enumerable.Repeat("1", 203)) + ");";
        var diagnostic = Assert.Single(await AnalyzerTest.Run("class C { void Foo() { " + body + " } }"));
        Assert.Equal("LEASH0001", diagnostic.Id);
        Assert.Equal("'Foo' is 58 logical lines long (limit 50). Statements: 42, elements: 573.", diagnostic.GetMessage());
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Foo", diagnostic.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    public async Task LogicalLineLimitIsInclusive(int count, int errors)
    {
        var source = "class C { void M() {" + string.Concat(Enumerable.Repeat("F();", count)) + "} }";
        Assert.Equal(errors, (await AnalyzerTest.Run(source, AnalyzerTest.Options(("max_lines", "2")))).Length);
    }

    [Theory]
    [InlineData(5, 0)]
    [InlineData(6, 0)]
    [InlineData(7, 1)]
    public async Task ParameterLimitIsInclusive(int count, int errors)
    {
        var parameters = string.Join(",", Enumerable.Range(0, count).Select(i => "int p" + i));
        Assert.Equal(errors, (await AnalyzerTest.Run("interface I { void M(" + parameters + "); }")).Length);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public async Task OutLimitIsInclusive(int count, int errors)
    {
        var parameters = string.Join(",", Enumerable.Range(0, count).Select(i => "out int p" + i));
        Assert.Equal(errors, (await AnalyzerTest.Run("interface I { void M(" + parameters + "); }")).Length);
    }

    [Fact]
    public async Task OutParametersAlsoCountTowardTheTotal()
    {
        var diagnostics = await AnalyzerTest.Run("class C { void Read(int a,int b,int c,int d,int e,out int x,out int y) {} }");
        Assert.Equal(new[] { "LEASH0002", "LEASH0003" }, diagnostics.Select(d => d.Id));
        Assert.Equal("'Read' has 7 parameters (limit 6).", diagnostics[0].GetMessage());
        Assert.Equal("'Read' has 2 out parameters (limit 1).", diagnostics[1].GetMessage());
    }

    [Theory]
    [InlineData("delegate void D(int x);", "D")]
    [InlineData("record R(int x);", "R")]
    [InlineData("class C(int x) {}", "C")]
    [InlineData("class C { C(int x) {} }", "C")]
    [InlineData("interface I { void M(int x); }", "M")]
    [InlineData("class C { int this[int x] => x; }", "this")]
    [InlineData("class C { static C operator +(C x, C y) => x; }", "operator +")]
    [InlineData("class C { static implicit operator int(C x) => 0; }", "implicit operator int")]
    [InlineData("class C { void M() { void L(int x) {} } }", "L")]
    [InlineData("class C { object f = x => x; }", "lambda")]
    [InlineData("class C { object f = (int x) => x; }", "lambda")]
    [InlineData("class C { object f = delegate(int x) {}; }", "anonymous method")]
    [InlineData("class C { static C operator checked +(C x, C y) => x; }", "operator checked +")]
    [InlineData("class C { static explicit operator checked int(C x) => 0; }", "explicit operator checked int")]
    public async Task CoversExplicitParameterListKinds(string source, string name)
    {
        var diagnostic = Assert.Single(await AnalyzerTest.Run(source, AnalyzerTest.Options(("max_parameters", "0"))));
        Assert.Equal("LEASH0002", diagnostic.Id);
        Assert.StartsWith("'" + name + "' has ", diagnostic.GetMessage());
    }

    [Fact]
    public async Task OperatorNamesStayOnOneLine()
    {
        const string source = """
            class C
            {
                static implicit operator System.Collections.Generic.Dictionary<
                    string, // key
                    int>(C x) => null;
            }
            """;
        var diagnostic = Assert.Single(await AnalyzerTest.Run(source, AnalyzerTest.Options(("max_parameters", "0"))));
        Assert.Equal("'implicit operator System.Collections.Generic.Dictionary< string, int>' has 1 parameters (limit 0).", diagnostic.GetMessage());
    }

    [Fact]
    public async Task DoesNotInferAnonymousOrAccessorParametersOrExpandTypes()
    {
        const string source = "class C<T> { object f = delegate {}; int P { set { F(value); } } delegate*<int,int,void> p; void M((int,int) x) {} }";
        Assert.Empty(await AnalyzerTest.Run(source, AnalyzerTest.Options(("max_parameters", "1"))));
    }

    [Fact]
    public async Task ModifiersAndOptionalParametersStillCountOnce()
    {
        const string source = "static class C { static void M(this C x, ref int a, in int b, out int c, int d = 0, params int[] e) {} }";
        Assert.Empty(await AnalyzerTest.Run(source));
        var diagnostic = Assert.Single(await AnalyzerTest.Run(source, AnalyzerTest.Options(("max_parameters", "5"))));
        Assert.Contains("6 parameters", diagnostic.GetMessage());
    }

    [Fact]
    public async Task PartialDeclarationsAndExternalContractsAreCheckedSeparately()
    {
        const string source = """
            partial class C {
                partial void M(int x);
                partial void M(int x) {}
                public override void N(int x) {}
                void I.P(int x) {}
                [System.Runtime.InteropServices.DllImport("native")] static extern void E(int x);
            }
            """;
        Assert.Equal(5, (await AnalyzerTest.Run(source, AnalyzerTest.Options(("max_parameters", "0")))).Length);
    }

    [Theory]
    [InlineData("class C { int value = x = y = 0; }", "value initializer", "value")]
    [InlineData("class C { int P {get;} = x = y = 0; }", "P initializer", "P")]
    [InlineData("class C { int P { get { F(); G(); return 0; } } }", "P.get", "get")]
    [InlineData("class C { object f = () => { F(); G(); }; }", "lambda", "=>")]
    [InlineData("using System; F(); G();", "top-level statements", "F")]
    public async Task UsesSourceBasedNamesAndPreciseLocations(string source, string name, string token)
    {
        var diagnostics = await AnalyzerTest.Run(source, AnalyzerTest.Options(("max_lines", "1")));
        var diagnostic = Assert.Single(diagnostics, d => d.GetMessage().StartsWith("'" + name + "'", StringComparison.Ordinal));
        Assert.Equal(token, diagnostic.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
    }

    [Theory]
    [InlineData(ReportDiagnostic.Suppress)]
    [InlineData(ReportDiagnostic.Warn)]
    [InlineData(ReportDiagnostic.Info)]
    public async Task SeverityOverridesAndNoWarnCannotChangeErrors(ReportDiagnostic severity)
    {
        var diagnostic = Assert.Single(await AnalyzerTest.Run("class C { void M(int x) {} }",
            AnalyzerTest.Options(("max_parameters", "0")), severity: severity));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.False(diagnostic.IsSuppressed);
    }

    [Fact]
    public async Task PragmasAndDiagnosticSuppressorsCannotSuppressErrors()
    {
        const string source = """
            #pragma warning disable LEASH0001, LEASH0002
            class C {
                void M(int x) { F(); G(); }
            }
            """;
        var diagnostics = await AnalyzerTest.Run(source, AnalyzerTest.Options(("max_lines", "1"), ("max_parameters", "0")),
            extraAnalyzer: new AttemptedSuppressor());
        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, d => { Assert.False(d.IsSuppressed); Assert.Equal(DiagnosticSeverity.Error, d.Severity); });
    }

    [Theory]
    [InlineData("SuppressMessage")]
    [InlineData("UnconditionalSuppressMessage")]
    public async Task RecordsRoslynAttributeSuppressionLimitation(string attribute)
    {
        // Roslyn 4.14 applies these attributes after analyzer callbacks, even to NotConfigurable errors.
        // Keep this regression visible: passing ordinary suppression tests must not imply an absolute guarantee.
        var source = "class C { [System.Diagnostics.CodeAnalysis." + attribute + "(\"Maintainability\", \"LEASH0002\")] void M(int x) {} }";
        var diagnostic = Assert.Single(await AnalyzerTest.Run(source, AnalyzerTest.Options(("max_parameters", "0"))));
        Assert.True(diagnostic.IsSuppressed);
        Assert.Contains(WellKnownDiagnosticTags.NotConfigurable, diagnostic.Descriptor.CustomTags);
    }

    [Fact]
    public async Task MessagesUseInvariantUngroupedNumbers()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var diagnostic = Assert.Single(await AnalyzerTest.Run("class C { void M(int x) {} }", AnalyzerTest.Options(("max_parameters", "0"))));
            Assert.Equal("'M' has 1 parameters (limit 0).", diagnostic.GetMessage());
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void DescriptorsContainHelpAndTheElementDefinition()
    {
        var analyzer = new LeashAnalyzer();
        Assert.Equal(5, analyzer.SupportedDiagnostics.Length);
        Assert.All(analyzer.SupportedDiagnostics, descriptor =>
        {
            Assert.True(descriptor.IsEnabledByDefault);
            Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity);
            Assert.Contains(WellKnownDiagnosticTags.NotConfigurable, descriptor.CustomTags);
            Assert.EndsWith("/" + descriptor.Id + ".md", descriptor.HelpLinkUri);
        });
        Assert.Contains(DiagnosticRules.ElementDefinition, DiagnosticRules.Size.Description.ToString());
    }

    [Fact]
    public async Task ExtensionBlockReceiversAreNotCallableParameterLists()
    {
        // The kind constant guards hosts newer than the Roslyn 4.14 compile-time baseline.
        Assert.Equal((int)SyntaxKind.ExtensionBlockDeclaration, AnalysisEngine.ExtensionBlockDeclarationKind);
        var tree = AnalyzerTest.Parse("static class E { extension(int receiver) { public void M(int x) {} } }", LanguageVersion.CSharp14);
        var diagnostic = Assert.Single(await AnalyzerTest.RunTrees(new[] { tree }, AnalyzerTest.Options(("max_parameters", "0"))));
        Assert.StartsWith("'M' has 1 parameters", diagnostic.GetMessage());
    }

    // Instantiated explicitly by tests; this is not a discoverable compiler extension.
#pragma warning disable RS1001
    private sealed class AttemptedSuppressor : DiagnosticSuppressor
    {
        private static readonly SuppressionDescriptor Suppression = new("SPR0001", "LEASH0002", "Attempt normal suppression");
        public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions => ImmutableArray.Create(Suppression);
        public override void ReportSuppressions(SuppressionAnalysisContext context)
        {
            foreach (var diagnostic in context.ReportedDiagnostics)
                if (diagnostic.Id == "LEASH0002")
                    context.ReportSuppression(Microsoft.CodeAnalysis.Diagnostics.Suppression.Create(Suppression, diagnostic));
        }
    }
#pragma warning restore RS1001
}
