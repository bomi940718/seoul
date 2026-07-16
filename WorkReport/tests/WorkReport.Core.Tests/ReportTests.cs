using System;
using System.Collections.Generic;
using System.Linq;
using WorkReport.Core.Models;
using WorkReport.Core.Reporting;
using Xunit;

namespace WorkReport.Core.Tests
{
    public class ReportBuilderTests
    {
        private static WorkRecord Rec(string date, string num, int source = 0, int row = 1, string status = "O")
            => new WorkRecord
            {
                Date = DateTime.Parse(date),
                ProjectNumber = num,
                ProjectName = "이름",
                Descriptions = "내용",
                Status = status,
                Author = source == 0 ? "본인" : "협업자",
                SourceIndex = source,
                SourceRow = row,
            };

        [Fact]
        public void 활성_프로젝트만_병합하고_정렬한다()
        {
            var projects = new List<ProjectInfo>
            {
                new ProjectInfo { Number = "A-1", Name = "에이", Active = true },
                new ProjectInfo { Number = "B-1", Name = "비", Active = false },
            };
            var records = new[]
            {
                Rec("2026-01-10", "A-1", source: 1, row: 5),
                Rec("2026-01-10", "a-1", source: 0, row: 9),   // 대소문자 무시 매칭
                Rec("2026-01-05", "A-1", source: 0, row: 20),
                Rec("2026-01-07", "B-1"),                       // 비활성 → 제외
                Rec("2026-01-07", "C-1"),                       // 미등록 → 제외
            };

            var built = ReportBuilder.Build(records, projects);

            var a = Assert.Single(built);
            Assert.Equal(3, a.Records.Count);
            Assert.Equal(new DateTime(2026, 1, 5), a.Records[0].Date);
            // 같은 날짜: 본인(0) 먼저
            Assert.Equal(0, a.Records[1].SourceIndex);
            Assert.Equal(1, a.Records[2].SourceIndex);
        }

        [Fact]
        public void 미등록_키를_건수와_함께_찾는다()
        {
            var projects = new List<ProjectInfo> { new ProjectInfo { Number = "A-1", Name = "에이" } };
            var records = new[]
            {
                Rec("2026-01-05", "A-1"),
                Rec("2026-01-06", "신규키"),
                Rec("2026-01-07", "신규키 "),
                Rec("2026-01-08", ""),
            };

            var keys = ReportBuilder.FindUnregisteredKeys(records, projects);

            var k = Assert.Single(keys);
            Assert.Equal("신규키", k.Number);
            Assert.Equal(2, k.Count);
        }

        [Theory]
        [InlineData("BRANDING-1", "스포츠 브라", "BRANDING-1_스포츠 브라.html")]
        [InlineData("A/B:C", "이름*?", "A_B_C_이름__.html")]
        [InlineData("General\nManagement", "관리", "General_Management_관리.html")]
        public void 파일명_불가_문자를_치환한다(string num, string name, string expected)
            => Assert.Equal(expected, ReportBuilder.MakeFileName(new ProjectInfo { Number = num, Name = name }));
    }

    public class HtmlRendererTests
    {
        private static ProjectReportData Data()
        {
            var p = new ProjectInfo { Number = "TEST-1", Name = "테스트" };
            return new ProjectReportData
            {
                Project = p,
                FileName = ReportBuilder.MakeFileName(p),
                Records = new List<WorkRecord>
                {
                    new WorkRecord
                    {
                        Date = new DateTime(2026, 1, 5),
                        Descriptions = "링크 https://example.com 포함\n둘째 줄 </script> 닫기 시도",
                        Status = "O", Author = "본인", SourceIndex = 0, SourceRow = 7,
                        Regiment = "기관", Outsider = "담당", Mail = "", Schedule = "", Plan = "", Note = "",
                        ProjectName = "테스트", ProjectNumber = "TEST-1",
                    },
                },
            };
        }

        [Fact]
        public void 리포트는_self_contained_이고_데이터를_임베드한다()
        {
            var html = new HtmlReportRenderer()
                .RenderProjectReport(Data(), new[] { "본인", "협업자" }, new DateTime(2026, 7, 16, 10, 0, 0));

            Assert.StartsWith("<!DOCTYPE html>", html);
            Assert.DoesNotContain("@@", html);                 // 토큰 전부 치환됨
            Assert.DoesNotContain("http://cdn", html);
            Assert.DoesNotContain("<link", html);              // 외부 리소스 없음
            Assert.Contains("TEST-1", html);
            Assert.Contains("example.com", html);
            // 데이터 내 </script> 는 이스케이프되어 스크립트 블록을 깨지 않는다
            Assert.DoesNotContain("</script> 닫기", html);
        }

        [Fact]
        public void 인덱스는_카드_데이터와_상대_링크를_담는다()
        {
            var d = Data();
            var html = new HtmlReportRenderer().RenderIndex(new List<ProjectReportData> { d }, DateTime.Now);

            Assert.Contains("TEST-1_테스트.html", html);
            Assert.DoesNotContain("@@", html);
        }

        [Fact]
        public void 전체_쓰기는_프로젝트별_파일과_index를_생성한다()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wr-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var files = new HtmlReportRenderer()
                    .WriteAll(dir, new List<ProjectReportData> { Data() }, new[] { "본인", "협업자" }, DateTime.Now);

                Assert.Equal(2, files.Count);
                Assert.True(System.IO.File.Exists(System.IO.Path.Combine(dir, "index.html")));
            }
            finally { System.IO.Directory.Delete(dir, true); }
        }
    }
}
