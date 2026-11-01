@echo off
REM 협력체 배포용 단일 exe 빌드. .NET 8 SDK 필요.
REM
REM 저장소(이 폴더)는 소스, 실제로 쓰는 실행본은 C:\Tools\lawreview-dist 에 둔다.
REM 빌드가 끝나면 그 폴더로 바로 복사한다 — 복사를 손으로 하면 잊어버려서
REM 화면을 고쳐놓고도 몇 주 전 exe를 계속 쓰는 일이 실제로 있었다.
REM   다른 곳에 두려면:  build-exe.bat D:\어딘가
REM   복사하지 않으려면: build-exe.bat -

setlocal
cd /d "%~dp0"

set "DIST=%~1"
if "%DIST%"=="" set "DIST=C:\Tools\lawreview-dist"

set "PUB=src\LawReview.App\bin\Release\net8.0-windows\win-x64\publish\LawReview.App.exe"

echo [1/3] 테스트 실행...
dotnet test -c Release || goto :fail

echo [2/3] 단일 exe 게시...
dotnet publish src\LawReview.App -c Release -r win-x64 --self-contained ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true || goto :fail

if "%DIST%"=="-" (
  echo [3/3] 배포 폴더 복사 건너뜀.
  goto :done
)

echo [3/3] 배포 폴더로 복사: %DIST%
if not exist "%DIST%" mkdir "%DIST%" || goto :fail
REM 앱이 실행 중이면 복사가 막힌다 — 먼저 닫으라고 알려준다.
copy /Y "%PUB%" "%DIST%\LawReview.App.exe" >nul || goto :locked

:done
echo.
echo 완료: %PUB%
if not "%DIST%"=="-" echo 배포본: %DIST%\LawReview.App.exe
echo 이 exe 파일 하나만 협력체에 전달하세요. (API 키는 각자 설정에서 입력)
exit /b 0

:locked
echo.
echo 배포 폴더로 복사하지 못했습니다 — 앱이 실행 중인지 확인하고 닫은 뒤 다시 실행하세요.
echo   게시된 파일은 여기 있습니다: %PUB%
exit /b 1

:fail
echo.
echo 빌드 실패 — 위 오류를 확인하세요.
exit /b 1
