using LawReview.Core.Models;
using LawReview.Core.Review;

namespace LawReview.Core.Ai;

/// <summary>
/// 정성 항목의 "적용/해당없음" 판정자.
/// 조문 원문을 옮겨 쓰는 일은 하지 않는다 — 원문은 항상 법제처 API에서 오고,
/// 판정자는 이 프로젝트에 해당 조문이 적용되는지 여부와 사유만 답한다.
/// </summary>
public interface IJudgmentProvider
{
    Task<Judgment> JudgeAsync(ChecklistItem item, IReadOnlyList<CitedArticle> articles,
        ProjectInput project, CancellationToken ct = default);
}

public sealed record Judgment(Applicability Applicability, string Reason);
