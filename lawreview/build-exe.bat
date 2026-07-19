@echo off
REM 협력체 배포용 단일 exe 빌드. .NET 8 SDK 필요.
REM 결과물: src\LawReview.App\bin\Release\net8.0-windows\win-x64\publish\LawReview.App.exe

setlocal
cd /d "%~dp0"

echo [1/2] 테스트 실행...
dotnet test -c Release || goto :fail

echo [2/2] 단일 exe 게시...
dotnet publish src\LawReview.App -c Release -r win-x64 --self-contained ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true || goto :fail

echo.
echo 완료: src\LawReview.App\bin\Release\net8.0-windows\win-x64\publish\LawReview.App.exe
echo 이 exe 파일 하나만 협력체에 전달하세요. (API 키는 각자 설정 탭에서 입력)
exit /b 0

:fail
echo.
echo 빌드 실패 — 위 오류를 확인하세요.
exit /b 1
