using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Leash;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace xUnitTest;

public sealed class GeneratorTests
{
    [Fact]
    public void GeneratedAttributeCompilesOnEverySupportedTarget()
    {
        const string source = """
            using System;
            using Leash;

            [method: LeashExempt("Primary constructor.")]
            class C(int a)
            {
                [LeashExempt("Field.")] int f = a;
                [LeashExempt("Event field.")] event Action E = null!;
                [LeashExempt("Property.")] int P { [LeashExempt("Accessor.")] get => f; }
                [LeashExempt("Indexer.")] int this[int i] => i;
                [LeashExempt("Constructor.")] C() : this(0) { }
                [LeashExempt("Finalizer.")] ~C() { }
                [Leash.LeashExemptAttribute("Operator.")] public static C operator +(C x, C y) => x;
                [LeashExempt("Method.")]
                void M()
                {
                    [LeashExempt("Local function.")] void L() { }
                    Func<int, int> lambda = [LeashExempt("Lambda.")] (int x) => x;
                    L();
                    E?.Invoke();
                }
            }

            [LeashExempt("Delegate.")]
            delegate void D(int a);
            """;
        var (compilation, diagnostics) = Generate(AnalyzerTest.Parse(source));
        Assert.Empty(diagnostics);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity >= DiagnosticSeverity.Warning));
    }

    [Fact]
    public void GeneratedAttributeCompilesUnderOldLanguageVersions()
    {
        var tree = AnalyzerTest.Parse("class C { [Leash.LeashExempt(\"reason\")] void M() { } }", LanguageVersion.CSharp7_3);
        var (compilation, _) = Generate(tree);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity >= DiagnosticSeverity.Warning));
    }

    [Fact]
    public void ProjectDefinedAttributesStillCompile()
    {
        // Earlier versions asked projects to define the attribute in their own code; the generated type must not collide.
        const string source = """
            using Leash;

            public sealed class LeashExemptAttribute : System.Attribute
            {
                public LeashExemptAttribute(string reason) { }
            }

            class C { [LeashExempt("reason")] void M() { } }
            """;
        var (compilation, _) = Generate(AnalyzerTest.Parse(source));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity >= DiagnosticSeverity.Warning));
    }

    [Fact]
    public void GeneratedCopiesDoNotConflictAcrossAssemblies()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (first, _) = Generate(AnalyzerTest.Parse(
            "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Second\")] public class A { [Leash.LeashExempt(\"reason\")] public void M() { } }"),
            "First");
        using var image = new MemoryStream();
        Assert.True(first.Emit(image, cancellationToken: cancellationToken).Success);
        var reference = MetadataReference.CreateFromImage(image.ToArray());
        var (second, _) = Generate(AnalyzerTest.Parse("using Leash; class B : A { [LeashExempt(\"reason\")] void N() { } }"), "Second", reference);
        Assert.Empty(second.GetDiagnostics(cancellationToken).Where(d => d.Severity >= DiagnosticSeverity.Warning));
    }

    [Fact]
    public async Task GeneratedAttributeExemptsAndItsSourceIsNotAnalyzed()
    {
        var (compilation, _) = Generate(AnalyzerTest.Parse("using Leash; class C { [LeashExempt(\"External contract.\")] void M(int a, int b) { } void N(int a) { } }"));
        var options = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, new SingleOptionProvider("leash.max_parameters", "0"));
        var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new LeashAnalyzer()), options)
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        Assert.StartsWith("'N' has 1 parameters", Assert.Single(diagnostics).GetMessage());
    }

    private static (Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) Generate(
        SyntaxTree tree, string assemblyName = "GeneratorTest", MetadataReference? extraReference = null)
    {
        var references = extraReference is null ? AnalyzerTest.References : AnalyzerTest.References.Add(extraReference);
        var parseOptions = (CSharpParseOptions)tree.Options;
        var nullable = parseOptions.LanguageVersion >= LanguageVersion.CSharp8 ? NullableContextOptions.Enable : NullableContextOptions.Disable;
        var compilation = CSharpCompilation.Create(assemblyName, new[] { tree }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: nullable));
        var driver = CSharpGeneratorDriver.Create(new ExemptAttributeGenerator()).WithUpdatedParseOptions(parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics, TestContext.Current.CancellationToken);
        Assert.Contains(output.SyntaxTrees, generated => generated.FilePath.EndsWith(ExemptAttributeGenerator.HintName, System.StringComparison.Ordinal));
        return (output, diagnostics);
    }

    private sealed class SingleOptionProvider(string key, string value) : AnalyzerConfigOptionsProvider
    {
        private readonly Options options = new(key, value);

        public override AnalyzerConfigOptions GlobalOptions => this.options;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => this.options;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => this.options;

        private sealed class Options(string key, string value) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string name, out string result)
            {
                result = name == key ? value : null!;
                return name == key;
            }
        }
    }
}
