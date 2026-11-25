@echo off
setlocal
chcp 65001 >nul
set "D=%~dp0"
echo.
echo === WirelessBattery kaldirma ===
echo  - Calisan program kapatilir
echo  - Windows ile baslama kaydi silinir
echo  - Bu klasordeki program dosyalari silinir: %D%
echo.
choice /c EH /n /m "Devam edilsin mi? (E = Evet, H = Hayir): "
if errorlevel 2 goto :iptal

taskkill /im WirelessBattery.exe /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v WirelessBattery /f >nul 2>&1
reg delete "HKCU\Software\WirelessBattery" /f >nul 2>&1
del /f /q "%D%WirelessBattery.exe" "%D%build.bat" "%D%README.txt" >nul 2>&1
del /f /q "%TEMP%\WirelessBattery-rapor.txt" >nul 2>&1
rd /s /q "%D%src" >nul 2>&1

echo.
echo Kaldirildi. Bir tusa basinca bu dosya ve bos klasor de silinir.
pause >nul
cd /d "%TEMP%"
(goto) 2>nul & del /f /q "%D%kaldir.bat" & rd "%D%"

:iptal
echo Iptal edildi.
pause
