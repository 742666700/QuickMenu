$ErrorActionPreference='Stop'
# One-time generator for the embedded heading font. The output is committed; normal builds do not need Python.
$source=Join-Path $env:WINDIR 'Fonts\NotoSerifSC-VF.ttf'
if(-not (Test-Path $source)) { throw "未找到 $source。请先安装 Noto Serif SC 可变字体。" }
$venv=Join-Path $env:TEMP 'quickmenu-fonttools'
if(-not (Test-Path (Join-Path $venv 'Scripts\python.exe'))) {
    python -m venv $venv
    & (Join-Path $venv 'Scripts\python.exe') -m pip install --quiet 'fonttools==4.53.1'
}
$root=Join-Path $PSScriptRoot '..\src\QuickMenu'
$output=Join-Path $root 'Assets\Fonts\QuickMenuSerifSC-SemiBold.ttf'
& (Join-Path $venv 'Scripts\python.exe') -I (Join-Path $PSScriptRoot 'subset_font.py') $source $root $output
if($LASTEXITCODE -ne 0) { throw '字体子集化失败。' }
