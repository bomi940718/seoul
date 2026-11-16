// 설계개요 화면.
// 표기 순서·항목명은 실무 표준(2023.0000_법규검토서_v1.0_full.hwp)을 그대로 따른다.
// 임의로 순서를 바꾸지 말 것 — 협력사와 공유하는 서식이다.

const state = {
  mode: "신축",
  project: {
    projectName: "", client: "", siteAddress: "", useZones: "",
    landCategory: "", siteArea: "", primaryUse: "",
    allowedUse: "", allowedUseBasis: "", disallowedUse: "", disallowedUseBasis: "",
    province: "", city: "",
  },
  // 설계개요 표: 표준 순서 고정 (실무 서식 그대로)
  //  plan/legal 종류
  //    input  : 사람이 넣는 값
  //    derived: 엔진이 계산해 채우는 칸 (편집 불가)
  //    param  : 기준값을 작게 입력받고, 그 옆/아래에 계산 결과를 보여주는 칸
  //             (입력과 결과가 한 칸을 공유해 서로 덮어쓰지 않도록 분리한 것)
  rows: [
    { key: "buildingArea", label: "건 축 면 적", plan: "input", planUnit: "m²", planDerived: true, legal: "derived" },
    { key: "coverage",     label: "건 폐 율",    plan: "derived", legal: "param", legalUnit: "%" },
    { key: "grossArea",    label: "연 면 적",    plan: "derived", legal: "derived" },
    { key: "floorRatio",   label: "용 적 률",    plan: "derived", legal: "param", legalUnit: "%" },
    { key: "scale",        label: "건 축 규 모", plan: "input", legal: "input" },
    { key: "height",       label: "최 고 높 이", plan: "input", planUnit: "m", legal: "input" },
    { key: "landscape",    label: "조 경 면 적", plan: "input", planUnit: "m²", legal: "param", legalUnit: "%" },
    { key: "parkingType",  label: "주 차 대 수", sub: "주차형식", plan: "input", legal: "input" },
    { key: "parkingIn",    sub: "옥내",       plan: "input", legal: "input" },
    { key: "parkingOut",   sub: "옥외",       plan: "derived", legal: "param", legalUnit: "m² 당 1대", legalPrefix: "시설면적" },
    { key: "parkingDis",   sub: "장애인전용", plan: "derived", legal: "derived" },
    { key: "parkingExt",   sub: "확장형",     plan: "derived", legal: "derived" },
    { key: "parkingEco",   sub: "친환경",     plan: "derived", legal: "derived" },
    { key: "parkingAll",   sub: "전체",       plan: "derived", legal: "derived" },
  ],
  values: {},   // key -> { before, plan, after, legal, basis }
  floors: [],   // { bldg, floor, use, excl, common, exclude }
  // 사람이 고친 판정. 항목Id -> { verdict, reason, criterion, calculation }
  // AI 판정(state.result)은 원본 그대로 두고 여기에만 덮어쓴다 → 언제든 되돌릴 수 있다.
  edits: {},
};

const VERDICTS = ["적용", "해당없음", "확인필요"];

const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const status = (m) => ($("#status").textContent = m);

// ── 표 그리기 ────────────────────────────────────────────────
function renderScaleTable() {
  const body = $("#scaleBody");
  body.innerHTML = "";
  for (const row of state.rows) {
    const v = (state.values[row.key] ||= { before: "", plan: "", after: "", legal: "", basis: "" });
    const tr = document.createElement("tr");
    if (row.sub && !row.label) tr.className = "sub-row";

    // 구분 (주차 하위행은 들여쓴 소항목)
    const th = document.createElement("th");
    if (row.label && row.sub) { th.innerHTML = `${row.label}<div class="sub">${row.sub}</div>`; }
    else if (row.sub) { th.textContent = row.sub; tr.classList.add("sub-row"); }
    else { th.textContent = row.label; }
    tr.appendChild(th);

    tr.appendChild(cellTd(row, v, "before", "col-before"));
    tr.appendChild(deltaTd(row, v));
    tr.appendChild(cellTd(row, v, "plan", "col-plan"));
    tr.appendChild(cellTd(row, v, "after", "col-after"));
    tr.appendChild(cellTd(row, v, "legal", "col-legal"));

    const basis = document.createElement("td");
    basis.appendChild(makeCell(row.key, "basis", v.basis, "basis", "근거 법규"));
    tr.appendChild(basis);

    body.appendChild(tr);
  }
}

function cellTd(row, v, slot, cls) {
  const td = document.createElement("td");
  td.className = cls;
  const kind = slot === "legal" ? (row.legal || "input") : (row.plan || "input");

  if (kind === "derived") {
    td.appendChild(derivedBox(row.key, slot, v[slot]));
    return td;
  }

  if (kind === "param") {
    // 기준값(작은 입력) + 계산 결과(아래 줄). 서로 다른 저장소를 쓴다.
    const wrap = document.createElement("div");
    wrap.className = "param-cell";
    if (row.legalPrefix) wrap.appendChild(tag(row.legalPrefix));
    wrap.appendChild(makeCell(row.key, slot, v[slot], "num param", "0"));
    if (row.legalUnit) wrap.appendChild(tag(row.legalUnit));
    td.appendChild(wrap);
    td.appendChild(derivedBox(row.key, slot + "Calc", v[slot + "Calc"]));
    return td;
  }

  // 일반 입력. 파생 표기가 있는 행(건축면적)은 산정식을 아래에 보조로 보여준다.
  const wrap = document.createElement("div");
  wrap.className = "param-cell";
  wrap.appendChild(makeCell(row.key, slot, v[slot], row.planUnit ? "num" : "", "입력"));
  if (row.planUnit && slot !== "legal") wrap.appendChild(tag(row.planUnit));
  td.appendChild(wrap);
  if (row.planDerived && slot !== "legal") td.appendChild(derivedBox(row.key, slot + "Calc", v[slot + "Calc"]));
  return td;
}

const tag = (t) => { const s = document.createElement("span"); s.className = "unit-tag"; s.textContent = t; return s; };

/// 증감 열 (신축이 아닐 때만 보인다). 변경전·변경후는 그대로 두고 그 사이에 증감을 둔다.
///  - 건축면적: 증감을 넣으면 변경후 = 변경전 + 증감, 변경후를 고치면 증감이 따라 바뀐다.
///  - 나머지: 변경전·변경후로 계산해 보여준다(건폐율·연면적·용적률·높이·주차).
function deltaTd(row, v) {
  const td = document.createElement("td");
  td.className = "col-delta";
  if (row.key === "buildingArea") {
    const wrap = document.createElement("div");
    wrap.className = "param-cell";
    const c = makeCell(row.key, "delta", v.delta, "num param", "±0.00");
    c.title = "기존 건축면적에서 늘거나 줄어드는 면적 (예: 증축 25.5, 철거 -10)";
    c.addEventListener("blur", () => {
      const b = num(state.values.buildingArea?.before);
      const d = String(state.values.buildingArea?.delta ?? "").trim();
      if (d !== "") setCell("buildingArea", "after", r2(b + num(d)).toFixed(2));
    });
    wrap.appendChild(c);
    wrap.appendChild(tag("m²"));
    td.appendChild(wrap);
    return td;
  }
  td.appendChild(derivedBox(row.key, "deltaCalc", v.deltaCalc));
  return td;
}

/// 증감 열의 계산 칸을 채운다 (엔진 계산 뒤에 부른다).
function refreshDeltas(o) {
  if (state.mode === "신축") return;
  const site = num(state.project.siteArea);
  const sgn = (x, unit, digits = 2) => (Math.abs(x) < 0.005 ? `0 ${unit}` : `${x > 0 ? "+" : ""}${x.toFixed(digits)} ${unit}`);
  const ba = state.values.buildingArea || {};
  const bBefore = num(ba.before), bAfter = buildingAreaInput();
  // 변경후를 직접 고쳤으면 증감 칸도 맞춘다 (입력 중인 칸은 건드리지 않는다)
  const deltaEl = document.querySelector('.cell[data-key="buildingArea"][data-slot="delta"]');
  if (String(ba.after ?? "").trim() !== "" && document.activeElement !== deltaEl) {
    const d = r2(bAfter - bBefore);
    if (num(ba.delta) !== d || (!d && String(ba.delta ?? "") !== "")) setCell("buildingArea", "delta", d ? String(d) : "");
  }
  const gAfter = state.floors.filter((f) => !f.exclude).reduce((s, f) => s + num(f.excl) + num(f.common), 0);
  const gBefore = existingGrossArea();
  setCalc("grossArea", "deltaCalc", gBefore || gAfter ? sgn(gAfter - gBefore, "m²") : "");
  if (site) {
    setCalc("coverage", "deltaCalc", bBefore || bAfter ? sgn((bAfter - bBefore) / site * 100, "%") : "");
    setCalc("floorRatio", "deltaCalc", gBefore || gAfter ? sgn((gAfter - gBefore) / site * 100, "%") : "");
  }
  const h = state.values.height || {};
  setCalc("height", "deltaCalc", num(h.before) && num(h.after) ? sgn(num(h.after) - num(h.before), "m") : "");
  const add = o?.change?.additionalParkingSpaces;
  setCalc("parkingAll", "deltaCalc", add == null ? "" : add > 0 ? `추가 ${add} 대` : "추가 없음");
  const pa = document.querySelector('.cell[data-key="parkingAll"][data-slot="deltaCalc"]');
  if (pa) pa.title = (o?.change?.additionalParking || "") +
    " — 변경후 법정 대수 − 변경전 법정 대수 (주차장법 시행령 제6조제4항, 증가분만 추가 확보)";
}

/// 엔진이 채우는 표시 전용 영역 (사람이 못 고치므로 입력을 덮어쓸 일이 없다)
function derivedBox(key, slot, value) {
  const d = document.createElement("div");
  d.className = "cell calc formula";
  d.dataset.key = key;
  d.dataset.slot = slot;
  d.textContent = value || "";
  return d;
}

function makeCell(key, slot, value, extra, ph) {
  const d = document.createElement("div");
  d.className = "cell " + (extra || "");
  d.dataset.key = key;
  d.dataset.slot = slot;
  if (ph) d.dataset.ph = ph;
  d.textContent = value || "";
  d.contentEditable = "true";
  d.addEventListener("input", () => { (state.values[key] ||= {})[slot] = d.textContent; });
  d.addEventListener("blur", recalc);
  return d;
}

// ── 상단 입력 바인딩 ─────────────────────────────────────────
function bindHeadFields() {
  // 건축주 구분(공공/민간) — 기본은 민간
  const pub = $("#chkPublic");
  pub.checked = !!state.project.isPublic;
  pub.onchange = () => { state.project.isPublic = pub.checked; autosave(); };
  $$(".head .cell[data-field]").forEach((el) => {
    const f = el.dataset.field;
    el.textContent = state.project[f] || "";
    el.addEventListener("input", () => {
      state.project[f] = el.textContent.trim();
      if (f === "projectName") stampProject();
    });
    el.addEventListener("blur", recalc);

    // 대지위치는 주소만 넣으면 조회된다 — Enter 또는 다른 칸으로 옮길 때.
    if (f === "siteAddress") {
      el.addEventListener("keydown", (e) => {
        if (e.key === "Enter" && !e.isComposing) { e.preventDefault(); el.blur(); }
      });
      el.addEventListener("blur", () => lookupAddress({ auto: true }));
    }
  });
}

// ── 면적표 ───────────────────────────────────────────────────
// 연면적(용적률 산정 기준)의 출처. 엑셀에서 행을 복사해 붙여넣을 수 있어야 한다.
function renderAreaTable() {
  const body = $("#areaBody");
  body.innerHTML = "";
  if (state.floors.length === 0) addFloorRow(false);

  const changeMode = state.mode !== "신축";
  state.floors.forEach((f, i) => {
    if (changeMode) fillDelta(f);
    const tr = document.createElement("tr");
    tr.appendChild(areaCell(i, "bldg", "동"));
    tr.appendChild(areaCell(i, "floor", "층"));
    // 신축이 아니면 변경전 용도·면적과 증감 열이 보인다 (CSS가 신축 모드에서 감춘다)
    tr.appendChild(areaCell(i, "beforeUse", "변경전 용도", false, "col-before"));
    tr.appendChild(areaCell(i, "use", changeMode ? "변경후 용도" : "용도"));
    tr.appendChild(areaCell(i, "beforeExcl", "0.00", true, "col-before"));
    tr.appendChild(areaCell(i, "beforeCommon", "0.00", true, "col-before"));
    tr.appendChild(areaCell(i, "delta", "±0.00", true, "col-delta"));
    tr.appendChild(areaCell(i, "excl", "0.00", true));
    tr.appendChild(areaCell(i, "common", "0.00", true));

    const sum = document.createElement("td");
    sum.className = "total";
    sum.textContent = fmt(num(f.excl) + num(f.common));
    // 증감은 한눈에 보여야 한다 — 증축 검토의 핵심 숫자다.
    if (changeMode) {
      const delta = num(f.excl) + num(f.common) - num(f.beforeExcl) - num(f.beforeCommon);
      if (Math.abs(delta) >= 0.005) {
        const d = document.createElement("div");
        d.className = "sub";
        d.textContent = (delta > 0 ? "+" : "") + fmt(delta);
        sum.appendChild(d);
      }
    }
    tr.appendChild(sum);

    // 연면적 제외 여부 (PIT 등)
    const note = document.createElement("td");
    const lab = document.createElement("label");
    lab.className = "flag";
    const cb = document.createElement("input");
    cb.type = "checkbox";
    cb.checked = !!f.exclude;
    cb.addEventListener("change", () => { f.exclude = cb.checked; refreshAreaTotals(); recalc(); });
    lab.appendChild(cb);
    lab.appendChild(document.createTextNode("연면적 제외"));
    note.appendChild(lab);
    tr.appendChild(note);

    const del = document.createElement("td");
    const btn = document.createElement("button");
    btn.className = "row-del"; btn.textContent = "×"; btn.title = "행 삭제";
    btn.addEventListener("click", () => { state.floors.splice(i, 1); renderAreaTable(); recalc(); });
    del.appendChild(btn);
    tr.appendChild(del);

    body.appendChild(tr);
  });
  refreshAreaTotals();
}

function areaCell(i, field, ph, isNum, cls) {
  const td = document.createElement("td");
  if (cls) td.className = cls;
  const d = document.createElement("div");
  d.className = "cell" + (isNum ? " num" : "");
  d.contentEditable = "true";
  d.dataset.ph = ph;
  d.dataset.row = i;
  d.dataset.field = field;
  d.textContent = state.floors[i][field] ?? "";
  d.addEventListener("input", () => { state.floors[i][field] = d.textContent.trim(); });
  d.addEventListener("blur", () => {
    if (state.mode !== "신축") syncFloor(state.floors[i], field);
    renderAreaTable(); recalc(); scheduleUseJudge();
  });
  d.addEventListener("paste", onAreaPaste);
  td.appendChild(d);
  return td;
}

/// 엑셀에서 여러 행을 붙여넣으면 표에 그대로 펼친다(탭 구분).
function onAreaPaste(e) {
  const text = (e.clipboardData || window.clipboardData).getData("text");
  if (!text || !/[\t\n]/.test(text)) return;      // 단일 셀 붙여넣기는 기본 동작
  e.preventDefault();
  const start = +e.currentTarget.dataset.row;
  const rows = text.split(/\r?\n/).filter((l) => l.trim());
  rows.forEach((line, k) => {
    const c = line.split("\t");
    const target = state.floors[start + k] || (state.floors[start + k] = blankFloor());
    if (c[0] !== undefined) target.bldg = c[0].trim();
    if (c[1] !== undefined) target.floor = c[1].trim();
    if (c[2] !== undefined) target.use = c[2].trim();
    if (c[3] !== undefined) target.excl = c[3].trim();
    if (c[4] !== undefined) target.common = c[4].trim();
    if (state.mode !== "신축") syncFloor(target, "excl");   // 붙여넣은 값은 변경후 — 증감을 계산한다
  });
  renderAreaTable();
  recalc();
  status(`면적표 ${rows.length}행 붙여넣기`);
}

const blankFloor = () => ({ bldg: "", floor: "", beforeUse: "", use: "", beforeExcl: "", beforeCommon: "",
  delta: "", excl: "", common: "", exclude: false });

const r2 = (v) => Math.round(v * 100) / 100;

/// 변경전 · 증감 · 변경후를 서로 맞춘다. 엔진은 변경후(excl/common)를 받는다.
///  - 증감을 고치면: 변경후 전용 = 변경전 전용 + 증감 (공용은 그대로, 비어 있으면 변경전 공용)
///  - 변경후나 변경전을 고치면: 증감 = 변경후 합 − 변경전 합
function syncFloor(f, field) {
  if (field === "delta") {
    const d = String(f.delta ?? "").trim();
    f.excl = num(f.beforeExcl) || num(d) ? r2(num(f.beforeExcl) + num(d)).toFixed(2) : "";
    if (String(f.common ?? "").trim() === "" && num(f.beforeCommon)) f.common = num(f.beforeCommon).toFixed(2);
    return;
  }
  f.delta = undefined;
  fillDelta(f);
}

/// 증감 칸이 비어 있으면(예전 저장본·붙여넣기) 변경전·변경후로 계산해 채운다.
function fillDelta(f) {
  if (f.delta !== undefined && f.delta !== null) return;
  const d = r2(num(f.excl) + num(f.common) - num(f.beforeExcl) - num(f.beforeCommon));
  f.delta = d ? String(d) : "";
}
function addFloorRow(render = true) {
  const last = state.floors[state.floors.length - 1];
  const f = blankFloor();
  if (last) { f.bldg = last.bldg; f.use = last.use; }   // 동·용도는 이어받는 편이 입력이 빠르다
  state.floors.push(f);
  if (render) renderAreaTable();
}
$("#btnAddFloor").addEventListener("click", () => addFloorRow());

function refreshAreaTotals() {
  const gross = state.floors.filter((f) => !f.exclude).reduce((s, f) => s + num(f.excl) + num(f.common), 0);
  const total = state.floors.reduce((s, f) => s + num(f.excl) + num(f.common), 0);
  $("#areaGross").textContent = gross ? fmt(gross) : "-";
  $("#areaTotal").textContent = total ? fmt(total) : "-";

  // 변경전 연면적과 증감 (신축이 아닐 때만 보이는 줄)
  const before = existingGrossArea();
  $("#areaBefore").textContent = before ? fmt(before) : "-";
  const delta = gross - before;
  $("#areaDelta").textContent = before
    ? `증감 ${delta >= 0 ? "+" : ""}${fmt(delta)} ㎡`
    : "변경전 면적을 입력하면 증감이 계산됩니다";
}

/// 변경전 연면적 (연면적 제외 층은 뺀다 — 변경후와 같은 기준이어야 증감이 맞다)
const existingGrossArea = () => state.floors
  .filter((f) => !f.exclude)
  .reduce((s, f) => s + num(f.beforeExcl) + num(f.beforeCommon), 0);

/// 면적표를 동 단위로 묶어 엔진 모델(Buildings/Floors)로 만든다.
/// 신축이 아니면 기존(변경전) 면적도 함께 보낸다 — 엔진이 증감과 변경전 기준을 계산한다.
function floorsByBuilding() {
  const byBldg = new Map();
  for (const f of state.floors) {
    if (!num(f.excl) && !num(f.common) && !num(f.beforeExcl) && !num(f.beforeCommon)) continue;
    const name = f.bldg || "동";
    if (!byBldg.has(name)) byBldg.set(name, []);
    byBldg.get(name).push({
      floorLabel: f.floor, use: f.use,
      exclusiveArea: num(f.excl), commonArea: num(f.common),
      existingExclusiveArea: num(f.beforeExcl), existingCommonArea: num(f.beforeCommon),
      excludeFromGrossArea: !!f.exclude,
    });
  }
  return byBldg;
}

/// 면적표 머리글을 모드에 맞춘다 (신축은 "전용", 그 외는 "변경후 전용").
function syncAreaHeaders() {
  const after = state.mode !== "신축";
  $("#thExcl").textContent = after ? "변경후 전용 (㎡)" : "전용 (㎡)";
  $("#thCommon").textContent = after ? "변경후 공용 (㎡)" : "공용 (㎡)";
  $("#thUse").textContent = after ? "변경후 용도" : "용 도";
  renderGuide();
}

/// 설계개요 맨 위 작업 순서 안내. 기존 건축물 모드는 순서가 달라서 따로 적는다.
function renderGuide() {
  const g = $("#guide");
  if (!g) return;
  g.innerHTML = state.mode === "신축"
    ? `<b>① 대지위치</b> 입력 후 Enter — 토지대장·토지이용계획·법정 건폐율/용적률·허용 용도·주차·조경이 한 번에 채워집니다 ` +
      `→ <b>② 건축면적·층별 면적표</b> 입력 → <b>③ 검토 실행</b>`
    : `<b>① 대지위치</b> 입력 후 Enter — 토지·용도지역·법정 한도·허용 용도와 <b>건축물대장(변경전)</b>이 한 번에 채워집니다 ` +
      `→ <b>② 서류 대조</b> 탭에서 토지대장·건축물대장·토지이용계획이 맞는지 확인 ` +
      `→ <b>③ 바뀌는 것만 입력</b>: 변경후는 변경전과 같게 채워져 있습니다. <b>증감(±)</b> 칸에 넣거나 변경후 값을 직접 고치면 서로 맞춰지고, ` +
      `${state.mode === "용도변경" ? "<b>변경후 용도</b>만 바꾸면 됩니다" : "증축 층은 '행 추가'"} — 건폐율·용적률·주차는 자동 반영 ` +
      `→ <b>④ 검토 실행</b>`;
}

const fmt = (v) => (v || 0).toLocaleString("ko-KR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

// ── 계산 (엔진 호출) ─────────────────────────────────────────
let recalcTimer = null;
// quiet: 계산은 하되 상태줄은 건드리지 않는다(자동조회 완료 메시지처럼 남겨야 할 안내가 있을 때).
let recalcQuiet = false;
function recalc(opts) {
  recalcQuiet = !!opts?.quiet;
  clearTimeout(recalcTimer);
  recalcTimer = setTimeout(doRecalc, 150);
}

async function doRecalc() {
  const siteArea = num(state.project.siteArea);
  if (!siteArea) return;

  // 면적표가 바뀌면 조경 구간도 달라질 수 있다.
  applyLandscapeRule();

  // 면적표를 동 단위로 묶어 엔진 모델(Buildings/Floors)로 보낸다.
  const byBldg = floorsByBuilding();

  const payload = {
    projectName: state.project.projectName,
    client: state.project.client,
    isPublic: !!state.project.isPublic,
    siteAddress: state.project.siteAddress,
    province: state.project.province,
    city: state.project.city,
    siteArea,
    primaryUse: state.project.primaryUse,
    mode: state.mode,
    plannedBuildingArea: buildingAreaInput(),
    existingBuildingArea: num(state.values.buildingArea?.before),
    zoning: {
      maxCoverageRatio: num(state.values.coverage?.legal) || null,
      maxFloorAreaRatio: num(state.values.floorRatio?.legal) || null,
      landscapeRatio: landscapeRatioInput(),
      source: state.values.coverage?.basis || "",
    },
    parking: {
      areaPerSpace: num(state.values.parkingOut?.legal) || 200,
      ecoRatio: ecoRatio(),
      parkingType: state.values.parkingType?.[planSlot()] || "",
      source: state.values.parkingType?.basis || "",
    },
    buildings: [...byBldg].map(([name, floors]) => ({ name, floors })),
  };

  try {
    const r = await fetch("/api/overview", {
      method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(payload),
    });
    const o = await r.json();
    // 계산 결과는 표시 전용 칸에만 쓴다. 사람이 넣은 기준값 칸은 절대 건드리지 않는다.
    for (const [key, v] of Object.entries(o.rows)) {
      const row = state.rows.find((x) => x.key === key);
      if (!row) continue;
      const planTarget = row.plan === "derived" ? planSlot() : planSlot() + "Calc";
      const legalTarget = row.legal === "derived" ? "legal" : "legalCalc";
      if (v?.planned != null) setCalc(key, planTarget, v.planned);
      if (v?.legal != null) setCalc(key, legalTarget, v.legal);
      // 변경전 열도 같은 규칙으로 채운다(입력 칸은 사람 것이므로 보조 칸에만 쓴다).
      if (v?.before != null) setCalc(key, row.plan === "derived" ? "before" : "beforeCalc", v.before);
    }
    warnCompliance(o.compliance);
    refreshDeltas(o);
    if (!recalcQuiet) status(o.change
      ? `계산 갱신됨 — ${o.change.grossArea} / 주차 ${o.change.additionalParking}`
      : `계산 갱신됨 — 연면적 ${fmt(o.grossFloorArea)} ㎡`);
  } catch { /* 입력 중 부분 오류는 무시 */ }
}

/// 친환경 전용주차구역 비율: 환경친화적자동차법 시행령 제18조의6 — 5% 이상,
/// 2022.1.28. 전에 건축허가를 받은 기축시설은 2% 이상 (시·도 조례로 상향 가능).
function ecoRatio() {
  if (state.mode === "신축") return 0.05;
  const b = state.docs?.register?.buildings?.find((x) => !x.isAnnex) || state.docs?.register?.buildings?.[0];
  const permit = b?.permitDate || b?.approvalDate || "";
  return permit && permit < "2022-01-28" ? 0.02 : 0.05;
}

/// 주차 행 근거 법규 — 법제처 원문으로 확인한 조항(2026-11-16). 사람이 고친 근거는 덮지 않는다.
const PARKING_BASIS = {
  parkingDis: "장애인ㆍ노인ㆍ임산부 등의 편의증진 보장에 관한 법률 시행령 별표 2 (부설주차장 10대 이상, 비율은 지자체 주차장 조례)",
  parkingExt: "주차장법 시행규칙 제11조제4항 (50대 이상 — 제6조제1항제14호 준용, 30% 이상)",
  parkingEco: "환경친화적 자동차의 개발 및 보급 촉진에 관한 법률 시행령 제18조의5·제18조의6 (50대 이상, 5% 이상·기축 2% 이상, 시·도 조례)",
};
function applyParkingBasis() {
  const prev = state.project.autoParkingBasis || {};
  for (const [k, v] of Object.entries(PARKING_BASIS)) {
    const cur = String(state.values[k]?.basis || "").trim();
    if (!cur || cur === prev[k]) setCell(k, "basis", v);
  }
  // 전체 행은 부설주차장 설치기준(시행령 별표 1 · 조례)을 그대로 따른다
  const out = String(state.values.parkingOut?.basis || "").trim();
  const all = String(state.values.parkingAll?.basis || "").trim();
  if (out && (!all || all === prev.parkingAll)) setCell("parkingAll", "basis", out);
  state.project.autoParkingBasis = { ...PARKING_BASIS, parkingAll: out };
}

// 건축면적은 사용자가 직접 넣는 값(설계 결과물).
// 신축이 아니면 변경후 칸 값(증감을 넣으면 변경전 + 증감으로 채워진다). 변경후가 비어 있으면 변경전 그대로.
function buildingAreaInput() {
  const v = state.values.buildingArea || {};
  if (state.mode === "신축") return num(v.plan);
  if (String(v.after ?? "").trim() !== "") return num(v.after);
  return r2(num(v.before) + num(v.delta));
}

/// 현재 연면적에 해당하는 조례 조경 구간을 골라 법정 칸에 넣는다.
function applyLandscapeRule() {
  if (!state.landscapeRules?.length) return;
  const gross = state.floors
    .filter((f) => !f.exclude)
    .reduce((s, f) => s + num(f.excl) + num(f.common), 0);
  if (!gross) return;

  const hit = state.landscapeRules.find(
    (r) => gross >= r.minGrossArea && (r.maxGrossArea == null || gross < r.maxGrossArea));
  if (!hit) return;
  setCell("landscape", "legal", String(Math.round(hit.ratio * 1000) / 10));   // 0.15 → 15
}

// 조경면적 법정 기준: "5"(=5%) 또는 "0.05" 모두 허용.
function landscapeRatioInput() {
  const n = num(state.values.landscape?.legal);
  if (!n) return null;
  return n > 1 ? n / 100 : n;
}

function warnCompliance(c) {
  markRow("coverage", c?.coverage);
  markRow("floorRatio", c?.floorArea);
  markRow("scale", c?.floors);
}
function markRow(key, ok) {
  const el = document.querySelector(`.cell[data-key="${key}"][data-slot="${planSlot()}"]`);
  if (!el) return;
  el.classList.toggle("over", ok === false);
}

const planSlot = () => (state.mode === "신축" ? "plan" : "after");
function pick(key, slot) { return state.values[key]?.[slot === "planSlot" ? planSlot() : slot]; }
function setCalc(key, slot, text) {
  const s = slot === "planSlot" ? planSlot() : slot;
  (state.values[key] ||= {})[s] = text || "";
  const el = document.querySelector(`.cell[data-key="${key}"][data-slot="${s}"]`);
  if (el) el.textContent = text || "";
}
const num = (v) => { const n = parseFloat(String(v ?? "").replace(/[^\d.-]/g, "")); return isNaN(n) ? 0 : n; };

// ── 자동조회 ─────────────────────────────────────────────────
// 주소만 넣으면 조회된다: 대지위치 칸에서 Enter를 치거나 다른 칸으로 옮기면 자동 실행.
// 버튼은 같은 주소로 다시 조회할 때 쓴다. 같은 주소로는 자동 재조회하지 않는다 —
// 조회는 지역지구·대지면적·법정 한도를 덮어쓰므로, 사람이 고친 값이 지워지면 안 된다.
$("#btnLookup").addEventListener("click", () => lookupAddress({ auto: false }));

let lookupBusy = false;
async function lookupAddress({ auto }) {
  const address = (state.project.siteAddress || "").trim();
  if (lookupBusy) return;
  if (!address) { if (!auto) status("대지위치를 입력하세요 (지번·도로명 모두 가능)"); return; }
  if (auto && address === state.project.lookedUpAddress) return;

  lookupBusy = true;
  const btn = $("#btnLookup");
  btn.disabled = true; btn.textContent = "조회 중…";
  status(`주소 조회 중… (${address})`);
  try {
    const r = await fetch("/api/lookup", {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ address, primaryUse: state.project.primaryUse }),
    });
    applyLookup(await r.json(), address);
  } catch (e) {
    status("조회 실패: 화면 호스트와 연결되지 않았습니다 — " + e.message);
  } finally {
    lookupBusy = false;
    btn.disabled = false; btn.textContent = "자동조회";
  }
}

function applyLookup(d, inputAddress) {
  if (!d.ok) {
    // 키가 없어서 실패한 것이면 상태줄에만 적지 않는다 — 실제로 이걸 못 보고
    // "주소 조회가 안 된다"로 여긴 일이 있었다. 설정 창을 바로 연다.
    if (/키/.test(d.message || "")) openSettings(d.message);
    status("조회 실패: " + d.message);
    return;
  }
  // 도로명을 지번으로 바꿔 조회한 경우 — 검토서는 지번 주소로 나가므로 바뀐 사실을 알린다.
  const addrNote = d.fromRoadAddress ? " / 도로명 주소를 지번 주소로 바꿨습니다" : "";

  state.project.province = d.province; state.project.city = d.city;
  setField("useZones", (d.zones || []).join(", "));
  if (d.area) setField("siteArea", Number(d.area).toFixed(2));
  if (d.category) setField("landCategory", d.category);
  setField("siteAddress", d.address);

  // 법정 건폐율·용적률을 도시계획조례에서 함께 가져온다(지구단위계획이 없어도 검토가 되도록).
  // 지구단위계획 값이 따로 있으면 사용자가 덮어쓰면 된다.
  let limitNote = "";
  const L = d.limits || {};
  if (L.coverage != null) { setCell("coverage", "legal", String(L.coverage)); setCell("coverage", "basis", L.coverageBasis || ""); }
  if (L.far != null) { setCell("floorRatio", "legal", String(L.far)); setCell("floorRatio", "basis", L.farBasis || ""); }
  if (L.coverage != null || L.far != null) {
    limitNote = ` / 법정 건폐율 ${L.coverage ?? "-"}% · 용적률 ${L.far ?? "-"}% (${L.zone || ""} 기준)`;
    // 공장·지식산업센터 등 용도별로 한도가 달라지는 단서가 있으면 함께 알린다(안양 준공업지역 등).
    if (/※ 단서/.test((L.coverageBasis || "") + (L.farBasis || ""))) limitNote += " — 용도별 단서 있음(법규 칸 확인)";
  } else {
    limitNote = " / 법정 한도는 조례에서 찾지 못했습니다 — 직접 입력하세요";
  }

  // 부설주차장 설치기준 — 시행령 별표에서 가져오고, 조례로 달라질 수 있음을 함께 알린다.
  let parkNote = "";
  if (d.parking?.areaPerSpace) {
    setCell("parkingOut", "legal", String(d.parking.areaPerSpace));
    const basis = [d.parking.basis, d.parking.ordinanceName ? `${d.parking.ordinanceName} 별표 확인 필요` : ""]
      .filter(Boolean).join("\n");
    setCell("parkingOut", "basis", basis);
    state.parkingOrdinanceLink = d.parking.ordinanceAnnexLink || "";
    parkNote = ` / 주차 ${d.parking.areaPerSpace}㎡당 1대 (${d.parking.matchedUse || ""} — 조례 확인 필요)`;
  } else if (state.project.primaryUse) {
    parkNote = " / 주차 기준을 찾지 못했습니다 — 직접 입력하세요";
  }

  // 조경 비율은 연면적 구간별로 갈리므로 구간표를 보관하고, 계산할 때 현재 연면적으로 고른다.
  state.landscapeRules = d.landscape?.rules || null;
  state.landscapeBasis = d.landscape?.basis || "";
  if (state.landscapeBasis) setCell("landscape", "basis", state.landscapeBasis);
  applyLandscapeRule();

  // 입력한 주소와 바뀐 지번 주소 둘 다 "조회한 주소"로 기록한다 — 칸이 지번으로 바뀐 뒤
  // 포커스가 빠질 때 같은 땅을 또 조회하지 않게.
  state.project.lookedUpAddress = d.address;
  state.project.pnu = d.pnu;
  state.lastLookupInput = inputAddress;

  // 서류 대조 탭이 쓰는 원자료 (토지대장 · 토지이용계획 · 건축물대장 · 용도 판정)
  const keepPdf = state.docs?.address === d.address ? state.docs.pdf : undefined;
  state.docs = {
    pdf: keepPdf,
    address: d.address, landArea: d.area, category: d.category, zones: d.zones || [],
    limits: d.limits || {}, register: d.register || null, registerError: d.registerError || "",
    zoneUse: d.zoneUse || null, province: d.province, city: d.city,
  };

  const useNote = applyZoneUse(d.zoneUse);
  const reg = d.register;
  let regNote = "";
  if (d.registerError) regNote = ` / 건축물대장: ${d.registerError}`;
  else if (reg && !reg.hasBuildings) regNote = " / 건축물대장: 등록된 건축물 없음(빈 땅이거나 미등재)";
  else if (reg && state.mode === "신축") regNote = ` / 건축물대장: 기존 건축물 ${reg.buildings.length}개동 있음 — 리모델링·증축·용도변경이면 위 모드를 바꾸세요`;

  applyParkingBasis();
  status(`자동조회 완료 — ${d.province} ${d.city} / 지목 ${d.category || "-"} / ${d.zones.length}개 지역·지구${addrNote}${limitNote}${parkNote}${useNote}${regNote}`);
  renderDocs();
  recalc({ quiet: true });   // 완료 메시지를 "계산 갱신됨"으로 덮지 않는다

  // 기존 건축물 공사면 대장 값(변경전)이 출발점이다 — 같은 조회에서 받아 온 것을 바로 채운다.
  if (state.mode !== "신축" && reg?.hasBuildings) applyRegister(reg, { auto: true });
}

/// 용도지역 허용·불허 용도 (시행령 별표 + 도시계획 조례 별표). 지구단위계획 값이 들어 있으면 덮지 않는다 —
/// 자동으로 넣은 값(직전 조회 결과)이 그대로 있을 때만 바꾼다.
function applyZoneUse(z) {
  if (!z) return " / 허용 용도: 조례를 찾지 못했습니다(법제처 키 확인)";
  const put = (field, value) => {
    const cur = (state.project[field] || "").trim();
    if (!cur || cur === (state.project.autoUse?.[field] || "")) setField(field, value);
  };
  put("allowedUse", z.allowed); put("allowedUseBasis", z.basis);
  put("disallowedUse", z.disallowed); put("disallowedUseBasis", z.basis);
  state.project.autoUse = { allowedUse: z.allowed, allowedUseBasis: z.basis, disallowedUse: z.disallowed, disallowedUseBasis: z.basis };
  const bad = (z.registerUses || []).filter((j) => j.verdict === "불허");
  return ` / ${z.zone} 허용 용도 채움${z.ordinanceFound ? "" : "(조례 별표 미확인)"}`
    + (bad.length ? ` / ⚠ 대장 용도 중 불허 ${bad.length}건 — 서류 대조 탭 확인` : "");
}

// ── 건축물대장 (변경전 값) ───────────────────────────────────
// 리모델링·증축·용도변경은 기존 건축물 값이 출발점이다. 자동조회가 대장(공공데이터포털 건축HUB)까지
// 받아 오면 건축면적·규모·높이·주차와 층별 용도·면적을 **변경전 열**에 채운다.
// 변경후는 변경전과 같게 시작한다(증감 0) — 바뀌는 것만 증감(±)으로 넣으면 된다.
const hasBeforeValues = () =>
  num(state.values.buildingArea?.before) > 0 || state.floors.some((f) => num(f.beforeExcl) || num(f.beforeCommon));
const floorsHaveData = () =>
  state.floors.some((f) => f.floor || f.use || num(f.excl) || num(f.common) || num(f.beforeExcl) || num(f.beforeCommon));

function applyRegister(d, { auto }) {
  if (!d?.hasBuildings) return;
  const bs = d.buildings;
  const mains = bs.filter((b) => !b.isAnnex);
  const ref = mains.length ? mains : bs;
  const f2 = (v) => (v == null ? "" : Number(v).toFixed(2));
  const sum = (xs) => xs.reduce((s, x) => s + (x || 0), 0);

  // 면적표에 이미 사람이 넣은 값이 있으면 묻고 덮는다(같은 땅을 다시 조회한 경우 등).
  const fillFloors = !floorsHaveData() ||
    confirm("각 층별 면적표에 이미 값이 있습니다.\n건축물대장 층별 용도·면적으로 다시 채울까요?\n(취소하면 설계개요 변경전 칸만 채웁니다)");

  // 설계개요 변경전 열
  setCell("buildingArea", "before", f2(d.totalBuildingArea));
  const above = Math.max(...ref.map((b) => b.floorsAbove || 0));
  const below = Math.max(...ref.map((b) => b.floorsBelow || 0));
  setCell("scale", "before", [below ? `지하 ${below}층` : "", above ? `지상 ${above}층` : ""].filter(Boolean).join(", ")
    + (bs.length > 1 ? ` (${bs.length}개동)` : ""));
  const heights = ref.map((b) => b.height).filter((h) => h > 0);
  if (heights.length) setCell("height", "before", f2(Math.max(...heights)));
  const pin = sum(bs.map((b) => b.parkingIndoor)), pout = sum(bs.map((b) => b.parkingOutdoor));
  const pmech = sum(bs.map((b) => b.parkingMechanical));
  setCell("parkingType", "before", `${pmech ? "자주식+기계식" : "자주식"} (대장: 옥내 ${pin}대, 옥외 ${pout}대)`);
  setCell("parkingIn", "before", `${pin} 대`);
  if (!state.project.primaryUse && ref[0]?.mainUse) setField("primaryUse", ref[0].mainUse);
  // 변경후는 변경전과 같게 채워 둔다(리모델링은 대부분 그대로) — 바뀌는 것만 증감이나 변경후를 고친다.
  setCell("buildingArea", "delta", "");
  for (const k of ["buildingArea", "scale", "height"])
    if (!String(state.values[k]?.after || "").trim() && state.values[k]?.before) setCell(k, "after", state.values[k].before);

  // 각 층별 면적표 — 대장의 층별개요를 아래층부터. 변경후 용도·면적은 변경전과 같게(증감 0) 시작.
  let rows = 0, floorSum = 0;
  if (fillFloors) {
    state.floors = [];
    for (const b of bs) {
      for (const fl of b.floors) {
        const a = f2(fl.area);
        state.floors.push({
          bldg: b.name, floor: fl.label, beforeUse: fl.use, use: fl.use,
          beforeExcl: a, beforeCommon: "", delta: "", excl: a, common: "",
          exclude: !!fl.excludedFromGross,
        });
        rows++; floorSum += fl.area || 0;
      }
    }
    renderAreaTable();
  }

  // 층별 합이 대장 연면적과 다르면 알린다 — 층별개요 누락·중복 등록이 실제로 있다.
  let check = "";
  if (fillFloors && d.totalGrossFloorArea && Math.abs(floorSum - d.totalGrossFloorArea) > 1)
    check = ` / ⚠ 층별 합 ${fmt(floorSum)}㎡ ≠ 대장 연면적 ${fmt(d.totalGrossFloorArea)}㎡ — 서류 대조 탭 확인`;

  const approval = ref.map((b) => b.approvalDate).filter(Boolean)[0] || "";
  const msg = `건축물대장 — ${bs.length}개동(${ref[0]?.mainUse || ""}${ref[0]?.structure ? ", " + ref[0].structure : ""})`
    + ` / 건축면적 ${fmt(d.totalBuildingArea)}㎡ · 연면적 ${fmt(d.totalGrossFloorArea)}㎡`
    + (approval ? ` / 사용승인 ${approval}` : "")
    + (fillFloors ? ` / 면적표 ${rows}행(변경전) 채움 — 바뀌는 것만 증감(±) 입력` : " / 면적표는 그대로 둠")
    + check;
  status(auto ? $("#status").textContent + " / " + msg : msg);
  renderDocs();
  recalc({ quiet: true });
}

// ── 서류 대조 탭 ─────────────────────────────────────────────
// 건축물대장 · 토지대장 · 토지이용계획확인원을 같은 항목끼리 맞춰 본다. 숫자가 다르면 검토 전제가 틀린 것이다.
// 값의 출처: 자동조회(API) + 발급본 PDF(API에 없는 소유자·위반건축물·건축물대장 지목).
const USE_ZONES = ["제1종전용주거지역", "제2종전용주거지역", "제1종일반주거지역", "제2종일반주거지역", "제3종일반주거지역",
  "준주거지역", "중심상업지역", "일반상업지역", "근린상업지역", "유통상업지역", "전용공업지역", "일반공업지역", "준공업지역",
  "보전녹지지역", "생산녹지지역", "자연녹지지역", "보전관리지역", "생산관리지역", "계획관리지역", "농림지역", "자연환경보전지역"];
const pickZone = (zs) => {
  for (const z of zs || []) { const n = z.replace(/\s/g, ""); const hit = USE_ZONES.find((u) => n.includes(u)); if (hit) return hit; }
  return "";
};
const normAddr = (a) => String(a || "").replace(/번지|\s/g, "").replace(/^(.*?[시도])/, "");
const vClass = (v) => (/불일치|초과|불허|위반/.test(v) ? "v-ng" : /확인|조건|대기/.test(v) ? "v-warn" : "v-ok");
const DOC_KINDS = ["building", "land", "plan"];
const NO_PDF = "PDF 넣으면 표시";

/// 같은 항목의 값들을 비교한다. 비교할 값이 둘 이상이면 일치/불일치, 하나뿐이면 그 서류만 기재.
function compareValues(vals, eq = (a, b) => a === b) {
  const xs = vals.filter((v) => v !== null && v !== undefined && v !== "");
  if (xs.length < 2) return xs.length ? "1개 서류만 기재" : "자료 없음";
  return xs.every((x) => eq(x, xs[0])) ? "일치" : "불일치";
}

function renderDocs() {
  const body = $("#docsBody");
  if (!body) return;
  renderDocCards();
  const D = state.docs;
  if (!D) {
    body.innerHTML = `<tr><td colspan="6" class="empty">설계개요에서 대지위치를 조회하면 표시됩니다.</td></tr>`;
    $("#regCheckBody").innerHTML = `<tr><td colspan="5" class="empty">설계개요에서 대지위치를 조회하면 표시됩니다.</td></tr>`;
    renderUseTable();
    return;
  }
  const R = D.register;
  const has = !!R?.hasBuildings;
  const b0 = has ? (R.buildings.find((b) => !b.isAnnex) || R.buildings[0]) : null;
  const P = D.pdf || {};
  const pb = P.building?.info, pl = P.land?.info, pp = P.plan?.info;
  const regMissing = !R ? `조회 실패${D.registerError ? ": " + D.registerError : ""}` : !has ? "등록된 건축물 없음" : "";
  const rows = [];
  const add = (item, b, l, p, verdict, note = "") => rows.push({ item, b, l, p, verdict, note });
  const area = (v) => (v == null ? "" : `${fmt(v)} ㎡`);

  // 1. 대지위치
  add("대지위치", regMissing || b0.address, D.address, D.address,
    has ? compareValues([normAddr(b0.address), normAddr(D.address)]) : "자료 없음",
    has && normAddr(b0.address) !== normAddr(D.address) ? "표기가 다릅니다 — 여러 필지에 걸친 건축물(부속지번)인지 확인" : "");

  // 2. 대지면적 — 건축물대장 대지면적 / 토지대장 면적 / 토지이용계획확인원 면적(PDF)
  const aB = has ? R.siteArea : null, aL = pl?.area ?? D.landArea, aP = pp?.area ?? null;
  const aV = compareValues([aB, aL, aP], (x, y) => Math.abs(x - y) < 0.01);
  add("대지면적", regMissing || area(aB) || "공란", area(aL) || "공란", area(aP) || (pp ? "공란" : NO_PDF), aV,
    aV === "불일치" ? "면적이 서로 다릅니다 — 분할·합병·지적정리 여부 확인. 검토는 토지대장 면적 기준" : "");

  // 3. 지목 — 건축물대장(PDF, 공란이면 공란) / 토지대장 / 토지이용계획확인원(PDF)
  const cB = pb?.category ?? "", cL = pl?.category ?? D.category ?? "", cP = pp?.category ?? "";
  const cV = compareValues([cB, cL, cP]);
  add("지목", regMissing || (pb ? cB || "공란" : NO_PDF), cL || "공란", pp ? cP || "공란" : NO_PDF, cV,
    (cV === "불일치" ? "지목이 서로 다릅니다 — 지적공부 정리 여부 확인. " : "")
    + (cL && cL !== "대" ? "지목이 '대'가 아니면 건축 시 형질변경·지목변경 필요 여부 확인" : ""));

  // 4. 용도지역 — 건축물대장 지역 / 토지대장(기재 없음) / 토지이용계획확인원
  const zB = has ? pickZone(R.zones) : "", zP = pickZone(pp?.zones?.length ? pp.zones : D.zones);
  const zV = compareValues([zB, zP]);
  add("용도지역", regMissing || zB || "공란", "-", zP || "공란", zV,
    zV === "불일치" ? "대장 기재 이후 용도지역이 바뀌었을 수 있습니다 — 검토는 현행 토지이용계획 기준" : "");

  // 5. 소유자 — 성명 + 주민(법인)등록번호 앞 6자리, 2인 이상이면 전원 대조
  const ownersText = (info) => !info ? NO_PDF : !info.hasText ? "글자 없는 PDF"
    : info.owners.length ? info.owners.map((o) => `${o.name} (${o.idFront})`).join(", ") : "소유자를 찾지 못함";
  let oV = "PDF 대기", oNote = "건축물대장·토지대장 발급본 PDF를 넣으면 성명·주민번호 앞자리로 대조합니다";
  if (pb?.owners?.length && pl?.owners?.length) {
    const key = (o) => `${o.name.replace(/\s/g, "")}|${o.idFront}`;
    const landKeys = new Set(pl.owners.map(key));
    const miss = pb.owners.filter((o) => !landKeys.has(key(o)));
    const extra = pl.owners.filter((o) => !pb.owners.some((b) => key(b) === key(o)));
    oV = miss.length ? "불일치" : "일치";
    oNote = (miss.length ? `토지대장에 없는 건물 소유자: ${miss.map((o) => o.name).join(", ")}. ` : `건물 소유자 ${pb.owners.length}명 모두 토지대장과 같음. `)
      + (extra.length ? `토지대장에만 있는 이름: ${extra.map((o) => o.name).join(", ")} (과거 소유자 이력일 수 있음 — 최종 소유자 확인)` : "");
  }
  add("소유자", regMissing || ownersText(pb), ownersText(pl), "-", oV, oNote);

  // 6. 위반건축물 — 건축물대장 발급본 상단 표기
  const vText = !pb ? NO_PDF : !pb.hasText ? "글자 없는 PDF" : pb.violation ? "위반건축물 표기 있음" : "표기 없음";
  add("위반건축물", regMissing || vText, "-", "-", !pb ? "PDF 대기" : pb.violation ? "위반" : "해당 없음",
    pb?.violation ? "위반 내용 해소(시정) 전에는 허가·용도변경이 제한됩니다 — 대장 '변동사항'란 확인" : "");

  body.innerHTML = rows.map((r) =>
    `<tr><th>${esc(r.item)}</th><td>${esc(r.b)}</td><td>${esc(r.l)}</td><td>${esc(r.p)}</td>` +
    `<td class="${vClass(r.verdict)}">${esc(r.verdict)}</td><td class="sub">${esc(r.note)}</td></tr>`).join("");
  renderRegChecks(D, R, has, b0, regMissing);
  renderUseTable();
}

/// 건축물대장 기준 검토 — 기존 건축물이 현행 기준에 맞는지 (증축 가능 여부·기존 부적합).
function renderRegChecks(D, R, has, b0, regMissing) {
  const body = $("#regCheckBody");
  if (!has) {
    body.innerHTML = `<tr><td colspan="5" class="empty">${esc(regMissing || "건축물대장 자료가 없습니다")} — 설계개요에서 '자동조회'를 다시 누르세요.</td></tr>`;
    return;
  }
  const rows = [];
  const add = (item, reg, std, verdict, note = "") => rows.push({ item, reg, std, verdict, note });
  const L = D.limits || {};
  const site = D.landArea || R.siteArea;
  const bc = R.recap?.coverageRatio ?? (R.totalBuildingArea && site ? r2(R.totalBuildingArea / site * 100) : b0.coverageRatio);
  const far = R.recap?.floorAreaRatio ?? b0.floorAreaRatio;
  if (bc != null) add("건폐율", `${r2(bc)}%`, L.coverage != null ? `법정 ${L.coverage}% 이하` : "법정 한도 없음",
    L.coverage == null ? "자료 없음" : bc <= L.coverage + 0.005 ? "적합" : "초과",
    L.coverage != null && bc > L.coverage + 0.005 ? "현행 건폐율 초과(기존 부적합) — 증축 불가, 대수선·용도변경은 완화 규정 확인" : "");
  if (far != null) add("용적률", `${r2(far)}%`, L.far != null ? `법정 ${L.far}% 이하` : "법정 한도 없음",
    L.far == null ? "자료 없음" : far <= L.far + 0.005 ? "적합" : "초과",
    L.far != null && far > L.far + 0.005 ? "현행 용적률 초과(기존 부적합) — 연면적 증가 불가" : "");
  const floorSum = R.buildings.reduce((s, b) => s + b.floors.reduce((t, f) => t + (f.excludedFromGross ? 0 : f.area || 0), 0), 0);
  if (R.totalGrossFloorArea) add("연면적", `표제부 ${fmt(R.totalGrossFloorArea)} ㎡`, `층별개요 합 ${fmt(floorSum)} ㎡`,
    Math.abs(floorSum - R.totalGrossFloorArea) <= 1 ? "일치" : "불일치",
    Math.abs(floorSum - R.totalGrossFloorArea) <= 1 ? "" : "대장 안에서 표제부와 층별개요가 다릅니다");
  const js = D.zoneUse?.registerUses || [];
  const rank = (v) => ({ "불허": 3, "확인 필요": 2, "조건부 허용": 1 }[v] || 0);
  const worst = js.reduce((w, j) => (rank(j.verdict) > rank(w?.verdict) ? j : w), js[0]);
  add("대장 용도", b0.mainUse || "-", `${pickZone(D.zones) || "-"} 허용 용도`, worst ? worst.verdict : "자료 없음",
    !worst ? "용도지역 규칙을 찾지 못했습니다(법제처 키 확인)"
      : rank(worst.verdict) === 0 ? `층별 용도 ${js.length}종 모두 허용 — 아래 표`
      : `${worst.use}: ${worst.reason} — 아래 표`);
  const appr = b0.approvalDate || "";
  const years = appr ? Math.floor((Date.now() - new Date(appr)) / (365.25 * 864e5)) : null;
  add("사용승인일", appr || "공란", years == null ? "" : `${years}년 경과`, appr ? "확인" : "자료 없음",
    state.mode === "용도변경" && years >= 5 && (R.totalGrossFloorArea || 0) < 1000
      ? "사용승인 5년 경과·연면적 1천㎡ 미만 — 용도변경 시 부설주차장 추가 설치 면제 검토(주차장법 시행령 제6조제4항)" : "");
  const pin = R.buildings.reduce((s, b) => s + (b.parkingIndoor || 0), 0), pout = R.buildings.reduce((s, b) => s + (b.parkingOutdoor || 0), 0);
  add("주차대수", `옥내 ${pin}대 · 옥외 ${pout}대 (계 ${pin + pout}대)`, "설계개요 주차 행 법정 대수", "확인",
    "변경후 법정 대수와의 차이는 설계개요 주차 '증감' 칸");
  body.innerHTML = rows.map((r) =>
    `<tr><th>${esc(r.item)}</th><td>${esc(r.reg)}</td><td>${esc(r.std)}</td>` +
    `<td class="${vClass(r.verdict)}">${esc(r.verdict)}</td><td class="sub">${esc(r.note)}</td></tr>`).join("");
}

// ── 서류 카드: 발급·열람 사이트 열기 / PDF 넣기 ────────────────────────
const DOC_SITE = {
  building: () => "https://www.gov.kr/search?srhQuery=" + encodeURIComponent("건축물대장 발급"),
  land: () => "https://www.gov.kr/search?srhQuery=" + encodeURIComponent("토지대장 발급"),
  plan: () => state.project.pnu
    ? `https://www.eum.go.kr/web/ar/lu/luLandDet.jsp?mode=search&isNoScr=script&pnu=${state.project.pnu}`
    : "https://www.eum.go.kr/web/ar/lu/luLandDet.jsp",
};

function renderDocCards() {
  const P = state.docs?.pdf || {};
  for (const card of $$(".doc-card")) {
    const k = card.dataset.kind, p = P[k];
    const st = card.querySelector(".doc-status");
    if (!p) { st.textContent = state.docs ? "PDF 없음" : "먼저 설계개요에서 대지위치를 조회하세요"; continue; }
    const i = p.info;
    st.textContent = !i.hasText ? `${p.fileName} — ${i.excerpt}`
      : `${p.fileName} — ` + [
          i.owners?.length ? `소유자 ${i.owners.length}명` : "",
          i.violation === true ? "위반건축물 표기 있음" : i.violation === false ? "위반 표기 없음" : "",
          i.category ? `지목 ${i.category}` : "", i.area ? `면적 ${fmt(i.area)}㎡` : "",
          i.zones?.length ? i.zones.join(", ") : "",
        ].filter(Boolean).join(" · ");
  }
}

for (const btn of $$("[data-open]")) {
  btn.addEventListener("click", async () => {
    const r = await (await fetch("/api/open-url", {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ path: DOC_SITE[btn.dataset.open]() }),
    })).json();
    status(r.ok ? "브라우저에서 열었습니다 — 발급·열람 후 PDF로 저장해 'PDF 넣기'로 불러오세요" : r.message);
  });
}

for (const card of $$(".doc-card")) {
  const input = card.querySelector("input[type=file]");
  input.addEventListener("change", async () => {
    const file = input.files?.[0];
    input.value = "";
    if (!file) return;
    if (!state.docs) { status("먼저 설계개요에서 대지위치를 조회하세요"); return; }
    const fd = new FormData();
    fd.append("kind", card.dataset.kind);
    fd.append("file", file);
    card.querySelector(".doc-status").textContent = "읽는 중…";
    try {
      const r = await (await fetch("/api/docs/parse", { method: "POST", body: fd })).json();
      if (!r.ok) { status(r.message); renderDocCards(); return; }
      (state.docs.pdf ||= {})[card.dataset.kind] = { fileName: r.fileName, info: r.info };
      renderDocs();
      autosave();
      status(`${card.querySelector("b").textContent} PDF 반영 — 서류 대조표를 확인하세요`);
    } catch (e) {
      status("PDF 읽기 실패: " + e.message);
      renderDocCards();
    }
  });
}

/// 용도 적합 표: 대장 용도(변경전) + 면적표·설계개요의 변경후 용도.
let useJudgments = {};   // 변경후 용도 → 판정 (서버에서 받아 둔 것)
function renderUseTable() {
  const body = $("#useBody");
  const D = state.docs;
  if (!body) return;
  const z = D?.zoneUse;
  if (!z) {
    body.innerHTML = `<tr><td colspan="5" class="empty">용도지역 규칙을 찾지 못했습니다 — 법제처 키와 대지위치 조회를 확인하세요.</td></tr>`;
    $("#useNote").textContent = "";
    return;
  }
  $("#useNote").textContent = `${z.zone} · ${z.permissive ? "건축할 수 있는 건축물 목록에 있어야 허용" : "건축할 수 없는 건축물 목록에 있으면 불허"} — 근거: ${z.basis}. 지구단위계획이 있으면 그 허용·불허 용도가 우선합니다.`;
  const row = (kind, j) =>
    `<tr><th>${kind}</th><td>${esc(j.use)}</td><td class="${vClass(j.verdict)}">${esc(j.verdict)}</td>` +
    `<td class="sub">${esc(j.citation)}</td><td class="sub">${esc(j.reason)}${j.condition ? "<br>" + esc(j.condition) : ""}</td></tr>`;
  const regRows = (z.registerUses || []).map((j) => row("대장(변경전)", j));
  const afterUses = changedAfterUses();
  const afterRows = afterUses.map((u) => useJudgments[u] ? row("변경후", useJudgments[u])
    : `<tr><th>변경후</th><td>${esc(u)}</td><td class="v-warn">판정 중…</td><td></td><td></td></tr>`);
  body.innerHTML = [...regRows, ...afterRows].join("") ||
    `<tr><td colspan="5" class="empty">판정할 용도가 없습니다.</td></tr>`;
}

/// 대장에 없는 변경후 용도 (용도변경으로 바뀌는 것 + 설계개요 용도 칸)
function changedAfterUses() {
  // 대장에 이미 있는 용도(층별 + 표제부 주용도)는 '변경후'로 다시 판정하지 않는다
  const regUses = new Set([...(state.docs?.zoneUse?.registerUses || []).map((j) => j.use),
    ...(state.docs?.register?.buildings || []).map((b) => b.mainUse)]);
  const uses = [...state.floors.map((f) => (f.use || "").trim()), (state.project.primaryUse || "").trim()];
  return [...new Set(uses.filter((u) => u && !regUses.has(u)))];
}

let useJudgeTimer = null;
function scheduleUseJudge() {
  clearTimeout(useJudgeTimer);
  useJudgeTimer = setTimeout(judgeAfterUses, 400);
}
async function judgeAfterUses() {
  const D = state.docs;
  if (!D?.zoneUse) return;
  const uses = changedAfterUses().filter((u) => !useJudgments[u]);
  if (uses.length) {
    try {
      const r = await (await fetch("/api/zone-use/judge", {
        method: "POST", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ province: D.province, city: D.city, zones: D.zones, uses }),
      })).json();
      for (const j of r.judgments || []) useJudgments[j.use] = j;
    } catch { /* 다음 입력 때 다시 */ }
  }
  renderUseTable();
}

/// 설계개요 표의 특정 칸에 값을 넣는다(사용자 입력 칸도 채워야 하므로 setCalc과 별도).
function setCell(key, slot, value) {
  (state.values[key] ||= {})[slot] = value;
  const el = document.querySelector(`.cell[data-key="${key}"][data-slot="${slot}"]`);
  if (el) el.textContent = value;
}

function setField(f, v) {
  state.project[f] = v;
  const el = document.querySelector(`.head .cell[data-field="${f}"]`);
  if (el) el.textContent = v;
  if (f === "projectName") stampProject();
}

/// 인쇄물 머리에 사업명을 남긴다 — 탭마다 쪽이 갈리므로 여러 장이 흩어져도 어느 건인지 알아야 한다.
/// (CSS의 .sheet[data-project]::before가 이 값을 찍는다)
function stampProject() {
  const name = (state.project.projectName || "").trim();
  $$(".sheet").forEach((s) => { s.dataset.project = name; });
}

// 인쇄는 화면 그대로 쓴다(별도 서식 생성 없음). 쪽 나눔·머리행 반복은 app.css의 @media print가 한다.
$("#btnPrint").addEventListener("click", () => { stampProject(); window.print(); });

// ── 모드 · 탭 ────────────────────────────────────────────────
$("#modes").addEventListener("click", (e) => {
  const b = e.target.closest(".mode"); if (!b) return;
  $$(".mode").forEach((x) => x.classList.remove("active"));
  b.classList.add("active");
  state.mode = b.dataset.mode;
  document.body.dataset.mode = state.mode;
  syncAreaHeaders();
  renderScaleTable();  // 건축면적 변경후 칸이 증감(±) 입력으로 바뀐다
  renderAreaTable();   // 증감 표시가 모드에 따라 달라진다
  recalc();            // 기준 열(계획/변경후)이 바뀌므로 다시 계산한다
  status(`${state.mode} 모드`);
  // 주소 조회를 신축 모드에서 해 뒀다면, 기존 건축물 모드로 바꾸는 순간 대장 값을 채운다.
  if (state.mode !== "신축" && state.docs?.register?.hasBuildings && !hasBeforeValues())
    applyRegister(state.docs.register, { auto: true });
});

$("#tabs").addEventListener("click", (e) => {
  const b = e.target.closest(".tab"); if (!b) return;
  $$(".tab").forEach((x) => x.classList.remove("active"));
  $$(".page").forEach((x) => x.classList.remove("active"));
  b.classList.add("active");
  $("#page-" + b.dataset.page).classList.add("active");
  if (b.dataset.page === "docs") { renderDocs(); judgeAfterUses(); }
});

// ── 검토 실행 ────────────────────────────────────────────────
// 항목마다 조문 조회·AI 판정이 붙어 수 분 걸리므로 진행 상황을 보여준다.
$("#btnRun").addEventListener("click", () => runReview("basic"));
$("#btnRunDetail").addEventListener("click", () => runReview("detail"));

// stage=basic: 주요 법규까지(계약 전 단계) / detail: 장별 상세검토(필요할 때만)
async function runReview(stage) {
  const payload = buildProjectPayload();
  if (!payload.siteArea) { status("대지면적을 먼저 입력하세요."); return; }

  $("#runLog").textContent = "";
  $("#runBar").style.width = "0%";
  $("#runCurrent").textContent = "시작하는 중…";
  $("#dlgRun").querySelector("h3").textContent = stage === "detail" ? "장별 상세검토 실행 중" : "검토 실행 중";
  $("#dlgRun").showModal();

  const r = await fetch("/api/review/start?stage=" + stage, {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(payload),
  });
  const { jobId } = await r.json();
  pollReview(jobId, stage);
}

$("#btnRunClose").addEventListener("click", () => $("#dlgRun").close());

async function pollReview(jobId, stage = "basic") {
  const tick = async () => {
    const d = await (await fetch("/api/review/" + jobId)).json();
    const done = (d.progress || []).filter((m) => m.startsWith("검토 중:")).length;
    if (d.total) $("#runBar").style.width = Math.min(100, (done / d.total) * 100) + "%";
    $("#runCurrent").textContent = d.progress?.length ? d.progress[d.progress.length - 1] : "…";
    $("#runLog").textContent = (d.progress || []).slice(-40).join("\n");
    $("#runLog").scrollTop = $("#runLog").scrollHeight;

    if (!d.done) { setTimeout(tick, 900); return; }

    if (d.error) {
      $("#runCurrent").textContent = "실패: " + d.error;
      status("검토 실패: " + d.error);
      return;
    }
    $("#runBar").style.width = "100%";
    // 상세검토는 상세 탭만 갱신한다(기본 검토 결과를 지우지 않는다).
    if (stage === "detail") renderDetails(d.result.details);
    else renderResult(d.result);
    state.result = d.result;
    state.amendments = null;   // 방금 현행 법령으로 다시 검토했다
    const c = d.result.counts;
    $("#runCurrent").textContent = `완료 — 적용 ${c.적용} · 해당없음 ${c.해당없음} · 확인필요 ${c.확인필요}`;
    status(`검토 완료 — ${c.total}항목 (적용 ${c.적용} / 해당없음 ${c.해당없음} / 확인필요 ${c.확인필요})`);
    setTimeout(() => $("#dlgRun").close(), 1200);
  };
  tick();
}

/// 건축규모 칸에서 지상 층수만 읽는다. "지하 1층, 지상 5층"을 숫자만 모으면 15가 되므로
/// "지상 N층"을 먼저 찾고, 없으면 첫 숫자를 쓴다.
function floorsAbove(text) {
  const s = String(text || "");
  const m = s.match(/지상\s*(\d+)/) || s.match(/(\d+)/);
  return m ? parseInt(m[1]) : 0;
}

function buildProjectPayload() {
  const byBldg = floorsByBuilding();
  const zones = (state.project.useZones || "").split(",").map((s) => s.trim()).filter(Boolean);
  return {
    projectName: state.project.projectName,
    client: state.project.client,
    isPublic: !!state.project.isPublic,
    siteAddress: state.project.siteAddress,
    province: state.project.province,
    city: state.project.city,
    useZones: zones,
    siteArea: num(state.project.siteArea),
    primaryUse: state.project.primaryUse,
    allowedUse: state.project.allowedUse || null,
    disallowedUse: state.project.disallowedUse || null,
    mode: state.mode,
    plannedBuildingArea: buildingAreaInput(),
    existingBuildingArea: num(state.values.buildingArea?.before),
    plannedFloorsAbove: floorsAbove(state.values.scale?.[planSlot()]),
    // 높이·규모의 변경전·변경후 — AI가 "높이가 바뀌는지 알 수 없다"로 확인필요를 내지 않도록 넘긴다.
    plannedHeight: String(state.values.height?.[planSlot()] || "").trim() || null,
    plannedScale: String(state.values.scale?.[planSlot()] || "").trim() || null,
    existingHeight: String(state.values.height?.before || "").trim() || null,
    existingScale: String(state.values.scale?.before || "").trim() || null,
    zoning: {
      maxCoverageRatio: num(state.values.coverage?.legal) || null,
      maxFloorAreaRatio: num(state.values.floorRatio?.legal) || null,
      landscapeRatio: landscapeRatioInput(),
      source: state.values.coverage?.basis || "",
    },
    parking: {
      areaPerSpace: num(state.values.parkingOut?.legal) || 200,
      ecoRatio: ecoRatio(),
      parkingType: state.values.parkingType?.[planSlot()] || "",
      source: state.values.parkingType?.basis || "",
    },
    buildings: [...byBldg].map(([name, floors]) => ({ name, floors })),
  };
}

// ── 판정 수정 ────────────────────────────────────────────────
// AI 판정은 틀릴 수 있고, 검토서에 서명하는 것은 사람이다.
// 그래서 화면의 판정·사유는 전부 사람이 고칠 수 있어야 하고, 고친 값이 검토서로 나가야 한다.
// 인용 조문(법제처 원문)만은 고칠 수 없다 — 원문을 사람이 손대면 인용의 의미가 없어진다.
const edit = (id) => state.edits[id] || {};
const isEdited = (id) => Object.keys(edit(id)).length > 0;
const editCount = () => Object.keys(state.edits).length;

/// 화면에 보일 값. 사람이 고쳤으면 그 값, 아니면 AI 판정 값.
function shownValue(x, field) {
  const e = edit(x.id);
  return e[field] !== undefined ? e[field] : (x[field] ?? "");
}

/// 이 칸이 실제로 무엇을 고치는지 정한다 — **보이는 것을 고친다**가 원칙.
/// 설계기준(산정식) 칸은 산정식이 없으면 판정 사유가 대신 보이고(실무 서식이 그렇다,
/// 검토서 DOCX도 같은 순서로 적는다), 그때 이 칸을 고치면 사유를 고치는 것이다.
function boundField(x, field, fallback) {
  return fallback && !shownValue(x, field) ? fallback : field;
}

/// 판정 칸. 고른 즉시 반영되고, 고친 항목은 ↺로 AI 판정으로 되돌릴 수 있다.
function verdictHtml(x) {
  const v = shownValue(x, "verdict") || x.verdict;
  const opts = VERDICTS.map((o) => `<option value="${o}"${o === v ? " selected" : ""}>${o}</option>`).join("");
  return `<select class="verdict-pick v-${v}" data-edit-id="${x.id}" data-edit-field="verdict">${opts}</select>
    <button class="revert" data-revert="${x.id}" title="AI 판정으로 되돌리기">수정됨 ↺</button>`;
}

/// 편집 가능한 텍스트 칸. 설계개요와 같은 방식 — 칸에 갇히지 않고 내용만큼 늘어난다.
function editCell(x, field, opt = {}) {
  const bound = boundField(x, field, opt.fallback);
  const changed = edit(x.id)[bound] !== undefined ? " changed" : "";
  return `<div class="cell edit${changed} ${opt.cls || ""}" contenteditable="true"
    data-edit-id="${x.id}" data-edit-field="${bound}"
    data-ph="${opt.ph || "(비어 있음)"}">${esc(shownValue(x, bound))}</div>`;
}

// 어느 표의 판정이든 여기서 한 번에 받는다(행을 다시 그려도 계속 동작한다).
document.addEventListener("change", (e) => {
  const el = e.target.closest?.("select[data-edit-id]");
  if (el) applyEdit(el.dataset.editId, el.dataset.editField, el.value);
});

// blur는 버블링되지 않으므로 캡처 단계에서 받는다.
document.addEventListener("blur", (e) => {
  const el = e.target.closest?.(".cell.edit[data-edit-id]");
  if (el) applyEdit(el.dataset.editId, el.dataset.editField, cellText(el));
}, true);

document.addEventListener("click", (e) => {
  const btn = e.target.closest?.("[data-revert]");
  if (!btn) return;
  delete state.edits[btn.dataset.revert];
  if (state.result) renderResult(state.result);   // 되돌리기는 편집 중이 아니므로 다시 그려도 안전
  autosave();
  status(editCount() ? `판정 ${editCount()}건 수정됨` : "AI 판정으로 되돌렸습니다.");
});

function applyEdit(id, field, value) {
  if (!id) return;
  const row = findResultRow(id);
  const original = row?.[field] ?? "";
  const e = (state.edits[id] ||= {});
  if (value === original) delete e[field];      // 원래 값으로 되돌려 놓으면 수정이 아니다
  else e[field] = value;
  if (Object.keys(e).length === 0) delete state.edits[id];
  refreshEditMarks(id);
  autosave();
  status(editCount() ? `판정 ${editCount()}건 수정됨 — 검토서 저장 시 반영됩니다.` : "준비됨");
}

/// 표를 통째로 다시 그리면 지금 편집 중인 칸이 사라져 탭 이동이 끊긴다.
/// 그래서 고친 항목의 표시(색·되돌리기 버튼)만 손본다.
function refreshEditMarks(id) {
  const row = findResultRow(id);
  for (const el of $$("[data-edit-id]")) {
    if (el.dataset.editId !== id) continue;
    el.closest("tr")?.classList.toggle("row-edited", isEdited(id));
    if (el.tagName === "SELECT") { el.className = "verdict-pick v-" + el.value; continue; }
    el.classList.toggle("changed", edit(id)[el.dataset.editField] !== undefined);
    // 같은 값을 보여주는 칸이 여러 곳(상세검토 표와 지구단위계획 표 등)이면 함께 맞춘다.
    if (row && document.activeElement !== el) {
      const shown = shownValue(row, el.dataset.editField);
      if (cellText(el) !== shown) el.textContent = shown;
    }
  }
}

// contenteditable은 공백을 nbsp로 바꾸고 줄바꿈을 요소로 넣는다 — 평문으로 되돌린다.
const cellText = (el) => el.innerText.replace(/ /g, " ").replace(/\r/g, "").trim();

/// 결과 안에서 항목 하나를 찾는다(표마다 흩어져 있어 전부 훑는다).
function findResultRow(id) {
  const r = state.result;
  if (!r) return null;
  const pools = [r.summary, r.cert, r.district, r.site, ...(r.details || []).map((g) => g.items)];
  for (const pool of pools) {
    const hit = (pool || []).find((x) => x.id === id);
    if (hit) return hit;
  }
  return null;
}

/// 검토서 저장 시 서버로 보낼 수정 목록. 결과에 없는 항목(예전 검토의 잔재)은 빼고 보낸다.
function overridePayload() {
  return Object.entries(state.edits)
    .filter(([id]) => findResultRow(id))
    .map(([id, e]) => ({ id, ...e }));
}

// ── 법령 개정 감시 ───────────────────────────────────────────
// 검토서는 작성 후 몇 달씩 쓰이는데 그 사이 법이 바뀌면 인용 조문이 구법이 된다.
// 불러올 때 시행일자를 다시 맞춰 보고, 바뀐 법규를 검토법규 표에 표시한다.
// 고치지는 않는다 — 조문 번호와 내용이 함께 바뀌었을 수 있어 다시 검토할지는 사람이 정한다.
async function checkAmendments(projectName) {
  const laws = state.result?.laws;
  if (!laws?.length) return;

  try {
    const body = Object.fromEntries(laws.map((l) => [l.name, l.effectiveDate]));
    const r = await fetch("/api/laws/check", {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ laws: body }),
    });
    const d = await r.json();
    if (!d.ok || !d.changed?.length) return;

    state.amendments = new Map(d.changed.map((c) => [c.name, c]));
    renderResult(state.result);   // 표에 개정 표시를 입힌다
    status(`${projectName} — 검토 후 개정된 법규 ${d.changed.length}건: `
      + d.changed.map((c) => c.name).join(", ") + " (다시 검토하세요)");
  } catch { /* 개정 확인 실패가 불러오기를 막으면 안 된다 */ }
}

/// 개정된 법규의 비고 칸 문구 ("2026.07.28 시행으로 개정됨").
function amendmentNote(name) {
  const a = state.amendments?.get(name);
  if (!a) return "";
  const d = a.current;
  const shown = d.length === 8 ? `${d.slice(0, 4)}.${d.slice(4, 6)}.${d.slice(6)}` : d;
  return `<span class="amended">개정됨 → ${esc(shown)} 시행</span>`;
}

// ── 결과 렌더 (검토서 p4~p7) ─────────────────────────────────
function renderResult(res) {
  // p4 검토법규 (검토 후 개정된 법규는 비고에 표시)
  fill("#lawsBody", res.laws, (l) => `<tr><td class="body-cell">${esc(l.name)}</td>
    <td class="center">${esc(l.effectiveDate)}</td><td class="center">${amendmentNote(l.name)}</td></tr>`, 3);

  // p5 요약 / p6 인증 — 같은 5열 구성
  // 법적기준·설계기준(산정식)·판정·사유는 고칠 수 있다. 대상(근거 조문)은 인용이므로 읽기 전용.
  const sumRow = (x) => `<tr class="${isEdited(x.id) ? "row-edited" : ""}">
    <td class="body-cell">${esc(x.title)}</td>
    <td class="body-cell sub">${esc(x.basis)}</td>
    <td>${editCell(x, "criterion", { ph: "법적 기준" })}</td>
    <td>${editCell(x, "calculation", { fallback: "reason", ph: "산정식 또는 판정 사유" })}</td>
    <td class="center verdict-cell">${verdictHtml(x)}</td></tr>`;
  fill("#summaryBody", res.summary, sumRow, 5);
  fill("#certBody", res.cert, sumRow, 5);

  // p6 지구단위계획 / p7 해당 지번 — 구분|항목|내용|적용여부
  fill("#districtBody", res.district, detailRows, 4);
  fill("#siteBody", res.site, detailRows, 4);

  renderDetails(res.details);
}

/// 장별 상세검토 — 법령 하나가 한 행이 되도록 나눠서 어느 법을 적용했는지 바로 보이게 한다.
/// (표준 서식도 구분 → 항목(법령 조문) → 내용 순으로 행을 나눈다)
function renderDetails(details) {
  const host = $("#detailBody");
  host.innerHTML = "";
  if (!details || !details.length) {
    host.innerHTML = `<p class="todo">상세검토를 실행하면 제4~7장 등 장별 조문 검토가 표시됩니다.</p>`;
    return;
  }
  for (const g of details) {
    const div = document.createElement("div");
    div.className = "detail-group";
    const rows = g.items.map((x) => {
      const cites = x.citations?.length ? x.citations : [null];
      const cls = (i) => (i === 0 ? "item-start" : "cite-row") + (isEdited(x.id) ? " row-edited" : "");
      return cites.map((c, i) => `<tr class="${cls(i)}">
        ${i === 0 ? `<td class="body-cell item-name" rowspan="${cites.length}">${esc(x.title)}</td>` : ""}
        <td class="body-cell law-name">${c ? esc(c.law) + " " + esc(c.article) : "-"}
          ${c?.effectiveDate ? `<div class="sub">[시행 ${esc(c.effectiveDate)}]</div>` : ""}</td>
        <td class="body-cell">${c ? esc(c.body) : esc(shownValue(x, "reason"))}</td>
        ${i === 0 ? `<td class="center verdict-cell" rowspan="${cites.length}">${verdictHtml(x)}
            ${editCell(x, "reason", { cls: "reason-edit", ph: "판정 사유" })}</td>` : ""}
      </tr>`).join("");
    }).join("");
    div.innerHTML = `<h3>${esc(g.section)}</h3>` +
      `<table class="doc detail"><thead><tr>
         <th style="width:150px">구 분</th><th style="width:210px">항 목</th>
         <th>내 용</th><th style="width:150px">적용여부</th></tr></thead><tbody>${rows}</tbody></table>`;
    host.appendChild(div);
  }
}

const detailRows = (x) => `<tr class="${isEdited(x.id) ? "row-edited" : ""}">
  <td class="body-cell">${esc(x.title)}</td>
  <td class="body-cell sub">${x.citations.map((c) => esc(c.law + " " + c.article)).join("<br>")}</td>
  <td class="body-cell">${citeHtml(x)}</td>
  <td class="center verdict-cell">${verdictHtml(x)}</td></tr>`;

/// 인용 조문은 법제처 원문이라 읽기 전용이고, 그 아래 판정 사유만 고칠 수 있다.
function citeHtml(x) {
  const cites = (x.citations || []).map((c) => `<div class="cite">
      <div class="cite-head">${esc(c.law)} ${esc(c.article)}${c.articleTitle ? "(" + esc(c.articleTitle) + ")" : ""}
        ${c.effectiveDate ? `<span class="sub">[시행 ${esc(c.effectiveDate)}]</span>` : ""}</div>
      <div class="cite-body">${esc(c.body)}</div></div>`).join("");
  return cites + `<div class="reason-wrap"><b>판정 사유</b>
    ${editCell(x, "reason", { cls: "reason-edit", ph: "판정 사유" })}</div>`;
}

function fill(sel, items, rowFn, cols) {
  const el = $(sel);
  el.innerHTML = items?.length
    ? items.map(rowFn).join("")
    : `<tr><td colspan="${cols}" class="empty">해당 항목이 없습니다.</td></tr>`;
}

const esc = (s) => String(s ?? "").replace(/[&<>]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;" }[c]));

// ── 검토서 저장 (DOCX) ───────────────────────────────────────
$("#btnReport").addEventListener("click", async () => {
  const payload = buildProjectPayload();
  if (!payload.projectName) payload.projectName = state.project.projectName || "무제";
  status("검토서 생성 중…");
  const r = await fetch("/api/report", {
    method: "POST", headers: { "Content-Type": "application/json" },
    // overrides = 화면에서 사람이 고친 판정. 검토서에는 이 값이 나간다.
    body: JSON.stringify({ project: payload, overrides: overridePayload() }),
  });
  const d = await r.json();
  if (!d.ok) { status("검토서 저장 실패: " + d.message); return; }
  status(`검토서 저장됨 — ${d.path} (${(d.size / 1024).toFixed(0)} KB)` +
    (d.edited ? ` · 수정한 판정 ${d.edited}건 반영` : ""));
  if (confirm(`검토서를 저장했습니다.\n${d.path}\n\n지금 열까요?`)) {
    await fetch("/api/report/open", {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ path: d.path }),
    });
  }
});

// ── 프로젝트 저장 / 불러오기 ─────────────────────────────────
// 입력이 날아가지 않는 게 먼저다: 변경할 때마다 이 PC에 자동 저장하고,
// 이름을 붙여 저장한 것은 언제든 다시 불러온다.
const AUTOSAVE_KEY = "lawreview.autosave";

function snapshot() {
  return {
    version: 1, savedAt: new Date().toISOString(),
    mode: state.mode, project: state.project, values: state.values,
    floors: state.floors, result: state.result || null, edits: state.edits,
    docs: state.docs || null,
  };
}

function restore(s) {
  if (!s) return;
  state.mode = s.mode || "신축";
  state.project = Object.assign(state.project, s.project || {});
  state.values = s.values || {};
  state.floors = s.floors || [];
  state.result = s.result || null;
  state.edits = s.edits || {};
  state.docs = s.docs || null;
  useJudgments = {};
  document.body.dataset.mode = state.mode;
  $$(".mode").forEach((b) => b.classList.toggle("active", b.dataset.mode === state.mode));
  renderScaleTable();
  renderAreaTable();
  bindHeadFields();
  stampProject();
  syncAreaHeaders();
  if (state.result) renderResult(state.result);
  applyParkingBasis();
  renderDocs();
}

let autosaveTimer = null;
function autosave() {
  clearTimeout(autosaveTimer);
  autosaveTimer = setTimeout(() => {
    try { localStorage.setItem(AUTOSAVE_KEY, JSON.stringify(snapshot())); } catch { /* 용량 초과 무시 */ }
  }, 400);
}
document.addEventListener("input", autosave, true);
document.addEventListener("change", autosave, true);

$("#btnSave").addEventListener("click", async () => {
  const suggested = state.project.projectName || "무제 프로젝트";
  const name = prompt("저장할 이름을 입력하세요.", suggested);
  if (!name) return;
  const r = await fetch("/api/projects/" + encodeURIComponent(name), {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(snapshot()),
  });
  status(r.ok ? `저장됨 — ${name}` : "저장 실패");
});

$("#btnOpen").addEventListener("click", async () => {
  const list = await (await fetch("/api/projects")).json();
  const host = $("#projectList");
  host.innerHTML = list.length
    ? list.map((p) => `<div class="proj-row" data-name="${esc(p.name)}">
        <div><div class="proj-name">${esc(p.name)}</div><div class="sub">${esc(p.savedAt)}</div></div>
        <div><button class="btn sm proj-open">열기</button>
             <button class="btn sm ghost proj-del">삭제</button></div></div>`).join("")
    : `<p class="todo">저장된 프로젝트가 없습니다.</p>`;
  $("#dlgOpen").showModal();
});

$("#btnOpenClose").addEventListener("click", () => $("#dlgOpen").close());

$("#projectList").addEventListener("click", async (e) => {
  const row = e.target.closest(".proj-row");
  if (!row) return;
  const name = row.dataset.name;
  if (e.target.classList.contains("proj-open")) {
    const s = await (await fetch("/api/projects/" + encodeURIComponent(name))).json();
    restore(s);
    $("#dlgOpen").close();
    status(`불러옴 — ${name}`);
    checkAmendments(name);   // 저장해 둔 사이 법이 바뀌었는지 (응답을 기다리지 않는다)
  } else if (e.target.classList.contains("proj-del")) {
    if (!confirm(`"${name}" 프로젝트를 삭제할까요?`)) return;
    await fetch("/api/projects/" + encodeURIComponent(name), { method: "DELETE" });
    row.remove();
    status(`삭제됨 — ${name}`);
  }
});

// ── 설정 ─────────────────────────────────────────────────────
$("#btnSettings").addEventListener("click", () => openSettings());

/// 설정 창을 연다. notice가 있으면 왜 열렸는지 창 맨 위에 적는다(키가 없어 조회가 막힌 경우 등).
async function openSettings(notice) {
  const box = $("#setNotice");
  box.textContent = notice || "";
  box.hidden = !notice;
  const s = await (await fetch("/api/settings")).json();
  $("#setModel").value = s.claudeModel || "claude-sonnet-5-5";
  $("#setVworldDomain").value = s.vworldDomain || "http://localhost";
  $("#setMoleg").placeholder = s.hasMoleg ? "저장됨 (변경 시 입력)" : "미입력";
  $("#setClaude").placeholder = s.hasClaude ? "저장됨 (변경 시 입력)" : "미입력";
  $("#setVworld").placeholder = s.hasVworld ? "저장됨 (변경 시 입력)" : "미입력";
  $("#setDataGoKr").placeholder = s.hasDataGoKr ? "저장됨 (변경 시 입력)" : "미입력";
  $("#dlgSettings").showModal();
}

$("#btnSaveSettings").addEventListener("click", async () => {
  const body = {
    molegApiKey: $("#setMoleg").value || null,
    claudeApiKey: $("#setClaude").value || null,
    vworldApiKey: $("#setVworld").value || null,
    dataGoKrApiKey: $("#setDataGoKr").value || null,
    claudeModel: $("#setModel").value,
    vworldDomain: $("#setVworldDomain").value,
  };
  await fetch("/api/settings", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
  status("설정 저장됨");
});

// ── 시작 ─────────────────────────────────────────────────────
document.body.dataset.mode = state.mode;
renderScaleTable();
applyParkingBasis();
renderAreaTable();
bindHeadFields();
stampProject();
syncAreaHeaders();

// 앱을 닫았다 열어도 직전 작업이 남아 있어야 한다.
try {
  const saved = localStorage.getItem(AUTOSAVE_KEY);
  if (saved) {
    restore(JSON.parse(saved));
    status("직전 작업을 복원했습니다. (저장하려면 상단 '저장')");
  } else {
    status("준비됨 — 대지위치(지번·도로명)를 입력하고 Enter를 누르면 자동으로 조회합니다");
  }
} catch {
  status("준비됨 — 대지위치(지번·도로명)를 입력하고 Enter를 누르면 자동으로 조회합니다");
}

// 키가 없으면 조회·검토가 전부 막힌다. 실행하자마자 알려야 한다 — 상태줄 한 줄로는
// 못 보고 넘어간다(실제로 "주소 조회가 안 된다"로 오인한 일이 있었다).
(async () => {
  try {
    const s = await (await fetch("/api/settings")).json();
    const missing = [!s.hasVworld && "VWorld(주소 조회)", !s.hasMoleg && "법제처 OC(조문 조회)",
      !s.hasDataGoKr && "공공데이터포털(건축물대장)"].filter(Boolean);
    if (missing.length) status(`설정 필요 — ${missing.join(", ")} 키가 없습니다. 상단 "설정"에서 입력하세요`);
  } catch { /* 호스트가 아직 안 떴으면 조용히 넘어간다 */ }
})();
