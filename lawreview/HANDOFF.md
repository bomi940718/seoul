# 법규검토서 생성기 — 작업 인수인계 문서

> 새 대화(세션)에서 이 프로젝트를 이어갈 때 이 문서부터 읽을 것.
> 마지막 업데이트: 2026-07-19 / 브랜치: `claude/korean-law-review-automation-fkmgrh`

---

## 1. 프로젝트 목표

건축 인허가 **법규검토서 작성 자동화**. 희림건축이 메가존클라우드와 만든 AI 법규검토 시스템(설계도면 법규검토 수일 → 수십 분)과 같은 구조를 소규모로 구현한다.
대지 조건·면적표를 입력하면 → 법제처 현행 법령·조례 원문을 근거로 항목별 적용 여부를 판정하고 → 실무 검토서와 동일한 구성의 **검토서.docx**를 출력한다.

- 참고 저장소: [chrisryugj/korean-law-mcp](https://github.com/chrisryugj/korean-law-mcp) — 법제처 API 활용 참조 구현 (Node라서 코드에 직접 쓰진 않고, API 조합 방식을 참고)
- 출력 형식의 원형: 실무 법규검토서 HWP (대전 둔곡 세이퍼존 공장, 2023.10) — 사용자가 첨부했던 파일. 구조 분석 결과는 이미 체크리스트와 DOCX 빌더에 반영되어 있음

## 2. 확정된 설계 결정 (변경하려면 사용자에게 물어볼 것)

| 결정 | 내용 | 이유 |
|---|---|---|
| 언어 | **C# (.NET 8)** | 사용자가 파이썬을 모름. 항상 C#으로 개발 |
| UI | **WinForms** (탭 모듈 구조) | 협력체(외부 회사)도 같이 사용, exe 배포 |
| 출력 | **DOCX** (OpenXML) | HWP 재현 불필요, 수정 가능한 파일이면 됨 |
| 배포 | 자가포함 단일 exe | `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` |
| API 키 | **각 사용자가 직접 발급·입력**, 해당 PC(%APPDATA%\LawReview)에만 저장 | exe에 키를 심으면 유출·과금 위험 |
| 코드 위치 | GitHub은 작업용, **최종 산출물은 사용자 PC `C:\Tools`** | 사용자가 clone/pull로 받아감 |
| 확장 예정 | 토지이용계획확인원 **CAD 변환 모듈** | WinForms에 IAppModule 탭으로 추가. 본 개발 끝나고 사용자와 논의 후 진행 |

### 검토 품질 원칙 (사용자가 명시적으로 요구한 것 — 절대 규칙)

1. **검토서에 인용되는 모든 조문은 법제처 Open API에서 조회한 현행 원문만.** 조문번호·시행일자 함께 기록.
2. **토지이음(eum.go.kr)의 개략 검토 내용은 검토서에 절대 인용 금지.** 실무에서 지자체 조례를 따라가지 못해 틀리는 경우가 많음. 토지이음의 역할은 (a) 용도지역·지구 확인, (b) 필지별 **관련 모법 목록 색인** — 딱 여기까지.
3. **숫자는 코드가, 판단만 AI가.** 건폐율·용적률·주차 산정식은 결정적 코드로 계산. Claude는 조문 원문 근거로 "적용/해당없음/확인필요" 판정 + 사유만.
4. **조례는 조문번호 하드코딩 금지.** 지자체마다 번호가 다름(공개공지: 대전 건축조례 34조, 타 시는 다른 번호). 제목 키워드(`articleTitleKeyword`)로 탐색. 이 규칙은 테스트로 강제되어 있음.
5. **자동 판정 불가능한 것은 정직하게 "확인필요".** 지구단위계획 지침도(도면) 규제 등.
6. 적용 우선순위: **지구단위계획 > 조례 > 시행령**.

## 3. 완료된 작업

### 1단계 — 엔진 + WinForms 뼈대 (커밋 39aadd8 이후 첫 커밋)

- `LawReview.Core` (엔진 라이브러리)
  - `Models/ProjectInput.cs` — 설계개요·면적표·법정한도·주차기준 입력 모델
  - `LawApi/MolegClient.cs` — 법제처 Open API (lawSearch/lawService, 법령·자치법규·행정규칙)
  - `Review/QuantitativeCalculator.cs` — 건폐율·용적률·연면적·주차대수 계산. 산정식 문자열을 검토서 표기 그대로 생성 ("6,030.10 x 0.70 = 4,221.07 m²"). 주차 단수처리: 0.5 이상 올림·미만 버림, 장애인은 반올림 최소 1대
  - `Review/Checklist.cs` + `checklists/standard.json` — 검토 항목 선언적 정의
  - `Review/ReviewEngine.cs` — 오케스트레이터 (조문 조회→캐시→판정→결과). `{시}`/`{구}` 자리표시자를 프로젝트 지자체명으로 치환
  - `Ai/ClaudeJudgmentProvider.cs` — Anthropic Messages API. JSON 응답 {"판정","사유"} 파싱, 실패 시 확인필요
  - `Report/DocxReportBuilder.cs` — 표지→면적표→설계개요→검토법규→요약 검토표→장별 상세검토
  - `AppSettings.cs` — %APPDATA%\LawReview\settings.json
- `LawReview.App` (WinForms) — `IAppModule` 탭 구조. ReviewModule(입력+면적표 그리드(엑셀 붙여넣기 지원)+실행 로그+docx 저장), SettingsModule(API 키)
  - 주의: 리눅스 크로스빌드를 위해 `UseWindowsForms` 대신 `FrameworkReference Include="Microsoft.WindowsDesktop.App.WindowsForms"` + `EnableWindowsTargeting` 사용. 디자이너/.resx 안 씀 (코드 전용 UI). 이 구조 유지할 것

### 2단계 — 검토 항목 전면 확충 + 별표 조회 (커밋 01cd3c5)

- 체크리스트 19 → **54항목**: 실무 검토서 전체 장 커버 — 요약표, 제4장(대지와 도로), 제5장(구조·재료·피난·방화 22항목), 제6장(대지 안의 공지), 제7장(건축설비 8항목), 녹색건축물, 각종 인증 의무(신재생·에너지효율·녹색건축·BF), 장애인편의법, 주차장법 상세, 교통영향평가·미술작품·매장유산
- MolegClient: 가지번호 조문 파싱("제48조의2"), 제목 키워드 조문 탐색, 별표·서식 검색(target=licbyl, 검토서에 원문 링크로 인용)

### 실 API 검증 완료 (2026-07-19, OC 키 lawreview) — 파서 전면 보정

법제처 4종(법령·자치법규·행정규칙·별표) 실호출로 확인한 실제 응답 구조가 target마다 달랐고,
MolegClient를 그에 맞게 수정했다. **실응답 캡처가 `tests/Fixtures/`에 커밋되어 있으니
파서를 고칠 때는 반드시 이 픽스처 테스트를 통과시킬 것.** 핵심 차이:

- 행정규칙 검색: 항목 키 `admrul`, 단건이면 배열 아닌 객체. 본문 조회는 `MST=` 대신 `ID=`
- 행정규칙 본문: `조문내용`이 구조 없는 문자열 배열 → "제N조(제목)" 정규식으로 분리
- 자치법규 본문: 기본정보 키 `자치법규기본정보`, 조문은 `조문.조[]`, `조내용`에 전체 평문,
  조문번호는 6자리 문자열 배열(`["000400"]`=제4조, 뒤 2자리 가지번호)
- 별표: licbyl 검색은 `search=2`(법령명) 필수인데 일부 법령(건축법 시행령)은 색인에 없음
  → **본문 응답의 별표 섹션(LawText.Annexes)을 우선 사용** (PDF/HWP 링크 포함)
- 법령 검색 첫 결과 폴백 위험: "대전광역시 건축 조례" 검색 첫 결과가 "건축기본조례"
  → 정확명 → 공백무시 일치 → 첫 결과 순 매칭(ReviewEngine.PickBestMatch)

**둔곡 E2E 성공**: `dotnet run --project src/LawReview.Cli -- <OC키> [출력.docx]` (개발용 러너)
→ 54항목 전부 조문 인용 성공, 검토서 생성. 건폐율·용적률·주차 산정식 원본 일치,
공개공지 = 대전 건축조례 제34조를 제목 키워드로 정확 탐색, 별표 링크 인용 확인.
Claude 판정만 미검증(키 없음 — 러너가 "확인필요"로 대체).

### 3단계 — 서울도시공간포털 지구단위계획 수집기 (2026-07-19)

`src/LawReview.Core/Municipal/` — 시별 모듈 구조(`IDistrictPlanProvider` + `DistrictPlanProviders` 레지스트리).

- `SeoulUrbanPortalClient`: 실측으로 확인한 비공개 API 사용 (세션 불필요)
  - 목록: `POST /ctymgrpln/getDstplanList.json` — JSON 본문(**UTF-8 필수**, CP949로 보내면
    500), `classifyG:"UQQ301"`=지구단위계획, `keywordList:["봉천동"]`(명칭·위치 부분일치).
    응답은 Spring Page(`content[]`/`totalElements`). 조서(면적 기정/변경/변경후) 포함
  - 고시문 PDF: `GET /{tnNtfc.tnNtfcImage.aImagePath}/{aImageName}` 정적 경로 직접 다운로드
    (한글 파일명 URL 인코딩 필요, 구형 레코드는 HWP)
- ReviewEngine 연동: `district_unit_plan` 항목에서 주소의 법정동으로 후보 구역 검색 →
  구역명·고시번호·고시문 링크를 인용으로 첨부. **판정은 확인필요 유지** (원칙 5 — 지침도
  규제는 자동 판정 불가). 미구현 지자체(서울 외)는 기존 수동 안내 유지
- 단독 실행: `dotnet run --project src/LawReview.Cli -- --districtplan 봉천동 [저장.pdf]`
- 실검증: 봉천동 8개 구역 조회 + 고시문 PDF(22MB) 다운로드 성공

### 4단계 — 토지이음 색인 (2026-07-19)

`src/LawReview.Core/LandUse/VworldClient.cs` — 브이월드(VWorld) 국토정보플랫폼 연동.
**역할은 색인만** (검토 품질 원칙 2 — 개략 검토 내용은 검토서에 인용 금지):

- 지번 주소 → PNU(19자리): `api.vworld.kr/req/address` (getCoord, type=PARCEL)
- PNU → 용도지역·지구·구역 이름 목록: `api.vworld.kr/ned/data/getLandUseAttr` (cnflcAt=1)
- PNU → 토지대장(대지면적·지목·법정동명): `api.vworld.kr/ned/data/ladfrlList` (`lndpclAr`은 문자열)
- 앱 연동: 대지위치 옆 **"자동조회" 버튼** → **지역/지구 + 대지면적 + 광역/기초 지자체**를 한 번에 채움.
  지자체명은 토지대장 법정동명(`ldCodeNm`)에서 분리(`SplitMunicipality`) — 주소 문자열 파싱보다 정확.
  도 산하 "성남시 분당구"는 조례명이 "성남시 ○○ 조례"라 **시+구를 함께** 기초 지자체로 둔다(테스트로 강제).
  건축면적·연면적·층수·면적표는 **설계 결과물이라 조회 불가** — 사용자가 입력한다(이 경계는 유지할 것)
- 키는 www.vworld.kr 무료 발급, 발급 시 등록한 **서비스 URL(domain)** 과 일치해야 NED API 동작.
  설정 탭에 VWorld 키·서비스 URL 입력란 추가 (없으면 지역/지구 직접 입력 — 선택 기능)
- 단독 실행: `VWORLD_KEY=... dotnet run --project src/LawReview.Cli -- --landuse "대전광역시 유성구 둔곡동 407-5"`
- 실검증(2026-07-25 확장 포함): 둔곡 407-5 → 용도지역 6건·**대지면적 6,030.10㎡(실무 검토서와 일치)**·대전광역시/유성구,
  서울 봉천동 857-1 → 9건·484.50㎡·서울특별시/관악구, 성남 정자동 178-1 → 10건·6,600㎡·경기도/**성남시 분당구**.
  앱 화면에서도 주소만 입력→자동조회로 4개 칸이 채워지는 것 확인. 응답 구조는 cadastral-mcp와 동일

### 테스트 — 44건 전부 통과

검증 기준이 **실제 실무 검토서(둔곡)의 수치**: 연면적 2,484.43㎡(PIT 제외), 건폐율 24.10%, 법정 건축면적 4,221.07㎡, 주차 12.42→12대, 장애인 0.36→1대.
파싱 테스트는 실 API 캡처 픽스처(`tests/LawReview.Core.Tests/Fixtures/`) 기반.

### 원본 검토서에서 발견한 사람 오류 2건 (자동화 정당성 근거)

1. 검토법규 목록에 "평택시 주차장 조례" — 대전 프로젝트인데 이전 프로젝트 템플릿 복사 잔재
2. 법정 연면적 21,150.35㎡로 표기 — 6,030.10×3.5 = **21,105.35**가 맞음 (전기 오류)

## 4. 저장소 구조

```
seoul/  (bomi940718/seoul, 브랜치 claude/korean-law-review-automation-fkmgrh)
├─ seoul_tour_2026_fixed_SHARE.html   ← 기존 무관 파일 (건드리지 말 것)
└─ lawreview/
   ├─ HANDOFF.md          ← 이 문서
   ├─ README.md           ← 아키텍처·빌드·로드맵
   ├─ LawReview.sln
   ├─ src/LawReview.Core/   (LawApi·Review·Ai·Report·Municipal·LandUse)
   ├─ src/LawReview.App/    (WinForms 배포 대상)
   ├─ src/LawReview.Cli/    (개발·검증용 콘솔 러너 — 배포 대상 아님)
   └─ tests/LawReview.Core.Tests/
```

사용자 로컬: `C:\Tools`에 clone해서 사용. 개발 푸시 후 사용자가 `git pull`.

```
cd C:\Tools
git clone -b claude/korean-law-review-automation-fkmgrh https://github.com/bomi940718/seoul.git
cd seoul\lawreview
dotnet test && dotnet run --project src\LawReview.App
```

### 5단계 — 배포 (2026-07-19)

- **자가포함 단일 exe**: `build-exe.bat` 또는
  `dotnet publish src/LawReview.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
   -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true`
  → 약 70MB, .NET 런타임 포함. 받는 사람은 설치 없이 exe만 더블클릭.
- **체크리스트를 어셈블리에 내장**(`LawReview.Core.csproj`의 EmbeddedResource) — `ChecklistLoader.LoadDefault()`가
  실행폴더 `checklists/standard.json`을 우선하고 없으면 내장 사본 사용. 단일 exe에서 폴더 없이 동작(테스트로 강제).
- **Claude 키 없이도 실행 가능**: `OfflineJudgmentProvider`가 AI 판정을 "확인필요"로 대체(조문 인용·계산·검토서는 정상).
- 협력체 안내: `DEPLOY.md`(키 발급 절차·신뢰 범위 포함).
- **빌드된 exe는 사용자 PC `C:\Tools\lawreview-dist\LawReview.App.exe`에 배치** (git엔 미포함 — 70MB).

### AI 판정 파서 버그 수정 (2026-07-25)

실제 Claude 판정으로 둔곡 검토서를 생성해 54항목을 교차검증한 결과:

- **판정 품질 자체는 양호** — 층수·연면적 기준을 조문 항·호까지 짚어 정확히 판단.
  사유가 자기 인용 조문을 안 짚은 항목 0건 (오염 없음).
- **버그 3건 발견·수정** (모두 테스트로 고정):
  1. **잘린 응답에 판정을 버림** — "건축물의 내화구조"가 `판정 응답 해석 실패`로 확인필요 처리됨.
     `max_tokens=1024`로 사유가 문장 중간에 잘려 JSON이 미완성 → `ParseJudgment`가 엄격 파싱만 해
     **AI가 내놓은 "해당없음"을 통째로 버림**. → `max_tokens=2048` + `ParsePartial` 추가(미완성
     JSON에서도 정규식으로 판정·사유 복구, 잘렸다는 사실을 사유에 명시).
  2. **응답 첫 블록을 무조건 text로 가정** — `content[0].GetProperty("text")`가
     `KeyNotFoundException`을 던져 **검토 전체가 중단**(실제 발생). → `ExtractText`가 content
     블록을 훑어 첫 text 블록을 찾고, 없으면 그 항목만 확인필요 처리.
  3. **판정 1건 실패가 전체를 죽임** — ReviewEngine이 판정 호출을 항목 단위로 격리하지 않았음.
     → try/catch로 감싸 해당 항목만 확인필요로 두고 나머지 50여 항목을 끝까지 진행.
     (API 일시 오류로 검토 전체를 잃지 않게 하는 안전장치)

수정 후 `claude-sonnet-5`로 재실행 검증: **예외 없이 54항목 완주**, 해석 실패 0건,
"건축물의 내화구조"가 정상적으로 **적용**(영 제56조제1항제3호 2천㎡ 이상 근거) 판정됨.
판정 분포 적용 14 / 해당없음 23 / 확인필요 12. sonnet-5 실동작도 이때 처음 확인.

> 검증 팁: 생성된 docx를 평문으로 훑으면 표의 행 경계를 넘어 옆 항목 텍스트를 집어와
> **없는 버그를 만들어낸다**(실제로 한 번 오진했음). 반드시 `<w:tr>`/`<w:tc>` 구조로 파싱할 것.

## 5. 현재 상태의 한계 (다음 세션이 제일 먼저 알아야 할 것)

- ~~법제처 API 실호출 미검증~~ → **완료 (2026-07-19).** 실응답 픽스처 테스트가 회귀를 막는다.
- **Claude 판정은 여전히 실 호출 미검증** (프롬프트·파싱 로직만 테스트됨). CLI 러너는
  ANTHROPIC_API_KEY 환경변수가 있으면 실제 판정을 쓴다. 사용자가 앱 설정에 Claude 키를 넣고
  실제 판정 품질(사유가 조문 근거를 제대로 짚는지)을 확인하는 것이 다음 검증 포인트.
- WinForms는 **실행·검토서 생성까지 확인 완료**(둔곡 예시, offline 판정). 실 Claude 판정 화면은 미확인.
- 지구단위계획은 **후보 구역 목록**까지만 자동 (법정동 키워드 검색). 필지→구역 정확 대응은
  지도(WFS) 연동 필요 — 포털 지도 서비스는 ArcGIS proxy(`/proxy/proxy.jsp`) 경유라 추후 검토.
- 서울 외 지자체(대전 등)의 지구단위계획 포털은 미구현 — `IDistrictPlanProvider` 구현 추가 방식.
- VWorld "자동조회"는 지번 주소만 인식(도로명 주소 미지원). 키 발급 도메인과 서비스 URL 불일치 시 실패.

## 6. 다음 작업 (우선순위 순)

### 검증 (사용자 피드백 대기)

- 앱에 Claude 키 넣고 실제 AI 판정 품질 확인 → 판정 프롬프트 튜닝 (현재 미검증 영역)
- 생성 검토서 서식 세부 조정 (사용자 피드백 반영)

### 3단계 확장 (지자체 문서 수집)

- 고시문 PDF 텍스트 파싱 → 결정조서·지침 본문 추출 (대형 PDF, 도면 페이지 혼재 — 텍스트
  레이어 유무 확인 필요. 도면 규제는 계속 "확인필요" + 원본 링크, 원칙 5)
- 심의기준·경관가이드도 같은 파이프라인 재사용 (소스 URL 등록 방식)
- 서울 외 지자체 `IDistrictPlanProvider` 구현 추가

### 이후 (사용자와 논의 후)

- 토지이용계획확인원 CAD 변경 모듈 — IAppModule 탭으로 추가
- 법령 개정 감시 (korean-law-mcp의 ordinance_radar 개념 — 상위법 개정 시 조례 미정비 경고)

## 7. 새 대화 시작 프롬프트 (복사해서 사용)

```
저장소 bomi940718/seoul의 브랜치 claude/korean-law-review-automation-fkmgrh에서 진행 중인
프로젝트를 이어서 개발해줘.

[중요 — 코드가 이미 존재함]
lawreview/ 폴더에 C# 솔루션이 이미 커밋되어 있어 (엔진 LawReview.Core, WinForms 앱
LawReview.App, 테스트 19건). 처음부터 새로 만들지 말고, 반드시 lawreview/HANDOFF.md(인수인계
문서)와 lawreview/README.md를 먼저 읽은 뒤 그 위에서 이어가.

[목표]
건축 인허가 법규검토서 자동화 도구. 대지조건과 면적표를 입력하면 → 법제처 Open API에서
현행 법령·조례 원문을 조회해 항목별 적용/해당없음/확인필요를 판정하고 → 실무 검토서와
동일한 구성(면적표→설계개요→검토법규→요약 검토표→장별 상세검토)의 검토서.docx를 출력한다.
협력체도 같이 쓸 거라 WinForms exe로 배포한다. git은 작업용이고 최종 산출물은 내 PC의
C:\Tools에 둔다 (내가 git pull로 받아감).

[현재까지 완료 — 전부 git에 커밋되어 있음]
- 1단계: 엔진+WinForms 뼈대 (법제처 API 클라이언트, 정량계산, 체크리스트 엔진,
  Claude 판정, DOCX 생성, API 키 설정 화면)
- 2단계: 검토 항목 54개 확충, 별표 조회, 가지번호 조문, 조례 제목 키워드 탐색
- 실 API 검증 완료 (OC 키 lawreview): 4종 응답 실구조에 맞게 파서 보정, 실응답 픽스처
  테스트 커밋, 둔곡 E2E로 검토서 생성·원본 대조 성공 (CLI 러너: src/LawReview.Cli)
- 3단계 1차: 서울도시공간포털 지구단위계획 수집기 (후보 구역 검색 + 고시문 PDF 다운로드,
  ReviewEngine의 district_unit_plan 항목에 인용 연동)
- 테스트 41건 통과

[지켜야 할 원칙 — HANDOFF.md 2절에 상세]
- 개발은 반드시 C# (.NET 8). 파이썬 쓰지 마
- 검토서에 인용되는 조문은 법제처 API 현행 원문만
- 토지이음은 용도지역·관련 모법 색인 용도만. 개략 검토 내용은 검토서에 인용 금지
- 숫자 계산은 코드가, AI는 적용 여부 판정만
- 조례는 조문번호 하드코딩 금지 (지자체마다 다름 — 제목 키워드 탐색, 테스트로 강제됨)

[이번 세션에서 할 일]
HANDOFF.md 6절(다음 작업)에서 이어서: 고시문 PDF 텍스트 파싱, 4단계(토지이음 색인),
5단계(WinForms 화면 확인·배포) 중 우선순위대로.
작업 결과는 같은 브랜치에 커밋·푸시해줘.
```

## 8. 참고 링크

- 법제처 Open API 신청: https://open.law.go.kr (OC = 가입 이메일 @ 앞부분, 승인 1~2일)
- 발급 확인: `https://www.law.go.kr/DRF/lawSearch.do?OC=아이디&target=law&type=JSON&query=건축법` → JSON 나오면 정상
- korean-law-mcp: https://github.com/chrisryugj/korean-law-mcp
- 서울도시공간포털 (지구단위계획 열람): https://urban.seoul.go.kr
- 토지이음: https://www.eum.go.kr
- Anthropic API 키: https://console.anthropic.com (Claude 키는 과금 위험 — 세션·저장소에 올리지 말 것)
