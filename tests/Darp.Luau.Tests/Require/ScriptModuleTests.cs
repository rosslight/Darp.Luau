using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Internal.Require;
using Shouldly;

namespace Darp.Luau.Tests.Require;

public sealed class ScriptModuleTests
{
    [Fact]
    public void RequireSimpleRelativePath()
    {
        var fs = new FakeFileSystem([
            ("./main.luau", """return require("./without_config/dependency")"""),
            ("./without_config/dependency.luau", """return {"result from dependency"}"""),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        using LuauTable result = state.LoadFile("./main.luau").Execute<LuauTable>();
        result.GetUtf8String(1).ShouldBe("result from dependency");
    }

    [Fact]
    public void RequireSimpleRelativePathFromSourceChunk()
    {
        var fs = new FakeFileSystem([("./without_config/dependency.luau", """return {"result from dependency"}""")]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        using LuauTable result = state.Load("""return require("./without_config/dependency")""").Execute<LuauTable>();

        result.GetUtf8String(1).ShouldBe("result from dependency");
    }

    [Fact]
    public void RequirePathWithUtf8Characters()
    {
        const string modulePath = "./unicode/\u00FCmlaut";
        var fs = new FakeFileSystem([(modulePath + ".luau", """return {"result from umlaut"}""")]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        using LuauTable result = state.Load($"""return require("{modulePath}")""").Execute<LuauTable>();

        result.GetUtf8String(1).ShouldBe("result from umlaut");
    }

    [Fact]
    public void RequireRelativeToRequiringFile()
    {
        var fs = new FakeFileSystem([
            (
                "./without_config/module.luau",
                """
                local result = require("./dependency")
                result[#result+1] = "required into module"
                return result
                """
            ),
            ("./without_config/dependency.luau", """return {"result from dependency"}"""),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        using LuauTable result = state.LoadFile("./without_config/module.luau").Execute<LuauTable>();
        result.GetUtf8String(1).ShouldBe("result from dependency");
        result.GetUtf8String(2).ShouldBe("required into module");
    }

    [Fact]
    public void NestedRequire_ShouldKeepParentAndChildCacheKeysSeparate()
    {
        var fs = new FakeFileSystem([
            (
                "./main.luau",
                """
                local parent1 = require("./parent")
                local child1 = require("./child")
                local parent2 = require("./parent")
                local child2 = require("./child")

                return {
                    parent1.name,
                    child1.name,
                    parent2.name,
                    child2.name,
                    parent1 == parent2,
                    child1 == child2,
                    parent1 == child1,
                }
                """
            ),
            (
                "./parent.luau",
                """
                local child = require("./child")
                return { name = "parent", child = child }
                """
            ),
            ("./child.luau", """return { name = "child" }"""),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        using LuauTable result = state.LoadFile("./main.luau").Execute<LuauTable>();
        result.GetUtf8String(1).ShouldBe("parent");
        result.GetUtf8String(2).ShouldBe("child");
        result.GetUtf8String(3).ShouldBe("parent");
        result.GetUtf8String(4).ShouldBe("child");
        result.TryGet(5, out bool sameParent).ShouldBeTrue();
        sameParent.ShouldBeTrue();
        result.TryGet(6, out bool sameChild).ShouldBeTrue();
        sameChild.ShouldBeTrue();
        result.TryGet(7, out bool parentIsChild).ShouldBeTrue();
        parentIsChild.ShouldBeFalse();
    }

    [Fact]
    public void RequireLuaFile()
    {
        var fs = new FakeFileSystem([
            ("./main.luau", """return require("./without_config/lua_dependency")"""),
            ("./without_config/lua_dependency.lua", """return {"result from lua_dependency"}"""),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        using LuauTable result = state.LoadFile("./main.luau").Execute<LuauTable>();
        result.GetUtf8String(1).ShouldBe("result from lua_dependency");
    }

    [Fact]
    public void RequireInitLuau()
    {
        var fs = new FakeFileSystem([
            ("./main.luau", """return require("./without_config/luau")"""),
            ("./without_config/luau/init.luau", """return {"result from init.luau"}"""),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        using LuauTable result = state.LoadFile("./main.luau").Execute<LuauTable>();
        result.GetUtf8String(1).ShouldBe("result from init.luau");
    }

    [Fact]
    public void RequireInitLua()
    {
        var fs = new FakeFileSystem([
            ("./main.luau", """return require("./without_config/lua")"""),
            ("./without_config/lua/init.lua", """return {"result from init.lua"}"""),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        using LuauTable result = state.LoadFile("./main.luau").Execute<LuauTable>();
        result.GetUtf8String(1).ShouldBe("result from init.lua");
    }

    [Fact]
    public void RequireSubmoduleUsingSelfAliasFromInit()
    {
        var fs = new FakeFileSystem([
            ("./main.luau", """return require("./without_config/nested")"""),
            (
                "./without_config/nested/init.luau",
                """
                local result = require("@self/submodule")
                return result
                """
            ),
            ("./without_config/nested/submodule.luau", """return {"result from submodule"}"""),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        using LuauTable result = state.LoadFile("./main.luau").Execute<LuauTable>();
        result.GetUtf8String(1).ShouldBe("result from submodule");
    }

    [Fact]
    public void RequireNestedInits()
    {
        var fs = new FakeFileSystem([
            (
                "./without_config/nested_inits_requirer.luau",
                """
                local result = require("./nested_inits")
                result[#result+1] = "required into module"
                return result
                """
            ),
            (
                "./without_config/nested_inits/init.luau",
                """
                local result = require("@self/init")
                return result
                """
            ),
            ("./without_config/nested_inits/init/init.luau", """return {"result from nested_inits/init"}"""),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        using LuauTable result = state.LoadFile("./without_config/nested_inits_requirer.luau").Execute<LuauTable>();
        result.GetUtf8String(1).ShouldBe("result from nested_inits/init");
        result.GetUtf8String(2).ShouldBe("required into module");
    }

    [Fact]
    public void CannotRequireInitLuauDirectly()
    {
        var fs = new FakeFileSystem([
            ("./main.luau", """return require("./without_config/nested/init")"""),
            ("./without_config/nested/init.luau", """return {"result from init"}"""),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        LuaException exception = Should.Throw<LuaException>(() => state.LoadFile("./main.luau").Execute<LuauTable>());
        exception.Message.ShouldContain("could not resolve child component \"init\"");
    }

    [Fact]
    public void RequireWithFileAmbiguity()
    {
        var fs = new FakeFileSystem([
            ("./without_config/ambiguous_file_requirer.luau", """return require("./ambiguous/file/dependency")"""),
            ("./without_config/ambiguous/file/dependency.lua", """return {"result from dependency"}"""),
            ("./without_config/ambiguous/file/dependency.luau", """return {"result from dependency"}"""),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        LuaException exception = Should.Throw<LuaException>(() =>
            state.LoadFile("./without_config/ambiguous_file_requirer.luau").Execute<LuauTable>()
        );
        exception.Message.ShouldContain(
            "error requiring module \"./ambiguous/file/dependency\": could not resolve child component \"dependency\" (ambiguous)"
        );
    }

    [Fact]
    public void RequireWithDirectoryAmbiguity()
    {
        var fs = new FakeFileSystem([
            (
                "./without_config/ambiguous_directory_requirer.luau",
                """return require("./ambiguous/directory/dependency")"""
            ),
            ("./without_config/ambiguous/directory/dependency.luau", """return {"result from dependency"}"""),
            ("./without_config/ambiguous/directory/dependency/init.luau", """return {"result from dependency"}"""),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        LuaException exception = Should.Throw<LuaException>(() =>
            state.LoadFile("./without_config/ambiguous_directory_requirer.luau").Execute<LuauTable>()
        );
        exception.Message.ShouldContain(
            "error requiring module \"./ambiguous/directory/dependency\": could not resolve child component \"dependency\" (ambiguous)"
        );
    }

    [Theory]
    [InlineData("""return require("/an/absolute/path")""")]
    [InlineData("""return require("an/unprefixed/path")""")]
    [InlineData("""return require([[an\unprefixed\path]])""")]
    public void RequireInvalidScriptPath(string source)
    {
        var fs = new FakeFileSystem([]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        LuaException exception = Should.Throw<LuaException>(() => state.Load(source).Execute<LuauTable>());
        exception.Message.ShouldContain("require path must start with a valid prefix: ./, ../, or @");
    }

    [Fact]
    public void RequirePathWithAliasFromLuaurc()
    {
        var fs = new FakeFileSystem([
            (
                "./config/src/.luaurc",
                """
                {
                    "aliases": {
                        "dep": "./dependency",
                        "subdir": "./subdirectory"
                    }
                }
                """
            ),
            ("./config/src/alias_requirer.luau", """return require("@dep")"""),
            ("./config/src/directory_alias_requirer.luau", """return require("@subdir/subdirectory_dependency")"""),
            ("./config/src/dependency.luau", """return {"result from dependency"}"""),
            (
                "./config/src/subdirectory/subdirectory_dependency.luau",
                """return {"result from subdirectory_dependency"}"""
            ),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        using LuauTable dependency = state.LoadFile("./config/src/alias_requirer.luau").Execute<LuauTable>();
        dependency.GetUtf8String(1).ShouldBe("result from dependency");

        using LuauTable subdirectoryDependency = state
            .LoadFile("./config/src/directory_alias_requirer.luau")
            .Execute<LuauTable>();
        subdirectoryDependency.GetUtf8String(1).ShouldBe("result from subdirectory_dependency");
    }

    [Fact]
    public void RequirePathWithAliasFromConfigLuau()
    {
        var fs = new FakeFileSystem([
            (
                "./config/src/.config.luau",
                """
                return {
                    luau = {
                        aliases = {
                            dep = "./dependency",
                            subdir = "./subdirectory"
                        }
                    }
                }
                """
            ),
            ("./config/src/alias_requirer.luau", """return require("@dep")"""),
            ("./config/src/directory_alias_requirer.luau", """return require("@subdir/subdirectory_dependency")"""),
            ("./config/src/dependency.luau", """return {"result from dependency"}"""),
            (
                "./config/src/subdirectory/subdirectory_dependency.luau",
                """return {"result from subdirectory_dependency"}"""
            ),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        using LuauTable dependency = state.LoadFile("./config/src/alias_requirer.luau").Execute<LuauTable>();
        dependency.GetUtf8String(1).ShouldBe("result from dependency");

        using LuauTable subdirectoryDependency = state
            .LoadFile("./config/src/directory_alias_requirer.luau")
            .Execute<LuauTable>();
        subdirectoryDependency.GetUtf8String(1).ShouldBe("result from subdirectory_dependency");
    }

    [Fact]
    public void RequirePathWithParentAlias()
    {
        var fs = new FakeFileSystem([
            (
                "./config/.luaurc",
                """
                {
                    "aliases": {
                        "dep": "./this_should_be_overwritten_by_child_luaurc",
                        "otherdep": "./src/other_dependency"
                    }
                }
                """
            ),
            (
                "./config/src/.luaurc",
                """
                {
                    "aliases": {
                        "dep": "./dependency"
                    }
                }
                """
            ),
            ("./config/src/alias_requirer.luau", """return require("@dep")"""),
            ("./config/src/parent_alias_requirer.luau", """return require("@otherdep")"""),
            ("./config/src/dependency.luau", """return {"result from dependency"}"""),
            ("./config/src/other_dependency.luau", """return {"result from other_dependency"}"""),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        using LuauTable childAlias = state.LoadFile("./config/src/alias_requirer.luau").Execute<LuauTable>();
        childAlias.GetUtf8String(1).ShouldBe("result from dependency");

        using LuauTable parentAlias = state.LoadFile("./config/src/parent_alias_requirer.luau").Execute<LuauTable>();
        parentAlias.GetUtf8String(1).ShouldBe("result from other_dependency");
    }

    [Theory]
    [InlineData("""return require("@@")""", "@@ is not a valid alias")]
    [InlineData("""return require("@.")""", "@. is not a valid alias")]
    [InlineData("""return require("@..")""", "@.. is not a valid alias")]
    [InlineData("""return require("@")""", " is not a valid alias")]
    [InlineData("""return require("@this.alias.does.not.exist")""", "@this.alias.does.not.exist is not a valid alias")]
    public void RequireInvalidAlias(string source, string expectedError)
    {
        var fs = new FakeFileSystem([]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        LuaException exception = Should.Throw<LuaException>(() => state.Load(source).Execute<LuauTable>());
        exception.Message.ShouldContain(expectedError);
    }

    [Fact]
    public void AliasNotParsedIfConfigsAmbiguous()
    {
        var fs = new FakeFileSystem([
            (
                "./config/.luaurc",
                """
                {
                    "aliases": {
                        "dep": "./dependency"
                    }
                }
                """
            ),
            (
                "./config/.config.luau",
                """
                return {
                    luau = {
                        aliases = {
                            dep = "./dependency"
                        }
                    }
                }
                """
            ),
            ("./config/requirer.luau", """return require("@dep")"""),
            ("./config/dependency.luau", """return {"result from dependency"}"""),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        LuaException exception = Should.Throw<LuaException>(() =>
            state.LoadFile("./config/requirer.luau").Execute<LuauTable>()
        );
        exception.Message.ShouldContain("could not resolve alias \"dep\" (ambiguous configuration file)");
    }

    [Fact]
    public void CannotRequireConfigLuau()
    {
        var fs = new FakeFileSystem([
            ("./config/requirer.luau", """return require("./.config")"""),
            ("./config/.config.luau", """return {"result from .config.luau"}"""),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        LuaException exception = Should.Throw<LuaException>(() =>
            state.LoadFile("./config/requirer.luau").Execute<LuauTable>()
        );
        exception.Message.ShouldContain("could not resolve child component \".config\"");
    }

    [Fact]
    public void RequireChainedAliasesSuccess()
    {
        var fs = new FakeFileSystem([
            (
                "./config/chained_aliases/.luaurc",
                """
                {
                    "aliases": {
                        "outer": "./"
                    }
                }
                """
            ),
            (
                "./config/chained_aliases/subdirectory/.luaurc",
                """
                {
                    "aliases": {
                        "passthroughinner": "./inner_dependency",
                        "passthroughouter": "@outer",
                        "dep": "@passthroughinner",
                        "outerdep": "@outer/outer_dependency",
                        "outerdir": "@passthroughouter"
                    }
                }
                """
            ),
            (
                "./config/chained_aliases/subdirectory/successful_requirer.luau",
                """
                local result = {}
                table.insert(result, require("@dep"))
                table.insert(result, require("@outerdep"))
                table.insert(result, require("@outerdir/outer_dependency"))
                return result
                """
            ),
            (
                "./config/chained_aliases/subdirectory/inner_dependency.luau",
                """return {"result from inner_dependency"}"""
            ),
            ("./config/chained_aliases/outer_dependency.luau", """return {"result from outer_dependency"}"""),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        using LuauTable result = state
            .LoadFile("./config/chained_aliases/subdirectory/successful_requirer.luau")
            .Execute<LuauTable>();
        result.TryGet(1, out LuauTable innerDependency).ShouldBeTrue();
        using (innerDependency)
        {
            innerDependency.GetUtf8String(1).ShouldBe("result from inner_dependency");
        }
        result.TryGet(2, out LuauTable outerDependency01).ShouldBeTrue();
        using (outerDependency01)
        {
            outerDependency01.GetUtf8String(1).ShouldBe("result from outer_dependency");
        }
        result.TryGet(3, out LuauTable outerDependency02).ShouldBeTrue();
        using (outerDependency02)
        {
            outerDependency02.GetUtf8String(1).ShouldBe("result from outer_dependency");
        }
    }

    [Theory]
    [InlineData(
        """return require("@cyclicentry")""",
        "error requiring module \"@cyclicentry\": detected alias cycle (@cyclic1 -> @cyclic2 -> @cyclic3 -> @cyclic1)"
    )]
    [InlineData(
        """return require("@brokenchain")""",
        "error requiring module \"@brokenchain\": @missing is not a valid alias"
    )]
    [InlineData(
        """return require("@dependoninner")""",
        "error requiring module \"@dependoninner\": @passthroughinner is not a valid alias"
    )]
    public void RequireChainedAliasesFailure(string source, string expectedError)
    {
        var fs = new FakeFileSystem([
            (
                "./config/chained_aliases/.luaurc",
                """
                {
                    "aliases": {
                        "outer": "./",
                        "cyclicentry": "@cyclic1",
                        "cyclic1": "@cyclic2",
                        "cyclic2": "@cyclic3",
                        "cyclic3": "@cyclic1",
                        "dependoninner": "@passthroughinner"
                    }
                }
                """
            ),
            (
                "./config/chained_aliases/subdirectory/.luaurc",
                """
                {
                    "aliases": {
                        "passthroughinner": "./inner_dependency",
                        "brokenchain": "@missing"
                    }
                }
                """
            ),
            ("./config/chained_aliases/subdirectory/requirer.luau", source),
            (
                "./config/chained_aliases/subdirectory/inner_dependency.luau",
                """return {"result from inner_dependency"}"""
            ),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        LuaException exception = Should.Throw<LuaException>(() =>
            state.LoadFile("./config/chained_aliases/subdirectory/requirer.luau").Execute<LuauTable>()
        );
        exception.Message.ShouldContain(expectedError);
    }

    [Fact]
    public void RequirePrimitiveValues()
    {
        var fs = new FakeFileSystem([
            (
                "./main.luau",
                """
                return
                    require("./types/boolean"),
                    require("./types/number"),
                    require("./types/string"),
                    require("./types/nil") == nil
                """
            ),
            ("./types/boolean.luau", "return false"),
            ("./types/nil.luau", "return nil"),
            ("./types/number.luau", "return 12345"),
            ("./types/string.luau", "return \"foo\""),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        (bool boolean, int number, string text, bool nilWasReturned) = state
            .LoadFile("./main.luau")
            .Execute<bool, int, string, bool>();
        boolean.ShouldBeFalse();
        number.ShouldBe(12345);
        text.ShouldBe("foo");
        nilWasReturned.ShouldBeTrue();
    }

    [Fact]
    public void RequireTableFunctionAndBuffer()
    {
        var fs = new FakeFileSystem([
            (
                "./main.luau",
                """
                return
                    require("./types/table"),
                    require("./types/function"),
                    require("./types/buffer")
                """
            ),
            ("./types/table.luau", """return { "foo", "bar" }"""),
            (
                "./types/function.luau",
                """
                return function()
                    return 1 + 1
                end
                """
            ),
            ("./types/buffer.luau", "return buffer.create(16)"),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        (LuauTable table, LuauFunction function, LuauBuffer buffer) = state
            .LoadFile("./main.luau")
            .Execute<LuauTable, LuauFunction, LuauBuffer>();
        using (table)
        {
            table.GetUtf8String(1).ShouldBe("foo");
            table.GetUtf8String(2).ShouldBe("bar");
        }
        using (function)
        {
            function.Invoke<int>().ShouldBe(2);
        }
        using (buffer)
        {
            buffer.TryGet(out ReadOnlySpan<byte> bytes).ShouldBeTrue();
            bytes.Length.ShouldBe(16);
        }
    }

    [Fact]
    public void RequireUserdata()
    {
        var fs = new FakeFileSystem([
            ("./main.luau", """return require("./types/userdata")"""),
            ("./types/userdata.luau", "return newproxy()"),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        using LuauValue result = state.LoadFile("./main.luau").Execute<LuauValue>();
        result.Type.ShouldBe(LuauValueType.Userdata);
    }

    [Fact]
    public void RequireCoroutine()
    {
        var fs = new FakeFileSystem([
            ("./main.luau", """return require("./types/coroutine")"""),
            ("./types/coroutine.luau", """return coroutine.create(function() return "foo" end)"""),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        using LuauCoroutine result = state.LoadFile("./main.luau").Execute<LuauCoroutine>();
        result.Resume<string>().ShouldBe("foo");
    }

    [Theory]
    [InlineData("return vector.create(1, 2, 3)")]
    public void RequireUnsupportedValueForLuauValue(string moduleSource)
    {
        var fs = new FakeFileSystem([
            ("./main.luau", """return require("./types/unsupported")"""),
            ("./types/unsupported.luau", moduleSource),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        Should.Throw<NotSupportedException>(() => state.LoadFile("./main.luau").Execute<LuauValue>());
    }

    [Fact]
    public void ScriptModule_ShouldCacheRequiredModule()
    {
        var fs = new FakeFileSystem([
            (
                "./main.luau",
                """
                local result1 = require("./dependency")
                local result2 = require("./dependency")
                return result1 == result2
                """
            ),
            ("./dependency.luau", """return {"result from dependency"}"""),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        state.LoadFile("./main.luau").Execute<bool>().ShouldBeTrue();
    }

    [Fact]
    public void FailedScriptModuleRequire_ShouldNotCacheFailureAsModuleValue()
    {
        var fs = new FakeFileSystem([
            ("./main.luau", """return require("./dependency")"""),
            ("./dependency.luau", "return"),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        LuaException exception = Should.Throw<LuaException>(() => state.LoadFile("./main.luau").Execute<LuauTable>());
        exception.Message.ShouldContain("must return a single value");

        fs.SetFile("./dependency.luau", "return { 17 }");

        LuauTable result = state.LoadFile("./main.luau").Execute<LuauTable>();
        using (result)
        {
            result.TryGet(1, out int value).ShouldBeTrue();
            value.ShouldBe(17);
        }
    }

    [Fact]
    public void ScriptModule_SyntaxError_ShouldSurfaceLoadError()
    {
        var fs = new FakeFileSystem([
            ("./main.luau", """return require("./dependency")"""),
            ("./dependency.luau", "return function("),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        LuaException exception = Should.Throw<LuaException>(() => state.LoadFile("./main.luau").Execute<LuauTable>());
        exception.Message.ShouldContain("error while loading module './dependency'");

        fs.SetFile("./dependency.luau", "return { 17 }");

        using LuauTable result = state.LoadFile("./main.luau").Execute<LuauTable>();
        result.TryGet(1, out int value).ShouldBeTrue();
        value.ShouldBe(17);
    }

    [Fact]
    public void ScriptModule_RuntimeError_ShouldSurfaceRunError()
    {
        var fs = new FakeFileSystem([
            ("./main.luau", """return require("./dependency")"""),
            ("./dependency.luau", """error("boom from dependency")"""),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        LuaException exception = Should.Throw<LuaException>(() => state.LoadFile("./main.luau").Execute<LuauTable>());
        exception.Message.ShouldContain("error while running module './dependency'");
        exception.Message.ShouldContain("boom from dependency");

        fs.SetFile("./dependency.luau", "return { 17 }");

        using LuauTable result = state.LoadFile("./main.luau").Execute<LuauTable>();
        result.TryGet(1, out int value).ShouldBeTrue();
        value.ShouldBe(17);
    }

    [Fact]
    public void ScriptModule_Yield_ShouldThrowCannotYield()
    {
        var fs = new FakeFileSystem([
            ("./main.luau", """return require("./dependency")"""),
            ("./dependency.luau", "coroutine.yield()"),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        LuaException exception = Should.Throw<LuaException>(() => state.LoadFile("./main.luau").Execute<LuauTable>());
        exception.Message.ShouldContain("module './dependency' can not yield");

        fs.SetFile("./dependency.luau", "return { 17 }");

        using LuauTable result = state.LoadFile("./main.luau").Execute<LuauTable>();
        result.TryGet(1, out int value).ShouldBeTrue();
        value.ShouldBe(17);
    }

    [Fact]
    public void ScriptModule_ReadFileReturnsNull_ShouldSurfaceReadErrorAndNotCacheFailure()
    {
        var fs = new ReadFailureFileSystem();

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        LuaException exception = Should.Throw<LuaException>(() => state.LoadFile("./main.luau").Execute<LuauTable>());
        exception.Message.ShouldContain("could not read file");
        exception.Message.ShouldContain("dependency");

        fs.FailDependencyRead = false;

        using LuauTable result = state.LoadFile("./main.luau").Execute<LuauTable>();
        result.TryGet(1, out int value).ShouldBeTrue();
        value.ShouldBe(17);
    }

    [Fact]
    public void ScriptModule_ReadFileThrows_ShouldUseLoaderExceptionBoundaryAndNotCacheFailure()
    {
        var fs = new ReadFailureFileSystem { ThrowDependencyRead = true };

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        LuaException exception = Should.Throw<LuaException>(() => state.LoadFile("./main.luau").Execute<LuauTable>());
        exception.Message.ShouldContain("script module loader callback failed");
        exception.Message.ShouldContain("boom from ReadFile");

        fs.ThrowDependencyRead = false;
        fs.FailDependencyRead = false;

        using LuauTable result = state.LoadFile("./main.luau").Execute<LuauTable>();
        result.TryGet(1, out int value).ShouldBeTrue();
        value.ShouldBe(17);
    }

    [Fact]
    public void ScriptModule_FileSystemThrowsWhileResolving_ShouldFailTheRequire()
    {
        var fs = new ReadFailureFileSystem { FailDependencyRead = false, ThrowDependencyLookup = true };

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        // Luau calls the file system while it resolves the path. An exception there must not leave the callback.
        Should.Throw<LuaException>(() => state.LoadFile("./main.luau").Execute<LuauTable>());

        fs.ThrowDependencyLookup = false;

        using LuauTable result = state.LoadFile("./main.luau").Execute<LuauTable>();
        result.TryGet(1, out int value).ShouldBeTrue();
        value.ShouldBe(17);
    }

    [Fact]
    public void ScriptModule_ErrorWithAnEmbeddedNul_ShouldKeepTheWholeMessage()
    {
        var fs = new FakeFileSystem([
            ("./main.luau", "local _, err = pcall(require, './dependency') return err"),
            ("./dependency.luau", "error('left\\0right', 0)"),
        ]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        string error = state.LoadFile("./main.luau").Execute<string>();

        error.ShouldContain("left\0right");
    }

    [Fact]
    public void EnableScriptModules_ShouldInstallRequire()
    {
        var fs = new FakeFileSystem([]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();

        state.Globals.TryGet("require", out LuauValue value).ShouldBeTrue();
        value.Type.ShouldBe(LuauValueType.Function);
    }

    [Fact]
    public void MultipleCallsToEnableScriptModules_ShouldWork()
    {
        var fs = new FakeFileSystem([]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.EnableScriptModules();
        state.EnableScriptModules();
    }

    [Fact]
    public async Task StatesNavigatingIndependently()
    {
        var fs01 = new FakeFileSystem(ChainedAliasFailureFiles("""return require("@brokenchain")"""));
        var fs02 = new FakeFileSystem(ChainedAliasFailureFiles("""return require("@dependoninner")"""));
        var fs03 = new FakeFileSystem(ChainedAliasFailureFiles("""return require("@cyclicentry")"""));
        var fs04 = new FakeFileSystem([
            (
                "./config/chained_aliases/.luaurc",
                """
                {
                    "aliases": {
                        "outer": "./"
                    }
                }
                """
            ),
            (
                "./config/chained_aliases/subdirectory/.luaurc",
                """
                {
                    "aliases": {
                        "passthroughinner": "./inner_dependency",
                        "dep": "@passthroughinner",
                        "outerdep": "@outer/outer_dependency"
                    }
                }
                """
            ),
            (
                "./config/chained_aliases/subdirectory/requirer.luau",
                """
                local result = {}
                table.insert(result, require("@dep"))
                table.insert(result, require("@outerdep"))
                return result
                """
            ),
            (
                "./config/chained_aliases/subdirectory/inner_dependency.luau",
                """return {"result from inner_dependency"}"""
            ),
            ("./config/chained_aliases/outer_dependency.luau", """return {"result from outer_dependency"}"""),
        ]);

        using var state01 = new LuauState(LuauLibraries.All, fs01);
        state01.EnableScriptModules();
        using var state02 = new LuauState(LuauLibraries.All, fs02);
        state02.EnableScriptModules();
        using var state03 = new LuauState(LuauLibraries.All, fs03);
        state03.EnableScriptModules();
        using var state04 = new LuauState(LuauLibraries.All, fs04);
        state04.EnableScriptModules();

        var tasks = new List<Task>
        {
            Task.Run(
                () =>
                {
                    LuaException exception = Should.Throw<LuaException>(() =>
                        state01.LoadFile("./config/chained_aliases/subdirectory/requirer.luau").Execute<LuauTable>()
                    );
                    exception.Message.ShouldContain(
                        "error requiring module \"@brokenchain\": @missing is not a valid alias"
                    );
                },
                TestContext.Current.CancellationToken
            ),
            Task.Run(
                () =>
                {
                    LuaException exception = Should.Throw<LuaException>(() =>
                        state02.LoadFile("./config/chained_aliases/subdirectory/requirer.luau").Execute<LuauTable>()
                    );
                    exception.Message.ShouldContain(
                        "error requiring module \"@dependoninner\": @passthroughinner is not a valid alias"
                    );
                },
                TestContext.Current.CancellationToken
            ),
            Task.Run(
                () =>
                {
                    LuaException exception = Should.Throw<LuaException>(() =>
                        state03.LoadFile("./config/chained_aliases/subdirectory/requirer.luau").Execute<LuauTable>()
                    );
                    exception.Message.ShouldContain(
                        "error requiring module \"@cyclicentry\": detected alias cycle (@cyclic1 -> @cyclic2 -> @cyclic3 -> @cyclic1)"
                    );
                },
                TestContext.Current.CancellationToken
            ),
            Task.Run(
                () =>
                {
                    using LuauTable result = state04
                        .LoadFile("./config/chained_aliases/subdirectory/requirer.luau")
                        .Execute<LuauTable>();
                    result.TryGet(1, out LuauTable innerDependency).ShouldBeTrue();
                    using (innerDependency)
                    {
                        innerDependency.GetUtf8String(1).ShouldBe("result from inner_dependency");
                    }
                    result.TryGet(2, out LuauTable outerDependency).ShouldBeTrue();
                    using (outerDependency)
                    {
                        outerDependency.GetUtf8String(1).ShouldBe("result from outer_dependency");
                    }
                },
                TestContext.Current.CancellationToken
            ),
        };

        await Task.WhenAll(tasks);
    }

    private static (string FileName, string Content)[] ChainedAliasFailureFiles(string source) =>
        [
            (
                "./config/chained_aliases/.luaurc",
                """
                {
                    "aliases": {
                        "cyclicentry": "@cyclic1",
                        "cyclic1": "@cyclic2",
                        "cyclic2": "@cyclic3",
                        "cyclic3": "@cyclic1",
                        "dependoninner": "@passthroughinner"
                    }
                }
                """
            ),
            (
                "./config/chained_aliases/subdirectory/.luaurc",
                """
                {
                    "aliases": {
                        "brokenchain": "@missing"
                    }
                }
                """
            ),
            ("./config/chained_aliases/subdirectory/requirer.luau", source),
        ];

    private sealed class ReadFailureFileSystem : ILuauFileSystem
    {
        private readonly FakeFileSystem _inner = new([
            ("./main.luau", """return require("./dependency")"""),
            ("./dependency.luau", "return { 17 }"),
        ]);

        public bool FailDependencyRead { get; set; } = true;

        public bool ThrowDependencyRead { get; set; }

        public bool ThrowDependencyLookup { get; set; }

        public string GetCurrentDirectory() => _inner.GetCurrentDirectory();

        public bool FileExists([NotNullWhen(true)] string? path)
        {
            ThrowIfDependencyLookup(path);
            return _inner.FileExists(path);
        }

        public bool DirectoryExists([NotNullWhen(true)] string? path)
        {
            ThrowIfDependencyLookup(path);
            return _inner.DirectoryExists(path);
        }

        private void ThrowIfDependencyLookup(string? path)
        {
            if (ThrowDependencyLookup && path?.Contains("dependency", StringComparison.Ordinal) == true)
                throw new InvalidOperationException("boom from the file system");
        }

        public string? ReadFile(string path)
        {
            if (!path.EndsWith("dependency.luau", StringComparison.Ordinal))
                return _inner.ReadFile(path);

            if (ThrowDependencyRead)
                throw new InvalidOperationException("boom from ReadFile");

            return FailDependencyRead ? null : _inner.ReadFile(path);
        }
    }
}
