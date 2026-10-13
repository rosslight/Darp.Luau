using Darp.Luau.Tests.Fixtures;
using Shouldly;

namespace Darp.Luau.Tests;

/// <summary> Which managed type reads which Luau value. Read as typed results, which share the reader of every site. </summary>
public sealed class TypedReadTests : IDisposable
{
    private readonly LuauState _state = new();

    private ulong ActiveReferences => _state.MemoryStatistics.ActiveRegistryReferences;

    private T? Read<T>(string expression) => _state.Load($"return {expression}").Execute<T>();

    [Fact]
    public void Numbers_ShouldReadIntoEveryNumericType()
    {
        Read<double>("42").ShouldBe(42);
        Read<float>("42").ShouldBe(42);
        Read<Half>("42").ShouldBe((Half)42);
        Read<decimal>("42").ShouldBe(42);
        Read<sbyte>("42").ShouldBe((sbyte)42);
        Read<byte>("42").ShouldBe((byte)42);
        Read<short>("42").ShouldBe((short)42);
        Read<ushort>("42").ShouldBe((ushort)42);
        Read<int>("42").ShouldBe(42);
        Read<uint>("42").ShouldBe(42u);
        Read<long>("42").ShouldBe(42);
        Read<ulong>("42").ShouldBe(42ul);
        Read<nint>("42").ShouldBe(42);
        Read<nuint>("42").ShouldBe((nuint)42);
        Read<Int128>("42").ShouldBe(42);
        Read<UInt128>("42").ShouldBe((UInt128)42);
    }

    [Fact]
    public void NullableNumbers_ShouldReadANumber()
    {
        Read<double?>("1.5").ShouldBe(1.5);
        Read<float?>("1.5").ShouldBe(1.5f);
        Read<Half?>("1.5").ShouldBe((Half)1.5);
        Read<decimal?>("1.5").ShouldBe(1.5m);
        Read<sbyte?>("-42").ShouldBe((sbyte)-42);
        Read<byte?>("255").ShouldBe((byte)255);
        Read<short?>("-42").ShouldBe((short)-42);
        Read<ushort?>("42").ShouldBe((ushort)42);
        Read<int?>("-42").ShouldBe(-42);
        Read<uint?>("42").ShouldBe(42u);
        Read<long?>("-42").ShouldBe(-42L);
        Read<ulong?>("42").ShouldBe(42ul);
        Read<nint?>("-42").ShouldBe((nint)(-42));
        Read<nuint?>("42").ShouldBe((nuint)42);
        Read<Int128?>("-42").ShouldBe((Int128)(-42));
        Read<UInt128?>("42").ShouldBe((UInt128)42);
    }

    [Fact]
    public void NullableNumbers_ShouldReadNilAsNull()
    {
        Read<double?>("nil").ShouldBeNull();
        Read<float?>("nil").ShouldBeNull();
        Read<Half?>("nil").ShouldBeNull();
        Read<decimal?>("nil").ShouldBeNull();
        Read<sbyte?>("nil").ShouldBeNull();
        Read<byte?>("nil").ShouldBeNull();
        Read<short?>("nil").ShouldBeNull();
        Read<ushort?>("nil").ShouldBeNull();
        Read<int?>("nil").ShouldBeNull();
        Read<uint?>("nil").ShouldBeNull();
        Read<long?>("nil").ShouldBeNull();
        Read<ulong?>("nil").ShouldBeNull();
        Read<nint?>("nil").ShouldBeNull();
        Read<nuint?>("nil").ShouldBeNull();
        Read<Int128?>("nil").ShouldBeNull();
        Read<UInt128?>("nil").ShouldBeNull();
    }

    [Fact]
    public void Enums_ShouldReadTheNumberOfTheirUnderlyingType()
    {
        Read<IntEnum>("2").ShouldBe(IntEnum.Two);
        Read<ByteEnum>("200").ShouldBe(ByteEnum.TwoHundred);
        Read<LongEnum>("-3").ShouldBe(LongEnum.MinusThree);
        Read<DayOfWeek>("3").ShouldBe(DayOfWeek.Wednesday);
        Read<SByteEnum>("-1").ShouldBe(SByteEnum.MinusOne);
        Read<ShortEnum>("-300").ShouldBe(ShortEnum.MinusThreeHundred);
        Read<UShortEnum>("60000").ShouldBe(UShortEnum.SixtyThousand);
        Read<UIntEnum>("4000000000").ShouldBe(UIntEnum.FourBillion);
        Read<ULongEnum>("9007199254740992").ShouldBe(ULongEnum.TwoToThe53);
        // An enum value does not have to be a named one.
        Read<IntEnum>("7").ShouldBe((IntEnum)7);
    }

    [Fact]
    public void NullableEnums_ShouldReadANumberOrNil()
    {
        Read<IntEnum?>("2").ShouldBe(IntEnum.Two);
        Read<ByteEnum?>("200").ShouldBe(ByteEnum.TwoHundred);
        Read<LongEnum?>("-3").ShouldBe(LongEnum.MinusThree);
        Read<SByteEnum?>("-1").ShouldBe(SByteEnum.MinusOne);
        Read<ShortEnum?>("-300").ShouldBe(ShortEnum.MinusThreeHundred);
        Read<UShortEnum?>("60000").ShouldBe(UShortEnum.SixtyThousand);
        Read<UIntEnum?>("4000000000").ShouldBe(UIntEnum.FourBillion);
        Read<ULongEnum?>("9007199254740992").ShouldBe(ULongEnum.TwoToThe53);

        Read<IntEnum?>("nil").ShouldBeNull();
        Read<ByteEnum?>("nil").ShouldBeNull();
        Read<LongEnum?>("nil").ShouldBeNull();
        Read<SByteEnum?>("nil").ShouldBeNull();
        Read<ShortEnum?>("nil").ShouldBeNull();
        Read<UShortEnum?>("nil").ShouldBeNull();
        Read<UIntEnum?>("nil").ShouldBeNull();
        Read<ULongEnum?>("nil").ShouldBeNull();
    }

    [Fact]
    public void Enum_ShouldRejectANumberItsUnderlyingTypeCannotHold()
    {
        Should.Throw<LuaGetException>(() => Read<ByteEnum>("256"));
        Should.Throw<LuaGetException>(() => Read<IntEnum>("1.5"));
        Should.Throw<LuaGetException>(() => Read<IntEnum>("'two'"));
    }

    [Fact]
    public void Booleans_ShouldRead()
    {
        Read<bool>("true").ShouldBeTrue();
        Read<bool>("false").ShouldBeFalse();
        Read<bool?>("true").ShouldBe(true);
        Read<bool?>("nil").ShouldBeNull();
    }

    [Fact]
    public void Strings_ShouldReadAsManagedText()
    {
        Read<string>("'héllo'").ShouldBe("héllo");
        Read<string>("''").ShouldBe(string.Empty);
    }

    [Fact]
    public void Buffers_ShouldReadAsAManagedArray()
    {
        Read<byte[]>("buffer.fromstring('abc')").ShouldBe("abc"u8.ToArray());
        Read<byte[]>("buffer.create(0)").ShouldBeEmpty();
    }

    [Fact]
    public void OwnedReferences_ShouldReadTheMatchingLuauType()
    {
        using (LuauTable table = Read<LuauTable>("{ value = 1 }"))
            table.GetNumber("value").ShouldBe(1);
        using (LuauFunction function = Read<LuauFunction>("function() return 7 end"))
            function.Invoke<int>().ShouldBe(7);
        using (LuauString text = Read<LuauString>("'text'"))
            text.ToString().ShouldBe("text");
        using (LuauBuffer buffer = Read<LuauBuffer>("buffer.fromstring('abc')"))
            buffer.ToString().ShouldBe("616263");
        using (LuauCoroutine coroutine = Read<LuauCoroutine>("coroutine.create(function() end)"))
            coroutine.Status.ShouldBe(LuauCoroutineStatus.Suspended);
    }

    [Fact]
    public void OwnedReference_ShouldRejectAnotherLuauType()
    {
        Should.Throw<LuaGetException>(() => Read<LuauTable>("1"));
        Should.Throw<LuaGetException>(() => Read<LuauFunction>("{}"));
        Should.Throw<LuaGetException>(() => Read<LuauTable>("nil"));
    }

    [Fact]
    public void NullableOwnedReference_ShouldReadNilAsNull()
    {
        Read<LuauTable?>("nil").ShouldBeNull();
        Read<LuauFunction?>("nil").ShouldBeNull();
        Read<LuauString?>("nil").ShouldBeNull();
        Read<LuauBuffer?>("nil").ShouldBeNull();
        Read<LuauUserdata?>("nil").ShouldBeNull();
        Read<LuauCoroutine?>("nil").ShouldBeNull();
    }

    [Fact]
    public void NullableOwnedReference_ShouldReadTheReference()
    {
        ulong baseline = ActiveReferences;
        using LuauFunction identity = _state.Load("return ...").ToFunction();

        using (LuauTable table = Read<LuauTable?>("{ value = 1 }").ShouldNotBeNull())
            table.GetNumber("value").ShouldBe(1);
        using (LuauFunction function = Read<LuauFunction?>("function() return 7 end").ShouldNotBeNull())
            function.Invoke<int>().ShouldBe(7);
        using (LuauString text = Read<LuauString?>("'text'").ShouldNotBeNull())
            text.ToString().ShouldBe("text");
        using (LuauBuffer buffer = Read<LuauBuffer?>("buffer.fromstring('abc')").ShouldNotBeNull())
            buffer.ToString().ShouldBe("616263");
        using (LuauCoroutine coroutine = Read<LuauCoroutine?>("coroutine.create(function() end)").ShouldNotBeNull())
            coroutine.Status.ShouldBe(LuauCoroutineStatus.Suspended);
        using (LuauUserdata userdata = identity.Invoke<LuauUserdata?>(new ValueUserdata()).ShouldNotBeNull())
            userdata.IsDisposed.ShouldBeFalse();

        // The function is the one reference that is still held.
        ActiveReferences.ShouldBe(baseline + 1);
    }

    [Fact]
    public void NullableOwnedReference_ShouldBeReleasedWhenALaterResultCannotBeRead()
    {
        ulong baseline = ActiveReferences;

        Should.Throw<LuaGetException>(() => _state.Load("return {}, 'text'").Execute<LuauTable?, int>());
        Should.Throw<LuaGetException>(() =>
            _state.Load("return {}, function() end, 'text'").Execute<LuauValue, LuauFunction?, int>()
        );

        ActiveReferences.ShouldBe(baseline);
    }

    [Fact]
    public void ReferenceResult_ShouldTakeOneRegistryReference()
    {
        ulong active = ActiveReferences;
        ulong created = _state.MemoryStatistics.CreatedRegistryReferences;

        LuauTable table = Read<LuauTable>("{}");
        ActiveReferences.ShouldBe(active + 1);
        _state.MemoryStatistics.CreatedRegistryReferences.ShouldBe(created + 1);

        table.Dispose();
        ActiveReferences.ShouldBe(active);
    }

    [Fact]
    public void ManagedUserdata_ShouldReadAsTheObject()
    {
        var userdata = new ValueUserdata { Value = 5 };
        using LuauFunction identity = _state.Load("return ...").ToFunction();

        identity.Invoke<ValueUserdata>(userdata).ShouldBeSameAs(userdata);
        identity.Invoke<object>(userdata).ShouldBeSameAs(userdata);
        using LuauUserdata reference = identity.Invoke<LuauUserdata>(userdata);
        reference.TryGetManaged(out ValueUserdata? managed, out _).ShouldBeTrue();
        managed.ShouldBeSameAs(userdata);
    }

    [Fact]
    public void ManagedUserdata_ShouldRejectAnotherTypeAndOtherValues()
    {
        using LuauFunction identity = _state.Load("return ...").ToFunction();

        Should.Throw<LuaGetException>(() => identity.Invoke<ValueUserdata>(new OtherValueUserdata()));
        Should.Throw<LuaGetException>(() => identity.Invoke<ValueUserdata>(1));
    }

    [Fact]
    public void ManagedUserdata_ShouldNotBeDisposedWhenALaterResultCannotBeRead()
    {
        var userdata = new DisposableUserdata();
        using LuauFunction identity = _state.Load("return ..., 'text'").ToFunction();

        Should.Throw<LuaGetException>(() => identity.Invoke<DisposableUserdata, int>(IntoLuau.FromUserdata(userdata)));

        userdata.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public void LuauValue_ShouldReadEveryValueIncludingNil()
    {
        using LuauValue number = Read<LuauValue>("1");
        using LuauValue table = Read<LuauValue>("{}");
        using LuauValue nil = Read<LuauValue>("nil");

        number.Type.ShouldBe(LuauValueType.Number);
        table.Type.ShouldBe(LuauValueType.Table);
        nil.Type.ShouldBe(LuauValueType.Nil);
    }

    [Fact]
    public void Vector_ShouldNotBeSupported()
    {
        ulong baseline = ActiveReferences;

        Should.Throw<NotSupportedException>(() => Read<LuauValue>("vector.create(1, 2, 3)"));
        Should.Throw<NotSupportedException>(() =>
            _state.Load("return {}, vector.create(1, 2, 3)").Execute<LuauTable, LuauValue>()
        );

        ActiveReferences.ShouldBe(baseline);
    }

    [Fact]
    public void UnsupportedType_ShouldBeAProgrammerError()
    {
        Should.Throw<NotSupportedException>(() => Read<DateTime>("1"));
        Should.Throw<NotSupportedException>(() => Read<DateTime?>("nil"));
    }

    [Fact]
    public void RequiredValueType_ShouldRejectNil()
    {
        LuaGetException exception = Should.Throw<LuaGetException>(() => Read<int>("nil"));

        exception.Message.ShouldBe("result #1: number expected, got nil");
    }

    [Fact]
    public void WrongType_ShouldNameWhatWasExpectedAndFound()
    {
        Should
            .Throw<LuaGetException>(() => Read<int>("'text'"))
            .Message.ShouldBe("result #1: number expected, got string");
        Should
            .Throw<LuaGetException>(() => Read<string>("1"))
            .Message.ShouldBe("result #1: string expected, got number");
        Should
            .Throw<LuaGetException>(() => Read<int>("1.5"))
            .Message.ShouldBe("result #1: number that fits Int32 expected, got 1.5");
        Should
            .Throw<LuaGetException>(() => _state.Load("return 1, {}").Execute<int, bool>())
            .Message.ShouldBe("result #2: boolean expected, got table");
    }

    [Fact]
    public void ReferenceType_ShouldReadNilAsNull()
    {
        Read<string>("nil").ShouldBeNull();
        Read<byte[]>("nil").ShouldBeNull();
        Read<ValueUserdata>("nil").ShouldBeNull();
    }

    [Fact]
    public void MissingResult_ShouldReadAsNil()
    {
        using LuauFunction nothing = _state.Load("return").ToFunction();

        nothing.Invoke<int?>().ShouldBeNull();
        nothing.Invoke<string>().ShouldBeNull();
        _state.Load("return").Execute<int?>().ShouldBeNull();
        Should
            .Throw<LuaGetException>(() => nothing.Invoke<int>())
            .Message.ShouldBe("result #1: number expected, got nil");
    }

    public void Dispose() => _state.Dispose();

    private enum IntEnum
    {
        Two = 2,
    }

    private enum ByteEnum : byte
    {
        TwoHundred = 200,
    }

    private enum LongEnum : long
    {
        MinusThree = -3,
    }

    private enum SByteEnum : sbyte
    {
        MinusOne = -1,
    }

    private enum ShortEnum : short
    {
        MinusThreeHundred = -300,
    }

    private enum UShortEnum : ushort
    {
        SixtyThousand = 60000,
    }

    private enum UIntEnum : uint
    {
        FourBillion = 4000000000,
    }

    private enum ULongEnum : ulong
    {
        TwoToThe53 = 9007199254740992,
    }

    private sealed class DisposableUserdata : ILuauUserdata<DisposableUserdata>, IDisposable
    {
        public bool IsDisposed { get; private set; }

        public static void Register(LuauUserdataRegistry<DisposableUserdata> registry) { }

        public void Dispose() => IsDisposed = true;
    }
}
