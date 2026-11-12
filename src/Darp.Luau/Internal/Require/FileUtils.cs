using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Darp.Luau.Internal.Require;

/// <summary>
/// A collection of utilities for working with file paths.
/// </summary>
/// <seealso href="https://github.com/luau-lang/luau/blob/master/CLI/src/FileUtils.cpp"/>
internal static class FileUtils
{
    public static bool IsAbsolutePath(ReadOnlySpan<char> path)
    {
        if (OperatingSystem.IsWindows())
        {
            // Must either begin with "X:/", "X:\", "/", or "\", where X is a drive letter
            return (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '/' || path[2] == '\\'))
                || (path.Length >= 1 && (path[0] == '/' || path[0] == '\\'));
        }
        // Must begin with '/'
        return path.Length > 0 && path[0] == '/';
    }

    public static string NormalizePath(string strPath)
    {
        string[] parts = strPath.Split('/', '\\');
        bool bIsAbsolute = IsAbsolutePath(strPath);

        //
        // 1. Normalize path components
        //

        List<string> partsNormalized = [];
        for (int i = bIsAbsolute ? 1 : 0; i < parts.Length; ++i)
        {
            string strPart = parts[i];
            if (Equals(strPart, ".."))
            {
                if (partsNormalized.Count == 0)
                {
                    if (!bIsAbsolute)
                        partsNormalized.Add("..");
                }
                else if (Equals(partsNormalized.Last(), ".."))
                {
                    partsNormalized.Add("..");
                }
                else
                {
                    partsNormalized.RemoveAt(partsNormalized.Count - 1);
                }
            }
            else if (strPart.Length > 0 && !Equals(strPart, "."))
            {
                partsNormalized.Add(strPart);
            }
        }

        var sbNormalized = new StringBuilder();

        //
        // 2. Add correct prefix to formatted path
        //

        if (bIsAbsolute)
        {
            sbNormalized.Append(parts[0]).Append('/');
        }
        else if (partsNormalized.Count == 0 || !Equals(partsNormalized[0], ".."))
        {
            sbNormalized.Append("./");
        }

        //
        // 3. Join path components to form the normalized path
        //

        for (int i = 0; i < partsNormalized.Count; ++i)
        {
            if (i > 0)
                sbNormalized.Append('/');
            sbNormalized.Append(partsNormalized[i]);
        }

        string strNormalized = sbNormalized.ToString();
        if (HasSuffix(strNormalized, ".."))
            strNormalized += "/";
        return strNormalized;
    }

    public static int RequiredIndexOfFirstSlash(ReadOnlySpan<char> str)
    {
        int nPos = str.IndexOf('/');
        if (nPos >= 0)
            return nPos;

        throw new ArgumentOutOfRangeException(
            nameof(str),
            "String provided does not provide a '/'. Methods that call this should have checked the presence before!"
        );
    }

    public static int RequiredIndexOfLastSlash(string str)
    {
        int nPos = str.AsSpan().LastIndexOf('/');
        if (nPos >= 0)
            return nPos;

        throw new ArgumentOutOfRangeException(
            nameof(str),
            "String provided does not provide a '/'. Methods that call this should have checked the presence before!"
        );
    }

    public static bool HasSuffix(string str, string strSuffix)
    {
        return str.EndsWith(strSuffix, StringComparison.InvariantCulture);
    }

    public static string RemoveSuffix(string str, string strSuffix)
    {
        return str.Remove(str.Length - strSuffix.Length);
    }
}
