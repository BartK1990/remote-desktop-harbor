# Project setup

Replace `MyApp` with the product name. Target framework: the current .NET LTS (`net10.0` at the time of writing).

## Solution layout

```
MyApp.slnx
global.json                      # pinned SDK
Directory.Build.props            # shared compiler settings
Directory.Packages.props         # central package versions
src/
  MyApp.App/        net10.0-windows, WPF, WinExe  — views, view models, composition root
  MyApp.Windows/    net10.0-windows               — P/Invoke, DPAPI, Credential Manager, registry reads, OS integration
  MyApp.Core/       net10.0                        — domain, logic, crypto envelope, interfaces (IKeyProtector, …)
  MyApp.Data/       net10.0                        — storage (e.g. SQLite + Dapper), migrations
tests/
  MyApp.Core.Tests/ MyApp.Data.Tests/              — run on any OS
  MyApp.App.Tests/                                 — WPF/DPAPI; runs on Windows only
tools/                                             — dev tools not shipped (e.g. screenshot capture)
docs/spec/SPEC.md, docs/adr/                       — optional spec + ADRs
website/                                           — optional docs site
```

Dependency direction: `App → Windows → Core`, `App → Data → Core`. `Core`/`Data` never reference Windows APIs;
define an interface in `Core` and implement it in `Windows` (e.g. `IKeyProtector` → `DpapiKeyProtector`).

## global.json

```json
{ "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }
```

## Directory.Build.props

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <!-- Lets Linux/macOS build the Windows-only projects (WPF, interop). -->
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
    <Product>MyApp</Product>
    <Version>0.1.0</Version>           <!-- overridden by -p:Version in release -->
    <Deterministic>true</Deterministic>
    <!-- Symbols inside the binaries: release folders contain only the exe. -->
    <DebugType Condition="'$(Configuration)' == 'Release'">embedded</DebugType>
    <!-- Add analyzer rules that are pure noise for the codebase here, with care. -->
    <NoWarn>$(NoWarn)</NoWarn>
  </PropertyGroup>
</Project>
```

`Directory.Packages.props` sets `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` and lists
every `<PackageVersion>`; csproj files use `<PackageReference Include="…" />` without versions.

## App csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <AssemblyName>MyApp</AssemblyName>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <ApplicationIcon>Assets\AppIcon.ico</ApplicationIcon>
    <!-- Startup performance. -->
    <TieredPGO>true</TieredPGO>
    <TieredCompilation>true</TieredCompilation>
    <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
    <!-- Built-in Fluent theme (Application.ThemeMode) is flagged experimental. -->
    <NoWarn>$(NoWarn);WPF0001</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" />
  </ItemGroup>
  <ItemGroup>
    <Resource Include="Assets\AppIcon.ico" />   <!-- also usable as window/title-bar icon -->
  </ItemGroup>
</Project>
```

OS-neutral library projects: `<TargetFramework>net10.0</TargetFramework>`, optionally `<IsTrimmable>true</IsTrimmable>`
to keep them reusable.

## Publish profile — `src/MyApp.App/Properties/PublishProfiles/win-x64.pubxml`

```xml
<Project>
  <PropertyGroup>
    <Configuration>Release</Configuration>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <!-- Native DLLs (SQLite, WPF, MSAL, …) bundled; extracted to per-user temp on first run, no admin. -->
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <!-- Precompiled code for startup. Compression off on purpose: it decompresses on every start. -->
    <PublishReadyToRun>true</PublishReadyToRun>
    <EnableCompressionInSingleFile>false</EnableCompressionInSingleFile>
    <DebugType>embedded</DebugType>
    <CopyOutputSymbolsToPublishDirectory>false</CopyOutputSymbolsToPublishDirectory>
  </PropertyGroup>
</Project>
```

Publish: `dotnet publish src/MyApp.App/MyApp.App.csproj -p:PublishProfile=win-x64 -o out`.
Framework-dependent variant: `-c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
-p:PublishReadyToRun=true -p:DebugType=embedded`.

Do **not** set `PublishTrimmed` or `PublishAot` — unsupported for WPF.

## app.manifest

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="MyApp.app" />
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <!-- Never ask for elevation: must run for standard users. -->
        <requestedExecutionLevel level="asInvoker" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" /> <!-- Windows 10 / 11 -->
    </application>
  </compatibility>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
      <longPathAware xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">true</longPathAware>
    </windowsSettings>
  </application>
</assembly>
```

## Windows-only test project

Builds everywhere, runs only on Windows, so `dotnet test` on Linux skips it instead of failing:

```xml
<PropertyGroup>
  <TargetFramework>net10.0-windows</TargetFramework>
  <UseWPF>true</UseWPF>
  <IsTestProject Condition="'$(OS)' != 'Windows_NT'">false</IsTestProject>
  <NoWarn>$(NoWarn);CA1416</NoWarn>
</PropertyGroup>
<ItemGroup>
  <!-- Copy XAML sources next to the tests for view-source checks (see wpf-ui.md). -->
  <None Include="..\..\src\MyApp.App\Views\*.xaml" Link="Xaml\%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
  <None Include="..\..\src\MyApp.App\Themes\*.xaml" Link="Xaml\Themes\%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```
