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
    public void 목록에_없는_용도는_그_밖의_건축물_기준을_쓴다()
    {
        var r = ParkingStandardResolver.Resolve(Decree(), "동물병원");
        Assert.Equal(300, r.AreaPerSpace);
    }

    [Fact]
    public void 조례로_달라질_수_있음을_함께_알린다()
    {
        // 실제로 둔곡(대전)은 조례가 공장을 200㎡/대로 강화했다.
        // 자치법규 별표는 HWP라 읽을 수 없으므로 안내와 원문 링크를 남겨야 한다.
        var r = ParkingStandardResolver.Resolve(Decree(), "공장");
        Assert.Contains("조례", r.Note);
        Assert.NotEmpty(r.AnnexLink);
    }

    [Fact]
    public void 별표를_못_받으면_빈_결과를_돌려준다()
    {
        var empty = new LawText("주차장법 시행령", "20250817", Array.Empty<Article>(), Array.Empty<Annex>());
        Assert.Null(ParkingStandardResolver.Resolve(empty, "공장").AreaPerSpace);
        Assert.Null(ParkingStandardResolver.Resolve(null, "공장").AreaPerSpace);
    }
}
