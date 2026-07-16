# WorkReport — Excel 워크리포트 분리 애드인

건축사사무소 2인이 각자 기록하는 일일 업무일지 xlsx 두 개를 병합해,
프로젝트별 **self-contained HTML 리포트**와 **index.html 대시보드**를 생성하는 Excel-DNA 애드인.

- 일지 xlsx는 순수 .xlsx 유지 (읽기 전용, 매크로 없음)
- HTML은 빌드 산출물: 실행할 때마다 전체 재생성(멱등), 외부 리소스 0 (오프라인 NAS에서 더블클릭)
- 로컬 임시폴더에 생성 후 NAS로 복사 (네트워크 경로 직접 쓰기 금지)

## 구성

| 경로 | 내용 | 대상 |
|---|---|---|
| `src/WorkReport.Core` | 파서(ClosedXML)·병합·HTML 렌더러·설정 | netstandard2.0 |
| `src/WorkReport.AddIn` | Excel-DNA 리본 + WPF 창 (예정) | net48 |
| `tests/WorkReport.Core.Tests` | 단위 테스트 (xunit) | net8.0 |
| `tools/ParseCheck` | 샘플 xlsx 파싱 검증·HTML 시안 생성 콘솔 | net8.0 |

## 개발 빌드 (Linux/Windows 공통)

```bash
cd WorkReport
dotnet build
dotnet test
# 파싱 검증 + HTML 시안 생성
dotnet run --project tools/ParseCheck -- <일지.xlsx> <시트명> --author 이름 --html <출력폴더>
```

## 배포 빌드 (Windows, 예정)

`WorkReport-AddIn64.xll` 단일 파일 (ExcelDnaPack 패킹) — 애드인 프로젝트 추가 시 절차 문서화 예정.
설치: xll을 `C:\Tools`에 복사 → 파일 속성에서 차단 해제(Mark of the Web) → Excel 추가기능 등록.

상세 스펙 확정 사항은 [docs/DEV-NOTES.md](docs/DEV-NOTES.md) 참조.
