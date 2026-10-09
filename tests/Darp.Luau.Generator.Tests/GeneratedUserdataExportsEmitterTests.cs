namespace Darp.Luau.Generator.Tests;

public sealed class GeneratedUserdataExportsEmitterTests
{
    [Fact]
    public async Task Userdata_WithPropertiesAndMethods_ShouldGenerateRegister()
    {
        const string code = """
            using Darp.Luau;

            public enum CharacterKind
            {
                Hero = 1,
                Vendor = 2,
            }

            [LuauUserdata("Character")]
            public sealed partial class Character
            {
                private string? _secret;

                [LuauMember("kind")]
                public CharacterKind Kind { get; set; } = CharacterKind.Hero;

                [LuauMember("name")]
                public string Name { get; set; } = "unknown";

                [LuauMember("score", Access = LuauPropertyAccess.ReadOnly)]
                public int Score { get; set; } = 10;

                [LuauMember("secret", Access = LuauPropertyAccess.WriteOnly)]
                public string? Secret
                {
                    get => _secret;
                    set => _secret = value;
                }

                [LuauMember("rename")]
                public (string Name, int Score) Rename(string name, int score)
                {
                    Name = name;
                    Score = score;
                    return (Name, Score);
                }

                [LuauMember("reset")]
                public void Reset()
                {
                    Score = 0;
                }
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsSource(code);
    }

    [Fact]
    public async Task Userdata_WithAwaitableMethods_ShouldAwaitThemAndPassTheCancellationToken()
    {
        const string code = """
            using System.Threading;
            using System.Threading.Tasks;
            using Darp.Luau;

            [LuauUserdata("Player")]
            public sealed partial class Player
            {
                [LuauMember("save")]
                public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

                [LuauMember("load")]
                public Task<string?> LoadAsync(CancellationToken cancellationToken, string slot) =>
                    Task.FromResult<string?>(slot);

                [LuauMember("stats")]
                public ValueTask<(int Wins, Player? Rival)> GetStatsAsync(int season) => new((season, null));
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsSource(code);
    }

    [Fact]
    public async Task Userdata_WithKeywordNamesAndAnInitOnlyProperty_ShouldEscapeAndOnlyRead()
    {
        const string code = """
            using Darp.Luau;

            [LuauUserdata("Player")]
            public sealed partial class Player
            {
                [LuauMember("kind")]
                public int @class { get; set; }

                [LuauMember("level")]
                public int Level { get; init; }

                [LuauMember("fallback")]
                public int @default(int @in) => @in;
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsSource(code);
    }

    [Fact]
    public async Task Userdata_WithAKeywordTypeName_ShouldGenerate()
    {
        const string code = """
            using Darp.Luau;

            namespace @event
            {
                [LuauUserdata("class")]
                public sealed partial class @class
                {
                    [LuauMember("level")]
                    public int Level { get; set; }
                }
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsSource(code);
    }

    [Fact]
    public async Task Module_WithGeneratedUserdata_ShouldGenerateOnLoadAndUserdataRegister()
    {
        const string code = """
            using Darp.Luau;

            [LuauUserdata("HeroCard")]
            public sealed partial class HeroCard
            {
                [LuauMember("name")]
                public string Name { get; set; } = "";
            }

            [LuauModule("guild")]
            public static partial class GuildModule
            {
                [LuauMember("heroes.create")]
                public static HeroCard CreateHero(string name) => new() { Name = name };

                [LuauMember("heroes.rename")]
                public static void RenameHero(HeroCard hero, string name)
                {
                    hero.Name = name;
                }
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsSource(code);
    }
}
