# Rebuilds ChatListUI.zip from the CRA production build output (RendernerApp/build)
# into the location the WinUI host serves from. Call after `npm run build`.
#
#   pwsh -File zip-build.ps1
#
# The zip keeps the build/ contents at its root - exactly the layout the
# WebResourceRequested interceptor expects (index.html at the archive root).

$ErrorActionPreference = 'Stop'

# RendererApp sits at <repo>/Source/React Chat Renderer/RendererApp, so 3 levels up.
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$buildDir = Join-Path $PSScriptRoot 'build'
$destZip = Join-Path $repoRoot 'Source\Desktop\MdcAi.ChatUI\Assets\ChatListUI.zip'

if (-not (Test-Path (Join-Path $buildDir 'index.html'))) {
    Write-Error "No production build found at '$buildDir'. Run 'npm run build' first."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem

if (Test-Path $destZip) {
    Remove-Item $destZip
}

$zip = [System.IO.Compression.ZipFile]::Open($destZip, 'Create')

try {
    Get-ChildItem $buildDir -Recurse -File | ForEach-Object {
        # Zip entries MUST use forward slashes ('/') - the C# host looks entries up by
        # URL path (static/js/main.js), and .NET's GetEntry does NOT normalize
        # Windows-style backslashes. Path.GetRelativePath returns '\' on Windows,
        # so normalize or every static asset 404s and the WebView shows a blank root.
        $relative = [System.IO.Path]::GetRelativePath($buildDir, $_.FullName).Replace('\', '/')
        $null = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $_.FullName, $relative, 'Optimal')
    }
}
finally {
    $zip.Dispose()
}

Write-Host "Wrote $destZip"
Get-ChildItem $destZip | Select-Object Name, Length