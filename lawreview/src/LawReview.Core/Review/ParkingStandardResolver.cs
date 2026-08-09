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

    /// <summary>별표 1의 표에서 용도에 맞는 "시설면적 N㎡당 1대"를 찾는다.</summary>
    public static ParkingStandard Resolve(LawText? decree, string primaryUse)
    {
        var annex = decree?.Annexes?.FirstOrDefault(a => a.Title.Contains(AnnexTitle));
        var body = annex?.Content ?? "";
        if (body.Length == 0 || string.IsNullOrWhiteSpace(primaryUse))
            return new ParkingStandard();

        var rows = ParseRows(body);
        var use = primaryUse.Trim();

        // 용도명이 정확히 걸리는 항목을 먼저 찾고, 없으면 "그 밖의 건축물"로 떨어진다.
        var hit = rows.FirstOrDefault(r => r.Uses.Any(u => use.Contains(u) || u.Contains(use)))
                  ?? rows.FirstOrDefault(r => r.Uses.Any(u => u.Contains("그 밖의 건축물")));

        if (hit is null || hit.AreaPerSpace is null) return new ParkingStandard();

        return new ParkingStandard
        {
            AreaPerSpace = hit.AreaPerSpace,
            Basis = "주차장법 시행령 [별표 1]",
            MatchedUse = hit.Label,
            Note = "지자체 주차장 조례가 이 기준을 강화·완화할 수 있습니다. 조례 별표를 확인하세요.",
            AnnexLink = annex?.Link ?? "",
        };
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
    }
}

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
