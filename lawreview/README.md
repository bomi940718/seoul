# 법규검토서 생성기 (LawReview)

건축 인허가 법규검토서를 자동 생성하는 도구.
대지 조건과 면적표를 입력하면 법제처 현행 법령·조례 원문을 근거로 항목별 적용 여부를 판정하고,
실무 검토서 구성의 **검토서.docx**를 출력한다.

## 설계 원칙

1. **조문 인용은 법제처 Open API 원문만.** 토지이음 등 개략 검토 텍스트는 검토서에 들어가지 않는다.
2. **숫자는 코드가, 판단만 AI가.** 건폐율·용적률·주차대수 산정식은 결정적 코드로 계산하고,
   Claude는 조문 원문을 근거로 "적용/해당없음/확인필요" 판정과 사유만 답한다.
3. **조례는 항상 해당 지자체 것만.** 체크리스트의 `{시}`/`{구}` 자리표시자가 프로젝트 주소의
   지자체명으로 치환되므로, 다른 도시 조례가 검토서에 섞이는 사고(실무 검토서에서 실제 발견된 유형)가
   구조적으로 불가능하다.
4. **자동 판정이 불가능한 것은 정직하게 "확인필요".** 지구단위계획 지침도(도면) 규제 등.

## 구성

```
lawreview/
├─ src/LawReview.Core/        검토 엔진 (라이브러리)
│  ├─ Models/                 프로젝트 입력 (설계개요·면적표·법정한도)
│  ├─ LawApi/MolegClient.cs   법제처 국가법령정보 Open API 클라이언트
│  ├─ Review/                 정량 계산기 · 체크리스트 · 검토 오케스트레이터
│  ├─ Ai/                     Claude 판정 (Anthropic Messages API)
│  ├─ Report/                 검토서 DOCX 생성 (OpenXML)
│  └─ checklists/standard.json  검토 항목 정의 (근거 법령·판정 방식)
├─ src/LawReview.App/         WinForms 앱 (배포용 exe)
│  └─ Modules/                탭 모듈 — IAppModule 구현으로 기능 추가 (추후 CAD 변환 등)
└─ tests/LawReview.Core.Tests/  실제 실무 검토서(둔곡, 2023) 수치 기반 검증
```

## 검토서 생성 흐름

```
입력 (프로젝트 정보 + 면적표 + 법정한도)
  → 정량 계산  (건폐율·용적률·주차 산정식)          … QuantitativeCalculator
  → 근거 조문 조회 (법제처 현행 원문, 세션 캐시)      … MolegClient
  → 항목별 판정  (정량=코드 / 정성=Claude / 수동)     … ReviewEngine + IJudgmentProvider
  → 검토서.docx  (면적표→설계개요→검토법규→요약표→장별 상세)  … DocxReportBuilder
```

## 빌드·실행

```bash
dotnet test                      # 엔진 테스트
dotnet build src/LawReview.App   # WinForms 앱 빌드
```

협력체 배포용 단일 exe (Windows):

```bash
dotnet publish src/LawReview.App -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## 사용 준비 (각 사용자)

| 키 | 발급처 | 비용 | 용도 |
|---|---|---|---|
| 법제처 Open API (OC) | [open.law.go.kr](https://open.law.go.kr) → Open API 사용 신청 | 무료 | 조문 원문·시행일자 조회 |
| Claude API | [console.anthropic.com](https://console.anthropic.com) | 호출당 과금 | 적용/해당없음 판정 |

키는 앱의 **설정 탭**에 입력하며 해당 PC(`%APPDATA%\LawReview`)에만 저장된다. exe에 키를 심어 배포하지 않는다.

## 로드맵

- [x] 1단계: 엔진 + WinForms 뼈대 (정량 계산·법제처 연동·판정·DOCX)
- [x] 2단계: 검토 항목 확충 — 실무 검토서 전체 장 커버(제4~7장·녹색건축·인증·장애인편의·주차장법 등 50여 항목),
      별표·서식 조회(원문 링크 인용), 가지번호 조문("제48조의2") 지원,
      조례 조문을 제목 키워드로 탐색(지자체별 조문번호 차이 대응)
- [x] 3단계(1차): 지자체 문서 수집 — 서울도시공간포털 지구단위계획 후보 구역 검색·고시문 PDF
      다운로드(`Municipal/`), 검토서 `district_unit_plan` 항목에 원문 링크 인용 (판정은 확인필요 유지)
- [ ] 3단계(2차): 고시문 PDF 텍스트 파싱, 타 지자체 제공자 추가
- [x] 4단계: 토지이음 색인 — VWorld로 지번 주소 → PNU → 용도지역·지구 자동조회(`LandUse/`),
      앱 "자동조회" 버튼으로 지역/지구 입력란 채움 (이름 색인만, 개략 검토 내용 인용 안 함 — 원칙 2)
- [x] 5단계: 배포 — 자가포함 단일 exe(체크리스트 내장, 약 70MB), 협력체 안내([DEPLOY.md](DEPLOY.md)),
      빌드 스크립트([build-exe.bat](build-exe.bat)). AI 판정은 Claude 키 없이도 "확인필요"로 진행 가능
- [ ] 이후: 토지이용계획확인원 CAD 변환 모듈 (IAppModule로 탭 추가)
