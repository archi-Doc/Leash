using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace xUnitTest;

public sealed class ExemptionTests
{
    [Theory]
    [InlineData("LeashExempt(\"reason\")")]
    [InlineData("LeashExemptAttribute(\"reason\")")]
    [InlineData("global::Some.LeashExempt(\"reason\")")]
    [InlineData("Alias::LeashExempt(\"reason\")")]
    [InlineData("LeashExempt(@\"reason\")")]
    [InlineData("LeashExempt(\"\"\"reason\"\"\")")]
    [InlineData("LeashExempt<int>(\"reason\")")]
    [InlineData("Some.LeashExemptAttribute<int>(\"reason\")")]
    public async Task MatchesWrittenNamesAndLiteralReasons(string attribute)
    {
        Assert.Empty(await AnalyzerTest.Run("class C { [" + attribute + "] void M(int x) { F(); G(); } }",
            AnalyzerTest.Options(("max_parameters", "0"), ("max_lines", "1"))));
    }

    [Theory]
    [InlineData("LeashExempt")]
    [InlineData("LeashExempt()")]
    [InlineData("LeashExempt(\"\")")]
    [InlineData("LeashExempt(\" \\t\\r\\n\")")]
    [InlineData("LeashExempt($\"reason\")")]
    [InlineData("LeashExempt(\"reason\"u8)")]
    [InlineData("LeashExempt(Reason)")]
    [InlineData("LeashExempt(\"rea\" + \"son\")")]
    [InlineData("LeashExempt(reason: \"reason\")")]
    [InlineData("LeashExempt(Reason = \"reason\")")]
    [InlineData("LeashExempt(\"a\", \"b\")")]
    [InlineData("LeashExempt(null)")]
    [InlineData("LeashExempt<int>(\"\")")]
    public async Task InvalidReasonsDoNotExempt(string attribute)
    {
        var diagnostics = await AnalyzerTest.Run("class C { [" + attribute + "] void M(int x) {} }",
            AnalyzerTest.Options(("max_parameters", "0")));
        Assert.Equal(new[] { "LEASH0005", "LEASH0002" }, diagnostics.Select(d => d.Id));
    }

    [Theory]
    [InlineData("[LeashExempt(\"reason\")] class C {}")]
    [InlineData("[assembly: LeashExempt(\"reason\")] class C {}")]
    [InlineData("[module: LeashExempt(\"reason\")] class C {}")]
    [InlineData("class C { [return: LeashExempt(\"reason\")] int M() => 1; }")]
    [InlineData("class C { void M([LeashExempt(\"reason\")] int x) {} }")]
    public async Task InvalidAttributeTargetsAreDiagnosed(string source)
    {
        var diagnostic = Assert.Single(await AnalyzerTest.Run(source));
        Assert.Equal("LEASH0005", diagnostic.Id);
        Assert.Contains("unsupported target", diagnostic.GetMessage());
    }

    [Fact]
    public async Task DisabledExemptionsAreRejectedOncePerAttribute()
    {
        var diagnostics = await AnalyzerTest.Run("class C { [LeashExempt(\"reason\")] void M(int x) {} }",
            AnalyzerTest.Options(("allow_exempt", "false"), ("max_parameters", "0")));
        Assert.Equal(new[] { "LEASH0005", "LEASH0002" }, diagnostics.Select(d => d.Id));
        Assert.Contains("disabled", diagnostics[0].GetMessage());
    }

    [Fact]
    public async Task AliasesAreNotResolved()
    {
        var diagnostics = await AnalyzerTest.Run("using Exempt = LeashExemptAttribute; class C { [Exempt(\"reason\")] void M(int x) {} }",
            AnalyzerTest.Options(("max_parameters", "0")));
        Assert.Equal("LEASH0002", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task InvalidDuplicateVetoesTheDeclarationExemption()
    {
        var diagnostics = await AnalyzerTest.Run("class C { [LeashExempt(\"reason\"), LeashExempt(\"\")] void M(int x) {} }",
            AnalyzerTest.Options(("max_parameters", "0")));
        Assert.Equal(new[] { "LEASH0005", "LEASH0002" }, diagnostics.Select(d => d.Id));
    }

    [Fact]
    public async Task ParentExemptionDoesNotExemptLocalFunctions()
    {
        var diagnostics = await AnalyzerTest.Run("class C { [LeashExempt(\"reason\")] void M() { void L() { F(); G(); } } }",
            AnalyzerTest.Options(("max_lines", "1")));
        Assert.StartsWith("'L'", Assert.Single(diagnostics).GetMessage());
    }

    [Fact]
    public async Task ExemptLocalFunctionStillContributesToTheParent()
    {
        var diagnostics = await AnalyzerTest.Run("class C { void M() { [LeashExempt(\"reason\")] void L() { F(); G(); } } }",
            AnalyzerTest.Options(("max_lines", "1")));
        var diagnostic = Assert.Single(diagnostics);
        Assert.StartsWith("'M'", diagnostic.GetMessage());
        Assert.Contains("Statements: 3", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("class C { [LeashExempt(\"reason\")] int P {get {F(); G(); return 0;} set {F(); G();}} = x = y = 0; }")]
    [InlineData("class C { [LeashExempt(\"reason\")] int this[int a,int b] {get {F(); G(); return 0;} set {F(); G();}} }")]
    [InlineData("class C { [LeashExempt(\"reason\")] int a = x = y = 0, b = x = y = 0; }")]
    [InlineData("[LeashExempt(\"reason\")] delegate void D(int a,int b);")]
    public async Task MemberExemptionsCoverOnlyTheirAssociatedTargets(string source) =>
        Assert.Empty(await AnalyzerTest.Run(source, AnalyzerTest.Options(("max_lines", "1"), ("max_parameters", "0"))));

    [Fact]
    public async Task MemberExemptionsDoNotLeakIntoInitializerLambdas()
    {
        var diagnostics = await AnalyzerTest.Run("class C { [LeashExempt(\"reason\")] object f = () => { F(); G(); }; }",
            AnalyzerTest.Options(("max_lines", "1")));
        Assert.StartsWith("'lambda'", Assert.Single(diagnostics).GetMessage());
    }

    [Fact]
    public async Task PrimaryConstructorExemptionDoesNotExemptTypeMembers()
    {
        var diagnostics = await AnalyzerTest.Run("[method: LeashExempt(\"reason\")] class C(int x) : B(x = y = 0) { void M(int x) {} }",
            AnalyzerTest.Options(("max_lines", "1"), ("max_parameters", "0")));
        Assert.StartsWith("'M'", Assert.Single(diagnostics).GetMessage());
    }

    [Fact]
    public async Task AttributedLambdaCanBeExemptedIndependently()
    {
        var diagnostics = await AnalyzerTest.Run("class C { object f = [LeashExempt(\"reason\")] (int x) => { F(); G(); }; }",
            AnalyzerTest.Options(("max_lines", "1"), ("max_parameters", "0")));
        Assert.StartsWith("'f initializer'", Assert.Single(diagnostics).GetMessage());
    }

    [Fact]
    public async Task ExemptionsCannotHideConfigurationOrAttributeErrors()
    {
        var diagnostics = await AnalyzerTest.Run("class C { [LeashExempt(\"reason\")] void M([LeashExempt(\"\")] int x) {} }",
            AnalyzerTest.Options(("max_lines", "bad"), ("max_parameters", "0")));
        Assert.Equal(new[] { "LEASH0004", "LEASH0005" }, diagnostics.Select(d => d.Id));
    }

    [Theory]
    [InlineData("[LeashExempt(\"reason\")] enum E { A }", "E")]
    [InlineData("enum E { [LeashExempt(\"reason\")] A }", "A")]
    [InlineData("class C<[LeashExempt(\"reason\")] T> {}", "T")]
    [InlineData("class C { void M([LeashExempt(\"reason\")] int x) {} }", "x")]
    public async Task UnsupportedTargetsAreNamedByTheirDeclaration(string source, string name)
    {
        var diagnostic = Assert.Single(await AnalyzerTest.Run(source));
        Assert.Equal("LEASH0005", diagnostic.Id);
        Assert.StartsWith($"Invalid exemption on '{name}': unsupported target", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("method: ")]
    [InlineData("")]
    public async Task ExtensionBlocksCannotBeExempted(string target)
    {
        // An extension block's receiver is not a primary constructor, so not even a method-targeted attribute applies.
        var tree = AnalyzerTest.Parse("static class E { [" + target + "LeashExempt(\"reason\")] extension(int receiver) { public void M(int x) {} } }",
            Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp14);
        var diagnostics = await AnalyzerTest.RunTrees(new[] { tree }, AnalyzerTest.Options(("max_parameters", "0")));
        Assert.Equal(new[] { "LEASH0005", "LEASH0002" }, diagnostics.Select(d => d.Id));
        Assert.StartsWith("Invalid exemption on 'extension': unsupported target", diagnostics[0].GetMessage());
        Assert.StartsWith("'M' has 1 parameters", diagnostics[1].GetMessage());
    }

    [Theory]
    [InlineData("assembly")]
    [InlineData("module")]
    public async Task AssemblyLevelAttributesAreNamedByTheirTarget(string target)
    {
        var diagnostic = Assert.Single(await AnalyzerTest.Run($$"""[{{target}}: LeashExempt("reason")] class C {}"""));
        Assert.Equal("LEASH0005", diagnostic.Id);
        Assert.StartsWith($"Invalid exemption on '{target}'", diagnostic.GetMessage());
    }
}
