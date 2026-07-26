using System;
using System.Collections.Generic;
using System.Linq;
using WorkReport.Core.Models;
using WorkReport.Core.Reporting;
using Xunit;

namespace WorkReport.Core.Tests
{
    public class GroupAndAliasTests
    {
        private static WorkRecord Rec(string projectName, string projectNumber, int row = 1)
            => new WorkRecord
            {
                Date = new DateTime(2026, 1, 1),
                ProjectName = projectName,
                ProjectNumber = projectNumber,
                Descriptions = "내용",
                SourceRow = row,
            };

        private static readonly List<string> Groups =
            new List<string> { "ARCHITECTURE", "INTERIOR", "EDUCATION", "BRANDING", "MANAGEMENT", "PLANNING" };

        // ---------- 그룹 자동 배정 ----------

        [Theory]
        [InlineData("BRANDING-1", "BRANDING")]          // 접미사가 붙어도
        [InlineData("General Management", "MANAGEMENT")] // 대소문자 무시
        [InlineData("Planning project", "PLANNING")]
        [InlineData("ARCHITECTURE", "ARCHITECTURE")]
        [InlineData("미유헌(美留軒)", null)]                 // 해당 없음
        public void 값_하나에서_그룹을_찾는다(string value, string expected)
        {
            Assert.Equal(expected, GroupResolver.MatchOne(value, Groups));
        }

        [Fact]
        public void 한_글자_그룹은_아무데나_걸리지_않게_무시한다()
        {
            Assert.Null(GroupResolver.MatchOne("ARCHITECTURE", new[] { "A" }));
        }

        [Fact]
        public void 수동_지정한_그룹이_항상_이긴다()
        {
            var project = new ProjectInfo { Number = "IN_BEGINNING", Group = "내부용" };
            var records = new[] { Rec("INTERIOR", "IN_BEGINNING") };

            Assert.Equal("내부용", GroupResolver.Resolve(project, records, Groups));
        }

        [Fact]
        public void H열에_없으면_I열에서_그룹을_찾는다()
        {
            // H·I가 뒤바뀐 기록: 카테고리가 I열에 들어가 있다
            var project = new ProjectInfo { Number = "ARCHITECTURE" };
            var records = new[] { Rec("용도변경", "ARCHITECTURE") };

            Assert.Equal("ARCHITECTURE", GroupResolver.Resolve(project, records, Groups));
        }

        [Fact]
        public void 여러_그룹이_걸리면_많이_나온_쪽이_이긴다()
        {
            var project = new ProjectInfo { Number = "X" };
            var records = new[]
            {
                Rec("EDUCATION", "X"), Rec("EDUCATION", "X"), Rec("EDUCATION", "X"),
                Rec("BRANDING", "X"),
            };

            Assert.Equal("EDUCATION", GroupResolver.Resolve(project, records, Groups));
        }

        [Fact]
        public void 그룹을_못_찾으면_ETC()
        {
            var project = new ProjectInfo { Number = "일반관리" };
            var records = new[] { Rec("SOWOOZOO", "일반관리") };

            Assert.Equal("ETC", GroupResolver.Resolve(project, records, Groups));
        }

        [Fact]
        public void 그룹_목록이_비어_있으면_ETC()
        {
            var project = new ProjectInfo { Number = "X" };
            Assert.Equal("ETC", GroupResolver.Resolve(project, new[] { Rec("EDUCATION", "X") }, new List<string>()));
        }

        // ---------- 넘버 별칭 ----------

        [Fact]
        public void 별칭으로_뒤바뀐_기록까지_한_프로젝트로_모은다()
        {
            var project = new ProjectInfo
            {
                Number = "IN_BEGINNING",
                Name = "인테리어",
                Aliases = new List<string> { "INTERIOR" },
            };
            var records = new[]
            {
                Rec("INTERIOR", "IN_BEGINNING", 1),
                Rec("IN_BEGINNING", "INTERIOR", 2),
                Rec("무관", "OTHER", 3),
            };

            var built = ReportBuilder.Build(records, new[] { project }, Groups);

            Assert.Single(built);
            Assert.Equal(2, built[0].Records.Count);
            Assert.Equal("INTERIOR", built[0].Group);
        }

        [Fact]
        public void 별칭도_등록된_것으로_보아_미등록_목록에서_빠진다()
        {
            var project = new ProjectInfo
            {
                Number = "IN_BEGINNING",
                Aliases = new List<string> { "INTERIOR" },
            };
            var records = new[] { Rec("INTERIOR", "IN_BEGINNING"), Rec("IN_BEGINNING", "INTERIOR") };

            Assert.Empty(ReportBuilder.FindUnregisteredKeys(records, new[] { project }));
        }

        // ---------- 저장·로드 ----------

        [Fact]
        public void 그룹_목록과_별칭이_저장하고_다시_읽어도_유지된다()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WRReg_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                var reg = WorkReport.Core.Config.ProjectRegistry.Load(dir);
                reg.Groups = new List<string> { "ARCHITECTURE", "INTERIOR" };
                reg.Projects.Add(new ProjectInfo
                {
                    Number = "IN_BEGINNING",
                    Name = "인테리어",
                    Aliases = new List<string> { "INTERIOR" },
                });
                reg.Save();

                var again = WorkReport.Core.Config.ProjectRegistry.Load(dir);

                Assert.Equal(new[] { "ARCHITECTURE", "INTERIOR" }, again.Groups);
                Assert.Equal(new[] { "INTERIOR" }, again.Projects.Single().Aliases);
                Assert.Equal(new[] { "IN_BEGINNING", "INTERIOR" }, again.Projects.Single().AllNumbers);
            }
            finally
            {
                try { System.IO.Directory.Delete(dir, true); } catch { }
            }
        }

        // ---------- 뒤바뀐 쌍 탐지 ----------

        [Fact]
        public void 뒤바뀐_쌍을_찾고_많은_쪽을_기준으로_삼는다()
        {
            var records = new List<WorkRecord>();
            for (int i = 0; i < 61; i++) records.Add(Rec("INTERIOR", "IN_BEGINNING", i));
            for (int i = 0; i < 46; i++) records.Add(Rec("IN_BEGINNING", "INTERIOR", i));

            var pairs = ReportBuilder.FindMirrorPairs(records);

            Assert.Single(pairs);
            Assert.Equal("IN_BEGINNING", pairs[0].Primary);   // I열 기준 61건
            Assert.Equal(61, pairs[0].PrimaryCount);
            Assert.Equal("INTERIOR", pairs[0].Secondary);     // I열 기준 46건
            Assert.Equal(46, pairs[0].SecondaryCount);
            Assert.Equal(107, pairs[0].Total);
        }

        [Fact]
        public void 뒤바뀌지_않은_조합은_쌍으로_보지_않는다()
        {
            var records = new[]
            {
                Rec("SOWOOZOO", "일반관리"),
                Rec("ZERO", "개인관리"),
            };

            Assert.Empty(ReportBuilder.FindMirrorPairs(records));
        }

        [Fact]
        public void 여러_단어_키도_쌍_판정이_정확하다()
        {
            var records = new[]
            {
                Rec("평택 방축리 427 용도변경", "Planning project"),
                Rec("Planning project", "평택 방축리 427 용도변경"),
            };

            var pairs = ReportBuilder.FindMirrorPairs(records);

            Assert.Single(pairs);
            Assert.Equal(2, pairs[0].Total);
        }
    }
}
