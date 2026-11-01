using System.Text.Json;
using LawReview.Core.LawApi;

namespace LawReview.Core.Tests;

/// <summary>
/// 조례 조문 픽스처. `Fixtures/ordinance_articles.json`은 법제처 실응답에서 뽑은
/// 다섯 지자체 건축조례의 "공개 공지"·"일조" 조문 원문이다(2026-09 캡처).
///
/// 조례는 표기가 지자체마다 달라 한 곳만 맞춰 두면 다른 곳에서 값을 못 읽고
/// **조용히 기준 없이 넘어간다.** 실제 문구로 고정해 둔다.
/// </summary>
internal static class OrdinanceArticleFixture
{
    private static readonly Lazy<JsonDocument> Document = new(() => JsonDocument.Parse(
        File.ReadAllText(ChecklistTests.FindRepoFile(Path.Combine(
            "tests", "LawReview.Core.Tests", "Fixtures", "ordinance_articles.json")))));

    /// <summary>해당 지자체 건축조례를 LawText로 만든다(조문은 픽스처에 담긴 것만).</summary>
    public static LawText Ordinance(string name)
    {
        var node = Document.Value.RootElement.GetProperty(name);
        var articles = node.GetProperty("articles").EnumerateArray()
            .Select(a => new Article(
                a.GetProperty("number").GetString() ?? "",
                a.GetProperty("title").GetString() ?? "",
                a.GetProperty("body").GetString() ?? ""))
            .ToList();
        return new LawText(name, node.GetProperty("effectiveDate").GetString() ?? "", articles);
    }
}
