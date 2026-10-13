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

        // A span and a view have no read that takes nil. A LuauValue reads nil itself, and a cancellation token is
        // never a Luau argument.
        if (
            type.Type is not (LuauInteropKind.CancellationToken or LuauInteropKind.LuauValue)
            && !InteropTypeMapper.SupportsNullableValue(type.Type)
        )
        {
            return "has a default value that its type cannot have";
        }

        // The symbol has no constant for null and for the default of a struct.
        parameter = parameter with
        {
            Default = symbol.ExplicitDefaultValue is { } constant
                ? FormatConstant(constant, type)
                : InteropParameter.DefaultOfType,
        };
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
