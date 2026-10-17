param([string]$Destination)
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
$projectXml=[xml](Get-Content -Raw -LiteralPath (Join-Path $workspace 'src\QuickMenu\QuickMenu.csproj'))
$version=[string]$projectXml.Project.PropertyGroup.Version
if([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination=Join-Path $workspace "artifacts\portable-$version\QuickMenu-Source-$version.zip"
}
$Destination=[System.IO.Path]::GetFullPath($Destination)
[System.IO.Directory]::CreateDirectory((Split-Path -Parent $Destination)) | Out-Null
$temporary="$Destination.$([guid]::NewGuid().ToString('N')).tmp"
$rootName="QuickMenu-Source-$version"
$files=@()
foreach($name in @('QuickMenu.slnx','global.json','.gitignore','README.md','design-qa.md')) {
    $file=Join-Path $workspace $name
    if(Test-Path -LiteralPath $file -PathType Leaf){$files+=Get-Item -LiteralPath $file -Force}
}
foreach($name in @('src','tests','scripts','docs')) {
    $files+=Get-ChildItem -LiteralPath (Join-Path $workspace $name) -File -Recurse -Force | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj|\.git|\.tools|node_modules)[\\/]' -and $_.Extension -notin @('.user','.suo')
    }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=$null
try {
    $archive=[System.IO.Compression.ZipFile]::Open($temporary,[System.IO.Compression.ZipArchiveMode]::Create)
    foreach($file in $files | Sort-Object FullName -Unique) {
        $relative=$file.FullName.Substring($workspace.Length+1).Replace('\','/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,"$rootName/$relative",[System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    # Carry the full commit history as a git bundle, so another machine can clone it and keep iterating.
    $git=Get-Command git -ErrorAction SilentlyContinue
    if($git -and (Test-Path -LiteralPath (Join-Path $workspace '.git'))) {
        $bundle=Join-Path ([System.IO.Path]::GetTempPath()) "QuickMenu-$([guid]::NewGuid().ToString('N')).bundle"
        try {
            & $git.Source -C $workspace bundle create $bundle --all 2>&1 | Out-Null
            if($LASTEXITCODE -eq 0) { [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$bundle,"$rootName/QuickMenu.bundle",[System.IO.Compression.CompressionLevel]::Optimal) | Out-Null }
            else { Write-Warning '未能生成 git 历史包，源码包中不含提交历史。' }
        }
        finally { if(Test-Path -LiteralPath $bundle){Remove-Item -LiteralPath $bundle} }
    }
    $archive.Dispose()
    $archive=$null
    Move-Item -LiteralPath $temporary -Destination $Destination -Force
    Write-Output "源码包：$Destination"
}
finally {
    if($null -ne $archive){$archive.Dispose()}
    if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary}
}
