$platforms = @('x64', 'x86', 'ARM64')
$name = "MdcAi"
$msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"

[xml]$manifest = Get-Content -Path '.\Package.appxmanifest'
$namespaceManager = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
$namespaceManager.AddNamespace("def", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
$identityNode = $manifest.SelectSingleNode('//def:Identity', $namespaceManager)
$version = $identityNode.Version

[xml]$csprojContent = Get-Content -Path ".\$name.csproj"
$targetFramework = "$($csprojContent.Project.PropertyGroup.TargetFramework)" -replace '\s', ''

dotnet restore

foreach ($platform in $platforms) {
  & $msbuild `
    /p:Configuration=Release `
    /p:Platform=$platform `
    /p:UapAppxPackageBuildMode=StoreUpload `
    /p:AppxBundle=Always `
    /p:Packaged=True `
    /t:Publish `
    /p:PublishReadyToRun=False
  
  $packagePath = ".\bin\$platform\Release\$targetFramework\win10-$platform\AppPackages\$($name)_$($version)_Test"
  
  # Rename msixsym to appxsym because ms store
  Get-ChildItem "$packagePath\*.msixsym" | Rename-Item -NewName { $_.Name -replace '.msixsym', '.appxsym' }

  # Delete the old package
  Get-ChildItem "$packagePath\*.appxupload" | Remove-Item -Force

  # Compress the package
  $zipFiles = Get-ChildItem -Path "$packagePath\*" -Include "*.appxsym", "*.msix"
  Compress-Archive -Path $zipFiles.FullName -DestinationPath "$packagePath\$($name)_$($version)_$($platform).zip"

  # Rename zip to appxupload because ms store
  Get-ChildItem "$packagePath\*.zip" | Rename-Item -NewName { $_.Name -replace '.zip', '.appxupload' }
}
