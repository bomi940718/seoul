using System.Text;
using System.Text.RegularExpressions;
using LawReview.Core.LawApi;

namespace LawReview.Core.Review;

/// <summary>건축선 / 인접 대지경계선 — 대지 안의 공지 기준이 나뉘는 두 축.</summary>
public enum SetbackSide
{
    /// <summary>건축선으로부터 건축물까지 띄어야 하는 거리.</summary>
    건축선,
    /// <summary>인접 대지경계선으로부터 건축물까지 띄어야 하는 거리.</summary>
    인접대지경계선,
}

/// <summary>
/// **대지 안의 공지**(건축법 제58조) 이격거리 기준을 뽑는다.
///
/// 값이 어디 있는가: 건축법 시행령 제80조의2 → **별표 2**가 "건축조례에서 정하는 건축기준"의
/// 범위(예: 3미터 이상 6미터 이하)만 정하고, **실제 적용 거리는 지자체 건축조례의 별표**에 있다
/// (대전광역시 건축 조례 제40조 → 별표 3). 그래서 조례를 먼저 보고 없을 때만 시행령으로 내려간다.
///
/// 법 위계(기초 조례 → 광역 조례 → 모법)는 <see cref="ParkingStandardResolver"/>·
/// <see cref="LandscapeRatioResolver"/>와 같다. 조례 별표는 HWP 첨부라
/// <see cref="HwpTextExtractor"/>로 읽고, 한 파일에 여러 별표가 들어 있으므로
/// <see cref="AnnexText.Section"/>으로 "대지 안의 공지 기준" 별표만 잘라낸다.
///
/// **판정은 하지 않는다.** 이 표의 각 행에는 "산업단지에 건축하는 공장은 제외한다" 같은 단서가
/// 붙어 있어 용도명만으로 적용 여부를 단정할 수 없다(검토 품질 원칙 5). 해당될 수 있는 행을
/// 조문 원문 그대로 붙여 주고, 적용 여부 판단은 AI·사람에게 맡긴다.
/// </summary>
public static class SetbackStandardResolver
{
    /// <summary>조례·시행령 어느 쪽이든 별표 제목에 이 말이 들어간다("대지 안의 공지"/"대지의 공지").</summary>
    private const string AnnexTitleKeyword = "공지 기준";

    /// <summary>
    /// 법 위계대로 적용 기준을 정한다: **기초 조례 → 광역 조례 → 모법(건축법 시행령 별표 2)**.
    /// 조례 별표에서 기준을 찾으면 그것이 실제 적용 값이다(시행령은 범위만 정한다).
    /// </summary>
    /// <param name="ordinances">기초 → 광역 순서로 정렬된 건축조례 별표 (없으면 빈 목록)</param>
    /// <param name="decree">모법(건축법 시행령) 본문 — 별표 2 포함</param>
    public static SetbackStandard ResolveChain(IReadOnlyList<OrdinanceAnnex> ordinances, LawText? decree)
    {
        foreach (var o in ordinances)
        {
            var rules = ParseOrdinanceAnnex(o.Lines);
            if (rules.Count == 0) continue;
            return new SetbackStandard
            {
                Rules = rules,
                SourceName = o.Name,
                AnnexLabel = AnnexLabel(o.Lines),
                EffectiveDate = o.EffectiveDate,
                AnnexLink = o.Link,
            };
        }

        var fromDecree = ResolveFromDecree(decree);
        if (fromDecree.Rules.Count > 0 && ordinances.Count > 0)
            fromDecree.Note = $"{string.Join(" · ", ordinances.Select(o => o.Name))}에서 대지 안의 공지 별표를 " +
                              "읽지 못해 모법 기준(조례로 정할 범위)을 적용했습니다. 조례 별표를 직접 확인하세요.";
        return fromDecree;
    }

    /// <summary>건축법 시행령 별표 2("대지의 공지 기준")에서 기준 범위를 읽는다.</summary>
    public static SetbackStandard ResolveFromDecree(LawText? decree)
    {
        var annex = decree?.Annexes?.FirstOrDefault(a => AnnexText.Normalize(a.Title)
            .Contains(AnnexText.Normalize(AnnexTitleKeyword)));
        if (annex is null || annex.Content.Length == 0) return new SetbackStandard();

        var rules = ParseDecreeAnnex(annex.Content);
        if (rules.Count == 0) return new SetbackStandard();

        return new SetbackStandard
        {
            Rules = rules,
            SourceName = decree!.Name,
            AnnexLabel = $"별표 {annex.Number.TrimStart('0')}",
            EffectiveDate = decree.EffectiveDate,
            AnnexLink = annex.Link,
            Note = "시행령 별표는 건축조례로 정할 범위입니다. 실제 적용 거리는 해당 지자체 건축조례 별표를 확인하세요.",
        };
    }

    /// <summary>잘라낸 조례 별표의 머리글("[별표 3]")을 돌려준다. 없으면 "별표".</summary>
    private static string AnnexLabel(IReadOnlyList<string> lines)
    {
        var section = AnnexText.Section(lines, AnnexTitleKeyword);
        var head = section.FirstOrDefault(l => l.Contains("별표")) ?? "";
        var m = Regex.Match(head, @"별표\s*\d+");
        return m.Success ? m.Value : "별표";
    }

    // ── 파서 ─────────────────────────────────────────────────────────────

    // "1. 건축선으로부터 건축물까지 띄어야 하는 거리" / "2. 인접 대지경계선으로부터 …"
    private static readonly Regex SectionHead = new(@"^\s*\d\.\s*(건축선|인접)", RegexOptions.Compiled);
    // 항목 머리 "가. …" (조례는 들여쓰기가 없고, 시행령은 괘선 안에 있다)
    private static readonly Regex ItemHead = new(@"^\s*([가-힣])\.\s*(.*)$", RegexOptions.Compiled);
    // 거리 줄의 머리표. 원문마다 글자가 다르다(가운뎃점·한 점 지시선·불릿).
    private const string Bullets = "·․‧∙•ㆍ-–";
    // 표 머리글 줄 — 항목이 아니므로 버린다.
    private static readonly string[] TableHeads =
    {
        "대상건축물", "건축물의각부분까지", "띄어야할거리", "건축조례에서정하는건축기준",
        "지역구분", "띄어야", "하는거리",
    };
    // 거리 값이 들어 있는 칸("1.5미터 이상", "준공업지역: 1.5미터 이상", "(3미터 이상)").
    //
    // **줄 끝에 붙어 있을 때만** 거리로 본다. 조건 문장 속의 미터는 거리가 아니기 때문이다
    // — 안양 조례의 "(폭 12미터 이상 도로에 접한 부분…)"은 조건이지 이격거리가 아니다.
    // 제곱미터는 면적 조건이므로 제외한다(서울 조례는 "• 500제곱미터 이상"을 같은 칸에 둔다).
    private static readonly Regex DistanceCell =
        new(@"(?<!제곱)\d[\d.,]*\s*미터\s*(이상|이하)[\s)\]］]*$", RegexOptions.Compiled);

    /// <summary>
    /// 지자체 건축조례 별표(HWP에서 뽑은 줄 목록)를 읽는다.
    ///
    /// 조례마다 표 모양이 제각각이라 **줄의 역할로만 판단한다.** 실제로 확인한 것만도 네 가지다:
    ///  · 대전 — "가. …" 아래에 "․ 준공업지역: 1.5미터 이상" (조건과 거리가 한 줄)
    ///  · 안양 — 대상·지역구분·거리가 각각 다른 칸이라 줄이 따로 떨어진다
    ///  · 서울 — 머리표(•)를 쓰되 바닥면적 조건과 거리가 같은 칸에 섞인다
    ///  · 속초 — 머리표가 하이픈("-")이다
    ///
    /// 그래서 머리표를 떼어낸 뒤 <see cref="DistanceCell"/>로 거리 칸을 가려내고,
    /// 거리 앞에 쌓인 줄을 그 거리의 조건으로 묶는다. 조건 수와 거리 수가 같으면 순서대로
    /// 짝지어 "준공업지역: 1.5미터 이상"으로 되살리고, 수가 맞지 않으면 짝을 지어내지 않고
    /// 조건과 거리를 그대로 이어 붙인다 — **없는 규칙을 만들어내지 않는다.**
    /// </summary>
    public static IReadOnlyList<SetbackRule> ParseOrdinanceAnnex(IReadOnlyList<string> lines)
    {
        var section = AnnexText.Section(lines, AnnexTitleKeyword);
        if (section.Count == 0) return Array.Empty<SetbackRule>();

        var rules = new List<SetbackRule>();
        SetbackSide? side = null;
        Draft? current = null;
        var qualifiers = new List<string>();
        var distances = new List<string>();

        void Flush()
        {
            if (current is not null && distances.Count > 0)
            {
                if (qualifiers.Count == distances.Count)
                    for (var i = 0; i < distances.Count; i++)
                        current.Distances.Add($"{qualifiers[i]}: {distances[i]}");
                else if (qualifiers.Count == 0)
                    current.Distances.AddRange(distances);
                else
                    current.Distances.Add($"{string.Join(" · ", qualifiers)}: {string.Join(" / ", distances)}");
            }
            qualifiers.Clear();
            distances.Clear();
        }

        foreach (var raw in section)
        {
            var line = StripBullet(raw);
            if (line.Length == 0) continue;
            if (TableHeads.Contains(AnnexText.Normalize(line))) continue;

            if (SectionHead.Match(line) is { Success: true } sm)
            {
                Flush();
                side = sm.Groups[1].Value == "건축선" ? SetbackSide.건축선 : SetbackSide.인접대지경계선;
                current = null;
                continue;
            }
            if (side is null) continue;

            if (ItemHead.Match(line) is { Success: true } im && IsOrdinal(im.Groups[1].Value[0]))
            {
                Flush();
                current = new Draft(side.Value, im.Groups[1].Value, im.Groups[2].Value);
                rules.Add(current.Rule);
                continue;
            }
            if (current is null) continue;

            if (DistanceCell.IsMatch(line)) { distances.Add(line.TrimStart(':', ';', ' ')); continue; }

            // 거리 칸이 이미 나왔다면 여기서부터는 다음 조건 묶음이다.
            if (distances.Count > 0) Flush();
            // 항목명이 칸 안에서 줄바꿈된 경우만 항목명에 이어 붙인다("마. 그 밖의" + "건축물",
            // "…합계가 500제곱미터" + "이상인 공장(…)"). 문장이 미완인 때만이며,
            // "라. 공동주택" 다음의 "아파트"는 항목명이 아니라 조건 칸이다.
            if (current.Distances.Count == 0 && LooksIncomplete(current.Rule.Target))
                current.Append(line);
            else
                qualifiers.Add(line.TrimEnd(':', ' '));
        }
        Flush();
        return rules.Where(r => r.Distances.Count > 0).ToList();
    }

    /// <summary>
    /// 항목명이 아직 끝나지 않았는지 — 조사·연결어미로 끝나거나 괄호가 안 닫혔으면 다음 줄이 이어진다.
    /// (성남시 조례는 "…합계가 500제곱미터"에서 칸이 끊기고 "이상인 공장(…)"이 다음 줄에 온다.
    ///  여기서 끊어 버리면 항목명에 "공장"이 없어 용도 매칭이 통째로 어긋난다)
    /// </summary>
    private static bool LooksIncomplete(string target)
    {
        if (target.Length == 0) return true;
        var opens = target.Count(c => c is '(' or '｢' or '「' or '[');
        var closes = target.Count(c => c is ')' or '｣' or '」' or ']');
        if (opens > closes) return true;
        if (target.EndsWith("제곱미터") || char.IsDigit(target[^1])) return true;
        return "가이및의는은를을로에와과".Contains(target[^1]);
    }

    /// <summary>머리표(·․•- 등)와 앞뒤 공백을 떼어낸다. 조례마다 쓰는 글자가 다르다.</summary>
    private static string StripBullet(string raw)
    {
        var line = raw.Trim();
        return line.Length > 0 && Bullets.Contains(line[0]) ? line[1..].Trim() : line;
    }


    /// <summary>건축법 시행령 별표 2(괘선 표)를 읽는다. "┃대상 건축물 │기준 ┃" 두 칸 구조다.</summary>
    public static IReadOnlyList<SetbackRule> ParseDecreeAnnex(string content)
    {
        var rules = new List<SetbackRule>();
        SetbackSide? side = null;
        Draft? current = null;

        foreach (var raw in (content ?? "").Split('\n'))
        {
            var line = raw.Replace("\r", "");
            if (SectionHead.Match(line) is { Success: true } sm)
            {
                side = sm.Groups[1].Value == "건축선" ? SetbackSide.건축선 : SetbackSide.인접대지경계선;
                current = null;
                continue;
            }
            if (side is null) continue;
            // 비고는 표가 끝난 뒤의 설명이므로 항목으로 잡지 않는다.
            if (AnnexText.Normalize(line) == "비고") { side = null; continue; }

            var cut = line.IndexOf('│');
            if (cut < 0) continue;      // 괘선 경계줄 등

            var left = line[..cut].Replace("┃", "").Trim();
            var right = line[(cut + 1)..].Replace("┃", "").Trim();

            if (ItemHead.Match(left) is { Success: true } im && IsOrdinal(im.Groups[1].Value[0]))
            {
                current = new Draft(side.Value, im.Groups[1].Value, im.Groups[2].Value);
                rules.Add(current.Rule);
            }
            else if (left.Length > 0 && !TableHeads.Contains(AnnexText.Normalize(left)))
            {
                current?.Append(left, wrapped: true);   // 칸 안에서 줄바꿈된 항목명
            }

            if (right.Length == 0 || TableHeads.Contains(AnnexText.Normalize(right))) continue;
            if (Bullets.Contains(right[0])) current?.Distances.Add(right[1..].Trim());
            else current?.AppendDistance(right);         // 칸 안에서 줄바꿈된 기준값
        }
        return rules.Where(r => r.Distances.Count > 0).ToList();
    }

    /// <summary>"가·나·다…" 순서 문자인지. ("제1종…" 같은 줄이 항목으로 잡히는 것을 막는다)</summary>
    private static bool IsOrdinal(char c) => "가나다라마바사아자차카타파하".Contains(c);

    /// <summary>파싱 중인 행. Rule은 참조로 목록에 들어가 있고 여기서 이어 붙인다.</summary>
    private sealed class Draft
    {
        public SetbackRule Rule { get; }
        public List<string> Distances => (List<string>)Rule.Distances;

        public Draft(SetbackSide side, string label, string target)
        {
            Rule = new SetbackRule(side, label, target.Trim(), new List<string>());
        }

        /// <summary>
        /// 항목명 이어 붙이기.
        /// 괘선 표(<paramref name="wrapped"/>)는 칸 안에서 글자 단위로 잘리므로 공백 없이 붙이고,
        /// 왼쪽 칸과 오른쪽 칸이 같은 줄에 오므로 기준값이 이미 나왔어도 계속 이어진다.
        /// 조례 별표는 칸이 없어 항목명 줄이 끝나야 기준값 줄이 시작되므로, 그 뒤의 줄은 다음 항목 것이다.
        /// </summary>
        public void Append(string text, bool wrapped = false)
        {
            if (!wrapped && Distances.Count > 0) return;
            Rule.Target = wrapped ? Rule.Target + text : $"{Rule.Target} {text}".Trim();
        }

        public void AppendDistance(string text)
        {
            if (Distances.Count == 0) return;
            Distances[^1] += text;
        }
    }
}

/// <summary>대지 안의 공지 기준 한 행. Distances는 "준공업지역: 1.5미터 이상" 같은 원문 그대로.</summary>
public sealed record SetbackRule(SetbackSide Side, string Label, string Target, IReadOnlyList<string> Distances)
{
    /// <summary>항목명(대상 건축물). 여러 줄에 걸쳐 있어 파싱 중에 이어 붙인다.</summary>
    public string Target { get; set; } = Target;

    /// <summary>이 행에 조건·예외 단서가 붙어 있는지(자동 판정하면 안 되는 신호).</summary>
    public bool HasProviso => Target.Contains("제외") || Target.Contains("다만") || Target.Contains("한정");

    public string Text => $"{Label}. {Target} → {string.Join(" / ", Distances)}";

    /// <summary>이 행이 해당 용도에 걸릴 수 있는지(단서까지 판단하지는 않는다).</summary>
    public bool Mentions(string use) =>
        use.Length >= 2 && AnnexText.Normalize(Target).Contains(AnnexText.Normalize(use));

    /// <summary>
    /// "그 밖의 건축물" 행 — 어느 항목에도 걸리지 않을 때의 기준.
    /// 조례마다 말이 다르다: "그 밖의 건축물"(대전)·"기타 모든 건축물"(성남)·
    /// "연면적 1,000제곱미터 이상의 모든 건축물"(성남 건축선).
    /// </summary>
    /// 항목의 <b>첫머리</b>로만 판단한다 — "공동주택(…스프링클러나 그 밖에 이와 비슷한…)"처럼
    /// 본문 중간에 "그 밖에"가 들어간 행을 포괄 행으로 오인하면 엉뚱한 기준이 붙는다.
    public bool IsCatchAll =>
        Target.StartsWith("그 밖") || Target.StartsWith("그밖") || Target.StartsWith("기타")
        || Target.EndsWith("모든 건축물");
}

/// <summary>대지 안의 공지 기준 조회 결과. 못 찾으면 Rules가 비어 있다.</summary>
public sealed class SetbackStandard
{
    public IReadOnlyList<SetbackRule> Rules { get; set; } = Array.Empty<SetbackRule>();
    /// <summary>기준을 가져온 법령·조례 이름 (예: "대전광역시 건축 조례").</summary>
    public string SourceName { get; set; } = "";
    /// <summary>그 법령 안에서의 별표 번호 (예: "별표 3"). 지자체마다 다르므로 파싱으로 얻는다.</summary>
    public string AnnexLabel { get; set; } = "별표";
    public string EffectiveDate { get; set; } = "";
    public string AnnexLink { get; set; } = "";

    /// <summary>인용 표기 — "대전광역시 건축 조례 [별표 3]".</summary>
    public string? Basis => SourceName.Length > 0 ? $"{SourceName} [{AnnexLabel}]" : null;
    /// <summary>자동으로 확정할 수 없는 사정(모법 폴백 등). 화면·검토서에 그대로 노출한다.</summary>
    public string? Note { get; set; }

    /// <summary>
    /// 해당 용도가 걸리는 행을 축(건축선·인접대지경계선)별로 고른다.
    /// 걸리는 행이 없으면 그 축의 "그 밖의 건축물" 행을 쓴다(별표는 반드시 이 행을 둔다).
    /// </summary>
    public IReadOnlyList<SetbackRule> MatchedFor(string? primaryUse)
    {
        var use = (primaryUse ?? "").Trim();
        var picked = new List<SetbackRule>();
        foreach (var side in new[] { SetbackSide.건축선, SetbackSide.인접대지경계선 })
        {
            var rows = Rules.Where(r => r.Side == side).ToList();
            if (rows.Count == 0) continue;
            var hits = rows.Where(r => r.Mentions(use)).ToList();
            if (hits.Count == 0) hits = rows.Where(r => r.IsCatchAll).ToList();
            picked.AddRange(hits);
        }
        return picked;
    }

    /// <summary>요약 검토표의 "법적 기준" 칸에 넣을 한 줄 요약. 기준을 못 찾으면 null.</summary>
    public string? CriterionText(string? primaryUse)
    {
        var matched = MatchedFor(primaryUse);
        if (matched.Count == 0) return null;

        var sb = new StringBuilder();
        foreach (var side in new[] { SetbackSide.건축선, SetbackSide.인접대지경계선 })
        {
            var rows = matched.Where(r => r.Side == side).ToList();
            if (rows.Count == 0) continue;
            var distances = rows.SelectMany(r => r.Distances).Distinct();
            sb.AppendLine($"{SideLabel(side)}: {string.Join(" / ", distances)}");
        }
        if (Basis is not null) sb.Append($"({Basis})");
        return sb.ToString().Trim();
    }

    private static string SideLabel(SetbackSide side) =>
        side == SetbackSide.건축선 ? "건축선" : "인접대지경계선";
}
