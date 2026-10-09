using System.CodeDom.Compiler;
using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Generator.Helpers;
using Microsoft.CodeAnalysis.CSharp;

namespace Darp.Luau.Generator.GeneratedExports;

internal static class UserdataEmitter
{
    private const string LuauReturn = "global::Darp.Luau.LuauReturn";
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

        foreach (GeneratedMetamethodIr metamethod in model.Metamethods)
            WriteMetamethod(writer, metamethod);
    }

    private static void WriteMetamethod(IndentedTextWriter writer, GeneratedMetamethodIr metamethod)
    {
        writer.WriteLine($"registry.AddMetamethod(global::Darp.Luau.LuauMetamethod.{metamethod.Name}, static args =>");
        writer.WriteLine("{");
        writer.Indent++;

        // Operands of the wrong type are no error of the method but of the script: it is told what it combined,
        // and an index that is not a member reads as nil like any other.
        string? mismatch = GetOperandMismatchResult(metamethod);
        if (mismatch is null)
        {
            WriteMetamethodCall(writer, metamethod.Overloads[0]);
        }
        else
        {
            bool takesEveryOperand = false;
            foreach (GeneratedMetamethodOverloadIr overload in metamethod.Overloads)
            {
                // The overload is chosen before an operand is read, so that no other one has to undo a read.
                string? check = FormatOperandCheck(metamethod, overload);
                if (check is null)
                {
                    // Overloads differ in what they take, so one that takes everything is the only one.
                    WriteMetamethodCall(writer, overload);
                    takesEveryOperand = true;
                    break;
                }

                writer.WriteLine($"if ({check})");
                writer.WriteLine("{");
                writer.Indent++;
                WriteMetamethodCall(writer, overload);
                writer.Indent--;
                writer.WriteLine("}");
            }
            if (!takesEveryOperand)
                writer.WriteLine($"return {mismatch};");
        }

        writer.Indent--;
        writer.WriteLine("});");
    }

    /// <summary>
    /// Gets what a metamethod returns when no overload takes the operands. Null when Luau only calls the metamethod
    /// with the operands its one method takes.
    /// </summary>
    private static string? GetOperandMismatchResult(GeneratedMetamethodIr metamethod)
    {
        const string firstType = "{args.GetTypeName(1)}";
        const string secondType = "{args.GetTypeName(2)}";
        string? operation = metamethod.Name switch
        {
            "Add" => "add",
            "Sub" => "sub",
            "Mul" => "mul",
            "Div" => "div",
            "Mod" => "mod",
            "Pow" => "pow",
            "IDiv" => "idiv",
            _ => null,
        };
        if (operation is not null)
        {
            return $"{LuauReturn}.Error($\"attempt to perform arithmetic ({operation}) on {firstType} and {secondType}\")";
        }

        return metamethod.Name switch
        {
            "Concat" => $"{LuauReturn}.Error($\"attempt to concatenate {firstType} with {secondType}\")",
            "Index" => $"{LuauReturn}.Ok()",
            "NewIndex" when metamethod.Overloads.Length > 1 =>
                $"{LuauReturn}.Error($\"attempt to set a userdata member with a {secondType} key and a {{args.GetTypeName(3)}} value\")",
            _ when metamethod.Overloads.Length > 1 =>
                $"{LuauReturn}.Error(\"no overload of the metamethod accepts these arguments\")",
            _ => null,
        };
    }

    /// <summary> Writes the test whether an overload takes the operands, or returns null when it takes any. </summary>
    private static string? FormatOperandCheck(GeneratedMetamethodIr metamethod, GeneratedMetamethodOverloadIr overload)
    {
        const string valueType = "global::Darp.Luau.LuauValueType";
        var checks = new List<string>();
        int index = 0;
        foreach (InteropType operand in overload.Signature.Parameters)
        {
            if (operand.Type is LuauInteropKind.CancellationToken)
                continue;

            index++;
            string? check = operand.Type switch
            {
                LuauInteropKind.ManagedUserdata => $"args.IsUserdata<{operand.OriginalTypeName}>({index})",
                LuauInteropKind.Boolean => $"args.GetValueType({index}) is {valueType}.Boolean",
                LuauInteropKind.LuauTableView => $"args.GetValueType({index}) is {valueType}.Table",
                LuauInteropKind.LuauFunctionView => $"args.GetValueType({index}) is {valueType}.Function",
                LuauInteropKind.LuauBufferView => $"args.GetValueType({index}) is {valueType}.Buffer",
                LuauInteropKind.LuauUserdataView => $"args.GetValueType({index}) is {valueType}.Userdata",
                _ when MetamethodAnalyzer.IsNumber(operand.Type) => $"args.GetValueType({index}) is {valueType}.Number",
                _ when MetamethodAnalyzer.IsString(operand.Type) => $"args.GetValueType({index}) is {valueType}.String",
                _ => null,
            };
            if (check is null)
                continue;

            checks.Add(operand.IsNullable ? $"({check} || args.GetValueType({index}) is {valueType}.Nil)" : check);
        }

        // Luau passes a fixed number of operands to every metamethod but a call.
        if (metamethod.Name == "Call")
            checks.Insert(0, $"args.ArgumentCount == {index}");
        return checks.Count == 0 ? null : string.Join(" && ", checks);
    }

    private static void WriteMetamethodCall(IndentedTextWriter writer, GeneratedMetamethodOverloadIr overload)
    {
        string target = overload.CallTarget;
        CallbackBodyEmitter.Write(
            writer,
            overload.Signature,
            overload.CallKind switch
            {
                MetamethodCallKind.StaticMethod => arguments => $"{target}({string.Join(", ", arguments)})",
                MetamethodCallKind.InstanceMethod => arguments =>
                    $"{arguments[0]}.{target}({string.Join(", ", arguments.Skip(1))})",
                MetamethodCallKind.BinaryOperator => arguments => $"({arguments[0]} {target} {arguments[1]})",
                MetamethodCallKind.UnaryOperator => arguments => $"({target}{arguments[0]})",
                _ => throw new InvalidOperationException($"Unsupported metamethod call '{overload.CallKind}'."),
            }
        );
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
