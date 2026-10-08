namespace Darp.Luau.Generator.Tests;

public class InterceptorTests
{
    [Fact]
    public async Task NoParameters()
    {
        const string code = """
            using Darp.Luau;

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    state.CreateFunction(() => {});
                }
            }
            """;
        await VerifyHelper.VerifyGenerator(code);
    }

    [Fact]
    public async Task NoParameters_Two()
    {
        const string code = """
            using Darp.Luau;

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    state.CreateFunction(() => {});
                    state.CreateFunction(() => {});
                }
            }
            """;
        await VerifyHelper.VerifyGenerator(code);
    }

    [Fact]
    public async Task StringParameter()
    {
        const string code = """
            using Darp.Luau;

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    state.CreateFunction((string p1) => {});
                    state.CreateFunction((string p1, string p2) => {});
                    state.CreateFunction(OnCall);
                }

                private static void OnCall(string p1, string p2) { }
            }
            """;
        await VerifyHelper.VerifyGenerator(code);
    }

    [Fact]
    public async Task CharSpanParameter()
    {
        const string code = """
            using System;
            using Darp.Luau;

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    state.CreateFunction((ReadOnlySpan<char> p1) => {});
                }
            }
            """;
        await VerifyHelper.VerifyGenerator(code);
    }

    [Fact]
    public async Task NumberParameter()
    {
        const string code = """
            using Darp.Luau;

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    state.CreateFunction((double p1) => {});
                    state.CreateFunction((int p1) => {});
                }
            }
            """;
        await VerifyHelper.VerifyGenerator(code);
    }

    [Fact]
    public async Task NullableNumberParameter()
    {
        const string code = """
            using Darp.Luau;

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    state.CreateFunction((double? p1) => {});
                    state.CreateFunction((int? p1) => {});
                }
            }
            """;
        await VerifyHelper.VerifyGenerator(code);
    }

    [Fact]
    public async Task ParameterRoundtrip()
    {
        const string code = """
            using Darp.Luau;

            public static class Hi
            {
                public delegate string? MyDelegate(string? x);

                public static void DoSomething(LuauState state)
                {
                    state.CreateFunction((MyDelegate)((string? p1) => p1));
                    state.CreateFunction((System.Func<string?, string?>)((string? p1) => p1));
                    state.CreateFunction((string p1) => p1);
                }
            }
            """;
        await VerifyHelper.VerifyGenerator(code);
    }

    [Fact]
    public async Task LuauParameter()
    {
        const string code = """
            using Darp.Luau;

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    state.CreateFunction((LuauValue p1) => {});
                    state.CreateFunction((LuauTableView p1, LuauStringView p2, LuauFunctionView p3) => {});
                }
            }
            """;
        await VerifyHelper.VerifyGenerator(code);
    }

    [Fact]
    public async Task ManagedUserdataParameter()
    {
        const string code = """
            using System;
            using Darp.Luau;

            public sealed class MyUserdata : ILuauUserData<MyUserdata>
            {
                public static LuauReturnSingle OnIndex(MyUserdata self, in LuauState state, in ReadOnlySpan<char> fieldName) => LuauReturnSingle.NotHandled;
                public static LuauOutcome OnSetIndex(MyUserdata self, LuauArgsSingle args, in ReadOnlySpan<char> fieldName) => LuauOutcome.NotHandledError;
                public static LuauReturn OnMethodCall(MyUserdata self, LuauArgs functionArgs, in ReadOnlySpan<char> methodName) => LuauReturn.NotHandledError;
            }

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    state.CreateFunction((MyUserdata p1) => {});
                    state.CreateFunction((MyUserdata? p1) => p1 is null ? 0 : 1);
                }
            }
            """;
        await VerifyHelper.VerifyGenerator(code);
    }

    [Fact]
    public async Task GeneratedUserdataParameterAndReturn()
    {
        const string code = """
            using Darp.Luau;

            [LuauUserdata]
            public sealed partial class HeroCard
            {
                [LuauMember("name")]
                public string Name { get; set; } = "";
            }

            public static class Hi
            {
                public static void DoSomething(LuauState state, HeroCard hero)
                {
                    state.CreateFunction((HeroCard value) => value.Name);
                    state.CreateFunction(() => hero);
                }
            }
            """;

        await VerifyHelper.VerifyCreateFunctionWithGeneratedExportsSucceeds(code);
    }

    [Fact]
    public async Task ReturnParameters()
    {
        const string code = """
            using Darp.Luau;

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    state.CreateFunction(() => 1);
                    state.CreateFunction(() => "myString");
                    state.CreateFunction((string x) => x);
                }
            }
            """;
        await VerifyHelper.VerifyGenerator(code);
    }

    [Fact]
    public async Task ManagedUserdataReturnParameters()
    {
        const string code = """
                using System;
                using Darp.Luau;

                public sealed class MyUserdata : ILuauUserData<MyUserdata>
                {
                    public static LuauReturnSingle OnIndex(MyUserdata self, in LuauState state, in ReadOnlySpan<char> fieldName) => LuauReturnSingle.NotHandled;
                    public static LuauOutcome OnSetIndex(MyUserdata self, LuauArgsSingle args, in ReadOnlySpan<char> fieldName) => LuauOutcome.NotHandledError;
                    public static LuauReturn OnMethodCall(MyUserdata self, LuauArgs functionArgs, in ReadOnlySpan<char> methodName) => LuauReturn.NotHandledError;
                }

                public static class Hi
                {
                    public static void DoSomething(LuauState state, MyUserdata input)
                    {
                        state.CreateFunction(() => input);
                        state.CreateFunction(() => (MyUserdata?)null);
                        state.CreateFunction(() => ((MyUserdata, int))(input, 5));
                    }
                }
            """;
        await VerifyHelper.VerifyGenerator(code);
    }

    [Fact]
    public async Task ManagedUserdataNullableTupleReturnParameter()
    {
        const string code = """
            using System;
            using Darp.Luau;

            public sealed class MyUserdata : ILuauUserData<MyUserdata>
            {
                public static LuauReturnSingle OnIndex(MyUserdata self, in LuauState state, in ReadOnlySpan<char> fieldName) => LuauReturnSingle.NotHandled;
                public static LuauOutcome OnSetIndex(MyUserdata self, LuauArgsSingle args, in ReadOnlySpan<char> fieldName) => LuauOutcome.NotHandledError;
                public static LuauReturn OnMethodCall(MyUserdata self, LuauArgs functionArgs, in ReadOnlySpan<char> methodName) => LuauReturn.NotHandledError;
            }

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    state.CreateFunction(() => ((MyUserdata?)null, 5));
                }
            }
            """;
        await VerifyHelper.VerifyGenerator(code);
    }

    [Fact]
    public async Task ManagedUserdataNullableReturnParameter()
    {
        const string code = """
            using System;
            using Darp.Luau;

            public sealed class MyUserdata : ILuauUserData<MyUserdata>
            {
                public static LuauReturnSingle OnIndex(MyUserdata self, in LuauState state, in ReadOnlySpan<char> fieldName) => LuauReturnSingle.NotHandled;
                public static LuauOutcome OnSetIndex(MyUserdata self, LuauArgsSingle args, in ReadOnlySpan<char> fieldName) => LuauOutcome.NotHandledError;
                public static LuauReturn OnMethodCall(MyUserdata self, LuauArgs functionArgs, in ReadOnlySpan<char> methodName) => LuauReturn.NotHandledError;
            }

            public delegate MyUserdata? NullableCallback();

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    state.CreateFunction((NullableCallback)(() => null));
                }
            }
            """;
        await VerifyHelper.VerifyGenerator(code);
    }

    [Fact]
    public async Task ManagedUserdataNullableReturnParameter_MultipleBranches()
    {
        const string code = """
            using System;
            using Darp.Luau;

            public sealed class MyUserdata : ILuauUserData<MyUserdata>
            {
                public static LuauReturnSingle OnIndex(MyUserdata self, in LuauState state, in ReadOnlySpan<char> fieldName) => LuauReturnSingle.NotHandled;
                public static LuauOutcome OnSetIndex(MyUserdata self, LuauArgsSingle args, in ReadOnlySpan<char> fieldName) => LuauOutcome.NotHandledError;
                public static LuauReturn OnMethodCall(MyUserdata self, LuauArgs functionArgs, in ReadOnlySpan<char> methodName) => LuauReturn.NotHandledError;
            }

            public static class Hi
            {
                public static void DoSomething(LuauState state, bool returnNil, MyUserdata value)
                {
                    state.CreateFunction(() =>
                    {
                        if (returnNil)
                            return (MyUserdata?)null;
                        return value;
                    });
                }
            }
            """;
        await VerifyHelper.VerifyGenerator(code);
    }

    [Fact]
    public async Task ManagedUserdataNullableTupleReturnParameter_MultipleBranches()
    {
        const string code = """
            using System;
            using Darp.Luau;

            public sealed class MyUserdata : ILuauUserData<MyUserdata>
            {
                public static LuauReturnSingle OnIndex(MyUserdata self, in LuauState state, in ReadOnlySpan<char> fieldName) => LuauReturnSingle.NotHandled;
                public static LuauOutcome OnSetIndex(MyUserdata self, LuauArgsSingle args, in ReadOnlySpan<char> fieldName) => LuauOutcome.NotHandledError;
                public static LuauReturn OnMethodCall(MyUserdata self, LuauArgs functionArgs, in ReadOnlySpan<char> methodName) => LuauReturn.NotHandledError;
            }

            public static class Hi
            {
                public static void DoSomething(LuauState state, bool returnNil, MyUserdata value)
                {
                    state.CreateFunction(() =>
                    {
                        if (returnNil)
                            return ((MyUserdata?)null, 5);
                        return (value, 5);
                    });
                }
            }
            """;
        await VerifyHelper.VerifyGenerator(code);
    }

    [Fact]
    public async Task TupleReturnParameters()
    {
        const string code = """
            using Darp.Luau;

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    state.CreateFunction((int x1, int x2) => (x1, x2));
                    state.CreateFunction((decimal x1, decimal x2) => (x1, x2));
                }
            }
            """;
        await VerifyHelper.VerifyGenerator(code);
    }

    [Fact]
    public async Task NestedTupleReturn_ShouldFail()
    {
        const string code = """
            using Darp.Luau;

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    state.CreateFunction(() => ((1, 2), 3));
                }
            }
            """;
        await VerifyHelper.VerifyGeneratorWithErrors(code);
    }

    [Fact]
    public async Task TooManyTupleReturns_ShouldFail()
    {
        const string code = """
            using Darp.Luau;

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    state.CreateFunction(() => (1, 2, 3, 4, 5));
                }
            }
            """;
        await VerifyHelper.VerifyGeneratorWithErrors(code);
    }

    [Fact]
    public async Task EnumParameter()
    {
        const string code = """
            using Darp.Luau;

            public enum MyEnum;

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    state.CreateFunction((MyEnum p1) => p1);
                    state.CreateFunction((MyEnum? p1) => p1);
                }
            }
            """;
        await VerifyHelper.VerifyGenerator(code);
    }

    /*
    [Fact]
    public async Task Varargs()
    {
        const string code = """
            using Darp.Luau;

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    state.CreateFunction(OnCall1);
                    state.CreateFunction(OnCall2);
                    state.CreateFunction(OnCall3);
                    state.CreateFunction(OnCall4);
                }

                private static void OnCall1(params string[] p1) { }
                private static void OnCall2(string[] p1) { }
                private static void OnCall3(params ReadOnlySpan<byte> p1) { }
                private static void OnCall4(ReadOnlySpan<byte> p1) { }
            }
            """;
        await VerifyHelper.VerifyGenerator(code);
    }
    */

    [Fact]
    public async Task UnsupportedType_Lambda()
    {
        const string code = """
            using Darp.Luau;
            using System.Collections.Generic;

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    state.CreateFunction((object p1) => {});
                    state.CreateFunction(() => new object());
                }
            }
            """;
        await VerifyHelper.VerifyGeneratorWithErrors(code);
    }

    [Fact]
    public async Task UnsupportedType_MethodDeclaration()
    {
        const string code = """
            using Darp.Luau;
            using System.Collections.Generic;

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    state.CreateFunction(MyCallback);
                }

                public static object? MyCallback(object? p1) => p1;
            }
            """;
        await VerifyHelper.VerifyGeneratorWithErrors(code);
    }

    [Fact]
    public async Task AwaitableReturnsAndCancellationTokenParameter()
    {
        const string code = """
            using Darp.Luau;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    state.CreateFunction(
                        async (int milliseconds, CancellationToken cancellationToken) =>
                            await Task.Delay(milliseconds, cancellationToken)
                    );
                    state.CreateFunction(async (string name) =>
                    {
                        await Task.Yield();
                        return name.Length == 0 ? null : name;
                    });
                    state.CreateFunction((int a, int b) => new ValueTask<(int Sum, int Difference)>((a + b, a - b)));
                }
            }
            """;
        await VerifyHelper.VerifyGenerator(code);
    }

    [Fact]
    public async Task AsyncVoidCallbacks_ShouldFail()
    {
        const string code = """
            using System;
            using Darp.Luau;
            using System.Threading.Tasks;

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    state.CreateFunction<Action>(async () => await Task.Yield());
                    state.CreateFunction((Action)(async () => await Task.Yield()));
                    state.CreateFunction<Action<int>>(TickAsync);
                }

                private static async void TickAsync(int count) => await Task.Yield();
            }
            """;
        await VerifyHelper.VerifyGeneratorWithErrors(code);
    }

    [Fact]
    public async Task NestedAwaitableReturn_ShouldFail()
    {
        const string code = """
            using Darp.Luau;
            using System.Threading.Tasks;

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    state.CreateFunction(() => Task.FromResult(Task.FromResult(1)));
                }
            }
            """;
        await VerifyHelper.VerifyGeneratorWithErrors(code);
    }

    [Fact]
    public async Task InvalidManagedUserdataType_ShouldFail()
    {
        const string code = """
            using System;
            using Darp.Luau;

            public sealed class PlainUserdata
            {
            }

            public sealed class OtherUserdata : ILuauUserData<OtherUserdata>
            {
                public static LuauReturnSingle OnIndex(OtherUserdata self, in LuauState state, in ReadOnlySpan<char> fieldName) => LuauReturnSingle.NotHandled;
                public static LuauOutcome OnSetIndex(OtherUserdata self, LuauArgsSingle args, in ReadOnlySpan<char> fieldName) => LuauOutcome.NotHandledError;
                public static LuauReturn OnMethodCall(OtherUserdata self, LuauArgs functionArgs, in ReadOnlySpan<char> methodName) => LuauReturn.NotHandledError;
            }

            public sealed class WrongUserdata : ILuauUserData<OtherUserdata>
            {
                public static LuauReturnSingle OnIndex(OtherUserdata self, in LuauState state, in ReadOnlySpan<char> fieldName) => LuauReturnSingle.NotHandled;
                public static LuauOutcome OnSetIndex(OtherUserdata self, LuauArgsSingle args, in ReadOnlySpan<char> fieldName) => LuauOutcome.NotHandledError;
                public static LuauReturn OnMethodCall(OtherUserdata self, LuauArgs functionArgs, in ReadOnlySpan<char> methodName) => LuauReturn.NotHandledError;
            }

            public static class Hi
            {
                public static void DoSomething(LuauState state, PlainUserdata plain, WrongUserdata wrong)
                {
                    state.CreateFunction((PlainUserdata p1) => p1);
                    state.CreateFunction((WrongUserdata p1) => p1);
                    state.CreateFunction(() => plain);
                    state.CreateFunction(() => wrong);
                }
            }
            """;
        await VerifyHelper.VerifyGeneratorWithErrors(code);
    }

    [Fact]
    public async Task InvalidDelegateType_DelegateBase()
    {
        const string code = """
            using Darp.Luau;
            using System;

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    Delegate del = null!;
                    state.CreateFunction(del);
                    state.CreateFunction<Delegate>(() => {});
                }
            }
            """;
        await VerifyHelper.VerifyGeneratorWithErrors(code);
    }

    [Fact]
    public async Task MethodGroupEscapeHatch_ShouldFailClosed()
    {
        const string code = """
            using System;
            using Darp.Luau;

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    Func<Action, LuauFunction> create = state.CreateFunction;
                    create(() => {});
                }
            }
            """;
        await VerifyHelper.VerifyGeneratorAndAnalyzerWithErrors(code, new CreateFunctionUsageAnalyzer());
    }

    [Fact]
    public async Task CustomDelegateEscapeHatch_ShouldFailClosed()
    {
        const string code = """
            using System;
            using Darp.Luau;

            public delegate LuauFunction CreateAction(Action callback);

            public static class Hi
            {
                public static void DoSomething(LuauState state)
                {
                    CreateAction create = state.CreateFunction;
                    create(OnCall);
                }

                private static void OnCall() { }
            }
            """;
        await VerifyHelper.VerifyGeneratorAndAnalyzerWithErrors(code, new CreateFunctionUsageAnalyzer());
    }
}
