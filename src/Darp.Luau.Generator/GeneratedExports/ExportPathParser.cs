using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Darp.Luau.Generator.GeneratedExports;

internal static class ExportPathParser
{
    private static readonly HashSet<string> s_luauKeywords = new(StringComparer.Ordinal)
    {
        "and",
        "break",
        "continue",
        "do",
        "else",
        "elseif",
        "end",
        "false",
        "for",
        "function",
        "if",
        "in",
        "local",
        "nil",
        "not",
        "or",
        "repeat",
        "return",
        "then",
        "true",
        "until",
        "while",
    };

    public static bool TryParseModulePath(
        string rawPath,
        Location location,
        List<Diagnostic> diagnostics,
        out ImmutableArray<string> segments
    )
    {
        return TryParsePath(rawPath, allowDotted: true, location, diagnostics, out segments);
    }

    public static bool TryParseUserdataPath(
        string rawPath,
        string memberName,
        Location location,
        List<Diagnostic> diagnostics,
        out ImmutableArray<string> segments
    )
    {
        if (rawPath.IndexOf('.') >= 0)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    location,
                    $"userdata member '{memberName}' must use a single-segment Luau name"
                )
            );
            segments = [];
            return false;
        }

        return TryParsePath(rawPath, allowDotted: false, location, diagnostics, out segments);
    }

    private static bool TryParsePath(
        string rawPath,
        bool allowDotted,
        Location location,
        List<Diagnostic> diagnostics,
        out ImmutableArray<string> segments
    )
    {
        if (!allowDotted && rawPath.IndexOf('.') >= 0)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    location,
                    $"Luau member name '{rawPath}' must be a single segment"
                )
            );
            segments = [];
            return false;
        }

        string[] splitSegments = rawPath.Split('.');
        if (splitSegments.Length == 0 || splitSegments.Any(static x => x.Length == 0))
        {
            diagnostics.Add(
                Diagnostic.Create(DiagnosticDescriptors.InvalidLuauExportPathDescriptor, location, rawPath)
            );
            segments = [];
            return false;
        }

        if (splitSegments.Any(static segment => !IsLuauDotPathIdentifier(segment)))
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.LuauExportPathSegmentRequiresBracketAccessDescriptor,
                    location,
                    rawPath
                )
            );
        }

        segments = splitSegments.ToImmutableArray();
        return true;
    }

    public static bool IsLuauDotPathIdentifier(string segment)
    {
        if (s_luauKeywords.Contains(segment))
            return false;

        if (segment.Length == 0 || !IsIdentifierStart(segment[0]))
            return false;

        for (int i = 1; i < segment.Length; i++)
        {
            if (!IsIdentifierPart(segment[i]))
                return false;
        }

        return true;
    }

    private static bool IsIdentifierStart(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_';

    private static bool IsIdentifierPart(char c) => IsIdentifierStart(c) || c is >= '0' and <= '9';
}
