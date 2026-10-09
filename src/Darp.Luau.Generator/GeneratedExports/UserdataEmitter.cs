using System.CodeDom.Compiler;
using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Generator.Helpers;
using Microsoft.CodeAnalysis.CSharp;

namespace Darp.Luau.Generator.GeneratedExports;

internal static class UserdataEmitter
{
    private const string LuauReturnSingle = "global::Darp.Luau.LuauReturnSingle";
    private const string LuauOutcome = "global::Darp.Luau.LuauOutcome";

    public static bool TryEmit(GeneratedExportSurfaceIr model, [NotNullWhen(true)] out string? source)
    {
        if (model.Kind != LuauExportedTypeKind.Userdata)
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

        string userdataInterface = $"global::Darp.Luau.ILuauUserdata<{model.ManagedTypeName}>";
        writer.WriteLine(ExportEmitterHelper.WithBaseList(model.TypeDeclaration, userdataInterface));
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(RoslynHelper.GetGeneratedVersionAttribute());
        // Implemented explicitly: the type keeps the name 'Register' for its own members.
        writer.WriteLine(
            $"static void {userdataInterface}.Register(global::Darp.Luau.LuauUserdataRegistry<{model.ManagedTypeName}> registry)"
        );
        writer.WriteLine("{");
        writer.Indent++;
        WriteRegistrations(writer, model);
        writer.Indent--;
        writer.WriteLine("}");
        writer.Indent--;
        writer.WriteLine("}");

        ExportEmitterHelper.WriteNamespaceEnd(writer, hasNamespace);
    }

    private static void WriteRegistrations(IndentedTextWriter writer, GeneratedExportSurfaceIr model)
    {
        if (model.UserdataTypeName is not null)
            writer.WriteLine(
                $"registry.TypeName = {SymbolDisplay.FormatLiteral(model.UserdataTypeName, quote: true)};"
            );

        foreach (
            GeneratedExportMemberIr member in model.Members.OrderBy(static x => x.LuauName, StringComparer.Ordinal)
        )
        {
            switch (member)
            {
                case GeneratedExportPropertyIr property:
                    WriteProperty(writer, property);
                    break;
                case GeneratedExportMethodIr method:
                    WriteMethod(writer, method);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported generated userdata member '{member.GetType().Name}'."
                    );
            }
        }
    }

    private static void WriteProperty(IndentedTextWriter writer, GeneratedExportPropertyIr property)
    {
        string keyLiteral = SymbolDisplay.FormatLiteral(property.LuauName, quote: true);
        if (property.Getter is not null)
        {
            string value = LuauMarshalEmitter.FormatIntoLuauExpression(
                $"self.{property.ManagedName}",
                property.Getter.Type
            );
            writer.WriteLine($"registry.AddGetter({keyLiteral}, static (self, _) => {LuauReturnSingle}.Ok({value}));");
        }

        if (property.Setter is null)
            return;

        writer.WriteLine($"registry.AddSetter({keyLiteral}, static (self, args) =>");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteMultiLine(LuauMarshalEmitter.GenerateSingleArgumentRead("value", property.Setter.Type));
        writer.WriteLine($"self.{property.ManagedName} = value;");
        writer.WriteLine($"return {LuauOutcome}.Ok();");
        writer.Indent--;
        writer.WriteLine("});");
    }

    private static void WriteMethod(IndentedTextWriter writer, GeneratedExportMethodIr method)
    {
        string keyLiteral = SymbolDisplay.FormatLiteral(method.LuauName, quote: true);
        writer.WriteLine($"registry.AddMethod({keyLiteral}, static (self, args) =>");
        writer.WriteLine("{");
        writer.Indent++;
        CallbackBodyEmitter.Write(writer, method.Signature, $"self.{method.ManagedName}");
        writer.Indent--;
        writer.WriteLine("});");
    }
}
