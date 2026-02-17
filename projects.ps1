# Discover all .csproj files (relative paths from repo root)
$csprojFiles = Get-ChildItem -Recurse -Filter *.csproj | ForEach-Object {
    $_.FullName.Substring((Get-Location).Path.Length + 1).Replace('\', '/')
}

# Load the .slnx file
$solutionPath = ".\AssettoServer.slnx"
[xml]$slnx = Get-Content $solutionPath

# Get projects currently in the solution (normalized to forward slashes)
$currentProjects = $slnx.Solution.Project | ForEach-Object {
    $_.Path.Replace('\', '/')
}

$modified = $false

foreach ($csproj in $csprojFiles) {
    if ($currentProjects -notcontains $csproj) {
        $newProject = $slnx.CreateElement("Project")
        $newProject.SetAttribute("Path", $csproj.Replace('/', '\'))
        $newProject.SetAttribute("Type", "Classic C#")
        $slnx.Solution.AppendChild($newProject) > $null
        $modified = $true
        Write-Host "Added $csproj to solution"
    }
}

if ($modified) {
    $slnx.Save((Resolve-Path $solutionPath).Path)
    Write-Host "Solution updated."
} else {
    Write-Host "Solution is up to date."
}
