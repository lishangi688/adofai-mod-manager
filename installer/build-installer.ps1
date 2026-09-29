# ADOFAI Mod Manager —— 一键打包（发布版 + 安装包）
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
#   powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 -Version 0.2.0
#
# 产物：
#   dist\ADOFAI-Mod-Manager\                       绿色版（解压即用）
#   dist\ADOFAI-Mod-Manager-v{版本}-win-x64.zip    绿色版压缩包
#   dist\installer\ADOFAI-Mod-Manager-Setup-{版本}.exe   安装包

param(
    [string]$Version = "0.2.1",
    [switch]$Installer
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "==> 1/3 发布自包含版本 (v$Version)" -ForegroundColor Cyan
if (Test-Path 'dist\ADOFAI-Mod-Manager') { Remove-Item 'dist\ADOFAI-Mod-Manager' -Recurse -Force }
dotnet publish 'src\AdofaiModManager\AdofaiModManager.csproj' `
    -c Release -r win-x64 --self-contained true `
    -p:DebugType=none -p:Version=$Version `
    -o 'dist\ADOFAI-Mod-Manager'
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish 失败' }

Write-Host "==> 2/3 生成绿色版 zip" -ForegroundColor Cyan
$zip = "dist\ADOFAI-Mod-Manager-v$Version-win-x64.zip"
Get-ChildItem 'dist' -Filter '*.zip' -ErrorAction SilentlyContinue | Remove-Item -Force
Compress-Archive -Path 'dist\ADOFAI-Mod-Manager\*' -DestinationPath $zip -CompressionLevel Optimal
Write-Host "    $zip"

# 开发阶段默认只出绿色版；需要安装包时加 -Installer
if (-not $Installer) {
    Write-Host ''
    Write-Host '完成 ✅（本次只生成绿色版；需要安装包请加 -Installer）' -ForegroundColor Green
    Get-ChildItem 'dist' -Filter '*.zip' | ForEach-Object { "  绿色版: {0}  ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB) }
    exit 0
}

Write-Host "==> 3/3 生成安装包 (Inno Setup)" -ForegroundColor Cyan
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Warning '未找到 ISCC.exe（Inno Setup）。请先安装：winget install JRSoftware.InnoSetup'
    Write-Warning '绿色版已生成，安装包跳过。'
    exit 0
}

& $iscc "installer\setup.iss" "/DAppVersion=$Version"
if ($LASTEXITCODE -ne 0) { throw 'ISCC 编译失败' }

Write-Host ''
Write-Host '完成 ✅' -ForegroundColor Green
Get-ChildItem 'dist\installer' -Filter '*.exe' | ForEach-Object { "  安装包: {0}  ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB) }
Get-ChildItem 'dist' -Filter '*.zip' | ForEach-Object { "  绿色版: {0}  ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB) }
