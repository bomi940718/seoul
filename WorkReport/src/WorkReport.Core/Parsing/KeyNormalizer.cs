using System.Text;

namespace WorkReport.Core.Parsing
{
    /// <summary>
    /// 프로젝트 넘버 매칭 키 정규화.
    /// 실측: I열 키에 셀 내 줄바꿈이 포함된 값이 존재함 (예: "General\nManagement").
    /// 규칙: Trim + 내부 연속 공백/줄바꿈 → 단일 공백 + 대소문자 무시(Upper).
    /// </summary>
    public static class KeyNormalizer
    {
        public static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            var sb = new StringBuilder(raw.Length);
            bool pendingSpace = false;
            foreach (char c in raw.Trim())
            {
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = true;
                    continue;
                }
                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                pendingSpace = false;
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        public static bool Matches(string a, string b)
            => Normalize(a) == Normalize(b) && Normalize(a).Length > 0;
    }
}
