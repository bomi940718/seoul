using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WorkReport.Core.Reporting;
using Xunit;

namespace WorkReport.Core.Tests
{
    public class OutputManifestTests : IDisposable
    {
        private readonly string _dir;

        public OutputManifestTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "WorkReportTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { /* 정리 실패는 무시 */ }
        }

        private void Touch(string name) => File.WriteAllText(Path.Combine(_dir, name), "x");
        private bool Exists(string name) => File.Exists(Path.Combine(_dir, name));

        [Fact]
        public void 첫_실행에는_아무것도_지우지_않고_목록만_남긴다()
        {
            Touch("A_가.html");
            var warnings = new List<string>();

            var removed = OutputManifest.Prune(_dir, new[] { "A_가.html", "index.html" }, warnings);

            Assert.Empty(removed);
            Assert.Empty(warnings);
            Assert.True(Exists("A_가.html"));
            Assert.Equal(new[] { "A_가.html", "index.html" },
                OutputManifest.Load(_dir).Files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        }

        [Fact]
        public void 이름이_바뀌면_옛_파일을_지운다()
        {
            Touch("A_옛이름.html");
            Touch("index.html");
            OutputManifest.Prune(_dir, new[] { "A_옛이름.html", "index.html" }, new List<string>());

            // 프로젝트 이름 변경 → 새 파일명으로 생성됨
            Touch("A_새이름.html");
            var removed = OutputManifest.Prune(_dir, new[] { "A_새이름.html", "index.html" }, new List<string>());

            Assert.Equal(new[] { "A_옛이름.html" }, removed);
            Assert.False(Exists("A_옛이름.html"));
            Assert.True(Exists("A_새이름.html"));
            Assert.True(Exists("index.html"));
        }

        [Fact]
        public void 우리가_만들지_않은_파일은_절대_지우지_않는다()
        {
            Touch("A_가.html");
            OutputManifest.Prune(_dir, new[] { "A_가.html" }, new List<string>());

            // 사용자가 같은 폴더에 둔 파일들
            Touch("사용자메모.html");
            Touch("보관용.xlsx");

            var removed = OutputManifest.Prune(_dir, new[] { "B_나.html" }, new List<string>());

            Assert.Equal(new[] { "A_가.html" }, removed);   // 우리가 만든 것만
            Assert.True(Exists("사용자메모.html"));
            Assert.True(Exists("보관용.xlsx"));
        }

        [Fact]
        public void 이미_지워진_파일은_경고없이_넘어간다()
        {
            Touch("A_가.html");
            OutputManifest.Prune(_dir, new[] { "A_가.html" }, new List<string>());
            File.Delete(Path.Combine(_dir, "A_가.html"));   // 사용자가 직접 지운 경우

            var warnings = new List<string>();
            var removed = OutputManifest.Prune(_dir, new[] { "B_나.html" }, warnings);

            Assert.Empty(removed);
            Assert.Empty(warnings);
        }

        [Fact]
        public void 목록_파일이_깨져_있으면_삭제하지_않고_새로_기록한다()
        {
            Touch("A_가.html");
            File.WriteAllText(OutputManifest.PathIn(_dir), "{ 깨진 내용");

            var removed = OutputManifest.Prune(_dir, new[] { "B_나.html" }, new List<string>());

            Assert.Empty(removed);
            Assert.True(Exists("A_가.html"));
            Assert.Equal(new[] { "B_나.html" }, OutputManifest.Load(_dir).Files);
        }

        [Fact]
        public void 목록에_경로가_섞여_있어도_폴더_밖_파일은_건드리지_않는다()
        {
            string outside = Path.Combine(Path.GetTempPath(), "WorkReportOutside_" + Guid.NewGuid().ToString("N") + ".html");
            File.WriteAllText(outside, "x");
            try
            {
                new OutputManifest { Files = new List<string> { @"..\" + Path.GetFileName(outside), outside } }
                    .Save(_dir);

                var removed = OutputManifest.Prune(_dir, new[] { "index.html" }, new List<string>());

                Assert.Empty(removed);
                Assert.True(File.Exists(outside));
            }
            finally
            {
                File.Delete(outside);
            }
        }
    }
}
