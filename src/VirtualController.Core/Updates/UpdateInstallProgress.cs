namespace VirtualController.Core.Updates;

/// <summary>
/// Progress information for one installation step (see <see cref="UpdateInstaller"/>), reported through
/// <see cref="IProgress{T}"/> during <see cref="UpdateInstaller.PrepareAsync"/> for the update dialog's
/// progress bar and step description, e.g. "Step 1 of 3: Downloading files".
/// </summary>
/// <param name="StepNumber">Current step number (1-based).</param>
/// <param name="TotalSteps">Total number of steps (see <see cref="UpdateInstaller.TotalSteps"/>).</param>
/// <param name="StepDescription">User-facing description of the current step, e.g. "Downloading files".</param>
/// <param name="OverallPercent">Progress across the entire installation (0-100), not just the current step,
/// so the progress bar advances continuously rather than resetting at each step.</param>
public sealed record UpdateInstallProgress(int StepNumber, int TotalSteps, string StepDescription, double OverallPercent);
