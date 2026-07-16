using System;
using System.Collections.Generic;
using System.Linq;
using WorkReport.Core.Models;
using WorkReport.Core.Parsing;
using WorkReport.Core.Reporting;

// 개발 순서 1단계: 샘플 xlsx 파싱 검증 콘솔.
// 사용법: ParseCheck <일지.xlsx> [시트명] [--author 이름] [--html <출력폴더>]
//        --html 지정 시 I열의 모든 키를 프로젝트로 간주해 리포트+index 시안을 생성한다.

string path = null, sheet = null, author = "본인", htmlOut = null;
var rest = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--html" && i + 1 < args.Length) htmlOut = args[++i];
    else if (args[i] == "--author" && i + 1 < args.Length) author = args[++i];
    else rest.Add(args[i]);
}
if (rest.Count == 0)
{
    Console.Error.WriteLine("사용법: ParseCheck <일지.xlsx> [시트명] [--author 이름] [--html 출력폴더]");
    return 1;
}
path = rest[0];
sheet = rest.Count > 1 ? rest[1] : DateTime.Now.Year.ToString();

var parser = new JournalParser();
var result = parser.ParseFile(path, sheet, author, 0);

Console.WriteLine($"파일        : {path}");
Console.WriteLine($"시트        : {result.SheetName}");
Console.WriteLine($"헤더 행     : {result.HeaderRow}");
Console.WriteLine($"열 매핑     : {result.Columns}");
Console.WriteLine($"레코드 수   : {result.Records.Count}");
Console.WriteLine($"날짜 상속   : {result.Records.Count(r => r.DateInherited)}건");
Console.WriteLine($"기간        : {result.Records.Min(r => r.Date):yyyy-MM-dd} ~ {result.Records.Max(r => r.Date):yyyy-MM-dd}");

Console.WriteLine("\n[여부 분포]");
foreach (var g in result.Records.GroupBy(r => string.IsNullOrWhiteSpace(r.Status) ? "(빈값)" : r.Status.Trim())
                                .OrderByDescending(g => g.Count()))
    Console.WriteLine($"  {g.Key,-6} {g.Count()}건");

Console.WriteLine("\n[I열 프로젝트 넘버 분포 (정규화 기준)]");
var byKey = result.Records
    .Where(r => !string.IsNullOrWhiteSpace(r.ProjectNumber))
    .GroupBy(r => KeyNormalizer.Normalize(r.ProjectNumber))
    .OrderByDescending(g => g.Count())
    .ToList();
foreach (var g in byKey)
    Console.WriteLine($"  {g.Key,-28} {g.Count(),4}건   (예: {g.First().ProjectName})");

if (result.Warnings.Count > 0)
{
    Console.WriteLine("\n[경고]");
    foreach (var w in result.Warnings) Console.WriteLine("  - " + w);
}

if (htmlOut != null)
{
    var projects = byKey.Select(g => new ProjectInfo
    {
        Number = g.First().ProjectNumber.Trim(),
        Name = g.GroupBy(r => (r.ProjectName ?? "").Trim())
                .Where(n => n.Key.Length > 0)
                .OrderByDescending(n => n.Count())
                .Select(n => n.Key).FirstOrDefault() ?? g.Key,
        Active = true,
    }).ToList();

    var reports = ReportBuilder.Build(result.Records, projects);
    var renderer = new HtmlReportRenderer();
    var files = renderer.WriteAll(htmlOut, reports, new[] { author, "협업자" }, DateTime.Now);
    Console.WriteLine($"\n[HTML 생성] {files.Count}개 파일 → {htmlOut}");
    foreach (var f in files) Console.WriteLine("  " + f);
}

return 0;
