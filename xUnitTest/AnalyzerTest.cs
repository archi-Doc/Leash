using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Leash;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace xUnitTest;

internal static class AnalyzerTest
{
    internal static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp13);
    internal static readonly AnalyzerSettings Unlimited = new(null, null, null, true);
    internal static readonly ImmutableArray<MetadataReference> References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToImmutableArray();

    internal static SyntaxTree Parse(string source, string path = "Test.cs") =>
        CSharpSyntaxTree.ParseText(source, ParseOptions, path, cancellationToken: TestContext.Current.CancellationToken);

    internal static SyntaxTree Parse(string source, LanguageVersion version, string path = "Test.cs") =>
        CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(version), path, cancellationToken: TestContext.Current.CancellationToken);

    internal static List<Measurement> Measure(string source)
    {
        var measurements = new List<Measurement>();
        var cancellationToken = TestContext.Current.CancellationToken;
        AnalysisEngine.Analyze(Parse(source).GetRoot(cancellationToken), Unlimited, diagnostic => Assert.Fail(diagnostic.ToString()), cancellationToken, observe: measurements.Add);
        return measurements;
    }

    internal static Measurement Method(string statements) =>
        Assert.Single(Measure("class C { void M() { " + statements + " } }"));

    internal static Dictionary<string, string> Options(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(pair => "leash." + pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    internal static Task<ImmutableArray<Diagnostic>> Run(string source, Dictionary<string, string>? options = null,
        string path = "Test.cs", bool generated = false, ReportDiagnostic? severity = null,
        DiagnosticAnalyzer? extraAnalyzer = null) => RunTrees(new[] { Parse(source, path) }, options, generated, severity, extraAnalyzer);

    internal static async Task<ImmutableArray<Diagnostic>> RunTrees(SyntaxTree[] trees, Dictionary<string, string>? options = null,
        bool generated = false, ReportDiagnostic? severity = null, DiagnosticAnalyzer? extraAnalyzer = null)
    {
        var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true)
            .WithSyntaxTreeOptionsProvider(generated ? new GeneratedOptions() : null);
        if (severity is { } configured)
            compilationOptions = compilationOptions.WithSpecificDiagnosticOptions(
                DiagnosticRules.All.ToDictionary(rule => rule.Id, _ => configured));
        var compilation = CSharpCompilation.Create("AnalyzerTest", trees, References, compilationOptions);
        if (generated)
        {
            options = options is null ? new Dictionary<string, string>() : new Dictionary<string, string>(options);
            options["generated_code"] = "true";
        }
        var analyzerOptions = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, new OptionsProvider(options));
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new LeashAnalyzer());
        if (extraAnalyzer is not null)
            analyzers = analyzers.Add(extraAnalyzer);
        var diagnostics = await compilation.WithAnalyzers(analyzers,
            new CompilationWithAnalyzersOptions(analyzerOptions, onAnalyzerException: null, concurrentAnalysis: true,
                logAnalyzerExecutionTime: false, reportSuppressedDiagnostics: true)).GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AD0001");
        return diagnostics.OrderBy(d => d.Location.SourceTree?.FilePath).ThenBy(d => d.Location.SourceSpan.Start)
            .ThenBy(d => d.Id).ToImmutableArray();
    }

    private sealed class OptionsProvider(Dictionary<string, string>? values) : AnalyzerConfigOptionsProvider
    {
        private readonly TestConfig options = new(values ?? new Dictionary<string, string>());
        public override AnalyzerConfigOptions GlobalOptions => options;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => options;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => options;
    }

    private sealed class TestConfig(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
    }

    private sealed class GeneratedOptions : SyntaxTreeOptionsProvider
    {
        public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken) => GeneratedKind.MarkedGenerated;
        public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken cancellationToken,
            out ReportDiagnostic severity) { severity = default; return false; }
        public override bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken cancellationToken,
            out ReportDiagnostic severity) { severity = default; return false; }
    }
}
