@echo off
REM 소우주 공유용 단일 exe 빌드 — 키(법제처·Claude·VWorld·공공데이터포털)를 exe에 함께 묶어
REM 받는 사람이 설정 없이 바로 쓰게 한다. 결과는 NAS 공유 폴더에 exe 하나만 둔다.
REM
REM   키 원본: %USERPROFILE%\.lawreview\share-keys.json  (저장소 밖 — 절대 커밋하지 않는다)
REM   기본 대상: Y:\8.Tools\lawreview-dist   (다른 곳:  build-share.bat D:\어딘가)
REM
REM 일반 빌드(build-exe.bat)와 중간 파일을 섞지 않으려고 obj\share\ 를 따로 쓴다.
REM 섞이면 키가 든 Core.dll이 일반 빌드에 재사용될 수 있다.

setlocal
cd /d "%~dp0"

set "KEYS=%USERPROFILE%\.lawreview\share-keys.json"
set "DEST=%~1"
if "%DEST%"=="" set "DEST=Y:\8.Tools\lawreview-dist"
set "OUT=src\LawReview.App\bin\share-publish"

if not exist "%KEYS%" (
  echo 키 파일이 없습니다: %KEYS%
  echo 앱 설정의 키를 이 파일로 저장한 뒤 다시 실행하세요.
  exit /b 1
)

echo [1/3] 테스트 실행...
dotnet test -c Release || goto :fail

echo [2/3] 공유용 단일 exe 게시 (키 포함)...
dotnet publish src\LawReview.App -c Release -r win-x64 --self-contained ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:BundledSettings="%KEYS%" -p:IntermediateOutputPath=obj\share\ -p:OutputPath=bin\share\ ^
  -o "%OUT%" || goto :fail

echo [3/3] 공유 폴더로 복사: %DEST%
if not exist "%DEST%" mkdir "%DEST%" || goto :fail
copy /Y "%OUT%\LawReview.App.exe" "%DEST%\LawReview.App.exe" >nul || goto :locked

echo.
echo 완료: %DEST%\LawReview.App.exe  (키 포함 — 받는 PC에서 바로 사용)
exit /b 0

:locked
echo.
echo 공유 폴더로 복사하지 못했습니다 — 누군가 그 exe를 실행 중인지 확인하세요.
exit /b 1

:fail
echo.
echo 빌드 실패 — 위 오류를 확인하세요.
exit /b 1
