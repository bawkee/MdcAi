#msbuild MdcAi.csproj /p:Configuration=Release /p:UapAppxPackageBuildMode=StoreUpload /p:AppxBundle=Always /p:Platform=x64
#dotnet publish MdcAi.csproj -c Release -p:UapAppxPackageBuildMode=StoreUpload -p:AppxBundle=Always -r win-x64
#dotnet publish --configuration Release /p:Platform="x64"

# Commands from the winui gallery example
#/p:Configuration=Release /p:Platform=x64 /p:UapAppxPackageBuildMode=StoreUpload /p:AppxBundle=Always /p:AppxPackageDir=Packages\ /p:GenerateAppxPackageOnBuild=true /p:PublishReadyToRun=false

# The command that produces the msix (but no pdb files)
# dotnet publish MdcAi.sln --configuration Release /p:Platform="x64" -p:UapAppxPackageBuildMode=StoreUpload -p:AppxBundle=Always

# This does not work at all because of the error mentioned in line 1 in this file 
#dotnet publish /p:Configuration=Release /p:Platform=x64 /p:UapAppxPackageBuildMode=StoreUpload /p:AppxBundle=Always /p:AppxPackageDir=Packages\ /p:GenerateAppxPackageOnBuild=true /p:PublishReadyToRun=false

#dotnet publish MdcAi.sln /p:Configuration=Release /p:Platform=x64 /p:UapAppxPackageBuildMode=StoreUpload /p:AppxBundle=Always /p:PublishReadyToRun=false

# the msbuild that everyone uses but doesnt produce the msix no matter what (only works if main project has EnableMsixTooling)
& "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe" /p:Configuration=Release /p:Platform=x64 /p:UapAppxPackageBuildMode=StoreUpload /p:AppxBundle=Always /p:AppxPackageDir=Packages\ /p:GenerateAppxPackageOnBuild=true /p:PublishReadyToRun=false /t:Publish

# This produces the msix but not pdb files, all winui projects must have EnableMsixTooling
#dotnet publish /p:Configuration=Release /p:Platform=x64 /p:UapAppxPackageBuildMode=StoreUpload /p:AppxBundle=Always /p:AppxPackageDir=Packages\ /p:PublishReadyToRun=false

