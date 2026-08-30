using LawReview.Core.Models;
using LawReview.Core.Review;

namespace LawReview.Core.Ai;

/// <summary>
/// Claude 키 없이 실행할 때의 판정 대체자: AI 판정 항목을 전부 "확인필요"로 둔다.
/// 조문 인용·정량 계산·검토서 생성은 그대로 동작하므로, 키 발급 전에도 검토서 뼈대를 뽑아볼 수 있다.
/// </summary>
public sealed class OfflineJudgmentProvider : IJudgmentProvider
{
    public Task<Judgment> JudgeAsync(ChecklistItem item, IReadOnlyList<CitedArticle> articles,
        ProjectInput project, CancellationToken ct = default) =>
        Task.FromResult(new Judgment(Applicability.확인필요,
            "AI 판정 미실행 (Claude API 키 미설정) — 인용된 조문 원문을 직접 확인하세요."));
}
