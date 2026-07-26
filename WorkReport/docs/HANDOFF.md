# WorkReport 개발 이어가기 — 인수인계 프롬프트

> 새 세션 시작 시 이 문서 전체를 읽고, "현재 상태"를 검증한 뒤 "남은 작업" 4단계부터 진행할 것.

## 1. 최종 목표

건축사사무소 2인이 각자 기록하는 일일 업무일지 xlsx 2개를 병합해, 프로젝트별
**self-contained HTML 리포트 + index.html 대시보드**를 생성하는 **Excel-DNA 애드인(C#)**.

- 최종 산출물: `WorkReport-AddIn64.xll` 단일 파일 (ExcelDnaPack 패킹) + 설치 README
- 운영 플로우: [일상] 각자 일지 기록 → [갱신] 리본 버튼 1클릭(또는 저장 시 자동)
  → 병합 → %TEMP% 생성 → NAS 복사 → [조회] NAS에서 index.html 더블클릭
- 설치 위치: `C:\Tools\WorkReport` (두 PC 동일 설치, 설정만 각자)
- 원 지시서: "Excel 워크리포트 분리 애드인 개발 지시서 (v2)" — 세부 요구는 그 문서와
  `docs/DEV-NOTES.md`(실측으로 확정/변경된 사항)를 함께 따를 것

> **2026-07-26 기준: 1~6단계 전부 완료.** 아래 3장의 "남은 작업"은 모두 구현·검증됐다.
> 최종 산출물은 `dist\WorkReport-AddIn.xll`(32비트) / `dist\WorkReport-AddIn64.xll`(64비트),
> 설치·사용 안내는 `시작하기.md`, 4~6단계에서 확정된 사항은 `docs/DEV-NOTES.md` 7~8장 참조.

## 2. 완료된 것 (1~3단계 + 디자인 피드백 4회 반영)

브랜치 `claude/excel-dna-html-reports-wr7z6s`, 커밋 4개 푸시 완료. 테스트 25건 통과.

### src/WorkReport.Core (netstandard2.0 — ClosedXML, Newtonsoft.Json)
- `Parsing/JournalParser` — 시트명 해석(대괄호 허용), "DAY" 셀 앵커로 헤더 행 탐지
  + 헤더 텍스트로 열 보정(실측: 지시서보다 한 칸 밀림 → DEV-NOTES 참고),
  날짜 시리얼→DateTime, 빈 날짜 위 행 상속, 서브헤더/빈 행/날짜만 있는 행 스킵,
  FileShare.ReadWrite 읽기 전용
- `Parsing/KeyNormalizer` — Trim + 대소문자 무시 + **내부 줄바꿈·연속공백 → 공백 1개**
  (실측 I열 키에 줄바꿈 포함 값 존재)
- `Reporting/ReportBuilder` — 활성 프로젝트 병합, 날짜↑→본인→행순 정렬, 미등록 키 감지,
  파일명 `{넘버}_{이름}.html` (Windows 불가문자 치환)
- `Reporting/HtmlReportRenderer` — 임베디드 템플릿에 JSON 주입(EscapeHtml로 </script> 안전),
  WriteAll(전체 재생성, 멱등)
- `Config/LocalSettings` — 일지 경로 2개·작성자명 2개·공유설정폴더·출력루트·시트명·
  **autoRefreshOnSave**(저장 시 자동 갱신, 기본 false)
- `Config/ProjectRegistry` — {공유설정폴더}\projects.json, 타임스탬프 외부 변경 감지(락 없음)
- `Models/ProjectInfo` — number/name/active/outputDir + **group**(영어 대문자, 기본 ETC)
  + **status**(계획/진행/완료, 기본 진행)

### HTML 템플릿 (외부 리소스 0, 순수 JS)
- `report.html` — 헤더(넘버·이름·총건수·작성자별·갱신시각), 고정 필터바(텍스트/작성자/
  여부 O·△·X·미기재/기간/초기화), 행 = **`날짜(yyyy-MM-dd(요일)) | 작성자칩·담당기관·
  담당자·연락·시각·실행·비고 열 | 내용 열(Descriptions만)`** — 여부 배지 열은 삭제됨(필터는 유지),
  같은 날짜 첫 행만 날짜 표시, 3줄 초과 "더보기" 접기, 줄바꿈 <br>, URL 자동 링크
- `index.html` — 영어 그룹 탭·그룹별 섹션, 상태 토글(계획/진행/완료, **완료 기본 숨김**),
  카드(넘버·이름·총건수·O/△/X 필·마지막 기록) + **카드별 상태 셀렉터**
  (localStorage `workreport.status.{number}` 저장 — PC별 개인 설정, projects.json 값이 기본)

### 검증 도구/기준선
- `tools/ParseCheck` — 파싱 검증 + `--html` 시안 생성 콘솔
- 샘플(2025.12_SOWOOZOO_..._V1.0.xlsx, 시트 2026) 기준선: 헤더 4행, DAY=C열,
  레코드 741건, 날짜 상속 3건, 여부 X356/O297/△57/빈31, 정규화 키 16종
- `dotnet test` → 25건 전부 통과 상태가 정상

## 3. 남은 작업 → **모두 완료 (2026-07-26)**

### 4단계 — Excel-DNA 애드인 (src/WorkReport.AddIn, net48)
- ExcelDna.AddIn NuGet, customUI 리본 탭 `[워크리포트]`: **리포트 갱신 / 대시보드 열기 /
  프로젝트 관리 / 설정 / 로그 열기**
- 리포트 갱신 파이프라인: 활성 통합문서 저장(Excel COM은 이 용도만) → projects.json 로드
  → 일지 2개 파싱(실패 시 1초 간격 3회 재시도 후 스킵+경고) → %TEMP%\WorkReport 생성
  → NAS 출력루트로 File.Copy(overwrite) (접근 불가 시 로컬 경로 안내, 예외 중단 금지)
  → 결과 요약 창(프로젝트별 본인/협업자 행 수, 경고, [대시보드 열기] 버튼)
- **저장 시 자동 갱신**: Application.WorkbookAfterSave 구독, 저장 파일 == 설정의 내 일지일 때만,
  autoRefreshOnSave=true일 때만, 결과창 없이 조용히 실행
- 로그: %LOCALAPPDATA%\WorkReport\logs\yyyyMMdd.log (전 과정 기록)

### 5단계 — WPF 창
- 프로젝트 관리: 그리드(넘버|이름|**그룹**|**상태(계획/진행/완료)**|활성|개별출력폴더(선택)),
  추가/수정/삭제, 미등록 키 감지 목록(클릭 → 등록 폼 프리필), 저장 시 타임스탬프 충돌 경고
- 설정 창: LocalSettings 전 항목 + autoRefreshOnSave 체크박스

### 6단계 — 마무리
- 예외 처리 정리, ExcelDnaPack으로 `WorkReport-AddIn64.xll` 패킹
- README: C:\Tools 복사, Excel 추가기능 등록, Mark of the Web 차단 해제, 두 PC 설치

## 4. 원칙 (지시서 7장 금지사항 + 추가)

- 소스 xlsx에 쓰기 금지 / NAS 경로 직접 스트림 쓰기 금지(로컬 생성→복사) /
  HTML 증분 수정 금지(항상 전체 재생성) / 외부 리소스(CDN·웹폰트) 금지 /
  열 인덱스·시트명·경로 하드코딩 금지 / docx 생성 없음
- **개인정보 커밋 금지**: 샘플 xlsx와 실데이터로 생성한 HTML은 저장소에 넣지 않는다
- 커밋: `Claude <noreply@anthropic.com>`, 한국어 커밋 메시지 유지
- 그룹/상태는 HTML(및 projects.json)에서만 동작 — 엑셀 일지 서식은 절대 변경하지 않음

## 5. 시작 절차 (새 세션 체크리스트)

1. `C:\Tools\WorkReport`(= 저장소 WorkReport/ 폴더와 동일)에서 `dotnet test` → 25건 통과 확인
2. 샘플 xlsx로 `ParseCheck` 실행 → 위 기준선과 일치 확인
3. ~~4단계 착수~~ → **완료**. 이후 작업은 `docs/DEV-NOTES.md` 7~8장의 확정 사항을 먼저 읽을 것
   (Excel 비트, 커스텀 .dna 패킹 목록, 바인딩 리다이렉트, DialogResult 금지, 창 검증 방법)
4. 각 단계 완료 시 사용자 확인 요청 (지시서 6장 원칙)

## 6. 현재 상태 (2026-07-26)

- 커밋 7개 푸시 완료, 테스트 25건 통과, 최종 xll 2종 `dist\` 배포
- 검증 완료: packed xll을 Excel `RegisterXLL`로 로드 → 성공 / 갱신 파이프라인 E2E 기준선 일치
  (레코드 741건, 일반관리 194·개인관리 148·BRANDING-1 41·OFFLINE_DEC 60·평택 13, 미등록 11종)
  / 프로젝트 관리·설정·결과 창 실동작 캡처 확인 / projects.json 저장 정규화 확인
- **아직 하지 않은 것**: 실제 Excel 리본에서 버튼을 눌러 두 사람 일지로 NAS에 생성하는
  현장 운영 테스트(협업자 일지 경로·NAS 권한 포함). 두 PC 설치 후 1회 수행 권장.
