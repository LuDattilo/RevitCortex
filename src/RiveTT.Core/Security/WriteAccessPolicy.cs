using System;

namespace RiveTT.Core.Security;

/// <summary>
/// The session-wide write lock driven by the RiveTT ribbon toggle.
///
/// RiveTT loads with Revit and asks no authorisation dialog, so until now the
/// only thing between a connected agent and the model was the agent's own
/// judgement. This is the physical switch that was missing: while it is locked,
/// the router refuses every tool that is not classified read-only.
///
/// Deliberately unreachable from any tool. The lock is meant to be a human
/// decision taken in Revit; a tool able to lift it would be a lock in name only.
/// It also survives <see cref="Session.CortexSession.Reinitialize"/>, because it
/// describes the Revit session and not the document that happens to be open.
/// </summary>
public sealed class WriteAccessPolicy
{
    // One immutable snapshot, swapped atomically. The status dialog reads the
    // flag, its origin and its timestamp together, so a torn read would credit a
    // state to the wrong origin. The router reads the flag on the pipe worker
    // thread for every single call, which is why the read path takes no lock.
    private volatile Snapshot _state;

    public WriteAccessPolicy(bool writesAllowed = true, string origin = "default")
    {
        _state = new Snapshot(writesAllowed, origin, DateTime.UtcNow);
    }

    public bool WritesAllowed => _state.WritesAllowed;

    /// <summary>Who set the current state: "ribbon", "startup", "default".</summary>
    public string ChangedBy => _state.Origin;

    public DateTime ChangedUtc => _state.ChangedUtc;

    /// <summary>
    /// Applies a new state. Returns true when this call actually flipped it,
    /// which lets a caller stay silent on a no-op click.
    /// </summary>
    public bool Set(bool writesAllowed, string origin)
    {
        var previous = _state;
        _state = new Snapshot(writesAllowed,
            string.IsNullOrWhiteSpace(origin) ? "unknown" : origin, DateTime.UtcNow);
        return previous.WritesAllowed != writesAllowed;
    }

    private sealed class Snapshot
    {
        public Snapshot(bool writesAllowed, string origin, DateTime changedUtc)
        {
            WritesAllowed = writesAllowed;
            Origin = origin;
            ChangedUtc = changedUtc;
        }

        public bool WritesAllowed { get; }
        public string Origin { get; }
        public DateTime ChangedUtc { get; }
    }
}
