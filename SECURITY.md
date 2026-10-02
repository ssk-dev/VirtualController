# Security Policy

## Supported Versions

Only the latest published [release](../../releases) is actively supported with security
fixes. Older releases (including any version offered through the in-app rollback dialog)
are provided as-is without further security maintenance.

| Version          | Supported          |
| ----------------- | ------------------ |
| Latest release     | :white_check_mark: |
| Older releases     | :x:                 |

## Reporting a Vulnerability

If you discover a security vulnerability in VirtualController (for example in the update
mechanism, the ViGEmBus/HidHide integration, or the handling of locally stored profile
data), please report it responsibly:

1. **Do not** open a public GitHub issue for security-relevant vulnerabilities.
2. Instead, use GitHub's private vulnerability reporting feature for this repository
   (**Security** tab -> **Report a vulnerability**), if available, or contact the
   maintainer directly through their GitHub profile.
3. Please include:
   - A description of the vulnerability and its potential impact.
   - Steps to reproduce it (if possible, with a minimal example).
   - The affected version(s).

You can expect an initial response within a reasonable timeframe. Confirmed
vulnerabilities will be addressed in a timely manner and, where appropriate, credited in
the release notes of the fixing release.

## Scope

This policy covers the VirtualController application and its own source code. It does
not cover third-party dependencies (e.g. ViGEmBus, HidHide, .NET runtime) - vulnerabilities
in those should be reported to their respective maintainers.
