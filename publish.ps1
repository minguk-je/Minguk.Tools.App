<#
    Minguk.Tools 퍼블리시 + Velopack 패키징 + GitHub Releases 업로드

    모든 상대 경로는 이 스크립트가 있는 폴더(솔루션 루트)를 기준으로 해석한다.
    그래서 어느 위치에서 실행하든 스크립트 자체만 옮기지 않으면 동작한다.

    패키징 결과물은 소스 저장소가 아니라 릴리스 저장소 폴더에 쌓인다.
        ..\Minguk.Tools.App\Releases

    사용법:
        (인자는 반드시 -Version / -Upload 처럼 이름을 붙여서 줄 것)

        .\publish.ps1                            # 로컬 패키징만
        .\publish.ps1 -Version 1.0.1             # 버전 지정
        .\publish.ps1 -Version 1.0.1 -Upload     # GitHub Releases 까지 업로드

    업로드 전 준비:
        repo 권한(Contents: read/write)이 있는 PAT 를 환경변수에 등록한다.
            setx GITHUB_TOKEN "github_pat_xxxxxxxx"     (등록 후 새 창에서 실행)

        토큰을 이 파일에 적어 두지 말 것. 커밋되는 순간 그 토큰은 폐기 대상이 된다.

    주의:
        vpk(CLI) 버전은 Velopack 런타임(PackageReference) 버전과 반드시 같아야 한다.
        Velopack 패키지 버전을 올리면 아래 $VpkVersion 도 같이 올릴 것.
#>
param(
    [string]$Version       = "1.0.0",
    [switch]$Upload,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Runtime       = "win-x64",
    [string]$Channel       = "win",
    [string]$RepoUrl       = "https://github.com/minguk-je/Minguk.Tools.App",
    [string]$Token         = $env:GITHUB_TOKEN,
    [string]$VpkVersion    = "1.2.0"
)

$ErrorActionPreference = "Stop"

Push-Location $PSScriptRoot
try {
    # csproj 의 TargetFramework 와 같아야 한다. WGC(Windows.Graphics.Capture) 때문에 windows10 TFM 을 쓴다.
    $TargetFramework = "net10.0-windows10.0.22621.0"

    $Project    = ".\Minguk.Tools\Minguk.Tools.csproj"
    $PublishDir = ".\Minguk.Tools\bin\x64\$Configuration\$TargetFramework\publish\$Runtime"
    $ReleaseDir = "..\Minguk.Tools.App\Releases"   # 릴리스 저장소 폴더
    $IconPath   = ".\Minguk.Tools\Resource\CI_Minguk.ico"
    $PackId     = "Minguk.Tools"
    $MainExe    = "Minguk.Tools.exe"

    function Invoke-Step($Name, [scriptblock]$Body) {
        Write-Host "`n=== $Name ===" -ForegroundColor Cyan
        & $Body
        if ($LASTEXITCODE -ne 0) { throw "$Name 실패 (exit $LASTEXITCODE)" }
    }

    if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
        throw "버전 형식이 올바르지 않다: '$Version'. Velopack 은 SemVer 3자리를 요구한다 (예: 1.0.1, 1.0.1-beta1)."
    }

    if ($Upload -and [string]::IsNullOrWhiteSpace($Token)) {
        throw "GitHub 토큰이 없다. GITHUB_TOKEN 환경변수를 설정하거나 -Token 인자를 줄 것."
    }

    if (-not (Test-Path $ReleaseDir)) {
        New-Item -ItemType Directory -Path $ReleaseDir -Force | Out-Null
    }

    # 같은(또는 더 낮은) 버전을 다시 패키징하면 델타가 꼬인다. 미리 경고만 하고 진행한다.
    $existing = @(Get-ChildItem $ReleaseDir -Filter "$PackId-*-full.nupkg" -ErrorAction SilentlyContinue |
        ForEach-Object { if ($_.Name -match "^$([regex]::Escape($PackId))-(\d+\.\d+\.\d+)") { [version]$Matches[1] } } |
        Sort-Object -Descending)

    # prerelease 꼬리표(-beta1)를 떼고 숫자 부분만 비교한다.
    $BaseVersion = [version](($Version -split '-')[0])

    if ($existing.Count -gt 0 -and $BaseVersion -le $existing[0]) {
        Write-Warning "이미 $($existing[0]) 버전이 있다. $Version 으로 다시 패키징하면 델타 계산이 어긋난다."
        if ($Upload) {
            Write-Warning "GitHub 에 같은 태그(v$Version)가 이미 있으면 업로드가 거부된다. 해당 릴리스를 지우거나 버전을 올릴 것."
        }
    }

    # 1) vpk CLI 설치/버전 맞추기 (Velopack 런타임과 같은 버전)
    Invoke-Step "vpk $VpkVersion 준비" {
        dotnet tool update --global vpk --version $VpkVersion
    }

    # 2) 퍼블리시 (self-contained, win-x64)
    #    고객 PC 에 .NET 이 없다고 보고 런타임을 통째로 담는다.
    #    PublishSingleFile 은 끈다 - Velopack 이 폴더 단위로 델타를 만들기 때문에
    #    단일 파일로 묶으면 매 업데이트가 사실상 전체 다운로드가 된다.
    if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }

    Invoke-Step "dotnet publish" {
        dotnet publish $Project `
            -c $Configuration `
            -p:Platform=x64 `
            -r $Runtime `
            -p:Version=$Version `
            --self-contained true `
            -p:PublishSingleFile=false `
            -p:PublishReadyToRun=false `
            -o $PublishDir
    }

    $ExePath = Join-Path $PublishDir $MainExe
    if (-not (Test-Path $ExePath)) {
        throw "퍼블리시 결과에 $MainExe 가 없다: $ExePath"
    }

    # 3) 기존 릴리스 내려받기 (델타 패키지를 만들려면 이전 버전이 필요하다)
    if ($Upload) {
        Invoke-Step "기존 릴리스 다운로드" {
            vpk download github `
                --repoUrl $RepoUrl `
                --token $Token `
                --channel $Channel `
                --outputDir $ReleaseDir
        }
    }

    # 4) Velopack 패키징
    Invoke-Step "vpk pack ($Version)" {
        vpk pack -y `
            --packId $PackId `
            --packTitle $PackId `
            --packVersion $Version `
            --packDir $PublishDir `
            --mainExe $MainExe `
            --icon $IconPath `
            --channel $Channel `
            --outputDir $ReleaseDir
    }

    # 4-1) 설치본/포터블에 버전 표시 사본을 남긴다.
    #      원본 이름은 vpk 규약이라 그대로 두어야 한다 (업데이트가 이 이름을 찾는다).
    foreach ($name in @("$PackId-$Channel-Setup.exe", "$PackId-$Channel-Portable.zip")) {
        $src = Join-Path $ReleaseDir $name
        if (Test-Path $src) {
            $dst = Join-Path $ReleaseDir ($name -replace "^$([regex]::Escape($PackId))-", "$PackId-$Version-")
            Copy-Item $src $dst -Force
            Write-Host "  버전 표시 사본 생성: $(Split-Path $dst -Leaf)" -ForegroundColor DarkGray
        }
    }

    # 5) GitHub Releases 업로드
    if ($Upload) {
        Invoke-Step "GitHub Releases 업로드" {
            vpk upload github -y `
                --repoUrl $RepoUrl `
                --token $Token `
                --channel $Channel `
                --outputDir $ReleaseDir `
                --publish `
                --releaseName "$PackId $Version" `
                --tag "v$Version"
        }
        Write-Host "`n업로드 완료: $RepoUrl/releases/tag/v$Version" -ForegroundColor Green
    }

    Write-Host "`n완료: $(Resolve-Path $ReleaseDir)" -ForegroundColor Green
    Get-ChildItem $ReleaseDir | Select-Object Name, Length | Format-Table
}
finally {
    Pop-Location
}
