# Darp.Luau

A .NET library for embedding the Luau scripting language in an application. It exists so that an application can run scripts written by other people and expose a typed, lifetime-safe API to them.

## Language

### Roles

**Host**:
The .NET application that embeds Luau and runs scripts.
_Avoid_: Consumer, app, embedder

**Script author**:
The person who writes the Luau scripts a host runs. Usually an end user of the host, not its developer.
_Avoid_: User

**Host API**:
Everything a host exposes to scripts.
_Avoid_: Bindings

### Defining a host API

**Generated tier**:
Defining a host API by annotating C# types and letting the library produce the glue. The first entry point.

**Manual tier**:
Defining a host API by declaring its members in code and handling their arguments and returns yourself. First-party, for what an annotated C# type cannot express.
_Avoid_: Fallback, escape hatch

**Managed callback**:
A C# function that scripts can call.
_Avoid_: Builder

**Managed userdata**:
A C# object that scripts hold by identity and interact with through its members.

**Userdata type**:
A C# class whose instances are managed userdata, together with everything scripts can do with them.

**Static side**:
The functions and values scripts reach through a userdata type itself rather than through one of its instances.
_Avoid_: Statics, class table

**Host module**:
A module scripts load with `require` that is defined in C#.
_Avoid_: Library

**Script module**:
A module scripts load with `require` that is defined in a Luau file.
_Avoid_: Library

### Lifetimes

**Owned reference**:
A handle to a Luau value that the host keeps until it disposes it.

**Borrowed view**:
A handle to a Luau value that is valid only while the managed callback that received it is running.

**Borrowed span**:
A view of bytes that live inside Luau, valid only for immediate use.

**Managed copy**:
A Luau value converted into an ordinary C# value with a normal .NET lifetime.

### Execution

**Coroutine**:
A Luau thread of execution that can suspend and resume.
_Avoid_: Thread (reserved for operating-system threads)
