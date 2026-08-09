namespace LawReview.Core.Models;

/// <summary>
/// 조례를 제정하는 지자체를 판별한다.
///
/// 건축조례·도시계획조례는 아무 지자체나 만드는 게 아니다:
///  · 특별시·광역시·특별자치시·특별자치도 → 그 광역 지자체가 제정 (자치구는 제정하지 않음)
///    예) 대전광역시 유성구 → "대전광역시 건축 조례"
///  · 도(경기도·강원특별자치도 등) → 산하 시·군이 제정 (도는 제정하지 않음)
///    예) 강원특별자치도 속초시 → "속초시 도시계획 조례"
///       (실제로 강원특별자치도 도시계획 조례에는 용도지역별 건폐율 조문이 없다)
///
/// 이 구분을 틀리면 조례를 찾지 못해 법정 한도·조례 조문이 통째로 비게 된다.
/// </summary>
public static class Municipality
{
    /// <summary>조례명 앞에 붙일 지자체 이름을 돌려준다.</summary>
    public static string OrdinanceAuthority(string province, string city)
    {
        var p = (province ?? "").Trim();
        var c = (city ?? "").Trim();

        if (p.Length == 0) return c;

        // 특별시·광역시·특별자치시·특별자치도는 광역이 제정한다.
        if (p.EndsWith("특별시") || p.EndsWith("광역시") || p.EndsWith("특별자치시") || p.EndsWith("특별자치도"))
        {
            // 다만 특별자치도(강원·전북·제주)는 도시계획조례를 산하 시·군이 두는 경우가 있어
            // 시·군 이름이 있으면 그것을 우선한다. (제주는 시가 자치권이 없어 도가 제정)
            if (p.EndsWith("특별자치도") && c.Length > 0 && !p.StartsWith("제주"))
                return BaseCity(c);
            return p;
        }

        // 도(道) 산하는 시·군이 제정한다. 자치구는 조례 주체가 아니므로 시까지만 쓴다.
        return c.Length > 0 ? BaseCity(c) : p;
    }

    /// <summary>"성남시 분당구" → "성남시" (자치구는 건축·도시계획 조례를 제정하지 않는다).</summary>
    internal static string BaseCity(string city)
    {
        var parts = city.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
            if (part.EndsWith("시") || part.EndsWith("군")) return part;
        return parts.Length > 0 ? parts[0] : city;
    }
}
