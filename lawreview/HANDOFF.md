# 법규검토서 생성기 — 작업 인수인계 문서

> 새 대화(세션)에서 이 프로젝트를 이어갈 때 이 문서부터 읽을 것.
> 마지막 업데이트: 2026-07-18 / 브랜치: `claude/korean-law-review-automation-fkmgrh`

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

### 테스트 — 19건 전부 통과

검증 기준이 **실제 실무 검토서(둔곡)의 수치**: 연면적 2,484.43㎡(PIT 제외), 건폐율 24.10%, 법정 건축면적 4,221.07㎡, 주차 12.42→12대, 장애인 0.36→1대.

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
   ├─ src/LawReview.Core/
   ├─ src/LawReview.App/
   └─ tests/LawReview.Core.Tests/
```

사용자 로컬: `C:\Tools`에 clone해서 사용. 개발 푸시 후 사용자가 `git pull`.

```
cd C:\Tools
git clone -b claude/korean-law-review-automation-fkmgrh https://github.com/bomi940718/seoul.git
cd seoul\lawreview
dotnet test && dotnet run --project src\LawReview.App
```

## 5. 현재 상태의 한계 (다음 세션이 제일 먼저 알아야 할 것)

- **법제처 API를 실제 키로 호출해본 적이 없다.** 개발은 공식 API 문서·참조 구현 기반. 실제 응답 JSON 구조가 예상과 다를 수 있음 (특히 자치법규 응답의 배열 키, 조문 구조). 사용자가 OC 키를 받으면 **가장 먼저 실 API 엔드투엔드 검증**을 하고 MolegClient 파싱을 실데이터에 맞게 수정할 것.
- Claude 판정도 실 호출 미검증 (프롬프트·파싱 로직만 테스트됨).
- WinForms는 리눅스에서 컴파일 검증만 (실행 화면 미확인 — 윈도우 필요).
- 체크리스트의 행정규칙(에너지절약설계기준) admrul 조회는 응답 구조가 법령과 다를 가능성 높음 — 실검증 필요.

## 6. 다음 작업 (우선순위 순)

### 즉시: 실 API 검증 (사용자가 법제처 OC 키 제공 시)

1. OC 키로 `lawSearch/lawService` 실호출 → 법령·자치법규·행정규칙·별표 4종 응답 구조 확인
2. MolegClient 파싱 수정 (특히: 검색 응답의 배열 키 이름, 자치법규 필드명, 조문 "항"/"호"/"목" 중첩 실구조)
3. 둔곡 프로젝트 입력으로 전체 파이프라인 실행 → 생성된 검토서를 원본 검토서와 대조
4. Claude API 실호출 검증 (사용자 키는 세션에 받지 말고, 판정 프롬프트가 잘 동작하는지는 이 세션의 Claude로 시뮬레이션 가능)

### 3단계: 지자체 문서 수집 (지구단위계획)

- 서울도시공간포털(urban.seoul.go.kr)부터: 필지 → 지구단위계획구역 식별 → 결정도서(고시문·조서·지침) PDF 다운로드 → 텍스트 파싱 → `district_unit_plan` 항목에 반영
- 도면(지침도) 규제는 파싱 불가 → "확인필요" + 원본 링크 (원칙 5)
- 크롤러는 시별 모듈 구조로 (서울 먼저, 확장 가능하게)
- 심의기준·경관가이드도 같은 파이프라인 재사용 (소스 URL 등록 방식)

### 4단계: 토지이음 색인 연계

- 주소 → 용도지역·지구 + 관련 모법 목록 자동 조회 (공공데이터포털 토지이용계획 API 검토)
- **색인만.** 개략 검토 내용 인용 금지 (원칙 2)

### 5단계: 마무리·배포

- WinForms 실행 화면 다듬기 (윈도우에서 확인), 입력 검증 강화
- exe 배포 + 협력체용 안내 문서 (키 발급 절차 포함)
- 검토서 서식 세부 조정 (사용자 피드백 반영)

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
- 1단계: 엔진+WinForms 뼈대 (법제처 API 클라이언트, 건폐율·용적률·주차 정량계산,
  체크리스트 엔진, Claude 판정, DOCX 생성, API 키 설정 화면)
- 2단계: 검토 항목 54개 확충(실무 검토서 전체 장 커버), 별표 조회, 가지번호 조문,
  조례는 제목 키워드로 조문 탐색
- 테스트 19건 통과 (실제 실무 검토서(대전 둔곡, 2023) 수치가 검증 기준)
- 한계: 법제처 API를 실제 키로 호출해본 적이 아직 없음

[지켜야 할 원칙 — HANDOFF.md 2절에 상세]
- 개발은 반드시 C# (.NET 8). 파이썬 쓰지 마
- 검토서에 인용되는 조문은 법제처 API 현행 원문만
- 토지이음은 용도지역·관련 모법 색인 용도만. 개략 검토 내용은 검토서에 인용 금지
- 숫자 계산은 코드가, AI는 적용 여부 판정만
- 조례는 조문번호 하드코딩 금지 (지자체마다 다름 — 제목 키워드 탐색, 테스트로 강제됨)

[이번 세션에서 할 일]
법제처 OC 키: ______ (open.law.go.kr 발급 완료)
1. 실 API 엔드투엔드 검증부터: 법령·자치법규·행정규칙·별표 4종 응답의 실제 구조를 확인하고
   MolegClient 파싱을 실데이터에 맞게 수정. 둔곡 프로젝트 입력으로 전체 파이프라인을 돌려
   검토서를 생성하고 원본 검토서와 대조
2. 검증 끝나면 3단계: 서울도시공간포털(urban.seoul.go.kr) 지구단위계획 결정도서 수집기
작업 결과는 같은 브랜치에 커밋·푸시해줘.
```

- 키가 아직 없으면 → "할 일" 1을 빼고 3단계(크롤러)부터 시작해도 됨 (API 키 불필요)

## 8. 참고 링크

- 법제처 Open API 신청: https://open.law.go.kr (OC = 가입 이메일 @ 앞부분, 승인 1~2일)
- 발급 확인: `https://www.law.go.kr/DRF/lawSearch.do?OC=아이디&target=law&type=JSON&query=건축법` → JSON 나오면 정상
- korean-law-mcp: https://github.com/chrisryugj/korean-law-mcp
- 서울도시공간포털 (지구단위계획 열람): https://urban.seoul.go.kr
- 토지이음: https://www.eum.go.kr
- Anthropic API 키: https://console.anthropic.com (Claude 키는 과금 위험 — 세션·저장소에 올리지 말 것)
