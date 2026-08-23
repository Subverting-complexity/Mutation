# Contributing to Mutation

Thank you for your interest in contributing to Mutation. This document covers the process for reporting issues, submitting changes, and the conventions used in this project.

## Reporting bugs and requesting features

Open a [GitHub Issue](https://github.com/Subverting-complexity/Mutation/issues) describing the problem or idea. Include steps to reproduce for bugs, and a clear description of the expected behaviour for feature requests.

## Submitting changes

1. Fork the repository and create a branch from `main`.
2. Make your changes and add or update tests where applicable.
3. Ensure the tests pass (see below).
4. Open a pull request against `main` with a clear description of what the change does and why.

## Building and testing

Mutation is a WinUI 3 application targeting .NET 10. **Windows is required** for building and running the tests because the project depends on the Windows App SDK.

```
dotnet test Mutation.slnx --configuration Release
```

## Code style

- **Tabs, not spaces** for indentation.
- **TreatWarningsAsErrors** is enabled. The build must complete with zero warnings.
- **Nullable reference types** are enabled project-wide. Avoid suppressing nullable warnings unless there is a clear justification.

## Code of Conduct

This project follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code.
