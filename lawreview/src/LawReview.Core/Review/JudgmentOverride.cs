namespace LawReview.Core.Review;

/// <summary>
/// 검토자가 화면에서 고친 판정 한 건. AI 판정이 틀렸을 때 사람이 바로잡은 값이며,
/// 검토서(DOCX)에는 이 값이 나간다 — 문서에 서명하는 것은 사람이므로 최종 판단은 사람 것이다.
///
/// <para><b>인용 조문은 여기서 손대지 않는다.</b> 검토서에 실리는 조문은 법제처 현행 원문만이라는
/// 원칙(검토 품질 원칙 1)을 사람이 편집할 수 있게 되면 그 원칙이 무너진다.
/// 사람이 고칠 수 있는 것은 <b>판정·사유·법적기준·산정식</b> 네 가지뿐이다.</para>
///
/// null인 필드는 "고치지 않음"이고, 빈 문자열은 "비우기"다.
/// </summary>
public sealed class JudgmentOverride
{
    /// <summary>대상 체크리스트 항목 Id. 제목이 아니라 Id로 잡아야 서식이 바뀌어도 어긋나지 않는다.</summary>
    public string Id { get; set; } = "";
    /// <summary>적용 / 해당없음 / 확인필요. 셋 중 하나가 아니면 무시한다.</summary>
    public string? Verdict { get; set; }
    public string? Reason { get; set; }
    public string? Criterion { get; set; }
    public string? Calculation { get; set; }
}

public static class JudgmentOverrides
{
    /// <summary>
    /// 검토 결과에 사람의 수정을 얹은 <b>사본</b>을 만든다.
    /// 원본(작업 캐시에 남은 검토 결과)은 그대로 둬야 다시 돌리지 않고도 AI 판정으로 되돌릴 수 있다.
    /// </summary>
    public static ReviewResult Apply(ReviewResult src, IReadOnlyList<JudgmentOverride>? overrides)
    {
        if (overrides is null || overrides.Count == 0) return src;

        var byId = new Dictionary<string, JudgmentOverride>();
        foreach (var o in overrides)
            if (!string.IsNullOrWhiteSpace(o.Id)) byId[o.Id] = o;   // 같은 항목이 겹치면 뒤엣것

        var copy = new ReviewResult { Project = src.Project, Overview = src.Overview };
        foreach (var (k, v) in src.ReviewedLaws) copy.ReviewedLaws[k] = v;

        foreach (var row in src.Rows)
            copy.Rows.Add(byId.TryGetValue(row.Item.Id, out var o) ? Merge(row, o) : row);

        return copy;
    }

    /// <summary>사람이 고친 항목 수(검토서 저장 후 안내에 쓴다).</summary>
    public static int CountApplied(ReviewResult src, IReadOnlyList<JudgmentOverride>? overrides)
    {
        if (overrides is null) return 0;
        var ids = src.Rows.Select(r => r.Item.Id).ToHashSet();
        return overrides.Count(o => ids.Contains(o.Id) && !IsEmpty(o));
    }

    private static bool IsEmpty(JudgmentOverride o) =>
        o.Verdict is null && o.Reason is null && o.Criterion is null && o.Calculation is null;

    private static ReviewRow Merge(ReviewRow row, JudgmentOverride o)
    {
        var merged = new ReviewRow
        {
            Item = row.Item,
            Applicability = ParseVerdict(o.Verdict) ?? row.Applicability,
            Reason = o.Reason ?? row.Reason,
            CriterionText = o.Criterion ?? row.CriterionText,
            CalculationText = o.Calculation ?? row.CalculationText,
        };
        merged.Citations.AddRange(row.Citations);   // 조문 원문은 그대로 옮긴다
        return merged;
    }

    /// <summary>판정 문자열을 파싱한다. 셋 중 하나가 아니면 null(=원래 판정 유지).</summary>
    internal static Applicability? ParseVerdict(string? verdict) =>
        Enum.TryParse<Applicability>(verdict?.Trim(), out var v) ? v : null;
}
