$ErrorActionPreference = "Stop"

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$wpf = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF"
$outExe = Join-Path $PSScriptRoot "RgbBrightnessOSD.exe"
$src = Join-Path $PSScriptRoot "Program.cs"

& $csc `
    /nologo `
    /target:winexe `
    /optimize+ `
    /out:"$outExe" `
    /reference:"$wpf\PresentationFramework.dll" `
    /reference:"$wpf\PresentationCore.dll" `
    /reference:"$wpf\WindowsBase.dll" `
    /reference:"System.Xaml.dll" `
    /reference:"System.Windows.Forms.dll" `
    /reference:"System.Drawing.dll" `
    "$src"

if ($LASTEXITCODE -ne 0) {
    throw "csc.exe failed with exit code $LASTEXITCODE"
}

Write-Host "Built: $outExe"
