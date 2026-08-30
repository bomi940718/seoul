using LawReview.Core.LawApi;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 별표 본문 처리 검증.
///
/// 법령 별표는 본문이 API에 함께 오는데, 예전에는 인용에 링크만 남겼다. 그러면 판정하는 쪽이
/// 정작 표를 못 보고 조문 제목만으로 판단하게 된다(장애인편의 별표 1·2가 대표적).
/// 그렇다고 통째로 넣으면 별표 하나가 수만 자라 프롬프트가 감당하지 못하므로 **용도로 발췌**한다.
///
/// 픽스처 access_decree_annex1.txt는 편의증진법 시행령 별표 1(편의시설 설치 대상시설) 실원문이다.
/// </summary>
public class AnnexTextTests
{
    private static string AccessibilityAnnex1() => File.ReadAllText(ChecklistTests.FindRepoFile(
        Path.Combine("tests", "LawReview.Core.Tests", "Fixtures", "access_decree_annex1.txt")));

    [Fact]
    public void 짧은_별표는_그대로_인용한다()
    {
        const string body = "■ 주차장법 시행령 [별표 1]\n1. 위락시설 | 시설면적 100㎡당 1대";
        Assert.Equal(body, AnnexText.ExcerptForUse(body, "위락시설"));
    }

    [Fact]
    public void 긴_별표는_해당_용도_항목만_발췌한다()
    {
        var excerpt = AnnexText.ExcerptForUse(AccessibilityAnnex1(), "공장", maxChars: 4000);

        Assert.True(excerpt.Length < AccessibilityAnnex1().Length);
        Assert.Contains("파. 공장", excerpt);
        Assert.Contains("「장애인고용촉진 및 직업재활법」", excerpt);   // 공장 항목의 조건 본문
        Assert.Contains("발췌", excerpt);                          // 발췌했다는 사실을 밝힌다
        Assert.Contains("편의시설 설치 대상시설", excerpt);         // 별표 제목은 남긴다
        Assert.DoesNotContain("교정시설", excerpt);                 // 상관없는 항목은 빠진다
    }

    [Fact]
    public void 용도를_못_찾으면_앞부분을_자르고_생략을_밝힌다()
    {
        var excerpt = AnnexText.ExcerptForUse(AccessibilityAnnex1(), "존재하지않는용도", maxChars: 1000);

        Assert.Contains("이하 생략", excerpt);
        Assert.Contains("편의시설 설치 대상시설", excerpt);
    }

    [Fact]
    public void 용도가_비어_있어도_깨지지_않는다()
    {
        Assert.Contains("이하 생략", AnnexText.ExcerptForUse(AccessibilityAnnex1(), null, maxChars: 500));
        Assert.Equal("", AnnexText.ExcerptForUse("", "공장"));
    }

    // ── 조례 별표 묶음 자르기 ────────────────────────────────────────

    private static readonly string[] Bundle =
    {
        "[별표 1]",
        "건축위원회 구성",
        "위원 15명",
        "[별표 3] <개정 2022.4.15.>",
        "대지 안의 공지 기준(제40조 관련)",
        "1. 건축선으로부터 건축물까지 띄어야 하는 거리",
        "[별표 4]",
        "과태료 부과기준",
    };

    [Fact]
    public void 제목으로_별표_하나만_잘라낸다()
    {
        var section = AnnexText.Section(Bundle, "공지 기준");

        Assert.Equal("[별표 3] <개정 2022.4.15.>", section[0]);   // 머리글부터 포함
        Assert.Contains(section, l => l.Contains("건축선으로부터"));
        Assert.DoesNotContain(section, l => l.Contains("과태료"));
        Assert.DoesNotContain(section, l => l.Contains("건축위원회"));
    }

    [Fact]
    public void 제목이_없으면_빈_결과다()
    {
        Assert.Empty(AnnexText.Section(Bundle, "부설주차장의 설치기준"));
    }
}
