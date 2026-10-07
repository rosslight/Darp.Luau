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
            WriteReturn(writer, signature, callExpression);
            return;
        }

        // The arguments are read above, so the awaited work never touches the callback-scoped args.
        writer.WriteLine($"return global::Darp.Luau.LuauReturn.Await(Complete({callExpression}));");
        writer.WriteLine();
        writer.WriteLine(
            "static async global::System.Threading.Tasks.ValueTask<global::Darp.Luau.LuauReturn> Complete("
                + $"{EmitterHelper.GetReturnType(signature)} pending)"
        );
        writer.WriteLine("{");
        writer.Indent++;
        WriteReturn(writer, signature, "await pending");
        writer.Indent--;
        writer.WriteLine("}");
    }

    private static void WriteReturn(IndentedTextWriter writer, InteropSignature signature, string resultExpression)
    {
        if (signature.ReturnTypes.Length == 0)
        {
            writer.WriteLine($"{resultExpression};");
            writer.WriteLine("return global::Darp.Luau.LuauReturn.Ok();");
            return;
        }

        writer.WriteLine($"var returns = {resultExpression};");
        if (signature.ReturnTypes.Length == 1)
        {
            writer.WriteLine(
                $"return global::Darp.Luau.LuauReturn.Ok({LuauMarshalEmitter.FormatIntoLuauExpression("returns", signature.ReturnTypes[0])});"
            );
            return;
        }

        writer.WriteLine(
            $"return global::Darp.Luau.LuauReturn.Ok({string.Join(", ", signature.ReturnTypes.Select((x, i) => LuauMarshalEmitter.FormatIntoLuauExpression($"returns.Item{i + 1}", x)))});"
        );
    }
}
