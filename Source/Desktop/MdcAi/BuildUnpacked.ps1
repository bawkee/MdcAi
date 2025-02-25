$platforms = @('x64', 'x86', 'ARM64')
$name = "MdcAi"
$vstudio = "Community" # IMPORTANT: If you use VS Professional put 'Professional' here
$msbuild = "C:\Program Files\Microsoft Visual Studio\2022\${vstudio}\MSBuild\Current\Bin\MSBuild.exe"

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
    /p:Platform=$platform `
    /p:Configuration=Release-Unpackaged `
    /t:Publish `
    /p:PublishReadyToRun=False  

  $packagePath = ".\bin\$platform\Release-Unpackaged\$targetFramework\win10-$platform"
  
  # Compress the package
  $zipFiles = Get-ChildItem -Path "$packagePath\*" -Include "*"
  Compress-Archive -Path $zipFiles.FullName -DestinationPath ".\bin\$($name)_$($version)_$($platform).zip"
}
