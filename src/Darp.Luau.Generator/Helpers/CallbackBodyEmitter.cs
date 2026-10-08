using System.CodeDom.Compiler;

namespace Darp.Luau.Generator.Helpers;

internal static class CallbackBodyEmitter
{
    /// <summary>
    /// Writes the body of a callback that reads its arguments from <c>args</c>, calls <paramref name="callTarget"/>
    /// and returns the result as a <c>LuauReturn</c>.
    /// </summary>
    public static void Write(IndentedTextWriter writer, InteropSignature signature, string callTarget)
    {
        int luauArgumentCount = signature.Parameters.Count(static x => x.Type is not LuauInteropKind.CancellationToken);
        writer.WriteLine($"if (!args.TryValidateArgumentCount({luauArgumentCount}, out string? error))");
        writer.WriteLine("    return global::Darp.Luau.LuauReturn.Error(error);");

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
            writer.WriteMultiLine(LuauMarshalEmitter.GenerateParameterRead(luauIndex, parameter));
            arguments.Add($"a{luauIndex}");
        }

        string callExpression = $"{callTarget}({string.Join(", ", arguments)})";
        if (signature.Awaitable is AwaitableReturnKind.None)
        {
            if (signature.ReturnTypes.Length == 0)
            {
                writer.WriteLine($"{callExpression};");
                writer.WriteLine("return global::Darp.Luau.LuauReturn.Ok();");
                return;
            }

            writer.WriteLine($"var returns = {callExpression};");
            writer.WriteLine($"return {FormatOk(signature)};");
            return;
        }

        // Asked before the call, so the callback never starts work that cannot be awaited. The arguments are read
        // above, so that work never touches the callback-scoped args.
        writer.WriteLine("if (!args.TryGetAwaiter(out global::Darp.Luau.LuauAwaiter awaiter, out error))");
        writer.WriteLine("    return global::Darp.Luau.LuauReturn.Error(error);");
        string pending =
            signature.Awaitable is AwaitableReturnKind.ValueTask
                ? callExpression
                : $"new {GetValueTaskType(signature)}({callExpression})";
        if (signature.ReturnTypes.Length == 0)
        {
            writer.WriteLine($"return awaiter.Await({pending});");
            return;
        }

        // The state converts the result itself, which saves the callback an async state machine of its own.
        writer.WriteLine("return awaiter.Await(");
        writer.Indent++;
        writer.WriteLine($"{pending},");
        writer.WriteLine($"static returns => {FormatOk(signature)}");
        writer.Indent--;
        writer.WriteLine(");");
    }

    /// <summary> <c>LuauReturn.Ok(...)</c> of the values in a local named <c>returns</c>. </summary>
    private static string FormatOk(InteropSignature signature)
    {
        bool isTuple = signature.ReturnTypes.Length > 1;
        IEnumerable<string> values = signature.ReturnTypes.Select(
            (x, i) => LuauMarshalEmitter.FormatIntoLuauExpression(isTuple ? $"returns.Item{i + 1}" : "returns", x)
        );
        return $"global::Darp.Luau.LuauReturn.Ok({string.Join(", ", values)})";
    }

    private static string GetValueTaskType(InteropSignature signature)
    {
        const string valueTask = "global::System.Threading.Tasks.ValueTask";
        return EmitterHelper.GetResultType(signature) is { } resultType ? $"{valueTask}<{resultType}>" : valueTask;
    }
}
