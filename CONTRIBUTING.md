# Contributing to VirtualController

Thank you for your interest in contributing! This document explains how to set up the
project, the conventions used in this repository, and how to submit changes.

## Code of Conduct

This project follows a [Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are
expected to uphold it.

## Getting started

1. Make sure the [prerequisites](README.md#prerequisites-one-time-on-the-target-machine)
   from the README are installed (Windows 10 1809+, ViGEmBus, .NET 8 SDK).
2. Fork the repository and clone your fork.
3. Restore and build the solution:

   ```
   dotnet restore
   dotnet build -c Release
   ```

4. Run the app from source as described in the
   [README](README.md#running-from-source).

See the [Architecture overview](README.md#architecture-overview) in the README for a
short description of the `VirtualController.Core` (pure logic) and `VirtualController.App`
(WPF UI) projects.

## Making changes

- Keep changes focused and scoped to a single topic where possible.
- Follow the existing code style of the file/area you are touching (naming, indentation,
  comment style). This project uses mostly German inline comments in existing code; new
  comments may be written in either German or English, but should match the surrounding
  file where feasible.
- For WPF/XAML changes, keep styling centralized in `Themes/Theme.xaml` (colors/brushes)
  and `Themes/Styles.xaml` (control styles) instead of hard-coding values in views.
- If you change persisted data formats (`VirtualController.Core/Profiles`), make sure
  existing JSON files on disk can still be loaded (add migration logic if needed).

## Commit messages

This repository uses a specific commit message convention - see
[docs/COMMIT_CONVENTIONS.md](docs/COMMIT_CONVENTIONS.md) for the full description. In
short:

```
feature: - <short description of the change>

- <detail bullet 1>
- <detail bullet 2>
```

- Use `feature:` for new functionality/behavior and `fix:` for bug fixes.
- Write commit messages in English.
- The subject explains *why*/*what changes*, the body lists concrete details (affected
  files, classes, resource keys, etc.).

Release notes are generated automatically from these commit messages (see
`.github/workflows/release.yml`), so following this format helps keep the project's
release history useful for users.

## Pull requests

1. Create a feature branch from `master`.
2. Make sure the solution builds (`dotnet build -c Release`) without new warnings where
   avoidable.
3. Fill out the pull request template with a clear description of the change and, if
   applicable, screenshots for UI changes.
4. Reference any related issue(s).

## Reporting bugs / requesting features

Please use the provided [issue templates](.github/ISSUE_TEMPLATE) when opening a new
issue, so that all the information needed to triage it is included from the start.

## License

By contributing, you agree that your contributions will be licensed under the project's
[Non-Commercial Source License (NCSL)](LICENSE).
