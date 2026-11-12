# Builds FlatClip.exe with the Roslyn compiler against .NET Framework 4.8 (built into Windows 10/11).
# No .NET SDK needed; only a csc.exe from Visual Studio / Build Tools.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$csc = $null
if (Test-Path $vswhere) {
    $vs = & $vswhere -latest -products * -property installationPath
    if ($vs) { $csc = Join-Path $vs 'MSBuild\Current\Bin\Roslyn\csc.exe' }
}
if (-not $csc -or -not (Test-Path $csc)) { throw 'csc.exe (Roslyn) niet gevonden. Installeer Visual Studio Build Tools.' }

$fw  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wpf = "$fw\WPF"
New-Item -ItemType Directory -Force obj, bin | Out-Null

# 1. icon
& $csc /nologo /noconfig /target:exe /out:obj\MakeIcon.exe /r:"$fw\System.dll" /r:"$fw\System.Drawing.dll" tools\MakeIcon.cs
if ($LASTEXITCODE) { throw 'MakeIcon build failed' }
& .\obj\MakeIcon.exe obj\icon.ico

# 2. app
$refs = @(
    "$fw\System.dll", "$fw\System.Core.dll", "$fw\System.Xml.dll", "$fw\System.Xaml.dll",
    "$fw\System.Drawing.dll", "$fw\System.Windows.Forms.dll",
    "$fw\System.Runtime.Serialization.dll", "$fw\System.Security.dll",
    "$wpf\WindowsBase.dll", "$wpf\PresentationCore.dll", "$wpf\PresentationFramework.dll"
) | ForEach-Object { "/r:$_" }

$resources = @(
    '/resource:src\Theme.xaml,FlatClip.Theme.xaml',
    '/resource:src\Popup.xaml,FlatClip.Popup.xaml',
    '/resource:src\Settings.xaml,FlatClip.Settings.xaml',
    '/resource:src\SnippetEditor.xaml,FlatClip.SnippetEditor.xaml',
    '/resource:obj\icon.ico,FlatClip.icon.ico'
)

& $csc /nologo /noconfig /target:winexe /platform:anycpu /optimize+ /langversion:9.0 /nowarn:1701,1702 `
    /out:bin\FlatClip.exe /win32icon:obj\icon.ico /win32manifest:src\app.manifest @refs @resources (Get-ChildItem src\*.cs).FullName
if ($LASTEXITCODE) { throw 'Build failed' }
Write-Host "OK -> $PSScriptRoot\bin\FlatClip.exe"
