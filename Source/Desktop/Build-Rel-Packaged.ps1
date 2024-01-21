#& "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe" /p:Configuration=Release /p:Platform=x64 /p:UapAppxPackageBuildMode=StoreUpload /p:AppxBundle=Always /p:AppxPackageDir=Packages\ /p:GenerateAppxPackageOnBuild=true /p:PublishReadyToRun=false /t:Publish

& "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe" /p:Configuration=Release /p:Platform=x64 /p:Packaged=True /p:AppxPackageDir=Packages\ /t:Publish
