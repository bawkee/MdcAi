# Builds the x64 Release packaged (MSIX) variant, Publish target — the "Store-ready" build.
#
# Store-upload variant (note: this one builds the final .appxupload; slowest path):
#   & $msbuild /m /p:Configuration=Release /p:Platform=x64 /p:UapAppxPackageBuildMode=StoreUpload /p:AppxBundle=Always /p:AppxPackageDir=Packages\ /p:GenerateAppxPackageOnBuild=true /p:PublishReadyToRun=false /t:Publish
#
# Regular packaged build:
# NOTE: MSBuild CLI defaults to a SINGLE job (-m:1) unless /m is passed — without it the
# solution's projects build one at a time and most of your cores sit idle. /m = use all cores.
$vstudio = "Community" # IMPORTANT: If you use VS Professional put 'Professional' here
$msbuild = "C:\Program Files\Microsoft Visual Studio\2022\${vstudio}\MSBuild\Current\Bin\MSBuild.exe"

& $msbuild /m /p:Configuration=Release /p:Platform=x64 /p:Packaged=True /p:AppxPackageDir=Packages\ /t:Publish