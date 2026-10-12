using Shouldly;

namespace Darp.Luau.Tests.Require;

public sealed class ScriptModuleIntegrationTests
{
    private const string ScriptPath = "./Require/scripts";

    [Fact]
    public void ActualFiles_ShouldResolveMainRequireChain()
    {
        using var state = new LuauState();
        state.EnableScriptModules();

        string fileName = Path.Combine(ScriptPath, "main.luau");
        (int sum, int difference, LuauTable table) = state.LoadFile(fileName).Execute<int, int, LuauTable>();

        sum.ShouldBe(3);
        difference.ShouldBe(5);
        state.Globals.TryGet("result", out int globalResult).ShouldBeTrue();
        globalResult.ShouldBe(15);
        using (table)
        {
            table.TryGet("sum", out int sumInTable).ShouldBeTrue();
            sumInTable.ShouldBe(3);
            table.TryGet("difference", out int differenceInTable).ShouldBeTrue();
            differenceInTable.ShouldBe(5);
        }
    }

    [Fact]
    public void ActualFiles_ShouldResolveLuaurcAlias()
    {
        using var state = new LuauState();
        state.EnableScriptModules();

        string path = Path.Combine(ScriptPath, "config_tests/with_config/src/alias_requirer.luau");
        LuauTable result = state.LoadFile(path).Execute<LuauTable>();

        using (result)
        {
            result.Get<string>(1).ShouldBe("result from dependency");
        }
    }

    [Fact]
    public void ActualFiles_ShouldReportAmbiguousConfig()
    {
        using var state = new LuauState();
        state.EnableScriptModules();

        string path = Path.Combine(ScriptPath, "config_tests/config_ambiguity/requirer.luau");
        LuaException exception = Should.Throw<LuaException>(() => state.LoadFile(path).Execute<LuauTable>());

        exception.Message.ShouldContain("could not resolve alias \"dep\" (ambiguous configuration file)");
    }
}
