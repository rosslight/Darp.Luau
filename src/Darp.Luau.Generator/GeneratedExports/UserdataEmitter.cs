using System.CodeDom.Compiler;
using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Generator.Helpers;
using Microsoft.CodeAnalysis.CSharp;

namespace Darp.Luau.Generator.GeneratedExports;

internal static class UserdataEmitter
{
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

        writer.WriteLine(
            ExportEmitterHelper.WithBaseList(
                model.TypeDeclaration,
                $"global::Darp.Luau.ILuauUserData<{model.ManagedTypeName}>"
            )
        );
        writer.WriteLine("{");
        writer.Indent++;
        WriteOnIndex(writer, model);
        writer.WriteLine();
        WriteOnSetIndex(writer, model);
        writer.WriteLine();
        WriteOnMethodCall(writer, model);
        writer.Indent--;
        writer.WriteLine("}");

        ExportEmitterHelper.WriteNamespaceEnd(writer, hasNamespace);
    }

    private static void WriteOnIndex(IndentedTextWriter writer, GeneratedExportSurfaceIr model)
    {
        GeneratedExportPropertyIr[] properties = model.Members.OfType<GeneratedExportPropertyIr>().ToArray();
        writer.WriteLine(RoslynHelper.GetGeneratedVersionAttribute());
        if (properties.Length == 0)
        {
            writer.WriteLine(
                "public static global::Darp.Luau.LuauReturnSingle OnIndex("
                    + $"{model.ManagedTypeName} self, in global::Darp.Luau.LuauState state, in global::System.ReadOnlySpan<char> fieldName) "
                    + "=> global::Darp.Luau.LuauReturnSingle.NotHandled;"
            );
            return;
        }

        writer.WriteLine(
            "public static global::Darp.Luau.LuauReturnSingle OnIndex("
                + $"{model.ManagedTypeName} self, in global::Darp.Luau.LuauState state, in global::System.ReadOnlySpan<char> fieldName)"
        );
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("return fieldName switch");
        writer.WriteLine("{");
        writer.Indent++;
        foreach (
            GeneratedExportPropertyIr property in properties.OrderBy(static x => x.LuauName, StringComparer.Ordinal)
        )
        {
            string keyLiteral = SymbolDisplay.FormatLiteral(property.LuauName, quote: true);
            if (property.Getter is null)
            {
                writer.WriteLine(
                    $"{keyLiteral} => global::Darp.Luau.LuauReturnSingle.Error({SymbolDisplay.FormatLiteral($"userdata member '{property.LuauName}' is write-only", quote: true)}),"
                );
                continue;
            }

            writer.WriteLine(
                $"{keyLiteral} => global::Darp.Luau.LuauReturnSingle.Ok({LuauMarshalEmitter.FormatIntoLuauExpression($"self.{property.ManagedName}", property.Getter.Type)}),"
            );
        }

        writer.WriteLine("_ => global::Darp.Luau.LuauReturnSingle.NotHandled,");
        writer.Indent--;
        writer.WriteLine("};");
        writer.Indent--;
        writer.WriteLine("}");
    }

    private static void WriteOnSetIndex(IndentedTextWriter writer, GeneratedExportSurfaceIr model)
    {
        GeneratedExportPropertyIr[] properties = model.Members.OfType<GeneratedExportPropertyIr>().ToArray();
        writer.WriteLine(RoslynHelper.GetGeneratedVersionAttribute());
        if (properties.Length == 0)
        {
            writer.WriteLine(
                "public static global::Darp.Luau.LuauOutcome OnSetIndex("
                    + $"{model.ManagedTypeName} self, global::Darp.Luau.LuauArgsSingle args, in global::System.ReadOnlySpan<char> fieldName) "
                    + "=> global::Darp.Luau.LuauOutcome.NotHandledError;"
            );
            return;
        }

        writer.WriteLine(
            "public static global::Darp.Luau.LuauOutcome OnSetIndex("
                + $"{model.ManagedTypeName} self, global::Darp.Luau.LuauArgsSingle args, in global::System.ReadOnlySpan<char> fieldName)"
        );
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("switch (fieldName)");
        writer.WriteLine("{");
        writer.Indent++;
        foreach (
            GeneratedExportPropertyIr property in properties.OrderBy(static x => x.LuauName, StringComparer.Ordinal)
        )
        {
            string keyLiteral = SymbolDisplay.FormatLiteral(property.LuauName, quote: true);
            writer.WriteLine($"case {keyLiteral}:");
            writer.WriteLine("{");
            writer.Indent++;
            if (property.Setter is null)
            {
                writer.WriteLine(
                    $"return global::Darp.Luau.LuauOutcome.Error({SymbolDisplay.FormatLiteral($"userdata member '{property.LuauName}' is read-only", quote: true)});"
                );
            }
            else
            {
                writer.WriteMultiLine(LuauMarshalEmitter.GenerateSingleArgumentRead("value", property.Setter.Type));
                writer.WriteLine($"self.{property.ManagedName} = value;");
                writer.WriteLine("return global::Darp.Luau.LuauOutcome.Ok();");
            }

            writer.Indent--;
            writer.WriteLine("}");
        }

        writer.WriteLine("default:");
        writer.WriteLine("    return global::Darp.Luau.LuauOutcome.NotHandledError;");
        writer.Indent--;
        writer.WriteLine("}");
        writer.Indent--;
        writer.WriteLine("}");
    }

    private static void WriteOnMethodCall(IndentedTextWriter writer, GeneratedExportSurfaceIr model)
    {
        GeneratedExportMethodIr[] methods = model.Members.OfType<GeneratedExportMethodIr>().ToArray();
        writer.WriteLine(RoslynHelper.GetGeneratedVersionAttribute());
        if (methods.Length == 0)
        {
            writer.WriteLine(
                "public static global::Darp.Luau.LuauReturn OnMethodCall("
                    + $"{model.ManagedTypeName} self, global::Darp.Luau.LuauArgs args, in global::System.ReadOnlySpan<char> methodName) "
                    + "=> global::Darp.Luau.LuauReturn.NotHandledError;"
            );
            return;
        }

        writer.WriteLine(
            "public static global::Darp.Luau.LuauReturn OnMethodCall("
                + $"{model.ManagedTypeName} self, global::Darp.Luau.LuauArgs args, in global::System.ReadOnlySpan<char> methodName)"
        );
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("switch (methodName)");
        writer.WriteLine("{");
        writer.Indent++;
        foreach (GeneratedExportMethodIr method in methods.OrderBy(static x => x.LuauName, StringComparer.Ordinal))
        {
            string keyLiteral = SymbolDisplay.FormatLiteral(method.LuauName, quote: true);
            writer.WriteLine($"case {keyLiteral}:");
            writer.WriteLine("{");
            writer.Indent++;
            CallbackBodyEmitter.Write(writer, method.Signature, $"self.{method.ManagedName}");
            writer.Indent--;
            writer.WriteLine("}");
        }

        writer.WriteLine("default:");
        writer.WriteLine("    return global::Darp.Luau.LuauReturn.NotHandledError;");
        writer.Indent--;
        writer.WriteLine("}");
        writer.Indent--;
        writer.WriteLine("}");
    }
}
