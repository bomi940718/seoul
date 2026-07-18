using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LawReview.Core.Models;
using LawReview.Core.Review;
using static LawReview.Core.Review.QuantitativeCalculator;

namespace LawReview.Core.Report;

/// <summary>
/// ReviewResult를 법규검토서(.docx)로 조립한다.
/// 문서 구성은 실무 검토서 원형을 따른다:
/// 표지 → 면적표 → 설계개요 → 검토법규 → 요약 검토표 → 장별 상세검토.
/// </summary>
public sealed class DocxReportBuilder
{
    private const string Font = "맑은 고딕";

    public void Build(ReviewResult result, string outputPath)
    {
        using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        var body = main.Document.Body!;

        BuildCover(body, result.Project);
        BuildAreaTables(body, result.Project);
        BuildDesignOverview(body, result);
        BuildReviewedLaws(body, result);
        BuildSummaryTable(body, result);
        BuildDetailSections(body, result);

        main.Document.Save();
    }

    // ── 표지 ─────────────────────────────────────────────────────
    private static void BuildCover(Body body, ProjectInput p)
    {
        body.Append(Paragraph($"{p.ProjectName} 법규검토서", size: 36, bold: true, center: true));
        body.Append(Paragraph($"작성일: {DateTime.Now:yyyy년 M월 d일}", size: 20, center: true));
        body.Append(Paragraph("", size: 20));
    }

    // ── 면적표 ───────────────────────────────────────────────────
    private static void BuildAreaTables(Body body, ProjectInput p)
    {
        foreach (var b in p.Buildings)
        {
            body.Append(Heading($"■ {b.Name} 각 층별 면적표"));
            var rows = new List<string[]>
            {
                new[] { "층별", "용도", "전용 (㎡)", "공용 (㎡)", "합계 (㎡)", "비고" },
            };
            foreach (var f in b.Floors)
            {
                rows.Add(new[]
                {
                    f.FloorLabel, f.Use,
                    f.ExclusiveArea > 0 ? N(f.ExclusiveArea) : "-",
                    f.CommonArea > 0 ? N(f.CommonArea) : "-",
                    f.TotalArea > 0 ? N(f.TotalArea) : "-",
                    f.Note ?? (f.ExcludeFromGrossArea ? "연면적 제외" : ""),
                });
            }
            rows.Add(new[] { "연면적 소계", "", "", "", N(b.GrossFloorArea), "" });
            body.Append(Table(rows));
            body.Append(Paragraph(""));
        }

        if (p.Buildings.Count > 1)
        {
            body.Append(Heading("■ 총 면적표"));
            var rows = new List<string[]> { new[] { "동", "연면적 (㎡)", "바닥면적 합계 (㎡)" } };
            foreach (var b in p.Buildings)
                rows.Add(new[] { b.Name, N(b.GrossFloorArea), N(b.TotalFloorArea) });
            rows.Add(new[] { "합계", N(p.GrossFloorArea), N(p.TotalFloorArea) });
            body.Append(Table(rows));
            body.Append(Paragraph(""));
        }
    }

    // ── 설계개요 ─────────────────────────────────────────────────
    private static void BuildDesignOverview(Body body, ReviewResult r)
    {
        var p = r.Project;
        var o = r.Overview;
        body.Append(Heading("■ 설계개요"));

        var rows = new List<string[]>
        {
            new[] { "구분", "계획", "법정", "법규(근거)" },
            new[] { "사업명", p.ProjectName, "", "" },
            new[] { "건축주", p.Client, "", "" },
            new[] { "대지위치", p.SiteAddress, "", "" },
            new[] { "지역/지구", string.Join(", ", p.UseZones), "", "" },
            new[] { "대지면적", $"{N(p.SiteArea)} ㎡", "", "" },
            new[] { "용도", p.PrimaryUse, "", "" },
        };
        if (p.AllowedUse is not null)
            rows.Add(new[] { "허용용도", p.AllowedUse, "", p.UseBasis ?? "" });
        if (p.DisallowedUse is not null)
            rows.Add(new[] { "불허용도", p.DisallowedUse, "", p.UseBasis ?? "" });

        rows.Add(new[]
        {
            "건축면적", $"{N(p.PlannedBuildingArea)} ㎡",
            o.MaxBuildingAreaFormula is null ? "-" : $"{o.MaxBuildingAreaFormula} 이하",
            p.Zoning.Source,
        });
        rows.Add(new[]
        {
            "건폐율", o.CoverageFormula,
            p.Zoning.MaxCoverageRatio is double c ? $"{c:0.00} % 이하" : "-",
            p.Zoning.Source,
        });
        rows.Add(new[]
        {
            "연면적", $"{N(o.GrossFloorArea)} ㎡",
            o.MaxGrossFloorAreaFormula is null ? "-" : $"{o.MaxGrossFloorAreaFormula} 이하",
            p.Zoning.Source,
        });
        rows.Add(new[]
        {
            "용적률", o.FloorAreaRatioFormula,
            p.Zoning.MaxFloorAreaRatio is double far ? $"{far:0.00} % 이하" : "-",
            p.Zoning.Source,
        });
        rows.Add(new[]
        {
            "건축규모", $"지상 {p.PlannedFloorsAbove}층" + (p.PlannedFloorsBelow > 0 ? $" / 지하 {p.PlannedFloorsBelow}층" : ""),
            p.Zoning.MaxFloors is int mf ? $"지상 {mf}층 이하" : "-",
            p.Zoning.Source,
        });
        if (p.PlannedHeight is not null)
            rows.Add(new[] { "최고높이", p.PlannedHeight, p.Zoning.MaxHeight is double mh ? $"{mh:0.0} m 이하" : "-", "" });

        rows.Add(new[] { "주차대수 (계획 연면적 기준)", o.Parking.TotalFormula,
            o.ParkingAtLegalMax is null ? "-" : o.ParkingAtLegalMax.TotalFormula, p.Parking.Source });
        if (o.Parking.DisabledFormula is not null)
            rows.Add(new[] { "장애인전용", o.Parking.DisabledFormula,
                o.ParkingAtLegalMax?.DisabledFormula ?? "-", "주차대수의 3퍼센트 이상" });
        if (o.Parking.ExpandedFormula is not null)
            rows.Add(new[] { "확장형", o.Parking.ExpandedFormula,
                o.ParkingAtLegalMax?.ExpandedFormula ?? "-", "주차대수의 3퍼센트 이상" });

        body.Append(Table(rows));
        body.Append(Paragraph(""));
    }

    // ── 검토법규 목록 ─────────────────────────────────────────────
    private static void BuildReviewedLaws(Body body, ReviewResult r)
    {
        body.Append(Heading("■ 검토법규"));
        var rows = new List<string[]> { new[] { "법규명", "시행일자" } };
        foreach (var (name, date) in r.ReviewedLaws.OrderBy(kv => kv.Key))
            rows.Add(new[] { name, FormatDate(date) });
        if (rows.Count == 1)
            rows.Add(new[] { "(법제처 조회 결과 없음 — API 키를 확인하세요)", "" });
        body.Append(Table(rows));
        body.Append(Paragraph(""));
    }

    // ── 요약 검토표 ───────────────────────────────────────────────
    private static void BuildSummaryTable(Body body, ReviewResult r)
    {
        body.Append(Heading("■ 관련 주요 법규 검토서 (요약)"));
        var rows = new List<string[]>
        {
            new[] { "항목", "대상 (근거)", "법적 기준", "설계 기준 (산정식)", "판정" },
        };
        foreach (var row in r.Rows.Where(x => x.Item.InSummary))
        {
            var basis = row.Citations.Count > 0
                ? string.Join("\n", row.Citations.Select(c => $"{c.LawName} {FormatArticleRef(c.ArticleNo)}".TrimEnd()).Distinct())
                : string.Join("\n", row.Item.Basis.Select(b => ResolveName(b.LawName, r.Project)));
            rows.Add(new[]
            {
                row.Item.Title, basis,
                row.CriterionText ?? "-",
                row.CalculationText ?? row.Reason ?? "-",
                row.Applicability.ToString(),
            });
        }
        body.Append(Table(rows));
        body.Append(Paragraph(""));
    }

    // ── 장별 상세검토 ─────────────────────────────────────────────
    private static void BuildDetailSections(Body body, ReviewResult r)
    {
        var sections = r.Rows
            .Where(x => x.Item.Section != "요약")
            .GroupBy(x => x.Item.Section);

        foreach (var section in sections)
        {
            body.Append(Heading($"■ {section.Key}"));
            var rows = new List<string[]> { new[] { "항목", "내용 (조문 원문 — 법제처 현행)", "적용여부", "판정 사유" } };
            foreach (var row in section)
            {
                if (row.Citations.Count == 0)
                {
                    rows.Add(new[] { row.Item.Title, row.Item.Note ?? "-", row.Applicability.ToString(), row.Reason ?? "" });
                    continue;
                }
                var first = true;
                foreach (var c in row.Citations)
                {
                    rows.Add(new[]
                    {
                        first ? row.Item.Title : "",
                        (FormatArticleRef(c.ArticleNo) is { Length: > 0 } artRef
                            ? $"{c.LawName} {artRef}({c.Title})" : $"{c.LawName} {c.Title}") +
                            (c.EffectiveDate.Length > 0 ? $" [시행 {FormatDate(c.EffectiveDate)}]" : "") +
                            $"\n{c.Body}",
                        first ? row.Applicability.ToString() : "",
                        first ? row.Reason ?? "" : "",
                    });
                    first = false;
                }
            }
            body.Append(Table(rows));
            body.Append(Paragraph(""));
        }
    }

    private static string ResolveName(string lawName, ProjectInput p) =>
        ReviewEngine.ResolvePlaceholders(lawName, p);

    /// <summary>"48의2" → "제48조의2", "42" → "제42조". 별표 인용("-")은 빈 문자열.</summary>
    internal static string FormatArticleRef(string articleNo)
    {
        if (string.IsNullOrEmpty(articleNo) || articleNo == "-") return "";
        var idx = articleNo.IndexOf('의');
        return idx > 0 ? $"제{articleNo[..idx]}조의{articleNo[(idx + 1)..]}" : $"제{articleNo}조";
    }

    private static string FormatDate(string yyyymmdd) =>
        yyyymmdd.Length == 8 ? $"{yyyymmdd[..4]}.{yyyymmdd[4..6]}.{yyyymmdd[6..]}" : yyyymmdd;

    // ── OpenXML 요소 헬퍼 ─────────────────────────────────────────
    private static Paragraph Heading(string text) => Paragraph(text, size: 24, bold: true);

    private static Paragraph Paragraph(string text, int size = 20, bool bold = false, bool center = false)
    {
        var runProps = new RunProperties(
            new RunFonts { Ascii = Font, EastAsia = Font, HighAnsi = Font },
            new FontSize { Val = size.ToString() });
        if (bold) runProps.Append(new Bold());

        var para = new Paragraph();
        if (center)
            para.Append(new ParagraphProperties(new Justification { Val = JustificationValues.Center }));

        foreach (var (line, i) in text.Split('\n').Select((l, i) => (l, i)))
        {
            var run = new Run((RunProperties)runProps.CloneNode(true));
            if (i > 0) run.Append(new Break());
            run.Append(new Text(line) { Space = SpaceProcessingModeValues.Preserve });
            para.Append(run);
        }
        return para;
    }

    private static Table Table(IReadOnlyList<string[]> rows)
    {
        var table = new Table(new TableProperties(
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 }),
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct }));

        foreach (var (cells, rowIdx) in rows.Select((r, i) => (r, i)))
        {
            var tr = new TableRow();
            foreach (var cell in cells)
            {
                var tc = new TableCell(Paragraph(cell, size: 18, bold: rowIdx == 0));
                if (rowIdx == 0)
                    tc.PrependChild(new TableCellProperties(
                        new Shading { Val = ShadingPatternValues.Clear, Fill = "EEEEEE" }));
                tr.Append(tc);
            }
            table.Append(tr);
        }
        return table;
    }
}
