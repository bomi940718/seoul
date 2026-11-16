using LawReview.Core.Municipal;
using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 안양시 고시공고 지구단위계획 조회 검증.
///
/// 서울과 달리 경기도 시·군에는 지구단위계획 전용 포털이 없어 **시청 고시공고 게시판**을 읽는다.
/// 픽스처는 실제 응답의 표 블록 그대로다(2026-11 캡처, "호계동" 제목 검색 / 고시 제2026-135호 상세).
/// 게시판 서식이 바뀌면 조용히 0건이 되므로 실제 HTML로 고정해 둔다.
/// </summary>
public class AnyangNoticeClientTests
{
    private static string Fixture(string name) => File.ReadAllText(ChecklistTests.FindRepoFile(
        Path.Combine("tests", "LawReview.Core.Tests", "Fixtures", name)));

    [Fact]
    public void 목록에서_고시번호_제목_부서_등록일을_읽는다()
    {
        var rows = AnyangNoticeClient.ParseList(Fixture("anyang_notice_list.html"));

        Assert.NotEmpty(rows);
        var first = rows[0];
        Assert.Equal("고시 제2026-138호", first.NoticeNo);
        Assert.Equal("84160", first.Id);
        Assert.Equal("도시계획과", first.Dept);
        Assert.Equal("2026-07-10", first.Date);
        Assert.StartsWith("호계동 범내지구", first.Title);
    }

    [Fact]
    public void 상세에서_온전한_제목과_첨부파일_링크를_읽는다()
    {
        // 목록 제목은 "…결정(변경) 정정..."처럼 잘려 오므로 상세에서 다시 가져와야 한다.
        var (title, file) = AnyangNoticeClient.ParseDetail(Fixture("anyang_notice_view.html"));

        Assert.Equal("호계동 범내지구 도시관리계획(지구단위계획구역 및 계획) 결정(변경) 고시", title);
        Assert.StartsWith("https://eminwon.anyang.go.kr/", file);
        Assert.Contains("FileDown.jsp", file);
        Assert.DoesNotContain("&amp;", file);   // HTML 엔티티는 풀어서 내려받을 수 있어야 한다
    }

    [Theory]
    // 흔한 형태 — 행위 표기 앞까지가 구역명
    [InlineData("호계동 범내지구 도시관리계획(지구단위계획구역 및 계획) 결정(변경) 고시", "호계동 범내지구")]
    [InlineData("평촌신도시 도시관리계획(지구단위계획) 결정(변경) 고시", "평촌신도시")]
    [InlineData("안양 운동장사거리 일원 도시관리계획(지구단위계획구역) 결정 및 지형도면 고시",
                "안양 운동장사거리 일원")]
    // 행위 표기로 시작하는 제목 — 구역명이 괄호 안에 있다.
    // 이 경우를 따로 보지 않으면 괄호 한가운데서 잘려 "도시관리계획(호계동 범내지구"가 된다.
    [InlineData("도시관리계획(호계동 범내지구 지구단위계획) 결정(경미한 변경) 고시", "호계동 범내지구")]
    [InlineData("실시계획 변경인가(관양동 공업지역 지구단위계획 기반시설2차)", "관양동 공업지역")]
    public void 고시_제목에서_구역명을_뽑는다(string title, string expected)
        => Assert.Equal(expected, AnyangNoticeClient.ZoneNameFrom(title));

    [Fact]
    public void 담당_지자체를_기초까지_보고_가른다()
    {
        using var http = new HttpClient();
        var client = new AnyangNoticeClient(http);

        Assert.True(client.Matches("경기도", "안양시"));
        Assert.True(client.Matches("경기도", "안양시 동안구"));
        // 경기도의 다른 시·군은 각자 포털이 다르다 — 안양 고시를 인용하면 안 된다.
        Assert.False(client.Matches("경기도", "성남시 분당구"));
        Assert.False(client.Matches("서울특별시", "관악구"));
    }
}
