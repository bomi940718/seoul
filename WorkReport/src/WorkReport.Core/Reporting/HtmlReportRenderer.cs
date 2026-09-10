using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using WorkReport.Core.Models;

namespace WorkReport.Core.Reporting
{
    /// <summary>
    /// 임베디드 HTML 템플릿에 JSON 데이터를 주입해 self-contained 단일 파일을 생성한다.
    /// 외부 리소스 참조 없음 (CSS/JS 전부 템플릿 내 인라인).
    /// </summary>
    public class HtmlReportRenderer
    {
        /// <summary>프로젝트별 리포트를 모아두는 하위 폴더. 출력 폴더 루트에는 index.html만 남는다.</summary>
        public const string ReportsDirName = "reports";

        private static readonly string[] KoreanDow = { "일", "월", "화", "수", "목", "금", "토" };

        // </script> 문자열이 데이터에 있어도 스크립트 블록이 깨지지 않도록 <,> 를 이스케이프
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            StringEscapeHandling = StringEscapeHandling.EscapeHtml,
        };

        public string RenderProjectReport(ProjectReportData data, IList<string> authorNames, DateTime generatedAt)
        {
            var meta = new
            {
                number = data.Project.Number,
                name = data.Project.Name,
                generatedAt = generatedAt.ToString("yyyy-MM-dd HH:mm"),
                authors = authorNames,
            };
            var rows = data.Records.Select(r => new
            {
                d = r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                w = KoreanDow[(int)r.Date.DayOfWeek],
                a = r.SourceIndex,
                st = r.Status ?? "",
                ds = r.Descriptions ?? "",
                rg = r.Regiment ?? "",
                os = r.Outsider ?? "",
                ml = r.Mail ?? "",
                sc = r.Schedule ?? "",
                pl = r.Plan ?? "",
                nt = r.Note ?? "",
            });

            return LoadTemplate("report.html")
                .Replace("@@TITLE@@", HtmlEscape($"{data.Project.Number} · {data.Project.Name} — 워크리포트"))
                .Replace("@@META_JSON@@", JsonConvert.SerializeObject(meta, JsonSettings))
                .Replace("@@DATA_JSON@@", JsonConvert.SerializeObject(rows, JsonSettings));
        }

        public string RenderIndex(IList<ProjectReportData> projects, DateTime generatedAt)
        {
            var meta = new { generatedAt = generatedAt.ToString("yyyy-MM-dd HH:mm") };
            var cards = projects
                .Select(p => new
                {
                    number = p.Project.Number,
                    name = p.Project.Name,
                    group = string.IsNullOrWhiteSpace(p.Group) ? p.Project.EffectiveGroup : p.Group.Trim(),
                    status = p.Project.EffectiveStatus,
                    file = ReportsDirName + "/" + p.FileName,
                    total = p.Records.Count,
                    o = p.Records.Count(r => NormStatus(r.Status) == "O"),
                    tri = p.Records.Count(r => NormStatus(r.Status) == "△"),
                    x = p.Records.Count(r => NormStatus(r.Status) == "X"),
                    blank = p.Records.Count(r => NormStatus(r.Status) == "-"),
                    last = p.Records.Count == 0 ? "" : p.Records.Max(r => r.Date).ToString("yyyy-MM-dd"),
                })
                .OrderByDescending(c => c.last)
                .ThenBy(c => c.number, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return LoadTemplate("index.html")
                .Replace("@@META_JSON@@", JsonConvert.SerializeObject(meta, JsonSettings))
                .Replace("@@DATA_JSON@@", JsonConvert.SerializeObject(cards, JsonSettings));
        }

        /// <summary>프로젝트별 HTML + index.html 을 지정 폴더에 전부 생성 (멱등: 항상 전체 재생성).</summary>
        public List<string> WriteAll(string outputDir, IList<ProjectReportData> projects, IList<string> authorNames, DateTime generatedAt)
        {
            Directory.CreateDirectory(outputDir);
            string reportsDir = Path.Combine(outputDir, ReportsDirName);
            Directory.CreateDirectory(reportsDir);

            var written = new List<string>();
            foreach (var p in projects)
            {
                string path = Path.Combine(reportsDir, p.FileName);
                File.WriteAllText(path, RenderProjectReport(p, authorNames, generatedAt), new UTF8Encoding(false));
                written.Add(path);
            }
            string indexPath = Path.Combine(outputDir, "index.html");
            File.WriteAllText(indexPath, RenderIndex(projects, generatedAt), new UTF8Encoding(false));
            written.Add(indexPath);
            return written;
        }

        private static string NormStatus(string st)
        {
            st = (st ?? "").Trim().ToUpperInvariant();
            if (st == "O" || st == "0" || st == "○") return "O";
            if (st == "△") return "△";
            if (st == "X") return "X";
            return "-";
        }

        private static string HtmlEscape(string s)
            => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        private static string LoadTemplate(string name)
        {
            var asm = typeof(HtmlReportRenderer).GetTypeInfo().Assembly;
            string resource = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("." + name, StringComparison.OrdinalIgnoreCase));
            if (resource == null)
                throw new InvalidOperationException($"임베디드 템플릿 '{name}' 을(를) 찾을 수 없습니다.");
            using (var stream = asm.GetManifestResourceStream(resource))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }
    }
}
