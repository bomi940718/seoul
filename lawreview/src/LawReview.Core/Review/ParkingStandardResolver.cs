using System.Globalization;
using System.Text.RegularExpressions;
using LawReview.Core.LawApi;

namespace LawReview.Core.Review;

/// <summary>
/// 부설주차장 설치기준(시설면적 N㎡당 1대)을 **주차장법 시행령 별표 1**에서 뽑는다.
///
/// 왜 시행령인가: 실제 적용 기준은 지자체 주차장 조례가 강화·완화한 값인데
/// (예: 공장 — 시행령 350㎡/대, 대전광역시 조례 200㎡/대),
/// **자치법규 별표는 법제처 API가 HWP 첨부파일로만 주기 때문에 본문을 읽을 수 없다.**
/// 그래서 전국 공통 기준인 시행령 별표 1을 자동으로 채우고,
/// 조례로 달라질 수 있다는 사실과 조례 별표 원문 링크를 함께 남겨 사용자가 확인·수정하게 한다
/// (자동으로 확정할 수 없는 것은 정직하게 알린다 — 검토 품질 원칙 5).
/// </summary>
public static class ParkingStandardResolver
{
    private const string AnnexTitle = "부설주차장의 설치대상 시설물 종류 및 설치기준";

    /// <summary>
    /// 법 위계를 따라 적용 기준을 정한다.
    ///
    /// **기초 조례(시·군) → 광역 조례(도) → 모법(시행령)** 순으로 그 용도가 규정된 첫 법을 쓴다.
    /// 예) 안양시 주차장 조례에 없으면 → 경기도 주차장 조례 → 주차장법 시행령.
    /// 도 조례는 부설주차장 같은 상세 기준을 두지 않는 경우가 많지만 단계는 건너뛰지 않는다.
    ///
    /// 어느 법에도 그 용도가 명시되지 않았다면, **최종 적용 법(모법) 별표의 마지막 항목인
    /// "그 밖의 건축물"** 을 쓴다. 지자체법은 모법에 따라 가므로 모법에 항목이 없을 수는 없다.
    ///
    /// 이 위계는 주차장에 한정된 규칙이 아니라 조경·장애인편의 등 모든 법규에 동일하게 적용된다.
    /// </summary>
    /// <param name="ordinances">기초 → 광역 순서로 정렬된 조례 별표 (없으면 빈 목록)</param>
    /// <param name="decree">모법(주차장법 시행령) 본문 — 별표 1 포함</param>
    public static ParkingStandard ResolveChain(
        IReadOnlyList<OrdinanceAnnex> ordinances, LawText? decree, string primaryUse)
    {
        // 1) 위계 순서대로 그 용도가 명시된 첫 법을 찾는다
        foreach (var o in ordinances)
        {
            var hit = ResolveFromOrdinanceAnnex(o.Lines, primaryUse, o.Name, o.Link);
            if (hit.AreaPerSpace is not null) return hit;
        }

        var fromDecree = Resolve(decree, primaryUse);
        if (fromDecree.AreaPerSpace is not null)
        {
            if (ordinances.Count > 0)
                fromDecree.Note = $"{string.Join(" · ", ordinances.Select(o => o.Name))}에 해당 용도가 없어 모법 기준을 적용했습니다.";
            return fromDecree;
        }

        // 2) 어느 법에도 없으면 모법 별표의 "그 밖의 건축물"
        var other = Resolve(decree, "그 밖의 건축물");
        if (other.AreaPerSpace is not null)
            other.Note = $"'{primaryUse}'가 명시된 규정이 없어 모법의 '그 밖의 건축물' 기준을 적용했습니다.";
        return other;
    }

    /// <summary>별표 1의 표에서 용도에 맞는 "시설면적 N㎡당 1대"를 찾는다(정확 매칭만).</summary>
    public static ParkingStandard Resolve(LawText? decree, string primaryUse)
    {
        var annex = decree?.Annexes?.FirstOrDefault(a => a.Title.Contains(AnnexTitle));
        var body = annex?.Content ?? "";
        if (body.Length == 0 || string.IsNullOrWhiteSpace(primaryUse))
            return new ParkingStandard();

        var hit = MatchUse(ParseRows(body), primaryUse);
        if (hit is null) return new ParkingStandard();

        return new ParkingStandard
        {
            AreaPerSpace = hit.AreaPerSpace,
            Basis = "주차장법 시행령 [별표 1]",
            MatchedUse = hit.Label,
            Note = "지자체 주차장 조례가 이 기준을 강화·완화할 수 있습니다. 조례 별표를 확인하세요.",
            AnnexLink = annex?.Link ?? "",
        };
    }

    /// <summary>
    /// 용도명이 걸리는 항목을 찾는다. 기준값(㎡당 1대)이 있는 행만 대상으로 한다
    /// — 한 파일에 여러 별표(주차요금표 등)가 섞여 있어 번호만 보고 고르면 엉뚱한 행이 잡힌다.
    /// </summary>
    private static AnnexRow? MatchUse(List<AnnexRow> rows, string primaryUse)
    {
        var use = primaryUse.Trim();
        return rows.Where(r => r.AreaPerSpace is not null)
            .FirstOrDefault(r => r.Uses.Any(u => use.Contains(u) || u.Contains(use)));
    }

    /// <summary>
    /// 지자체 조례 별표(HWP에서 뽑은 줄 목록)에서 용도별 기준을 찾는다.
    /// 조례 별표는 표 테두리가 없고 "7. 수련시설, 공장…" 다음 줄에 "○ 시설면적 200㎡당 1대"가 온다.
    /// 실제 적용 기준은 조례이므로, 값이 나오면 시행령보다 우선한다.
    /// </summary>
    public static ParkingStandard ResolveFromOrdinanceAnnex(
        IReadOnlyList<string> lines, string primaryUse, string ordinanceName, string annexLink)
    {
        if (lines.Count == 0 || string.IsNullOrWhiteSpace(primaryUse)) return new ParkingStandard();

        var hit = MatchUse(ParseOrdinanceRows(lines), primaryUse);
        if (hit is null) return new ParkingStandard();

        return new ParkingStandard
        {
            AreaPerSpace = hit.AreaPerSpace,
            Basis = $"{ordinanceName} [별표]",
            MatchedUse = hit.Label,
            Note = hit.Proviso,     // "다만, 산업단지 공장용 건축물은 450㎡당 1대" 같은 단서
            AnnexLink = annexLink,
        };
    }

    private static readonly Regex OrdinanceItem = new(@"^\s*(\d+)\.\s*(.+)$", RegexOptions.Compiled);
    private static readonly Regex OrdinanceStd = new(@"^\s*○", RegexOptions.Compiled);

    internal static List<AnnexRow> ParseOrdinanceRows(IReadOnlyList<string> lines)
    {
        var rows = new List<AnnexRow>();
        AnnexRow? current = null;

        foreach (var line in lines)
        {
            var item = OrdinanceItem.Match(line);
            // 번호로 시작하되 기준 줄("○ …")이 아닌 것이 항목명이다.
            if (item.Success && !OrdinanceStd.IsMatch(line) && !line.Contains("㎡당"))
            {
                current = new AnnexRow { Label = item.Groups[2].Value.Trim(), UseText = item.Groups[2].Value };
                rows.Add(current);
                continue;
            }
            if (current is null) continue;

            // 기준줄("○ …")이 나오기 전까지는 용도명이 이어지는 줄이다.
            // (조례에 따라 "2. 문화 및 집회시설" 다음 줄에 ", 판매시설, 의료시설…"이 이어진다)
            if (current.AreaPerSpace is null && !OrdinanceStd.IsMatch(line) && !line.Contains("㎡당"))
            {
                current.UseText += " " + line.Trim();
                continue;
            }
            if (current.AreaPerSpace is not null) continue;

            if (AreaPer.Match(line) is { Success: true } m
                && double.TryParse(m.Groups[1].Value.Replace(",", ""),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                current.AreaPerSpace = v;
                // "다만, …" / "단, …" 단서는 값이 갈리는 조건이므로 반드시 남긴다.
                var proviso = new[] { "다만", "단," }
                    .Select(k => line.IndexOf(k, StringComparison.Ordinal))
                    .Where(i => i > 0).DefaultIfEmpty(-1).Min();
                if (proviso > 0) current.Proviso = line[proviso..].Trim();
            }
        }
        // 용도명은 여러 줄이 모인 뒤에야 완성되므로 마지막에 분해한다.
        foreach (var r in rows) r.Uses = SplitUses(r.UseText);
        return rows;
    }

    // "┃7. 수련시설, 공장(아파트형은 제외 │○ 시설면적 350㎡당 1대(시설면적/350㎡) ┃"
    // 용도명이 여러 줄에 걸치므로 번호로 시작하는 줄부터 다음 번호 전까지를 한 항목으로 본다.
    private static readonly Regex RowStart = new(@"┃\s*(\d+)\.\s*(.+?)\s*│", RegexOptions.Compiled);
    private static readonly Regex AreaPer = new(@"시설면적\s*([\d,]+)\s*㎡\s*당\s*1대", RegexOptions.Compiled);

    internal static List<AnnexRow> ParseRows(string body)
    {
        var rows = new List<AnnexRow>();
        AnnexRow? current = null;

        foreach (var raw in body.Split('\n'))
        {
            var line = raw.Replace("\r", "");
            var start = RowStart.Match(line);
            if (start.Success)
            {
                current = new AnnexRow { Label = start.Groups[2].Value.Trim() };
                rows.Add(current);
            }
            if (current is null) continue;

            // 용도 칸(│ 앞부분)의 글자를 이어붙인다 — 줄바꿈으로 잘린 용도명 복원
            var cut = line.IndexOf('│');
            if (cut > 0) current.UseText += " " + line[..cut].Replace("┃", "").Trim();

            if (current.AreaPerSpace is null && AreaPer.Match(line) is { Success: true } m
                && double.TryParse(m.Groups[1].Value.Replace(",", ""),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                current.AreaPerSpace = v;
        }

        foreach (var r in rows) r.Uses = SplitUses(r.UseText);
        return rows;
    }

    /// <summary>"7. 수련시설, 공장(아파트형은 제외한다)" → ["수련시설", "공장"]</summary>
    // (아래 SplitUses는 시행령·조례 양쪽에서 함께 쓴다)
    internal static List<string> SplitUses(string text)
    {
        var cleaned = Regex.Replace(text ?? "", @"^\s*\d+\.\s*", "");
        cleaned = Regex.Replace(cleaned, @"[\(\[「][^\)\]」]*[\)\]」]?", " ");   // 괄호 안 단서 제거
        return cleaned.Split(new[] { ',', '·', 'ㆍ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length >= 2)
            .ToList();
    }

    internal sealed class AnnexRow
    {
        public string Label { get; set; } = "";
        public string UseText { get; set; } = "";
        public List<string> Uses { get; set; } = new();
        public double? AreaPerSpace { get; set; }
        /// <summary>"다만, 산업단지 공장용 건축물은 450㎡당 1대" 같은 단서.</summary>
        public string? Proviso { get; set; }
    }
}

/// <summary>조례 별표 하나(위계 순서대로 넘긴다).</summary>
public sealed record OrdinanceAnnex(string Name, string Link, IReadOnlyList<string> Lines);

/// <summary>부설주차장 설치기준 조회 결과. 못 찾으면 AreaPerSpace가 null.</summary>
public sealed class ParkingStandard
{
    public double? AreaPerSpace { get; set; }
    public string? Basis { get; set; }
    public string? MatchedUse { get; set; }
    /// <summary>조례로 달라질 수 있다는 안내(검토서·화면에 그대로 노출한다).</summary>
    public string? Note { get; set; }
    public string AnnexLink { get; set; } = "";
}
