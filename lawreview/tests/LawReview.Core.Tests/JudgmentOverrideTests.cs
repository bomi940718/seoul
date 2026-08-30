using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LawReview.Core.Models;
using LawReview.Core.Report;
using LawReview.Core.Review;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 사람이 고친 판정이 검토서까지 그대로 나가는지 지킨다.
/// AI 판정은 틀릴 수 있고 문서에 서명하는 것은 사람이므로, 이 경로가 끊기면 도구를 믿을 수 없다.
/// </summary>
public class JudgmentOverrideTests
{
    private static ChecklistItem Item(string id, string section = "요약", bool inSummary = true) =>
        new() { Id = id, Title = id, Section = section, InSummary = inSummary };

    private static ReviewResult Sample()
    {
        var r = new ReviewResult { Project = new ProjectInput { ProjectName = "테스트", SiteArea = 1000 } };
        r.Rows.Add(new ReviewRow
        {
            Item = Item("landscaping"),
            Applicability = Applicability.해당없음,
            Reason = "AI가 판단한 사유",
            CriterionText = "대지면적의 5% 이상",
            Citations = { new CitedArticle("건축법", "42", "대지의 조경", "본문", "20240101") },
        });
        r.Rows.Add(new ReviewRow { Item = Item("parking"), Applicability = Applicability.적용, Reason = "그대로" });
        return r;
    }

    [Fact]
    public void 사람이_고친_판정과_사유가_반영된다()
    {
        var src = Sample();
        var edited = JudgmentOverrides.Apply(src, new[]
        {
            new JudgmentOverride { Id = "landscaping", Verdict = "적용", Reason = "사람이 고친 사유" },
        });

        var row = edited.Rows.Single(x => x.Item.Id == "landscaping");
        Assert.Equal(Applicability.적용, row.Applicability);
        Assert.Equal("사람이 고친 사유", row.Reason);
        // 고치지 않은 값과 인용 조문은 그대로 남아야 한다.
        Assert.Equal("대지면적의 5% 이상", row.CriterionText);
        Assert.Single(row.Citations);
        Assert.Equal("건축법", row.Citations[0].LawName);
    }

    [Fact]
    public void 원본_검토결과는_바뀌지_않는다()
    {
        var src = Sample();
        JudgmentOverrides.Apply(src, new[] { new JudgmentOverride { Id = "landscaping", Verdict = "적용" } });

        // 원본이 그대로여야 화면에서 "AI 판정으로 되돌리기"가 가능하다.
        var row = src.Rows.Single(x => x.Item.Id == "landscaping");
        Assert.Equal(Applicability.해당없음, row.Applicability);
        Assert.Equal("AI가 판단한 사유", row.Reason);
    }

    [Fact]
    public void 고치지_않은_항목은_건드리지_않는다()
    {
        var edited = JudgmentOverrides.Apply(Sample(),
            new[] { new JudgmentOverride { Id = "landscaping", Verdict = "적용" } });

        var untouched = edited.Rows.Single(x => x.Item.Id == "parking");
        Assert.Equal(Applicability.적용, untouched.Applicability);
        Assert.Equal("그대로", untouched.Reason);
    }

    [Theory]
    [InlineData("애매함")]
    [InlineData("")]
    [InlineData(null)]
    public void 판정_문자열이_셋_중_하나가_아니면_무시한다(string? verdict)
    {
        var edited = JudgmentOverrides.Apply(Sample(), new[]
        {
            new JudgmentOverride { Id = "landscaping", Verdict = verdict, Reason = "사유만 고침" },
        });

        var row = edited.Rows.Single(x => x.Item.Id == "landscaping");
        Assert.Equal(Applicability.해당없음, row.Applicability);   // 원래 판정 유지
        Assert.Equal("사유만 고침", row.Reason);                    // 사유는 반영
    }

    [Fact]
    public void 결과에_없는_항목_수정은_아무_영향이_없다()
    {
        var edited = JudgmentOverrides.Apply(Sample(),
            new[] { new JudgmentOverride { Id = "없는항목", Verdict = "적용" } });

        Assert.Equal(2, edited.Rows.Count);
        Assert.All(edited.Rows, r => Assert.NotEqual("없는항목", r.Item.Id));
    }

    [Fact]
    public void 수정_없이_저장하면_원본_그대로다()
    {
        var src = Sample();
        Assert.Same(src, JudgmentOverrides.Apply(src, null));
        Assert.Same(src, JudgmentOverrides.Apply(src, Array.Empty<JudgmentOverride>()));
    }

    [Fact]
    public void 검토서_요약표에_고친_판정이_찍힌다()
    {
        var edited = JudgmentOverrides.Apply(Sample(), new[]
        {
            new JudgmentOverride { Id = "landscaping", Verdict = "적용", Reason = "현장 확인 결과 적용 대상" },
        });

        var path = Path.Combine(Path.GetTempPath(), $"override_{Guid.NewGuid():N}.docx");
        try
        {
            new DocxReportBuilder().Build(edited, path);

            // 평문으로 훑으면 표의 행 경계를 넘어 옆 항목 텍스트를 집어와 없는 버그를 만든다 — 셀 단위로 읽는다.
            var cells = SummaryRowCells(path, "landscaping");
            Assert.Equal("적용", cells[^1]);
            Assert.Contains("현장 확인 결과 적용 대상", cells[3]);   // 산정식이 없으면 사유가 설계기준 칸에 온다
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void 산정식을_비우면_사유가_설계기준_칸으로_내려온다()
    {
        var src = Sample();
        src.Rows[0].CalculationText = "1,000.00 x 0.05 = 50.00 m²";
        var edited = JudgmentOverrides.Apply(src, new[]
        {
            new JudgmentOverride { Id = "landscaping", Calculation = "" },
        });

        var path = Path.Combine(Path.GetTempPath(), $"override_{Guid.NewGuid():N}.docx");
        try
        {
            new DocxReportBuilder().Build(edited, path);
            Assert.Contains("AI가 판단한 사유", SummaryRowCells(path, "landscaping")[3]);
        }
        finally { File.Delete(path); }
    }

    /// <summary>요약 검토표에서 항목 한 행의 셀들을 구조(w:tr/w:tc)로 읽는다.</summary>
    private static string[] SummaryRowCells(string path, string title)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var row = doc.MainDocumentPart!.Document.Body!
            .Descendants<TableRow>()
            .FirstOrDefault(tr => tr.Elements<TableCell>().FirstOrDefault()?.InnerText == title);
        Assert.NotNull(row);
        return row!.Elements<TableCell>().Select(c => c.InnerText).ToArray();
    }
}

/// <summary>기본 검토와 장별 상세를 따로 돌려도 검토서에는 둘 다 들어가야 한다.</summary>
public class ReviewResultMergeTests
{
    private static ReviewResult Result(string project, params (string Id, string Section, Applicability V)[] rows)
    {
        var r = new ReviewResult { Project = new ProjectInput { ProjectName = project } };
        foreach (var (id, section, v) in rows)
            r.Rows.Add(new ReviewRow
            {
                Item = new ChecklistItem { Id = id, Title = id, Section = section },
                Applicability = v,
            });
        return r;
    }

    [Fact]
    public void 기본검토와_상세검토가_합쳐진다()
    {
        var basic = Result("P", ("coverage_ratio", "요약", Applicability.적용));
        basic.ReviewedLaws["건축법"] = "20240101";
        var detail = Result("P", ("fire_resistance", "제5장 건축물의 구조 및 재료", Applicability.해당없음));
        detail.ReviewedLaws["건축법 시행령"] = "20240201";

        var merged = ReviewResults.Merge(new[] { basic, detail })!;

        Assert.Equal(2, merged.Rows.Count);
        Assert.Contains(merged.Rows, r => r.Item.Id == "coverage_ratio");
        Assert.Contains(merged.Rows, r => r.Item.Id == "fire_resistance");
        Assert.Equal(2, merged.ReviewedLaws.Count);
    }

    [Fact]
    public void 같은_항목은_나중_검토가_이긴다()
    {
        var first = Result("P", ("coverage_ratio", "요약", Applicability.확인필요));
        var second = Result("P", ("coverage_ratio", "요약", Applicability.적용));

        var merged = ReviewResults.Merge(new[] { first, second })!;

        Assert.Single(merged.Rows);
        Assert.Equal(Applicability.적용, merged.Rows[0].Applicability);
    }

    [Fact]
    public void 합친_뒤에도_체크리스트_순서를_따른다()
    {
        // 상세를 나중에 돌려도 검토서 순서(표준 서식)는 체크리스트 순서여야 한다.
        var detail = Result("P", ("landscaping", "제4장 건축물의 대지와 도로", Applicability.적용));
        var basic = Result("P", ("coverage_ratio", "요약", Applicability.적용));

        var merged = ReviewResults.Merge(new[] { detail, basic })!;

        var order = ChecklistLoader.LoadDefault().Select((i, n) => (i.Id, n)).ToDictionary(x => x.Id, x => x.n);
        var actual = merged.Rows.Select(r => order[r.Item.Id]).ToList();
        Assert.Equal(actual.OrderBy(x => x), actual);
    }

    [Fact]
    public void 검토가_없으면_null()
    {
        Assert.Null(ReviewResults.Merge(Array.Empty<ReviewResult>()));
    }
}
