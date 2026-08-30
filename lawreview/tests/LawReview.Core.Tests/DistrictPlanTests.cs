using System.Text.Json;
using LawReview.Core.Municipal;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 서울도시공간포털 지구단위계획 조회 검증.
/// Fixtures/urban_dstplan.json은 실 API(getDstplanList.json) 응답(2026-07 캡처)을
/// 파싱에 쓰는 필드만 남기고 축약한 것이다.
/// </summary>
public class DistrictPlanTests
{
    [Fact]
    public void 지구단위계획_목록_응답을_파싱한다()
    {
        var path = ChecklistTests.FindRepoFile(
            Path.Combine("tests", "LawReview.Core.Tests", "Fixtures", "urban_dstplan.json"));
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var records = SeoulUrbanPortalClient.ParseList(doc.RootElement);

        var r = Assert.Single(records);
        Assert.Equal("봉천지역중심 지구단위계획구역", r.ZoneName);
        Assert.Equal("관악구 봉천동 857-1번지 일대", r.Location);
        Assert.Equal("서울특별시", r.NoticeOrgan);
        Assert.Equal("제2026-290호", r.NoticeNo);
        Assert.Equal("2026-05-28", r.NoticeDate);
        Assert.Equal(593188, r.AreaAfter);
        // 고시문 PDF는 정적 경로 직접 다운로드 — 한글 파일명은 URL 인코딩되어야 한다.
        Assert.StartsWith("https://urban.seoul.go.kr/UpisArchive/DATA/PM/pdf/11620NTC202603250002/", r.NoticePdfUrl);
        Assert.DoesNotContain("고시.pdf", r.NoticePdfUrl);   // 인코딩 전 원문이 그대로 남으면 안 된다
        Assert.Contains("%", r.NoticePdfUrl);
    }

    [Theory]
    [InlineData("서울특별시 관악구 봉천동 857-1", "봉천동")]
    [InlineData("대전광역시 유성구 둔곡동 407-5", "둔곡동")]
    [InlineData("서울특별시 종로구 관철동 13-1번지 일대", "관철동")]
    [InlineData("도로명주소 세종대로 110", null)]   // 법정동을 못 찾으면 조회 생략
    public void 주소에서_검색_키워드를_뽑는다(string address, string? expected) =>
        Assert.Equal(expected, DistrictPlanProviders.KeywordFromAddress(address));

    [Fact]
    public async Task 서울_프로젝트의_지구단위계획_항목에_후보_구역이_인용된다()
    {
        var provider = new StubProvider();
        var engine = new LawReview.Core.Review.ReviewEngine(
            new LawReview.Core.LawApi.MolegClient(new HttpClient(), "test"),
            new NoJudge(), districtPlan: provider);
        var project = new LawReview.Core.Models.ProjectInput
        {
            Province = "서울특별시", City = "관악구",
            SiteAddress = "서울특별시 관악구 봉천동 857-1",
            SiteArea = 100, PlannedBuildingArea = 10,
        };
        var item = new LawReview.Core.Review.ChecklistItem
        {
            Id = "district_unit_plan", Title = "지구단위계획 지침", Section = "지구단위계획",
            Judgment = LawReview.Core.Review.JudgmentType.Manual, Note = "결정도서를 확인하세요.",
        };

        var result = await engine.RunAsync(project, new[] { item });

        var row = Assert.Single(result.Rows);
        Assert.Equal(LawReview.Core.Review.Applicability.확인필요, row.Applicability);   // 판정은 항상 확인필요 유지
        var cite = Assert.Single(row.Citations);
        Assert.Equal("봉천지역중심 지구단위계획구역", cite.Title);
        Assert.Contains("고시문 원문", cite.Body);
        Assert.Equal("봉천동", provider.LastKeyword);   // 주소에서 법정동 추출
    }

    [Fact]
    public async Task 직접_등록한_결정도서가_포털_자동조회보다_우선한다()
    {
        // 사용자가 해당 필지의 문서를 직접 지정했으면 포털 후보 목록은 노이즈가 되므로 조회하지 않는다.
        var provider = new StubProvider();
        var engine = new LawReview.Core.Review.ReviewEngine(
            new LawReview.Core.LawApi.MolegClient(new HttpClient(), "test"),
            new NoJudge(), districtPlan: provider);

        var file = Path.Combine(Path.GetTempPath(), $"dup_{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(file, "결정도서");
        try
        {
            var project = new LawReview.Core.Models.ProjectInput
            {
                Province = "서울특별시", City = "관악구",
                SiteAddress = "서울특별시 관악구 봉천동 857-1",
                SiteArea = 100, PlannedBuildingArea = 10,
                DistrictPlanFiles =
                {
                    new LawReview.Core.Models.DistrictPlanFile
                    {
                        FilePath = file, ZoneName = "봉천 제14 재개발 지구단위계획구역",
                        NoticeNo = "관악구 고시 제2024-44호", NoticeDate = "2024-04-18",
                    },
                },
            };
            var item = new LawReview.Core.Review.ChecklistItem
            {
                Id = "district_unit_plan", Title = "지구단위계획 지침",
                Judgment = LawReview.Core.Review.JudgmentType.Manual, Note = "확인하세요.",
            };

            var result = await engine.RunAsync(project, new[] { item });

            var row = Assert.Single(result.Rows);
            Assert.Null(provider.LastKeyword);                       // 포털 조회를 하지 않았다
            var cite = Assert.Single(row.Citations);
            Assert.Contains("직접 등록", cite.LawName);
            Assert.Equal("봉천 제14 재개발 지구단위계획구역", cite.Title);
            Assert.Contains("관악구 고시 제2024-44호", cite.Body);
            Assert.Contains(file, cite.Body);
            Assert.DoesNotContain("찾을 수 없습니다", cite.Body);
            Assert.Equal(LawReview.Core.Review.Applicability.확인필요, row.Applicability);  // 판정은 유지
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void 등록_파일이_없으면_경고를_남긴다()
    {
        var row = new LawReview.Core.Review.ReviewRow
        {
            Item = new LawReview.Core.Review.ChecklistItem { Id = "district_unit_plan", Title = "지구단위계획 지침" },
        };
        var project = new LawReview.Core.Models.ProjectInput
        {
            DistrictPlanFiles = { new LawReview.Core.Models.DistrictPlanFile { FilePath = @"C:\없는경로\고시문.pdf" } },
        };

        LawReview.Core.Review.ReviewEngine.AddUploadedDistrictPlanCitations(row, project);

        var cite = Assert.Single(row.Citations);
        Assert.Equal("고시문", cite.Title);              // 구역명 미입력 시 파일명으로 대체
        Assert.Contains("찾을 수 없습니다", cite.Body);
        Assert.Contains("파일 확인 필요", row.Reason);
    }

    private sealed class StubProvider : IDistrictPlanProvider
    {
        public string? LastKeyword;
        public string Province => "서울특별시";
        public Task<IReadOnlyList<DistrictPlanRecord>> SearchAsync(string keyword, CancellationToken ct = default)
        {
            LastKeyword = keyword;
            return Task.FromResult<IReadOnlyList<DistrictPlanRecord>>(new[]
            {
                new DistrictPlanRecord("봉천지역중심 지구단위계획구역", "관악구 봉천동 857-1번지 일대",
                    "서울특별시", "제2026-290호", "2026-05-28", "도시관리계획 결정(변경) 고시",
                    593188, "https://urban.seoul.go.kr/UpisArchive/x.pdf", SeoulUrbanPortalClient.PortalPageUrl),
            });
        }
        public Task<bool> DownloadNoticePdfAsync(DistrictPlanRecord r, string p, CancellationToken ct = default) =>
            Task.FromResult(false);
    }

    private sealed class NoJudge : LawReview.Core.Ai.IJudgmentProvider
    {
        public Task<LawReview.Core.Ai.Judgment> JudgeAsync(LawReview.Core.Review.ChecklistItem item,
            IReadOnlyList<LawReview.Core.Review.CitedArticle> articles,
            LawReview.Core.Models.ProjectInput project, CancellationToken ct = default) =>
            Task.FromResult(new LawReview.Core.Ai.Judgment(LawReview.Core.Review.Applicability.확인필요, "-"));
    }

    [Fact]
    public void 시별_제공자_등록()
    {
        using var http = new HttpClient();
        Assert.NotNull(DistrictPlanProviders.For("서울특별시", http));
        Assert.NotNull(DistrictPlanProviders.For("서울시", http));
        // 아직 미구현 지자체는 null — 검토 항목은 기존 수동 안내 문구를 유지한다.
        Assert.Null(DistrictPlanProviders.For("대전광역시", http));
    }
}
