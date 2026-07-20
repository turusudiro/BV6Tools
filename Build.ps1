$inputVersion = Read-Host "Enter version (eg 1.0.0) (default: 1.0.0)"
if ([string]::IsNullOrWhiteSpace($inputVersion)) {
	$Version = "1.0.0"
} else {
	$Version = $inputVersion
}

Write-Host "Building version $Version..." -ForegroundColor Cyan

$ProjectPath = "BV6Tools/BV6Tools.csproj"
$OutputDir   = "./publish"
$ZipName     = "./publish/BV6Tools-Release.zip"

if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
if (Test-Path $ZipName)   { Remove-Item $ZipName -Force }

dotnet restore $ProjectPath

dotnet publish $ProjectPath `
    --configuration Release `
    --runtime win-x64 `
    --framework net10.0-windows10.0.22621.0 `
    -p:PublishSingleFile=true `
    -p:SelfContained=true `
    -p:Version=$Version `
    -p:AssemblyVersion=$Version `
    -p:FileVersion=$Version `
    -o "$OutputDir/BV6Tools"

if ($LASTEXITCODE -ne 0) { exit 1 }

Get-ChildItem "$OutputDir/BV6Tools" -Include *.pdb, *.config, *.xml -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

Compress-Archive -Path "$OutputDir/BV6Tools" -DestinationPath $ZipName -Force
Remove-Item "$OutputDir/BV6Tools" -Recurse -Force