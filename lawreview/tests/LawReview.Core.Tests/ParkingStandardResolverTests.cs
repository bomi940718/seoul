using LawReview.Core.LawApi;
using LawReview.Core.Review;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 주차장법 시행령 별표 1에서 용도별 설치기준을 뽑는지 검증.
/// 표 텍스트는 실제 별표(2025 시행)에서 발췌한 형태 그대로다.
/// </summary>
public class ParkingStandardResolverTests
{
    private const string Annex1 = """
┏━━━━━━━━━━━━━━━━━━━━━━━━━┳━━━━━━━━━━━━━━━━━━━━━━━━━┓
┃    시설물                │  설치기준                 ┃
┃1. 위락시설               │○ 시설면적 100㎡당 1대(시설면적/100㎡) ┃
┃2. 문화 및 집회시설(관람장은 제 │○ 시설면적 150㎡당 1대(시설면적/150㎡) ┃
┃외한다), 종교시설, 판매시설, 운 │                          ┃
┃수시설, 의료시설           │                          ┃
┃3. 제1종 근린생활시설[「건축법 │○ 시설면적 200㎡당 1대(시설면적/200㎡) ┃
┃4. 단독주택(다가구주택은 제외한다) │○ 시설면적 50㎡ 초과 150㎡ 이하: 1대 ┃
┃7. 수련시설, 공장(아파트형은 제외 │○ 시설면적 350㎡당 1대(시설면적/350㎡) ┃
┃한다), 발전시설            │                          ┃
┃8. 창고시설               │○ 시설면적 400㎡당 1대(시설면적/400㎡) ┃
┃11. 그 밖의 건축물         │○ 시설면적 300㎡당 1대(시설면적/300㎡) ┃
┗━━━━━━━━━━━━━━━━━━━━━━━━━┻━━━━━━━━━━━━━━━━━━━━━━━━━┛
""";

    private static LawText Decree() => new(
        "주차장법 시행령", "20250817", Array.Empty<Article>(), new[]
        {
            new Annex("1", "부설주차장의 설치대상 시설물 종류 및 설치기준(제6조제1항 관련)",
                "별표", "https://www.law.go.kr/annex.pdf", Annex1),
        });

    [Theory]
    [InlineData("공장", 350)]
    [InlineData("위락시설", 100)]
    [InlineData("판매시설", 150)]
    [InlineData("창고시설", 400)]
    public void 용도별_설치기준을_찾는다(string use, double expected)
    {
        var r = ParkingStandardResolver.Resolve(Decree(), use);
        Assert.Equal(expected, r.AreaPerSpace);
        Assert.Equal("주차장법 시행령 [별표 1]", r.Basis);
    }

    [Fact]
    public void 시행령에_없는_용도는_정확매칭이_아니면_비어_있다()
    {
        // Resolve는 정확 매칭만 한다. "그 밖의 건축물"로 떨어뜨리는 건 ResolveChain의 몫이다.
        Assert.Null(ParkingStandardResolver.Resolve(Decree(), "동물병원").AreaPerSpace);
    }

    // ── 법 위계: 기초 조례 → 광역 조례 → 모법 → (모법의 "그 밖의 건축물") ──

    private static readonly string[] AnyangLines =
    {
        "7. 수련시설, 공장(아파트형은 제외한다), 발전시설",
        "○ 시설면적 200㎡당 1대(시설면적/200㎡) 다만, 산업단지 공장용 건축물은 450㎡당 1대",
    };

    private static readonly string[] GyeonggiLines =
    {
        "1. 위락시설",
        "○ 시설면적 80㎡당 1대(시설면적/80㎡)",
    };

    private static OrdinanceAnnex[] Hierarchy() =>
        new[]
        {
            new OrdinanceAnnex("안양시 주차장 설치 및 관리 조례", "link1", AnyangLines),
            new OrdinanceAnnex("경기도 주차장 조례", "link2", GyeonggiLines),
        };

    [Fact]
    public void 기초_조례에_있으면_가장_먼저_적용한다()
    {
        // 공장: 시행령 350 / 안양시 조례 200 → 기초 조례가 이긴다
        var r = ParkingStandardResolver.ResolveChain(Hierarchy(), Decree(), "공장");
        Assert.Equal(200, r.AreaPerSpace);
        Assert.Contains("안양시", r.Basis!);
    }

    [Fact]
    public void 기초에_없으면_광역_조례로_내려간다()
    {
        // 위락시설: 안양시 목록엔 없고 경기도 조례에 80㎡ → 광역 조례 적용(시행령 100이 아니다)
        var r = ParkingStandardResolver.ResolveChain(Hierarchy(), Decree(), "위락시설");
        Assert.Equal(80, r.AreaPerSpace);
        Assert.Contains("경기도", r.Basis!);
    }

    [Fact]
    public void 조례에_모두_없으면_모법으로_간다()
    {
        // 창고시설: 두 조례에 없음 → 주차장법 시행령 별표 1(400㎡)
        var r = ParkingStandardResolver.ResolveChain(Hierarchy(), Decree(), "창고시설");
        Assert.Equal(400, r.AreaPerSpace);
        Assert.Equal("주차장법 시행령 [별표 1]", r.Basis);
        Assert.Contains("모법 기준을 적용", r.Note!);
    }

    [Fact]
    public void 어느_법에도_없으면_모법의_그_밖의_건축물을_쓴다()
    {
        var r = ParkingStandardResolver.ResolveChain(Hierarchy(), Decree(), "동물병원");
        Assert.Equal(300, r.AreaPerSpace);
        Assert.Contains("그 밖의 건축물", r.Note!);
    }

    [Fact]
    public void 조례가_없어도_모법으로_판정된다()
    {
        var r = ParkingStandardResolver.ResolveChain(Array.Empty<OrdinanceAnnex>(), Decree(), "판매시설");
        Assert.Equal(150, r.AreaPerSpace);
    }

    [Fact]
    public void 별표를_못_받으면_빈_결과를_돌려준다()
    {
        var empty = new LawText("주차장법 시행령", "20250817", Array.Empty<Article>(), Array.Empty<Annex>());
        Assert.Null(ParkingStandardResolver.Resolve(empty, "공장").AreaPerSpace);
        Assert.Null(ParkingStandardResolver.Resolve(null, "공장").AreaPerSpace);
    }
}
