using System.Text.Json;
using LawReview.Core.LandUse;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// VWorld 토지이음 색인 검증. Fixtures/vworld_*.json은 실 API 응답(2026-07-19,
/// 둔곡 407-5 필지)을 파싱에 쓰는 필드만 남기고 축약한 것이다.
/// </summary>
public class VworldClientTests
{
    private static JsonDocument Fixture(string name)
    {
        var path = ChecklistTests.FindRepoFile(
            Path.Combine("tests", "LawReview.Core.Tests", "Fixtures", name));
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void 지번_주소에서_PNU를_파싱한다()
    {
        using var doc = Fixture("vworld_addr.json");
        var result = VworldClient.ParsePnu(doc.RootElement);
        Assert.NotNull(result);
        Assert.Equal("3020015200104070005", result!.Pnu);   // 19자리 필지고유번호
        Assert.Equal("대전광역시 유성구 둔곡동 407-5", result.RefinedAddress);
    }

    [Fact]
    public void 용도지역_목록을_파싱한다()
    {
        using var doc = Fixture("vworld_landuse.json");
        var zones = VworldClient.ParseZones(doc.RootElement);
        // 실무 검토서(둔곡)의 용도지역과 일치해야 한다.
        Assert.Contains("도시지역", zones);
        Assert.Contains("일반공업지역", zones);
        Assert.Contains("지구단위계획구역", zones);
        Assert.Equal(6, zones.Count);
    }

    [Fact]
    public void 토지대장에서_대지면적을_파싱한다()
    {
        using var doc = Fixture("vworld_landregister.json");
        var register = VworldClient.ParseLandRegister(doc.RootElement);
        Assert.NotNull(register);
        // 실무 검토서의 대지면적 6,030.10㎡와 일치해야 한다.
        Assert.Equal(6030.10, register!.Area!.Value, 2);
        Assert.Equal("공장용지", register.Category);
        Assert.Equal("대전광역시 유성구 둔곡동", register.LegalDongName);
    }

    [Theory]
    [InlineData("대전광역시 유성구 둔곡동", "대전광역시", "유성구")]
    [InlineData("서울특별시 관악구 봉천동", "서울특별시", "관악구")]
    // 도 산하 "○○시 ○○구"는 조례가 "성남시 건축 조례"이므로 시+구를 함께 기초 지자체로 둔다.
    [InlineData("경기도 성남시 분당구 정자동", "경기도", "성남시 분당구")]
    [InlineData("경상남도 창원시 의창구 북면", "경상남도", "창원시 의창구")]
    [InlineData("강원특별자치도 춘천시 후평동", "강원특별자치도", "춘천시")]
    // 단층제: 기초 지자체가 따로 없다.
    [InlineData("세종특별자치시 어진동", "세종특별자치시", "세종특별자치시")]
    public void 법정동명에서_지자체명을_분리한다(string name, string province, string city)
    {
        var (p, c) = VworldClient.SplitMunicipality(name);
        Assert.Equal(province, p);
        Assert.Equal(city, c);
    }

    [Fact]
    public void 실패_응답은_null을_돌려준다()
    {
        using var bad = JsonDocument.Parse("""{"response":{"status":"NOT_FOUND"}}""");
        Assert.Null(VworldClient.ParsePnu(bad.RootElement));
        using var empty = JsonDocument.Parse("{}");
        Assert.Empty(VworldClient.ParseZones(empty.RootElement));
        Assert.Null(VworldClient.ParseLandRegister(empty.RootElement));
    }
// ── 도로명 주소 ────────────────────────────────────────────────
    //
    // 도로명은 필지가 아니라 도로 구간에 붙는 이름이라 PNU 자리(level4LC)가 **빈 문자열**로 온다.
    // 그래서 도로명 → 좌표 → (그 자리의) 지번 주소 → PNU 세 걸음을 거친다.
    // 이 전제가 깨지면 도로명 주소를 넣은 사용자는 아무것도 조회하지 못한다.

    [Fact]
    public void 도로명_응답에는_PNU가_없다()
    {
        using var doc = Fixture("vworld_addr_road.json");
        Assert.Null(VworldClient.ParsePnu(doc.RootElement));
    }

    [Fact]
    public void 도로명_응답에서_좌표를_읽는다()
    {
        using var doc = Fixture("vworld_addr_road.json");
        var point = VworldClient.ParsePoint(doc.RootElement);

        Assert.NotNull(point);
        Assert.Equal(126.95884848283765, point!.Value.X, 8);
        Assert.Equal(37.46455896717501, point.Value.Y, 8);
    }

    [Fact]
    public void 좌표로_그_자리의_지번_주소를_얻는다()
    {
        using var doc = Fixture("vworld_addr_reverse.json");
        Assert.Equal("서울특별시 관악구 신림동 산 56-1", VworldClient.ParseReverseGeocode(doc.RootElement));
    }

    [Fact]
    public void 지번으로_못_찾으면_좌표도_없다()
    {
        // 도로명 주소를 PARCEL로 조회한 실응답(NOT_FOUND) — 여기서 도로명 경로로 넘어간다.
        using var doc = Fixture("vworld_addr_notfound.json");
        Assert.Null(VworldClient.ParsePnu(doc.RootElement));
        Assert.Null(VworldClient.ParsePoint(doc.RootElement));
    }

    [Fact]
    public void 역지오코딩_결과가_비면_null이다()
    {
        using var empty = JsonDocument.Parse("""{"response":{"status":"NOT_FOUND"}}""");
        Assert.Null(VworldClient.ParseReverseGeocode(empty.RootElement));
    }
}
