using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using Darp.Luau.Tests.Fixtures;
using Shouldly;

namespace Darp.Luau.Tests;

/// <summary> An owned reference and a borrowed view of the same Luau type offer the same operations. </summary>
public sealed class OwnedAndViewTests : IDisposable
{
    private const DynamicallyAccessedMemberTypes PublicApi =
        DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties;

    private readonly LuauState _state = new();

    [Fact]
    public void OwnedReference_ShouldOfferEveryOperationOfItsView()
    {
        MissingOnOwned(typeof(LuauTableView), typeof(LuauTable)).ShouldBeEmpty();
        MissingOnOwned(typeof(LuauFunctionView), typeof(LuauFunction)).ShouldBeEmpty();
        MissingOnOwned(typeof(LuauStringView), typeof(LuauString)).ShouldBeEmpty();
        MissingOnOwned(typeof(LuauBufferView), typeof(LuauBuffer)).ShouldBeEmpty();
        MissingOnOwned(typeof(LuauUserdataView), typeof(LuauUserdata)).ShouldBeEmpty();
    }

    /// <summary> The operations of <paramref name="view"/> that <paramref name="owned"/> does not have with the same signature. </summary>
    private static string[] MissingOnOwned(
        [DynamicallyAccessedMembers(PublicApi)] Type view,
        [DynamicallyAccessedMembers(PublicApi)] Type owned
    )
    {
        // Only a view is promoted to an owned reference.
        string[] onlyOnView = ["ToOwned"];
        HashSet<string> ownedOperations = [.. Operations(owned)];
        return
        [
            .. Operations(view)
                .Where(operation => !ownedOperations.Contains(operation) && !onlyOnView.Any(operation.Contains)),
        ];
    }

    private static IEnumerable<string> Operations([DynamicallyAccessedMembers(PublicApi)] Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(Signature);

    private static string Signature(MethodInfo method)
    {
        // An enumeration returns another type on purpose: that of a view borrows the view, that of an owned
        // reference holds a reference of its own and is a sequence for LINQ.
        string[] enumerations = ["GetEnumerator", "IPairs"];
        string signature = method.ToString() ?? method.Name;
        return enumerations.Contains(method.Name)
            ? signature[(signature.IndexOf(' ', StringComparison.Ordinal) + 1)..]
            : signature;
    }

    [Fact]
    public void String_ShouldGiveItsBytesAndItsText()
    {
        using LuauString owned = _state.CreateString("héllo");
        using LuauFunction function = _state.CreateFunctionManual(static args =>
        {
            LuauStringView view = args[1].Get<LuauStringView>();
            return LuauReturn.Ok($"{view.ToString()} {Encoding.UTF8.GetString(view.AsSpan())}");
        });

        owned.ToString().ShouldBe("héllo");
        Encoding.UTF8.GetString(owned.AsSpan()).ShouldBe("héllo");
        function.Invoke<string>(owned).ShouldBe("héllo héllo");
    }

    [Fact]
    public void Buffer_ShouldGiveItsBytes()
    {
        byte[] bytes = [1, 2, 3];
        using LuauBuffer owned = _state.CreateBuffer(bytes);
        using LuauFunction function = _state.CreateFunctionManual(static args =>
        {
            LuauBufferView view = args[1].Get<LuauBufferView>();
            return LuauReturn.Ok($"{view.ToString()} {view.AsSpan().Length} {view.ToArray().Length}");
        });

        owned.ToArray().ShouldBe(bytes);
        owned.AsSpan().ToArray().ShouldBe(bytes);
        owned.ToString().ShouldBe("010203");
        function.Invoke<string>(owned).ShouldBe("010203 3 3");
    }

    [Fact]
    public void Userdata_ShouldGiveTheManagedObjectItStandsFor()
    {
        var value = new ValueUserdata { Value = 7 };
        using LuauUserdata owned = _state.GetOrCreateUserdata(value);

        owned.Get<ValueUserdata>().ShouldBeSameAs(value);
        owned.Get<object>().ShouldBeSameAs(value);
        owned.TryGet(out ValueUserdata? resolved).ShouldBeTrue();
        resolved.ShouldBeSameAs(value);
        owned.TryGet(out OtherValueUserdata? _).ShouldBeFalse();
        Should
            .Throw<LuaGetException>(() => owned.Get<OtherValueUserdata>())
            .Message.ShouldBe("userdata: OtherValueUserdata expected, got userdata");
    }

    [Fact]
    public void DisposedReference_ShouldThrowWhenItIsRead()
    {
        LuauString text = _state.CreateString("text");
        LuauBuffer buffer = _state.CreateBuffer([1]);
        LuauUserdata userdata = _state.GetOrCreateUserdata(new ValueUserdata());
        text.Dispose();
        buffer.Dispose();
        userdata.Dispose();

        Should.Throw<ObjectDisposedException>(() => text.AsSpan().Length);
        Should.Throw<ObjectDisposedException>(() => buffer.AsSpan().Length);
        Should.Throw<ObjectDisposedException>(() => buffer.ToArray());
        Should.Throw<ObjectDisposedException>(() => userdata.Get<ValueUserdata>());
        Should.Throw<ObjectDisposedException>(() => userdata.TryGet(out ValueUserdata? _));
        // Describing a value must not throw.
        text.ToString().ShouldBe("<disposed>");
        buffer.ToString().ShouldBe("<disposed>");
    }

    [Fact]
    public void Reference_ThatIsDisposedBeforeItIsPushed_ShouldNotBeReadFromItsOldPlace()
    {
        using LuauTable target = _state.CreateTable();
        LuauTable value = _state.CreateTable();

        Should.Throw<ObjectDisposedException>(() =>
        {
            IntoLuau borrowed = value;
            value.Dispose();
            target.Set("key", borrowed);
        });

        target.ContainsKey("key").ShouldBeFalse();
    }

    [Fact]
    public void View_ShouldBePassedOnToLuauAsTheValueItIsAViewOf()
    {
        using LuauFunction echoFunction = _state.CreateFunctionManual(static args =>
        {
            LuauFunctionView view = args[1].Get<LuauFunctionView>();
            return LuauReturn.Ok(view, view.ToString());
        });
        using LuauFunction echoCoroutine = _state.CreateFunctionManual(static args =>
        {
            LuauCoroutineView view = args[1].Get<LuauCoroutineView>();
            return LuauReturn.Ok(view, view.ToString());
        });
        using LuauFunction echoUserdata = _state.CreateFunctionManual(static args =>
        {
            LuauUserdataView view = args[1].Get<LuauUserdataView>();
            return LuauReturn.Ok(view, view.ToString());
        });
        using LuauFunction check = _state
            .Load("local echo, value = ... local same, text = echo(value) return rawequal(same, value), text")
            .ToFunction();
        using LuauFunction function = _state.Load("return function() end").Execute<LuauFunction>();
        using LuauCoroutine coroutine = _state.Load("return coroutine.create(function() end)").Execute<LuauCoroutine>();
        using LuauUserdata userdata = _state.GetOrCreateUserdata(new ValueUserdata());

        check.Invoke<bool, string>(echoFunction, function).ShouldBe((true, function.ToString()));
        check.Invoke<bool, string>(echoCoroutine, coroutine).ShouldBe((true, coroutine.ToString()));
        check.Invoke<bool, string>(echoUserdata, userdata).ShouldBe((true, userdata.ToString()));
        function.ToString().ShouldStartWith("function: 0x");
    }

    [Fact]
    public async Task FunctionArgument_ShouldBeInvokedAsynchronouslyThroughItsView()
    {
        using LuauFunction call = _state.CreateFunctionManual(static args =>
        {
            LuauFunctionView callback = args[1].Get<LuauFunctionView>();
            LuauAwaiter awaiter = args.CreateAwaiter();
            // The view is used inside the callback. Only the pending result crosses the await.
            return awaiter.Await(Result(callback.InvokeAsync<int>([41], args.CancellationToken)));

            static async ValueTask<LuauReturn> Result(ValueTask<int> pending) => LuauReturn.Ok(await pending);
        });
        _state.Globals.Set("call", call);

        int result = await _state
            .Load("return call(function(x) return x + 1 end)")
            .ExecuteAsync<int>([], TestContext.Current.CancellationToken);

        result.ShouldBe(42);
    }

    public void Dispose() => _state.Dispose();
}
