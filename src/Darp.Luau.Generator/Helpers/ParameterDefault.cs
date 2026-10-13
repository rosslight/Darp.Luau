using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Darp.Luau.Generator.Helpers;

/// <summary> Reads the default value of a parameter of a managed callback. </summary>
internal static class ParameterDefault
{
    /// <summary>
    /// Gets <paramref name="symbol"/>, which is read as <paramref name="type"/>, together with its default value.
    /// </summary>
    /// <returns>
    /// What generated code cannot use about the default, worded to follow the name of the parameter. Null when it
    /// can use the default, or there is none.
    /// </returns>
    public static string? TryRead(IParameterSymbol symbol, InteropType type, out InteropParameter parameter)
    {
        parameter = new InteropParameter(type);
        if (!symbol.HasExplicitDefaultValue)
            return null;

        // Only a number, a bool, a string and an enum have a constant, and each of them has a nullable read.
        if (symbol.ExplicitDefaultValue is { } constant)
        {
            parameter = parameter with { Default = FormatConstant(constant, type) };
            return null;
        }

        // The default is null, or the default of a struct.
        if (type.IsNullable)
        {
            parameter = parameter with { Default = InteropParameter.NullDefault };
            return null;
        }

        // "string name = null!": the host passes null where it declared there is none, and so does the read.
        if (type.Type is LuauInteropKind.StringString or LuauInteropKind.ManagedUserdata)
        {
            parameter = parameter with { Default = "null!" };
            return null;
        }

        // A LuauValue reads nil itself. A span and a view have no read that takes nil.
        if (
            type.Type is not (LuauInteropKind.CancellationToken or LuauInteropKind.LuauValue)
            && !InteropTypeMapper.SupportsNullableValue(type.Type)
        )
        {
            return "has a default value that its type cannot have";
        }

        parameter = parameter with { Default = "default" };
        return null;
    }

    /// <summary> Writes <paramref name="constant"/>, the default of a parameter of <paramref name="type"/>, as C#. </summary>
    private static string FormatConstant(object constant, InteropType type)
    {
        // The constant of an enum is the number. Parentheses, because a cast does not take a negative number.
        if (type is { Type: LuauInteropKind.Enum, OriginalTypeName: { } enumName })
            return $"({enumName})({FormatNumber(constant)})";

        return constant switch
        {
            string text => SymbolDisplay.FormatLiteral(text, quote: true),
            // Without its suffix a literal is an int or a double, which is not what every number converts from.
            float number => FormatNotFinite(number, "float") ?? FormatNumber(constant) + "F",
            double number => FormatNotFinite(number, "double") ?? FormatNumber(constant) + "D",
            decimal => FormatNumber(constant) + "M",
            uint => FormatNumber(constant) + "U",
            long => FormatNumber(constant) + "L",
            ulong => FormatNumber(constant) + "UL",
            _ => FormatNumber(constant),
        };
    }

    /// <summary> Writes a number that has no literal as the constant of <paramref name="type"/>. Null for a number that has one. </summary>
    private static string? FormatNotFinite(double number, string type)
    {
        if (double.IsNaN(number))
            return $"{type}.NaN";
        if (double.IsPositiveInfinity(number))
            return $"{type}.PositiveInfinity";
        return double.IsNegativeInfinity(number) ? $"{type}.NegativeInfinity" : null;
    }

    /// <summary> Writes a number, or a bool, without a suffix. </summary>
    private static string FormatNumber(object constant) =>
        SymbolDisplay.FormatPrimitive(constant, quoteStrings: false, useHexadecimalNumbers: false)
        ?? throw new ArgumentOutOfRangeException(nameof(constant), constant, "Not the constant of a default value");
}
