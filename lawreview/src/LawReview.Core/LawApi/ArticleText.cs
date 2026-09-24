using System.Text.RegularExpressions;

namespace LawReview.Core.LawApi;

/// <summary>
/// 조문 본문을 항(①②③)·호(1. 2. 3.)로 나눈다.
///
/// 자치법규 본문은 `조내용`에 **줄바꿈 없는 평문 한 덩어리**로 온다. 항·호 경계가 글자로만
/// 표시되므로 이 분해가 조례에서 값을 읽는 모든 해석기의 출발점이다.
/// </summary>
public static class ArticleText
{
    // 법령 본문에 섞여 오는 <개정 …>·<신설 …> 태그와 표 이미지 <img …>.
    private static readonly Regex Tag = new("<[^>]*>", RegexOptions.Compiled);

    /// <summary>&lt;개정 2020.12.29.&gt; 같은 태그를 지운다 — 숫자가 호 번호로 오인되는 것을 막는다.</summary>
    public static string StripTags(string body) => Tag.Replace(body ?? "", " ");

    // 항 머리 ①(U+2460) ~ ⑳(U+3273 아님 — ①..⑳은 U+2460..U+2473 연속)
    private const char FirstCircled = '①';
    private const char LastCircled = '⑳';

    /// <summary>
    /// 항(①②③…) 단위로 나눈다. 항 표시가 없으면 본문 전체를 한 덩어리로 돌려준다.
    /// 조 제목과 첫 항 사이의 머리글은 버린다(첫 항 앞에는 "제34조(공개 공지 등의 확보)"만 온다).
    /// </summary>
    public static IReadOnlyList<string> Paragraphs(string body)
    {
        var text = body ?? "";
        var marks = new List<int>();
        for (var i = 0; i < text.Length; i++)
            if (text[i] >= FirstCircled && text[i] <= LastCircled) marks.Add(i);

        if (marks.Count == 0) return text.Trim().Length > 0 ? new[] { text.Trim() } : Array.Empty<string>();

        var result = new List<string>();
        for (var i = 0; i < marks.Count; i++)
        {
            var start = marks[i] + 1;
            var end = i + 1 < marks.Count ? marks[i + 1] : text.Length;
            result.Add(text[start..end].Trim());
        }
        return result;
    }

    /// <summary>
    /// 호(1. 2. 3. …) 단위로 나눈다.
    ///
    /// **번호를 순서대로 찾아 끊는다** — 정규식으로 "숫자+마침표"를 한꺼번에 찾으면 안 된다.
    /// 조례 본문은 호가 값에 바로 붙어 오기 때문이다: "…: 100분의 82. 업무시설…"에서
    /// 앞 호의 값은 "100분의 8"이고 "2."가 다음 호인데, 뭉뚱그려 찾으면 "82."를 경계로 잡아
    /// **비율 8이 통째로 사라진다.** 1 → 2 → 3 순서로 다음 번호만 찾으면 이 문제가 없다.
    /// </summary>
    public static IReadOnlyList<string> Clauses(string paragraph) =>
        Split(paragraph ?? "", n => $"{n}.");

    private const string Ordinals = "가나다라마바사아자차카타파하";

    /// <summary>
    /// 목(가. 나. 다. …) 단위로 나눈다. 호 안에서 다시 갈리는 기준(성남시 공개공지의 바닥면적
    /// 구간별 비율)이 여기 들어 있다.
    ///
    /// **"다."는 "…하여야 한다."의 끝과 생김새가 같다.** 그래서 호출부가 쪼갠 결과를 반드시
    /// 검증해야 한다(예: 모든 목에 비율이 있는지). 검증에 실패하면 쪼개지 않은 것으로 봐야 한다.
    /// </summary>
    public static IReadOnlyList<string> SubItems(string clause) =>
        Split(clause ?? "", n => n <= Ordinals.Length ? $"{Ordinals[n - 1]}." : null);

    /// <summary>
    /// 번호를 1 → 2 → 3 순서로 하나씩 찾아 끊는다. 정규식으로 한꺼번에 찾으면 안 되는 이유는
    /// <see cref="Clauses"/> 설명 참고.
    /// </summary>
    private static IReadOnlyList<string> Split(string text, Func<int, string?> markerFor)
    {
        var items = new List<string>();
        var number = 1;
        var marker = markerFor(number);
        if (marker is null) return items;

        var start = FindMarker(text, marker, 0);
        while (start >= 0)
        {
            var bodyStart = start + marker.Length;
            var nextMarker = markerFor(number + 1);
            var next = nextMarker is null ? -1 : FindMarker(text, nextMarker, bodyStart);
            items.Add(text[bodyStart..(next < 0 ? text.Length : next)].Trim());
            if (next < 0) break;
            start = next;
            marker = nextMarker!;
            number++;
        }
        return items;
    }

    /// <summary>표시가 나오는 자리. 뒤에 숫자가 이어지면 날짜·소수점이므로 건너뛴다.</summary>
    private static int FindMarker(string text, string marker, int from)
    {
        if (from >= text.Length) return -1;
        for (var i = text.IndexOf(marker, from, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(marker, i + 1, StringComparison.Ordinal))
        {
            var after = i + marker.Length;
            if (after < text.Length && char.IsDigit(text[after])) continue;   // "2020.12.29"
            return i;
        }
        return -1;
    }

    // "100분의 15" / "15퍼센트" / "15%"
    private static readonly Regex RatioPattern =
        new(@"100\s*분의\s*([\d.]+)|([\d.]+)\s*퍼센트|([\d.]+)\s*%", RegexOptions.Compiled);

    /// <summary>비율 표기를 0~1 사이 소수로 읽는다("100분의 8" → 0.08). 없으면 null.</summary>
    public static double? Ratio(string text)
    {
        var m = RatioPattern.Match(text ?? "");
        if (!m.Success) return null;
        var raw = m.Groups[1].Success ? m.Groups[1].Value
            : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
        return double.TryParse(raw.TrimEnd('.'), out var v) ? v / 100.0 : null;
    }

    /// <summary>
    /// 용도 목록을 쪼갠다. "및"은 조심해서 다뤄야 한다 —
    /// "판매시설 및 운수시설"은 둘이지만 "문화 및 집회시설"은 하나다.
    /// </summary>
    public static IReadOnlyList<string> SplitUses(string label)
    {
        var cleaned = Regex.Replace(label ?? "", @"[\(\[「｢][^\)\]」｣]*[\)\]」｣]?", " ");
        var parts = cleaned.Split(new[] { ',', '·', 'ㆍ', '，' }, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(SplitOnAnd)
            .Select(s => s.Trim())
            .Where(s => s.Length >= 2)
            .ToList();
        return parts;
    }

    private static IEnumerable<string> SplitOnAnd(string part)
    {
        var idx = part.IndexOf(" 및 ", StringComparison.Ordinal);
        if (idx <= 0) return new[] { part };
        var head = part[..idx].Trim();
        // 앞쪽이 그 자체로 완성된 용도명일 때만 쪼갠다("판매시설 및 운수시설" → 둘,
        // "문화 및 집회시설" → 하나).
        return LooksLikeUse(head)
            ? new[] { head, part[(idx + 3)..].Trim() }
            : new[] { part };
    }

    private static bool LooksLikeUse(string s) =>
        s.EndsWith("시설") || s.EndsWith("식장") || s.EndsWith("주택") || s.EndsWith("건축물");
}
