# Security Policy

## Reporting a Vulnerability

Please report security vulnerabilities to **security@poli.page**.

Do not file public GitHub issues for security concerns.
We aim to respond within 48 hours.

## Supported Versions

Only the latest minor version of `PoliPage` on NuGet receives security
updates. NuGet packages are immutable — old releases remain installable
but are not patched in place. We use `dotnet list package --vulnerable`
in CI to catch transitive issues; consumers should run the same command
in their own builds.
