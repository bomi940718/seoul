using LawReview.Core.Municipal;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 고시문 첨부에서 결정조서 본문을 뽑는지 검증.
///
/// 픽스처 anyang_notice_body.hwpx는 실제 고시문(안양시 고시 제2026-135호, 호계동 범내지구)에서
/// 본문 XML만 남긴 것이다 — 원본은 도면 이미지 때문에 4MB다. 파서가 보는 것은 Contents/section*.xml뿐이라
/// 추출 결과는 원본과 같다.
///
/// 지구단위계획은 적용 우선순위가 가장 높아(조례·시행령보다 먼저) 본문을 못 읽으면
/// 검토에서 가장 중요한 값이 빠진다. 형식 판별이 조용히 실패하지 않도록 실파일로 고정한다.
/// </summary>
public class NoticeTextExtractorTests
{
    private static byte[] Notice() => File.ReadAllBytes(ChecklistTests.FindRepoFile(
        Path.Combine("tests", "LawReview.Core.Tests", "Fixtures", "anyang_notice_body.hwpx")));

    [Fact]
    public void HWPX_본문을_줄_단위로_읽는다()
    {
        var lines = NoticeTextExtractor.ExtractLines(Notice());

        Assert.Equal(NoticeFormat.Hwpx, NoticeTextExtractor.Detect(Notice()));
        Assert.True(lines.Count > 100, $"본문 줄 수가 너무 적다: {lines.Count}");
        Assert.Contains(lines, l => l.Contains("호계동 범내지구"));
    }

    [Fact]
    public void 문단_안에서_쪼개진_글자를_이어_붙인다()
    {
        // HWPX는 서식 때문에 한 문단의 글자가 여러 <hp:t>로 갈린다.
        // 이어 붙이지 않으면 "제3종일반주거지역"이 "제3종", "일반주거지역"으로 흩어진다.
        var lines = NoticeTextExtractor.ExtractLines(Notice());
        Assert.Contains(lines, l => l.Contains("제3종일반주거지역"));
    }

    [Fact]
    public void 결정조서에서_검토에_쓰이는_줄만_발췌한다()
    {
        var excerpt = NoticeTextExtractor.Excerpt(NoticeTextExtractor.ExtractLines(Notice()));

        Assert.Contains("용도지역", excerpt);
        Assert.Contains("면적", excerpt);
        // 전문이 아니라 발췌다 — 고시문은 수백 줄이고 대부분 행정 문구다.
        Assert.True(excerpt.Split('\n').Length <= 41);
    }

    [Theory]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 }, NoticeFormat.Pdf)]   // %PDF-1.4
    [InlineData(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0 }, NoticeFormat.Hwpx)]              // ZIP
    // CFB 서명 뒤에 내용이 있어야 한다(HwpTextExtractor가 8바이트 초과를 요구한다)
    [InlineData(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0x00 }, NoticeFormat.Hwp)]
    [InlineData(new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C, 0x3E, 0, 0 }, NoticeFormat.Unknown)]     // <html>
    public void 매직넘버로_형식을_가린다(byte[] head, NoticeFormat expected)
        => Assert.Equal(expected, NoticeTextExtractor.Detect(head));

    [Fact]
    public void 깨진_파일이어도_예외를_던지지_않는다()
    {
        // 고시문은 서식이 제각각이라 깨진 첨부가 섞인다 — 검토 전체를 멈출 이유는 없다.
        Assert.Empty(NoticeTextExtractor.ExtractLines(new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4 }));
        Assert.Empty(NoticeTextExtractor.ExtractLines(Array.Empty<byte>()));
        Assert.Empty(NoticeTextExtractor.ExtractLines(new byte[] { 0x25, 0x50, 0x44, 0x46, 9, 9, 9, 9 }));
    }

    [Fact]
    public void 쓸만한_줄이_없으면_발췌가_비어_있다()
    {
        // 스캔 이미지 PDF가 이 경우다 — 값을 지어내지 않고 호출부가 안내 문구를 남긴다.
        Assert.Equal("", NoticeTextExtractor.Excerpt(new[] { "안양시 고시 제2026-135호", "아래와 같이 고시합니다." }));
    }
}
