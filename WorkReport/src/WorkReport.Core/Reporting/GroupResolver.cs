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
    /// 2) 없으면 그 프로젝트의 기록에서 H열·I열 값을 훑어 그룹 이름 목록과 대조한다.
    ///    일지에서 H·I를 바꿔 적은 기록이 있어 양쪽을 모두 본다.
    /// 3) 그래도 못 찾으면 ETC.
    /// </summary>
    public static class GroupResolver
    {
        public const string Fallback = "ETC";

        /// <summary>
        /// 값 하나에서 그룹 이름을 찾는다. 대소문자·공백 무시, 부분 일치
        /// (예: "BRANDING-1" → BRANDING, "General Management" → MANAGEMENT, "Planning project" → PLANNING).
        /// 목록 순서가 우선순위다.
        /// </summary>
        public static string MatchOne(string value, IEnumerable<string> groups)
        {
            string v = KeyNormalizer.Normalize(value);
            if (v.Length == 0) return null;

            foreach (var g in groups ?? Enumerable.Empty<string>())
            {
                string key = KeyNormalizer.Normalize(g);
                // 한 글자짜리 그룹은 아무 데나 걸리므로 제외
                if (key.Length < 2) continue;
                if (v.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return g.Trim();
            }
            return null;
        }

        /// <summary>
        /// 기록 묶음에서 그룹을 정한다. 가장 많이 걸린 그룹이 이기고,
        /// 건수가 같으면 그룹 목록에서 앞에 있는 것이 이긴다.
        /// </summary>
        public static string Resolve(ProjectInfo project, IEnumerable<WorkRecord> records, IList<string> groups)
        {
            if (project != null && !string.IsNullOrWhiteSpace(project.Group))
                return project.Group.Trim();

            if (groups == null || groups.Count == 0) return Fallback;

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
            if (hits.Count == 0) return Fallback;

            return hits
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => IndexOf(groups, kv.Key))
                .First().Key;
        }

        private static int IndexOf(IList<string> groups, string name)
        {
            for (int i = 0; i < groups.Count; i++)
                if (string.Equals((groups[i] ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return i;
            return int.MaxValue;
        }
    }
}
