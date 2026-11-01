namespace LawReview.Core.Municipal;

/// <summary>
/// 지구단위계획 결정 레코드 (지자체 포털 조회 결과).
/// 검토서에는 구역명·고시번호·고시일자·원문(고시문 PDF) 링크만 인용한다 — 도면(지침도) 규제는
/// 자동 판정 대상이 아니므로 항목 판정은 "확인필요"를 유지한다 (검토 품질 원칙 5).
/// </summary>
public sealed record DistrictPlanRecord(
    string ZoneName,        // 구역명 (예: 봉천지역중심 지구단위계획구역)
    string Location,        // 위치 (예: 관악구 봉천동 857-1번지 일대)
    string NoticeOrgan,     // 고시기관 (예: 서울특별시)
    string NoticeNo,        // 고시번호 (예: 제2026-290호)
    string NoticeDate,      // 고시일자 (yyyy-MM-dd)
    string NoticeTitle,     // 고시 제목
    double? AreaAfter,      // 구역면적(변경후, ㎡) — 조서정보
    string NoticePdfUrl,    // 고시문 PDF 원문 링크 (없으면 "")
    string PortalUrl);      // 포털 열람 페이지

/// <summary>
/// 시별 지구단위계획 조회 모듈. 서울(도시공간포털)부터 구현하고,
/// 타 시는 이 인터페이스 구현을 추가하는 방식으로 확장한다.
/// </summary>
public interface IDistrictPlanProvider
{
    /// <summary>이 제공자가 담당하는 광역 지자체명 (예: "서울특별시").</summary>
    string Province { get; }

    /// <summary>출처 이름. 검토 진행 표시와 검토서 인용에 쓴다 (예: "안양시 고시공고").</summary>
    string Name { get; }

    /// <summary>
    /// 이 프로젝트를 담당하는 제공자인지.
    /// 광역이 포털을 운영하는 곳(서울)은 광역명으로, 시·군이 제각각인 곳(경기도)은
    /// **기초 지자체까지 봐야** 한다 — 안양시 수집기를 경기도 전체에 쓸 수는 없다.
    /// </summary>
    bool Matches(string province, string city);

    /// <summary>명칭 또는 위치 키워드로 지구단위계획구역을 검색한다 (예: "봉천동").</summary>
    Task<IReadOnlyList<DistrictPlanRecord>> SearchAsync(string keyword, CancellationToken ct = default);

    /// <summary>고시문 PDF를 내려받는다. 링크가 없으면 false.</summary>
    Task<bool> DownloadNoticePdfAsync(DistrictPlanRecord record, string filePath, CancellationToken ct = default);
}

/// <summary>
/// 등록된 지자체별 제공자 목록. 광역+기초 지자체명으로 담당 제공자를 찾는다.
/// 새 지자체를 붙일 때는 <see cref="IDistrictPlanProvider"/> 구현을 만들어 여기 추가한다.
/// </summary>
public static class DistrictPlanProviders
{
    public static IDistrictPlanProvider? For(string province, string city, HttpClient http)
    {
        IDistrictPlanProvider[] all = { new SeoulUrbanPortalClient(http), new AnyangNoticeClient(http) };
        return all.FirstOrDefault(p => p.Matches(province ?? "", city ?? ""));
    }

    /// <summary>
    /// 지번 주소에서 검색 키워드를 뽑는다: "서울특별시 관악구 봉천동 857-1" → "봉천동".
    /// 포털 검색은 명칭·위치 부분일치이므로 법정동 단위가 재현율이 가장 좋다.
    /// 필지-구역 정확 대응은 지도(WFS) 없이는 불가능하므로 후보 목록 + 원문 링크로 안내한다.
    /// </summary>
    public static string? KeywordFromAddress(string siteAddress)
    {
        foreach (var token in siteAddress.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (token.Length >= 2 && (token.EndsWith('동') || token.EndsWith('가') || token.EndsWith('읍') || token.EndsWith('면')))
                return token;
        return null;
    }
}
