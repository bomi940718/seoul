using System.Text.Json;
using LawReview.Core.LawApi;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 법제처 API 실제 응답(2026-07 캡처, Fixtures/)에 대한 파싱 검증.
/// target마다 응답 구조가 다르다는 사실이 실검증으로 확인되었으므로,
/// 파서를 고칠 때는 반드시 이 실데이터 픽스처를 통과해야 한다.
/// </summary>
public class MolegParsingTests
{
    private static JsonDocument Fixture(string name)
    {
        var path = ChecklistTests.FindRepoFile(
            Path.Combine("tests", "LawReview.Core.Tests", "Fixtures", name));
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void 법령_검색_응답을_파싱한다()
    {
        using var doc = Fixture("search_law.json");
        var hits = MolegClient.ParseSearch(doc.RootElement, LawTarget.Law);
        Assert.Contains(hits, h => h.Name == "건축법" && h.SerialNo == "273437" && h.Kind == "법률");
        Assert.All(hits, h => Assert.NotEmpty(h.EffectiveDate));
    }

    [Fact]
    public void 자치법규_검색_응답을_파싱한다()
    {
        using var doc = Fixture("search_ordin.json");
        var hits = MolegClient.ParseSearch(doc.RootElement, LawTarget.Ordinance);
        Assert.Equal(3, hits.Count);
        Assert.Contains(hits, h => h.Name == "대전광역시 건축기본조례" && h.SerialNo == "1438889");
        Assert.All(hits, h => Assert.Equal("대전광역시", h.Department));
    }

    [Fact]
    public void 행정규칙_검색은_단건_객체_응답도_파싱한다()
    {
        // 행정규칙 검색은 항목 키가 "admrul"이고, 결과가 1건이면 배열이 아니라 객체로 온다.
        using var doc = Fixture("search_admrul.json");
        var hits = MolegClient.ParseSearch(doc.RootElement, LawTarget.AdminRule);
        var h = Assert.Single(hits);
        Assert.Equal("건축물의 에너지절약설계기준", h.Name);
        Assert.Equal("2100000282390", h.SerialNo);
        Assert.Equal("고시", h.Kind);
    }

    [Fact]
    public void 법령_본문에서_조문과_가지번호를_파싱한다()
    {
        using var doc = Fixture("svc_law.json");
        var law = MolegClient.ParseLawText(doc.RootElement);
        Assert.Equal("건축법", law.Name);
        Assert.NotEmpty(law.EffectiveDate);

        var art55 = law.FindArticle("55");
        Assert.NotNull(art55);
        Assert.Equal("건축물의 건폐율", art55!.Title);
        Assert.Contains("건폐율", art55.Body);

        Assert.NotNull(law.FindArticle("48의2"));   // 가지번호 조문
        // 항/호/목 중첩이 본문에 합쳐진다 (제2조 정의 — 호·목 포함).
        var art2 = law.FindArticle("2");
        Assert.Contains("도로법", art2!.Body);
    }

    [Fact]
    public void 자치법규_본문의_조_배열을_파싱한다()
    {
        // 자치법규는 기본정보 키가 "자치법규기본정보", 조문이 "조문"."조" 배열,
        // 조문번호가 6자리 문자열 배열(["000400","000400"])로 온다.
        using var doc = Fixture("svc_ordin.json");
        var law = MolegClient.ParseLawText(doc.RootElement);
        Assert.Equal("대전광역시 건축기본조례", law.Name);
        Assert.Equal("20191227", law.EffectiveDate);
        Assert.Equal(9, law.Articles.Count);

        var art4 = law.FindArticle("4");
        Assert.NotNull(art4);
        Assert.Equal("건축정책위원회", art4!.Title);
        Assert.Contains("건축정책위원회", art4.Body);
        // 제목 키워드 탐색(조례 필수 경로)이 실데이터에서 동작해야 한다.
        Assert.NotEmpty(law.FindArticlesByTitle("위원회"));
    }

    [Fact]
    public void 행정규칙_본문의_평문_조문내용을_조문으로_분리한다()
    {
        // 행정규칙 본문은 구조 없는 문자열 배열("조문내용")이다.
        using var doc = Fixture("svc_admrul.json");
        var law = MolegClient.ParseLawText(doc.RootElement);
        Assert.Equal("건축물의 에너지절약설계기준", law.Name);
        Assert.Equal("20260708", law.EffectiveDate);

        var art1 = law.FindArticle("1");
        Assert.NotNull(art1);
        Assert.Equal("목적", art1!.Title);

        var art2 = law.FindArticle("2");
        Assert.Equal("건축물의 열손실방지 등", art2!.Title);
        Assert.Contains("열관류율", art2.Body);
        // "제1장 총칙" 같은 장 제목은 조문으로 잡히면 안 된다.
        Assert.DoesNotContain(law.Articles, a => a.Body.StartsWith("제1장"));
    }

    [Fact]
    public void 법령_본문의_별표_목록을_파싱한다()
    {
        // 별표 검색(licbyl)에 없는 법령(건축법 시행령)도 본문 응답의 별표 섹션에서 조회된다.
        using var doc = Fixture("svc_law_annex.json");
        var law = MolegClient.ParseLawText(doc.RootElement);
        Assert.Equal("건축법 시행령", law.Name);
        Assert.NotNull(law.Annexes);

        var annex1 = law.FindAnnex("별표 1");
        Assert.NotNull(annex1);
        Assert.Contains("용도별 건축물의 종류", annex1!.Title);
        Assert.StartsWith("https://www.law.go.kr/", annex1.Link);
    }

    [Fact]
    public void 별표_검색_응답을_파싱한다()
    {
        using var doc = Fixture("search_licbyl.json");
        var hits = MolegClient.ParseAnnexSearch(doc.RootElement);
        Assert.NotEmpty(hits);
        // 6자리 별표번호 "001200" → "12"
        Assert.Contains(hits, a =>
            a.LawName == "건축물의 구조기준 등에 관한 규칙" && a.Number == "12" && a.Name.Contains("내진등급"));
    }

    [Theory]
    [InlineData("0012", "00", "12")]
    [InlineData("0001", "02", "1의2")]
    [InlineData("0001", null, "1")]
    public void 별표_조문_번호의_가지번호를_조립한다(string main, string? branch, string expected) =>
        Assert.Equal(expected, MolegClient.FormatBranchedNumber(main, branch));

    [Theory]
    [InlineData("48의2", "제48조의2")]   // 가지번호는 "제48의2조"가 아니라 "제48조의2"로 표기해야 한다
    [InlineData("42", "제42조")]
    [InlineData("-", "")]                // 별표 인용은 조번호 없음
    public void 검토서의_조문번호_표기(string no, string expected) =>
        Assert.Equal(expected, LawReview.Core.Report.DocxReportBuilder.FormatArticleRef(no));

    [Fact]
    public void 행정규칙_본문_조회는_ID_파라미터를_쓴다()
    {
        var client = new MolegClient(new HttpClient(), "testkey");
        Assert.Contains("ID=2100000282390",
            client.BuildServiceUri("2100000282390", LawTarget.AdminRule).ToString());
        Assert.Contains("MST=273437", client.BuildServiceUri("273437", LawTarget.Law).ToString());
        // 별표 검색은 법령명 검색(search=2)이어야 한다 — 기본값(별표명 검색)은 법령명으로 0건이 나온다.
        Assert.Contains("search=2", client.BuildAnnexSearchUri("건축법 시행령").ToString());
    }
}
