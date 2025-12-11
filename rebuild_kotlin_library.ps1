# PowerShell script to rebuild the Kotlin camera library and copy it to Unity

Write-Host "=== Rebuilding QuestCameraLib ===" -ForegroundColor Cyan

# Check if we're in the right directory
if (-not (Test-Path "QuestCameraLib")) {
    Write-Host "Error: QuestCameraLib directory not found!" -ForegroundColor Red
    Write-Host "Make sure you run this script from the project root directory." -ForegroundColor Yellow
    exit 1
}

# Step 1: Build the Kotlin library
Write-Host "`n[1/3] Building Kotlin library..." -ForegroundColor Green
Push-Location QuestCameraLib
try {
    if ($IsWindows -or $env:OS -eq "Windows_NT") {
        & .\gradlew.bat assembleRelease
    } else {
        & ./gradlew assembleRelease
    }
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: Gradle build failed!" -ForegroundColor Red
        Pop-Location
        exit 1
    }
} finally {
    Pop-Location
}

# Step 2: Check if the .aar was created
$aarPath = "QuestCameraLib\app\build\outputs\aar\app-release.aar"
if (-not (Test-Path $aarPath)) {
    Write-Host "Error: .aar file not found at $aarPath" -ForegroundColor Red
    exit 1
}

Write-Host "`n[2/3] .aar file built successfully!" -ForegroundColor Green
Write-Host "Location: $aarPath" -ForegroundColor Gray

# Step 3: Backup old .aar and copy new one
$unityPluginPath = "Assets\Plugins\Android\questcameralib.aar"
$backupPath = "Assets\Plugins\Android\questcameralib.aar.backup"

if (Test-Path $unityPluginPath) {
    Write-Host "`n[3/3] Backing up old .aar file..." -ForegroundColor Green
    Copy-Item $unityPluginPath $backupPath -Force
    Write-Host "Backup saved to: $backupPath" -ForegroundColor Gray
}

Write-Host "Copying new .aar to Unity plugins..." -ForegroundColor Green
Copy-Item $aarPath $unityPluginPath -Force
Write-Host "New .aar copied to: $unityPluginPath" -ForegroundColor Gray

Write-Host "`n=== SUCCESS ===" -ForegroundColor Green
Write-Host "Kotlin library rebuilt and copied to Unity!" -ForegroundColor Cyan
Write-Host "`nNext steps:" -ForegroundColor Yellow
Write-Host "  1. Open Unity and wait for it to reimport the .aar file" -ForegroundColor White
Write-Host "  2. Build your Android APK (File > Build Settings > Build)" -ForegroundColor White
Write-Host "  3. Install on Quest: adb install -r Build\QuestRealityCapture.apk" -ForegroundColor White
Write-Host "`nOr use Unity's 'Build and Run' to do steps 2-3 automatically." -ForegroundColor Gray




