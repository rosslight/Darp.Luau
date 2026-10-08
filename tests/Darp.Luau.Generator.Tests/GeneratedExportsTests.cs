namespace Darp.Luau.Generator.Tests;

public class GeneratedExportsTests
{
    [Fact]
    public async Task ValidModuleAndUserdata_ShouldReportNoDiagnostics()
    {
        const string code = """
            using Darp.Luau;

            [LuauUserdata]
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

            [LuauUserdata]
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

            [LuauUserdata]
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

            [LuauUserdata]
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
                [LuauUserdata]
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

            [LuauUserdata]
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

            [LuauUserdata]
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

            [LuauUserdata]
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

            [LuauUserdata]
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

            [LuauUserdata]
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
    public async Task UserdataPropertyAndManualHookConflicts_ShouldFail()
    {
        const string code = """
            using System;
            using System.Collections.Generic;
            using Darp.Luau;

            [LuauUserdata]
            public sealed partial class Player : ILuauUserData<Player>
            {
                [LuauMember("stats")]
                public Dictionary<string, int> Stats { get; } = new();

                [LuauMember("stats.total")]
                public int Total => 1;

                public static LuauReturnSingle OnIndex(Player self, in LuauState state, in ReadOnlySpan<char> fieldName) => LuauReturnSingle.NotHandled;
                public static LuauOutcome OnSetIndex(Player self, LuauArgsSingle args, in ReadOnlySpan<char> fieldName) => LuauOutcome.NotHandledError;
                public static LuauReturn OnMethodCall(Player self, LuauArgs functionArgs, in ReadOnlySpan<char> methodName) => LuauReturn.NotHandledError;
            }
            """;

        await VerifyHelper.VerifyGeneratedExportsWithErrors(code);
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
