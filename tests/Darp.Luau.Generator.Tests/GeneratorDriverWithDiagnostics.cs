using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Darp.Luau.Generator.Tests;

internal sealed class GeneratorDriverWithDiagnostics(
    GeneratorDriverRunResult runResult,
    ImmutableArray<Diagnostic> diagnostics
)
{
    public GeneratorDriverRunResult RunResult { get; } = runResult;

    public ImmutableArray<Diagnostic> Diagnostics { get; } = diagnostics;

    public static ConversionResult Convert(GeneratorDriverWithDiagnostics target, IReadOnlyDictionary<string, object> _)
    {
        List<Exception> exceptions = [];
        List<Target> generatedTargets = [];
        foreach (GeneratorRunResult result in target.RunResult.Results)
        {
            if (result.Exception is not null)
                exceptions.Add(result.Exception);

            generatedTargets.AddRange(
                result
                    .GeneratedSources.OrderBy(static source => source.HintName, StringComparer.Ordinal)
                    .Select(SourceToTarget)
            );
        }

        if (exceptions.Count == 1)
            throw exceptions[0];
        if (exceptions.Count > 1)
            throw new AggregateException(exceptions);

        object? info = target.Diagnostics.IsEmpty ? null : new { target.Diagnostics };
        return new ConversionResult(info, generatedTargets);
    }

    private static Target SourceToTarget(GeneratedSourceResult source)
    {
        string hintName = source.HintName;
        string text = $"//HintName: {hintName}\n{source.SourceText}";
        string filePath = source.SyntaxTree.FilePath;
        if (filePath.Length > 0)
        {
            string extension = Path.GetExtension(filePath)[1..];
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            return new Target(extension, text, fileName);
        }

        string name = Path.GetFileNameWithoutExtension(hintName);
        return hintName.EndsWith(".vb", StringComparison.Ordinal)
            ? new Target("vb", text, name)
            : new Target("cs", text, name);
    }
}
