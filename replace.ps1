$extensions = @(".cs", ".xaml", ".csproj", ".md", ".json")
$files = Get-ChildItem -Path . -Recurse -File | Where-Object { $extensions -contains $_.Extension -and $_.FullName -notmatch '\\(bin|obj|\\.git)\\' }
foreach ($f in $files) {
    try {
        $c = [System.IO.File]::ReadAllText($f.FullName)
        $newC = $c.Replace('Optiscaler Manager', 'Optiscaler Client').Replace('OptiScaler Manager', 'OptiScaler Client').Replace('OptiscalerManager', 'OptiscalerClient').Replace('Optiscaler-Manager', 'Optiscaler-Client')

        if ($c -ne $newC) {
            [System.IO.File]::WriteAllText($f.FullName, $newC, [System.Text.Encoding]::UTF8)
            Write-Host "Updated $($f.Name)"
        }
    } catch {
        Write-Host "Failed processing $($f.Name): $_"
    }
}
if (Test-Path "OptiscalerManager.csproj") {
    Rename-Item -Path "OptiscalerManager.csproj" -NewName "OptiscalerClient.csproj"
    Write-Host "Renamed project file."
}
