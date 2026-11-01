using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using LawReview.Core.LawApi;
using UglyToad.PdfPig;

namespace LawReview.Core.Municipal;

/// <summary>고시문 첨부 파일의 형식.</summary>
public enum NoticeFormat
{
    Unknown,
    Pdf,
    /// <summary>한글 2014 이후 형식 (ZIP + XML).</summary>
    Hwpx,
    /// <summary>한글 5.0 바이너리 형식 (CFB).</summary>
    Hwp,
}

/// <summary>
/// 지구단위계획 **고시문 첨부 파일에서 본문 텍스트를 뽑는다.**
///
/// 지금까지는 고시문을 링크로만 인용했다. 그런데 결정조서·지침에는 그 구역의 용도·건폐율·용적률·
/// 높이가 들어 있고, 적용 우선순위가 **지구단위계획 &gt; 조례 &gt; 시행령**(원칙 6)이라 검토에
/// 가장 먼저 봐야 하는 값이다. 링크만 걸면 사람이 파일을 따로 열어야 한다.
///
/// 지자체마다 형식이 다르다 — 서울은 PDF, 안양은 최근 HWPX·예전 HWP. 셋을 모두 읽는다.
///
/// **값을 해석하지는 않는다.** 지침은 도면(지침도)과 함께 읽어야 하고 구역마다 표기가 달라
/// 코드가 숫자를 집어내면 틀린다. 본문을 인용해 사람이 판단하게 한다(원칙 5).
/// </summary>
public static class NoticeTextExtractor
{
    public static NoticeFormat Detect(ReadOnlySpan<byte> file)
    {
        if (file.Length < 8) return NoticeFormat.Unknown;
        if (file[0] == '%' && file[1] == 'P' && file[2] == 'D' && file[3] == 'F') return NoticeFormat.Pdf;
        // HWPX·HWPZ는 ZIP이다. HWP 5.0은 CFB라 HwpTextExtractor가 가린다.
        if (file[0] == 'P' && file[1] == 'K') return NoticeFormat.Hwpx;
        return HwpTextExtractor.LooksLikeHwp(file) ? NoticeFormat.Hwp : NoticeFormat.Unknown;
    }

    /// <summary>본문을 줄 단위로 뽑는다. 형식을 모르거나 깨진 파일이면 빈 목록.</summary>
    public static IReadOnlyList<string> ExtractLines(byte[] file)
    {
        try
        {
            return Detect(file) switch
            {
                NoticeFormat.Pdf => ExtractPdf(file),
                NoticeFormat.Hwpx => ExtractHwpx(file),
                NoticeFormat.Hwp => HwpTextExtractor.ExtractLines(file),
                _ => Array.Empty<string>(),
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // 고시문은 서식이 제각각이라 깨진 파일이 섞인다 — 검토 전체를 멈출 이유는 없다.
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> ExtractPdf(byte[] file)
    {
        var lines = new List<string>();
        using var doc = PdfDocument.Open(new MemoryStream(file));
        foreach (var page in doc.GetPages())
            foreach (var raw in page.Text.Split('\n'))
                AddLine(lines, raw);
        return lines;
    }

    /// <summary>
    /// HWPX는 ZIP 안의 Contents/sectionN.xml에 본문이 있고 글자는 &lt;hp:t&gt; 요소에 담긴다.
    /// 문단(&lt;hp:p&gt;) 하나가 한 줄이다 — 글자 요소가 서식 때문에 여러 개로 쪼개져 있으므로
    /// 문단 단위로 이어 붙여야 "준주거지역"이 "준주", "거지역"으로 갈라지지 않는다.
    /// </summary>
    private static IReadOnlyList<string> ExtractHwpx(byte[] file)
    {
        var lines = new List<string>();
        using var zip = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);

        foreach (var entry in zip.Entries
                     .Where(e => e.FullName.StartsWith("Contents/section", StringComparison.OrdinalIgnoreCase)
                                 && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
        {
            using var stream = entry.Open();
            var root = XDocument.Load(stream).Root;
            if (root is null) continue;

            foreach (var paragraph in root.Descendants().Where(e => e.Name.LocalName == "p"))
            {
                var sb = new StringBuilder();
                foreach (var t in paragraph.Descendants().Where(e => e.Name.LocalName == "t"))
                    sb.Append(t.Value);
                AddLine(lines, sb.ToString());
            }
        }
        return lines;
    }

    private static void AddLine(List<string> lines, string raw)
    {
        var line = raw.Replace('\r', ' ').Replace('\t', ' ').Trim();
        if (line.Length > 0) lines.Add(line);
    }

    /// <summary>
    /// 결정조서·지침에서 검토에 쓰이는 줄만 추려 인용문을 만든다.
    ///
    /// 고시문 전체는 수백 줄이고 대부분이 행정 문구다. 용도·건폐율·용적률·높이처럼
    /// **설계개요에 바로 들어가는 값**이 있는 줄만 남겨 검토서에 싣는다.
    /// </summary>
    public static string Excerpt(IReadOnlyList<string> lines, int maxLines = 40)
    {
        var picked = lines.Where(IsUseful).Take(maxLines).ToList();
        if (picked.Count == 0) return "";

        var text = string.Join("\n", picked);
        return picked.Count >= maxLines ? $"{text}\n…(이하 생략 — 고시문 원문 확인)" : text;
    }

    private static readonly string[] UsefulWords =
    {
        "건폐율", "용적률", "높이", "허용용도", "불허용도", "권장용도", "지정용도",
        "획지", "가구", "대지면적", "건축한계선", "건축지정선", "벽면한계선",
        "구역면적", "면적(㎡)", "용도지역", "주차", "조경",
    };

    private static bool IsUseful(string line) =>
        line.Length is > 3 and < 300 && UsefulWords.Any(w => line.Contains(w, StringComparison.Ordinal));
}
