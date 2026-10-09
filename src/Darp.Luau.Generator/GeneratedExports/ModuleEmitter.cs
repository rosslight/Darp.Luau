using System.CodeDom.Compiler;
using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Generator.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Darp.Luau.Generator.GeneratedExports;

internal static class ModuleEmitter
{
    public static bool TryEmit(GeneratedExportSurfaceIr model, [NotNullWhen(true)] out string? source)
    {
        if (model.ModuleRoot is null || string.IsNullOrWhiteSpace(model.ModuleName))
        {
            source = null;
            return false;
        }

        using var stringWriter = new StringWriter();
        using var writer = new IndentedTextWriter(stringWriter);
        WriteFile(writer, model);
        source = stringWriter.ToString();
        return true;
    }

    private static void WriteFile(IndentedTextWriter writer, GeneratedExportSurfaceIr model)
    {
        ExportEmitterHelper.WriteFileHeader(writer);
        bool hasNamespace = ExportEmitterHelper.WriteNamespaceStart(writer, model.NamespaceName);

        writer.WriteLine(
            ExportEmitterHelper.WithBaseList(
                model.TypeDeclaration,
                model.IsStatic ? null : $"global::Darp.Luau.ILuauModule<{model.ManagedTypeName}>"
            )
        );
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(RoslynHelper.GetGeneratedVersionAttribute());
        writer.WriteLine(
            $"public static string ModuleName => {SymbolDisplay.FormatLiteral(model.ModuleName!, quote: true)};"
        );
        writer.WriteLine();
        writer.WriteLine(RoslynHelper.GetGeneratedVersionAttribute());
        writer.WriteLine(
            $"public {GetOnLoadModifier(model)}void OnLoad(global::Darp.Luau.LuauState state, in global::Darp.Luau.LuauTable module)"
        );
        writer.WriteLine("{");
        writer.Indent++;
        var localNames = new ExportEmitterHelper.LocalNameAllocator();
        WriteModuleNodeContents(writer, model.ModuleRoot!, "module", localNames);
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

            WriteMember(writer, child.Member, tableVariableName, localNames);
        }
    }

    private static void WriteMember(
        IndentedTextWriter writer,
        GeneratedExportMemberIr member,
        string tableVariableName,
        ExportEmitterHelper.LocalNameAllocator localNames
    )
    {
        switch (member)
        {
            case GeneratedExportPropertyIr property:
                WriteProperty(writer, property, tableVariableName);
                break;
            case GeneratedExportMethodIr method:
                WriteMethod(writer, method, tableVariableName, localNames);
                break;
            default:
                throw new InvalidOperationException($"Unsupported generated module member '{member.GetType().Name}'.");
        }
    }

    private static void WriteProperty(
        IndentedTextWriter writer,
        GeneratedExportPropertyIr property,
        string tableVariableName
    )
    {
        GeneratedExportAccessorIr accessor =
            property.Getter ?? throw new InvalidOperationException("Generated module properties must have a getter.");
        string keyLiteral = SymbolDisplay.FormatLiteral(property.PathSegments[^1], quote: true);
        writer.WriteLine(
            $"{tableVariableName}.Set({keyLiteral}, {LuauMarshalEmitter.FormatIntoLuauExpression(property.ManagedName, accessor.Type)});"
        );
    }

    private static void WriteMethod(
        IndentedTextWriter writer,
        GeneratedExportMethodIr method,
        string tableVariableName,
        ExportEmitterHelper.LocalNameAllocator localNames
    )
    {
        string functionVariableName = localNames.Next();
        string keyLiteral = SymbolDisplay.FormatLiteral(method.PathSegments[^1], quote: true);

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

    private static string GetOnLoadModifier(GeneratedExportSurfaceIr model) =>
        model.IsStatic ? "static " : string.Empty;
}
