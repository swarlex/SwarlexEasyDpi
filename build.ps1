#Requires -Version 5.1
<#
  SwarlexEasyDpi derleme betigi (Windows, kurulum gerektirmez).

  - GoodbyeDPI (native\goodbyedpi) C kaynagindan llvm-mingw ile derlenir. (native\byedpi
    Android surumu icindir; Windows programi kullanmaz.)
  - WinDivert surucusu imzali olmak zorunda oldugu icin derlenemez: resmi GoodbyeDPI
    0.2.3rc3 surumundeki imzali WinDivert.dll / WinDivert64.sys kullanilir.
  - Arayuz, Windows'ta hazir gelen .NET Framework derleyicisi (csc.exe) ile derlenir;
    ikililer tek exe'nin icine gomulur.

  Kullanim:  powershell -ExecutionPolicy Bypass -File build.ps1 [-Test]
  Cikti:     dist\SwarlexEasyDpi.exe
#>
param([switch]$Test)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$Kok   = $PSScriptRoot
$Arac  = Join-Path $Kok '.tools'
$Bld   = Join-Path $Kok 'build'
$Dist  = Join-Path $Kok 'dist'
$Csc   = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

# Indirilen her sey SHA256 ile sabitli.
$Indir = @{
    llvm = @{ Url = 'https://github.com/mstorsjo/llvm-mingw/releases/download/20260908/llvm-mingw-20260908-ucrt-x86_64.zip'
              Sha = '1BCF74D06B724AEECAA6412CA85F5B26FB1DA770E7CDCEFA9263C9C5C3AD34B6'; Klasor = 'llvm-mingw-20260908-ucrt-x86_64' }
    windivert = @{ Url = 'https://github.com/basil00/WinDivert/releases/download/v2.2.2/WinDivert-2.2.2-A.zip'
              Sha = '63CB41763BB4B20F600B6DE04E991A9C2BE73279E317D4D82F237B150C5F3F15'; Klasor = 'WinDivert-2.2.2-A' }
    goodbyedpi = @{ Url = 'https://github.com/ValdikSS/GoodbyeDPI/releases/download/0.2.3rc3/goodbyedpi-0.2.3rc3-2.zip'
              Sha = '37F96B32D050DADCC930A639EBA68E1CCD57ED5C04A5F77DFCA908F01905A4C5'; Klasor = 'goodbyedpi-0.2.3rc3-2' }
}
# GoodbyeDPI surumundeki imzali WinDivert (Pudochu/cagritaskn ile ayni dosyalar).
$WinDivertDosya = @{
    'WinDivert.dll'   = '6110BFA44667405179C3E15E12AF1B62037E447ED59B054B19042032995E6C7E'
    'WinDivert64.sys' = 'E69B5BA3F0CD6CFB2983E442636E7F0B342B61B15264B0328317D4559C82CF50'
}

function Adim([string]$m) { Write-Host "==> $m" -ForegroundColor Cyan }

function Hash([string]$yol) { (Get-FileHash -LiteralPath $yol -Algorithm SHA256).Hash }

function Calistir([string]$exe, [string[]]$argv) {
    # Derleyici uyarilari stderr'e yazar; PS 5.1'de cikti yonlendirilirse (CI, 2>&1)
    # 'Stop' bunlari hataya cevirir. Basari yalniz cikis koduna bakilarak belirlenir.
    $eski = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { & $exe @argv 2>&1 | ForEach-Object { "$_" } } finally { $ErrorActionPreference = $eski }
    if ($LASTEXITCODE -ne 0) { throw "$([IO.Path]::GetFileName($exe)) hata kodu $LASTEXITCODE" }
}

function Getir([string]$ad) {
    $p = $Indir[$ad]
    $hedef = Join-Path $Arac $p.Klasor
    if (Test-Path -LiteralPath $hedef) { return $hedef }
    New-Item -ItemType Directory -Force -Path $Arac | Out-Null
    $zip = Join-Path $Arac "$ad.zip"
    if (-not (Test-Path -LiteralPath $zip) -or (Hash $zip) -ne $p.Sha) {
        Adim "Indiriliyor: $($p.Url)"
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $eski = $ProgressPreference; $ProgressPreference = 'SilentlyContinue'
        try { Invoke-WebRequest -Uri $p.Url -OutFile $zip -UseBasicParsing } finally { $ProgressPreference = $eski }
    }
    if ((Hash $zip) -ne $p.Sha) { Remove-Item -LiteralPath $zip -Force; throw "$ad SHA256 uyusmuyor" }
    # Gecici klasore cikarip tek adimda tasi: yarim kalan cikarma "hazir" sanilmasin.
    $gecici = Join-Path $Arac "tmp-$ad"
    if (Test-Path -LiteralPath $gecici) { Remove-Item -LiteralPath $gecici -Recurse -Force }
    Expand-Archive -LiteralPath $zip -DestinationPath $gecici -Force
    Move-Item -LiteralPath (Join-Path $gecici $p.Klasor) -Destination $hedef
    Remove-Item -LiteralPath $gecici -Recurse -Force
    return $hedef
}

$llvm = Join-Path (Getir 'llvm') 'bin'
$wd   = Getir 'windivert'
$gd   = Getir 'goodbyedpi'
$cc   = Join-Path $llvm 'x86_64-w64-mingw32-clang.exe'
$rc   = Join-Path $llvm 'x86_64-w64-mingw32-windres.exe'

foreach ($d in 'gdpi', 'app') { New-Item -ItemType Directory -Force -Path (Join-Path $Bld $d) | Out-Null }
New-Item -ItemType Directory -Force -Path $Dist | Out-Null

Adim 'WinDivert (imzali, GoodbyeDPI surumunden)'
foreach ($ad in $WinDivertDosya.Keys) {
    $src = Join-Path $gd "x86_64\$ad"
    $dst = Join-Path $Bld "gdpi\$ad"
    Copy-Item -LiteralPath $src -Destination $dst -Force
    if ((Hash $dst) -ne $WinDivertDosya[$ad]) { throw "$ad hash uyusmuyor" }
}

$sertlik = @('-O2', '-D_FORTIFY_SOURCE=2', '-fstack-protector', '-Wl,--dynamicbase,--nxcompat,--high-entropy-va', '-s')

Adim 'GoodbyeDPI derleniyor (native\goodbyedpi)'
$gsrc = Join-Path $Kok 'native\goodbyedpi'
$rcObj = Join-Path $Bld 'gdpi\rc.o'
Push-Location $gsrc
try {
    Calistir $rc @('goodbyedpi-rc.rc', '-o', $rcObj)
    $cdosya = @(Get-ChildItem -Filter *.c | ForEach-Object { $_.Name }) + @(Get-ChildItem utils -Filter *.c | ForEach-Object { "utils\$($_.Name)" })
    Calistir $cc (@('-std=c99', '-Wall', '-Wno-unused', "-I$(Join-Path $Kok 'native\windivert')") + $cdosya + @($rcObj,
        "-L$(Join-Path $wd 'x64')", '-lWinDivert', '-lws2_32') + $sertlik + @('-o', (Join-Path $Bld 'gdpi\goodbyedpi.exe')))
} finally { Pop-Location }

$app = Join-Path $Kok 'app'
$wpf = Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'WPF'
$ref = @('-r:System.dll', '-r:System.Drawing.dll', '-r:System.Windows.Forms.dll', '-r:System.ServiceProcess.dll', '-r:System.Core.dll',
         '-r:System.Web.Extensions.dll', '-r:System.Xaml.dll', "-r:$wpf\PresentationFramework.dll", "-r:$wpf\PresentationCore.dll", "-r:$wpf\WindowsBase.dll")
$ortak = @('-nologo', '-codepage:65001', '-platform:x64', '-optimize+')
$ortakKaynak = 'Engine.cs', 'Native.cs', 'Tray.cs', 'Flyout.cs', 'Strings.cs', 'Updater.cs' | ForEach-Object { Join-Path $app $_ }

# Surum VERSION dosyasindan exe'ye islenir (guncelleyici GitHub'daki son surumle karsilastirir).
$surum = (Get-Content -LiteralPath (Join-Path $Kok 'VERSION') -Raw).Trim()
if ($surum -notmatch '^\d+\.\d+\.\d+$') { throw "gecersiz VERSION: $surum" }
$asmInfo = Join-Path $Bld 'app\AssemblyInfo.cs'
Set-Content -LiteralPath $asmInfo -Encoding UTF8 -Value @(
    "[assembly: System.Reflection.AssemblyVersion(`"$surum.0`")]",
    "[assembly: System.Reflection.AssemblyFileVersion(`"$surum.0`")]",
    "[assembly: System.Reflection.AssemblyInformationalVersion(`"$surum`")]",
    "[assembly: System.Reflection.AssemblyCompany(`"swarlex`")]",
    "[assembly: System.Reflection.AssemblyCopyright(`"Copyright (C) 2026 swarlex. GPL-3.0-or-later`")]")

Adim 'Simge'
$ico = Join-Path $Bld 'app\app.ico'
$iconTool = Join-Path $Bld 'app\icontool.exe'
Calistir $Csc ($ortak + @('-target:exe', "-out:$iconTool") + $ref + $ortakKaynak + @((Join-Path $app 'IconTool.cs')))
Calistir $iconTool @($ico)

Adim "SwarlexEasyDpi.exe v$surum"
$gom = @(
    "-resource:$(Join-Path $Bld 'gdpi\goodbyedpi.exe'),goodbyedpi.exe"
    "-resource:$(Join-Path $Bld 'gdpi\WinDivert.dll'),WinDivert.dll"
    "-resource:$(Join-Path $Bld 'gdpi\WinDivert64.sys'),WinDivert64.sys"
    "-resource:$(Join-Path $Kok 'lang\tr.json'),lang.tr.json"
    "-resource:$(Join-Path $Kok 'lang\en.json'),lang.en.json"
)
$exe = Join-Path $Dist 'SwarlexEasyDpi.exe'
# calisan bir kopya exe'yi kilitler: calisan exe yeniden adlandirilabilir, kenara al
if (Test-Path -LiteralPath $exe) {
    try { [IO.File]::Open($exe, 'Open', 'ReadWrite', 'None').Dispose() }
    catch { Remove-Item -LiteralPath "$exe.old" -Force -ErrorAction SilentlyContinue; Rename-Item -LiteralPath $exe -NewName 'SwarlexEasyDpi.exe.old' }
}
Calistir $Csc ($ortak + @('-target:winexe', '-warn:4', "-out:$exe", "-win32manifest:$(Join-Path $app 'app.manifest')", "-win32icon:$ico") +
    $ref + $gom + $ortakKaynak + @((Join-Path $app 'Program.cs'), (Join-Path $app 'TrayApp.cs'), $asmInfo))

if ($Test) {
    Adim 'Testler'
    $t = Join-Path $Bld 'app\tests.exe'
    Calistir $Csc ($ortak + @('-target:exe', "-out:$t") + $ref + $gom + $ortakKaynak + @((Join-Path $Kok 'tests\Tests.cs'), $asmInfo))
    Calistir $t @((Join-Path $Bld 'app'))
}

# Guncelleyici her surumde bu dosyayla karsilastirir.
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$exe.sha256" -Value "$hash  SwarlexEasyDpi.exe" -Encoding ASCII
Adim "Hazir: $exe ($([math]::Round((Get-Item $exe).Length / 1KB)) KB)"
