using System.Text;
using System.Text.RegularExpressions;

namespace LawReview.Core.LawApi;

/// <summary>
/// 별표 본문을 다루는 공통 도구.
///
/// 별표는 두 가지 모습으로 온다:
///  · **법령 별표** — 본문 텍스트가 API 응답에 그대로 담긴다(괘선 표 포함). 다만 통째로 인용하면
///    수만 자라 판정 프롬프트를 잡아먹으므로 **해당 용도 항목만 발췌**한다.
///  · **자치법규 별표** — 내용 없이 HWP 첨부 링크만 온다. 한 파일에 [별표 1]…[별표 N]이 모두
///    들어 있어, 필요한 별표 하나를 제목으로 잘라내야 한다.
/// </summary>
public static class AnnexText
{
    // "[별표 3] <개정 …>" / "[별표 2］" (닫는 괄호가 전각인 원문도 있다)
    private static readonly Regex AnnexHeader = new(@"^\s*﻿?\[별[표지]\s*\d+", RegexOptions.Compiled);

    /// <summary>
    /// 조례 별표 묶음(HWP 한 파일에 여러 별표)에서 제목에 <paramref name="titleKeyword"/>가 들어간
    /// 별표 하나를 잘라낸다. 별표 번호는 지자체마다 다르므로 **제목으로 찾는다**
    /// (조문번호 하드코딩 금지 원칙과 같은 이유).
    /// </summary>
    public static IReadOnlyList<string> Section(IReadOnlyList<string> lines, string titleKeyword)
    {
        var key = Normalize(titleKeyword);
        var start = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (!Normalize(lines[i]).Contains(key)) continue;
            // 제목 줄 바로 위가 "[별표 N]" 머리글이면 거기서부터가 그 별표다.
            start = i > 0 && AnnexHeader.IsMatch(lines[i - 1]) ? i - 1 : i;
            break;
        }
        if (start < 0) return Array.Empty<string>();

        var section = new List<string> { lines[start] };
        for (var i = start + 1; i < lines.Count; i++)
        {
            if (AnnexHeader.IsMatch(lines[i])) break;   // 다음 별표가 시작되면 끝
            section.Add(lines[i]);
        }
        return section;
    }

    /// <summary>공백·중점 표기 차이를 무시하고 비교하기 위한 정규화.</summary>
    internal static string Normalize(string s) => Regex.Replace(s ?? "", @"\s+", "");

    // 별표 표 안의 항목 머리("가.", "나." …). 법령 별표는 들여쓰기가 붙는다.
    private static readonly Regex ItemHead = new(@"^\s{0,6}([가-힣])\.\s", RegexOptions.Compiled);

    /// <summary>
    /// 법령 별표 본문에서 <paramref name="primaryUse"/>가 걸리는 항목만 발췌한다.
    /// 전문이 짧으면(<paramref name="maxChars"/> 이내) 그대로 돌려준다.
    ///
    /// 발췌하는 이유: 편의증진법 시행령 별표 2(대상시설별 편의시설의 종류 및 설치기준)는 5만 자에
    /// 가깝다. 통째로 넣으면 판정 프롬프트가 감당하지 못하고, 잘라내면 정작 필요한 용도 행이 사라진다.
    /// 그래서 **용도로 골라서** 넣는다. 발췌했다는 사실과 원문 링크는 인용에 함께 남긴다.
    /// </summary>
    public static string ExcerptForUse(string content, string? primaryUse, int maxChars = 4000)
    {
        var text = (content ?? "").Replace("\r", "").TrimEnd();
        if (text.Length == 0) return "";
        if (text.Length <= maxChars) return text;

        var lines = text.Split('\n');
        var use = (primaryUse ?? "").Trim();

        if (use.Length >= 2)
        {
            var picked = PickItemBlocks(lines, use, maxChars);
            if (picked.Length > 0)
                return $"{Header(lines)}\n(용도 \"{use}\"에 해당하는 항목만 발췌 — 전문은 원문 링크 참조)\n{picked}";
        }
        return $"{text[..maxChars]}\n…(이하 생략 — 전문은 원문 링크 참조)";
    }

    /// <summary>별표 제목 줄(맨 앞 비어 있지 않은 두 줄).</summary>
    private static string Header(IReadOnlyList<string> lines) =>
        string.Join("\n", lines.Where(l => l.Trim().Length > 0).Take(2));

    /// <summary>"가./나./…" 항목 단위로 끊어, 용도명이 들어간 항목만 이어 붙인다.</summary>
    private static string PickItemBlocks(IReadOnlyList<string> lines, string use, int maxChars)
    {
        var sb = new StringBuilder();
        var block = new List<string>();

        void Flush()
        {
            if (block.Count == 0) return;
            var body = string.Join("\n", block);
            if (Normalize(body).Contains(Normalize(use)) && sb.Length + body.Length <= maxChars)
                sb.AppendLine(body);
            block.Clear();
        }

        foreach (var line in lines)
        {
            if (ItemHead.IsMatch(line)) Flush();
            block.Add(line.TrimEnd());
        }
        Flush();
        return sb.ToString().TrimEnd();
    }
}
