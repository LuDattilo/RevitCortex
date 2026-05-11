using System;

namespace RevitCortex.Plugin.PowerBiLive;

public enum PowerBiAuthFlowStatus
{
    NotStarted,
    Starting,
    AwaitingUser,
    Completed,
    Failed
}

public class PowerBiAuthFlowSnapshot
{
    public PowerBiAuthFlowSnapshot(
        PowerBiAuthFlowStatus status,
        string? userCode,
        string? verificationUrl,
        DateTimeOffset? expiresOn,
        string? username,
        string? errorMessage,
        DateTimeOffset updatedAtUtc)
    {
        Status = status;
        UserCode = userCode;
        VerificationUrl = verificationUrl;
        ExpiresOn = expiresOn;
        Username = username;
        ErrorMessage = errorMessage;
        UpdatedAtUtc = updatedAtUtc;
    }

    public PowerBiAuthFlowStatus Status { get; }
    public string? UserCode { get; }
    public string? VerificationUrl { get; }
    public DateTimeOffset? ExpiresOn { get; }
    public string? Username { get; }
    public string? ErrorMessage { get; }
    public DateTimeOffset UpdatedAtUtc { get; }

    public bool IsRunning =>
        Status == PowerBiAuthFlowStatus.Starting ||
        Status == PowerBiAuthFlowStatus.AwaitingUser;
}

public class PowerBiAuthFlowState
{
    private readonly object _sync = new object();
    private PowerBiAuthFlowStatus _status = PowerBiAuthFlowStatus.NotStarted;
    private string? _userCode;
    private string? _verificationUrl;
    private DateTimeOffset? _expiresOn;
    private string? _username;
    private string? _errorMessage;
    private DateTimeOffset _updatedAtUtc = DateTimeOffset.UtcNow;

    public bool TryBegin()
    {
        lock (_sync)
        {
            if (_status == PowerBiAuthFlowStatus.Starting ||
                _status == PowerBiAuthFlowStatus.AwaitingUser)
                return false;

            _status = PowerBiAuthFlowStatus.Starting;
            _userCode = null;
            _verificationUrl = null;
            _expiresOn = null;
            _username = null;
            _errorMessage = null;
            _updatedAtUtc = DateTimeOffset.UtcNow;
            return true;
        }
    }

    public void SetDeviceCode(string userCode, string verificationUrl, DateTimeOffset expiresOn)
    {
        lock (_sync)
        {
            _status = PowerBiAuthFlowStatus.AwaitingUser;
            _userCode = userCode;
            _verificationUrl = verificationUrl;
            _expiresOn = expiresOn;
            _errorMessage = null;
            _updatedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public void SetCompleted(string? username)
    {
        lock (_sync)
        {
            _status = PowerBiAuthFlowStatus.Completed;
            _username = username;
            _errorMessage = null;
            _updatedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public void SetFailed(string errorMessage)
    {
        lock (_sync)
        {
            _status = PowerBiAuthFlowStatus.Failed;
            _errorMessage = errorMessage;
            _updatedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public PowerBiAuthFlowSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new PowerBiAuthFlowSnapshot(
                _status,
                _userCode,
                _verificationUrl,
                _expiresOn,
                _username,
                _errorMessage,
                _updatedAtUtc);
        }
    }
}
