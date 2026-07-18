using System;
using System.IO;
using ExcelDna.Integration;
using WorkReport.AddIn.Services;
using WorkReport.Core.Config;
using Excel = Microsoft.Office.Interop.Excel;

namespace WorkReport.AddIn
{
    /// <summary>애드인 진입점. WorkbookAfterSave 구독(저장 시 자동 갱신)을 담당한다.</summary>
    public class AddIn : IExcelAddIn
    {
        private static Excel.Application _app;

        public void AutoOpen()
        {
            try
            {
                _app = (Excel.Application)ExcelDnaUtil.Application;
                _app.WorkbookAfterSave += OnWorkbookAfterSave;
                Logger.Info("애드인 로드 완료 (버전 " + typeof(AddIn).Assembly.GetName().Version + ")");
            }
            catch (Exception ex)
            {
                Logger.Error("AutoOpen 실패", ex);
            }
        }

        public void AutoClose()
        {
            try
            {
                if (_app != null) _app.WorkbookAfterSave -= OnWorkbookAfterSave;
                _app = null;
                Logger.Info("애드인 종료");
            }
            catch (Exception ex)
            {
                Logger.Error("AutoClose 실패", ex);
            }
        }

        /// <summary>
        /// 저장 시 자동 갱신: 저장된 파일이 설정의 "내 일지"이고 autoRefreshOnSave=true일 때만,
        /// 결과 창 없이 조용히 실행한다. 갱신 파이프라인 자체의 저장은 IsRunning으로 걸러 재귀를 막는다.
        /// </summary>
        private static void OnWorkbookAfterSave(Excel.Workbook wb, bool success)
        {
            try
            {
                if (!success || RefreshService.IsRunning) return;

                var settings = LocalSettings.Load();
                if (!settings.AutoRefreshOnSave) return;
                if (string.IsNullOrWhiteSpace(settings.MyJournalPath)) return;

                string saved, mine;
                try
                {
                    saved = Path.GetFullPath(wb.FullName);
                    mine = Path.GetFullPath(settings.MyJournalPath);
                }
                catch
                {
                    return; // 경로 비교 불가(신규 문서 등)면 무시
                }
                if (!string.Equals(saved, mine, StringComparison.OrdinalIgnoreCase)) return;

                Logger.Info("내 일지 저장 감지 → 자동 갱신 예약");
                ExcelAsyncUtil.QueueAsMacro(() =>
                {
                    try
                    {
                        var result = RefreshService.Run(saveActiveWorkbook: false);
                        Logger.Info($"자동 갱신 완료: 프로젝트 {result.Projects.Count}건, 경고 {result.Warnings.Count}건");
                    }
                    catch (RefreshBlockedException ex)
                    {
                        Logger.Warn("자동 갱신 중단: " + ex.Message);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("자동 갱신 실패", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error("WorkbookAfterSave 처리 실패", ex);
            }
        }
    }
}
