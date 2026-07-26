using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

        /// <summary>
        /// 대시보드 탭에 쓸 그룹. Build가 그룹 이름 목록으로 자동 결정해 채운다.
        /// 비어 있으면 렌더러가 프로젝트에 적힌 그룹(EffectiveGroup)을 쓴다.
        /// </summary>
        public string Group { get; set; }
    }

    /// <summary>일지에서 H·I 열을 서로 바꿔 적어, 같은 프로젝트가 둘로 갈라진 쌍.</summary>
    public class MirrorPair
    {
        /// <summary>기록이 더 많은 쪽 (원본 표기).</summary>
        public string Primary { get; set; }
        public int PrimaryCount { get; set; }

        /// <summary>뒤바뀐 쪽 (원본 표기). 이쪽을 Primary의 별칭으로 묶으면 된다.</summary>
        public string Secondary { get; set; }
        public int SecondaryCount { get; set; }

        public int Total { get { return PrimaryCount + SecondaryCount; } }
    }

    /// <summary>두 일지의 레코드를 등록 프로젝트 기준으로 병합·그룹핑·정렬한다.</summary>
    public static class ReportBuilder
    {
        public static List<ProjectReportData> Build(IEnumerable<WorkRecord> allRecords,
            IEnumerable<ProjectInfo> activeProjects, IList<string> groups = null)
        {
            var projects = activeProjects.Where(p => p.Active).ToList();
            var dataByProject = new List<ProjectReportData>();
            // 넘버와 별칭이 모두 같은 프로젝트를 가리킨다
            var byKey = new Dictionary<string, ProjectReportData>();
            foreach (var p in projects)
            {
                var data = new ProjectReportData { Project = p, FileName = MakeFileName(p) };
                bool any = false;
                foreach (var number in p.AllNumbers)
                {
                    string key = KeyNormalizer.Normalize(number);
                    if (key.Length == 0 || byKey.ContainsKey(key)) continue;
                    byKey[key] = data;
                    any = true;
                }
                if (any) dataByProject.Add(data);
            }

            foreach (var rec in allRecords)
            {
                string key = KeyNormalizer.Normalize(rec.ProjectNumber);
                ProjectReportData data;
                if (key.Length > 0 && byKey.TryGetValue(key, out data))
                    data.Records.Add(rec);
            }

            foreach (var data in dataByProject)
            {
                // 날짜 오름차순 → 같은 날짜 내 본인(0) 먼저 → 원본 행 순서
                data.Records = data.Records
                    .OrderBy(r => r.Date.Date)
                    .ThenBy(r => r.SourceIndex)
                    .ThenBy(r => r.SourceRow)
                    .ToList();
                data.Group = GroupResolver.Resolve(data.Project, data.Records, groups);
            }

            return dataByProject.OrderBy(d => d.Project.Number, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// H·I 열을 서로 바꿔 적어 갈라진 쌍을 찾는다.
        /// (H=A, I=B) 기록과 (H=B, I=A) 기록이 둘 다 있으면 한 쌍으로 본다.
        /// </summary>
        public static List<MirrorPair> FindMirrorPairs(IEnumerable<WorkRecord> allRecords)
        {
            // 정규화 H → 정규화 I → 건수
            // (정규화 값에 공백이 들어갈 수 있어 문자열을 합치지 않고 사전을 중첩한다)
            var counts = new Dictionary<string, Dictionary<string, int>>();
            var display = new Dictionary<string, string>();   // 정규화 I → I열 원본 표기

            foreach (var r in allRecords)
            {
                string h = KeyNormalizer.Normalize(r.ProjectName);
                string i = KeyNormalizer.Normalize(r.ProjectNumber);
                if (h.Length == 0 || i.Length == 0 || h == i) continue;

                Dictionary<string, int> inner;
                if (!counts.TryGetValue(h, out inner))
                {
                    inner = new Dictionary<string, int>();
                    counts[h] = inner;
                }
                int n;
                inner.TryGetValue(i, out n);
                inner[i] = n + 1;

                if (!display.ContainsKey(i))
                    display[i] = Regex.Replace(r.ProjectNumber ?? "", @"\s+", " ").Trim();
            }

            var pairs = new List<MirrorPair>();
            var seen = new HashSet<string>();
            foreach (var outer in counts)
            {
                string h = outer.Key;
                foreach (var entry in outer.Value)
                {
                    string i = entry.Key;

                    // (H=i, I=h) 기록도 있어야 뒤바뀐 쌍이다
                    Dictionary<string, int> mirrorInner;
                    int mirrorCount;
                    if (!counts.TryGetValue(i, out mirrorInner)) continue;
                    if (!mirrorInner.TryGetValue(h, out mirrorCount)) continue;

                    // 한 쌍은 한 번만 담는다
                    string pairId = string.CompareOrdinal(h, i) < 0 ? h + "\u0001" + i : i + "\u0001" + h;
                    if (!seen.Add(pairId)) continue;

                    // I열이 i인 기록 수 = entry.Value, I열이 h인 기록 수 = mirrorCount
                    bool iIsBigger = entry.Value >= mirrorCount;
                    string primary = iIsBigger ? i : h;
                    string secondary = iIsBigger ? h : i;
                    pairs.Add(new MirrorPair
                    {
                        Primary = display.ContainsKey(primary) ? display[primary] : primary,
                        PrimaryCount = iIsBigger ? entry.Value : mirrorCount,
                        Secondary = display.ContainsKey(secondary) ? display[secondary] : secondary,
                        SecondaryCount = iIsBigger ? mirrorCount : entry.Value,
                    });
                }
            }

            return pairs.OrderByDescending(p => p.Total).ToList();
        }

        /// <summary>미등록 키 감지: I열에 존재하지만 projects.json에 없는 넘버 → (원본 표기, 건수, 예시 프로젝트명).</summary>
        public static List<UnregisteredKey> FindUnregisteredKeys(IEnumerable<WorkRecord> allRecords, IEnumerable<ProjectInfo> allProjects)
        {
            // 별칭으로 묶인 넘버도 등록된 것으로 본다
            var registered = new HashSet<string>(
                allProjects.SelectMany(p => p.AllNumbers).Select(KeyNormalizer.Normalize));

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
