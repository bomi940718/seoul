using System;
using System.Collections.Generic;
using System.Linq;
using WorkReport.Core.Models;
using WorkReport.Core.Parsing;

namespace WorkReport.Core.Reporting
{
    /// <summary>프로젝트 1건에 대한 병합·정렬 완료 데이터.</summary>
    public class ProjectReportData
    {
        public ProjectInfo Project { get; set; }
        public List<WorkRecord> Records { get; set; } = new List<WorkRecord>();
        public string FileName { get; set; }
    }

    /// <summary>두 일지의 레코드를 등록 프로젝트 기준으로 병합·그룹핑·정렬한다.</summary>
    public static class ReportBuilder
    {
        public static List<ProjectReportData> Build(IEnumerable<WorkRecord> allRecords, IEnumerable<ProjectInfo> activeProjects)
        {
            var projects = activeProjects.Where(p => p.Active).ToList();
            var byKey = new Dictionary<string, ProjectReportData>();
            foreach (var p in projects)
            {
                string key = KeyNormalizer.Normalize(p.Number);
                if (key.Length == 0) continue;
                if (!byKey.ContainsKey(key))
                    byKey[key] = new ProjectReportData { Project = p, FileName = MakeFileName(p) };
            }

            foreach (var rec in allRecords)
            {
                string key = KeyNormalizer.Normalize(rec.ProjectNumber);
                if (key.Length > 0 && byKey.TryGetValue(key, out var data))
                    data.Records.Add(rec);
            }

            foreach (var data in byKey.Values)
            {
                // 날짜 오름차순 → 같은 날짜 내 본인(0) 먼저 → 원본 행 순서
                data.Records = data.Records
                    .OrderBy(r => r.Date.Date)
                    .ThenBy(r => r.SourceIndex)
                    .ThenBy(r => r.SourceRow)
                    .ToList();
            }

            return byKey.Values.OrderBy(d => d.Project.Number, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>미등록 키 감지: I열에 존재하지만 projects.json에 없는 넘버 → (원본 표기, 건수, 예시 프로젝트명).</summary>
        public static List<UnregisteredKey> FindUnregisteredKeys(IEnumerable<WorkRecord> allRecords, IEnumerable<ProjectInfo> allProjects)
        {
            var registered = new HashSet<string>(allProjects.Select(p => KeyNormalizer.Normalize(p.Number)));
            return allRecords
                .Where(r => !string.IsNullOrWhiteSpace(r.ProjectNumber))
                .GroupBy(r => KeyNormalizer.Normalize(r.ProjectNumber))
                .Where(g => g.Key.Length > 0 && !registered.Contains(g.Key))
                .Select(g => new UnregisteredKey
                {
                    Number = g.First().ProjectNumber.Trim(),
                    Count = g.Count(),
                    SampleProjectName = g.Select(r => r.ProjectName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "",
                })
                .OrderByDescending(k => k.Count)
                .ToList();
        }

        /// <summary>출력 파일명 규칙: {프로젝트넘버}_{프로젝트명}.html (불가 문자·줄바꿈 → _).</summary>
        public static string MakeFileName(ProjectInfo p)
        {
            string raw = $"{p.Number}_{p.Name}";
            // Windows 파일명 불가 문자 기준 (개발 환경 OS와 무관하게 NAS/Windows 대상)
            var invalid = new HashSet<char>("\\/:*?\"<>|") { '\n', '\r', '\t' };
            for (char c = '\0'; c < ' '; c++) invalid.Add(c);
            var chars = raw.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            string name = new string(chars).Trim().Trim('.');
            if (name.Length == 0) name = "report";
            if (name.Length > 120) name = name.Substring(0, 120);
            return name + ".html";
        }
    }

    public class UnregisteredKey
    {
        public string Number { get; set; }
        public int Count { get; set; }
        public string SampleProjectName { get; set; }
    }
}
