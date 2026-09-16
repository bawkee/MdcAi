# Builds the x64 Release (unpackaged) variant, Publish target — the "dev/CI" packaging.
# NOTE: MSBuild CLI defaults to a SINGLE job (-m:1) unless /m is passed — without it the
# solution's projects build one at a time and most of your cores sit idle. /m = use all cores.
$vstudio = "Community" # IMPORTANT: If you use VS Professional put 'Professional' here
$msbuild = "C:\Program Files\Microsoft Visual Studio\2022\${vstudio}\MSBuild\Current\Bin\MSBuild.exe"

& $msbuild /m /p:Configuration=Release /p:Platform=x64 /p:Packaged=False /t:Publish