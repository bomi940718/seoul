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
    public void 실패_응답은_null을_돌려준다()
    {
        using var bad = JsonDocument.Parse("""{"response":{"status":"NOT_FOUND"}}""");
        Assert.Null(VworldClient.ParsePnu(bad.RootElement));
        using var empty = JsonDocument.Parse("{}");
        Assert.Empty(VworldClient.ParseZones(empty.RootElement));
    }
}
