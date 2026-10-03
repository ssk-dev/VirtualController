namespace VirtualController.Core.Updates;

/// <summary>
/// Indicates that preparing or starting update installation failed (download failed, archive is corrupted/not
/// extractable, or updater process could not start). Thrown before any installed file is modified (see
/// <see cref="UpdateInstaller"/> docs), so the current version remains usable for these failures.
/// </summary>
public sealed class UpdateInstallException : Exception
{
    public UpdateInstallException(string message) : base(message)
    {
    }

    public UpdateInstallException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
