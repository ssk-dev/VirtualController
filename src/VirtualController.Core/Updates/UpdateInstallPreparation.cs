namespace VirtualController.Core.Updates;

/// <summary>
/// Result of <see cref="UpdateInstaller.PrepareAsync"/>: the new version's ZIP archive was downloaded and
/// extracted to a temporary staging directory. Contains everything the separate updater process (see
/// <see cref="UpdateInstaller.LaunchUpdaterProcess"/>) needs to copy files into the installation after the app
/// exits and then restart it.
/// </summary>
/// <param name="StagingDirectory">Temporary directory containing the extracted update files.</param>
/// <param name="InstallDirectory">Target installation directory containing the executable and required native
/// WPF DLLs; the updater copies the new files here.</param>
/// <param name="ExecutableFileName">Main executable filename (e.g. "VirtualController.exe") used by the updater
/// to restart the app after copying.</param>
/// <param name="ProcessId">ID of the still-running app process. The updater waits for it to exit before replacing
/// target files locked during runtime.</param>
/// <param name="TargetVersion">Version being installed (e.g. "1.5.0"), for display in the separate updater only;
/// does not affect installation.</param>
public sealed record UpdateInstallPreparation(
    string StagingDirectory,
    string InstallDirectory,
    string ExecutableFileName,
    int ProcessId,
    string TargetVersion);
