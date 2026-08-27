using System;

namespace RiveTT.Core.Results;

/// <summary>User-facing text for internal failures: type name only, no raw message.</summary>
public static class SafeErrorMessages
{
    public static string ForInternal(Exception ex) =>
        $"Internal error ({ex.GetType().Name}). Details are in the local trace log.";
}
