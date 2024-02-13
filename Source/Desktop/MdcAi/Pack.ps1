$platforms = @('x64', 'x86', 'ARM64')
$msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"

foreach ($platform in $platforms) {
    & $msbuild `
      /p:Configuration=Release `
      /p:Platform=$platform `
      /p:UapAppxPackageBuildMode=StoreUpload `
      /p:AppxBundle=Always `
      /p:Packaged=True `
      /t:Publish `
      /p:PublishReadyToRun=False
}
