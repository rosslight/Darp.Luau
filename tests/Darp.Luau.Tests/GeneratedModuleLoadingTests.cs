using Shouldly;

namespace Darp.Luau.Tests;

public sealed class GeneratedModuleLoadingTests
{
    [Fact]
    public void GeneratedStaticModule_ShouldExposeValuesAndFunctions()
    {
        using var state = new LuauState();

        state.RegisterModule(ArcadeModule.ModuleName, ArcadeModule.OnLoad);
        state
            .Load(
                """
                local arcade = require("arcade")
                sum = arcade.add_score(40, 2)
                tokens = arcade.tokens
                difficulty = arcade.difficulty
                """
            )
            .Execute();

        state.Globals.GetNumber("sum").ShouldBe(42);
        state.Globals.GetNumber("tokens").ShouldBe(7);
        state.Globals.GetNumber("difficulty").ShouldBe((int)ArcadeDifficulty.Hard);
    }

    [Fact]
    public void GeneratedStaticModule_ShouldWorkUnderNone()
    {
        using var state = new LuauState(LuauLibraries.None);

        state.RegisterModule(ArcadeModule.ModuleName, ArcadeModule.OnLoad);

        int result = state.Load("""return require("arcade").add_score(40, 2)""").Execute<int>();

        result.ShouldBe(42);
    }

    [Fact]
    public void GeneratedStaticModule_ShouldExportMembersNamedLikeGeneratedLocalsOrKeywords()
    {
        using var state = new LuauState();

        state.RegisterModule(NamesModule.ModuleName, NamesModule.OnLoad);
        (int module, int stateValue, int result) = state
            .Load(
                """
                local names = require("names")
                return names.module, names.state, names.give_back(41)
                """
            )
            .Execute<int, int, int>();

        // The generated OnLoad has parameters named 'state' and 'module' itself.
        module.ShouldBe(1);
        stateValue.ShouldBe(2);
        result.ShouldBe(42);
    }

    [Fact]
    public void GeneratedModule_ShouldCreateNestedTables()
    {
        using var state = new LuauState();

        _ = state.RegisterModule<WorkshopModule>();
        state.Load("bundle = require('workshop').tools.hammer(5)").Execute();

        state.Globals.GetNumber("bundle").ShouldBe(10);
    }

    [Fact]
    public void GeneratedModule_ShouldRoundtripManagedUserdata()
    {
        using var state = new LuauState();

        _ = state.RegisterModule<GuildModule>();
        state.Load("champion = require('guild').heroes.create('Ada')").Execute();

        state.Globals.GetUserdata<HeroCard>("champion").Name.ShouldBe("Ada");
    }

    [Fact]
    public void GeneratedModule_ShouldPreserveDuplicateModuleCheck()
    {
        using var state = new LuauState();

        state.RegisterModule(ArcadeModule.ModuleName, ArcadeModule.OnLoad);

        Should.Throw<InvalidOperationException>(() =>
            state.RegisterModule(ArcadeModule.ModuleName, ArcadeModule.OnLoad)
        );
    }
}

public enum ArcadeDifficulty
{
    Normal = 1,
    Hard = 2,
}

[LuauModule("arcade")]
public static partial class ArcadeModule
{
    [LuauMember("tokens")]
    public static int Tokens => 7;

    [LuauMember("difficulty")]
    public static ArcadeDifficulty Difficulty => ArcadeDifficulty.Hard;

    [LuauMember("add_score")]
    public static int AddScore(int current, int bonus) => current + bonus;
}

[LuauModule("names")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Style",
    "IDE1006:Naming Styles",
    Justification = "The names collide with generated locals and a keyword on purpose."
)]
public static partial class NamesModule
{
    [LuauMember("module")]
    public static int module => 1;

    [LuauMember("state")]
    public static int state => 2;

    [LuauMember("give_back")]
    public static int @return(int value) => value + 1;
}

[LuauModule("workshop")]
public sealed partial class WorkshopModule
{
    [LuauMember("tools.hammer")]
    public int MakeHammer(int size) => size * 2;
}

[LuauModule("guild")]
public sealed partial class GuildModule
{
    [LuauMember("heroes.create")]
    public HeroCard CreateHero(string name) => new(name);
}

public sealed class HeroCard(string name) : ILuauUserdata<HeroCard>
{
    public string Name { get; } = name;

    public static void Register(LuauUserdataRegistry<HeroCard> registry) =>
        registry.AddGetter("name", static (self, _) => LuauReturnSingle.Ok(self.Name));
}
