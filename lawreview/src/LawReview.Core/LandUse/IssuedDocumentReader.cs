using System.Globalization;
using System.Text.RegularExpressions;
using LawReview.Core.Municipal;
using LawReview.Core.Review;

namespace LawReview.Core.LandUse;

/// <summary>
/// 발급·열람한 서류 PDF(건축물대장 · 토지대장 · 토지이용계획확인원)에서 대조할 항목을 읽는다.
///
/// 왜 PDF인가: 공개 API(건축HUB·VWorld)는 **소유자 성명·주민번호 앞자리, 위반건축물 표기, 지목(건축물대장)** 을
/// 주지 않는다 — 건축HUB 활용가이드(10개 오퍼레이션) 어디에도 해당 항목이 없다(2026-11-16 확인).
/// 이 항목은 정부24·세움터에서 발급한 문서에만 찍힌다. 그래서 사용자가 받은 PDF를 읽는다.
///
/// 서식이 기관·시기마다 조금씩 달라 표 구조에 기대지 않고, 글자 흐름에서 패턴으로 찾는다.
/// 글자가 없는 PDF(스캔 이미지)는 HasText=false로 돌려준다 — 그때는 사람이 본다.
/// </summary>
public static class IssuedDocumentReader
{
    public static IssuedDocInfo Read(IssuedDocKind kind, byte[] pdf)
    {
        IReadOnlyList<string> lines;
        try { lines = NoticeTextExtractor.ExtractLines(pdf); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { return new IssuedDocInfo(kind, false, new(), null, null, null, new(), 0, "PDF를 열 수 없습니다: " + ex.Message); }
        return Parse(kind, lines);
    }

    internal static IssuedDocInfo Parse(IssuedDocKind kind, IReadOnlyList<string> lines)
    {
        var text = string.Join(" ", lines.Select(l => l.Trim()).Where(l => l.Length > 0));
        var hasText = Regex.IsMatch(text, "[가-힣]{4,}");
        if (!hasText)
            return new IssuedDocInfo(kind, false, new(), null, null, null, new(), lines.Count,
                "글자를 읽을 수 없는 PDF입니다(스캔 이미지). 정부24에서 'PDF 저장'으로 받은 파일을 넣어 주세요.");

        var owners = kind == IssuedDocKind.LandUsePlan ? new List<DocOwner>() : FindOwners(text);
        bool? violation = kind == IssuedDocKind.BuildingRegister ? Norm(text).Contains("위반건축물") : null;
        var zones = ZoneUseResolver.UseZones.Where(z => Norm(text).Contains(z)).ToList();

        return new IssuedDocInfo(kind, true, owners, violation, FindCategory(text), FindArea(text), zones, lines.Count,
            text.Length > 600 ? text[..600] : text);
    }

    // ── 소유자 ───────────────────────────────────────────────────
    // "홍길동 800101-1******", "주식회사 ○○ 110111-0******" 처럼 성명 뒤에 주민(법인)등록번호가 온다.
    private static readonly Regex OwnerRx = new(
        @"(?<name>(?:(?:주식회사|유한회사|\(주\)|㈜)\s*)?[가-힣A-Za-z]{2,20}(?:\s*(?:주식회사|\(주\)|㈜))?)\s*[\(（]?\s*(?<id>\d{6})\s*[-－]\s*[\d\*＊]{1,7}");

    // 이름 자리에 잡히면 안 되는 서식 낱말
    private static readonly string[] NotNames =
        { "성명", "명칭", "주민등록번호", "등록번호", "소유자", "주소", "지분", "변동일", "변동원인", "소유권", "공유", "외", "번호" };

    internal static List<DocOwner> FindOwners(string text)
    {
        var result = new List<DocOwner>();
        foreach (Match m in OwnerRx.Matches(text))
        {
            var name = Regex.Replace(m.Groups["name"].Value, @"\s+", " ").Trim();
            foreach (var w in NotNames) name = Regex.Replace(name, $"^{w}\\s*", "");
            if (name.Length < 2 || NotNames.Contains(name)) continue;
            var id = m.Groups["id"].Value;
            if (!result.Any(o => o.Name == name && o.IdFront == id)) result.Add(new DocOwner(name, id));
        }
        return result;
    }

    // ── 지목 · 면적 ──────────────────────────────────────────────
    private static readonly string[] Categories =
    {
        "전", "답", "과수원", "목장용지", "임야", "광천지", "염전", "대", "공장용지", "학교용지", "주차장", "주유소용지",
        "창고용지", "도로", "철도용지", "제방", "하천", "구거", "유지", "양어장", "수도용지", "공원", "체육용지",
        "유원지", "종교용지", "사적지", "묘지", "잡종지",
    };

    internal static string? FindCategory(string text)
    {
        // 토지대장: "(08) 대" / 토지이용계획확인원: "지목 대"
        var m = Regex.Match(text, @"\(\s*\d{2}\s*\)\s*(?<c>[가-힣]{1,5})");
        if (m.Success && Categories.Contains(m.Groups["c"].Value)) return m.Groups["c"].Value;
        m = Regex.Match(text, @"지\s*목\s*[:：]?\s*(?<c>[가-힣]{1,5})");
        if (m.Success && Categories.Contains(m.Groups["c"].Value)) return m.Groups["c"].Value;
        return null;
    }

    internal static double? FindArea(string text)
    {
        // "면적 167.1㎡" / "대지면적 167.1 ㎡"
        var m = Regex.Match(text, @"면\s*적\s*[:：]?\s*(?:\(㎡\))?\s*(?<a>\d{1,3}(?:,\d{3})*(?:\.\d+)?)\s*(?:㎡|m²|m2)?");
        return m.Success && double.TryParse(m.Groups["a"].Value.Replace(",", ""), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var a) && a > 0 ? a : null;
    }

    private static string Norm(string s) => Regex.Replace(s, @"\s", "");
}

public enum IssuedDocKind { BuildingRegister, LandRegister, LandUsePlan }

/// <summary>서류에서 읽은 소유자. IdFront = 주민(법인)등록번호 앞 6자리.</summary>
public sealed record DocOwner(string Name, string IdFront);

public sealed record IssuedDocInfo(
    IssuedDocKind Kind,
    bool HasText,
    List<DocOwner> Owners,
    bool? Violation,            // 건축물대장만: "위반건축물" 표기 여부
    string? Category,           // 지목
    double? Area,               // 면적(㎡)
    List<string> Zones,         // 용도지역
    int LineCount,
    string Excerpt);
