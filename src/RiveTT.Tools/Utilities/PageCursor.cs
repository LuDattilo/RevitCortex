using System;
using System.Text;

namespace RiveTT.Tools.Utilities;

/// <summary>
/// Opaque offset cursor shared by every tool that pages through a
/// FilteredElementCollector result — first introduced by filter_elements.
/// Encodes the document version alongside the offset so a cursor from before
/// an edit is rejected instead of silently returning a shifted page.
/// </summary>
public static class PageCursor
{
    public static string Encode(long documentVersion, int offset)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{documentVersion}:{offset}"));

    public static bool TryDecode(string? cursor, long documentVersion, out int offset, out string? error)
    {
        offset = 0;
        error = null;
        if (string.IsNullOrWhiteSpace(cursor)) return true;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = decoded.Split(':');
            if (parts.Length != 2 || !long.TryParse(parts[0], out var version) ||
                !int.TryParse(parts[1], out offset) || offset < 0)
                throw new FormatException();
            if (version != documentVersion)
            {
                error = "The search cursor expired because the Revit document changed";
                return false;
            }
            return true;
        }
        catch
        {
            error = "The search cursor is invalid";
            return false;
        }
    }
}
