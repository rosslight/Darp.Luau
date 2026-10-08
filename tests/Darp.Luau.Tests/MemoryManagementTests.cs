using Darp.Luau.Tests.Fixtures;
using Darp.Luau.Utils;
using Shouldly;

namespace Darp.Luau.Tests;

public sealed class MemoryManagementTests
{
    [Fact]
    public void LuauValue_Dispose_ShouldReleaseTrackedReference()
    {
        using var state = new LuauState();
        using (LuauTable table = state.CreateTable())
        {
            table.Set("v", 1);
            state.Globals.Set("payload", table);
        }

        ulong baselineActiveReferences = state.MemoryStatistics.ActiveRegistryReferences;

        state.Globals.TryGetLuauValue("payload", out LuauValue value).ShouldBeTrue();
        state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(baselineActiveReferences + 1);

        value.Dispose();
        state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(baselineActiveReferences);
    }

    [Fact]
    public void LuauValue_TryGetBuffer_ShouldCloneReferenceOwnership()
    {
        using var state = new LuauState();
        byte[] expected = [0x01, 0x02, 0x03, 0x04];
        state.Globals.Set("payload", expected);

        state.Globals.TryGetLuauValue("payload", out LuauValue value).ShouldBeTrue();
        value.TryGet(out LuauBuffer buffer).ShouldBeTrue();

        value.Dispose();

        using (buffer)
        {
            buffer.TryGet(out byte[]? bytes).ShouldBeTrue();
            bytes.ShouldNotBeNull();
            bytes.ShouldBe(expected);
        }
    }

    [Fact]
    public void Globals_Dispose_ShouldNotInvalidateStateGlobalTable()
    {
        using var state = new LuauState();

        LuauTable globals = state.Globals;
        globals.Dispose();

        state.Globals.Set("value", 123);
        state.Globals.TryGet("value", out int value).ShouldBeTrue();
        value.ShouldBe(123);
    }

    [Fact]
    public void MemoryStatistics_RepeatedValueRoundtrip_ShouldReturnToBaseline()
    {
        using var state = new LuauState();
        using (LuauTable table = state.CreateTable())
        {
            table.Set("v", 1);
            state.Globals.Set("payload", table);
        }

        ulong baselineActiveReferences = state.MemoryStatistics.ActiveRegistryReferences;

        for (int i = 0; i < 2000; i++)
        {
            state.Globals.TryGetLuauValue("payload", out LuauValue value).ShouldBeTrue();
            value.TryGet(out LuauTable roundtrip).ShouldBeTrue();

            roundtrip.Dispose();
            value.Dispose();
        }

        LuauMemoryStatistics stats = state.MemoryStatistics;
        stats.ActiveRegistryReferences.ShouldBe(baselineActiveReferences);
    }

    [Fact]
    public void ManagedCallback_WhenLuauCollectsTheFunction_ShouldReleaseItsHandle()
    {
        using var state = new LuauState();
        int baselineCallbacks = state.MemoryStatistics.ActiveManagedCallbacks;

        using (LuauFunction kept = state.CreateFunctionBuilder(_ => LuauReturn.Ok(42)))
        {
            state.Globals.Set("kept", kept);
        }
        for (int i = 0; i < 1000; i++)
        {
            using LuauFunction dropped = state.CreateFunctionBuilder(_ => LuauReturn.Ok());
        }

        state.CollectGarbage();

        state.MemoryStatistics.ActiveManagedCallbacks.ShouldBe(baselineCallbacks + 1);
        state.Load("return kept()").Execute<int>().ShouldBe(42);
    }

    [Fact]
    public void ManagedCallback_WhenTheStateIsDisposed_ShouldReleaseItsHandle()
    {
        var state = new LuauState();
        using (LuauFunction kept = state.CreateFunctionBuilder(_ => LuauReturn.Ok()))
        {
            state.Globals.Set("kept", kept);
        }
        state.MemoryStatistics.ActiveManagedCallbacks.ShouldBe(1);

        state.Dispose();

        state.MemoryStatistics.ActiveManagedCallbacks.ShouldBe(0);
    }

    [Fact]
    public void LuauTable_ExplicitCastToLuauValue_ShouldTransferReferenceOwnership()
    {
        using var state = new LuauState();
        using LuauTable table = state.CreateTable();
        table.Set("value", 123);

        ulong baselineActiveReferences = state.MemoryStatistics.ActiveRegistryReferences;

        LuauValue value = table.DisposeAndToLuauValue();
        state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(baselineActiveReferences);

        value.TryGet(out LuauTable transferredTable).ShouldBeTrue();
        using (transferredTable)
        {
            transferredTable.GetNumber("value").ShouldBe(123);
        }

        value.Dispose();
        state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(baselineActiveReferences - 1);
    }

    [Fact]
    public void LuauValue_TryGetLuauTable_ShouldCloneReferenceOwnership()
    {
        using var state = new LuauState();
        using (LuauTable table = state.CreateTable())
        {
            table.Set("v", 1);
            state.Globals.Set("payload", table);
        }

        state.Globals.TryGetLuauValue("payload", out LuauValue value).ShouldBeTrue();
        ulong referencesAfterValue = state.MemoryStatistics.ActiveRegistryReferences;

        value.TryGet(out LuauTable tableAlias).ShouldBeTrue();
        state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(referencesAfterValue + 1);

        tableAlias.Dispose();
        state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(referencesAfterValue);

        value.TryGet(out LuauTable secondAlias).ShouldBeTrue();
        state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(referencesAfterValue + 1);
        secondAlias.Dispose();
        state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(referencesAfterValue);

        value.Dispose();
        state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(referencesAfterValue - 1);
    }

    [Fact]
    public void LuauTable_ToIntoLuau_ShouldBorrowWithoutExtraReferenceTracking()
    {
        using var state = new LuauState();
        using LuauTable table = state.CreateTable();

        ulong baselineActiveReferences = state.MemoryStatistics.ActiveRegistryReferences;
        state.Globals.Set("payload", table);

        state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(baselineActiveReferences);
    }

    [Fact]
    public void DisposedTable_ToIntoLuau_ShouldThrowObjectDisposedException()
    {
        using var state = new LuauState();
        LuauTable table = state.CreateTable();
        table.Dispose();

        Should.Throw<ObjectDisposedException>(() => state.Globals.Set("payload", table));
    }

    [Fact]
    public void DisposedFunction_ToIntoLuau_ShouldThrowObjectDisposedException()
    {
        using var state = new LuauState();
        LuauFunction function = state.CreateFunctionBuilder(_ => LuauReturn.Ok());
        function.Dispose();

        Should.Throw<ObjectDisposedException>(() => state.Globals.Set("payload", function));
    }

    [Fact]
    public void DisposedUserdata_ToIntoLuau_ShouldThrowObjectDisposedException()
    {
        using var state = new LuauState();
        LuauUserdata userdata = state.GetOrCreateUserdata(new ValueUserdata { Value = 42 });
        userdata.Dispose();

        Should.Throw<ObjectDisposedException>(() => state.Globals.Set("payload", userdata));
    }

    [Fact]
    public void CrossState_ReferenceUsage_ShouldThrowInvalidOperationException()
    {
        using var stateA = new LuauState();
        using var stateB = new LuauState();
        using LuauTable tableA = stateA.CreateTable();

        Should.Throw<InvalidOperationException>(() => stateB.Globals.Set("payload", tableA));

        LuauValue valueA = tableA.DisposeAndToLuauValue();
        try
        {
            Should.Throw<InvalidOperationException>(() => stateB.Globals.Set("payload2", valueA));
        }
        finally
        {
            valueA.Dispose();
        }
    }

    [Fact]
    public void CrossState_FunctionInvocationArgument_ShouldThrowInvalidOperationException()
    {
        using var stateA = new LuauState();
        using var stateB = new LuauState();
        using LuauTable tableA = stateA.CreateTable();
        using LuauFunction functionB = stateB.CreateFunctionBuilder(_ => LuauReturn.Ok());

        Should.Throw<InvalidOperationException>(() => functionB.Invoke(tableA));
    }

    [Fact]
    public void LuauValue_TryGetString_Twice_ShouldSucceed()
    {
        using var state = new LuauState();
        state.Globals.Set("payload", "hello");

        state.Globals.TryGetLuauValue("payload", out LuauValue value).ShouldBeTrue();
        try
        {
            value.TryGet(out string? first).ShouldBeTrue();
            first.ShouldBe("hello");

            value.TryGet(out string? second).ShouldBeTrue();
            second.ShouldBe("hello");
        }
        finally
        {
            value.Dispose();
        }
    }

    [Fact]
    public void LuauValue_TryGetString_ShouldNotReleaseReferenceUntilDispose()
    {
        using var state = new LuauState();
        state.Globals.Set("payload", "hello");

        ulong baselineActiveReferences = state.MemoryStatistics.ActiveRegistryReferences;
        state.Globals.TryGetLuauValue("payload", out LuauValue value).ShouldBeTrue();
        ulong referencesAfterValue = state.MemoryStatistics.ActiveRegistryReferences;
        referencesAfterValue.ShouldBe(baselineActiveReferences + 1);

        value.TryGet(out string? text).ShouldBeTrue();
        text.ShouldBe("hello");
        state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(referencesAfterValue);

        value.Dispose();
        state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(baselineActiveReferences);
    }

    [Fact]
    public void LuauValue_ToString_String_ShouldBeNonDestructive()
    {
        using var state = new LuauState();
        state.Globals.Set("payload", "hello");

        state.Globals.TryGetLuauValue("payload", out LuauValue value).ShouldBeTrue();
        try
        {
            value.ToString().ShouldBe("hello");
            value.TryGet(out string? afterToString).ShouldBeTrue();
            afterToString.ShouldBe("hello");
        }
        finally
        {
            value.Dispose();
        }
    }

    [Fact]
    public void LuauValue_TryGetBuffer_Twice_ShouldSucceed()
    {
        using var state = new LuauState();
        byte[] expected = [0x01, 0x02, 0x03, 0x04];
        state.Globals.Set("payload", expected);

        state.Globals.TryGetLuauValue("payload", out LuauValue value).ShouldBeTrue();
        try
        {
            value.TryGet(out byte[]? first).ShouldBeTrue();
            first.ShouldNotBeNull();
            first.ShouldBe(expected);

            value.TryGet(out byte[]? second).ShouldBeTrue();
            second.ShouldNotBeNull();
            second.ShouldBe(expected);
        }
        finally
        {
            value.Dispose();
        }
    }

    [Fact]
    public void LuauValue_ToString_Buffer_ShouldBeNonDestructive()
    {
        using var state = new LuauState();
        byte[] expected = [0x01, 0x02, 0x03];
        state.Globals.Set("payload", expected);

        state.Globals.TryGetLuauValue("payload", out LuauValue value).ShouldBeTrue();
        try
        {
            value.ToString().ShouldBe(Convert.ToHexString(expected));
            value.TryGet(out byte[]? afterToString).ShouldBeTrue();
            afterToString.ShouldNotBeNull();
            afterToString.ShouldBe(expected);
        }
        finally
        {
            value.Dispose();
        }
    }

    [Fact]
    public void LuauValue_ToString_Userdata_ShouldBeNonDestructive()
    {
        using var state = new LuauState();
        var payload = new ValueUserdata { Value = 123 };
        state.Globals.Set("payload", payload);

        state.Globals.TryGetLuauValue("payload", out LuauValue value).ShouldBeTrue();
        try
        {
            _ = value.ToString();

            value.TryGet(out LuauUserdata userdata).ShouldBeTrue();
            using (userdata)
            {
                userdata.TryGetManaged(out ValueUserdata? resolved, out string? error).ShouldBeTrue(error);
                ReferenceEquals(payload, resolved).ShouldBeTrue();
            }
        }
        finally
        {
            value.Dispose();
        }
    }
}
