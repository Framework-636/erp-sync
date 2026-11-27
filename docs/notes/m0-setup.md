# M0: Setup

**Goal:** an empty solution that builds and is pushed to the repo.
**Finished:** November 27, 2026

## What I did

1. Installed the .NET 10 SDK (10.0.404) with winget, and the C# Dev Kit extension in VS Code.
2. Created the solution and five empty projects with the `dotnet` command line (#2).
3. Set up user secrets so passwords and keys never go in the repo, and wrote the setup steps in the README (#4).

## The files, and what each one is for

### `global.json`

Tells the `dotnet` command which SDK version to use inside this folder. `rollForward: latestFeature` means it may use a newer 10.0 patch (for security fixes) but never jumps to .NET 11. This keeps my PC and the GitHub build (M5) on the same version.

### `erp-sync.slnx`

The solution file: a list of the projects that make up the system. Running `dotnet build` in the repo folder builds every project listed here. `.slnx` is the newer XML format, which is short enough to read. The older `.sln` format was full of generated IDs.

### `src/ErpSync.ErpSimulator` (console app)

A plain program that runs top to bottom. In M1 it will fill the stand-in ERP with fake items and keep changing quantities.

- `Program.cs` is the code. It only prints "Hello, World!" for now.
- `ErpSync.ErpSimulator.csproj` is the project file. `OutputType Exe` means it builds a runnable program, `TargetFramework net10.0` means it runs on .NET 10, `Nullable enable` makes the compiler warn me when something might be null, and `ImplicitUsings enable` adds common `using` lines automatically.

### `src/ErpSync.Bridge` (worker service)

A program that keeps running in the background until it's stopped. This will be the bridge that finds changes and pushes them (M2, M3).

- `Program.cs` builds a "host", the .NET piece that handles startup, configuration, logging and shutdown, and registers `Worker` as a background service.
- `Worker.cs` holds the loop. It logs a message every second until the app is stopped. In M2 this becomes the 15-second sync cycle.
- `appsettings.json` and `appsettings.Development.json` hold settings like log levels. Only non-secret values go here.
- `Properties/launchSettings.json` tells `dotnet run` how to start it locally. It sets the environment to `Development`, which is what loads `appsettings.Development.json`.
- `.csproj` uses the `Microsoft.NET.Sdk.Worker` SDK and references the `Microsoft.Extensions.Hosting` package. `UserSecretsId` links the project to its secrets file on my PC.

### `src/ErpSync.Api` (ASP.NET Core Web API)

A web server that answers HTTP requests. In M3 it will receive changes from the bridge.

- `Program.cs` sets up the web app. The template includes a sample `/weatherforecast` address, which I'll replace with a health check in M3.
- `ErpSync.Api.http` is a file of sample requests that VS Code can send to the running API.
- `appsettings*.json` and `launchSettings.json` work the same way as in the bridge. `launchSettings.json` also sets the local web addresses (ports).
- `.csproj` uses the `Microsoft.NET.Sdk.Web` SDK and the `Microsoft.AspNetCore.OpenApi` package, which describes the API's addresses in a standard format.

### `tests/ErpSync.Bridge.Tests` and `tests/ErpSync.Api.Tests` (xUnit)

Test projects. Each one has a `ProjectReference` to the project it tests, so tests can use that project's classes.

- `UnitTest1.cs` is an empty sample test. Real tests for change detection come in M2.
- Packages: `xunit` is the test framework, `xunit.runner.visualstudio` lets `dotnet test` and VS Code find and run the tests, `Microsoft.NET.Test.Sdk` is the test host, and `coverlet.collector` measures how much code the tests cover.

### `.gitignore` (already there)

Keeps build output (`bin/`, `obj/`), editor files and `.env` files out of git.

## Secrets

Passwords and keys go in .NET **user secrets**, never in `appsettings.json`. User secrets live in a JSON file under `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\`, outside the repo, so they can't be committed by accident. In the `Development` environment, .NET reads them automatically on top of `appsettings.json`. When the app is deployed (M5), the same setting names will come from Azure instead.

## Problems I hit

- **NU1100 on restore.** NuGet had no nuget.org source on my new PC, so no packages could download. Fixed with `dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org`. Lesson: the projects were still created, only the downloads failed, so I didn't need to start over.

## What I learned

- The `dotnet` command line can create, build and test a whole solution without Visual Studio.
