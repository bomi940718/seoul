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
