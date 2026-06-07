# RimWorldAI Steam Workshop 一键推送
# 用法: .\scripts\publish.ps1 [-Mcp] [-Agent] [-Ui] [-All]
param([switch]$Mcp, [switch]$Agent, [switch]$Ui, [switch]$All)

if (-not $Mcp -and -not $Agent -and -not $Ui -and -not $All) { $Agent = $true }

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

Write-Host "===== 1. 构建 =====" -ForegroundColor Cyan
dotnet build "$root\RimWorldAI.sln" --configuration Release
if ($LASTEXITCODE -ne 0) { throw "构建失败" }

Write-Host "===== 2. Changelog (上次推送以来的提交) =====" -ForegroundColor Cyan
Push-Location $root
$shaFile = "$env:TEMP\workshop_last_sha.txt"
$lastSha = if (Test-Path $shaFile) { Get-Content $shaFile } else { "origin/main~1" }
$changes = git log "$lastSha..HEAD" --format="- %s" 2>$null
if (-not $changes) { $changes = git log -1 --format="%s" }
$changelog = ($changes -join "; ") -replace '"', ''
$currentSha = git rev-parse HEAD
Pop-Location
Write-Host $changelog

function Publish-Mod {
    param($name, $vdfTemplate)
    Write-Host "===== 推送 $name =====" -ForegroundColor Cyan
    $vdf = Get-Content "$root\scripts\$vdfTemplate" -Raw
    $vdf = $vdf -replace '"visibility" "0"', ('"visibility" "0"`n    "changenote" "' + $changelog + '"')
    $tmpVdf = "$env:TEMP\workshop_$name.vdf"
    Set-Content $tmpVdf $vdf
    steamcmd +login anonymous +workshop_build_item $tmpVdf +quit
}

if ($Mcp -or $All)   { Publish-Mod "mcp"     "workshop_mcp.vdf" }
if ($Agent -or $All) { Publish-Mod "agent"   "workshop_agent.vdf" }
if ($Ui -or $All)    { Publish-Mod "agentui" "workshop_agentui.vdf" }

Set-Content $shaFile $currentSha
Write-Host "===== 完成 =====" -ForegroundColor Green
