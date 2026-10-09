using Shouldly;

namespace Darp.Luau.Tests;

public sealed class GeneratedUserdataTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private static LuauState CreateStateWithPoint()
    {
        var state = new LuauState();
        using LuauFunction point = state.CreateFunction((double x, double y) => new Point(x, y));
        state.Globals.Set("point", point);
        return state;
    }

    [Fact]
    public void Operators_ShouldRunTheMethodThatTakesTheOperands()
    {
        using LuauState state = CreateStateWithPoint();

        (string sum, string scaledRight, string scaledLeft, string negated) = state
            .Load(
                """
                local a, b = point(1, 2), point(10, 20)
                return tostring(a + b), tostring(a * 2), tostring(3 * a), tostring(-a)
                """
            )
            .Execute<string, string, string, string>();

        sum.ShouldBe("(11, 22)");
        scaledRight.ShouldBe("(2, 4)");
        scaledLeft.ShouldBe("(3, 6)");
        negated.ShouldBe("(-1, -2)");
    }

    [Fact]
    public void Operator_WithOperandsNoMethodTakes_ShouldTellTheScriptWhatItCombined()
    {
        using LuauState state = CreateStateWithPoint();

        LuaException exception = Should.Throw<LuaException>(() => state.Load("return point(1, 2) * 'two'").Execute());

        exception.Message.ShouldContain("attempt to perform arithmetic (mul) on Point and string");
    }

    [Fact]
    public void EqLenAndIndex_ShouldWork()
    {
        using LuauState state = CreateStateWithPoint();

        (bool equal, int length, double second, bool unknownIsNil) = state
            .Load(
                """
                local a = point(1, 2)
                return a == point(1, 2), #a, a[2], a[3] == nil and a.missing == nil
                """
            )
            .Execute<bool, int, double, bool>();

        equal.ShouldBeTrue();
        length.ShouldBe(2);
        second.ShouldBe(2);
        unknownIsNil.ShouldBeTrue();
    }

    [Fact]
    public void Method_ShouldBeCallableAsAValue()
    {
        using LuauState state = CreateStateWithPoint();

        (double viaColon, double viaDot) = state
            .Load(
                """
                local a, b = point(1, 2), point(3, 4)
                return a:dot(b), a.dot(a, b)
                """
            )
            .Execute<double, double>();

        viaColon.ShouldBe(11);
        viaDot.ShouldBe(11);
    }

    [Fact]
    public async Task AwaitingMethod_CalledAsAValue_ShouldSuspendTheScript()
    {
        using LuauState state = CreateStateWithPoint();

        string scaled = await state
            .Load(
                """
                local a = point(1, 2)
                local later = a.scaledLater
                return tostring(later(a, 10))
                """
            )
            .ExecuteAsync<string>([], TestToken);

        scaled.ShouldBe("(10, 20)");
    }

    [Fact]
    public async Task Call_ShouldChooseItsOverloadByTheArgumentsAndAwait()
    {
        using LuauState state = CreateStateWithPoint();

        (double sum, double scaledSum) = await state
            .Load(
                """
                local a = point(1, 2)
                return a(), a(10)
                """
            )
            .ExecuteAsync<double, double>([], TestToken);

        sum.ShouldBe(3);
        scaledSum.ShouldBe(30);
    }

    [Fact]
    public void Overloads_ShouldBeChosenByTheLuauTypeOfTheArgument()
    {
        using LuauState state = CreateStateWithPoint();
        using LuauUserdata probe = state.GetOrCreateUserdata(new Probe());
        using LuauBuffer bytes = state.CreateBuffer([1, 2, 3]);
        state.Globals.Set("probe", probe);
        state.Globals.Set("bytes", bytes);

        string chosen = state
            .Load(
                """
                return table.concat({
                  probe(true), probe(1), probe('a'), probe({}), probe(function() end), probe(bytes),
                  probe(point(1, 2)), probe(probe),
                }, ' ')
                """
            )
            .Execute<string>();

        chosen.ShouldBe("boolean number string table function buffer point probe");
    }

    [Theory]
    [InlineData("probe(nil)")]
    [InlineData("probe(1, 2)")]
    [InlineData("probe()")]
    public void Overloads_WhenNoneTakesTheArguments_ShouldRaiseAnError(string call)
    {
        using var state = new LuauState();
        using LuauUserdata probe = state.GetOrCreateUserdata(new Probe());
        state.Globals.Set("probe", probe);

        LuaException exception = Should.Throw<LuaException>(() => state.Load(call).Execute());

        exception.Message.ShouldContain("no overload of the metamethod accepts these arguments");
    }
}

/// <summary> Says which of its overloads a call reached. </summary>
[LuauUserdata("Probe")]
internal sealed partial class Probe
{
    private int _calls;

    private string Reached(string overload)
    {
        _calls++;
        return overload;
    }

    [LuauMetamethod(LuauMetamethod.Len)]
    private int Calls() => _calls;

    [LuauMetamethod(LuauMetamethod.Call)]
    private string Take(bool value) => Reached("boolean");

    [LuauMetamethod(LuauMetamethod.Call)]
    private string Take(double value) => Reached("number");

    [LuauMetamethod(LuauMetamethod.Call)]
    private string Take(string value) => Reached("string");

    [LuauMetamethod(LuauMetamethod.Call)]
    private string Take(LuauTableView value) => Reached("table");

    [LuauMetamethod(LuauMetamethod.Call)]
    private string Take(LuauFunctionView value) => Reached("function");

    [LuauMetamethod(LuauMetamethod.Call)]
    private string Take(LuauBufferView value) => Reached("buffer");

    [LuauMetamethod(LuauMetamethod.Call)]
    private string Take(Point value) => Reached("point");

    [LuauMetamethod(LuauMetamethod.Call)]
    private string Take(Probe value) => Reached("probe");
}

[LuauUserdata("Point")]
internal sealed partial class Point(double x, double y)
{
    [LuauMember("x")]
    public double X { get; } = x;

    [LuauMember("y")]
    public double Y { get; } = y;

    [LuauMember("dot")]
    public double Dot(Point other) => X * other.X + Y * other.Y;

    [LuauMember("scaledLater")]
    public async Task<Point> ScaledLaterAsync(double factor)
    {
        await Task.Yield();
        return new Point(X * factor, Y * factor);
    }

    [LuauMetamethod(LuauMetamethod.Add)]
    public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y);

    [LuauMetamethod(LuauMetamethod.Unm)]
    public static Point operator -(Point a) => new(-a.X, -a.Y);

    [LuauMetamethod(LuauMetamethod.Mul)]
    private Point Scale(double factor) => new(X * factor, Y * factor);

    [LuauMetamethod(LuauMetamethod.Mul)]
    private static Point Scale(double factor, Point point) => point.Scale(factor);

    [LuauMetamethod(LuauMetamethod.Eq)]
    private bool SameAs(Point other) => X == other.X && Y == other.Y;

    [LuauMetamethod(LuauMetamethod.Len)]
    private static int Count(Point point) => point is null ? 0 : 2;

    [LuauMetamethod(LuauMetamethod.ToString)]
    private string Describe() => FormattableString.Invariant($"({X}, {Y})");

    [LuauMetamethod(LuauMetamethod.Index)]
    private double? Component(int index) =>
        index switch
        {
            1 => X,
            2 => Y,
            _ => null,
        };

    [LuauMetamethod(LuauMetamethod.Call)]
    private double Sum() => X + Y;

    [LuauMetamethod(LuauMetamethod.Call)]
    private async Task<double> ScaledSumLaterAsync(double factor)
    {
        await Task.Yield();
        return (X + Y) * factor;
    }
}
