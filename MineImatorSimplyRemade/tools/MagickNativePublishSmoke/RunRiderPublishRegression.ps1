$ErrorActionPreference = 'Stop'

$applicationProject = Join-Path $PSScriptRoot '..\..\MineImatorSimplyRemade.csproj'
$smokeProject = Join-Path $PSScriptRoot 'MagickNativePublishSmoke.csproj'
$publishDirectory = Join-Path $PSScriptRoot 'bin\RiderPublishRegression'

$aotPropertyOutput = & dotnet msbuild $applicationProject '-getProperty:PublishAot,PublishSingleFile' '-p:PublishAot=true' '-p:PublishSingleFile=true'
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
if (($aotPropertyOutput | ConvertFrom-Json).Properties.PublishSingleFile -ne 'false') {
    throw 'PublishSingleFile must be disabled when PublishAot is enabled.'
}

$managedPropertyOutput = & dotnet msbuild $applicationProject '-getProperty:PublishAot,PublishSingleFile' '-p:PublishAot=false' '-p:PublishSingleFile=true'
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
if (($managedPropertyOutput | ConvertFrom-Json).Properties.PublishSingleFile -ne 'true') {
    throw 'PublishSingleFile must remain enabled for non-AOT publishing when requested.'
}

& dotnet publish $smokeProject '--configuration' 'Release' '--runtime' 'win-x64' '--output' $publishDirectory '--nologo' '-p:PublishAot=true' '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:IncludeAllContentForSelfExtract=true' '-p:PublishTrimmed=true' '-p:SelfContained=true'
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& (Join-Path $publishDirectory 'MagickNativePublishSmoke.exe')
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}