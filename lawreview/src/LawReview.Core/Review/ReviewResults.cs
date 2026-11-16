namespace LawReview.Core.Review;

public static class ReviewResults
{
    /// <summary>
    /// 여러 번 돌린 검토 결과를 하나로 합친다(오래된 것 → 최신 순으로 넘길 것).
    ///
    /// <para>기본 검토(주요 법규)와 장별 상세검토는 비용 때문에 따로 돌린다. 그래서 검토서를 뽑을 때
    /// 마지막 실행 하나만 쓰면 <b>다른 한쪽이 검토서에서 통째로 빠진다.</b> 항목 단위로 합쳐야 한다.</para>
    ///
    /// 같은 항목이 두 번 나오면 나중 검토를 쓰고, 행 순서는 체크리스트(실무 표준 서식) 순서로 되돌린다.
    /// </summary>
    public static ReviewResult? Merge(IReadOnlyList<ReviewResult> results)
    {
        if (results.Count == 0) return null;
        if (results.Count == 1) return results[0];

        var newest = results[^1];
        var merged = new ReviewResult { Project = newest.Project, Overview = newest.Overview };

        var byId = new Dictionary<string, ReviewRow>();
        foreach (var r in results)
        {
            foreach (var (name, date) in r.ReviewedLaws) merged.ReviewedLaws[name] = date;
            foreach (var row in r.Rows) byId[row.Item.Id] = row;   // 나중 검토가 이긴다
        }

        merged.Rows.AddRange(byId.Values.OrderBy(row => CanonicalOrder(row.Item.Id)));
        return merged;
    }

    /// <summary>체크리스트 파일에서의 위치. 서식 순서가 곧 표준이므로 합친 뒤 이 순서로 되돌린다.</summary>
    private static int CanonicalOrder(string itemId) =>
        CanonicalIndex.Value.TryGetValue(itemId, out var i) ? i : int.MaxValue;

    private static readonly Lazy<Dictionary<string, int>> CanonicalIndex = new(() =>
    {
        try
        {
            return ChecklistLoader.LoadDefault()
                .Select((item, i) => (item.Id, i))
                .ToDictionary(x => x.Id, x => x.i);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return new Dictionary<string, int>();   // 순서를 못 얻으면 넣은 순서를 쓴다
        }
    });
}
