using Darp.Luau.Generator.Helpers;
using Microsoft.CodeAnalysis;

namespace Darp.Luau.Generator.GeneratedExports;

internal static class ExportTreeBuilder
{
    /// <summary> Puts the members of a module into the tables their paths name. </summary>
    public static GeneratedModuleExportNodeIr BuildModuleTree(
        List<AnalyzedExportMember> members,
        List<Diagnostic> diagnostics
    )
    {
        var root = new MutableModuleExportNode(string.Empty);
        foreach (AnalyzedExportMember member in members)
            AddModuleMember(root, member, diagnostics);

        return Freeze(root);
    }

    private static void AddModuleMember(
        MutableModuleExportNode root,
        AnalyzedExportMember member,
        List<Diagnostic> diagnostics
    )
    {
        MutableModuleExportNode current = root;
        for (int i = 0; i < member.PathSegments.Length; i++)
        {
            string segment = member.PathSegments[i];
            bool isLeaf = i == member.PathSegments.Length - 1;
            if (!current.Children.TryGetValue(segment, out MutableModuleExportNode? child))
            {
                child = new MutableModuleExportNode(segment);
                current.Children.Add(segment, child);
            }

            if (isLeaf)
            {
                if (child.Children.Count > 0)
                {
                    diagnostics.Add(
                        Diagnostic.Create(
                            DiagnosticDescriptors.LuauExportPathConflictDescriptor,
                            member.Location,
                            member.Member.LuauName,
                            GetNamespaceConflictPath(child, member.Member.LuauName)
                        )
                    );
                    return;
                }

                if (child.Member is not null)
                    return;

                child.Member = member;
                return;
            }

            if (child.Member is not null)
            {
                diagnostics.Add(
                    Diagnostic.Create(
                        DiagnosticDescriptors.LuauExportPathConflictDescriptor,
                        member.Location,
                        child.Member.Member.LuauName,
                        member.Member.LuauName
                    )
                );
                return;
            }

            current = child;
        }
    }

    private static string GetNamespaceConflictPath(MutableModuleExportNode node, string prefix)
    {
        if (node.Member is not null)
            return node.Member.Member.LuauName;

        foreach (KeyValuePair<string, MutableModuleExportNode> child in node.Children)
            return GetNamespaceConflictPath(child.Value, prefix + "." + child.Key);

        return prefix;
    }

    private static GeneratedModuleExportNodeIr Freeze(MutableModuleExportNode node)
    {
        return new GeneratedModuleExportNodeIr(
            node.Name,
            node.Member?.Member,
            node.Children.Values.Select(Freeze).ToImmutableEquatableArray()
        );
    }

    private sealed class MutableModuleExportNode(string name)
    {
        public string Name { get; } = name;

        public AnalyzedExportMember? Member { get; set; }

        public Dictionary<string, MutableModuleExportNode> Children { get; } = new(StringComparer.Ordinal);
    }
}
