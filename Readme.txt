Minguk.Tools 메모
=================

글자 읽기(OCR) 언어 팩
----------------------
캡처 모니터의 "글자 읽기"·"이름표 읽기" 는 Windows 내장 OCR(Windows.Media.Ocr)을 쓴다.
언어 팩이 있어야 하고, 한국어 팩만 있으면 숫자 0 을 "이", 21 을 "기" 로 읽는 일이 있다.
숫자·영문이 많으면 영어(미국) OCR 을 추가한다. 다시 시작할 필요 없다.

PowerShell(관리자):

    Add-WindowsCapability -Online -Name "Language.OCR~~~en-US~0.0.1.0"

깔린 것 확인:

    Get-WindowsCapability -Online -Name "Language.OCR*" | Where-Object State -eq Installed | Select-Object Name

한국어 팩이 없는 PC 에서는:

    Add-WindowsCapability -Online -Name "Language.OCR~~~ko-KR~0.0.1.0"

설정 화면으로 하려면: 설정 > 시간 및 언어 > 언어 및 지역 > 언어 추가 > English (United States)
> 설치 옵션에서 "광학 문자 인식(OCR)" 만 체크.

다른 PC 로 옮길 때
------------------
%AppData%\Minguk.Tools 폴더 하나만 복사하면 된다 (데이터셋·모델·libtorch·사전학습 가중치·설정).
NVIDIA 카드가 있어야 실시간 검출 속도가 나온다. 학습은 GPU 가 없으면 29배 느리다.
