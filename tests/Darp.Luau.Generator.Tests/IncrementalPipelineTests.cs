using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using Shouldly;

namespace Darp.Luau.Generator.Tests;

public sealed class IncrementalPipelineTests
{
    [Fact]
    public async Task GeneratedExportsAnalyzer_ShouldIgnoreGeneratedPartialMembers()
    {
        const string source = """
            using Darp.Luau;

            [LuauUserdata]
            public sealed partial class HeroCard
            {
                [LuauMember("name")]
                public string Name { get; set; } = "unknown";
            }

            [LuauModule("guild")]
            public sealed partial class GuildModule
            {
                [LuauMember("heroes.create")]
                public HeroCard CreateHero(string name) => new() { Name = name };
            }
            """;

        CSharpParseOptions parseOptions = CreateParseOptions();
        CSharpCompilation compilation = CreateCompilation(source, parseOptions);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new GeneratedExportsGenerator().AsSourceGenerator()],
            parseOptions: parseOptions
        );
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation generatedCompilation,
            out _,
            cancellationToken
        );

        ImmutableArray<Diagnostic> diagnostics = await generatedCompilation
            .WithAnalyzers([new GeneratedExportsAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(cancellationToken);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void CreateFunctionModelStage_ShouldBeCacheable()
    {
        const string source = """
            using Darp.Luau;

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    state.CreateFunction((int value) => value);
                    state.CreateFunction((string value) => value);
                }
            }
            """;

        GeneratorDriverRunResult firstRun = RunTwice(new CreateFunctionGenerator(), source, out var secondRun);

        AssertCacheableStep(firstRun, secondRun, "CreateFunctionModels");
    }

    [Fact]
    public void GeneratedExportsModelStages_ShouldBeCacheable()
    {
        const string source = """
            using Darp.Luau;

            [LuauUserdata]
            public sealed partial class Character
            {
                [LuauMember("name")]
                public string Name { get; set; } = "unknown";
            }

            [LuauModule("game")]
            public static partial class GameModule
            {
                [LuauMember("answer")]
                public static int Answer => 42;
            }
            """;

        GeneratorDriverRunResult firstRun = RunTwice(new GeneratedExportsGenerator(), source, out var secondRun);

        AssertCacheableStep(firstRun, secondRun, "GeneratedExportsModuleModels");
        AssertCacheableStep(firstRun, secondRun, "GeneratedExportsUserdataModels");
    }

    private static GeneratorDriverRunResult RunTwice<TGenerator>(
        TGenerator generator,
        string source,
        out GeneratorDriverRunResult secondRun
    )
        where TGenerator : IIncrementalGenerator
    {
        CSharpParseOptions parseOptions = CreateParseOptions();
        CSharpCompilation compilation = CreateCompilation(source, parseOptions);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            parseOptions: parseOptions,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true
            )
        );
        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult firstRun = driver.GetRunResult();
        driver = driver.RunGenerators(compilation);
        secondRun = driver.GetRunResult();
        return firstRun;
    }

    private static CSharpParseOptions CreateParseOptions()
    {
        return CSharpParseOptions
            .Default.WithLanguageVersion(LanguageVersion.CSharp13)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "Darp.Luau.Generator")]);
    }

    private static CSharpCompilation CreateCompilation(string source, CSharpParseOptions parseOptions)
    {
        VerifyHelper.AddReferenceAssemblyMarker<LuauState>();

        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
            SourceText.From(source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')),
            parseOptions
        );
        PortableExecutableReference[] references = AppDomain
            .CurrentDomain.GetAssemblies()
            .Where(static x => !x.IsDynamic && !string.IsNullOrEmpty(x.Location))
            .Where(static x => x.FullName?.Contains("JetBrains") is not true)
            .Select(static x => MetadataReference.CreateFromFile(x.Location))
            .ToArray();

        return CSharpCompilation.Create(
            assemblyName: Assembly.GetExecutingAssembly().FullName,
            syntaxTrees: [syntaxTree],
            references: references,
            new CSharpCompilationOptions(OutputKind.NetModule, nullableContextOptions: NullableContextOptions.Enable)
        );
    }

    private static void AssertCacheableStep(
        GeneratorDriverRunResult firstRun,
        GeneratorDriverRunResult secondRun,
        string stepName
    )
    {
        firstRun.Results.Length.ShouldBe(1);
        secondRun.Results.Length.ShouldBe(1);
        firstRun.Results[0].TrackedSteps.ShouldContainKey(stepName);
        secondRun.Results[0].TrackedSteps.ShouldContainKey(stepName);

        ImmutableArray<IncrementalGeneratorRunStep> firstSteps = firstRun.Results[0].TrackedSteps[stepName];
        ImmutableArray<IncrementalGeneratorRunStep> secondSteps = secondRun.Results[0].TrackedSteps[stepName];
        firstSteps.Length.ShouldBe(secondSteps.Length);

        for (int i = 0; i < firstSteps.Length; i++)
        {
            object[] firstOutputs = firstSteps[i].Outputs.Select(static x => x.Value).ToArray();
            object[] secondOutputs = secondSteps[i].Outputs.Select(static x => x.Value).ToArray();

            secondOutputs.ShouldBe(firstOutputs, customMessage: $"{stepName} should produce equal outputs");
            secondSteps[i]
                .Outputs.ShouldAllBe(
                    static x =>
                        x.Reason == IncrementalStepRunReason.Cached || x.Reason == IncrementalStepRunReason.Unchanged,
                    customMessage: $"{stepName} should be cached or unchanged on the second run"
                );
            foreach (object output in secondOutputs)
                AssertObjectGraphIsGeneratorOwned(output, stepName);
        }
    }

    private static void AssertObjectGraphIsGeneratorOwned(object? value, string stepName)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Visit(value);

        void Visit(object? node)
        {
            if (node is null)
                return;

            Type type = node.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
                return;

            if (!visited.Add(node))
                return;

            node.ShouldNotBeOfType<Compilation>($"{stepName} should not root Roslyn compilation state");
            node.ShouldNotBeOfType<ISymbol>($"{stepName} should not root Roslyn symbols");
            node.ShouldNotBeOfType<SyntaxNode>($"{stepName} should not root Roslyn syntax");
            node.ShouldNotBeOfType<IOperation>($"{stepName} should not root Roslyn operations");
            node.ShouldNotBeOfType<SemanticModel>($"{stepName} should not root Roslyn semantic models");
            node.ShouldNotBeOfType<Diagnostic>($"{stepName} should not carry diagnostics");
            node.ShouldNotBeOfType<AttributeData>($"{stepName} should not root Roslyn attributes");

            foreach (
                FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            )
                Visit(field.GetValue(node));
        }
    }
}
