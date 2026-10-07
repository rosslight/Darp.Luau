using System.CodeDom.Compiler;
using Darp.Luau.Generator.Helpers;

namespace Darp.Luau.Generator.GeneratedExports;

internal static class ExportCallbackEmitter
{
    public static void WriteStaticMethodBody(IndentedTextWriter writer, GeneratedExportMethodIr method)
    {
        WriteMethodBody(writer, method, method.IsStatic ? method.ContainingTypeName : "this");
    }

    public static void WriteMethodBody(
        IndentedTextWriter writer,
        GeneratedExportMethodIr method,
        string receiverExpression
    )
    {
        int luauArgumentCount = method.Parameters.Count(static x => x.Type is not LuauInteropKind.CancellationToken);
        writer.WriteLine($"if (!args.TryValidateArgumentCount({luauArgumentCount}, out string? error))");
        writer.WriteLine("    return global::Darp.Luau.LuauReturn.Error(error);");

        var arguments = new List<string>(method.Parameters.Length);
        int luauIndex = 0;
        foreach (InteropType parameter in method.Parameters)
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

        string callExpression = $"{receiverExpression}.{method.ManagedName}({string.Join(", ", arguments)})";
        if (method.Awaitable is AwaitableReturnKind.None)
        {
            WriteReturn(writer, method, callExpression);
            return;
        }

        // The arguments are read above, so the awaited work never touches the callback-scoped args.
        writer.WriteLine($"return global::Darp.Luau.LuauReturn.Await(Complete({callExpression}));");
        writer.WriteLine();
        writer.WriteLine(
            "static async global::System.Threading.Tasks.ValueTask<global::Darp.Luau.LuauReturn> Complete("
                + $"{GetAwaitableTypeName(method)} pending)"
        );
        writer.WriteLine("{");
        writer.Indent++;
        WriteReturn(writer, method, "await pending");
        writer.Indent--;
        writer.WriteLine("}");
    }

    private static void WriteReturn(IndentedTextWriter writer, GeneratedExportMethodIr method, string resultExpression)
    {
        if (method.ReturnTypes.Length == 0)
        {
            writer.WriteLine($"{resultExpression};");
            writer.WriteLine("return global::Darp.Luau.LuauReturn.Ok();");
            return;
        }

        writer.WriteLine($"var returns = {resultExpression};");
        if (method.ReturnTypes.Length == 1)
        {
            writer.WriteLine(
                $"return global::Darp.Luau.LuauReturn.Ok({LuauMarshalEmitter.FormatIntoLuauExpression("returns", method.ReturnTypes[0])});"
            );
            return;
        }

        writer.WriteLine(
            $"return global::Darp.Luau.LuauReturn.Ok({string.Join(", ", method.ReturnTypes.Select((x, i) => LuauMarshalEmitter.FormatIntoLuauExpression($"returns.Item{i + 1}", x)))});"
        );
    }

    private static string GetAwaitableTypeName(GeneratedExportMethodIr method)
    {
        string awaitable =
            method.Awaitable is AwaitableReturnKind.Task
                ? "global::System.Threading.Tasks.Task"
                : "global::System.Threading.Tasks.ValueTask";
        return method.ReturnTypes.Length switch
        {
            0 => awaitable,
            1 => $"{awaitable}<{EmitterHelper.GetDotnetType(method.ReturnTypes[0])}>",
            _ => $"{awaitable}<({string.Join(", ", method.ReturnTypes.Select(EmitterHelper.GetDotnetType))})>",
        };
    }
}
