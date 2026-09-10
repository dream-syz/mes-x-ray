# ADR-0002: Target framework — .NET 10

- Status: accepted (confirmed by the project owner on 2026-09-10; the design document was updated to match)
- Date: 2026-09-10
- Design reference: §11 (Backend: ".NET 10 / ASP.NET Core")

## Context

An earlier draft of the design recommended .NET 8 to stay aligned with the existing EBBA stack. X-Ray, however, does not ship a runtime component into the MES: it is a stand-alone analysis service that only *reads* sanitized source, SQL definitions and fixtures. The development and demo machines run the .NET 10 SDK, and .NET 10 is the current LTS release.

## Decision

All projects target `net10.0`; `global.json` pins SDK `10.0.100` with roll-forward `latestFeature`. Central Package Management (`Directory.Packages.props`) pins Roslyn 5.0, ScriptDom 180.x, ASP.NET Core 10.0.x packages (including `Microsoft.AspNetCore.OpenApi`) and xUnit. `TreatWarningsAsErrors` with `AnalysisLevel=latest-recommended` is enabled for every project.

No retargeting to .NET 8 is planned. Should it ever become necessary, nothing in the code base depends on .NET 10-only APIs without a .NET 8 equivalent (minimal APIs, `IExceptionHandler`, `IProblemDetailsService`, `WebApplicationFactory`, source-generated regexes, collection expressions); the change would be `TargetFramework` in `Directory.Build.props`, package versions in `Directory.Packages.props`, and swapping the OpenAPI package for Swashbuckle.

## Consequences

- The POC runs on the machines it is demoed from without installing an extra SDK, and CI (`setup-dotnet` with `global.json`) uses the same version.
- The scanners analyze the **fixture's** C# with Roslyn independently of the target framework of the scanned code, so nothing about EBBA needs to change.
