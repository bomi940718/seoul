# 법규검토서 생성기 — 작업 인수인계 문서

> 새 대화(세션)에서 이 프로젝트를 이어갈 때 이 문서부터 읽을 것.
> 마지막 업데이트: 2026-08-16 / 브랜치: `claude/korean-law-review-automation-fkmgrh`
>
> **분석 문서 2개를 반드시 함께 읽을 것** (여기 요약된 것보다 훨씬 상세하다):
> - `docs/report-standard-analysis.md` — 8년 사용 실무 표준 서식(HWP)의 순서·표 구조.
>   **이 순서는 협력사와 공유하는 서식이라 임의로 바꾸면 안 된다.**
> - `docs/usechange-excel-analysis.md` — 용도변경 가이드 엑셀 분석(다음 큰 작업)

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
| UI | **HTML** (WebView2 창, 런타임 없으면 브라우저 폴백) | 검토서와 같은 표를 그려야 함. 엔진은 C# 유지, 배포는 여전히 exe 1개 |
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

### 지구단위계획 결정도서 직접 등록 (2026-07-26)

포털 수집기가 없는 지자체(서울 외 전부)를 사용자가 파일로 보완하는 경로.
**출처 우선순위 규칙이 이 기능의 핵심**이니 안양시 등 수집기를 추가할 때 이 규칙을 깨지 말 것:

| 상황 | 동작 | 검토서 표기 |
|---|---|---|
| 파일 등록함 | 그 파일만 인용, **포털 조회 생략** | `지구단위계획 (직접 등록)` |
| 미등록 + 수집기 있음(서울) | 포털 후보 자동 조회 | `지구단위계획` |
| 미등록 + 수집기 없음 | 체크리스트 수동 안내 유지 | — |

- 모델: `ProjectInput.DistrictPlanFiles` (`DistrictPlanFile`: 경로 + 구역명·고시번호·고시일자, 모두 선택)
- 엔진: `ReviewEngine.AddUploadedDistrictPlanCitations` — 파일이 실제로 없으면 경고 문구를 함께 남김.
  구역명 미입력 시 파일명으로 대체. **어느 경로든 판정은 확인필요 유지**(원칙 5)
- UI: 면적표 아래 "지구단위계획 결정도서" 패널 — 파일 등록(다중 선택) 시 구역명·고시번호를 물어보고 목록 표시
- 실검증: 대전(수집기 없는 지역) 프로젝트에 안양 고시문 등록 → 검토서에 `지구단위계획 (직접 등록)
  평촌지구 지구단위계획구역 / 안양시 고시 제2024-15호 / 파일 경로`로 인용됨. 49항목 정상, 오류 0건

### HTML UI 전환 (2026-08-09) — 지금의 화면 구조

WinForms 폼을 버리고 **화면만 HTML로** 바꿨다. C# 엔진(LawReview.Core)은 그대로 재사용한다.
인터넷 없이 동작한다(127.0.0.1 바인딩, 화면 파일은 어셈블리에 내장).

```
LawReview.App (셸)  ─ WebView2 창으로 표시. 런타임 없으면 기본 브라우저로 폴백
   └ LawReview.Web  ─ Kestrel을 빈 로컬 포트에 띄우고 wwwroot(HTML/CSS/JS) + API 제공
        └ LawReview.Core ─ 엔진(변경 없음). 테스트 65건이 이 계층을 지킨다
```

- 예전 화면이 필요하면 `LawReview.App.exe --winforms`
- 화면 파일: `src/LawReview.Web/wwwroot/{index.html, app.css, app.js}`
- API: `/api/settings`, `/api/lookup`(주소 자동조회), `/api/overview`(정량계산),
  `/api/review/start?stage=basic|detail|all` + `/api/review/{id}`(진행 폴링),
  `/api/projects[/{name}]`(저장/불러오기)

**탭 = 검토서 페이지 구성**: 설계개요(p3) · 검토법규(p4) · 요약 검토서(p5) · 인증 의무(p6) ·
지구단위계획 지침(p6) · 해당 지번 주요 법규(p7) · 장별 상세검토(별도 실행)

**검토는 2단계로 나뉜다** — 계약 전에는 주요 법규까지만 보므로 기본 검토(`basic`)는 18항목만
돌려 AI 호출 비용을 줄이고, 장별 상세는 전용 버튼으로 따로 실행한다(`detail`).

**설계개요 표의 칸은 3종**이며 이 구분을 깨면 안 된다(실제로 버그가 났던 부분):
`input`(사람 입력) / `derived`(엔진이 채우는 표시 전용) / `param`(기준값 입력 + 결과 표시 분리).
예전에는 계산 결과가 사용자 기준값 칸을 덮어써서 그 문자열이 다시 입력으로 먹히는
되먹임 버그가 있었다(조경 18,181㎡·주차 0대).

**입력은 자동 저장된다** — 변경 시 localStorage 스냅샷, 이름 저장은
`%APPDATA%\LawReview\projects\*.json`.

### 성능 실측 (둔곡 기준, 2026-08-09)

| 작업 | 시간 |
|---|---|
| 주소 자동조회(VWorld 3콜) | **0.07~0.23초** |
| 기본 검토 18항목 (claude-sonnet-5) | **91초** |
| 전체 54항목 | 4분 내외 |

### 법 위계 — 모든 법규에 적용되는 대원칙 (2026-08-09, 사용자 확인)

법은 **모법 → 광역(도) → 기초(시·군)** 순으로 위임되고, **적용할 때는 그 역순**으로
가장 구체적인 것부터 본다. 주차장에만 해당하는 규칙이 아니라 **조경·장애인편의 등
모든 법규에 동일하게 적용**된다.

```
① 기초 조례(안양시 주차장 조례)에 그 용도 규정이 있으면 → 적용
② 없으면 광역 조례(경기도 주차장 조례)  ← 도는 상세 기준을 안 두는 경우가 많지만 단계는 건너뛰지 않는다
③ 없으면 모법(주차장법 시행령 별표 1)
④ 모법 별표에도 그 용도가 없으면 → 그 별표의 마지막 항목 "그 밖의 건축물"
```

- 지자체법은 모법에 따라 가므로 **모법에 항목이 없을 수는 없는 구조**다.
- 근린생활시설처럼 지자체가 따로 적지 않는 용도가 흔하다 → ②③④로 내려간다.
- 구현: `Municipality.OrdinanceHierarchy`(기초→광역), `ParkingStandardResolver.ResolveChain`.
  **다른 법규를 자동화할 때도 이 순서를 그대로 따를 것.**
- 법제처 API에서 법령해석까지 받아둔 이유가 이런 판단 때문이다. **법은 틀리면 안 된다.**

### 조례 별표(HWP) 읽기 — 해결됨 (2026-08-16)

지자체 조례의 별표(부설주차장 설치기준 등)는 법제처 API가 내용을 주지 않고 HWP 첨부
링크만 준다. `HwpTextExtractor`(CFB + zlib + HWPTAG_PARA_TEXT)로 직접 읽는다.

**두 개의 함정이 있었고 둘 다 해결했다 — 다시 만들지 말 것:**

1. **User-Agent 없으면 HWP가 아니라 HTML이 온다.** 법제처 flDownload는 UA가 비면
   5.7KB짜리 안내 페이지를 돌려준다. HttpClient는 기본 UA가 없어 매번 빈손이었고,
   그 결과 조용히 모법 기준으로 떨어졌다. → `ApiEndpoints.CreateHttpClient()`에서 UA 지정.
   받은 바이트가 HWP가 아니면 건너뛰도록 방어도 넣었다.
2. **조례 별표는 용도명이 여러 줄에 걸친다.** ("2. 문화 및 집회시설" 다음 줄에
   ", 판매시설, 의료시설…") → 기준줄("○ …") 전까지 이어붙여 용도명을 완성한 뒤 분해.

실검증(모두 조례 값으로 정확히 조회됨):
대전 유성구/공장 200㎡·대 (단서 "산업단지 450㎡"까지) · 속초시/판매시설 150㎡·대 ·
안양시/공장 250㎡·대. 실무 검토서(둔곡)의 200㎡·대와 일치한다.

### 법 위계를 적용한 법규 (진행 상황)

| 법규 | 자동조회 | 출처 |
|---|---|---|
| 건폐율·용적률 | ✅ | 도시계획조례 "용도지역 안에서의 건폐율/용적률" 조문 |
| 부설주차장 기준 | ✅ | 주차장 조례 별표(HWP) → 없으면 주차장법 시행령 별표 1 → "그 밖의 건축물" |
| 조경면적 비율 | ✅ | 건축조례 "대지의 조경" 조문 (연면적 구간별, "100분의 15"/"15퍼센트" 양쪽 표기) |
| 장애인편의·대지 안의 공지 등 | ⬜ 미적용 | 같은 패턴으로 붙이면 된다 |

패턴: `Municipality.OrdinanceHierarchy(province, city)`로 기초→광역 순서를 얻고,
각 단계에서 조문 또는 별표를 파싱한 뒤 없으면 다음 단계로 내려간다.
조경처럼 값이 연면적·규모에 따라 갈리면 **구간표를 화면에 넘겨** 재조회 없이 고르게 한다.

## 5. 현재 상태의 한계 (다음 세션이 제일 먼저 알아야 할 것)

- **검토 결과가 읽기 전용이다.** AI 판정이 틀려도 화면에서 못 고친다(6절 1번).
- 장애인편의·대지 안의 공지 등은 아직 법 위계 자동조회가 안 붙었다.
- 지구단위계획은 **후보 구역 목록**까지만 자동(법정동 키워드). 필지→구역 정확 대응은
  지도(WFS) 연동이 필요하다. 서울 외 지자체 포털은 미구현 — 직접 파일 등록으로 커버 중.
- VWorld 자동조회는 **지번 주소만** 인식한다(도로명 미지원).
- 자동조회는 조례·별표를 여러 건 받아오므로 **5~7초** 걸린다(정상).
- 인쇄용 CSS가 없다.

## 6. 다음 작업 (우선순위 순)

### 1. AI 판정 수정 경로  ← 사용자가 1순위로 요구
검토 결과가 읽기 전용이라 **틀린 판정을 사람이 고칠 수 없다.** 판정·사유를 화면에서
수정하고 그 값이 검토서(DOCX)로 나가야 한다. 편집은 셀에 갇히지 않게(현재 설계개요 방식 참고).

### 2. 남은 법규에 법 위계 적용
장애인편의·대지 안의 공지 등. 패턴은 이미 확립되어 있다(3절 표 참고) —
`Municipality.OrdinanceHierarchy` + 조문/별표 파싱 + 못 찾으면 다음 단계.

### 3. 인쇄/PDF 서식 정리
HTML이라 Ctrl+P로 되지만 인쇄용 CSS가 없다. 검토서와 같은 페이지 분할이 필요.

### 4. 용도변경 검토기 (별도 모드)
`docs/usechange-excel-analysis.md`를 먼저 읽을 것. 착수 시 사용자 확인 필요:
- 업종별 1일 오수발생량 원단위 (00.Reference의 계산기는 2019년판 — 최신본 필요)
- 건축물대장 공공 API 연동 방식 (용도변경은 자동, 신축은 수기 입력)

확정된 사항: 원인자부담금은 **(변경 − 기존)** 증가분 기준. VBA는 이식하지 않음.

### 5. 리모델링·증축 모드 데이터 연결
화면 탭·열 구성(변경전/변경후/법정)은 이미 있다. 흐름만 붙이면 된다.

### 이후
- 안양시 지구단위계획 수집기(`IDistrictPlanProvider` 추가) — 사용자 주 활동 지역
- 고시문 PDF 텍스트 파싱 → 결정조서·지침 본문 추출
- 법령해석 활용(받아둔 해석례를 판정 근거로) — 법 판단 정확도
- 토지이용계획확인원 CAD 변환 모듈 / 법령 개정 감시

## 7. 새 대화 시작 프롬프트 (복사해서 사용)

```
저장소 bomi940718/seoul의 브랜치 claude/korean-law-review-automation-fkmgrh에서 진행 중인
프로젝트를 이어서 개발해줘. 로컬은 C:\Tools\seoul 에 이미 clone되어 있어. 먼저 git pull 해줘.

[중요 — 코드가 이미 존재함. 처음부터 만들지 마]
먼저 아래 3개를 반드시 읽고 그 위에서 이어가:
  1. lawreview/HANDOFF.md                       ← 인수인계(여기부터)
  2. lawreview/docs/report-standard-analysis.md ← 실무 표준 서식(HWP) 순서·표 구조
  3. lawreview/docs/usechange-excel-analysis.md ← 용도변경 엑셀 분석(다음 큰 작업)

[현재 구조]
C# 엔진(LawReview.Core) + 로컬 웹호스트(LawReview.Web, HTML 화면) +
WebView2 셸(LawReview.App). 인터넷 없이 동작하고 배포는 exe 하나.
테스트 97건이 엔진을 지킨다. 실행: dotnet run --project lawreview/src/LawReview.App

[완료된 것]
법제처 실 API 검증, 주소 자동조회(VWorld: 지역지구·대지면적·지목·지자체),
지구단위계획(서울 포털 자동조회 + 직접 파일 등록), 실 Claude 판정,
HTML 화면 7개 탭(설계개요~장별 상세검토), 검토 2단계 분리(기본 18항목/상세),
프로젝트 저장·불러오기·자동저장, 자가포함 exe 배포

[절대 규칙]
- 개발은 C#(.NET 8). 파이썬 쓰지 마
- 검토서 조문은 법제처 API 현행 원문만
- 숫자 계산은 코드가, AI는 적용 여부 판정만
- 조례 조문번호 하드코딩 금지(제목 키워드 탐색)
- **검토서 항목 순서·표 구성은 8년 쓴 실무 표준이다. 임의로 바꾸지 마**
- 도면(지구단위계획 지침도) 규제는 자동 판정하지 말고 "확인필요"
- **법 위계: 기초 조례 → 광역 조례 → 모법 → 모법 별표의 "그 밖의 용도"**
  (주차장뿐 아니라 모든 법규에 적용. 법은 틀리면 안 된다)

[이번 세션에서 할 일]
HANDOFF.md 6절(다음 작업)에서 이어서 진행해줘.
작업 결과는 같은 브랜치에 커밋·푸시.
```

## 8. 참고 링크

- 법제처 Open API 신청: https://open.law.go.kr (OC = 가입 이메일 @ 앞부분, 승인 1~2일)
- 발급 확인: `https://www.law.go.kr/DRF/lawSearch.do?OC=아이디&target=law&type=JSON&query=건축법` → JSON 나오면 정상
- korean-law-mcp: https://github.com/chrisryugj/korean-law-mcp
- 서울도시공간포털 (지구단위계획 열람): https://urban.seoul.go.kr
- 토지이음: https://www.eum.go.kr
- Anthropic API 키: https://console.anthropic.com (Claude 키는 과금 위험 — 세션·저장소에 올리지 말 것)
