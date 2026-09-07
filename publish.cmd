@echo off
setlocal

rem ============================================================
rem  Minguk.Tools 퍼블리시 + Velopack 패키징 (cmd 판)
rem
rem  사용법:
rem      publish.cmd                  버전 1.0.0 으로 로컬 패키징
rem      publish.cmd 1.0.1            버전 지정
rem      publish.cmd 1.0.1 upload     GitHub Releases 까지 업로드
rem
rem  업로드하려면 GITHUB_TOKEN 환경변수에 PAT 가 등록되어 있어야 한다.
rem      setx GITHUB_TOKEN "github_pat_xxxx"
rem
rem  결과물은 소스 저장소가 아니라 릴리스 저장소 폴더에 쌓인다.
rem      ..\Minguk.Tools.App\Releases
rem
rem  옵션이 더 필요하면 publish.ps1 을 쓸 것. 이 파일은 그 요약판이다.
rem ============================================================

set "VERSION=%~1"
if "%VERSION%"=="" set "VERSION=1.0.0"

set "MODE=%~2"

set "ROOT=%~dp0"
rem csproj 의 TargetFramework 와 같아야 한다.
set "TFM=net10.0-windows10.0.22621.0"

set "PROJECT=%ROOT%Minguk.Tools\Minguk.Tools.csproj"
set "PUBLISH_DIR=%ROOT%Minguk.Tools\bin\x64\Release\%TFM%\publish\win-x64"
set "ICON=%ROOT%Minguk.Tools\Resource\CI_Minguk.ico"
for %%I in ("%ROOT%..\Minguk.Tools.App\Releases") do set "RELEASE_DIR=%%~fI"

set "PACK_ID=Minguk.Tools"
set "MAIN_EXE=Minguk.Tools.exe"
set "REPO_URL=https://github.com/minguk-je/Minguk.Tools.App"
set "CHANNEL=win"
set "RUNTIME=win-x64"
rem vpk CLI 버전은 Velopack PackageReference 버전과 반드시 같아야 한다.
set "VPK_VERSION=1.2.0"

set "UPLOAD=0"
if /i "%MODE%"=="upload" set "UPLOAD=1"

if "%UPLOAD%"=="1" if "%GITHUB_TOKEN%"=="" (
    echo [실패] GITHUB_TOKEN 환경변수가 없다. PAT 를 등록한 뒤 다시 실행할 것.
    exit /b 1
)

if not exist "%RELEASE_DIR%" mkdir "%RELEASE_DIR%"

echo.
echo === vpk %VPK_VERSION% 준비 ===
dotnet tool update --global vpk --version %VPK_VERSION%
if errorlevel 1 goto :failed

echo.
echo === dotnet publish ===
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
dotnet publish "%PROJECT%" -c Release -p:Platform=x64 -r %RUNTIME% -p:Version=%VERSION% --self-contained true -p:PublishSingleFile=false -p:PublishReadyToRun=false -o "%PUBLISH_DIR%"
if errorlevel 1 goto :failed
if not exist "%PUBLISH_DIR%\%MAIN_EXE%" goto :exemissing

if "%UPLOAD%"=="1" (
    echo.
    echo === 기존 릴리스 다운로드 ^(델타 패키지용^) ===
    vpk download github --repoUrl %REPO_URL% --token %GITHUB_TOKEN% --channel %CHANNEL% --outputDir "%RELEASE_DIR%"
    if errorlevel 1 goto :failed
)

if exist "%RELEASE_DIR%\%PACK_ID%-%VERSION%-full.nupkg" (
    echo [경고] %VERSION% 버전이 이미 있다. 델타 계산이 어긋날 수 있다.
)

echo.
echo === vpk pack %VERSION% ===
vpk pack -y --packId %PACK_ID% --packTitle "%PACK_ID%" --packVersion %VERSION% --packDir "%PUBLISH_DIR%" --mainExe %MAIN_EXE% --icon "%ICON%" --channel %CHANNEL% --outputDir "%RELEASE_DIR%"
if errorlevel 1 goto :failed

rem 설치본/포터블에 버전 표시 사본을 남긴다. 원본 이름은 vpk 규약이라 그대로 둔다.
if exist "%RELEASE_DIR%\%PACK_ID%-%CHANNEL%-Setup.exe" copy /y "%RELEASE_DIR%\%PACK_ID%-%CHANNEL%-Setup.exe" "%RELEASE_DIR%\%PACK_ID%-%VERSION%-%CHANNEL%-Setup.exe" >nul
if exist "%RELEASE_DIR%\%PACK_ID%-%CHANNEL%-Portable.zip" copy /y "%RELEASE_DIR%\%PACK_ID%-%CHANNEL%-Portable.zip" "%RELEASE_DIR%\%PACK_ID%-%VERSION%-%CHANNEL%-Portable.zip" >nul

if "%UPLOAD%"=="1" (
    echo.
    echo === GitHub Releases 업로드 ===
    vpk upload github -y --repoUrl %REPO_URL% --token %GITHUB_TOKEN% --channel %CHANNEL% --outputDir "%RELEASE_DIR%" --publish --releaseName "%PACK_ID% %VERSION%" --tag "v%VERSION%"
    if errorlevel 1 goto :failed
    echo.
    echo 업로드 완료: %REPO_URL%/releases/tag/v%VERSION%
)

echo.
echo 완료: %RELEASE_DIR%
dir /b "%RELEASE_DIR%"
endlocal
exit /b 0

:failed
echo.
echo [실패] 이전 단계에서 오류가 발생했다. (errorlevel %errorlevel%)
endlocal
exit /b 1

:exemissing
echo.
echo [실패] 퍼블리시 결과에 %MAIN_EXE% 가 없다. 퍼블리시 출력 폴더를 확인할 것.
endlocal
exit /b 1
