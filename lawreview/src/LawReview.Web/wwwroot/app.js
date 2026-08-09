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
};

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
  $$(".head .cell[data-field]").forEach((el) => {
    const f = el.dataset.field;
    el.textContent = state.project[f] || "";
    el.addEventListener("input", () => { state.project[f] = el.textContent.trim(); });
    el.addEventListener("blur", recalc);
  });
}

// ── 면적표 ───────────────────────────────────────────────────
// 연면적(용적률 산정 기준)의 출처. 엑셀에서 행을 복사해 붙여넣을 수 있어야 한다.
function renderAreaTable() {
  const body = $("#areaBody");
  body.innerHTML = "";
  if (state.floors.length === 0) addFloorRow(false);

  state.floors.forEach((f, i) => {
    const tr = document.createElement("tr");
    tr.appendChild(areaCell(i, "bldg", "동"));
    tr.appendChild(areaCell(i, "floor", "층"));
    tr.appendChild(areaCell(i, "use", "용도"));
    tr.appendChild(areaCell(i, "excl", "0.00", true));
    tr.appendChild(areaCell(i, "common", "0.00", true));

    const sum = document.createElement("td");
    sum.className = "total";
    sum.textContent = fmt(num(f.excl) + num(f.common));
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

function areaCell(i, field, ph, isNum) {
  const td = document.createElement("td");
  const d = document.createElement("div");
  d.className = "cell" + (isNum ? " num" : "");
  d.contentEditable = "true";
  d.dataset.ph = ph;
  d.dataset.row = i;
  d.dataset.field = field;
  d.textContent = state.floors[i][field] ?? "";
  d.addEventListener("input", () => { state.floors[i][field] = d.textContent.trim(); });
  d.addEventListener("blur", () => { renderAreaTable(); recalc(); });
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
  });
  renderAreaTable();
  recalc();
  status(`면적표 ${rows.length}행 붙여넣기`);
}

const blankFloor = () => ({ bldg: "", floor: "", use: "", excl: "", common: "", exclude: false });
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
}

const fmt = (v) => (v || 0).toLocaleString("ko-KR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

// ── 계산 (엔진 호출) ─────────────────────────────────────────
let recalcTimer = null;
function recalc() {
  clearTimeout(recalcTimer);
  recalcTimer = setTimeout(doRecalc, 150);
}

async function doRecalc() {
  const siteArea = num(state.project.siteArea);
  if (!siteArea) return;

  // 면적표를 동 단위로 묶어 엔진 모델(Buildings/Floors)로 보낸다.
  const byBldg = new Map();
  for (const f of state.floors) {
    if (!num(f.excl) && !num(f.common)) continue;
    const name = f.bldg || "동";
    if (!byBldg.has(name)) byBldg.set(name, []);
    byBldg.get(name).push({
      floorLabel: f.floor, use: f.use,
      exclusiveArea: num(f.excl), commonArea: num(f.common),
      excludeFromGrossArea: !!f.exclude,
    });
  }

  const payload = {
    projectName: state.project.projectName,
    client: state.project.client,
    siteAddress: state.project.siteAddress,
    province: state.project.province,
    city: state.project.city,
    siteArea,
    primaryUse: state.project.primaryUse,
    plannedBuildingArea: buildingAreaInput(),
    zoning: {
      maxCoverageRatio: num(state.values.coverage?.legal) || null,
      maxFloorAreaRatio: num(state.values.floorRatio?.legal) || null,
      landscapeRatio: landscapeRatioInput(),
      source: state.values.coverage?.basis || "",
    },
    parking: {
      areaPerSpace: num(state.values.parkingOut?.legal) || 200,
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
    }
    warnCompliance(o.compliance);
    status(`계산 갱신됨 — 연면적 ${fmt(o.grossFloorArea)} ㎡`);
  } catch { /* 입력 중 부분 오류는 무시 */ }
}

// 건축면적은 사용자가 직접 넣는 값(설계 결과물).
const buildingAreaInput = () => num(state.values.buildingArea?.[planSlot()]);

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
$("#btnLookup").addEventListener("click", async () => {
  status("주소 조회 중…");
  const r = await fetch("/api/lookup", {
    method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ address: state.project.siteAddress }),
  });
  const d = await r.json();
  if (!d.ok) { status("조회 실패: " + d.message); return; }

  state.project.province = d.province; state.project.city = d.city;
  setField("useZones", (d.zones || []).join(", "));
  if (d.area) setField("siteArea", Number(d.area).toFixed(2));
  if (d.category) setField("landCategory", d.category);
  setField("siteAddress", d.address);
  status(`자동조회 완료 — ${d.province} ${d.city} / 지목 ${d.category || "-"} / ${d.zones.length}개 지역·지구`);
  recalc();
});

function setField(f, v) {
  state.project[f] = v;
  const el = document.querySelector(`.head .cell[data-field="${f}"]`);
  if (el) el.textContent = v;
}

// ── 모드 · 탭 ────────────────────────────────────────────────
$("#modes").addEventListener("click", (e) => {
  const b = e.target.closest(".mode"); if (!b) return;
  $$(".mode").forEach((x) => x.classList.remove("active"));
  b.classList.add("active");
  state.mode = b.dataset.mode;
  document.body.dataset.mode = state.mode;
  status(`${state.mode} 모드`);
});

$("#tabs").addEventListener("click", (e) => {
  const b = e.target.closest(".tab"); if (!b) return;
  $$(".tab").forEach((x) => x.classList.remove("active"));
  $$(".page").forEach((x) => x.classList.remove("active"));
  b.classList.add("active");
  $("#page-" + b.dataset.page).classList.add("active");
});

// ── 설정 ─────────────────────────────────────────────────────
$("#btnSettings").addEventListener("click", async () => {
  const s = await (await fetch("/api/settings")).json();
  $("#setModel").value = s.claudeModel || "claude-sonnet-5";
  $("#setVworldDomain").value = s.vworldDomain || "http://localhost";
  $("#setMoleg").placeholder = s.hasMoleg ? "저장됨 (변경 시 입력)" : "미입력";
  $("#setClaude").placeholder = s.hasClaude ? "저장됨 (변경 시 입력)" : "미입력";
  $("#setVworld").placeholder = s.hasVworld ? "저장됨 (변경 시 입력)" : "미입력";
  $("#dlgSettings").showModal();
});

$("#btnSaveSettings").addEventListener("click", async () => {
  const body = {
    molegApiKey: $("#setMoleg").value || null,
    claudeApiKey: $("#setClaude").value || null,
    vworldApiKey: $("#setVworld").value || null,
    claudeModel: $("#setModel").value,
    vworldDomain: $("#setVworldDomain").value,
  };
  await fetch("/api/settings", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
  status("설정 저장됨");
});

// ── 시작 ─────────────────────────────────────────────────────
document.body.dataset.mode = state.mode;
renderScaleTable();
renderAreaTable();
bindHeadFields();
status("준비됨 — 대지위치를 입력하고 자동조회를 누르세요");
