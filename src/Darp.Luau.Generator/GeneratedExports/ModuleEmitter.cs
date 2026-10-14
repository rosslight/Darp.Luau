using System.CodeDom.Compiler;
using Darp.Luau.Generator.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Darp.Luau.Generator.GeneratedExports;

internal static class ModuleEmitter
{
    public static string Emit(GeneratedModuleIr model)
    {
        using var stringWriter = new StringWriter();
        using var writer = new IndentedTextWriter(stringWriter);
        WriteFile(writer, model);
        return stringWriter.ToString();
    }

    private static void WriteFile(IndentedTextWriter writer, GeneratedModuleIr model)
    {
        GeneratedExportTypeIr type = model.Type;
        ExportEmitterHelper.WriteFileHeader(writer);
        bool hasNamespace = ExportEmitterHelper.WriteNamespaceStart(writer, type.NamespaceName);

        writer.WriteLine(
            ExportEmitterHelper.WithBaseList(
                type.TypeDeclaration,
                type.IsStatic ? null : $"global::Darp.Luau.ILuauModule<{type.ManagedTypeName}>"
            )
        );
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(RoslynHelper.GetGeneratedVersionAttribute());
        writer.WriteLine(
            $"public static string ModuleName => {SymbolDisplay.FormatLiteral(model.ModuleName, quote: true)};"
        );
        writer.WriteLine();
        writer.WriteLine(RoslynHelper.GetGeneratedVersionAttribute());
        writer.WriteLine(
            $"public {(type.IsStatic ? "static " : string.Empty)}void OnLoad(global::Darp.Luau.LuauState state, in global::Darp.Luau.LuauTable module)"
        );
        writer.WriteLine("{");
        writer.Indent++;
        var localNames = new ExportEmitterHelper.LocalNameAllocator();
        WriteModuleNodeContents(writer, model.Root, "module", localNames);
        writer.Indent--;
        writer.WriteLine("}");
        writer.Indent--;
        writer.WriteLine("}");

        ExportEmitterHelper.WriteNamespaceEnd(writer, hasNamespace);
    }

    private static void WriteModuleNodeContents(
        IndentedTextWriter writer,
        GeneratedModuleExportNodeIr node,
        string tableVariableName,
        ExportEmitterHelper.LocalNameAllocator localNames
    )
    {
        foreach (GeneratedModuleExportNodeIr child in node.Children.OrderBy(static x => x.Name, StringComparer.Ordinal))
        {
            if (child.Member is null)
            {
                string nestedTableName = localNames.Next();
                writer.WriteLine($"using global::Darp.Luau.LuauTable {nestedTableName} = state.CreateTable();");
                WriteModuleNodeContents(writer, child, nestedTableName, localNames);
                writer.WriteLine(
                    $"{tableVariableName}.Set({SymbolDisplay.FormatLiteral(child.Name, quote: true)}, {nestedTableName});"
                );
                writer.WriteLine();
                continue;
            }

            string keyLiteral = SymbolDisplay.FormatLiteral(child.Name, quote: true);
            switch (child.Member)
            {
                case GeneratedExportPropertyIr property:
                    writer.WriteLine(
                        $"{tableVariableName}.Set({keyLiteral}, {LuauMarshalEmitter.FormatIntoLuauExpression(property.ManagedName, property.Type)});"
                    );
                    break;
                case GeneratedExportMethodIr method:
                    WriteMethod(writer, method, tableVariableName, keyLiteral, localNames);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported generated module member '{child.Member.GetType().Name}'."
                    );
            }
        }
    }

    private static void WriteMethod(
        IndentedTextWriter writer,
        GeneratedExportMethodIr method,
        string tableVariableName,
        string keyLiteral,
        ExportEmitterHelper.LocalNameAllocator localNames
    )
    {
        string functionVariableName = localNames.Next();

        writer.WriteLine(
            $"using global::Darp.Luau.LuauFunction {functionVariableName} = state.CreateFunctionManual(args =>"
        );
        writer.WriteLine("{");
        writer.Indent++;
        string receiver = method.IsStatic ? method.ContainingTypeName : "this";
        CallbackBodyEmitter.Write(writer, method.Signature, $"{receiver}.{method.ManagedName}");

        writer.Indent--;
        writer.WriteLine("});");
        writer.WriteLine($"{tableVariableName}.Set({keyLiteral}, {functionVariableName});");
    }
}
