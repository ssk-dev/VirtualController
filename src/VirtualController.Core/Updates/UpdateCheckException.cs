namespace VirtualController.Core.Updates;

/// <summary>
/// Indicates an update check failed because the source was unreachable (e.g. no internet or server outage; see
/// <see cref="Exception.InnerException"/> for details) or returned an invalid/unexpected response. Thrown by
/// <see cref="UpdateChecker.CheckAsync"/> so callers can handle both cases consistently: startup checks ignore
/// the exception so normal app operation is unaffected, while manual checks display
/// <see cref="Exception.Message"/> as a readable error.
/// </summary>
public sealed class UpdateCheckException : Exception
{
    public UpdateCheckException(string message) : base(message)
    {
    }

    public UpdateCheckException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
