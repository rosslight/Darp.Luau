namespace Darp.Luau.Generator.Tests;

public sealed class GeneratedModuleExportsEmitterTests
{
    [Fact]
    public async Task StaticModule_ShouldGenerateOnLoadMethod()
    {
        const string code = """
            using Darp.Luau;

            public enum Difficulty
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
                public static Difficulty Difficulty => Difficulty.Hard;

                [LuauMember("add_score")]
                public static int AddScore(int current, int bonus) => current + bonus;
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsSource(code);
    }

    [Fact]
    public async Task StaticModule_WithAwaitableFunction_ShouldAwaitIt()
    {
        const string code = """
            using System.Threading;
            using System.Threading.Tasks;
            using Darp.Luau;

            [LuauModule("net")]
            public static partial class NetModule
            {
                [LuauMember("fetch")]
                public static async Task<string> FetchAsync(string url, CancellationToken cancellationToken)
                {
                    await Task.Delay(1, cancellationToken);
                    return url;
                }
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsSource(code);
    }

    [Fact]
    public async Task StaticModule_WithMethodNameCollidingWithGeneratedLocal_ShouldGenerateQualifiedCall()
    {
        const string code = """
            using Darp.Luau;

            [LuauModule("collisions")]
            public static partial class CollisionModule
            {
                [LuauMember("returns")]
                public static int returns() => 7;
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsSource(code);
    }

    [Fact]
    public async Task InstanceModule_ShouldGenerateOnLoadMethod()
    {
        const string code = """
            using System;
            using Darp.Luau;

            public sealed class HeroCard : ILuauUserData<HeroCard>
            {
                public HeroCard(string name) => Name = name;

                public string Name { get; }

                public static LuauReturnSingle OnIndex(HeroCard self, in LuauState state, in ReadOnlySpan<char> fieldName) =>
                    fieldName switch
                    {
                        "name" => LuauReturnSingle.Ok(self.Name),
                        _ => LuauReturnSingle.NotHandled,
                    };

                public static LuauOutcome OnSetIndex(HeroCard self, LuauArgsSingle args, in ReadOnlySpan<char> fieldName) =>
                    LuauOutcome.NotHandledError;

                public static LuauReturn OnMethodCall(HeroCard self, LuauArgs functionArgs, in ReadOnlySpan<char> methodName) =>
                    LuauReturn.NotHandledError;
            }

            [LuauModule("guild")]
            public sealed partial class GuildModule
            {
                [LuauMember("heroes.create")]
                public HeroCard CreateHero(string name) => new(name);
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsSource(code);
    }

    [Fact]
    public async Task Module_WithInvalidMember_ShouldGenerateValidMembers()
    {
        const string code = """
            using Darp.Luau;

            [LuauModule("arcade")]
            public static partial class ArcadeModule
            {
                [LuauMember("tokens")]
                public static int Tokens => 7;

                [LuauMember("unsupported")]
                public static int Unsupported(int value = 0) => value;

                [LuauMember("add_score")]
                public static int AddScore(int current, int bonus) => current + bonus;
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsSourceWithErrors(code);
    }

    [Fact]
    public async Task Module_WithRecoverableMemberErrors_ShouldGenerateOnLoadMethodAndValidMembers()
    {
        const string code = """
            using Darp.Luau;

            [LuauModule("recoverable")]
            public sealed partial class RecoverableModule
            {
                public int CurrentValue { get; set; }

                [LuauMember("ok")]
                public int Ok() => 1;

                [LuauMember("unsupported")]
                public int Unsupported(int value = 0) => value;

                [LuauMember("duplicate")]
                public int DuplicateOne() => 1;

                [LuauMember("duplicate")]
                public int DuplicateTwo() => 2;

                [LuauMember("Field")]
                public int CreateField() => 1;

                [LuauMember("Field.u8")]
                public int CreateU8() => 8;

                [LuauMember("bad..path")]
                public int BadPath() => 0;

                [LuauMember("current")]
                public int Current => CurrentValue;
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsSourceWithErrors(code);
    }

    [Fact]
    public async Task Module_WithFatalTypeErrors_ShouldNotGenerateOnLoadMethod()
    {
        const string code = """
            using Darp.Luau;

            [LuauModule("nonpartial")]
            public static class NonPartialModule
            {
                [LuauMember("ok")]
                public static int Ok() => 1;
            }

            [LuauModule("ledger")]
            public sealed partial class LedgerModule
            {
                public const string ModuleName = "ledger";

                [LuauMember("ok")]
                public int Ok() => 1;
            }

            [LuauModule(" ")]
            public static partial class BlankModuleName
            {
                [LuauMember("ok")]
                public static int Ok() => 1;
            }

            [LuauModule("generic")]
            public sealed partial class GenericModule<T>
            {
            }

            public static partial class Container
            {
                [LuauModule("nested")]
                public static partial class NestedModule
                {
                }
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsSourceWithErrors(code);
    }

    [Fact]
    public async Task Module_WithBracketAccessNamesAndCollidingPathText_ShouldGenerateSafeLocalNames()
    {
        const string code = """
            using Darp.Luau;

            [LuauModule("demo")]
            public static partial class DemoModule
            {
                [LuauMember("foo-bar")]
                public static int FooBar() => 1;

                [LuauMember("tools.create item")]
                public static int CreateItem() => 2;

                [LuauMember("a.b_c")]
                public static int One() => 3;

                [LuauMember("a_b.c")]
                public static int Two() => 4;
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsSourceWithErrors(code);
    }
}
