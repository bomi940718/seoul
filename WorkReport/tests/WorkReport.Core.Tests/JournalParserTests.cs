using System;
using System.Linq;
using WorkReport.Core.Parsing;
using Xunit;

namespace WorkReport.Core.Tests
{
    public class JournalParserTests
    {
        private readonly JournalParser _parser = new JournalParser();

        [Fact]
        public void 헤더와_열을_DAY_앵커로_탐지한다()
        {
            using var wb = TestWorkbook.Create();
            var ws = wb.Worksheet("2026");
            TestWorkbook.AddRow(ws, 7, new DateTime(2026, 1, 5), "테스트", "TEST-1", "내용");

            var r = _parser.Parse(wb, "2026", "본인", 0);

            Assert.Equal(4, r.HeaderRow);
            Assert.Equal(3, r.Columns.Day);
            Assert.Equal(6, r.Columns.Regiment);
            Assert.Equal(9, r.Columns.ProjectNumber);
            Assert.Equal(13, r.Columns.Note);
        }

        [Fact]
        public void 날짜_시리얼_숫자를_DateTime으로_변환한다()
        {
            using var wb = TestWorkbook.Create();
            var ws = wb.Worksheet("2026");
            // 46010 = 2025-12-19
            TestWorkbook.AddRow(ws, 7, 46010.0, "테스트", "TEST-1", "시리얼 날짜 행");

            var r = _parser.Parse(wb, "2026", "본인", 0);

            Assert.Single(r.Records);
            Assert.Equal(new DateTime(2025, 12, 19), r.Records[0].Date);
        }

        [Fact]
        public void 빈_날짜는_위_행에서_상속한다()
        {
            using var wb = TestWorkbook.Create();
            var ws = wb.Worksheet("2026");
            TestWorkbook.AddRow(ws, 7, new DateTime(2026, 1, 5), "테스트", "TEST-1", "첫 행");
            TestWorkbook.AddRow(ws, 8, null, "테스트", "TEST-1", "날짜 없는 행");

            var r = _parser.Parse(wb, "2026", "본인", 0);

            Assert.Equal(2, r.Records.Count);
            Assert.Equal(r.Records[0].Date, r.Records[1].Date);
            Assert.True(r.Records[1].DateInherited);
        }

        [Fact]
        public void 서브헤더와_빈_행과_날짜만_있는_행은_건너뛴다()
        {
            using var wb = TestWorkbook.Create();
            var ws = wb.Worksheet("2026");
            // 6행은 빈 행, 7행 데이터, 8행 날짜만, 9행 데이터
            TestWorkbook.AddRow(ws, 7, new DateTime(2026, 1, 5), "테스트", "TEST-1", "내용 있음");
            ws.Cell(8, 3).Value = new DateTime(2026, 1, 6);
            TestWorkbook.AddRow(ws, 9, new DateTime(2026, 1, 7), "테스트", "TEST-1", "다음 내용");

            var r = _parser.Parse(wb, "2026", "본인", 0);

            Assert.Equal(2, r.Records.Count);
            Assert.All(r.Records, rec => Assert.False(string.IsNullOrEmpty(rec.Descriptions)));
        }

        [Fact]
        public void 날짜만_있는_행의_날짜도_상속_기준으로_쓴다()
        {
            using var wb = TestWorkbook.Create();
            var ws = wb.Worksheet("2026");
            ws.Cell(7, 3).Value = new DateTime(2026, 2, 1); // 날짜만 있는 행
            TestWorkbook.AddRow(ws, 8, null, "테스트", "TEST-1", "날짜 상속 대상");

            var r = _parser.Parse(wb, "2026", "본인", 0);

            Assert.Single(r.Records);
            Assert.Equal(new DateTime(2026, 2, 1), r.Records[0].Date);
        }

        [Fact]
        public void 시트명은_대괄호_유무를_무시하고_찾는다()
        {
            // 엑셀 시트명에는 대괄호가 허용되지 않으므로, 설정에 "[2026]"이라 적어도 실제 시트 "2026"을 찾아야 한다
            using var wb = TestWorkbook.Create("2026");
            var ws = wb.Worksheet("2026");
            TestWorkbook.AddRow(ws, 7, new DateTime(2026, 1, 5), "테스트", "TEST-1", "내용");

            var r = _parser.Parse(wb, "[2026]", "본인", 0);
            Assert.Single(r.Records);
        }

        [Fact]
        public void 없는_시트면_예외에_존재_시트명을_담는다()
        {
            using var wb = TestWorkbook.Create("2026");
            var ex = Assert.Throws<InvalidOperationException>(() => _parser.Parse(wb, "2030", "본인", 0));
            Assert.Contains("2026", ex.Message);
        }

        [Fact]
        public void 셀_줄바꿈과_작성자_태깅을_유지한다()
        {
            using var wb = TestWorkbook.Create();
            var ws = wb.Worksheet("2026");
            TestWorkbook.AddRow(ws, 7, new DateTime(2026, 1, 5), "테스트", "TEST-1", "1줄\n2줄\n3줄");

            var r = _parser.Parse(wb, "2026", "나", 1);

            Assert.Contains("\n", r.Records[0].Descriptions);
            Assert.Equal("나", r.Records[0].Author);
            Assert.Equal(1, r.Records[0].SourceIndex);
        }
    }

    public class KeyNormalizerTests
    {
        [Theory]
        [InlineData("BRANDING-1", "branding-1", true)]
        [InlineData(" BRANDING-1 ", "BRANDING-1", true)]
        [InlineData("General\nManagement", "General Management", true)]   // 실측: 키 내부 줄바꿈
        [InlineData("평택 방축리 427\n용도변경", "평택 방축리 427 용도변경", true)]
        [InlineData("BRANDING-1", "BRANDING-2", false)]
        [InlineData("", "", false)]
        public void 매칭_규칙(string a, string b, bool expected)
            => Assert.Equal(expected, KeyNormalizer.Matches(a, b));
    }
}
