$root = 'C:\Users\drooijackers\Documents\Claude\TwitchDownloader'
$files = Get-ChildItem -Path $root -Include '*.cs','*.cshtml' -Recurse | Where-Object { $_.FullName -notlike '*\obj\*' }
Write-Output "Found $($files.Count) files to scan"
$count = 0
foreach ($f in $files) {
    $content = [System.IO.File]::ReadAllText($f.FullName)
    if ($content.Contains('TwitchDownloader')) {
        $newContent = $content.Replace('TwitchDownloader', 'TwitchKickDownloader')
        [System.IO.File]::WriteAllText($f.FullName, $newContent)
        Write-Output "Updated: $($f.Name)"
        $count++
    }
}
Write-Output "Done. Updated $count files."
