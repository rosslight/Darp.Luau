using System.CodeDom.Compiler;

namespace Darp.Luau.Generator.Helpers;

internal static class CallbackBodyEmitter
{
    private const string LuauReturn = "global::Darp.Luau.LuauReturn";
    private const string LuauValue = "global::Darp.Luau.LuauValue";

    /// <summary>
    /// Writes the body of a callback that reads its arguments from <c>args</c>, calls <paramref name="callTarget"/>
    /// and returns the result as a <c>LuauReturn</c>.
    /// </summary>
    /// <remarks>
    /// A <c>LuauValue</c> holds a reference of its own. One that is passed as an argument belongs to the call and is
    /// released when the call is over. One that is returned is handed over to Luau and released after it was copied.
    /// </remarks>
    public static void Write(IndentedTextWriter writer, InteropSignature signature, string callTarget)
    {
        int luauArgumentCount = signature.Parameters.Count(static x => x.Type is not LuauInteropKind.CancellationToken);
        writer.WriteLine($"if (!args.TryValidateArgumentCount({luauArgumentCount}, out string? error))");
        writer.WriteLine($"    return {LuauReturn}.Error(error);");

        List<string> ownedArguments = GetOwnedArguments(signature);
        if (ownedArguments.Count == 0)
        {
            WriteReadsAndCall(writer, signature, callTarget, ownedArguments);
            return;
        }

        bool isAwaitable = signature.Awaitable is not AwaitableReturnKind.None;
        foreach (string argument in ownedArguments)
            writer.WriteLine($"{LuauValue} {argument} = default;");
        if (isAwaitable)
            writer.WriteLine("bool isHandedOver = false;");
        writer.WriteLine("try");
        writer.WriteLine("{");
        writer.Indent++;
        WriteReadsAndCall(writer, signature, callTarget, ownedArguments);
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine("finally");
        writer.WriteLine("{");
        writer.Indent++;
        if (isAwaitable)
        {
            // The awaited work may still use the arguments. They are released when it completes.
            writer.WriteLine("if (!isHandedOver)");
            writer.WriteLine("{");
            writer.Indent++;
        }
        foreach (string argument in ownedArguments)
            writer.WriteLine($"{argument}.Dispose();");
        if (isAwaitable)
        {
            writer.Indent--;
            writer.WriteLine("}");
        }
        writer.Indent--;
        writer.WriteLine("}");

        if (isAwaitable)
            WriteReleaseAfter(writer, signature, ownedArguments);
    }

    private static void WriteReadsAndCall(
        IndentedTextWriter writer,
        InteropSignature signature,
        string callTarget,
        List<string> ownedArguments
    )
    {
        var arguments = new List<string>(signature.Parameters.Length);
        int luauIndex = 0;
        foreach (InteropType parameter in signature.Parameters)
        {
            if (parameter.Type is LuauInteropKind.CancellationToken)
            {
                arguments.Add("args.CancellationToken");
                continue;
            }

            luauIndex++;
            if (parameter.Type is LuauInteropKind.LuauValue)
            {
                writer.WriteLine(
                    $"if (!args.TryReadLuauValue(parameterIndex: {luauIndex}, out a{luauIndex}, out error))"
                );
                writer.WriteLine($"    return {LuauReturn}.Error(error);");
            }
            else
            {
                writer.WriteMultiLine(LuauMarshalEmitter.GenerateParameterRead(luauIndex, parameter));
            }
            arguments.Add($"a{luauIndex}");
        }

        string callExpression = $"{callTarget}({string.Join(", ", arguments)})";
        if (signature.Awaitable is AwaitableReturnKind.None)
        {
            if (signature.ReturnTypes.Length == 0)
            {
                writer.WriteLine($"{callExpression};");
                writer.WriteLine($"return {LuauReturn}.Ok();");
                return;
            }

            writer.WriteLine($"var returns = {callExpression};");
            WriteReturnOk(writer, signature);
            return;
        }

        // Asked before the call, so the callback never starts work that cannot be awaited. The arguments are read
        // above, so that work never touches the callback-scoped args.
        writer.WriteLine("if (!args.TryGetAwaiter(out global::Darp.Luau.LuauAwaiter awaiter, out error))");
        writer.WriteLine($"    return {LuauReturn}.Error(error);");
        string pending =
            signature.Awaitable is AwaitableReturnKind.ValueTask
                ? callExpression
                : $"new {GetValueTaskType(signature)}({callExpression})";
        if (ownedArguments.Count > 0)
        {
            writer.WriteLine($"var pending = {pending};");
            writer.WriteLine("isHandedOver = true;");
            writer.WriteLine($"return awaiter.Await(ReleaseAfter(pending, {string.Join(", ", ownedArguments)}));");
            return;
        }

        if (signature.ReturnTypes.Length == 0)
        {
            writer.WriteLine($"return awaiter.Await({pending});");
            return;
        }

        // The state converts the result itself, which saves the callback an async state machine of its own.
        writer.WriteLine("return awaiter.Await(");
        writer.Indent++;
        writer.WriteLine($"{pending},");
        if (GetOwnedResults(signature).Count == 0)
        {
            writer.WriteLine($"static returns => {FormatOk(signature)}");
        }
        else
        {
            writer.WriteLine("static returns =>");
            writer.WriteLine("{");
            writer.Indent++;
            WriteReturnOk(writer, signature);
            writer.Indent--;
            writer.WriteLine("}");
        }
        writer.Indent--;
        writer.WriteLine(");");
    }

    /// <summary> Writes the local function that awaits the work and then releases the arguments it was given. </summary>
    private static void WriteReleaseAfter(
        IndentedTextWriter writer,
        InteropSignature signature,
        List<string> ownedArguments
    )
    {
        string parameters = string.Join(", ", ownedArguments.Select(static x => $"{LuauValue} {x}"));
        writer.WriteLineNoTabs("");
        writer.WriteLine(
            $"static async global::System.Threading.Tasks.ValueTask<{LuauReturn}> ReleaseAfter({GetValueTaskType(signature)} pending, {parameters})"
        );
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("try");
        writer.WriteLine("{");
        writer.Indent++;
        if (signature.ReturnTypes.Length == 0)
        {
            writer.WriteLine("await pending;");
            writer.WriteLine($"return {LuauReturn}.Ok();");
        }
        else
        {
            writer.WriteLine("var returns = await pending;");
            WriteReturnOk(writer, signature);
        }
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine("finally");
        writer.WriteLine("{");
        writer.Indent++;
        foreach (string argument in ownedArguments)
            writer.WriteLine($"{argument}.Dispose();");
        writer.Indent--;
        writer.WriteLine("}");
        writer.Indent--;
        writer.WriteLine("}");
    }

    /// <summary> Returns <c>LuauReturn.Ok(...)</c> of the local <c>returns</c> and releases what it handed over. </summary>
    private static void WriteReturnOk(IndentedTextWriter writer, InteropSignature signature)
    {
        List<string> ownedResults = GetOwnedResults(signature);
        if (ownedResults.Count == 0)
        {
            writer.WriteLine($"return {FormatOk(signature)};");
            return;
        }

        writer.WriteLine("try");
        writer.WriteLine("{");
        writer.WriteLine($"    return {FormatOk(signature)};");
        writer.WriteLine("}");
        writer.WriteLine("finally");
        writer.WriteLine("{");
        foreach (string result in ownedResults)
            writer.WriteLine($"    {result}.Dispose();");
        writer.WriteLine("}");
    }

    /// <summary> The locals of the <c>LuauValue</c> arguments, which hold a reference each. </summary>
    private static List<string> GetOwnedArguments(InteropSignature signature)
    {
        var ownedArguments = new List<string>();
        int luauIndex = 0;
        foreach (InteropType parameter in signature.Parameters)
        {
            if (parameter.Type is LuauInteropKind.CancellationToken)
                continue;
            luauIndex++;
            if (parameter.Type is LuauInteropKind.LuauValue)
                ownedArguments.Add($"a{luauIndex}");
        }
        return ownedArguments;
    }

    /// <summary> The expressions of the returned <c>LuauValue</c>s in a local named <c>returns</c>. </summary>
    private static List<string> GetOwnedResults(InteropSignature signature)
    {
        var ownedResults = new List<string>();
        bool isTuple = signature.ReturnTypes.Length > 1;
        for (int i = 0; i < signature.ReturnTypes.Length; i++)
        {
            InteropType returnType = signature.ReturnTypes[i];
            if (returnType.Type is not LuauInteropKind.LuauValue)
                continue;
            string value = isTuple ? $"returns.Item{i + 1}" : "returns";
            ownedResults.Add(returnType.IsNullable ? $"{value}?" : value);
        }
        return ownedResults;
    }

    /// <summary> <c>LuauReturn.Ok(...)</c> of the values in a local named <c>returns</c>. </summary>
    private static string FormatOk(InteropSignature signature)
    {
        bool isTuple = signature.ReturnTypes.Length > 1;
        IEnumerable<string> values = signature.ReturnTypes.Select(
            (x, i) => LuauMarshalEmitter.FormatIntoLuauExpression(isTuple ? $"returns.Item{i + 1}" : "returns", x)
        );
        return $"{LuauReturn}.Ok({string.Join(", ", values)})";
    }

    private static string GetValueTaskType(InteropSignature signature)
    {
        const string valueTask = "global::System.Threading.Tasks.ValueTask";
        return EmitterHelper.GetResultType(signature) is { } resultType ? $"{valueTask}<{resultType}>" : valueTask;
    }
}
