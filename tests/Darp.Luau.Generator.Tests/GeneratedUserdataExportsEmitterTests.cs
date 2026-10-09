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
    public async Task Userdata_WithANameAndAStaticSide_ShouldRegisterTheNameFunctionsAndValues()
    {
        const string code = """
            using Darp.Luau;

            [LuauUserdata("Vec2")]
            public sealed partial class Vec2(double x, double y)
            {
                [LuauMember("x")]
                public double X { get; set; } = x;

                [LuauMember("y")]
                public double Y { get; } = y;

                [LuauMember("new")]
                public static Vec2 Create(double x, double y) => new(x, y);

                [LuauMember("zero")]
                public static Vec2 Zero { get; } = new(0, 0);

                [LuauMember("dimensions")]
                public static int Dimensions => 2;

                // The static side is a table of its own, so it can use a name of the instance.
                [LuauMember("x")]
                public static Vec2 UnitX => new(1, 0);
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsSource(code);
    }

    [Fact]
    public async Task Userdata_WithMetamethods_ShouldChooseTheOverloadByTheOperandsBeforeReadingThem()
    {
        const string code = """
            using System.Threading;
            using System.Threading.Tasks;
            using Darp.Luau;

            [LuauUserdata("Vec2")]
            public sealed partial class Vec2(double x, double y)
            {
                public double X { get; } = x;
                public double Y { get; } = y;

                [LuauMetamethod(LuauMetamethod.Add)]
                public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);

                [LuauMetamethod(LuauMetamethod.Unm)]
                public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);

                // An instance method: the instance is the left operand.
                [LuauMetamethod(LuauMetamethod.Mul)]
                private Vec2 Scale(double factor) => new(X * factor, Y * factor);

                // A static method names both operands, so the instance can be the right one.
                [LuauMetamethod(LuauMetamethod.Mul)]
                private static Vec2 Scale(double factor, Vec2 vec) => vec.Scale(factor);

                [LuauMetamethod(LuauMetamethod.Eq)]
                private bool SameAs(Vec2 other) => X == other.X && Y == other.Y;

                [LuauMetamethod(LuauMetamethod.Len)]
                private int Count() => 2;

                [LuauMetamethod(LuauMetamethod.ToString)]
                private string Describe() => $"({X}, {Y})";

                [LuauMetamethod(LuauMetamethod.Index)]
                private double? Component(int index) => index switch { 1 => X, 2 => Y, _ => null };

                [LuauMetamethod(LuauMetamethod.NewIndex)]
                private void Reject(string name, LuauValue value) => throw new System.InvalidOperationException(name);

                [LuauMetamethod(LuauMetamethod.Call)]
                private double Dot(Vec2 other) => X * other.X + Y * other.Y;

                [LuauMetamethod(LuauMetamethod.Call)]
                private async Task<double> ScaledSumAsync(double factor, string? unit, CancellationToken cancellationToken)
                {
                    await Task.Delay(1, cancellationToken);
                    return (X + Y) * factor;
                }
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsSource(code);
    }

    [Fact]
    public async Task Userdata_WithMetamethodsOnOperators_ShouldCallEachOperatorByItsToken()
    {
        const string code = """
            using Darp.Luau;

            [LuauUserdata("Amount")]
            public sealed partial class Amount
            {
                [LuauMetamethod(LuauMetamethod.Add)]
                public static Amount operator +(Amount a, Amount b) => a;

                [LuauMetamethod(LuauMetamethod.Sub)]
                public static Amount operator -(Amount a, Amount b) => a;

                [LuauMetamethod(LuauMetamethod.Mul)]
                public static Amount operator *(Amount a, double factor) => a;

                [LuauMetamethod(LuauMetamethod.Div)]
                public static Amount operator /(Amount a, double divisor) => a;

                [LuauMetamethod(LuauMetamethod.Mod)]
                public static Amount operator %(Amount a, double divisor) => a;

                [LuauMetamethod(LuauMetamethod.Unm)]
                public static Amount operator -(Amount a) => a;

                [LuauMetamethod(LuauMetamethod.Eq)]
                public static bool operator ==(Amount a, Amount b) => true;

                public static bool operator !=(Amount a, Amount b) => false;

                [LuauMetamethod(LuauMetamethod.Lt)]
                public static bool operator <(Amount a, Amount b) => true;

                public static bool operator >(Amount a, Amount b) => false;

                [LuauMetamethod(LuauMetamethod.Le)]
                public static bool operator <=(Amount a, Amount b) => true;

                public static bool operator >=(Amount a, Amount b) => false;
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsSource(code);
    }

    [Fact]
    public async Task Userdata_WithMetamethodOverloads_ShouldTestTheLuauTypeOfEveryOperand()
    {
        const string code = """
            using Darp.Luau;

            [LuauUserdata("Coin")]
            public sealed partial class Coin { }

            [LuauUserdata("Bag")]
            public sealed partial class Bag
            {
                [LuauMetamethod(LuauMetamethod.Add)]
                public Bag Add(Bag other) => this;

                // Told apart from a bag by the managed type of the userdata.
                [LuauMetamethod(LuauMetamethod.Add)]
                public Bag Add(Coin coin) => this;

                [LuauMetamethod(LuauMetamethod.Sub)]
                public Bag Without(bool everything) => this;

                [LuauMetamethod(LuauMetamethod.Sub)]
                public Bag Without(LuauTableView items) => this;

                [LuauMetamethod(LuauMetamethod.Mod)]
                public Bag Keep(LuauFunctionView predicate) => this;

                [LuauMetamethod(LuauMetamethod.Pow)]
                public Bag Fill(LuauBufferView bytes) => this;

                [LuauMetamethod(LuauMetamethod.IDiv)]
                public Bag Split(double? parts) => this;

                [LuauMetamethod(LuauMetamethod.Concat)]
                public string Join(string text) => text;

                [LuauMetamethod(LuauMetamethod.Concat)]
                public static string Join(string text, Bag bag) => text;

                [LuauMetamethod(LuauMetamethod.NewIndex)]
                public void Put(string name, double amount) { }

                [LuauMetamethod(LuauMetamethod.NewIndex)]
                public void Put(int slot, double amount) { }

                // Any userdata, and any value: the first tests that it is one, the second only the instance.
                [LuauMetamethod(LuauMetamethod.Mul)]
                public Bag Merge(LuauUserdataView other) => this;

                [LuauMetamethod(LuauMetamethod.Div)]
                public Bag Share(LuauValue with) => this;
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
