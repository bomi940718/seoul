# Cadastral MCP 사용 지침서

> 토지이용계획확인원 조회 + 지적도 DXF(캐드) 내보내기 도구
> 설계 전 **땅튀기기(가설계 사전 검토)** 용도
> 원본: https://github.com/chanjong-ui/cadastral-mcp

---

## 1. 이 도구가 하는 일

브이월드(국토교통부) Open API를 이용해, Claude Desktop 대화창에서 **말로 시키는 것만으로**:

- 지번 주소로 **토지대장·용도지역·공시지가**를 통합 조회
- 필지 경계를 **DXF(캐드 파일)**로 내보내기
- 표 + 지적도가 한 장에 들어간 **토지 리포트 DXF** 생성
- 건축법·국토계획법 등 **국가법령 조문** 검색/조회

> ⚠️ 결과물은 **참고용**입니다. 정부24에서 발급하는 공식 토지이용계획확인원을 법적으로 대체하지 않습니다. 인허가 단계에서는 반드시 공식 서류를 확인하세요.

---

## 2. 사전 준비 (최초 1회만)

### 2-1. 필수 프로그램
- **Node.js 20.19.0 이상** (필수) — https://nodejs.org LTS
- **Python 3 + ezdxf** (리포트 DXF 기능 사용 시) — `pip install ezdxf`

### 2-2. VWorld API 키 발급
1. https://www.vworld.kr 가입 → 오픈API 인증키 신청
2. 신청 시 **활용 API 3개 반드시 체크**: `2D데이터 API`, `검색 API`, `국가중점 API`
3. **서비스URL** 항목에는 `http://localhost` 입력 (개인 PC 로컬 사용)
   - ⚠️ 여기 입력한 값을 뒤의 `VWORLD_DOMAIN`에 **한 글자도 다르지 않게** 넣어야 함
   - 값이 다르면 2D데이터·국가중점 API가 `INCORRECT_KEY`로 실패

### 2-3. 저장소 설치 및 빌드
명령 프롬프트(cmd)에서 (경로에 한글·공백 없는 곳 권장):

```cmd
cd C:\tools
git clone https://github.com/chanjong-ui/cadastral-mcp.git
cd cadastral-mcp
npm install
npm run build
pip install ezdxf
```

빌드 성공 확인:
```cmd
dir build
```
→ `index.js` 파일이 보이면 성공 (`index.d.ts`와 헷갈리지 말 것)

---

## 3. Claude Desktop 연동 설정

`claude_desktop_config.json` 파일을 엽니다.
- 경로: `%APPDATA%\Claude\claude_desktop_config.json`
- 또는 Claude Desktop → 설정 → 개발자 → 구성 편집

`mcpServers` 안에 아래 블록을 추가 (다른 서버가 이미 있으면 콤마로 구분해 이어붙임):

```json
"cadastral-mcp": {
  "command": "node",
  "args": [
    "C:\\tools\\cadastral-mcp\\build\\index.js"
  ],
  "env": {
    "VWORLD_KEY": "발급받은키",
    "VWORLD_DOMAIN": "http://localhost",
    "DXF_OUTPUT_DIR": "C:\\Tools\\cadastral-mcp\\dxf출력"
  }
}
```

**환경변수 설명**

| 변수 | 필수 | 설명 |
|------|:---:|------|
| `VWORLD_KEY` | ✅ | 발급받은 인증키 |
| `VWORLD_DOMAIN` | ✅ | 키 신청 시 등록한 서비스URL과 **정확히 동일**하게 (`http://localhost`) |
| `DXF_OUTPUT_DIR` | 선택 | DXF 저장 폴더 (미지정 시 `./output`) |
| `CADASTRAL_PYTHON` | 선택 | Python이 여러 개일 때 실행할 python 경로 지정 |
| `LAW_OC` | 선택 | 지자체 조례 조회용 법제처 OC |

설정 저장 후 **Claude Desktop을 트레이에서 완전히 종료**(창 X만 누르면 안 됨 → 우측 하단 트레이 아이콘 우클릭 → 종료)하고 다시 실행합니다.

---

## 4. 제공 도구 8종

| 도구 | 기능 | 주요 입력 |
|------|------|-----------|
| `get_land_use_plan` | 지번 주소로 토지대장·용도지역·공시지가 통합 조회 | `address` |
| `get_land_use_plan_by_pnu` | PNU로 동일 조회 | `pnu` (19자리) |
| `export_cadastral_dxf` | 지번 주소로 필지 경계 DXF 내보내기 | `address`, `bufferMeters`, `includeNeighbors` |
| `export_cadastral_dxf_by_pnu` | PNU로 DXF 내보내기 | `pnu` |
| `export_land_report_dxf` | 표+지적도 한 장 리포트 DXF 생성 | `address` (+Python 필요) |
| `export_land_report_dxf_by_pnu` | PNU로 리포트 DXF 생성 | `pnu` |
| `search_national_law` | 국가법령 이름으로 검색 | `query` |
| `get_national_law_text` | 법령 조문 조회 | `mst`, `jo` |

**참고 개념**
- **PNU**: 19자리 필지 고유번호. 지번 주소는 검색 API가 자동으로 PNU로 변환하므로, 보통은 주소만 말하면 됨.
- **DXF 좌표계**: 기본 `EPSG:5186`. VWorld가 서버에서 투영 변환하므로 캐드에서 추가 변환 없이 바로 사용 가능.
- **DXF 레이어**: `TARGET_PARCEL`(대상 필지, 굵은 빨강) / `NEIGHBOR_PARCELS`(주변 필지, 얇은 파랑) / 각 지번 라벨 / `SOURCE_NOTE`(출처).

---

## 5. 실무 사용 예시 (땅튀기기 워크플로우)

설정이 끝나면 Claude Desktop 채팅창에 **한국어로 그냥 말하면** 됩니다.

### ① 대상지 기초 조사
```
서울시 강남구 역삼동 123-45 토지 정보 조회해줘
```
→ 지목·면적·용도지역·개별공시지가가 표로 정리되어 나옴

### ② 지적도 캐드 파일 뽑기
```
그 필지 지적도 DXF로 내보내줘. 주변 30m까지 포함해서.
```
→ `bufferMeters`를 지정하지 않으면 도구가 "몇 미터로 할까요?"라고 먼저 물어봄
→ `DXF_OUTPUT_DIR` 폴더에 `.dxf` 파일 생성 → AutoCAD/ZWCAD/캐디안에서 바로 열림

### ③ 한 장짜리 검토 리포트
```
이 땅 표 포함된 토지 리포트 DXF로 만들어줘
```
→ 좌측: 대지위치·면적·지목·지역지구 + 최대건폐율/용적률/조경/주차 (국가법령 + 지자체 조례 병렬)
→ 우상단: 법정동·지번·면적·소유구분·공시지가 7행 표
→ 우하단: 지적도 (패널 밖 필지는 XCLIP 크롭)
→ **Python + ezdxf 필요**

### ④ 여러 필지 일괄 처리
```
아래 5개 지번 전부 리포트 DXF로 뽑아줘:
- 역삼동 123-45
- 역삼동 123-46
- ...
```
→ 검토 대상 목록을 엑셀에서 복사·붙여넣기 한 번으로 일괄 생성

### ⑤ 법령 검토까지 이어서
```
이 용도지역 기준 건폐율·용적률 법령 근거 찾아줘
```
→ `search_national_law`로 국토계획법 시행령 검색 → `get_national_law_text`로 제84·85조 원문 조회
→ 가설계 사전 검토를 한 대화 안에서 마무리

---

## 6. 문제 해결 (Troubleshooting)

### "MCP cadastral-mcp: Server disconnected"
API 키 문제가 아니라 **`node build/index.js` 프로세스가 시작되지 못하고 죽는 것**. 순서대로 점검:

1. **Node 버전 확인** — cmd에서 `node -v` → `v20.19.0` 미만이면 재설치
2. **빌드 산출물 확인**
   ```cmd
   cd C:\tools\cadastral-mcp
   dir build
   ```
   `index.js`가 없으면 `npm install && npm run build` 다시 실행
3. **서버 직접 실행해 실제 에러 보기**
   ```cmd
   node build\index.js
   ```
   - `Cannot find module ...` → `npm install` 안 됨
   - **아무 에러 없이 커서만 멈춤 = 정상** (MCP 서버는 조용히 대기). `Ctrl+C`로 종료 후 Claude Desktop 재시작
4. **Claude Desktop 완전 종료 후 재시작** — 트레이 아이콘 우클릭 → 종료 (창만 닫으면 설정 재로딩 안 됨)

### 조회는 되는데 DXF 내보내기만 `INCORRECT_KEY`
→ `VWORLD_DOMAIN`이 키 신청 시 서비스URL과 다름. `http://` 유무, 끝의 `/` 까지 정확히 일치시킬 것.

### 리포트 DXF 생성 실패 / 지적도 크롭 실패
→ Python 또는 ezdxf 미설치. `pip install ezdxf` 실행.
→ Python이 여러 개면 `.env` 또는 config `env`에 `CADASTRAL_PYTHON`으로 경로 지정.
→ XCLIP 실패 시에도 파일은 생성되며, `지적도_XCLIP크롭` 필드에 실패 사유가 표시됨.

### JSON 설정 오류로 전체 서버가 안 뜸
→ `claude_desktop_config.json`은 콤마·중괄호 하나만 틀려도 전체가 깨짐. 온라인 JSON 검사기(jsonlint 등)에 붙여 문법 확인.

---

## 7. 데이터 출처 및 한계

**출처**
- 지적 경계·지번·공시지가: VWorld 2D데이터 API (연속지적도 `LP_PA_CBND_BUBUN`)
- 토지대장·용도지역지구·개별공시지가: VWorld 국가중점(NED) API
- 법령: 법제처 국가법령정보 API

**한계**
- VWorld는 **월 단위 갱신** → 실시간 최신 상태가 아닐 수 있음
- 지자체 조례 패턴 미매칭 시 국가법령 상한만 표시됨
- 주차 기준이 별표(HWP/PDF)로 위임된 경우 파싱 100% 보장 안 됨
- 도넛형(홀 있는) 필지는 로직상 지원하나 실사용 검증 미완
- **모든 결과는 참고용** — 법적 효력이 있는 공식 서류를 대체하지 않음

---

## 8. 빠른 참조 (Cheat Sheet)

```
① 조사    "○○동 123-45 토지 정보 조회해줘"
② 지적도  "그 필지 DXF로 내보내줘, 주변 30m 포함"
③ 리포트  "표 포함 토지 리포트 DXF로 만들어줘"
④ 일괄    "아래 지번들 전부 리포트 DXF로 뽑아줘: ..."
⑤ 법령    "이 용도지역 건폐율·용적률 법령 근거 찾아줘"

출력 위치: DXF_OUTPUT_DIR 폴더 (기본 ./output)
좌표계:    EPSG:5186 (변환 불필요, 캐드 직접 사용)
```

---

*최종 업데이트: 2026-07-18*
