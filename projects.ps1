# Discover all .csproj files
$csprojFiles = Get-ChildItem -Recurse -Filter *.csproj

# Track the projects currently in the solution
$solutionPath = ".\AssettoServer.sln"
$currentSolutionProjects = dotnet sln $solutionPath list

# Remove unused projects from the solution
foreach ($project in $currentSolutionProjects) {
    if (-not (dotnet list $project reference)) {
        dotnet sln $solutionPath remove $project
    }
}

# Ensure all remaining projects have a GUID and are added to the solution
foreach ($file in $csprojFiles) {
    # Load the csproj file
    [xml]$csprojContent = Get-Content $file.FullName
    
    # Check if a GUID exists
    $projectGuid = $csprojContent.Project.PropertyGroup.ProjectGuid
    if ($projectGuid -eq $null) {
        # Generate a new GUID
        $guid = [Guid]::NewGuid().ToString().ToUpper()
        $newGuidElement = $csprojContent.CreateElement("ProjectGuid")
        $newGuidElement.InnerText = "{$guid}"
        
        # Insert the GUID into the csproj
        $csprojContent.Project.PropertyGroup.AppendChild($newGuidElement) > $null
        
        # Save the csproj file
        $csprojContent.Save($file.FullName)
    }

    # Check if the project is already in the solution
    $isInSolution = $currentSolutionProjects -contains $file.FullName

    # Add the project to the solution if it's not already there
    if (-not $isInSolution) {
        dotnet sln $solutionPath add $file.FullName
    }
}