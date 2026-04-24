# Bumps the patch version, tags, and pushes to trigger the GitHub Actions release.

$latest = git tag --list "v*" --sort=-version:refname | Select-Object -First 1

if (-not $latest) {
    Write-Error "No existing version tags found."
    exit 1
}

if ($latest -notmatch '^v(\d+)\.(\d+)\.(\d+)$') {
    Write-Error "Latest tag '$latest' is not in expected vX.Y.Z format."
    exit 1
}

$major = [int]$Matches[1]
$minor = [int]$Matches[2]
$patch = [int]$Matches[3] + 1
$next  = "v$major.$minor.$patch"

Write-Host "Latest release : $latest"
Write-Host "Next release   : $next"
Write-Host ""

$confirm = Read-Host "Push $next to GitHub? (y/N)"
if ($confirm -ne 'y') {
    Write-Host "Cancelled."
    exit 0
}

git tag $next
if ($LASTEXITCODE -ne 0) { Write-Error "git tag failed."; exit 1 }

git push origin $next
if ($LASTEXITCODE -ne 0) { Write-Error "git push failed."; exit 1 }

Write-Host ""
Write-Host "Tag $next pushed. GitHub Actions is now building the release."
Write-Host "https://github.com/FattyFatty001/ScreenTracker/actions"
