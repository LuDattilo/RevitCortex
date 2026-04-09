namespace RevitCortex.Core.Results;

public enum CortexErrorCode
{
    None = 0,
    ElementNotFound = 100,
    PermissionDenied = 200,
    TransactionFailed = 300,
    InvalidInput = 400,
    Timeout = 500,
    Unknown = 900
}
