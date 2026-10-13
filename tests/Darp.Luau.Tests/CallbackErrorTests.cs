using Shouldly;

namespace Darp.Luau.Tests;

/// <summary> A callback fails by throwing: the message of its exception is the Luau error. </summary>
public sealed class CallbackErrorTests : IDisposable
{
    private readonly LuauState _state = new();

    /// <summary> Runs <paramref name="call"/> and returns the error a script catches from it. </summary>
    private string ErrorOf(string call) =>
        _state
            .Load(
                $"local ok, err = pcall(function() {call} end) assert(not ok, 'the call succeeded') return tostring(err)"
            )
            .Execute<string>();

    [Fact]
    public void ManualCallback_ThatThrows_ShouldRaiseTheMessageOfItsException()
    {
        using LuauFunction function = _state.CreateFunctionManual(static args =>
        {
            int a = args[1].Get<int>();
            int b = args[2].Get<int>();
            if (a <= b)
                throw new ArgumentException("Expected a to be greater than b");
            return LuauReturn.Ok(a - b);
        });
        _state.Globals.Set("f", function);

        _state.Load("return f(3, 1)").Execute<int>().ShouldBe(2);
        ErrorOf("f(1, 3)").ShouldBe("Expected a to be greater than b");
    }

    [Fact]
    public void TypedCallback_ThatThrows_ShouldRaiseTheMessageOfItsException()
    {
        using LuauFunction function = _state.CreateFunction(
            (int a, int b) => a > b ? a - b : throw new ArgumentException("Expected a to be greater than b")
        );
        _state.Globals.Set("f", function);

        _state.Load("return f(3, 1)").Execute<int>().ShouldBe(2);
        ErrorOf("f(1, 3)").ShouldBe("Expected a to be greater than b");
    }

    [Fact]
    public void Callback_ThatThrows_ShouldFailAHostCallThatDoesNotCatchIt()
    {
        using LuauFunction function = _state.CreateFunctionManual(static _ =>
            throw new InvalidOperationException("the device is busy")
        );
        _state.Globals.Set("f", function);

        Should.Throw<LuaException>(() => _state.Load("f()").Execute()).Message.ShouldEndWith("the device is busy");
    }

    [Fact]
    public void Exception_WithoutAMessage_ShouldRaiseTheNameOfItsType()
    {
        using LuauFunction function = _state.CreateFunctionManual(static _ =>
            throw new InvalidOperationException(string.Empty)
        );
        _state.Globals.Set("f", function);

        ErrorOf("f()").ShouldBe(nameof(InvalidOperationException));
    }

    [Fact]
    public void Exception_WithAMessageOfWhitespace_ShouldRaiseThatMessage()
    {
        using LuauFunction function = _state.CreateFunctionManual(static _ =>
            throw new InvalidOperationException(" \t")
        );
        _state.Globals.Set("f", function);

        ErrorOf("f()").ShouldBe(" \t");
    }

    [Fact]
    public void ModuleFunction_ThatThrows_ShouldRaiseTheMessageOfItsException()
    {
        _state.RegisterModule(VaultModule.ModuleName, VaultModule.OnLoad);

        _state.Load("return require('vault').open('sesame')").Execute<string>().ShouldBe("open");
        ErrorOf("require('vault').open('please')").ShouldBe("wrong password");
    }

    [Fact]
    public void Callback_ThatLetsAFailedInvokeThrough_ShouldRaiseTheMessageOfThatLuaException()
    {
        using LuauFunction function = _state.CreateFunctionManual(static args =>
        {
            args[1].Get<LuauFunctionView>().Invoke();
            return LuauReturn.Ok();
        });
        _state.Globals.Set("call", function);

        // The exception of the failed call names the call and its status in front of the error of the script.
        string error = ErrorOf("call(function() error('inner', 0) end)");
        error.ShouldStartWith("Lua invocation");
        error.ShouldEndWith(": inner");
    }

    [Fact]
    public void Callback_ThatThrowsOperationCanceled_ShouldRaiseItsMessageUnderASyncCall()
    {
        using LuauFunction function = _state.CreateFunctionManual(static _ =>
            throw new OperationCanceledException("gave up")
        );
        _state.Globals.Set("f", function);

        ErrorOf("f()").ShouldBe("gave up");
    }

    [Fact]
    public void UserdataMembers_ThatThrow_ShouldRaiseTheMessageOfTheirException()
    {
        _state.Globals.Set("account", new Account());

        ErrorOf("return account.locked").ShouldBe("the account is locked");
        ErrorOf("account.balance = -1").ShouldBe("the balance must not be negative");
        ErrorOf("account:withdraw(5)").ShouldBe("not enough money");
        ErrorOf("return account + 1").ShouldBe("an account cannot be added to");

        _state.Load("account.balance = 3 return account.balance").Execute<int>().ShouldBe(3);
    }

    [Fact]
    public void DefaultResult_ShouldReturnNoValues()
    {
        using LuauFunction function = _state.CreateFunctionManual(static _ => default);
        _state.Globals.Set("f", function);

        _state.Load("return select('#', f())").Execute<int>().ShouldBe(0);
    }

    [Fact]
    public void DefaultSingleResult_ShouldReturnNil()
    {
        _state.Globals.Set("account", new Account());

        _state.Load("return account.note == nil").Execute<bool>().ShouldBeTrue();
    }

    public void Dispose() => _state.Dispose();

    private sealed class Account : ILuauUserdata<Account>
    {
        private int _balance;

        public static void Register(LuauUserdataRegistry<Account> registry)
        {
            registry.AddGetter("balance", static (self, _) => LuauReturnSingle.Ok(self._balance));
            registry.AddSetter(
                "balance",
                static (self, value) =>
                {
                    int balance = value.Get<int>();
                    self._balance =
                        balance >= 0 ? balance : throw new ArgumentException("the balance must not be negative");
                }
            );
            registry.AddGetter("locked", static (_, _) => throw new InvalidOperationException("the account is locked"));
            registry.AddGetter("note", static (_, _) => default);
            registry.AddMethod(
                "withdraw",
                static (self, args) =>
                {
                    int amount = args[1].Get<int>();
                    if (amount > self._balance)
                        throw new InvalidOperationException("not enough money");
                    self._balance -= amount;
                    return LuauReturn.Ok(self._balance);
                }
            );
            registry.AddMetamethod(
                LuauMetamethod.Add,
                static _ => throw new NotSupportedException("an account cannot be added to")
            );
        }

        public static implicit operator IntoLuau(Account value) => IntoLuau.FromUserdata(value);
    }
}

[LuauModule("vault")]
public static partial class VaultModule
{
    [LuauMember("open")]
    public static string Open(string password) =>
        password == "sesame" ? "open" : throw new ArgumentException("wrong password");
}
