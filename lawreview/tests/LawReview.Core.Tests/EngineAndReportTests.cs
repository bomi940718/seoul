using DocumentFormat.OpenXml.Packaging;
using LawReview.Core.Ai;
using LawReview.Core.LawApi;
using LawReview.Core.Models;
using LawReview.Core.Report;
using LawReview.Core.Review;
using Xunit;

namespace LawReview.Core.Tests;

public class ChecklistTests
{
    [Fact]
    public void 표준_체크리스트가_로드된다()
    {
        var path = FindRepoFile(Path.Combine("src", "LawReview.Core", "checklists", "standard.json"));
        var items = ChecklistLoader.Load(path);

        Assert.True(items.Count >= 15);
        Assert.Contains(items, i => i.Id == "coverage_ratio" && i.Judgment == JudgmentType.Quantitative);
        Assert.Contains(items, i => i.Id == "landscaping" && i.Judgment == JudgmentType.Ai);
        Assert.Contains(items, i => i.Id == "district_unit_plan" && i.Judgment == JudgmentType.Manual);
        // 조례 참조는 지자체 자리표시자를 써야 한다 — 특정 도시 이름이 하드코딩되면 안 된다.
        var ordinances = items.SelectMany(i => i.Basis).Where(b => b.Target == LawTarget.Ordinance);
        Assert.All(ordinances, b => Assert.Contains("{시}", b.LawName));
    }

    [Fact]
    public void 조례명의_지자체_자리표시자가_치환된다()
    {
        var p = new ProjectInput { Province = "대전광역시", City = "유성구" };
        Assert.Equal("대전광역시 주차장 조례", ReviewEngine.ResolvePlaceholders("{시} 주차장 조례", p));
        Assert.Equal("유성구 건축 조례", ReviewEngine.ResolvePlaceholders("{구} 건축 조례", p));
    }

    internal static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent!;
        }
        throw new FileNotFoundException(relative);
    }
}

public class MolegClientTests
{
    [Fact]
    public void 요청_URI가_올바르게_구성된다()
    {
        var client = new MolegClient(new HttpClient(), "testkey");
        var search = client.BuildSearchUri("건축법", LawTarget.Law).ToString();
        Assert.Contains("lawSearch.do", search);
        Assert.Contains("OC=testkey", search);
        Assert.Contains("target=law", search);

        var ordin = client.BuildSearchUri("대전광역시 주차장 조례", LawTarget.Ordinance).ToString();
        Assert.Contains("target=ordin", ordin);

        var service = client.BuildServiceUri("12345", LawTarget.Law).ToString();
        Assert.Contains("lawService.do", service);
        Assert.Contains("MST=12345", service);
    }
}

public class JudgmentParsingTests
{
    [Theory]
    [InlineData("{\"판정\":\"적용\",\"사유\":\"공장이므로 적용\"}", Applicability.적용)]
    [InlineData("{\"판정\":\"해당없음\",\"사유\":\"산업단지 공장 면제\"}", Applicability.해당없음)]
    [InlineData("판단 결과입니다: {\"판정\":\"확인필요\",\"사유\":\"조문만으로 불명확\"}", Applicability.확인필요)]
    public void 판정_JSON_응답을_해석한다(string text, Applicability expected)
    {
        var j = ClaudeJudgmentProvider.ParseJudgment(text);
        Assert.Equal(expected, j.Applicability);
        Assert.NotEmpty(j.Reason);
    }

    [Fact]
    public void 해석_불가능한_응답은_확인필요로_처리한다() =>
        Assert.Equal(Applicability.확인필요, ClaudeJudgmentProvider.ParseJudgment("자유 텍스트 답변").Applicability);
}

public class DocxReportTests
{
    [Fact]
    public void 검토서_docx가_생성되고_열린다()
    {
        var result = new ReviewResult
        {
            Project = new ProjectInput
            {
                ProjectName = "테스트 공장",
                Client = "테스트 건축주",
                SiteAddress = "대전광역시 유성구 둔곡동 407-5",
                Province = "대전광역시",
                City = "유성구",
                UseZones = { "도시지역", "일반공업지역" },
                SiteArea = 6030.10,
                PrimaryUse = "공장",
                PlannedBuildingArea = 1453.22,
                PlannedFloorsAbove = 2,
                Zoning = new ZoningLimits { MaxCoverageRatio = 70, MaxFloorAreaRatio = 350, Source = "지구단위계획" },
                Parking = new ParkingRule { Source = "대전광역시 주차장 조례 제16조" },
                Buildings =
                {
                    new BuildingArea
                    {
                        Name = "A동",
                        Floors = { new FloorArea { FloorLabel = "1층", Use = "공장", ExclusiveArea = 1000 } },
                    },
                },
            },
        };
        result.Overview = QuantitativeCalculator.Calculate(result.Project);
        result.ReviewedLaws["건축법"] = "20230611";
        result.Rows.Add(new ReviewRow
        {
            Item = new ChecklistItem { Id = "landscaping", Title = "대지 안의 조경", Section = "제4장", InSummary = true, Judgment = JudgmentType.Ai },
            Applicability = Applicability.해당없음,
            Reason = "산업단지 내 공장 — 건축법 시행령 27조 1항 4호 면제",
            Citations = { new CitedArticle("건축법", "42", "대지의 조경", "① 면적이 200제곱미터 이상인 대지에…", "20230611") },
        });

        var path = Path.Combine(Path.GetTempPath(), $"lawreview_test_{Guid.NewGuid():N}.docx");
        try
        {
            new DocxReportBuilder().Build(result, path);
            Assert.True(new FileInfo(path).Length > 1000);

            using var doc = WordprocessingDocument.Open(path, false);
            var text = doc.MainDocumentPart!.Document.Body!.InnerText;
            Assert.Contains("법규검토서", text);
            Assert.Contains("설계개요", text);
            Assert.Contains("검토법규", text);
            Assert.Contains("대지 안의 조경", text);
            Assert.Contains("해당없음", text);
            Assert.Contains("4,221.07", text);   // 법정 건축면적 산정식
        }
        finally
        {
            File.Delete(path);
        }
    }
}
