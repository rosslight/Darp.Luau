using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;

namespace Darp.Luau;

/// <summary> A virtual file system for abstract file operations. </summary>
public interface ILuauFileSystem
{
    /// <summary> Gets the current working directory. </summary>
    /// <returns> The current directory </returns>
    [Pure]
#pragma warning disable CA1024 // Use properties where appropriate -> ignored to mirror the Directory API
    string GetCurrentDirectory();
#pragma warning restore CA1024

    /// <summary> Determines whether the given path refers to an existing file. </summary>
    /// <param name="path">The path to test.</param>
    /// <returns>True, if the file exists. False if the file does not exist or an error occured</returns>
    [Pure]
    bool FileExists([NotNullWhen(true)] string? path);

    /// <summary> Determines whether the given path refers to an existing directory. </summary>
    /// <param name="path">The path to test.</param>
    /// <returns>True, if the directory exists. False if the directory does not exist or an error occured</returns>
    [Pure]
    bool DirectoryExists([NotNullWhen(true)] string? path);

    /// <summary> Reads the content of a file. If the file does not exist, returns null. </summary>
    /// <param name="path"> The path to the file to read </param>
    /// <returns> The content of the file read or null </returns>
    [Pure]
    string? ReadFile(string path);
}
