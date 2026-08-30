using LawReview.Core.LawApi;
using LawReview.Core.Review;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 대지 안의 공지(건축법 제58조) 이격거리 기준 파싱 검증.
///
/// 픽스처는 둘 다 실제 원문이다:
///  · Fixtures/daejeon_building_annex.hwp — 대전광역시 건축 조례 별표(법제처 flDownload 원본).
///    [별표 3]이 대지 안의 공지 기준이고, 같은 파일에 별표 1·2·4가 함께 들어 있다.
///  · Fixtures/bldg_decree_annex2.txt — 건축법 시행령 별표 2(API 별표내용 그대로, 괘선 표).
///
/// 조례 값이 실제 적용 기준이고 시행령은 조례로 정할 범위만 준다. 파서가 깨지면 이격거리가
/// 조용히 모법 범위로 떨어지므로 실파일로 고정한다.
/// </summary>
public class SetbackStandardResolverTests
{
    private static IReadOnlyList<string> OrdinanceLines() => HwpTextExtractor.ExtractLines(
        File.ReadAllBytes(ChecklistTests.FindRepoFile(Path.Combine(
            "tests", "LawReview.Core.Tests", "Fixtures", "daejeon_building_annex.hwp"))));

    private static string DecreeAnnex2() => File.ReadAllText(ChecklistTests.FindRepoFile(Path.Combine(
        "tests", "LawReview.Core.Tests", "Fixtures", "bldg_decree_annex2.txt")));

    private static LawText Decree() => new(
        "건축법 시행령", "20260728", Array.Empty<Article>(), new[]
        {
            new Annex("1", "용도별 건축물의 종류(제3조의5 관련)", "별표", "https://www.law.go.kr/annex1.pdf", ""),
            new Annex("2", "대지의 공지 기준(제80조의2 관련)", "별표", "https://www.law.go.kr/annex2.pdf", DecreeAnnex2()),
        });

    private static OrdinanceAnnex DaejeonOrdinance() =>
        new("대전광역시 건축 조례", "https://www.law.go.kr/flDownload.do?flSeq=1", OrdinanceLines(), "20260410");

    // ── 조례 별표 ────────────────────────────────────────────────────

    [Fact]
    public void 조례_별표에서_두_축의_기준을_모두_읽는다()
    {
        var rules = SetbackStandardResolver.ParseOrdinanceAnnex(OrdinanceLines());

        Assert.Contains(rules, r => r.Side == SetbackSide.건축선);
        Assert.Contains(rules, r => r.Side == SetbackSide.인접대지경계선);
        Assert.All(rules, r => Assert.NotEmpty(r.Distances));
    }

    [Fact]
    public void 조례_공장_행의_거리를_원문_그대로_읽는다()
    {
        var rules = SetbackStandardResolver.ParseOrdinanceAnnex(OrdinanceLines());

        var 건축선 = rules.First(r => r.Side == SetbackSide.건축선 && r.Target.Contains("공장"));
        Assert.Contains(건축선.Distances, d => d.Contains("준공업지역") && d.Contains("1.5미터 이상"));
        Assert.Contains(건축선.Distances, d => d.Contains("준공업지역 외의 지역") && d.Contains("3미터 이상"));

        var 인접 = rules.First(r => r.Side == SetbackSide.인접대지경계선 && r.Target.Contains("공장"));
        Assert.Contains(인접.Distances, d => d.Contains("준공업지역 외의 지역") && d.Contains("1.5미터 이상"));
    }

    [Fact]
    public void 조례_공동주택_행은_주택_종류별_거리를_따로_갖는다()
    {
        var rules = SetbackStandardResolver.ParseOrdinanceAnnex(OrdinanceLines());
        var 공동주택 = rules.First(r => r.Side == SetbackSide.건축선 && r.Target.StartsWith("공동주택"));

        Assert.Contains(공동주택.Distances, d => d.StartsWith("아파트"));
        Assert.Contains(공동주택.Distances, d => d.StartsWith("연립주택"));
        Assert.Contains(공동주택.Distances, d => d.StartsWith("다세대주택"));
    }

    [Fact]
    public void 다른_별표_내용이_섞이지_않는다()
    {
        // 같은 HWP에 별표 1(건축위원회)·별표 4(과태료 등)가 함께 들어 있다.
        var rules = SetbackStandardResolver.ParseOrdinanceAnnex(OrdinanceLines());
        Assert.All(rules, r => Assert.DoesNotContain("과태료", r.Target));
    }

    // ── 칸 형식 조례 (안양시) ────────────────────────────────────────
    //
    // 같은 "대지의 공지 기준"이라도 조례마다 표 모양이 다르다. 안양시는 대상·지역구분·거리가
    // 각각 다른 칸이라 머리표(․)가 없고 줄이 따로 떨어져 나온다. 대전 형식만 파싱하면
    // 이 조례에서는 값을 통째로 놓치고 조용히 모법으로 떨어진다.

    private static IReadOnlyList<string> AnyangLines() => HwpTextExtractor.ExtractLines(
        File.ReadAllBytes(ChecklistTests.FindRepoFile(Path.Combine(
            "tests", "LawReview.Core.Tests", "Fixtures", "anyang_setback_annex.hwp"))));

    [Fact]
    public void 칸_형식_조례에서도_기준을_읽는다()
    {
        var rules = SetbackStandardResolver.ParseOrdinanceAnnex(AnyangLines());

        Assert.Contains(rules, r => r.Side == SetbackSide.건축선);
        Assert.Contains(rules, r => r.Side == SetbackSide.인접대지경계선);
        Assert.All(rules, r => Assert.NotEmpty(r.Distances));
    }

    [Fact]
    public void 칸_형식은_지역구분과_거리를_짝지어_되살린다()
    {
        var rules = SetbackStandardResolver.ParseOrdinanceAnnex(AnyangLines());
        var 공장 = rules.First(r => r.Side == SetbackSide.건축선 && r.Target.Contains("공장"));

        // 조건 칸(준공업지역 / 그 밖의 지역)과 거리 칸(1.5미터 / 3미터)이 순서대로 대응한다.
        Assert.Contains("준공업지역: 1.5미터 이상", 공장.Distances);
        Assert.Contains("그 밖의 지역: 3미터 이상", 공장.Distances);
    }

    [Fact]
    public void 조건_문장_속의_미터는_거리_칸으로_보지_않는다()
    {
        // "(폭 12미터 이상 도로에 접한 부분…)"은 거리 값이 아니라 조건이다.
        var rules = SetbackStandardResolver.ParseOrdinanceAnnex(AnyangLines());
        var 공동주택 = rules.First(r => r.Side == SetbackSide.건축선 && r.Target.StartsWith("공동주택"));

        Assert.Contains(공동주택.Distances, d => d.Contains("폭 12미터 이상 도로에 접한"));
        Assert.DoesNotContain("공동주택 아파트", 공동주택.Target);   // 조건 칸이 항목명에 붙지 않는다
    }

    // ── 조례마다 다른 표 모양 ────────────────────────────────────────
    //
    // 같은 "대지 안의 공지 기준"인데 조례마다 표 짜임새가 다르다. 실제 원문에서 확인한 변형을
    // 그대로 픽스처로 둔다(별표 부분만 추출한 텍스트). 하나라도 놓치면 그 지자체에서는
    // 값을 못 읽고 조용히 모법 범위로 떨어진다 — 조용한 실패라 눈치채기 어렵다.

    private static IReadOnlyList<string> Fixture(string name) => File.ReadAllLines(
        ChecklistTests.FindRepoFile(Path.Combine("tests", "LawReview.Core.Tests", "Fixtures", name)));

    [Fact]
    public void 서울_면적조건과_거리가_같은_머리표를_쓸_때_면적을_거리로_보지_않는다()
    {
        // 서울 조례는 "• 1,000제곱미터 이상"(면적 조건)과 "• 3미터 이상"(거리)을 같은 칸에 둔다.
        var rules = SetbackStandardResolver.ParseOrdinanceAnnex(Fixture("seoul_setback_annex.txt"));
        var 판매 = rules.First(r => r.Side == SetbackSide.건축선 && r.Target.Contains("판매시설"));

        Assert.Equal(new[] { "1,000제곱미터 이상: 3미터 이상" }, 판매.Distances);
    }

    [Fact]
    public void 성남_항목명이_칸_안에서_끊겨도_용도를_잃지_않는다()
    {
        // "…합계가 500제곱미터"에서 칸이 끊기고 "이상인 공장(…)"이 다음 줄에 온다.
        // 여기서 끊으면 항목명에 "공장"이 없어 용도 매칭이 통째로 어긋난다.
        var rules = SetbackStandardResolver.ParseOrdinanceAnnex(Fixture("seongnam_setback_annex.txt"));
        var 공장 = rules.First(r => r.Side == SetbackSide.건축선 && r.Target.Contains("공장"));

        Assert.Contains(공장.Distances, d => d.Contains("준공업지역") && d.Contains("1.5미터 이상"));
    }

    [Fact]
    public void 성남_포괄행은_그_밖의_건축물이_아니라_모든_건축물로_적힌다()
    {
        var rules = SetbackStandardResolver.ParseOrdinanceAnnex(Fixture("seongnam_setback_annex.txt"));
        var std = new SetbackStandard { Rules = rules };

        // 건축선 "연면적 1,000제곱미터 이상의 모든 건축물" / 인접 "기타 모든 건축물"
        var matched = std.MatchedFor("업무시설");
        Assert.Equal(2, matched.Count);
        Assert.All(matched, r => Assert.True(r.IsCatchAll));
        // 본문 중간에 "그 밖에"가 들어간 공동주택 행이 포괄 행으로 잡히면 안 된다.
        Assert.DoesNotContain(matched, r => r.Target.StartsWith("공동주택"));
    }

    [Fact]
    public void 속초_하이픈_머리표도_읽는다()
    {
        var rules = SetbackStandardResolver.ParseOrdinanceAnnex(Fixture("sokcho_setback_annex.txt"));
        var 공장 = rules.First(r => r.Side == SetbackSide.건축선 && r.Target.Contains("공장"));

        Assert.Contains(공장.Distances, d => d.Contains("준공업지역") && d.Contains("1.5미터 이상"));
        Assert.Contains(rules, r => r.Side == SetbackSide.인접대지경계선);
    }

    // ── 시행령 별표 2 (괘선 표) ──────────────────────────────────────

    [Fact]
    public void 시행령_별표2에서_조례로_정할_범위를_읽는다()
    {
        var std = SetbackStandardResolver.ResolveFromDecree(Decree());

        Assert.Equal("건축법 시행령 [별표 2]", std.Basis);
        var 공장 = std.Rules.First(r => r.Side == SetbackSide.건축선 && r.Target.Contains("공장"));
        Assert.Contains(공장.Distances, d => d.Contains("1.5미터 이상 6미터 이하"));
        // 칸 안에서 줄바꿈된 "3미터 이상 6미터 이" + "하"가 이어 붙어야 한다.
        Assert.Contains(공장.Distances, d => d.Contains("3미터 이상 6미터 이하"));
    }

    [Fact]
    public void 시행령_별표2의_비고는_항목으로_잡지_않는다()
    {
        var std = SetbackStandardResolver.ResolveFromDecree(Decree());
        Assert.All(std.Rules, r => Assert.DoesNotContain("착공신고", r.Target));
    }

    // ── 법 위계 ──────────────────────────────────────────────────────

    [Fact]
    public void 조례에_기준이_있으면_조례가_이긴다()
    {
        var std = SetbackStandardResolver.ResolveChain(new[] { DaejeonOrdinance() }, Decree());

        Assert.Equal("대전광역시 건축 조례 [별표 3]", std.Basis);
        Assert.Equal("20260410", std.EffectiveDate);
        // 조례는 확정값("3미터 이상"), 시행령은 범위("3미터 이상 6미터 이하")다.
        var 공장 = std.Rules.First(r => r.Side == SetbackSide.건축선 && r.Target.Contains("공장"));
        Assert.All(공장.Distances, d => Assert.DoesNotContain("6미터 이하", d));
    }

    [Fact]
    public void 조례_별표를_읽지_못하면_모법으로_내려가고_그_사실을_남긴다()
    {
        var 빈조례 = new OrdinanceAnnex("안양시 건축 조례", "", Array.Empty<string>());
        var std = SetbackStandardResolver.ResolveChain(new[] { 빈조례 }, Decree());

        Assert.Equal("건축법 시행령 [별표 2]", std.Basis);
        Assert.Contains("안양시 건축 조례", std.Note);
    }

    [Fact]
    public void 아무데도_없으면_빈_결과다()
    {
        var std = SetbackStandardResolver.ResolveChain(Array.Empty<OrdinanceAnnex>(), null);
        Assert.Empty(std.Rules);
        Assert.Null(std.Basis);
    }

    // ── 용도 매칭 ────────────────────────────────────────────────────

    [Fact]
    public void 용도가_없는_경우_그_밖의_건축물_행을_쓴다()
    {
        var std = SetbackStandardResolver.ResolveChain(new[] { DaejeonOrdinance() }, Decree());
        var matched = std.MatchedFor("교육연구시설");

        Assert.NotEmpty(matched);
        Assert.All(matched, r => Assert.True(r.IsCatchAll));
    }

    [Fact]
    public void 법적기준은_두_축을_모두_담고_출처를_밝힌다()
    {
        var std = SetbackStandardResolver.ResolveChain(new[] { DaejeonOrdinance() }, Decree());
        var text = std.CriterionText("공장")!;

        Assert.Contains("건축선", text);
        Assert.Contains("인접대지경계선", text);
        Assert.Contains("대전광역시 건축 조례 [별표 3]", text);
    }

    [Fact]
    public void 단서가_붙은_행은_자동판정_금지_신호를_남긴다()
    {
        // "산업단지에 건축하는 공장은 제외한다" — 용도명만으로 적용을 단정할 수 없다는 표시.
        var std = SetbackStandardResolver.ResolveChain(new[] { DaejeonOrdinance() }, Decree());
        Assert.Contains(std.MatchedFor("공장"), r => r.HasProviso);
    }
}
