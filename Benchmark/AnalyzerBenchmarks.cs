using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Leash;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Benchmark;

[MemoryDiagnoser]
public class AnalyzerBenchmarks
{
    private static readonly AnalyzerSettings Limits = new(int.MaxValue, int.MaxValue, int.MaxValue, true);
    private static readonly ImmutableArray<DiagnosticAnalyzer> Analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new LeashAnalyzer());
    private SyntaxNode root = null!;
    private SyntaxNode[] retained = null!;
    private CSharpCompilation compilation = null!;

    [Params(10, 100, 1000)]
    public int MethodCount { get; set; }

    [Params(CodeShape.Flat, CodeShape.Dense, CodeShape.Nested)]
    public CodeShape Shape { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var text = new StringBuilder("class C {");
        for (var i = 0; i < MethodCount; i++)
        {
            text.Append("void M").Append(i).Append("() {");
            switch (Shape)
            {
                case CodeShape.Flat:
                    for (var j = 0; j < 20; j++)
                        text.Append("F();");
                    break;
                case CodeShape.Dense:
                    text.Append("F(0");
                    for (var j = 0; j < 200; j++)
                        text.Append("+1");
                    text.Append(");");
                    break;
                case CodeShape.Nested:
                    for (var j = 0; j < 32; j++)
                        text.Append("void L").Append(j).Append("() {");
                    text.Append("F();");
                    text.Append('}', 32);
                    break;
            }
            text.Append('}');
        }
        text.Append("void F(int value = 0) {} }");
        var tree = CSharpSyntaxTree.ParseText(text.ToString(), new CSharpParseOptions(LanguageVersion.CSharp13));
        root = tree.GetRoot();
        // Roslyn holds block statement lists weakly. Keeping every red node alive prevents the garbage
        // collector from discarding and re-materializing subtrees between iterations, so the traversal
        // benchmark measures the engine rather than Roslyn's lazy node creation.
        retained = root.DescendantNodes().ToArray();
        compilation = CSharpCompilation.Create("Benchmark", new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        // Warm the reusable traversal buffer outside timing.
        AnalysisEngine.Analyze(root, Limits, static _ => { });
    }

    [Benchmark(Description = "Traverse a parsed tree (no diagnostics)")]
    public long TraverseParsedTree() => AnalysisEngine.Analyze(root, Limits, static _ => { });

    [Benchmark(Description = "Roslyn driver with default limits")]
    public Task<ImmutableArray<Diagnostic>> AnalyzeWithRoslyn() => compilation.WithAnalyzers(Analyzers).GetAnalyzerDiagnosticsAsync();

    public enum CodeShape { Flat, Dense, Nested }
}
