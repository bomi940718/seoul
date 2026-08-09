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
  // 설계개요 표: 표준 순서 고정
  rows: [
    { key: "buildingArea", label: "건 축 면 적", calc: true },
    { key: "coverage",     label: "건 폐 율",    calc: true, legalInput: true, legalUnit: "%" },
    { key: "grossArea",    label: "연 면 적",    calc: true },
    { key: "floorRatio",   label: "용 적 률",    calc: true, legalInput: true, legalUnit: "%" },
    { key: "scale",        label: "건 축 규 모", floors: true },
    { key: "height",       label: "최 고 높 이" },
    { key: "landscape",    label: "조 경 면 적" },
    { key: "parkingType",  label: "주 차 대 수", sub: "주차형식" },
    { key: "parkingIn",    sub: "옥내" },
    { key: "parkingOut",   sub: "옥외" },
    { key: "parkingDis",   sub: "장애인전용" },
    { key: "parkingExt",   sub: "확장형" },
    { key: "parkingEco",   sub: "친환경" },
    { key: "parkingAll",   sub: "전체" },
  ],
  values: {},   // key -> { before, plan, after, legal, basis }
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
  // 계산으로 채워지는 칸은 편집 잠금(값은 엔진이 산정식 문자열로 넣는다)
  const isCalc = row.calc && (slot === "plan" || slot === "before" || slot === "after")
              || (row.calc && slot === "legal" && !row.legalInput);
  const extra = isCalc ? "calc formula" : (row.legalInput && slot === "legal" ? "num" : "");
  td.appendChild(makeCell(row.key, slot, v[slot], extra, isCalc ? "" : "입력"));
  return td;
}

function makeCell(key, slot, value, extra, ph) {
  const d = document.createElement("div");
  d.className = "cell " + (extra || "");
  d.dataset.key = key;
  d.dataset.slot = slot;
  if (ph) d.dataset.ph = ph;
  d.textContent = value || "";
  if (!extra?.includes("calc")) {
    d.contentEditable = "true";
    d.addEventListener("input", () => { (state.values[key] ||= {})[slot] = d.textContent; });
    d.addEventListener("blur", recalc);
  }
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

// ── 계산 (엔진 호출) ─────────────────────────────────────────
let recalcTimer = null;
function recalc() {
  clearTimeout(recalcTimer);
  recalcTimer = setTimeout(doRecalc, 150);
}

async function doRecalc() {
  const siteArea = num(state.project.siteArea);
  if (!siteArea) return;

  const payload = {
    projectName: state.project.projectName,
    siteAddress: state.project.siteAddress,
    province: state.project.province,
    city: state.project.city,
    siteArea,
    primaryUse: state.project.primaryUse,
    plannedBuildingArea: num(pick("buildingArea", "planSlot")),
    zoning: {
      maxCoverageRatio: num(state.values.coverage?.legal) || null,
      maxFloorAreaRatio: num(state.values.floorRatio?.legal) || null,
    },
    parking: { areaPerSpace: num(state.values.parkingOut?.legal) || 200 },
    buildings: [],
  };

  try {
    const r = await fetch("/api/overview", {
      method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(payload),
    });
    const o = await r.json();
    setCalc("buildingArea", "legal", o.coverage.legal);
    setCalc("coverage", "planSlot", o.coverage.planned);
    setCalc("grossArea", "legal", o.floorArea.legal);
    setCalc("floorRatio", "planSlot", o.floorArea.planned);
    setCalc("parkingOut", "planSlot", o.parking.planned.formula);
    setCalc("parkingDis", "planSlot", o.parking.planned.disabled);
    status("계산 갱신됨");
  } catch { /* 입력 중 부분 오류는 무시 */ }
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
bindHeadFields();
status("준비됨 — 대지위치를 입력하고 자동조회를 누르세요");
