param(
    # Also publish two portable zips to artifacts/: a self-contained one and a small one (-small) that needs the .NET 10 Desktop Runtime.
    [switch]$Package,
    # Skip tests that need a signed-in desktop session (windows, tray, hotkeys), e.g. on a build server.
    [switch]$SkipInteractiveTests,
    # With -Package, also put a source zip (with the git history as a bundle) inside the self-contained portable folder.
    [switch]$WithSource
)
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
# A .NET kept beside the sources (.tools/dotnet) wins over the one on PATH.
$localDotnet=Join-Path $workspace '.tools\dotnet\dotnet.exe'
$dotnet=if(Test-Path -LiteralPath $localDotnet){$localDotnet}else{'dotnet'}
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
if(Test-Path -LiteralPath $localDotnet){$env:DOTNET_ROOT=Split-Path -Parent $localDotnet}
& (Join-Path $PSScriptRoot 'create-icon.ps1')
& $dotnet build (Join-Path $workspace 'tests\QuickMenu.Fixture\QuickMenu.Fixture.csproj') -c Debug --nologo
if($LASTEXITCODE -ne 0){throw 'The test fixture failed to build.'}
$filter=if($SkipInteractiveTests){@('--filter','Category!=Interactive')}else{@()}
& $dotnet test (Join-Path $workspace 'tests\QuickMenu.Tests\QuickMenu.Tests.csproj') -c Release --nologo @filter
if($LASTEXITCODE -ne 0){throw 'Tests failed.'}
if($Package) {
    $projectXml=[xml](Get-Content -Raw -LiteralPath (Join-Path $workspace 'src\QuickMenu\QuickMenu.csproj'))
    $version=@($projectXml.Project.PropertyGroup.Version | Where-Object { $_ })[0]
    # The large zip carries its own runtime and runs anywhere. The -small zip needs the .NET 10 Desktop Runtime;
    # without it the .NET launcher shows a dialog with a download link instead of failing silently.
    $builds=@(
        @{ Suffix=''; Publish=@('--self-contained','true','-p:IncludeNativeLibrariesForSelfExtract=true') },
        @{ Suffix='-small'; Publish=@('--self-contained','false') }
    )
    foreach($build in $builds) {
        $output=Join-Path $workspace "artifacts\portable-$version$($build.Suffix)"
        if(Test-Path -LiteralPath $output){Remove-Item -LiteralPath $output -Recurse -Force}
        & $dotnet publish (Join-Path $workspace 'src\QuickMenu\QuickMenu.csproj') -c Release -r win-x64 @($build.Publish) -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $output --nologo
        if($LASTEXITCODE -ne 0){throw 'Publishing failed.'}
        Copy-Item -LiteralPath (Join-Path $workspace 'docs\使用说明.md') -Destination $output
        Copy-Item -LiteralPath (Join-Path $workspace 'docs\user-guide.md') -Destination $output
        Copy-Item -LiteralPath (Join-Path $workspace 'LICENSE') -Destination (Join-Path $output 'LICENSE.txt')
        if($WithSource -and -not $build.Suffix) { & (Join-Path $PSScriptRoot 'package-source.ps1') -Destination (Join-Path $output "QuickMenu-Source-$version.zip") }
        Compress-Archive -Path (Join-Path $output '*') -DestinationPath (Join-Path $workspace "artifacts\QuickMenu-Portable-$version-x64$($build.Suffix).zip") -Force
    }
}
