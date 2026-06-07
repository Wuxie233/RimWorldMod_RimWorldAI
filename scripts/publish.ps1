# RimWorldAI Steam Workshop 一键推送
# 用法:
#   .\scripts\publish.ps1 -Check         检查哪些 mod 有变更待推送
#   .\scripts\publish.ps1 -Push [-Mcp] [-Agent] [-Ui] [-All] [-Force]
#     -Force: 强制推送（跳过内容校验）

param([switch]$Check, [switch]$Push, [switch]$Mcp, [switch]$Agent, [switch]$Ui, [switch]$All, [switch]$Force)

if (-not $Check -and -not $Push) {
    Write-Host "Usage: .\scripts\publish.ps1 -Check  (检查)"
    Write-Host "       .\scripts\publish.ps1 -Push [-Mcp] [-Agent] [-Ui] [-All] (推送)"
    exit 1
}

if ($Push -and -not $Mcp -and -not $Agent -and -not $Ui -and -not $All) { $Agent = $true }

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$stateFile = "$PSScriptRoot\publish_state.json"
$changelogFile = "$PSScriptRoot\CHANGELOG.md"

function Get-PublishHash($dir) {
    if (-not (Test-Path $dir)) { return "" }
    $files = Get-ChildItem $dir -Recurse -File | Sort-Object FullName
    $hash = [string]::Join("", ($files | ForEach-Object { "$($_.FullName.Replace($dir,''))|$($_.Length)" }))
    return [System.BitConverter]::ToString([System.Security.Cryptography.SHA256]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes($hash))).Replace("-","")
}

function Get-Changelog {
    if (-not (Test-Path $changelogFile)) { return "" }
    $text = Get-Content $changelogFile -Raw
    $m = [regex]::Match($text, '##\s*\[([\d-]+)\]\s*-\s*(.+?)(?=\n##\s*\[|\z)', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if ($m.Success) { return @{ Date = $m.Groups[1].Value; Text = ($m.Groups[2].Value -replace '\n\s*\n', '' -replace '\n\s*-?\s*', '; ').Trim() } }
    return $null
}

$state = if (Test-Path $stateFile) { Get-Content $stateFile -Raw | ConvertFrom-Json } else { @{} }

$mods = @(
    @{ Name = "RimWorldMCP";    Key = "rimworld_mcp";    Vdf = "workshop_mcp.vdf";    Dir = "$root\publish\RimWorldMCP" }
    @{ Name = "RimWorldAgent";  Key = "rimworld_agent";  Vdf = "workshop_agent.vdf";  Dir = "$root\publish\RimWorldAgent" }
    @{ Name = "RimWorldAgentUI"; Key = "rimworld_agentui"; Vdf = "workshop_agentui.vdf"; Dir = "$root\publish\RimWorldAgentUI" }
)

# ===== Check 模式 =====
if ($Check) {
    $cl = Get-Changelog
    if (-not $cl) {
        Write-Host "CHANGELOG: (空)" -ForegroundColor Red
    } else {
        Write-Host "CHANGELOG [$($cl.Date)]: $($cl.Text.Substring(0, [Math]::Min(80, $cl.Text.Length)))" -ForegroundColor Cyan
    }
    Write-Host ""
    Write-Host "========================================"
    Write-Host ("{0,-18} {1,12} {2,10} {3,40}" -f "Mod", "Version", "变化?", "发布目录")
    Write-Host "========================================"
    foreach ($m in $mods) {
        $hash = Get-PublishHash $m.Dir
        $lastHash = $state.($m.Key).last_sha
        $ver = [int]$state.($m.Key).version
        $changed = ($hash -ne $lastHash) -or (-not $lastHash)
        $color = if ($changed) { "Yellow" } else { "Green" }
        $label = if ($changed) { "有变更" } else { "无变更" }
        Write-Host ("{0,-18} {1,10}v{2} {3,10} {4,40}" -f $m.Name, "", $ver, $label, $m.Dir) -ForegroundColor $color
    }
    Write-Host "========================================"
    $pending = @($mods | Where-Object { $hash = Get-PublishHash $_.Dir; $hash -ne $state.($_.Key).last_sha })
    if ($pending.Count -gt 0) {
        Write-Host "`n待推送: $($pending.Name -join ', ')" -ForegroundColor Yellow
        Write-Host "运行: .\scripts\publish.ps1 -Push 开始推送" -ForegroundColor Yellow
    } else {
        Write-Host "`n所有 mod 均无变更" -ForegroundColor Green
    }
    exit 0
}

# ===== Push 模式 =====
Write-Host "===== 1. 构建全部项目 (Release) =====" -ForegroundColor Cyan
dotnet build "$root\RimWorldAI.sln" --configuration Release
if ($LASTEXITCODE -ne 0) { throw "构建失败" }

$cl = Get-Changelog
$changelog = if ($cl) { $cl.Text } else { "日常更新" }
Write-Host "===== 2. Changelog =====" -ForegroundColor Cyan
Write-Host $changelog

function Publish-Mod($m) {
    $hash = Get-PublishHash $m.Dir
    if (-not $Force -and $state.($m.Key) -and $state.($m.Key).last_sha -eq $hash) {
        Write-Host "===== 跳过 $($m.Name) (内容未变化, 用 -Force 强制) =====" -ForegroundColor Yellow
        return
    }

    Write-Host "===== 推送 $($m.Name) =====" -ForegroundColor Cyan
    $vdf = Get-Content "$PSScriptRoot\$($m.Vdf)" -Raw
    $cl = $changelog -replace '"', '`"'
    $vdf = $vdf -replace '"visibility" "0"', ('"visibility" "0"`n    "changenote" "' + $cl + '"')
    $tmpVdf = "$env:TEMP\workshop_$($m.Key).vdf"
    Set-Content $tmpVdf $vdf

    steamcmd +login anonymous +workshop_build_item $tmpVdf +quit
    if ($LASTEXITCODE -eq 0) {
        if (-not $state.($m.Key)) { $state | Add-Member -NotePropertyName $($m.Key) -NotePropertyValue (@{}) -Force }
        $state.($m.Key).last_sha = $hash
        $state.($m.Key).version = ([int]$state.($m.Key).version) + 1
        $state | ConvertTo-Json | Set-Content $stateFile
        Write-Host "  已推送 v$($state.($m.Key).version)" -ForegroundColor Green
    } else {
        Write-Host "  推送失败!" -ForegroundColor Red
    }
}

foreach ($m in $mods) {
    if ($m.Name -eq "RimWorldMCP"    -and ($Mcp -or $All)) { Publish-Mod $m }
    if ($m.Name -eq "RimWorldAgent"  -and ($Agent -or $All)) { Publish-Mod $m }
    if ($m.Name -eq "RimWorldAgentUI" -and ($Ui -or $All)) { Publish-Mod $m }
}

Write-Host "===== 完成 =====" -ForegroundColor Green
