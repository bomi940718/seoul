using LawReview.Core.LawApi;

namespace LawReview.Core.Review;

/// <summary>검토 당시 인용한 법령과 지금 현행 법령의 시행일자 비교 결과.</summary>
/// <param name="LawName">법령·조례명</param>
/// <param name="ReviewedDate">검토서에 적힌 시행일자 (yyyyMMdd)</param>
/// <param name="CurrentDate">지금 법제처의 시행일자. 조회 실패면 빈 문자열</param>
public sealed record LawAmendment(string LawName, string ReviewedDate, string CurrentDate)
{
    /// <summary>개정되었는지. 조회에 실패했으면 단정하지 않는다(false).</summary>
    public bool Changed => CurrentDate.Length > 0 && ReviewedDate.Length > 0 && CurrentDate != ReviewedDate;

    /// <summary>조회를 못 해 확인하지 못한 경우.</summary>
    public bool Unchecked => CurrentDate.Length == 0;
}

/// <summary>
/// **법령 개정 감시** — 저장해 둔 검토서가 아직 현행 법령에 근거하고 있는지 확인한다.
///
/// 검토서는 작성 후 몇 달씩 쓰이는데 그 사이 법이 바뀐다. 인용 조문이 구법이면 검토서가
/// 통째로 틀린 것이 되므로(원칙 1 — 현행 원문만 인용) 불러올 때 시행일자를 다시 맞춰 본다.
///
/// **고치지는 않는다.** 바뀐 법령을 알려 줄 뿐이고, 다시 검토할지는 사람이 정한다 —
/// 조문 번호와 내용이 함께 바뀌었을 수 있어 자동 치환은 위험하다.
/// </summary>
public static class LawAmendmentWatcher
{
    /// <summary>
    /// 인용했던 법령들의 현행 시행일자를 조회해 비교한다.
    /// 조회에 실패한 법령은 <see cref="LawAmendment.Unchecked"/>로 남긴다 — 못 봤다는 것과
    /// 안 바뀌었다는 것은 다르다.
    /// </summary>
    public static async Task<IReadOnlyList<LawAmendment>> CheckAsync(
        MolegClient law, IReadOnlyDictionary<string, string> reviewedLaws, CancellationToken ct = default)
    {
        var results = new List<LawAmendment>();
        foreach (var (name, reviewedDate) in reviewedLaws)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(new LawAmendment(name, Normalize(reviewedDate),
                Normalize(await CurrentEffectiveDateAsync(law, name, ct))));
        }
        return results;
    }

    /// <summary>
    /// 법령명으로 현행 시행일자를 찾는다. 자치법규와 법령은 조회 대상(target)이 달라
    /// **이름으로 먼저 가른 뒤**, 못 찾으면 다른 쪽도 본다(행정규칙이 섞여 있다).
    /// </summary>
    private static async Task<string> CurrentEffectiveDateAsync(MolegClient law, string name, CancellationToken ct)
    {
        foreach (var target in TargetsFor(name))
        {
            try
            {
                var hits = await law.SearchAsync(name, target, ct);
                if (ReviewEngine.PickBestMatch(hits, name) is { } best && best.EffectiveDate.Length > 0)
                    return best.EffectiveDate;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException
                                       && !ct.IsCancellationRequested)
            {
                // 한 건 실패로 나머지 확인을 포기하지 않는다 — Unchecked로 남는다.
            }
        }
        return "";
    }

    private static LawTarget[] TargetsFor(string name) =>
        name.Contains("조례") || name.Contains("규칙") && !name.Contains("시행규칙")
            ? new[] { LawTarget.Ordinance, LawTarget.Law }
            : new[] { LawTarget.Law, LawTarget.AdminRule };

    /// <summary>"2026.07.28"·"2026-07-28" → "20260728". 비교는 형식을 맞춘 뒤에 한다.</summary>
    internal static string Normalize(string? date) =>
        new((date ?? "").Where(char.IsDigit).ToArray());
}
