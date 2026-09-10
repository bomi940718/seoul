using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ExcelDna.Integration;
using WorkReport.Core.Config;
using WorkReport.Core.Models;
using WorkReport.Core.Parsing;
using WorkReport.Core.Reporting;

namespace WorkReport.AddIn.Services
{
    /// <summary>프로젝트 1건의 갱신 결과 요약 (본인/협업자 행 수).</summary>
    public class ProjectSummary
    {
        public string Number { get; set; }
        public string Name { get; set; }
        public int MyCount { get; set; }
        public int PartnerCount { get; set; }
        public int Total => MyCount + PartnerCount;
    }

    public class RefreshResult
    {
        public DateTime GeneratedAt { get; set; }
        public List<ProjectSummary> Projects { get; } = new List<ProjectSummary>();
        public List<string> Warnings { get; } = new List<string>();
        public List<UnregisteredKey> UnregisteredKeys { get; set; } = new List<UnregisteredKey>();

        /// <summary>프로젝트 이름 변경 등으로 더 이상 쓰이지 않아 정리된 옛 HTML 파일명.</summary>
        public List<string> RemovedFiles { get; } = new List<string>();
        public string LocalOutputDir { get; set; }
        /// <summary>대시보드가 실제로 있는 폴더 (NAS 성공 시 NAS, 실패 시 로컬).</summary>
        public string FinalOutputDir { get; set; }
        public bool NasCopyOk { get; set; }
        public string DashboardPath => Path.Combine(FinalOutputDir ?? LocalOutputDir ?? "", "index.html");
    }

    /// <summary>설정 미비 등 사용자가 조치해야 하는 상황 (스택트레이스 없이 메시지만 안내).</summary>
    public class RefreshBlockedException : Exception
    {
        public RefreshBlockedException(string message) : base(message) { }
    }

    /// <summary>
    /// 리포트 갱신 파이프라인:
    /// 활성 통합문서 저장 → projects.json 로드 → 일지 2개 파싱(3회 재시도)
    /// → %TEMP%\WorkReport 생성 → NAS 출력루트 복사 → 결과 반환.
    /// </summary>
    public static class RefreshService
    {
        private static int _running;

        /// <summary>파이프라인 내부의 Save()가 WorkbookAfterSave 자동 갱신을 재귀 호출하지 않도록 하는 플래그.</summary>
        public static bool IsRunning => Interlocked.CompareExchange(ref _running, 0, 0) == 1;

        public static string LocalStagingDir => Path.Combine(Path.GetTempPath(), "WorkReport");

        public static RefreshResult Run(bool saveActiveWorkbook)
        {
            if (Interlocked.Exchange(ref _running, 1) == 1)
                throw new RefreshBlockedException("리포트 갱신이 이미 실행 중입니다.");
            try
            {
                return RunCore(saveActiveWorkbook);
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }

        private static RefreshResult RunCore(bool saveActiveWorkbook)
        {
            var result = new RefreshResult { GeneratedAt = DateTime.Now };
            Logger.Info("===== 리포트 갱신 시작 =====");

            var settings = LocalSettings.Load();
            if (string.IsNullOrWhiteSpace(settings.SharedConfigDir))
                throw new RefreshBlockedException("공유설정 폴더가 설정되지 않았습니다.\n[워크리포트] 탭 → [설정]에서 경로를 지정하세요.");
            if (string.IsNullOrWhiteSpace(settings.MyJournalPath))
                throw new RefreshBlockedException("내 일지 xlsx 경로가 설정되지 않았습니다.\n[워크리포트] 탭 → [설정]에서 경로를 지정하세요.");

            if (saveActiveWorkbook) SaveActiveWorkbook(result.Warnings);

            ProjectRegistry registry;
            try
            {
                registry = ProjectRegistry.Load(settings.SharedConfigDir);
            }
            catch (Exception ex)
            {
                Logger.Error("projects.json 로드 실패", ex);
                throw new RefreshBlockedException(
                    $"프로젝트 등록 파일을 읽지 못했습니다.\n{ProjectRegistry.PathIn(settings.SharedConfigDir)}\n\n{ex.Message}");
            }
            Logger.Info($"projects.json 로드: {registry.Projects.Count}건 (활성 {registry.Projects.Count(p => p.Active)}건)");
            if (registry.Projects.Count == 0)
                result.Warnings.Add("등록된 프로젝트가 없습니다. [프로젝트 관리]에서 프로젝트를 등록하세요.");

            var allRecords = ParseJournals(settings, result.Warnings);

            var reportData = ReportBuilder.Build(allRecords, registry.Projects, registry.Groups);
            result.UnregisteredKeys = ReportBuilder.FindUnregisteredKeys(allRecords, registry.Projects);
            if (result.UnregisteredKeys.Count > 0)
                Logger.Warn("미등록 키: " + string.Join(", ", result.UnregisteredKeys.Select(k => $"{k.Number}({k.Count})")));

            foreach (var d in reportData)
            {
                result.Projects.Add(new ProjectSummary
                {
                    Number = d.Project.Number,
                    Name = d.Project.Name,
                    MyCount = d.Records.Count(r => r.SourceIndex == 0),
                    PartnerCount = d.Records.Count(r => r.SourceIndex == 1),
                });
            }

            // 로컬 스테이징 생성 (NAS 직접 스트림 쓰기 금지 원칙)
            var renderer = new HtmlReportRenderer();
            var authors = new[] { settings.MyAuthorName, settings.PartnerAuthorName };
            List<string> written = renderer.WriteAll(LocalStagingDir, reportData, authors, result.GeneratedAt);
            result.LocalOutputDir = LocalStagingDir;
            Logger.Info($"로컬 생성 완료: {written.Count}개 파일 → {LocalStagingDir}");

            result.FinalOutputDir = LocalStagingDir;
            result.NasCopyOk = false;
            if (string.IsNullOrWhiteSpace(settings.OutputRootDir))
            {
                result.Warnings.Add($"출력 루트가 설정되지 않아 로컬에만 생성했습니다: {LocalStagingDir}");
            }
            else
            {
                try
                {
                    Directory.CreateDirectory(settings.OutputRootDir);
                    foreach (var file in written)
                    {
                        // reports\ 하위 구조를 그대로 유지해 복사한다
                        string dest = Path.Combine(settings.OutputRootDir, RelativeToStaging(file));
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        File.Copy(file, dest, true);
                    }
                    result.FinalOutputDir = settings.OutputRootDir;
                    result.NasCopyOk = true;
                    Logger.Info($"출력 루트 복사 완료: {settings.OutputRootDir}");
                }
                catch (Exception ex)
                {
                    Logger.Error("출력 루트 복사 실패", ex);
                    result.Warnings.Add(
                        $"출력 루트({settings.OutputRootDir})에 복사하지 못했습니다: {ex.Message}\n로컬 결과를 확인하세요: {LocalStagingDir}");
                }
            }

            // 폴더별로 "이번에 넣은 파일" 목록을 모아 두었다가, 마지막에 옛 파일을 정리한다
            // (루트에는 index.html, reports\ 에는 프로젝트별 리포트가 들어간다)
            var filesByDir = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in written)
            {
                string sub = Path.GetDirectoryName(RelativeToStaging(file)) ?? "";
                var name = new[] { Path.GetFileName(file) };
                AddFiles(filesByDir, Path.Combine(LocalStagingDir, sub), name);
                if (result.NasCopyOk) AddFiles(filesByDir, Path.Combine(settings.OutputRootDir, sub), name);
            }

            // 프로젝트별 개별 출력 폴더(선택): 공통 루트와 별개로 추가 복사
            foreach (var d in reportData)
            {
                if (string.IsNullOrWhiteSpace(d.Project.OutputDir)) continue;
                try
                {
                    Directory.CreateDirectory(d.Project.OutputDir);
                    File.Copy(Path.Combine(LocalStagingDir, HtmlReportRenderer.ReportsDirName, d.FileName),
                        Path.Combine(d.Project.OutputDir, d.FileName), true);
                    AddFiles(filesByDir, d.Project.OutputDir, new[] { d.FileName });
                    Logger.Info($"개별 출력 복사: {d.Project.Number} → {d.Project.OutputDir}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"개별 출력 복사 실패: {d.Project.Number}", ex);
                    result.Warnings.Add($"{d.Project.Number} 개별 출력 폴더({d.Project.OutputDir}) 복사 실패: {ex.Message}");
                }
            }

            // 프로젝트 이름/넘버가 바뀌면 옛 이름의 HTML이 남으므로, 우리가 만든 것만 골라 지운다
            foreach (var pair in filesByDir)
            {
                try
                {
                    var removed = OutputManifest.Prune(pair.Key, pair.Value, result.Warnings);
                    if (removed.Count == 0) continue;
                    result.RemovedFiles.AddRange(removed);
                    Logger.Info($"옛 파일 정리: {pair.Key} → {string.Join(", ", removed)}");
                }
                catch (Exception ex)
                {
                    // 정리는 부가 기능이므로 실패해도 갱신 자체를 막지 않는다
                    Logger.Error($"옛 파일 정리 실패: {pair.Key}", ex);
                    result.Warnings.Add($"옛 파일 정리 중 문제가 있었습니다({pair.Key}): {ex.Message}");
                }
            }

            Logger.Info($"===== 리포트 갱신 완료: 프로젝트 {result.Projects.Count}건, " +
                        $"정리 {result.RemovedFiles.Count}건, 경고 {result.Warnings.Count}건 =====");
            return result;
        }

        /// <summary>%TEMP%\WorkReport 기준 상대 경로 (예: "index.html", "reports\ABC.html").</summary>
        private static string RelativeToStaging(string fullPath)
        {
            string root = LocalStagingDir.TrimEnd('\\', '/');
            if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return fullPath.Substring(root.Length).TrimStart('\\', '/');
            return Path.GetFileName(fullPath);
        }

        private static void AddFiles(Dictionary<string, HashSet<string>> map, string dir, IEnumerable<string> names)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            HashSet<string> set;
            if (!map.TryGetValue(dir, out set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[dir] = set;
            }
            foreach (string n in names) set.Add(n);
        }

        /// <summary>
        /// 설정의 일지 2개를 파싱해 레코드를 합친다 (프로젝트 관리 창의 미등록 키 스캔에서도 사용).
        /// 읽기 실패는 경고로만 남기고 계속 진행한다.
        /// </summary>
        public static List<WorkRecord> ParseJournals(LocalSettings settings, List<string> warnings)
        {
            string sheet = settings.EffectiveSheetName;
            var allRecords = new List<WorkRecord>();
            var sources = new[]
            {
                new { Path = settings.MyJournalPath, Author = settings.MyAuthorName, Index = 0, Label = "내 일지" },
                new { Path = settings.PartnerJournalPath, Author = settings.PartnerAuthorName, Index = 1, Label = "협업자 일지" },
            };
            foreach (var src in sources)
            {
                if (string.IsNullOrWhiteSpace(src.Path))
                {
                    warnings.Add($"{src.Label} 경로가 비어 있어 건너뜁니다.");
                    continue;
                }
                var parsed = ParseWithRetry(src.Path, sheet, src.Author, src.Index, src.Label, warnings);
                if (parsed != null)
                {
                    allRecords.AddRange(parsed.Records);
                    foreach (var w in parsed.Warnings) warnings.Add($"{src.Label}: {w}");
                    Logger.Info($"{src.Label} 파싱 완료: {parsed.Records.Count}건 (시트 {parsed.SheetName})");
                }
            }
            return allRecords;
        }

        private static ParseResult ParseWithRetry(string path, string sheet, string author, int index,
            string label, List<string> warnings)
        {
            const int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var parser = new JournalParser();
                    return parser.ParseFile(path, sheet, author, index);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"{label} 파싱 실패 (시도 {attempt}/{maxAttempts}): {ex.Message}");
                    if (attempt == maxAttempts)
                    {
                        warnings.Add($"{label}({path}) 읽기 실패로 건너뜁니다: {ex.Message}");
                        return null;
                    }
                    Thread.Sleep(1000);
                }
            }
            return null;
        }

        /// <summary>Excel COM은 이 용도(활성 통합문서 저장)로만 사용한다.</summary>
        private static void SaveActiveWorkbook(List<string> warnings)
        {
            try
            {
                dynamic app = ExcelDnaUtil.Application;
                dynamic wb = app.ActiveWorkbook;
                if (wb == null) return;
                if (!(bool)wb.Saved)
                {
                    wb.Save();
                    Logger.Info($"활성 통합문서 저장: {(string)wb.Name}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("활성 통합문서 저장 실패: " + ex.Message);
                warnings.Add("활성 통합문서를 저장하지 못했습니다 (읽기 전용 등). 마지막 저장 시점 기준으로 생성합니다.");
            }
        }
    }
}
