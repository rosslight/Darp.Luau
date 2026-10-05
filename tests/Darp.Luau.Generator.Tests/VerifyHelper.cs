using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Shouldly;

namespace Darp.Luau.Generator.Tests;

public static class VerifyHelper
{
    public static Task VerifyGenerator(string source, [CallerFilePath] string? callerFilePath = null)
    {
        string fileName =
            Path.GetFileNameWithoutExtension(callerFilePath) ?? throw new ArgumentNullException(nameof(callerFilePath));
        AddReferenceAssemblyMarker<LuauState>();
        return VerifyGenerator<CreateFunctionGenerator>(
            [source],
            [],
            "DBO0",
            fileName,
            task => task.ScrubGeneratedCodeAttribute(),
            new Dictionary<string, string>(),
            new Dictionary<string, string> { { "InterceptorsNamespaces", "Darp.Luau.Generator" } },
            LanguageVersion.CSharp13
        );
    }

    public static Task VerifyGeneratorWithErrors(string source, [CallerFilePath] string? callerFilePath = null)
    {
        string fileName =
            Path.GetFileNameWithoutExtension(callerFilePath) ?? throw new ArgumentNullException(nameof(callerFilePath));
        AddReferenceAssemblyMarker<LuauState>();
        return VerifyGenerator<CreateFunctionGenerator>(
            [source],
            [],
            "DLUAU",
            fileName,
            task => task.ScrubGeneratedCodeAttribute(),
            new Dictionary<string, string>(),
            new Dictionary<string, string> { { "InterceptorsNamespaces", "Darp.Luau.Generator" } },
            LanguageVersion.CSharp13,
            allowCompilationErrors: true,
            analyzers: ImmutableArray.Create<DiagnosticAnalyzer>(new CreateFunctionUsageAnalyzer()),
            verifyDiagnosticsSnapshot: true,
            verifyGeneratedSources: false
        );
    }

    public static Task VerifyGeneratorAndAnalyzerWithErrors(
        string source,
        DiagnosticAnalyzer analyzer,
        [CallerFilePath] string? callerFilePath = null
    )
    {
        string fileName =
            Path.GetFileNameWithoutExtension(callerFilePath) ?? throw new ArgumentNullException(nameof(callerFilePath));
        AddReferenceAssemblyMarker<LuauState>();
        return VerifyGenerator<CreateFunctionGenerator>(
            [source],
            [],
            "DLUAU",
            fileName,
            task => task.ScrubGeneratedCodeAttribute(),
            new Dictionary<string, string>(),
            new Dictionary<string, string> { { "InterceptorsNamespaces", "Darp.Luau.Generator" } },
            LanguageVersion.CSharp13,
            allowCompilationErrors: true,
            analyzers: ImmutableArray.Create(analyzer),
            verifyDiagnosticsSnapshot: true,
            verifyGeneratedSources: false
        );
    }

    public static Task VerifyGeneratedExports(string source, [CallerFilePath] string? callerFilePath = null)
    {
        return VerifyGeneratedExports([source], callerFilePath);
    }

    public static Task VerifyGeneratedExportsSource(string source, [CallerFilePath] string? callerFilePath = null)
    {
        return VerifyGeneratedExportsSource([source], callerFilePath);
    }

    public static Task VerifyGeneratedExports(string[] sources, [CallerFilePath] string? callerFilePath = null)
    {
        string fileName =
            Path.GetFileNameWithoutExtension(callerFilePath) ?? throw new ArgumentNullException(nameof(callerFilePath));
        AddReferenceAssemblyMarker<LuauState>();
        return VerifyGenerator<GeneratedExportsGenerator>(
            sources,
            [],
            null,
            fileName,
            static task => task,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            LanguageVersion.CSharp13,
            analyzers: ImmutableArray.Create<DiagnosticAnalyzer>(new GeneratedExportsAnalyzer()),
            verifyDiagnosticsSnapshot: true,
            verifyCompilationDiagnosticsSnapshot: true,
            verifyGeneratedSources: false
        );
    }

    public static Task VerifyGeneratedExportsSource(string[] sources, [CallerFilePath] string? callerFilePath = null)
    {
        string fileName =
            Path.GetFileNameWithoutExtension(callerFilePath) ?? throw new ArgumentNullException(nameof(callerFilePath));
        AddReferenceAssemblyMarker<LuauState>();
        return VerifyGenerator<GeneratedExportsGenerator>(
            sources,
            [],
            null,
            fileName,
            task => task.ScrubGeneratedCodeAttribute(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            LanguageVersion.CSharp13,
            analyzers: ImmutableArray.Create<DiagnosticAnalyzer>(new GeneratedExportsAnalyzer())
        );
    }

    public static Task VerifyGeneratedExportsSourceWithErrors(string source, [CallerFilePath] string? callerFilePath = null)
    {
        string fileName =
            Path.GetFileNameWithoutExtension(callerFilePath) ?? throw new ArgumentNullException(nameof(callerFilePath));
        AddReferenceAssemblyMarker<LuauState>();
        return VerifyGenerator<GeneratedExportsGenerator>(
            [source],
            [],
            "DLUAU",
            fileName,
            task => task.ScrubGeneratedCodeAttribute(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            LanguageVersion.CSharp13,
            allowCompilationErrors: true,
            analyzers: ImmutableArray.Create<DiagnosticAnalyzer>(new GeneratedExportsAnalyzer()),
            verifyDiagnosticsSnapshot: true,
            verifyGeneratedSources: true
        );
    }

    public static Task VerifyGeneratedExportsWithErrors(string source, [CallerFilePath] string? callerFilePath = null)
    {
        return VerifyGeneratedExportsWithErrors([source], callerFilePath);
    }

    public static Task VerifyGeneratedExportsWithErrors(
        string[] sources,
        [CallerFilePath] string? callerFilePath = null
    )
    {
        string fileName =
            Path.GetFileNameWithoutExtension(callerFilePath) ?? throw new ArgumentNullException(nameof(callerFilePath));
        AddReferenceAssemblyMarker<LuauState>();
        return VerifyGenerator<GeneratedExportsGenerator>(
            sources,
            [],
            "DLUAU",
            fileName,
            static task => task,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            LanguageVersion.CSharp13,
            allowCompilationErrors: true,
            analyzers: ImmutableArray.Create<DiagnosticAnalyzer>(new GeneratedExportsAnalyzer()),
            verifyDiagnosticsSnapshot: true,
            verifyCompilationDiagnosticsSnapshot: true,
            verifyGeneratedSources: false
        );
    }

    public static async Task VerifyCreateFunctionWithGeneratedExportsSucceeds(string source)
    {
        AddReferenceAssemblyMarker<LuauState>();

        CSharpParseOptions parseOptions = CSharpParseOptions
            .Default.WithLanguageVersion(LanguageVersion.CSharp13)
            .WithFeatures(new Dictionary<string, string> { { "InterceptorsNamespaces", "Darp.Luau.Generator" } });
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(NormalizeLineEndings(source), parseOptions);

        PortableExecutableReference[] references = AppDomain
            .CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Where(static assembly => assembly.FullName?.Contains("JetBrains") is not true)
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName: Assembly.GetExecutingAssembly().FullName,
            syntaxTrees: [syntaxTree],
            references: references,
            new CSharpCompilationOptions(OutputKind.NetModule, nullableContextOptions: NullableContextOptions.Enable)
        );

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new GeneratedExportsGenerator().AsSourceGenerator(),
                new CreateFunctionGenerator().AsSourceGenerator(),
            ],
            parseOptions: parseOptions
        );
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation newCompilation, out _);

        GeneratorDriverRunResult runResult = driver.GetRunResult();
        ImmutableArray<Diagnostic> generatorDiagnostics = runResult.Results
            .SelectMany(static result => result.Diagnostics)
            .ToImmutableArray();
        ImmutableArray<Diagnostic> compilationDiagnostics = newCompilation.GetDiagnostics();
        ImmutableArray<Diagnostic> analyzerDiagnostics = await newCompilation
            .WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(
                    new GeneratedExportsAnalyzer(),
                    new CreateFunctionUsageAnalyzer()
                )
            )
            .GetAnalyzerDiagnosticsAsync();

        Diagnostic[] invalidDiagnostics = compilationDiagnostics
            .Concat(generatorDiagnostics)
            .Concat(analyzerDiagnostics)
            .Where(static diagnostic => IsDiagnosticInvalid(null, diagnostic))
            .OrderBy(static diagnostic => diagnostic.Location.SourceTree?.FilePath ?? "", StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Location.SourceSpan.Start)
            .ThenBy(static diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.GetMessage(), StringComparer.Ordinal)
            .ToArray();
        invalidDiagnostics.ShouldBeEmpty(
            string.Join(
                "\n",
                invalidDiagnostics.Select(static diagnostic =>
                    $"[{diagnostic.Location.GetLineSpan()}] {diagnostic.Id}: {diagnostic.GetMessage()}"
                )
            )
                + "\n"
                + string.Join("\n", runResult.ToReadableString())
        );

        runResult.Results
            .SelectMany(static result => result.GeneratedSources)
            .Count(static source => source.HintName.Contains("CreateFunctionInterceptors", StringComparison.Ordinal))
            .ShouldBe(2);
    }

    /// <summary>
    /// This functions ensures that the assembly is referenced and <see cref="AppDomain.GetAssemblies()"/> of the <see cref="AppDomain.CurrentDomain"/> contains this assembly
    /// </summary>
    /// <typeparam name="TMarker"> The type of an object of the assembly </typeparam>
    /// <returns> The task </returns>
    public static void AddReferenceAssemblyMarker<TMarker>()
    {
        _ = typeof(TMarker).Assembly;
    }

    public static SettingsTask ScrubGeneratedCodeAttribute(
        this SettingsTask settingsTask,
        string scrubbedVersionName = "GeneratorVersion"
    )
    {
        return settingsTask.ScrubLinesWithReplace(line =>
        {
            var regex = new Regex("""GeneratedCodeAttribute\("[^"\n]+",\s*"(?<version>\d+\.\d+\.\d+\.\d+)"\)""");
            return regex.Replace(
                line,
                match =>
                {
                    string versionToReplace = match.Groups["version"].Value;
                    return match.Value.Replace(versionToReplace, scrubbedVersionName);
                }
            );
        });
    }

    private static async Task VerifyGenerator<TGenerator>(
        string[] sources,
        AdditionalText[] additionalTexts,
        string? allowedDiagnosticCode,
        string directory,
        Func<SettingsTask, SettingsTask> configureSettingsTask,
        IReadOnlyDictionary<string, string> analyzerConfigOptions,
        IReadOnlyDictionary<string, string> features,
        LanguageVersion languageVersion = LanguageVersion.Default,
        NullableContextOptions nullableContextOptions = NullableContextOptions.Enable,
        bool allowCompilationErrors = false,
        ImmutableArray<DiagnosticAnalyzer> analyzers = default,
        bool verifyDiagnosticsSnapshot = false,
        bool verifyCompilationDiagnosticsSnapshot = false,
        bool verifyGeneratedSources = true
    )
        where TGenerator : IIncrementalGenerator, new()
    {
        CSharpParseOptions parseOptions = CSharpParseOptions
            .Default.WithLanguageVersion(languageVersion)
            .WithFeatures(features);
        SyntaxTree[] syntaxTrees = sources
            .Select(NormalizeLineEndings)
            .Select(x => CSharpSyntaxTree.ParseText(x, parseOptions))
            .ToArray();

        // Get all references of the currently loaded assembly
        PortableExecutableReference[] references = AppDomain
            .CurrentDomain.GetAssemblies() // Get currently loaded assemblies
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Where(a => a.FullName?.Contains("JetBrains") is not true)
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName: Assembly.GetExecutingAssembly().FullName,
            syntaxTrees: syntaxTrees,
            references: references,
            new CSharpCompilationOptions(OutputKind.NetModule, nullableContextOptions: nullableContextOptions)
        );

        // Assert that there are no compilation errors (except for CS5001 which informs about the missing program entry)
        // Skip this check if allowCompilationErrors is true (for error tests)
        if (!allowCompilationErrors)
        {
            compilation
                .GetDiagnostics()
                .Where(x => x.Id is not "CS5001" && (x.Severity > DiagnosticSeverity.Warning || x.IsWarningAsError))
                .ShouldBeEmpty();
        }

        var generator = new TGenerator();

        var provider = new OptionsProvider(analyzerConfigOptions);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: additionalTexts,
            parseOptions: parseOptions,
            optionsProvider: provider
        );
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation newCompilation, out _);
        ImmutableArray<Diagnostic> generatorDiagnostics = driver
            .GetRunResult()
            .Results.SelectMany(static x => x.Diagnostics)
            .ToImmutableArray();
        ImmutableArray<Diagnostic> compilationDiagnostics = newCompilation.GetDiagnostics();
        ImmutableArray<Diagnostic> analyzerDiagnostics = analyzers.IsDefaultOrEmpty
            ? []
            : await newCompilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();

        IEnumerable<Diagnostic> snapshotDiagnostics = [];
        if (verifyCompilationDiagnosticsSnapshot)
            snapshotDiagnostics = snapshotDiagnostics.Concat(generatorDiagnostics).Concat(compilationDiagnostics);
        if (verifyDiagnosticsSnapshot)
            snapshotDiagnostics = snapshotDiagnostics.Concat(analyzerDiagnostics);

        ImmutableArray<Diagnostic> diagnosticsToVerify = snapshotDiagnostics
            .Where(x => x.Id is not "CS5001")
            .OrderBy(static x => x.Location.SourceTree?.FilePath ?? "", StringComparer.Ordinal)
            .ThenBy(static x => x.Location.SourceSpan.Start)
            .ThenBy(static x => x.Id, StringComparer.Ordinal)
            .ThenBy(static x => x.GetMessage(), StringComparer.Ordinal)
            .ToImmutableArray();
        object verificationTarget = verifyGeneratedSources
            ? diagnosticsToVerify.IsEmpty
                ? driver
                : new GeneratorDriverWithDiagnostics(driver.GetRunResult(), diagnosticsToVerify)
            : new { Diagnostics = diagnosticsToVerify.ToArray() };
        SettingsTask settingsTask = Verify(verificationTarget).UseDirectory(Path.Join("Snapshots", directory));
        await configureSettingsTask(settingsTask);
        // Assert that there are no compilation errors (except for CS5001 which informs about the missing program entry)
        compilationDiagnostics
            .Concat(generatorDiagnostics)
            .Concat(analyzerDiagnostics)
            .ShouldNotContain(
                x => IsDiagnosticInvalid(allowedDiagnosticCode, x),
                string.Join(
                    "\n",
                    compilationDiagnostics
                        .Concat(generatorDiagnostics)
                        .Concat(analyzerDiagnostics)
                        .Select(x => $"[{x.Location.GetLineSpan()}]: {x.GetMessage()}")
                )
                    + "\n"
                    + string.Join("\n", driver.GetRunResult().ToReadableString())
            );
    }

    private static string NormalizeLineEndings(string source)
    {
        return source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    private static string ToReadableString(this GeneratorDriverRunResult result)
    {
        return string.Join(
            "\n",
            result.GeneratedTrees.SelectMany(x => x.GetText().Lines).Select((x, i) => $"{i + 1, 4} {x}")
        );
    }

    private static bool IsDiagnosticInvalid(string? allowedDiagnosticCode, Diagnostic x)
    {
        return x.Id is not "CS5001"
            && (allowedDiagnosticCode is null || !x.Id.StartsWith(allowedDiagnosticCode, StringComparison.Ordinal))
            && (x.Severity > DiagnosticSeverity.Warning || x.IsWarningAsError);
    }
}

file sealed class EmbeddedResourcesAdditionalText(string logicalPath) : AdditionalText
{
    private readonly Assembly _assembly = typeof(EmbeddedResourcesAdditionalText).Assembly;

    public override string Path { get; } = logicalPath;

    public override SourceText GetText(CancellationToken cancellationToken = default)
    {
        string resourceName = System.IO.Path.Join(_assembly.GetName().Name, Path).Replace('\\', '.').Replace('/', '.');
        using Stream s =
            _assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName} ({Path})");

        using var reader = new StreamReader(s);
        return SourceText.From(reader.ReadToEnd());
    }
}

file sealed class DictOptions(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
{
    private readonly IReadOnlyDictionary<string, string> _values = values;

    public override bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value!);
}

file sealed class OptionsProvider(IReadOnlyDictionary<string, string> globalValues) : AnalyzerConfigOptionsProvider
{
    private readonly AnalyzerConfigOptions _empty = new DictOptions(new Dictionary<string, string>());

    public override AnalyzerConfigOptions GlobalOptions { get; } = new DictOptions(globalValues);

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _empty;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _empty;
}
