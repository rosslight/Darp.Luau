using System.Diagnostics.CodeAnalysis;

namespace Darp.Luau.Internal.Require;

internal sealed class FileSystem : ILuauFileSystem
{
    public string GetCurrentDirectory() => Directory.GetCurrentDirectory();

    public bool FileExists([NotNullWhen(true)] string? path) => File.Exists(path);

    public bool DirectoryExists([NotNullWhen(true)] string? path) => Directory.Exists(path);

    public string? ReadFile(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }
}
