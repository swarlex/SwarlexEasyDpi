# SPDX-License-Identifier: GPL-3.0-or-later
<#
.SYNOPSIS
  One command from local change to GitHub: every installed copy then offers the update.
  (Adapted from SwarlexBattery's tools/release.ps1.)

  1. raises VERSION (patch by default, or -Version X.Y.Z)
  2. builds and tests dist\SwarlexEasyDpi.exe + .sha256
  3. commits every change on a branch vX.Y.Z, opens a pull request and merges it into main
  4. creates the GitHub release vX.Y.Z with the exe and its checksum (GitHub CLI "gh", signed in);
     the release text is this version's section of docs/CHANGELOG.md, or -Notes

.EXAMPLE
  .\tools\release.ps1 -Notes "Faster connection"            # 1.0.0 -> 1.0.1
  .\tools\release.ps1 -Version 1.1.0 -Notes "New settings"
#>
param(
    [string]$Version,
    [Parameter(Mandatory)] [string]$Notes,
    [string]$Repo = 'swarlex/SwarlexEasyDpi'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh -and (Test-Path "$env:ProgramFiles\GitHub CLI\gh.exe")) { $gh = "$env:ProgramFiles\GitHub CLI\gh.exe" }
if (-not $gh) { throw 'GitHub CLI (gh) not found. Install it with: winget install GitHub.cli, then: gh auth login' }
& $gh auth status *> $null
if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI is not signed in: gh auth login --web' }

# yalniz main dalindan yayinla
$cur_branch = (git rev-parse --abbrev-ref HEAD).Trim()
if ($cur_branch -ne 'main') { throw "Releases are made from main (now: $cur_branch)" }
git status --short | Out-Host   # yayina girecek degisiklikler
# izlenmeyen dosya varsa dur: git add -A onlari da yayinlardi
$untracked = @(git ls-files --others --exclude-standard)
if ($untracked.Count -gt 0) { throw "Untracked files would be published; add them to git or .gitignore first:`n  $($untracked -join "`n  ")" }

# 1. version
$vf = Join-Path $root 'VERSION'
$cur = [version](Get-Content -LiteralPath $vf -Raw).Trim()
if (-not $Version) { $Version = "$($cur.Major).$($cur.Minor).$($cur.Build + 1)" }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must be X.Y.Z: $Version" }
if ([version]$Version -le $cur) { throw "The new version ($Version) must be greater than the current one ($cur)" }
Set-Content -LiteralPath $vf -Value $Version -Encoding ASCII

# 2. build + tests (a failure stops here: nothing is pushed or published)
try { & (Join-Path $root 'build.ps1') -Test }
catch { Set-Content -LiteralPath $vf -Value "$cur" -Encoding ASCII; throw "Build failed, nothing was published: $_" }
$exe = Join-Path $root 'dist\SwarlexEasyDpi.exe'
$sha = "$exe.sha256"
$lic = Join-Path $root 'dist\LICENSES.txt'

# 3. commit on a release branch, pull request, merge into main
$msg = "v$Version`: $Notes"
$ErrorActionPreference = 'Continue'   # git writes harmless notes to stderr; only exit codes count
$branch = "v$Version"
git checkout -q -B $branch
git add -A
git commit -q -m $msg
if ($LASTEXITCODE -ne 0) { git checkout -q main; throw 'git commit failed' }
git push -q -u origin $branch
if ($LASTEXITCODE -ne 0) { git checkout -q main; throw 'git push failed' }
& $gh pr create --repo $Repo --base main --head $branch --title "Release v$Version" --body $Notes
if ($LASTEXITCODE -ne 0) { git checkout -q main; throw 'gh pr create failed' }
& $gh pr merge $branch --repo $Repo --merge --delete-branch
if ($LASTEXITCODE -ne 0) { git checkout -q main; throw 'gh pr merge failed (the pull request is open; merge it on GitHub)' }
git checkout -q main
git pull -q --ff-only origin main
if ($LASTEXITCODE -ne 0) { throw 'git pull failed (main could not be fast-forwarded)' }
git branch -q -D $branch 2>$null

# 4. GitHub release with this version's CHANGELOG section
$body = $Notes
$log = Join-Path $root 'docs\CHANGELOG.md'
if (Test-Path -LiteralPath $log) {
    $text = Get-Content -LiteralPath $log -Raw
    $m = [regex]::Match($text, "(?ms)^## $([regex]::Escape($Version))\s*\r?\n(.*?)(?=^## |\z)")
    if ($m.Success -and $m.Groups[1].Value.Trim()) { $body = $m.Groups[1].Value.Trim() }
}
$notesFile = Join-Path ([IO.Path]::GetTempPath()) "swarlexeasydpi-notes-$Version.md"
[IO.File]::WriteAllText($notesFile, $body, (New-Object Text.UTF8Encoding $false))   # BOM'suz: ilk baslik bozulmasin
& $gh release create "v$Version" $exe $sha $lic --repo $Repo --title "v$Version" --notes-file $notesFile --target main
$rc = $LASTEXITCODE
Remove-Item -LiteralPath $notesFile -Force -ErrorAction SilentlyContinue
if ($rc -ne 0) { throw 'gh release create failed' }
"Released v${Version}: https://github.com/$Repo/releases/tag/v$Version"
