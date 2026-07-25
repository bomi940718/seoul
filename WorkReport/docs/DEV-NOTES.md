# 개발 노트 — 지시서(v2) 대비 확정 사항

지시서: `Excel 워크리포트 분리 애드인 개발 지시서 (v2 — HTML 출력 확정판)`
샘플: `2025.12_SOWOOZOO_ARCHITECTS_Baek_Bomi_V1.0.xlsx` (개인정보 포함 → 저장소에 커밋하지 않음)

## 1. 실측 열 구성 (지시서 3.1과 다름 — 사용자 보고 완료)

샘플 파일 실측 결과, A열이 비어 있고 지시서 표 대비 A~G 구간이 한 칸씩 밀려 있다.

| 내용 | 지시서 | 실측 |
|---|---|---|
| 주차 | A | B |
| DAY | B | **C** |
| 메일 접수/발송 | C | D |
| Schedule | D | E |
| Regiment / Outsider | E / F | F / G |
| PROJECT명 / 넘버 / Descriptions / 여부 / 실행계획 / 비고 | H~M | H~M (동일) |

**대응**: 열 인덱스를 하드코딩하지 않고 헤더 행에서 "DAY" 셀을 앵커로 탐지한 뒤
DAY 기준 상대 오프셋(+1~+10)을 기본값으로, 헤더 텍스트(메일/Schedule/담당/PROJECT/여부/실행/비고)와
서브헤더(Regiment/Outsider)로 보정한다. → `JournalParser.BuildColumnMap`

## 2. 헤더 구조 실측

- 2행 제목, **4행 헤더**(DAY=C4), 5행 서브헤더(Regiment/Outsider), 데이터는 7행부터
- `The person in charge` F4:G4 병합, `PROJECT` H4:I5 병합
- 날짜: 날짜 서식 숫자(시리얼), 표시형식 `yyyy.mm.dd(aaa)`
- 시트명 `2026`이지만 2025-12월 데이터 포함 (시트명은 조회 키일 뿐)

## 3. 매칭 키 정규화 확장 (지시서: Trim + 대소문자 무시)

실측 I열 키에 **셀 내 줄바꿈 포함 값** 존재: `General\nManagement`, `평택 방축리 427\n용도변경`.
→ 내부 연속 공백·줄바꿈을 단일 공백으로 정규화하는 규칙 추가. → `KeyNormalizer`

## 4. 지시서에 추가된 요구사항

- **저장 시 자동 갱신**: `Application.WorkbookAfterSave` 이벤트 구독.
  저장된 통합문서가 설정의 "내 일지 xlsx"일 때만 리포트 갱신 자동 실행.
  로컬 설정 `autoRefreshOnSave` (기본 false). 자동 실행 시 결과 팝업 대신 조용한 알림.
- 여부 필터에 "미기재" 옵션 추가 (실측: 빈값 31건 존재).

## 5. 검증 기준선 (샘플 파일, 2026 시트)

`dotnet run --project tools/ParseCheck -- <샘플.xlsx> 2026` 기대값:

- 헤더 행 4, 열 매핑 DAY=3 … 비고=13
- 레코드 741건 (날짜만 있고 내용 없는 행 23건 스킵), 날짜 상속 3건
- 기간 2025-12-19 ~ 2026-08-29
- 여부: X 356 / O 297 / △ 57 / 빈값 31
- I열 정규화 키 16종 (일반관리 194, 개인관리 148, …)

## 6. 빌드 환경

- `WorkReport.Core`(netstandard2.0) + 테스트 + ParseCheck 는 Linux/.NET 8 SDK에서 빌드·실행 가능
- Excel-DNA 애드인(net48)·WPF 창·ExcelDnaPack 패킹은 Windows에서 최종 빌드 (README 절차 참조)

## 7. 4단계(애드인) 실측 확정 사항

- **이 PC의 Excel은 32비트(x86)** (16.0 ClickToRun, `C:\Program Files (x86)`). 지시서의
  "AddIn64.xll 단일" 전제와 달리 32/64비트 xll을 **모두** 생성하도록 함
  (`publish\WorkReport-AddIn-packed.xll` = 32비트, `...64-packed.xll` = 64비트).
  설치 시 각 PC Excel 비트에 맞는 파일을 등록할 것.
- ExcelDna.AddIn 1.9.0의 **자동 생성 .dna에는 참조 어셈블리가 포함되지 않아**
  packed xll에 의존성이 누락됨 → 프로젝트의 `WorkReport-AddIn.dna` 커스텀 템플릿에
  ClosedXML 계열 등 14개 참조를 `Pack="true"`로 명시. **NuGet 의존성이 바뀌면 이 템플릿도 갱신할 것.**
- ClosedXML이 요구하는 `System.Memory 4.0.1.1` 등 버전 불일치 →
  `AutoGenerateBindingRedirects` + csproj의 `ExcelDnaCopyXllConfig` 타깃으로 `.xll.config` 생성.
  ExcelDnaPack이 이를 **CONFIG 리소스로 xll 내부에 내장**하므로 단일 파일 배포 유지됨.
- 검증 완료 (2026-07-18):
  - 32비트 packed xll을 실제 Excel `RegisterXLL`로 로드 → True, AutoOpen 로그 기록 확인
  - 애드인 어셈블리 직접 로드로 갱신 파이프라인 E2E 실행 → 기준선 일치
    (일반관리 194 / 개인관리 148 / BRANDING-1 41 / OFFLINE_DEC 60 / 평택 13, 미등록 11종,
    출력 루트 복사·경고 처리 정상)
- 리본의 [프로젝트 관리]·[설정] 버튼은 5단계 구현 전까지 안내 문구만 표시.

## 8. 5단계(WPF 창) 확정 사항

- 창은 XAML 없이 **코드로 구성**(기존 `ResultWindow` 방식 유지) — 애드인 어셈블리를 가볍게 두고
  Excel-DNA 패킹 대상 파일 수를 늘리지 않기 위함.
- `WindowHelper.ShowOverExcel`: `ExcelDnaUtil.WindowHandle`을 Owner로 지정해 창이 Excel 뒤로
  숨는 것을 방지. **세 창 모두 `ShowDialog`로만 띄울 것** — `OnSave`가 `DialogResult`를 대입한다.
- 폴더 선택은 WPF에 대화상자가 없어 `System.Windows.Forms.FolderBrowserDialog` 사용
  → csproj에 `UseWindowsForms=true` 추가.
- 프로젝트 관리 창의 미등록 키 스캔은 일지 파싱(수 초)이라 `Task.Run` +
  `TaskScheduler.FromCurrentSynchronizationContext()`로 UI 스레드에 반영.
  이를 위해 `RefreshService.ParseJournals`를 public으로 분리(갱신 파이프라인과 공용).
- 저장 시 정규화: 넘버·이름 Trim, 빈 그룹 → `ETC`, 빈/이상 상태 → `진행`,
  공백뿐인 개별 출력 폴더 → `null`. 넘버 누락·중복(정규화 기준)은 저장 차단.
- `HasExternalChange()` 감지 시 [덮어쓰기 / 재로드] 선택 대화상자 — 락은 여전히 사용하지 않음.

### 검증 방법 (2026-07-25)

Excel 없이 창을 검증하려면 **스크래치패드 하네스**(저장소 미포함, net48 WinExe,
`WorkReport.AddIn.csproj`를 ProjectReference)를 만들어 창을 화면 밖(-4000,-4000)에 실제로
`Show()`한 뒤 `RenderTargetBitmap`으로 PNG 캡처했다. Loaded 이벤트(스캔)까지 실동작한다.

- 주의 1: WinExe는 콘솔이 없어 `Console.WriteLine`이 보이지 않는다 → 파일 로그 사용.
- 주의 2: GUI 앱은 PowerShell `&`가 **대기하지 않는다** → `Start-Process -Wait` 필요.
- 주의 3: PowerShell에서 WPF를 직접 호스팅하면 `AssemblyResolve` 훅 때문에
  StackOverflow가 나므로, 위 하네스 방식이 안정적이다.
- 결과: 프로젝트 5건 로드·상태 콤보·미등록 키 11종(레코드 741건)이 ParseCheck 기준선과 일치.
  저장 경로는 정규화 결과까지 projects.json에서 확인.

### 빌드 주의

애드인이 Excel에 로드된 상태에서는 `publish\*-packed.xll`이 잠겨 패킹이 실패한다.
Excel을 닫거나 `-p:RunExcelDnaPack=false`로 컴파일만 검증할 것.
