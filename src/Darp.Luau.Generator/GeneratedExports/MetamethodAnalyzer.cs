using Darp.Luau.Generator.Helpers;
using Microsoft.CodeAnalysis;

namespace Darp.Luau.Generator.GeneratedExports;

/// <summary> Turns the methods a userdata type marks with <c>[LuauMetamethod]</c> into its metamethods. </summary>
internal static class MetamethodAnalyzer
{
    /// <summary> An overload together with what is only needed to compare it with the other overloads. </summary>
    private sealed record Candidate(
        string Metamethod,
        IMethodSymbol Method,
        Location Location,
        GeneratedMetamethodOverloadIr Overload,
        IReadOnlyList<ITypeSymbol> OperandTypes
    );

    public static ImmutableEquatableArray<GeneratedMetamethodIr> Analyze(
        DiscoveredExportType discoveredType,
        LuauApiSymbols context,
        List<Diagnostic> diagnostics
    )
    {
        var candidates = new List<Candidate>();
        foreach (DiscoveredExportMetamethod metamethod in discoveredType.Metamethods)
        {
            Candidate? candidate = AnalyzeMethod(discoveredType, metamethod, context, diagnostics);
            if (candidate is not null)
                candidates.Add(candidate);
        }

        var metamethods = new List<GeneratedMetamethodIr>();
        foreach (IGrouping<string, Candidate> group in candidates.GroupBy(static x => x.Metamethod))
        {
            Candidate[] overloads = group.ToArray();
            ReportOverloadsLuauCannotTellApart(overloads, diagnostics);
            foreach (Candidate overload in overloads)
                ReportOverloadOfAnotherTypesOperator(discoveredType.Symbol, overload, context, diagnostics);

            metamethods.Add(
                new GeneratedMetamethodIr(
                    group.Key,
                    overloads.Select(static x => x.Overload).ToImmutableEquatableArray()
                )
            );
        }

        return metamethods.OrderBy(static x => x.Name, StringComparer.Ordinal).ToImmutableEquatableArray();
    }

    private static Candidate? AnalyzeMethod(
        DiscoveredExportType discoveredType,
        DiscoveredExportMetamethod discovered,
        LuauApiSymbols context,
        List<Diagnostic> diagnostics
    )
    {
        IMethodSymbol method = discovered.Symbol;
        Location location = discovered.Origin.Location;
        if (discoveredType.Kind != LuauExportedTypeKind.Userdata)
        {
            Report(
                diagnostics,
                location,
                $"method '{method.Name}' is a metamethod, which only a [LuauUserdata] type has"
            );
            return null;
        }

        string? metamethod = LuauApiSymbols.GetMetamethodName(discovered.Attribute);
        if (metamethod is null)
        {
            Report(diagnostics, location, $"method '{method.Name}' names a metamethod that does not exist");
            return null;
        }

        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.UserDefinedOperator))
        {
            Report(diagnostics, location, $"'{method.Name}' is neither an ordinary method nor an operator");
            return null;
        }

        if (!method.ExplicitInterfaceImplementations.IsEmpty)
        {
            Report(
                diagnostics,
                location,
                $"method '{method.Name}' is an explicit interface implementation, which generated code cannot access"
            );
            return null;
        }

        if (method is { IsPartialDefinition: true, PartialImplementationPart: null })
        {
            Report(diagnostics, location, $"partial method '{method.Name}' has no implementation");
            return null;
        }

        if (
            !ExportAnalyzer.TryMapMethodSignature(
                method,
                LuauExportedTypeKind.Userdata,
                context,
                location,
                diagnostics,
                out ImmutableEquatableArray<InteropType> parameters,
                out ImmutableEquatableArray<InteropType> returns,
                out AwaitableReturnKind awaitable
            )
        )
        {
            return null;
        }

        // An instance method takes the instance as its first operand.
        var operands = new List<InteropType>();
        var operandTypes = new List<ITypeSymbol>();
        if (!method.IsStatic)
        {
            if (!InteropTypeMapper.TryMapGeneratedUserdataType(discoveredType.Symbol, context, out InteropType self))
                return null;
            operands.Add(self with { IsNullable = false });
            operandTypes.Add(discoveredType.Symbol);
        }
        operands.AddRange(parameters);
        operandTypes.AddRange(method.Parameters.Select(static x => x.Type));

        var signature = new InteropSignature(operands.ToImmutableEquatableArray(), returns, awaitable);
        string ownTypeName = discoveredType.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string? shapeError = GetShapeError(metamethod, signature, ownTypeName);
        if (shapeError is not null)
        {
            Report(diagnostics, location, $"metamethod '{metamethod}' on '{method.Name}' {shapeError}");
            return null;
        }

        if (!TryGetCall(method, ownTypeName, out MetamethodCallKind callKind, out string? callTarget))
        {
            Report(
                diagnostics,
                location,
                $"operator '{method.Name}' cannot be called from generated code; declare the metamethod on a method that calls it"
            );
            return null;
        }

        return new Candidate(
            metamethod,
            method,
            location,
            new GeneratedMetamethodOverloadIr(callKind, callTarget, signature),
            operandTypes
        );
    }

    /// <summary> Says what is wrong with the operands and the result of a metamethod, or returns null. </summary>
    private static string? GetShapeError(string metamethod, InteropSignature signature, string ownTypeName)
    {
        InteropType[] operands = signature
            .Parameters.Where(static x => x.Type is not LuauInteropKind.CancellationToken)
            .ToArray();
        bool IsOwn(InteropType type) =>
            type is { Type: LuauInteropKind.ManagedUserdata, IsNullable: false }
            && type.OriginalTypeName == ownTypeName;

        if (metamethod == "Call")
        {
            return operands.Length > 0 && IsOwn(operands[0])
                ? null
                : "must take the instance as its first operand: use an instance method, or a static one whose first parameter is the type";
        }

        if (signature.Awaitable is not AwaitableReturnKind.None)
            return "cannot return a task: Luau cannot wait inside a metamethod other than Call";
        if (operands.Length != signature.Parameters.Length)
            return "cannot take a CancellationToken: only Call runs where a script can be cancelled while it waits";

        switch (metamethod)
        {
            case "Add" or "Sub" or "Mul" or "Div" or "Mod" or "Pow" or "IDiv" or "Concat":
                if (operands.Length != 2 || !operands.Any(IsOwn))
                    return "must have two operands, at least one of them the type itself";
                return signature.ReturnTypes.Length == 1 ? null : "must return one value";
            case "Eq" or "Lt" or "Le":
                if (operands.Length != 2 || !operands.All(IsOwn))
                    return "must compare two values of the type itself";
                return signature.ReturnTypes is [{ Type: LuauInteropKind.Boolean, IsNullable: false }]
                    ? null
                    : "must return bool";
            case "Unm":
                if (operands.Length != 1 || !IsOwn(operands[0]))
                    return "must have the instance as its only operand";
                return signature.ReturnTypes.Length == 1 ? null : "must return one value";
            case "Len":
                if (operands.Length != 1 || !IsOwn(operands[0]))
                    return "must have the instance as its only operand";
                return signature.ReturnTypes is [{ IsNullable: false } result] && IsNumber(result.Type)
                    ? null
                    : "must return a number";
            case "ToString":
                if (operands.Length != 1 || !IsOwn(operands[0]))
                    return "must have the instance as its only operand";
                return signature.ReturnTypes is [{ IsNullable: false } text] && IsString(text.Type)
                    ? null
                    : "must return a string that is never null";
            case "Index":
                if (operands.Length != 2 || !IsOwn(operands[0]))
                    return "must take the instance and the key";
                return signature.ReturnTypes.Length == 1 ? null : "must return one value";
            case "NewIndex":
                if (operands.Length != 3 || !IsOwn(operands[0]))
                    return "must take the instance, the key and the value";
                return signature.ReturnTypes.Length == 0 ? null : "must not return a value";
            default:
                return "is not supported";
        }
    }

    private static bool TryGetCall(
        IMethodSymbol method,
        string ownTypeName,
        out MetamethodCallKind callKind,
        out string callTarget
    )
    {
        string name = ExportProjector.EscapeIdentifier(method.Name);
        if (method.MethodKind is not MethodKind.UserDefinedOperator)
        {
            callKind = method.IsStatic ? MetamethodCallKind.StaticMethod : MetamethodCallKind.InstanceMethod;
            callTarget = method.IsStatic ? $"{ownTypeName}.{name}" : name;
            return true;
        }

        callKind = method.Parameters.Length == 1 ? MetamethodCallKind.UnaryOperator : MetamethodCallKind.BinaryOperator;
        string? token = (method.Name, method.Parameters.Length) switch
        {
            ("op_Addition", 2) => "+",
            ("op_Subtraction", 2) => "-",
            ("op_Multiply", 2) => "*",
            ("op_Division", 2) => "/",
            ("op_Modulus", 2) => "%",
            ("op_ExclusiveOr", 2) => "^",
            ("op_BitwiseAnd", 2) => "&",
            ("op_BitwiseOr", 2) => "|",
            ("op_Equality", 2) => "==",
            ("op_Inequality", 2) => "!=",
            ("op_LessThan", 2) => "<",
            ("op_LessThanOrEqual", 2) => "<=",
            ("op_GreaterThan", 2) => ">",
            ("op_GreaterThanOrEqual", 2) => ">=",
            ("op_UnaryNegation", 1) => "-",
            ("op_UnaryPlus", 1) => "+",
            ("op_LogicalNot", 1) => "!",
            ("op_OnesComplement", 1) => "~",
            _ => null,
        };
        callTarget = token ?? string.Empty;
        return token is not null;
    }

    private static void ReportOverloadsLuauCannotTellApart(Candidate[] overloads, List<Diagnostic> diagnostics)
    {
        for (int second = 1; second < overloads.Length; second++)
        {
            for (int first = 0; first < second; first++)
            {
                if (!CanReceiveTheSameOperands(overloads[first], overloads[second]))
                    continue;

                Report(
                    diagnostics,
                    overloads[second].Location,
                    $"metamethod '{overloads[second].Metamethod}' on '{overloads[second].Method.Name}' takes operands that "
                        + $"Luau cannot tell apart from those of '{overloads[first].Method.Name}': overloads must differ in "
                        + "the number of operands or in the Luau type of one of them"
                );
                break;
            }
        }
    }

    private static bool CanReceiveTheSameOperands(Candidate first, Candidate second)
    {
        (InteropType Type, ITypeSymbol Symbol)[] firstOperands = GetLuauOperands(first);
        (InteropType Type, ITypeSymbol Symbol)[] secondOperands = GetLuauOperands(second);
        if (firstOperands.Length != secondOperands.Length)
            return false;

        for (int i = 0; i < firstOperands.Length; i++)
        {
            if (!CanReceiveTheSameValue(firstOperands[i], secondOperands[i]))
                return false;
        }
        return true;
    }

    private static (InteropType Type, ITypeSymbol Symbol)[] GetLuauOperands(Candidate candidate) =>
        candidate
            .Overload.Signature.Parameters.Zip(candidate.OperandTypes, static (type, symbol) => (type, symbol))
            .Where(static x => x.type.Type is not LuauInteropKind.CancellationToken)
            .ToArray();

    private static bool CanReceiveTheSameValue(
        (InteropType Type, ITypeSymbol Symbol) first,
        (InteropType Type, ITypeSymbol Symbol) second
    )
    {
        if (first.Type.IsNullable && second.Type.IsNullable)
            return true;

        LuauOperandType firstType = GetLuauOperandType(first.Type.Type);
        LuauOperandType secondType = GetLuauOperandType(second.Type.Type);
        if (firstType is LuauOperandType.Any || secondType is LuauOperandType.Any)
            return true;
        if (firstType != secondType)
            return false;
        if (firstType is not LuauOperandType.Userdata)
            return true;

        // An instance of a derived class is also read as its base class.
        return first.Type.Type is not LuauInteropKind.ManagedUserdata
            || second.Type.Type is not LuauInteropKind.ManagedUserdata
            || DerivesFrom(first.Symbol, second.Symbol)
            || DerivesFrom(second.Symbol, first.Symbol);
    }

    private static bool DerivesFrom(ITypeSymbol type, ITypeSymbol baseType)
    {
        ITypeSymbol unannotatedBase = baseType.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (
                SymbolEqualityComparer.Default.Equals(
                    current.WithNullableAnnotation(NullableAnnotation.NotAnnotated),
                    unannotatedBase
                )
            )
                return true;
        }
        return false;
    }

    /// <summary>
    /// Luau picks the metamethod of the left operand and never tries another one, so an overload for a left operand
    /// of a type that has the operator itself is never called.
    /// </summary>
    private static void ReportOverloadOfAnotherTypesOperator(
        INamedTypeSymbol ownType,
        Candidate candidate,
        LuauApiSymbols context,
        List<Diagnostic> diagnostics
    )
    {
        if (
            candidate.Metamethod is not ("Add" or "Sub" or "Mul" or "Div" or "Mod" or "Pow" or "IDiv" or "Concat")
            || candidate.OperandTypes[0] is not INamedTypeSymbol leftType
            || SymbolEqualityComparer.Default.Equals(leftType, ownType)
            || context.GetUserdataAttribute(leftType) is null
        )
        {
            return;
        }

        bool leftTypeHasTheOperator = leftType
            .GetMembers()
            .OfType<IMethodSymbol>()
            .Select(context.GetMetamethodAttribute)
            .Any(attribute =>
                attribute is not null && LuauApiSymbols.GetMetamethodName(attribute) == candidate.Metamethod
            );
        if (!leftTypeHasTheOperator)
            return;

        diagnostics.Add(
            Diagnostic.Create(
                DiagnosticDescriptors.UnreachableMetamethodOverloadDescriptor,
                candidate.Location,
                candidate.Metamethod,
                candidate.Method.Name,
                leftType.Name
            )
        );
    }

    private enum LuauOperandType
    {
        Any,
        Boolean,
        Number,
        String,
        Table,
        Function,
        Buffer,
        Userdata,
    }

    private static LuauOperandType GetLuauOperandType(LuauInteropKind kind) =>
        kind switch
        {
            LuauInteropKind.Boolean => LuauOperandType.Boolean,
            LuauInteropKind.LuauTableView => LuauOperandType.Table,
            LuauInteropKind.LuauFunctionView => LuauOperandType.Function,
            LuauInteropKind.LuauBufferView => LuauOperandType.Buffer,
            LuauInteropKind.LuauUserdataView or LuauInteropKind.ManagedUserdata => LuauOperandType.Userdata,
            _ when IsNumber(kind) => LuauOperandType.Number,
            _ when IsString(kind) => LuauOperandType.String,
            // A LuauValue takes any value. What is not known here is treated the same, so that it is never
            // assumed to differ from another operand.
            _ => LuauOperandType.Any,
        };

    public static bool IsNumber(LuauInteropKind kind) =>
        kind
            is LuauInteropKind.Number
                or LuauInteropKind.NumberByte
                or LuauInteropKind.NumberUShort
                or LuauInteropKind.NumberUInt
                or LuauInteropKind.NumberULong
                or LuauInteropKind.NumberUInt128
                or LuauInteropKind.NumberSByte
                or LuauInteropKind.NumberShort
                or LuauInteropKind.NumberInt
                or LuauInteropKind.NumberLong
                or LuauInteropKind.NumberInt128
                or LuauInteropKind.NumberHalf
                or LuauInteropKind.NumberFloat
                or LuauInteropKind.NumberDecimal
                or LuauInteropKind.Enum;

    public static bool IsString(LuauInteropKind kind) =>
        kind
            is LuauInteropKind.String
                or LuauInteropKind.StringCharSpan
                or LuauInteropKind.StringString
                or LuauInteropKind.LuauStringView;

    private static void Report(List<Diagnostic> diagnostics, Location location, string reason) =>
        diagnostics.Add(
            Diagnostic.Create(DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor, location, reason)
        );
}
