using System.Diagnostics.CodeAnalysis;
using Shouldly;

namespace Darp.Luau.Tests;

public sealed class UserdataRegistryTests
{
    [Fact]
    public void Method_ReadAsAValue_ShouldBeTheSameCallAsColonSyntax()
    {
        using var state = new LuauState();
        state.Globals.Set("v", new Vec(3, 4));

        (double viaLocal, double viaDot, double viaColon, bool sameFunction) = state
            .Load(
                """
                local dot = v.dot
                return dot(v, v), v.dot(v, v), v:dot(v), v.dot == dot
                """
            )
            .Execute<double, double, double, bool>();

        viaLocal.ShouldBe(25);
        viaDot.ShouldBe(25);
        viaColon.ShouldBe(25);
        sameFunction.ShouldBeTrue();
    }

    [Fact]
    public void Method_KeptByAScript_ShouldRunOnTheInstanceItIsCalledOn()
    {
        using var state = new LuauState();
        state.Globals.Set("a", new Vec(1, 0));
        state.Globals.Set("b", new Vec(0, 2));

        state.Load("dot = a.dot; a = nil").Execute();
        state.CollectGarbage();

        state.Load("return dot(b, b)").Execute<double>().ShouldBe(4);
    }

    [Theory]
    [InlineData("return v.dot(1)")]
    [InlineData("return v.dot()")]
    [InlineData("return v.dot(other, v)")]
    public void Method_CalledWithoutAnInstanceOfItsType_ShouldExplainTheCall(string source)
    {
        using var state = new LuauState();
        state.Globals.Set("v", new Vec(1, 2));
        state.Globals.Set("other", new Fallbacks());

        LuaException exception = Should.Throw<LuaException>(() => state.Load(source).Execute());

        exception.Message.ShouldContain("userdata method 'dot' must be called on its instance, as in value:dot(...)");
    }

    [Theory]
    [InlineData("f.readOnly = 1", "userdata member 'readOnly' is read-only")]
    [InlineData("return f.writeOnly", "userdata member 'writeOnly' is write-only")]
    [InlineData("f.method = nil", "userdata method 'method' cannot be assigned")]
    public void DeclaredName_ShouldNotReachIndexOrNewIndex(string source, string expectedError)
    {
        using var state = new LuauState();
        var fallbacks = new Fallbacks();
        state.Globals.Set("f", fallbacks);

        LuaException exception = Should.Throw<LuaException>(() => state.Load(source).Execute());

        exception.Message.ShouldContain(expectedError);
        fallbacks.Reads.ShouldBeEmpty();
        fallbacks.Writes.ShouldBeEmpty();
    }

    [Fact]
    public void UnknownKey_OfAnyType_ShouldReachIndexAndNewIndex()
    {
        using var state = new LuauState();
        var fallbacks = new Fallbacks();
        state.Globals.Set("f", fallbacks);

        (string byName, string byNumber) = state
            .Load(
                """
                f.unknown = 1
                f[2] = 3
                return f.unknown, f[2]
                """
            )
            .Execute<string, string>();

        byName.ShouldBe("read unknown");
        byNumber.ShouldBe("read 2");
        fallbacks.Writes.ShouldBe(["unknown=1", "2=3"]);
        fallbacks.Reads.ShouldBe(["unknown", "2"]);
    }

    [Fact]
    public void Index_ThatReturnsAFunction_ShouldServeMethodNamesThatAreOnlyKnownAtRunTime()
    {
        using var state = new LuauState();
        state.Globals.Set("d", new DynamicMethods());

        (string viaColon, string viaDot) = state
            .Load("return d:greet('Ada'), d.shout(d, 'Bob')")
            .Execute<string, string>();

        viaColon.ShouldBe("greet Ada");
        viaDot.ShouldBe("shout Bob");
    }

    [Fact]
    public void TypeName_ShouldBeWhatTypeofReturns()
    {
        using var state = new LuauState();
        state.Globals.Set("named", new Vec(1, 2));
        state.Globals.Set("unnamed", new Fallbacks());

        (string named, string unnamed) = state.Load("return typeof(named), typeof(unnamed)").Execute<string, string>();

        named.ShouldBe("Vec");
        unnamed.ShouldBe("userdata");
    }

    [Fact]
    public void ArithmeticMetamethods_ShouldGetTheOperandsInTheOrderOfTheScript()
    {
        using var state = new LuauState();
        state.Globals.Set("a", new Vec(1, 2));
        state.Globals.Set("b", new Vec(10, 20));

        (string sum, string scaledRight, string scaledLeft, string negated) = state
            .Load("return tostring(a + b), tostring(a * 2), tostring(3 * a), tostring(-a)")
            .Execute<string, string, string, string>();

        sum.ShouldBe("(11, 22)");
        scaledRight.ShouldBe("(2, 4)");
        scaledLeft.ShouldBe("(3, 6)");
        negated.ShouldBe("(-1, -2)");
    }

    [Fact]
    public void ComparisonMetamethods_ShouldCompareTwoValuesOfTheType()
    {
        using var state = new LuauState();
        state.Globals.Set("small", new Vec(1, 1));
        state.Globals.Set("sameAsSmall", new Vec(1, 1));
        state.Globals.Set("big", new Vec(5, 5));

        (bool equal, bool notEqual, bool less, bool lessOrEqual) = state
            .Load("return small == sameAsSmall, small == big, small < big, big <= small")
            .Execute<bool, bool, bool, bool>();

        equal.ShouldBeTrue();
        notEqual.ShouldBeFalse();
        less.ShouldBeTrue();
        lessOrEqual.ShouldBeFalse();
    }

    [Fact]
    public void Eq_BetweenDifferentTypes_ShouldBeFalseWithoutCallingTheHost()
    {
        using var state = new LuauState();
        state.Globals.Set("v", new Vec(1, 1));
        state.Globals.Set("other", new Fallbacks());
        Vec.EqualityChecks = 0;

        bool equal = state.Load("return v == other").Execute<bool>();

        equal.ShouldBeFalse();
        Vec.EqualityChecks.ShouldBe(0);
    }

    [Fact]
    public void LenConcatAndCallMetamethods_ShouldWork()
    {
        using var state = new LuauState();
        state.Globals.Set("v", new Vec(3, 4));

        (int length, string concatenated, double called) = state
            .Load("return #v, v .. '!', v(10)")
            .Execute<int, string, double>();

        length.ShouldBe(2);
        concatenated.ShouldBe("(3, 4)!");
        called.ShouldBe(70);
    }

    [Fact]
    public void OperatorThatIsNotDeclared_ShouldRaiseTheErrorOfLuauWithTheTypeName()
    {
        using var state = new LuauState();
        state.Globals.Set("v", new Vec(1, 2));

        LuaException exception = Should.Throw<LuaException>(() => state.Load("return v / 2").Execute());

        exception.Message.ShouldContain("attempt to perform arithmetic (div) on Vec and number");
    }

    [Fact]
    public void Register_ThatFails_ShouldFailTheSameWayOnEveryUse()
    {
        using var state = new LuauState();

        ArgumentException first = Should.Throw<ArgumentException>(() => state.GetOrCreateUserdata(new Duplicate()));
        ArgumentException second = Should.Throw<ArgumentException>(() => state.GetOrCreateUserdata(new Duplicate()));

        first.Message.ShouldContain("already declares 'value'");
        second.ShouldBeSameAs(first);
        state.Load("return 1 + 1").Execute<int>().ShouldBe(2);
    }

    [Fact]
    public void Registry_ShouldRejectNamesOfLuauTypesAndUseAfterRegister()
    {
        using var state = new LuauState();
        using LuauUserdata _ = state.GetOrCreateUserdata(new Fallbacks());
        LuauUserdataRegistry<Fallbacks> registry = Fallbacks.Registry.ShouldNotBeNull();

        Fallbacks.BuiltInTypeNameError.ShouldNotBeNull().Message.ShouldContain("built-in Luau type");
        Should.Throw<InvalidOperationException>(() =>
            registry.AddGetter("late", static (_, _) => LuauReturnSingle.Ok(1))
        );
    }

    private sealed class Vec(double x, double y) : ILuauUserdata<Vec>
    {
        public static int EqualityChecks { get; set; }

        public double X { get; } = x;
        public double Y { get; } = y;

        public static void Register(LuauUserdataRegistry<Vec> registry)
        {
            registry.TypeName = "Vec";
            registry.AddGetter("x", static (self, _) => LuauReturnSingle.Ok(self.X));
            registry.AddGetter("y", static (self, _) => LuauReturnSingle.Ok(self.Y));
            registry.AddMethod(
                "dot",
                static (self, args) =>
                    args.TryReadUserdata(1, out Vec? other, out string? error)
                        ? LuauReturn.Ok(self.X * other.X + self.Y * other.Y)
                        : LuauReturn.Error(error)
            );

            registry.AddMetamethod(
                LuauMetamethod.Add,
                static args =>
                    TryReadBoth(args, out Vec? a, out Vec? b, out string? error)
                        ? LuauReturn.Ok(new Vec(a.X + b.X, a.Y + b.Y))
                        : LuauReturn.Error(error)
            );
            registry.AddMetamethod(
                LuauMetamethod.Mul,
                static args =>
                {
                    // The vector can be either operand.
                    if (
                        args.TryReadUserdata(1, out Vec? left, out _) && args.TryReadNumber(2, out double factor, out _)
                    )
                        return LuauReturn.Ok(new Vec(left.X * factor, left.Y * factor));
                    if (args.TryReadNumber(1, out factor, out _) && args.TryReadUserdata(2, out Vec? right, out _))
                        return LuauReturn.Ok(new Vec(right.X * factor, right.Y * factor));
                    return LuauReturn.Error("a vector can only be multiplied with a number");
                }
            );
            registry.AddMetamethod(
                LuauMetamethod.Unm,
                static args =>
                    args.TryReadUserdata(1, out Vec? self, out string? error)
                        ? LuauReturn.Ok(new Vec(-self.X, -self.Y))
                        : LuauReturn.Error(error)
            );
            registry.AddMetamethod(
                LuauMetamethod.Eq,
                static args =>
                {
                    EqualityChecks++;
                    return TryReadBoth(args, out Vec? a, out Vec? b, out string? error)
                        ? LuauReturn.Ok(a.X == b.X && a.Y == b.Y)
                        : LuauReturn.Error(error);
                }
            );
            registry.AddMetamethod(
                LuauMetamethod.Lt,
                static args =>
                    TryReadBoth(args, out Vec? a, out Vec? b, out string? error)
                        ? LuauReturn.Ok(a.X + a.Y < b.X + b.Y)
                        : LuauReturn.Error(error)
            );
            registry.AddMetamethod(
                LuauMetamethod.Le,
                static args =>
                    TryReadBoth(args, out Vec? a, out Vec? b, out string? error)
                        ? LuauReturn.Ok(a.X + a.Y <= b.X + b.Y)
                        : LuauReturn.Error(error)
            );
            registry.AddMetamethod(LuauMetamethod.Len, static _ => LuauReturn.Ok(2));
            registry.AddMetamethod(
                LuauMetamethod.Concat,
                static args =>
                {
                    if (!args.TryReadUserdata(1, out Vec? self, out string? error))
                        return LuauReturn.Error(error);
                    if (!args.TryReadUtf8String(2, out string? suffix, out error))
                        return LuauReturn.Error(error);
                    return LuauReturn.Ok(self.ToString() + suffix);
                }
            );
            registry.AddMetamethod(
                LuauMetamethod.ToString,
                static args =>
                    args.TryReadUserdata(1, out Vec? self, out string? error)
                        ? LuauReturn.Ok(self.ToString())
                        : LuauReturn.Error(error)
            );
            registry.AddMetamethod(
                LuauMetamethod.Call,
                static args =>
                {
                    if (!args.TryReadUserdata(1, out Vec? self, out string? error))
                        return LuauReturn.Error(error);
                    if (!args.TryReadNumber(2, out double factor, out error))
                        return LuauReturn.Error(error);
                    return LuauReturn.Ok((self.X + self.Y) * factor);
                }
            );
        }

        private static bool TryReadBoth(
            LuauArgs args,
            [NotNullWhen(true)] out Vec? a,
            [NotNullWhen(true)] out Vec? b,
            [NotNullWhen(false)] out string? error
        )
        {
            b = null;
            return args.TryReadUserdata(1, out a, out error) && args.TryReadUserdata(2, out b, out error);
        }

        public override string ToString() => FormattableString.Invariant($"({X}, {Y})");

        public static implicit operator IntoLuau(Vec value) => IntoLuau.FromUserdata(value);
    }

    /// <summary> A type without a name whose <c>Index</c> and <c>NewIndex</c> record what reaches them. </summary>
    private sealed class Fallbacks : ILuauUserdata<Fallbacks>
    {
        public static LuauUserdataRegistry<Fallbacks>? Registry { get; private set; }
        public static ArgumentException? BuiltInTypeNameError { get; private set; }

        public List<string> Reads { get; } = [];
        public List<string> Writes { get; } = [];

        public static void Register(LuauUserdataRegistry<Fallbacks> registry)
        {
            Registry = registry;
            try
            {
                registry.TypeName = "table";
            }
            catch (ArgumentException exception)
            {
                BuiltInTypeNameError = exception;
            }

            registry.AddGetter("readOnly", static (_, _) => LuauReturnSingle.Ok(1));
            registry.AddSetter("writeOnly", static (_, _) => LuauOutcome.Ok());
            registry.AddMethod("method", static (_, _) => LuauReturn.Ok());
            registry.AddMetamethod(
                LuauMetamethod.Index,
                static args =>
                {
                    if (!args.TryReadUserdata(1, out Fallbacks? self, out string? error))
                        return LuauReturn.Error(error);
                    string key = ReadKey(args, 2);
                    self.Reads.Add(key);
                    return LuauReturn.Ok($"read {key}");
                }
            );
            registry.AddMetamethod(
                LuauMetamethod.NewIndex,
                static args =>
                {
                    if (!args.TryReadUserdata(1, out Fallbacks? self, out string? error))
                        return LuauReturn.Error(error);
                    self.Writes.Add($"{ReadKey(args, 2)}={ReadKey(args, 3)}");
                    return LuauReturn.Ok();
                }
            );
        }

        private static string ReadKey(LuauArgs args, int index) =>
            args.TryReadUtf8String(index, out string? text, out _) ? text
            : args.TryReadNumber(index, out double number, out _) ? FormattableString.Invariant($"{number}")
            : "?";

        public static implicit operator IntoLuau(Fallbacks value) => IntoLuau.FromUserdata(value);
    }

    private sealed class DynamicMethods : ILuauUserdata<DynamicMethods>
    {
        public static void Register(LuauUserdataRegistry<DynamicMethods> registry) =>
            registry.AddMetamethod(
                LuauMetamethod.Index,
                static args =>
                {
                    if (!args.TryReadUtf8String(2, out string? name, out string? error))
                        return LuauReturn.Error(error);

                    // The function is a method like any other: it gets the instance as its first argument.
                    LuauFunction method = args.State.CreateFunctionManual(call =>
                        call.TryReadUtf8String(2, out string? who, out string? callError)
                            ? LuauReturn.Ok($"{name} {who}")
                            : LuauReturn.Error(callError)
                    );
                    return LuauReturn.Ok(method.DisposeAndToLuauValue());
                }
            );

        public static implicit operator IntoLuau(DynamicMethods value) => IntoLuau.FromUserdata(value);
    }

    private sealed class Duplicate : ILuauUserdata<Duplicate>
    {
        public static void Register(LuauUserdataRegistry<Duplicate> registry)
        {
            registry.AddGetter("value", static (_, _) => LuauReturnSingle.Ok(1));
            registry.AddMethod("value", static (_, _) => LuauReturn.Ok());
        }
    }
}
