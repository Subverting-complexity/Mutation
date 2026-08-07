## Tech stack

This is a .Net 10 solution.
The Mutation.Ui is a WinUI 3 project (NOT WPF).

## Build \& test

* Restore: dotnet restore
* Build: dotnet build --configuration Release > logs/build_output.txt
* Test: dotnet test --configuration Release --logger "trx;LogFileName=test-results.trx" > logs/test_output.txt

Note: Output files are redirected to the `logs/` directory to keep the root clean.


## Conventions

* Use the .NET 10 SDK already installed in the environment
* Prefer 'dotnet build' over 'msbuild'
* Use tabs and not spaces.

## Package versions

Versions are managed centrally in `Directory.Packages.props`. A project's
`<PackageReference>` carries no `Version` attribute — add or change the
`<PackageVersion>` entry instead, so every project agrees on one version.

Transitive pinning is on, so a `<PackageVersion>` also settles a package
nothing references directly. That is how the `Microsoft.Extensions.*` family
is held on a single 10.0.x band even though a dependency asks for 8.0.x.
