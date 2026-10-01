using Shouldly;

[assembly: CaptureConsole]

namespace Darp.Luau.Tests;

// Synchronous embedding smoke test. The Lua files beside it are design sketches.
// See docs/AdvertisementAnalyzerExperiment.md; this does not validate their full API.
public sealed class AdvertisementAnalyzerTests
{
    [Fact]
    public void CreateAdvertisementAnalyzerModule_ShouldSucceed()
    {
        using var state = new LuauState();

        AdvertisementAnalyzer aa = state.RegisterModule<AdvertisementAnalyzer>();

        state.Globals.Set("version", 1);

        LuauChunk chunk = state.Load(
            """
            local aa = require("aa")
            local field1 = aa.Field:u8("p.u8", "u8", nil)
            local p = aa.Protocol("bitproto", "Bit-Packed Proto")

            p:set_dissector(function(evt, tree)
                tree:add("My tree", field1.title)
            end)

            aa.register_protocol(p)
            """
        );
        chunk.Execute();
        var evt = new Evt();
        var tree = new Tree();
        foreach (Protocol p in aa.Protocols)
        {
            p.Dissector!.Value.Invoke(IntoLuau.FromUserdata(evt), IntoLuau.FromUserdata(tree));
        }
        tree.Fields.ShouldBe([("My tree", "u8")]);
    }
}

[LuauModule("aa")]
public sealed partial class AdvertisementAnalyzer
{
    public List<Protocol> Protocols { get; } = [];

    [LuauMember(nameof(Field))]
    public static Field Field { get; } = new();

    [LuauMember("Protocol")]
    public static Protocol Protocol(string name, string description) => new(name, description);

    [LuauMember("register_protocol")]
    public void RegisterProtocol(Protocol protocol) => Protocols.Add(protocol);
}

[LuauUserdata]
public sealed partial class Field
{
    [LuauMember("u8")]
    public ProtocolField U8(string key, string title, DisplayType? display) =>
        new(key, title, ProtocolFieldType.U8, display ?? DisplayType.DEC);

    [LuauMember("u16")]
    public ProtocolField U16(string key, string title, DisplayType? display) =>
        new(key, title, ProtocolFieldType.U16, display ?? DisplayType.DEC);

    public static ProtocolField U24(string key, string title, DisplayType? display) =>
        new(key, title, ProtocolFieldType.U24, display ?? DisplayType.DEC);

    public static ProtocolField U32(string key, string title, DisplayType? display) =>
        new(key, title, ProtocolFieldType.U32, display ?? DisplayType.DEC);

    public static ProtocolField F32(string key, string title, DisplayType? display) =>
        throw new NotImplementedException();

    [LuauMember("bool")]
    public ProtocolField Bool(string key, string title, DisplayType? display) => throw new NotImplementedException();
}

public enum ProtocolFieldType
{
    U8,
    U16,
    U24,
    U32,
    U64,
    U128,
}

public enum DisplayType
{
    NONE,
    DEC,
    HEX,
}

[LuauUserdata]
public sealed partial class ProtocolField(string key, string title, ProtocolFieldType type, DisplayType display)
{
    [LuauMember("key")]
    public string Key { get; } = key;

    [LuauMember("title")]
    public string Title { get; } = title;

    [LuauMember("type")]
    public ProtocolFieldType Type { get; } = type;

    [LuauMember("display")]
    public DisplayType Display { get; } = display;
}

[LuauUserdata]
public sealed partial class Protocol(string name, string description) : IDisposable
{
    [LuauMember("name")]
    public string Name { get; } = name;

    [LuauMember("description")]
    public string Description { get; } = description;

    public Field[] Fields { get; set; } = [];

    public LuauFunction? Dissector { get; private set; }

    [LuauMember("set_dissector")]
    public void SetDissector(LuauFunctionView dissector)
    {
        Dissector?.Dispose();
        Dissector = dissector.ToOwned();
    }

    public static implicit operator IntoLuau(Protocol protocol) => IntoLuau.FromUserdata(protocol);

    public void Dispose() => Dissector?.Dispose();
}

[LuauUserdata]
public sealed partial class Evt
{
    // [LuauMember("get")]
    public IntoLuau Get(ReadOnlySpan<byte> key) => throw new NotImplementedException();
}

[LuauUserdata]
public sealed partial class Tree
{
    public List<(string Key, string Value)> Fields { get; } = [];

    [LuauMember("add")]
    public void Add(string key, string value) => Fields.Add((key, value));
}

public interface IProtocol { }
