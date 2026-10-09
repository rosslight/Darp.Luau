namespace Darp.Luau.Generator.Tests;

public class GeneratedExportsTests
{
    [Fact]
    public async Task ValidModuleAndUserdata_ShouldReportNoDiagnostics()
    {
        const string code = """
            using Darp.Luau;

            [LuauUserdata("Character")]
            public sealed partial class Character
            {
                [LuauMember("name")]
                public string Name { get; set; } = "unknown";

                [LuauMember("rename")]
                public void Rename(string name) => Name = name;
            }

            [LuauModule("game")]
            public static partial class GameModule
            {
                [LuauMember("answer")]
                public static int Answer => 42;

                [LuauMember("clamp")]
                public static int Clamp(int value, int min, int max) => value;
            }
            """;

        await VerifyHelper.VerifyGeneratedExports(code);
    }

    [Fact]
    public async Task PartialModuleAcrossFiles_ShouldReportNoDiagnostics()
    {
        string[] sources =
        [
            """
                using Darp.Luau;

                [LuauModule("mathx")]
                public static partial class MathX
                {
                    [LuauMember("pi")]
                    public static double Pi => 3.14;
                }
                """,
            """
                using Darp.Luau;

                public static partial class MathX
                {
                    [LuauMember("clamp")]
                    public static int Clamp(int value, int min, int max) => value;
                }
                """,
        ];

        await VerifyHelper.VerifyGeneratedExports(sources);
    }

    [Fact]
    public async Task NonPartialExportTypes_ShouldFail()
    {
        const string code = """
            using Darp.Luau;

            [LuauModule("game")]
            public static class GameModule
            {
            }

            [LuauUserdata("Player")]
            public sealed class Player
            {
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsWithErrors(code);
    }

    [Fact]
    public async Task FileLocalExportTypes_ShouldFail()
    {
        const string code = """
            using Darp.Luau;

            [LuauModule("file_local")]
            file static partial class FileLocalModule
            {
            }

            [LuauUserdata("FileLocalUserdata")]
            file sealed partial class FileLocalUserdata
            {
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsWithErrors(code);
    }

    [Fact]
    public async Task GenericUserdataType_ShouldFail()
    {
        const string code = """
            using Darp.Luau;

            [LuauUserdata("Player")]
            public sealed partial class Player<T>
            {
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsWithErrors(code);
    }

    [Fact]
    public async Task NestedUserdataType_ShouldFail()
    {
        const string code = """
            using Darp.Luau;

            public static class Container
            {
                [LuauUserdata("Player")]
                public sealed partial class Player
                {
                }
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsWithErrors(code);
    }

    [Fact]
    public async Task ModulePathConflictsAndInvalidPaths_ShouldFail()
    {
        const string code = """
            using Darp.Luau;

            [LuauModule("aa")]
            public static partial class AnalyzerModule
            {
                [LuauMember("Field")]
                public static int Field => 1;

                [LuauMember("Field.u8")]
                public static int CreateU8() => 8;

                [LuauMember("Field..u16")]
                public static int CreateU16() => 16;
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsWithErrors(code);
    }

    [Fact]
    public async Task InvalidLuauDotPathSegments_ShouldWarn()
    {
        const string code = """
            using Darp.Luau;

            [LuauUserdata("Character")]
            public sealed partial class Character
            {
                [LuauMember("foo bar")]
                public string BracketName { get; set; } = "";

                [LuauMember("local")]
                public string KeywordName { get; set; } = "";

                [LuauMember("field_1")]
                public string ValidName { get; set; } = "";
            }

            [LuauModule("game")]
            public static partial class GameModule
            {
                [LuauMember("123abc")]
                public static int StartsWithDigit => 1;

                [LuauMember("foo-bar")]
                public static int HasHyphen => 2;

                [LuauMember("tools.end")]
                public static int KeywordSegment() => 3;

                [LuauMember("bad-name.end.123abc")]
                public static int MultipleInvalidSegments() => 4;

                [LuauMember("foo")]
                public static int Foo => 5;

                [LuauMember("_private")]
                public static int Private => 6;

                [LuauMember("Field.u8")]
                public static int CreateU8() => 8;
            }
            """;

        await VerifyHelper.VerifyGeneratedExports(code);
    }

    [Fact]
    public async Task ModulePropertyAndMethodShapeDiagnostics_ShouldFail()
    {
        const string code = """
            using Darp.Luau;

            [LuauModule("game")]
            public static partial class GameModule
            {
                [LuauMember("current")]
                public static int Current { get; set; }

                [LuauMember("create")]
                public static int Create(int value = 0) => value;
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsWithErrors(code);
    }

    [Fact]
    public async Task NestedAwaitableReturnAndCancellationTokenProperty_ShouldFail()
    {
        const string code = """
            using System.Threading;
            using System.Threading.Tasks;
            using Darp.Luau;

            [LuauUserdata("Player")]
            public sealed partial class Player
            {
                [LuauMember("token")]
                public CancellationToken Token { get; set; }

                [LuauMember("load")]
                public Task<Task<int>> LoadAsync() => Task.FromResult(Task.FromResult(1));
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsWithErrors(code);
    }

    [Fact]
    public async Task AsyncVoidMembers_ShouldFail()
    {
        const string code = """
            using System.Threading.Tasks;
            using Darp.Luau;

            [LuauUserdata("Player")]
            public sealed partial class Player
            {
                [LuauMember("save")]
                public async void Save() => await Task.Yield();
            }

            [LuauModule("game")]
            public static partial class GameModule
            {
                [LuauMember("tick")]
                public static async void Tick() => await Task.Yield();
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsWithErrors(code);
    }

    [Fact]
    public async Task MembersGeneratedCodeCannotUse_ShouldFail()
    {
        const string code = """
            #nullable enable
            using System.Threading.Tasks;
            using Darp.Luau;

            public interface INamed
            {
                string Name { get; set; }
            }

            [LuauUserdata("Player")]
            public sealed partial class Player : INamed
            {
                [LuauMember("name")]
                string INamed.Name { get; set; } = "";

                [LuauMember("level", Access = LuauPropertyAccess.ReadWrite)]
                public int Level { get; init; }

                [LuauMember("ping")]
                partial void Ping();

                [LuauMember("load")]
                public Task<int>? Load() => null;
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsWithErrors(code);
    }

    [Fact]
    public async Task CancellationTokenWithADefaultValue_ShouldReportNoDiagnostics()
    {
        const string code = """
            using System.Threading;
            using System.Threading.Tasks;
            using Darp.Luau;

            [LuauUserdata("Player")]
            public sealed partial class Player
            {
                [LuauMember("wait")]
                public Task Wait(int milliseconds, CancellationToken cancellationToken = default) =>
                    Task.Delay(milliseconds, cancellationToken);
            }
            """;

        await VerifyHelper.VerifyGeneratedExports(code);
    }

    [Fact]
    public async Task ModuleFunctionByteSpanReturn_ShouldFail()
    {
        const string code = """
            using System;
            using Darp.Luau;

            [LuauModule("game")]
            public static partial class GameModule
            {
                [LuauMember("bytes")]
                public static ReadOnlySpan<byte> Bytes() => "abc"u8;
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsWithErrors(code);
    }

    [Fact]
    public async Task InstanceModuleProperty_ShouldFail()
    {
        const string code = """
            using Darp.Luau;

            [LuauModule("game")]
            public sealed partial class GameModule
            {
                public int CurrentValue { get; set; }

                [LuauMember("current")]
                public int Current => CurrentValue;
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsWithErrors(code);
    }

    [Fact]
    public async Task UserdataAttributeAndManualRegister_ShouldFail()
    {
        const string code = """
            using System;
            using System.Collections.Generic;
            using Darp.Luau;

            [LuauUserdata("Player")]
            public sealed partial class Player : ILuauUserdata<Player>
            {
                [LuauMember("stats")]
                public Dictionary<string, int> Stats { get; } = new();

                [LuauMember("stats.total")]
                public int Total => 1;

                public static void Register(LuauUserdataRegistry<Player> registry) { }
            }

            // Without the interface in its base list, the method would be replaced by the generated one unnoticed.
            [LuauUserdata("Enemy")]
            public sealed partial class Enemy
            {
                public static void Register(LuauUserdataRegistry<Enemy> registry) { }
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsWithErrors(code);
    }

    [Fact]
    public async Task MethodsNamedRegisterThatAreNotTheRegistration_ShouldReportNoDiagnostics()
    {
        const string code = """
            using Darp.Luau;

            [LuauUserdata("Player")]
            public sealed partial class Player
            {
                public static int Register(LuauUserdataRegistry<Player> registry) => 0;

                public static void Register<T>(LuauUserdataRegistry<Player> registry) { }

                public static void Register(ref LuauUserdataRegistry<Player> registry) { }

                public static void Register(LuauUserdataRegistry<Enemy> registry) { }
            }

            public class Enemy : ILuauUserdata<Enemy>
            {
                public static void Register(LuauUserdataRegistry<Enemy> registry) { }
            }

            // The registration it inherits describes an enemy, not a boss.
            [LuauUserdata("Boss")]
            public sealed partial class Boss : Enemy
            {
            }
            """;

        await VerifyHelper.VerifyGeneratedExports(code);
    }

    [Fact]
    public async Task GenericModuleType_ShouldFail()
    {
        const string code = """
            using Darp.Luau;

            [LuauModule("vault")]
            public sealed partial class VaultModule<T>
            {
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsWithErrors(code);
    }

    [Fact]
    public async Task NestedModuleType_ShouldFail()
    {
        const string code = """
            using Darp.Luau;

            public static class Container
            {
                [LuauModule("quests")]
                public static partial class QuestModule
                {
                }
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsWithErrors(code);
    }

    [Fact]
    public async Task ReservedModuleName_ShouldFail()
    {
        const string code = """
            using Darp.Luau;

            [LuauModule("./game")]
            public static partial class GameModule
            {
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsWithErrors(code);
    }

    [Fact]
    public async Task UserdataTypeNameThatIsNoLuauIdentifier_ShouldFail()
    {
        const string code = """
            using Darp.Luau;

            [LuauUserdata("")]
            public sealed partial class Unnamed { }

            // typeof(value) must not claim to be a built-in type.
            [LuauUserdata("number")]
            public sealed partial class Number { }

            [LuauUserdata("Game.Player")]
            public sealed partial class Dotted { }

            [LuauUserdata("2d")]
            public sealed partial class StartsWithADigit { }

            // A Luau keyword.
            [LuauUserdata("end")]
            public sealed partial class Keyword { }

            [LuauUserdata("Player_2")]
            public sealed partial class Valid { }
            """;

        await VerifyHelper.VerifyGeneratedExportsWithErrors(code);
    }

    [Fact]
    public async Task MetamethodsWithTheWrongShape_ShouldFail()
    {
        const string code = """
            using System.Threading.Tasks;
            using Darp.Luau;

            [LuauUserdata("Money")]
            public sealed partial class Money
            {
                // No operand is the type itself.
                [LuauMetamethod(LuauMetamethod.Add)]
                public static double Sum(double a, double b) => a + b;

                [LuauMetamethod(LuauMetamethod.Eq)]
                public int SameAs(Money other) => 0;

                // Luau cannot wait inside an operator.
                [LuauMetamethod(LuauMetamethod.Sub)]
                public Task<Money> SubtractAsync(Money other) => Task.FromResult(other);

                [LuauMetamethod(LuauMetamethod.ToString)]
                public string? Describe() => null;

                [LuauMetamethod(LuauMetamethod.Len)]
                public string Length() => "";

                [LuauMetamethod(LuauMetamethod.NewIndex)]
                public bool Store(string key, double value) => true;

                [LuauMetamethod(LuauMetamethod.Call)]
                public static void Run(double amount) { }

                [LuauMember("total")]
                public static double Total { get; set; }
            }

            [LuauModule("bank")]
            public static partial class BankModule
            {
                [LuauMetamethod(LuauMetamethod.Call)]
                public static void Open() { }
            }

            // Registered by hand: the generator never looks at the attribute.
            public sealed class Account : ILuauUserdata<Account>
            {
                public static void Register(LuauUserdataRegistry<Account> registry) { }

                [LuauMetamethod(LuauMetamethod.ToString)]
                public string Describe() => "account";
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsWithErrors(code);
    }

    [Fact]
    public async Task MetamethodsWithTheWrongOperands_ShouldFail()
    {
        const string code = """
            using System.Threading;
            using Darp.Luau;

            [LuauUserdata("Money")]
            public sealed partial class Money
            {
                // Luau only calls it for two values of the type.
                [LuauMetamethod(LuauMetamethod.Lt)]
                public bool LessThan(double amount) => false;

                [LuauMetamethod(LuauMetamethod.Unm)]
                public static Money Negate(double amount) => new();

                [LuauMetamethod(LuauMetamethod.Len)]
                public int Length(int unit) => 0;

                [LuauMetamethod(LuauMetamethod.ToString)]
                public static string Describe(double amount) => "";

                [LuauMetamethod(LuauMetamethod.Index)]
                public double Read() => 0;

                [LuauMetamethod(LuauMetamethod.NewIndex)]
                public void Store(string key) { }

                [LuauMetamethod(LuauMetamethod.Add)]
                public void Deposit(Money other) { }

                [LuauMetamethod(LuauMetamethod.Pow)]
                public void Invert() { }

                [LuauMetamethod(LuauMetamethod.Concat)]
                public void Append(string text) { }

                // Nothing can cancel an operator: the script does not wait in it.
                [LuauMetamethod(LuauMetamethod.Mul)]
                public Money Scale(double factor, CancellationToken cancellationToken) => this;
            }

            [LuauUserdata("Wallet")]
            public sealed partial class Wallet
            {
                [LuauMetamethod(LuauMetamethod.Unm)]
                public void Empty() { }

                [LuauMetamethod(LuauMetamethod.Index)]
                public void Read(string key) { }
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsWithErrors(code);
    }

    [Fact]
    public async Task MetamethodsGeneratedCodeCannotCall_ShouldFail()
    {
        const string code = """
            #pragma warning disable CS0660, CS0661 // Equals and GetHashCode are of no interest here
            using Darp.Luau;

            public interface IDescribed
            {
                string Describe();
            }

            [LuauUserdata("Money")]
            public sealed partial class Money : IDescribed
            {
                [LuauMetamethod(LuauMetamethod.ToString)]
                string IDescribed.Describe() => "";

                [LuauMetamethod(LuauMetamethod.ToString)]
                public static implicit operator string(Money money) => "";

                [LuauMetamethod(LuauMetamethod.Len)]
                partial void Count();

                // An operator only declares the metamethod it stands for.
                [LuauMetamethod(LuauMetamethod.Unm)]
                public static Money operator ++(Money money) => money;

                [LuauMetamethod(LuauMetamethod.Pow)]
                public static Money operator ^(Money a, Money b) => a;

                [LuauMetamethod(LuauMetamethod.Sub)]
                public static Money operator +(Money a, Money b) => a;

                public static bool operator ==(Money a, Money b) => true;

                [LuauMetamethod(LuauMetamethod.Eq)]
                public static bool operator !=(Money a, Money b) => false;

                [LuauMetamethod(LuauMetamethod.Add)]
                public Money Add(System.Uri source) => this;

                [LuauMetamethod((LuauMetamethod)999)]
                public Money Unknown() => this;
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsWithErrors(code);
    }

    [Fact]
    public async Task MetamethodOverloadsThatLuauCannotTellApart_ShouldFail()
    {
        const string code = """
            using Darp.Luau;

            [LuauUserdata("Coin")]
            public partial class Coin { }

            [LuauUserdata("GoldCoin")]
            public sealed partial class GoldCoin : Coin { }

            [LuauUserdata("Money")]
            public sealed partial class Money
            {
                [LuauMetamethod(LuauMetamethod.Add)]
                public Money Add(Coin coin) => this;

                // A gold coin is read as a coin as well.
                [LuauMetamethod(LuauMetamethod.Add)]
                public Money Add(GoldCoin coin) => this;

                [LuauMetamethod(LuauMetamethod.Mul)]
                public Money Scale(int factor) => this;

                // An int and a double are both a number in Luau.
                [LuauMetamethod(LuauMetamethod.Mul)]
                public Money Scale(double factor) => this;

                [LuauMetamethod(LuauMetamethod.Div)]
                public Money Split(string? parts) => this;

                // Both take nil.
                [LuauMetamethod(LuauMetamethod.Div)]
                public Money Split(double? parts) => this;

                [LuauMetamethod(LuauMetamethod.Call)]
                public void Run(double amount) { }

                // A LuauValue takes every value, but a call with two arguments is told apart by their number.
                [LuauMetamethod(LuauMetamethod.Call)]
                public void Run(LuauValue amount) { }

                [LuauMetamethod(LuauMetamethod.Call)]
                public void Run(double amount, double times) { }
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsWithErrors(code);
    }

    [Fact]
    public async Task GeneratedMemberNameConflicts_ShouldFail()
    {
        const string code = """
            using Darp.Luau;

            [LuauModule("ledger")]
            public sealed partial class LedgerModule
            {
                public const string ModuleName = "ledger";
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsWithErrors(code);
    }
}
