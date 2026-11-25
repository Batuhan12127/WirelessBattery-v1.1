@echo off
setlocal EnableDelayedExpansion
chcp 65001 >nul
cd /d "%~dp0"

echo.
echo === WirelessBattery derleme ===
echo Klasor: %CD%
echo.

rem --- Windows'la birlikte gelen C# derleyicisi (kurulum gerektirmez)
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "!CSC!" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "!CSC!" goto :nocsc

rem --- Kaynak dosyalari bul: once src\ klasoru, yoksa ayni klasor
set "SRC="
if exist "src\Program.cs" set "SRC=src"
if not defined SRC if exist "Program.cs" set "SRC=."
if not defined SRC goto :nosrc

set "FILES="
for %%F in ("%SRC%\*.cs") do set FILES=!FILES! "%%F"
echo Kaynak klasoru: %SRC%
echo Dosyalar:!FILES!
echo.

rem --- Calisan kopya varsa kapat (exe kilitliyse derleme basarisiz olur)
taskkill /im WirelessBattery.exe /f >nul 2>&1

echo Derleniyor...
"!CSC!" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /out:WirelessBattery.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll !FILES!
if errorlevel 1 goto :fail

echo.
echo TAMAM: WirelessBattery.exe olusturuldu ve baslatiliyor.
start "" "%CD%\WirelessBattery.exe"
echo.
pause
exit /b 0

:nocsc
echo HATA: .NET Framework 4 derleyicisi (csc.exe) bulunamadi.
echo Aranan yer: %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\
goto :fail

:nosrc
echo HATA: Program.cs bulunamadi. Bu klasorde kaynak dosyalar yok.
echo.
echo Cozum: zip dosyasini ZIP ICINDEN CALISTIRMA. Once sag tik, "Tumunu ayikla"
echo (Extract all) de, olusan klasorun icindeki build.bat'i calistir.
echo Klasorde "src" adli klasor ve icinde 11 tane .cs dosyasi olmali.
goto :fail

:fail
echo.
echo DERLEME BASARISIZ - yukaridaki mesajlari kopyalayip bana gonder.
pause
exit /b 1
