@echo off
setlocal EnableExtensions

pushd "%~dp0"
set "PROJECT_ROOT=%CD%"
set "APP_PROJECT=%PROJECT_ROOT%\src\Kazuko.App\Kazuko.App.csproj"
set "FIREBIRD_CLIENT=%PROJECT_ROOT%\firebird\Firebird-5.0.4.1812-0-windows-x64\fbclient.dll"
set "DATABASE_FILE=%PROJECT_ROOT%\data\data.fdb"
set "ARTIFACTS_DIR=%PROJECT_ROOT%\artifacts"
set "OUTPUT_DIR=%ARTIFACTS_DIR%\Kazuko-Test-win-x64"
set "ZIP_FILE=%ARTIFACTS_DIR%\Kazuko-Test-win-x64.zip"

echo [1/5] Kiem tra dieu kien dong goi...
where dotnet >nul 2>&1
if errorlevel 1 (
    echo LOI: Khong tim thay dotnet SDK.
    goto :failed
)

if not exist "%APP_PROJECT%" (
    echo LOI: Khong tim thay project Kazuko.App.
    goto :failed
)

if not exist "%FIREBIRD_CLIENT%" (
    echo LOI: Thieu Firebird Embedded tai thu muc firebird.
    goto :failed
)

if not exist "%DATABASE_FILE%" (
    echo LOI: Khong tim thay data\data.fdb.
    goto :failed
)

tasklist /FI "IMAGENAME eq KAZUKO_ERP_JEWELRY.exe" 2>nul | find /I "KAZUKO_ERP_JEWELRY.exe" >nul
if not errorlevel 1 (
    echo LOI: Hay dong KAZUKO ERP truoc khi dong goi de database duoc copy an toan.
    goto :failed
)

if not exist "%ARTIFACTS_DIR%" mkdir "%ARTIFACTS_DIR%"

if /I not "%OUTPUT_DIR%"=="%PROJECT_ROOT%\artifacts\Kazuko-Test-win-x64" (
    echo LOI: Duong dan output khong an toan.
    goto :failed
)

if exist "%OUTPUT_DIR%" rmdir /S /Q "%OUTPUT_DIR%"
if exist "%ZIP_FILE%" del /F /Q "%ZIP_FILE%"

echo [2/5] Publish ban portable win-x64...
dotnet publish "%APP_PROJECT%" ^
    --configuration Release ^
    --runtime win-x64 ^
    --self-contained true ^
    --output "%OUTPUT_DIR%" ^
    -p:PublishSingleFile=false ^
    -p:PublishReadyToRun=false ^
    -m:1
if errorlevel 1 goto :failed

echo [3/5] Copy database test...
if not exist "%OUTPUT_DIR%\data" mkdir "%OUTPUT_DIR%\data"
copy /Y "%DATABASE_FILE%" "%OUTPUT_DIR%\data\data.fdb" >nul
if errorlevel 1 goto :failed

echo [4/5] Tao file chay nhanh cho may test...
> "%OUTPUT_DIR%\CHAY-KAZUKO-TEST.bat" echo @echo off
>> "%OUTPUT_DIR%\CHAY-KAZUKO-TEST.bat" echo setlocal EnableExtensions
>> "%OUTPUT_DIR%\CHAY-KAZUKO-TEST.bat" echo set "KAZUKO_PORTABLE_MODE=1"
>> "%OUTPUT_DIR%\CHAY-KAZUKO-TEST.bat" echo set "KAZUKO_FIREBIRD_DATABASE=%%~dp0data\data.fdb"
>> "%OUTPUT_DIR%\CHAY-KAZUKO-TEST.bat" echo set "KAZUKO_FIREBIRD_EMBEDDED=true"
>> "%OUTPUT_DIR%\CHAY-KAZUKO-TEST.bat" echo set "KAZUKO_FIREBIRD_PASSWORD=portable-embedded"
>> "%OUTPUT_DIR%\CHAY-KAZUKO-TEST.bat" echo start "" "%%~dp0KAZUKO_ERP_JEWELRY.exe"

> "%OUTPUT_DIR%\HUONG-DAN-TEST.txt" echo 1. Giai nen toan bo file ZIP.
>> "%OUTPUT_DIR%\HUONG-DAN-TEST.txt" echo 2. Chay CHAY-KAZUKO-TEST.bat.
>> "%OUTPUT_DIR%\HUONG-DAN-TEST.txt" echo 3. Dung tai khoan KAZUKO co trong database da dong goi de dang nhap.
>> "%OUTPUT_DIR%\HUONG-DAN-TEST.txt" echo 4. Du lieu test nam tai data\data.fdb trong thu muc vua giai nen.
>> "%OUTPUT_DIR%\HUONG-DAN-TEST.txt" echo 5. Khong chay truc tiep trong file ZIP.

echo [5/5] Nen thanh file ZIP...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -LiteralPath '%OUTPUT_DIR%' -DestinationPath '%ZIP_FILE%' -Force"
if errorlevel 1 goto :failed

echo.
echo DONG GOI THANH CONG:
echo %ZIP_FILE%
echo.
echo Luu y: ZIP co chua ban sao database hien tai. Chi gui cho nguoi test tin cay.
popd
exit /B 0

:failed
echo.
echo DONG GOI THAT BAI.
popd
exit /B 1
