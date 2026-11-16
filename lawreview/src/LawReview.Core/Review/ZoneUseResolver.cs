using System.Text.RegularExpressions;
using LawReview.Core.LawApi;

namespace LawReview.Core.Review;

/// <summary>
/// 용도지역 안에서 건축할 수 있는(없는) 건축물 — 허용·불허 용도.
///
/// 법 위계: 「국토계획법 시행령」 별표 2~22 (용도지역별)
///   제1호 = 법령이 바로 허용(또는 금지)하는 용도
///   제2호 = 도시·군계획조례가 정하는 바에 따라 허용(금지)할 수 있는 용도
///   → 지자체 도시계획 조례 별표(같은 용도지역)가 제2호 중 실제로 허용(금지)하는 것을 정한다.
/// 그래서 "제2호에 있다"만으로는 허용이 아니다 — 조례 별표에 있어야 허용이다(허용형 지역 기준).
///
/// 지구단위계획이 있으면 그 허용·불허 용도가 우선한다. 이 결과는 지구단위계획이 없을 때의 기본값이다.
/// 판정은 문자열 대조이고, 조건(도로 너비·바닥면적 등)이 붙은 항목은 "조건부"로 조건 원문을 함께 돌려준다 —
/// 조건 충족 여부는 사람이 본다.
/// </summary>
public static class ZoneUseResolver
{
    public const string DecreeName = "국토의 계획 및 이용에 관한 법률 시행령";

    /// <summary>용도지역 21개(국토계획법 제36조, 시행령 제30조). 지역지구 목록에서 이것만 골라낸다.</summary>
    public static readonly string[] UseZones =
    {
        "제1종전용주거지역", "제2종전용주거지역", "제1종일반주거지역", "제2종일반주거지역", "제3종일반주거지역",
        "준주거지역", "중심상업지역", "일반상업지역", "근린상업지역", "유통상업지역",
        "전용공업지역", "일반공업지역", "준공업지역", "보전녹지지역", "생산녹지지역", "자연녹지지역",
        "보전관리지역", "생산관리지역", "계획관리지역", "농림지역", "자연환경보전지역",
    };

    /// <summary>「건축법 시행령」 별표 1의 호 번호 → 용도 이름.</summary>
    public static readonly IReadOnlyDictionary<string, string> Categories = new Dictionary<string, string>
    {
        ["1"] = "단독주택", ["2"] = "공동주택", ["3"] = "제1종 근린생활시설", ["4"] = "제2종 근린생활시설",
        ["5"] = "문화 및 집회시설", ["6"] = "종교시설", ["7"] = "판매시설", ["8"] = "운수시설",
        ["9"] = "의료시설", ["10"] = "교육연구시설", ["11"] = "노유자시설", ["12"] = "수련시설",
        ["13"] = "운동시설", ["14"] = "업무시설", ["15"] = "숙박시설", ["16"] = "위락시설",
        ["17"] = "공장", ["18"] = "창고시설", ["19"] = "위험물 저장 및 처리 시설", ["20"] = "자동차 관련 시설",
        ["21"] = "동물 및 식물 관련 시설", ["22"] = "자원순환 관련 시설", ["23"] = "교정시설",
        ["23의2"] = "국방·군사시설", ["24"] = "방송통신시설", ["25"] = "발전시설", ["26"] = "묘지 관련 시설",
        ["27"] = "관광 휴게시설", ["28"] = "장례시설", ["29"] = "야영장 시설",
    };

    /// <summary>
    /// 용도 이름만 있고 별표 1의 분류가 없을 때(예: "일반음식점") 분류를 정하는 표.
    /// **면적에 따라 1종/2종이 갈리는 것(사무소·휴게음식점·학원 등)은 넣지 않는다** — 넣으면 틀린 판정이 나간다.
    /// </summary>
    private static readonly (string Word, string Category)[] DetailCategory =
    {
        ("일반음식점", "4"), ("단란주점", "4"), ("안마시술소", "4"), ("노래연습장", "4"), ("다중생활시설", "4"),
        ("고시원", "4"), ("장의사", "4"), ("총포판매소", "4"), ("종교집회장", "4"), ("자동차영업소", "4"),
        ("동물병원", "4"), ("독서실", "4"), ("기원", "4"),
        ("의원", "3"), ("치과의원", "3"), ("한의원", "3"), ("미용원", "3"), ("목욕장", "3"), ("세탁소", "3"),
        ("지역아동센터", "3"), ("파출소", "3"), ("공중화장실", "3"),
        ("아파트", "2"), ("연립주택", "2"), ("다세대주택", "2"), ("기숙사", "2"),
        ("다가구주택", "1"), ("다중주택", "1"), ("공관", "1"),
        ("오피스텔", "14"), ("병원", "9"), ("요양병원", "9"), ("유흥주점", "16"), ("주차장", "20"), ("세차장", "20"),
    };

    // ── 진입점 ───────────────────────────────────────────────────
    /// <summary>지역지구 목록에서 용도지역 하나를 고른다(없으면 null).</summary>
    public static string? PickUseZone(IEnumerable<string> zones)
    {
        foreach (var z in zones)
        {
            var n = Norm(z);
            var hit = UseZones.FirstOrDefault(u => n.Contains(u));
            if (hit is not null) return hit;
        }
        return null;
    }

    /// <summary>시행령 본문(별표 포함)과 조례 별표 줄에서 그 용도지역의 규칙을 만든다.</summary>
    public static ZoneUseRules? Build(string zone, LawText decree, string? ordinanceName, string? ordinanceAnnexTitle,
        IReadOnlyList<string>? ordinanceLines)
    {
        var annex = (decree.Annexes ?? Array.Empty<Annex>())
            .FirstOrDefault(a => Norm(a.Title).StartsWith(zone) && Norm(a.Title).Contains("건축할수"));
        if (annex is null || annex.Content.Length == 0) return null;

        var permissive = Norm(annex.Title).Contains("건축할수있는");
        var decreeClauses = ParseClauses(JoinWrapped(annex.Content.Split('\n')));
        // 조례 별표에는 층수 제한 같은 다른 호도 섞여 있다 — "건축할 수 있다/없다"를 정하는 호의 목만 쓴다.
        var ordClauses = ordinanceLines is { Count: > 0 }
            ? ParseClauses(ordinanceLines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList())
                .Where(c => c.Category is not null && Norm(c.SectionHeader).Contains("건축할수")
                            && !c.SectionHeader.Contains("층수")).ToList()
            : new List<Clause>();

        return new ZoneUseRules(zone, permissive, $"{DecreeName} 별표 {annex.Number}", annex.Title,
            decreeClauses.Where(c => c.Section == 1).ToList(),
            decreeClauses.Where(c => c.Section == 2).ToList(),
            ordinanceName, ordinanceAnnexTitle, ordClauses);
    }

    /// <summary>
    /// 용도 하나를 판정한다. use는 대장 표기 그대로 받는다(예: "제2종근린생활시설(사무소)", "일반음식점").
    /// categoryNo를 알면(대장 용도코드 앞 두 자리) 그것을 우선한다.
    /// </summary>
    public static ZoneUseJudgment Judge(ZoneUseRules r, string use, string? categoryNo = null)
    {
        var (cat, detail) = Classify(use, categoryNo);
        if (cat is null)
            return new(use, "확인 필요", "건축법 시행령 별표 1의 분류를 정할 수 없습니다(면적에 따라 1종/2종이 갈리는 용도 등)", "", "");
        var catName = Categories[cat];

        Clause? Find(IEnumerable<Clause> cs) => cs.FirstOrDefault(c => c.Category == cat && Covers(c, detail));
        var d1 = Find(r.DecreeSection1);
        var d2 = Find(r.DecreeSection2);
        var ord = r.OrdinanceClauses.Where(c => c.Category == cat).ToList();
        // 세목이 있는 항목: 세부 용도가 세목에 있어야 한다. 세부 용도를 모르면 항목 자체로 잡되 조건부.
        var ordHit = ord.Select(c => (Clause: c, Sub: MatchSub(c, detail)))
            .FirstOrDefault(x => x.Clause.Subs.Count == 0 ? Covers(x.Clause, detail) : x.Sub is not null || detail.Length == 0);
        var ordCite = ordHit.Clause is null ? "" : Cite(r.OrdinanceName + " " + OrdinanceAnnexNo(r), ordHit.Clause, ordHit.Sub);
        var cond = ordHit.Sub?.Text ?? ordHit.Clause?.Text ?? "";
        if (ordHit.Clause is { Subs.Count: > 0 } && ordHit.Sub is null) cond = "세부 용도 확인 필요 — " + cond;

        if (r.Permissive)
        {
            if (d1 is not null)
                return new(use, Conditional(d1.Text) ? "조건부 허용" : "허용", $"{catName} — 법령이 바로 허용",
                    Cite(r.DecreeAnnex, d1, null), d1.Text);
            if (ordHit.Clause is not null)
                return new(use, Conditional(cond) ? "조건부 허용" : "허용", $"{catName} — 조례로 허용", ordCite, cond);
            if (d2 is not null)
                return r.OrdinanceName is null || r.OrdinanceClauses.Count == 0
                    ? new(use, "확인 필요", $"{catName} — 조례로 허용할 수 있는 용도인데 조례 별표를 읽지 못했습니다", Cite(r.DecreeAnnex, d2, null), d2.Text)
                    : new(use, "불허", $"{catName} — 조례 허용 목록에 없음", $"{r.OrdinanceName} {OrdinanceAnnexNo(r)}", "");
            return new(use, "불허", $"{catName} — 이 용도지역에서 건축할 수 있는 건축물에 없음", r.DecreeAnnex, "");
        }

        // 금지형(준주거·상업·준공업·계획관리): 목록에 있으면 불허, 없으면 허용.
        // 단, "○○ 중 △△"만 금지하는데 세부 용도를 모르면 불허로 단정하지 않는다 — 금지 대상인지 사람이 본다.
        var partialUnknown = (Clause c) => detail.Length == 0 && (c.Text.Contains(" 중 ") || c.Subs.Count > 0);
        if (d1 is not null)
            return partialUnknown(d1)
                ? new(use, "조건부 허용", $"{catName} 중 일부만 법령이 금지 — 세부 용도 확인", Cite(r.DecreeAnnex, d1, null), d1.Text)
                : new(use, "불허", $"{catName} — 법령이 금지", Cite(r.DecreeAnnex, d1, null), d1.Text);
        if (ordHit.Clause is not null)
            return partialUnknown(ordHit.Clause) && ordHit.Sub is null
                ? new(use, "조건부 허용", $"{catName} 중 일부만 조례가 금지 — 세부 용도 확인", ordCite, ordHit.Clause.Text)
                : new(use, "불허", $"{catName} — 조례로 금지", ordCite, cond);
        if (d2 is not null && (r.OrdinanceName is null || r.OrdinanceClauses.Count == 0))
            return new(use, "확인 필요", $"{catName} — 조례로 금지할 수 있는 용도인데 조례 별표를 읽지 못했습니다", Cite(r.DecreeAnnex, d2, null), d2.Text);
        return new(use, "허용", $"{catName} — 금지 목록에 없음", r.DecreeAnnex + (r.OrdinanceName is null ? "" : $" · {r.OrdinanceName} {OrdinanceAnnexNo(r)}"), "");
    }

    /// <summary>설계개요 허용용도·불허용도 칸에 들어갈 요약.</summary>
    public static (string Allowed, string Disallowed, string Basis) Summarize(ZoneUseRules r)
    {
        string List(IEnumerable<Clause> cs) => string.Join(", ", cs
            .Where(c => c.Category is not null)
            .Select(c => Categories[c.Category!] + (Conditional(c.Text) || c.Subs.Count > 0 || c.Text.Contains(" 중 ") ? "(일부)" : ""))
            .Distinct());

        var ordNo = OrdinanceAnnexNo(r);
        var basis = r.DecreeAnnex + (r.OrdinanceName is null ? "" : $", {r.OrdinanceName} {ordNo}");
        var d1 = List(r.DecreeSection1);
        var ord = List(r.OrdinanceClauses);

        if (r.Permissive)
        {
            var allowed = $"[법령] {d1}" + (ord.Length > 0 ? $" / [조례] {ord}" : r.OrdinanceName is null ? " / [조례] 확인 필요" : "");
            return (allowed, "위 허용 용도 외 용도", basis);
        }
        var disallowed = $"[법령] {d1}" + (ord.Length > 0 ? $" / [조례] {ord}" : r.OrdinanceName is null ? " / [조례] 확인 필요" : "");
        return ("위 불허 용도 외 용도", disallowed, basis);
    }

    // ── 해석 ─────────────────────────────────────────────────────
    private static readonly Regex SectionRx = new(@"^(\d+)\.\s*(.*)$");
    private static readonly Regex ItemRx = new(@"^([가-힣])\.\s*(.*)$");
    private static readonly Regex SubRx = new(@"^(\d+)\)\s*(.*)$");
    private static readonly Regex SubSubRx = new(@"^([가-힣])\)\s*(.*)$");
    private static readonly Regex CategoryRx = new(@"별표\s*1?\s*제(\d+)호(의\d)?");

    /// <summary>법령 별표 본문은 고정 폭으로 줄이 꺾여 온다 — 항목 머리가 아닌 줄은 앞 줄에 붙인다.</summary>
    internal static List<string> JoinWrapped(IEnumerable<string> raw)
    {
        var lines = new List<string>();
        foreach (var r in raw)
        {
            var t = r.Trim();
            if (t.Length == 0) continue;
            var head = SectionRx.IsMatch(t) || ItemRx.IsMatch(t) || SubRx.IsMatch(t) || SubSubRx.IsMatch(t) || t.StartsWith('■');
            if (head || lines.Count == 0) lines.Add(t);
            else lines[^1] += t;
        }
        return lines;
    }

    internal static List<Clause> ParseClauses(IReadOnlyList<string> lines)
    {
        var result = new List<Clause>();
        var section = 0;
        var header = "";
        var preamble = "";
        Clause? cur = null;
        foreach (var line in lines)
        {
            Match m;
            if ((m = SectionRx.Match(line)).Success)
            {
                section = int.Parse(m.Groups[1].Value);
                var text = m.Groups[2].Value;
                // 조례 별표 중에는 호 자체가 용도인 것이 있다(서울 별표 7: "1. 「건축법 시행령」 별표 1 제5호의 …").
                // 그때 머리말은 번호 없는 앞 문단("… 다음 각 호의 건축물을 건축할 수 없다")이다.
                if (text.Contains("「건축법 시행령」") && CategoryOf(text) is { } c)
                {
                    cur = new Clause(section, "", text, c) { SectionHeader = preamble };
                    result.Add(cur);
                    continue;
                }
                header = text; cur = null; continue;
            }
            if (cur is { Mark: "" } && (m = ItemRx.Match(line)).Success)
            {
                cur.Subs.Add(new SubClause(m.Groups[1].Value + "목", m.Groups[2].Value));
                continue;
            }
            if (section == 0 && !ItemRx.IsMatch(line) && !SubRx.IsMatch(line)) { preamble = line; continue; }
            if ((m = ItemRx.Match(line)).Success && section > 0)
            {
                cur = new Clause(section, m.Groups[1].Value, m.Groups[2].Value, CategoryOf(m.Groups[2].Value)) { SectionHeader = header };
                result.Add(cur);
                continue;
            }
            if ((m = SubRx.Match(line)).Success && cur is not null)
            {
                cur.Subs.Add(new SubClause(m.Groups[1].Value + ")", m.Groups[2].Value));
                continue;
            }
            // 가) 나) 같은 더 아래 단계는 바로 위 세목의 조건으로 붙인다.
            if (SubSubRx.IsMatch(line) && cur is { Subs.Count: > 0 })
                cur.Subs[^1] = cur.Subs[^1] with { Text = cur.Subs[^1].Text + " " + line };
        }
        return result;
    }

    internal static string? CategoryOf(string text)
    {
        var m = CategoryRx.Match(text);
        if (m.Success)
        {
            var key = m.Groups[1].Value + m.Groups[2].Value;
            if (Categories.ContainsKey(key)) return key;
        }
        var n = Norm(text);
        // 이름으로 찾을 때는 긴 이름부터(“제2종 근린생활시설”이 “근린생활시설”보다 먼저)
        foreach (var kv in Categories.OrderByDescending(k => k.Value.Length))
            if (n.Contains(Norm(kv.Value))) return kv.Key;
        return null;
    }

    /// <summary>용도 표기 → (별표 1 호 번호, 세부 용도).</summary>
    internal static (string? Category, string Detail) Classify(string use, string? categoryNo)
    {
        var raw = use.Trim();
        // "제2종근린생활시설(사무소)" → 분류 + 괄호 안 세부
        var paren = Regex.Match(raw, @"^(.*?)\((.*)\)\s*$");
        var head = paren.Success ? paren.Groups[1].Value : raw;
        var inner = paren.Success ? paren.Groups[2].Value : "";

        var cat = categoryNo is { Length: > 0 } && Categories.ContainsKey(categoryNo) ? categoryNo : null;
        cat ??= CategoryFromName(head);
        if (cat is not null) return (cat, inner.Length > 0 ? inner : (CategoryFromName(head) is null ? head : ""));

        var dn = Norm(head);
        var hit = DetailCategory.FirstOrDefault(x => dn == Norm(x.Word));
        return (hit.Category, head);
    }

    private static string? CategoryFromName(string s)
    {
        var n = Norm(s);
        foreach (var kv in Categories.OrderByDescending(k => k.Value.Length))
            if (n == Norm(kv.Value) || n.StartsWith(Norm(kv.Value))) return kv.Key;
        return null;
    }

    /// <summary>
    /// 항목이 그 용도를 포함하는지. "○○ 중 △△"처럼 한정된 항목이면 세부 용도가 본문에 있어야 한다.
    /// 세부 용도를 모르면(분류만 앎) 포함으로 보되, 판정은 조건부가 된다(Conditional이 " 중 "을 잡는다).
    /// </summary>
    private static bool Covers(Clause c, string detail) =>
        !c.Text.Contains(" 중 ") || detail.Length == 0 || Norm(c.Text).Contains(Norm(detail));

    private static SubClause? MatchSub(Clause c, string detail)
    {
        if (detail.Length == 0 || c.Subs.Count == 0) return null;
        var d = Norm(detail);
        return c.Subs.FirstOrDefault(s => Norm(s.Text).Contains(d));
    }

    private static bool Conditional(string text) =>
        text.Contains("한정") || text.Contains("한한다") || text.Contains("미만") || text.Contains("이하인")
        || text.Contains("접한 대지") || text.Contains(" 중 ") || text.StartsWith("세부 용도 확인");

    private static string Cite(string law, Clause c, SubClause? s) =>
        $"{law} 제{c.Section}호" + (c.Mark.Length > 0 ? $"{c.Mark}목" : "") + (s is null ? "" : $" {s.Mark}");

    private static string OrdinanceAnnexNo(ZoneUseRules r)
    {
        var m = Regex.Match(r.OrdinanceAnnexTitle ?? "", @"별표\s*\d+(의\d+)?");
        return m.Success ? m.Value.Replace(" ", " ") : "별표";
    }

    /// <summary>비교용 정규화 — 공백·가운뎃점 변형을 지운다(법제처 원문은 ㆍ·? 가 섞여 온다).</summary>
    public static string Norm(string s) => Regex.Replace(s ?? "", @"[\s·ㆍ?・‧]", "");
}

public sealed record ZoneUseRules(
    string Zone,
    bool Permissive,                 // true: "건축할 수 있는" 목록, false: "건축할 수 없는" 목록
    string DecreeAnnex,              // 예: 국토의 계획 및 이용에 관한 법률 시행령 별표 5
    string DecreeAnnexTitle,
    List<Clause> DecreeSection1,
    List<Clause> DecreeSection2,
    string? OrdinanceName,
    string? OrdinanceAnnexTitle,
    List<Clause> OrdinanceClauses);

public sealed record Clause(int Section, string Mark, string Text, string? Category)
{
    /// <summary>이 목이 속한 호의 머리말 (예: "건축할 수 있는 건축물").</summary>
    public string SectionHeader { get; init; } = "";
    public List<SubClause> Subs { get; } = new();
}

public sealed record SubClause(string Mark, string Text);

/// <summary>용도 하나의 판정. Verdict: 허용 / 조건부 허용 / 불허 / 확인 필요.</summary>
public sealed record ZoneUseJudgment(string Use, string Verdict, string Reason, string Citation, string Condition);
