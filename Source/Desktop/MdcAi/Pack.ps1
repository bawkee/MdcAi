#msbuild MdcAi.csproj /p:Configuration=Release /p:UapAppxPackageBuildMode=StoreUpload /p:AppxBundle=Always /p:Platform=x64
#dotnet publish MdcAi.csproj -c Release -p:UapAppxPackageBuildMode=StoreUpload -p:AppxBundle=Always -r win-x64
#dotnet publish --configuration Release /p:Platform="x64"

dotnet publish --configuration Release /p:Platform="x64" -p:UapAppxPackageBuildMode=StoreUpload -p:AppxBundle=Always
