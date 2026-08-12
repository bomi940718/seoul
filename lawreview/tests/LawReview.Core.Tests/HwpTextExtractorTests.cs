using LawReview.Core.LawApi;
using LawReview.Core.Review;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 조례 별표(HWP) 파싱 검증. 픽스처는 실제 대전광역시 주차장 조례 별표 파일이다
/// (법제처 flDownload로 받은 원본). 파서가 깨지면 주차 기준이 조용히 틀린 값으로
/// 떨어지므로 실파일로 고정해 둔다.
/// </summary>
public class HwpTextExtractorTests
{
    private static byte[] AnnexFile() => File.ReadAllBytes(
        ChecklistTests.FindRepoFile(Path.Combine(
            "tests", "LawReview.Core.Tests", "Fixtures", "daejeon_parking_annex.hwp")));

    [Fact]
    public void HWP_본문_텍스트를_뽑는다()
    {
        var lines = HwpTextExtractor.ExtractLines(AnnexFile());

        Assert.NotEmpty(lines);
        Assert.Contains(lines, l => l.Contains("별표"));
        Assert.Contains(lines, l => l.Contains("시설면적") && l.Contains("㎡당"));
    }

    [Fact]
    public void HWP가_아니면_빈_결과를_돌려준다()
    {
        Assert.Empty(HwpTextExtractor.ExtractLines(new byte[] { 1, 2, 3, 4 }));
        Assert.Empty(HwpTextExtractor.ExtractLines(Array.Empty<byte>()));
    }

    [Fact]
    public void 조례_별표에서_용도별_주차기준을_찾는다()
    {
        var lines = HwpTextExtractor.ExtractLines(AnnexFile());

        var r = ParkingStandardResolver.ResolveFromOrdinanceAnnex(
            lines, "공장", "대전광역시 주차장 조례", "https://example/annex.hwp");
        // 근린생활시설처럼 조례가 따로 적는 용도도 읽혀야 한다
        var neighborhood = ParkingStandardResolver.ResolveFromOrdinanceAnnex(
            lines, "제2종 근린생활시설", "대전광역시 주차장 조례", "");
        Assert.Equal(134, neighborhood.AreaPerSpace);

        // 실무 검토서(둔곡)의 기준과 일치해야 한다 — 시행령(350㎡)이 아니라 조례(200㎡)
        Assert.Equal(200, r.AreaPerSpace);
        Assert.Equal("대전광역시 주차장 조례 [별표]", r.Basis);
        Assert.Contains("공장", r.MatchedUse);
        // 단서도 놓치면 안 된다
        Assert.Contains("산업단지", r.Note ?? "");
    }

    [Theory]
    [InlineData("위락시설", 67)]
    [InlineData("창고시설", 400)]
    public void 조례_별표의_다른_용도도_읽는다(string use, double expected)
    {
        var lines = HwpTextExtractor.ExtractLines(AnnexFile());
        var r = ParkingStandardResolver.ResolveFromOrdinanceAnnex(
            lines, use, "대전광역시 주차장 조례", "");
        Assert.Equal(expected, r.AreaPerSpace);
    }
}
