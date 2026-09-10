using System;
using System.Collections.Generic;
using System.Linq;
using WorkReport.Core.Models;
using WorkReport.Core.Parsing;

namespace WorkReport.Core.Reporting
{
    /// <summary>
    /// 프로젝트의 그룹을 정한다.
    ///
    /// 1) 프로젝트에 그룹이 직접 적혀 있으면 그것을 쓴다 (수동 지정이 항상 이긴다).
    /// 2) 없으면 그 프로젝트의 기록에서 H열·I열 값을 훑어 그룹 규칙의 키워드와 대조한다.
    ///    일지에서 H·I를 바꿔 적은 기록이 있어 양쪽을 모두 본다.
    /// 3) 그래도 못 찾으면 ETC.
    /// </summary>
    public static class GroupResolver
    {
        public const string Fallback = "ETC";

        /// <summary>
        /// 값 하나에서 그룹을 찾는다. 대소문자·공백 무시, 부분 일치
        /// (예: 키워드 BRANDING → "BRANDING-1" 일치, 키워드 MANAGEMENT → "General Management" 일치).
        /// 목록 순서가 우선순위다.
        /// </summary>
        public static string MatchOne(string value, IEnumerable<GroupRule> groups)
        {
            string v = KeyNormalizer.Normalize(value);
            if (v.Length == 0) return null;

            foreach (var g in groups ?? Enumerable.Empty<GroupRule>())
            {
                if (g == null || string.IsNullOrWhiteSpace(g.Name)) continue;
                foreach (var keyword in g.AllKeywords)
                {
                    string key = KeyNormalizer.Normalize(keyword);
                    // 한 글자짜리 키워드는 아무 데나 걸리므로 제외
                    if (key.Length < 2) continue;
                    if (v.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                        return g.Name.Trim();
                }
            }
            return null;
        }

        /// <summary>
        /// 기록 묶음에서 그룹을 정한다. 가장 많이 걸린 그룹이 이기고,
        /// 건수가 같으면 그룹 목록에서 앞에 있는 것이 이긴다.
        /// </summary>
        public static string Resolve(ProjectInfo project, IEnumerable<WorkRecord> records, IList<GroupRule> groups)
        {
            if (project != null && !string.IsNullOrWhiteSpace(project.Group))
                return project.Group.Trim();

            if (groups == null || groups.Count == 0) return FallbackFor(project);

            var hits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in records ?? Enumerable.Empty<WorkRecord>())
            {
                // H열(이름)을 먼저 보고, 없으면 I열(넘버)에서 찾는다
                string g = MatchOne(r.ProjectName, groups) ?? MatchOne(r.ProjectNumber, groups);
                if (g == null) continue;
                int n;
                hits.TryGetValue(g, out n);
                hits[g] = n + 1;
            }
            if (hits.Count == 0) return FallbackFor(project);

            return hits
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => IndexOf(groups, kv.Key))
                .First().Key;
        }

        /// <summary>
        /// 규칙에 걸리지 않을 때의 그룹. 프로젝트 이름(일지 H열)을 그대로 쓴다 —
        /// 대시보드 카드의 큰 글씨와 같은 값이라, 규칙을 하나도 안 적어도 이름별로 묶인다.
        /// </summary>
        private static string FallbackFor(ProjectInfo project)
        {
            if (project == null || string.IsNullOrWhiteSpace(project.Name)) return Fallback;
            return System.Text.RegularExpressions.Regex.Replace(project.Name, @"\s+", " ").Trim();
        }

        private static int IndexOf(IList<GroupRule> groups, string name)
        {
            for (int i = 0; i < groups.Count; i++)
                if (string.Equals((groups[i]?.Name ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return i;
            return int.MaxValue;
        }

        // ---- 프로젝트 관리 창의 여러 줄 입력 ↔ 규칙 목록 ----
        // 한 줄에 규칙 하나: "ARCHITECTURE = Planning project, 용도변경" 또는 그냥 "INTERIOR"

        public static string Format(IEnumerable<GroupRule> groups)
        {
            if (groups == null) return "";
            return string.Join(Environment.NewLine, groups
                .Where(g => g != null && !string.IsNullOrWhiteSpace(g.Name))
                .Select(g => g.Keywords == null || g.Keywords.Count == 0
                    ? g.Name
                    : g.Name + " = " + string.Join(", ", g.Keywords)));
        }

        public static List<GroupRule> Parse(string text)
        {
            var list = new List<GroupRule>();
            foreach (var line in (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string s = line.Trim();
                if (s.Length == 0) continue;

                int eq = s.IndexOf('=');
                string name = eq < 0 ? s : s.Substring(0, eq).Trim();
                if (name.Length == 0) continue;

                var rule = new GroupRule { Name = name };
                if (eq >= 0)
                {
                    rule.Keywords = s.Substring(eq + 1)
                        .Split(',')
                        .Select(k => k.Trim())
                        .Where(k => k.Length > 0)
                        .ToList();
                }
                list.Add(rule);
            }
            return list;
        }
    }
}
