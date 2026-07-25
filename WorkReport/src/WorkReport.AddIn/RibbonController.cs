using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using ExcelDna.Integration.CustomUI;
using WorkReport.AddIn.Services;
using WorkReport.AddIn.UI;
using WorkReport.Core.Config;

namespace WorkReport.AddIn
{
    /// <summary>[워크리포트] 리본 탭: 리포트 갱신 / 대시보드 열기 / 프로젝트 관리 / 설정 / 로그 열기.</summary>
    [ComVisible(true)]
    public class RibbonController : ExcelRibbon
    {
        public override string GetCustomUI(string ribbonId)
        {
            return @"
<customUI xmlns='http://schemas.microsoft.com/office/2009/07/customui'>
  <ribbon>
    <tabs>
      <tab id='wrTab' label='워크리포트'>
        <group id='wrGroupReport' label='리포트'>
          <button id='wrRefresh' label='리포트 갱신' imageMso='Refresh' size='large'
                  onAction='OnRefresh'
                  screentip='일지 2개를 병합해 프로젝트별 HTML 리포트와 대시보드를 다시 생성합니다.'/>
          <button id='wrDashboard' label='대시보드 열기' imageMso='HyperlinkInsert' size='large'
                  onAction='OnOpenDashboard'
                  screentip='index.html 대시보드를 기본 브라우저로 엽니다.'/>
        </group>
        <group id='wrGroupManage' label='관리'>
          <button id='wrProjects' label='프로젝트 관리' imageMso='TableInsert' size='large'
                  onAction='OnManageProjects'
                  screentip='projects.json의 프로젝트 목록(넘버·이름·그룹·상태·활성)을 편집합니다.'/>
          <button id='wrSettings' label='설정' imageMso='PropertySheet' size='large'
                  onAction='OnSettings'
                  screentip='일지 경로·작성자명·공유설정 폴더·출력 루트 등 PC별 설정을 편집합니다.'/>
          <button id='wrLog' label='로그 열기' imageMso='FileProperties' size='normal'
                  onAction='OnOpenLog'
                  screentip='오늘 로그 파일을 엽니다.'/>
        </group>
      </tab>
    </tabs>
  </ribbon>
</customUI>";
        }

        public void OnRefresh(IRibbonControl control)
        {
            try
            {
                var result = RefreshService.Run(saveActiveWorkbook: true);
                WindowHelper.ShowOverExcel(new ResultWindow(result));
            }
            catch (RefreshBlockedException ex)
            {
                MessageBox.Show(ex.Message, "워크리포트", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                Logger.Error("리포트 갱신 실패", ex);
                MessageBox.Show(
                    "리포트 갱신 중 오류가 발생했습니다.\n\n" + ex.Message + "\n\n자세한 내용은 [로그 열기]로 확인하세요.",
                    "워크리포트", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void OnOpenDashboard(IRibbonControl control)
        {
            try
            {
                var settings = LocalSettings.Load();
                string nasIndex = string.IsNullOrWhiteSpace(settings.OutputRootDir)
                    ? null : Path.Combine(settings.OutputRootDir, "index.html");
                string localIndex = Path.Combine(RefreshService.LocalStagingDir, "index.html");

                string target = nasIndex != null && File.Exists(nasIndex) ? nasIndex
                    : File.Exists(localIndex) ? localIndex : null;
                if (target == null)
                {
                    MessageBox.Show("대시보드가 아직 없습니다. 먼저 [리포트 갱신]을 실행하세요.",
                        "워크리포트", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                Process.Start(target);
                Logger.Info("대시보드 열기: " + target);
            }
            catch (Exception ex)
            {
                Logger.Error("대시보드 열기 실패", ex);
                MessageBox.Show("대시보드를 열지 못했습니다: " + ex.Message,
                    "워크리포트", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void OnManageProjects(IRibbonControl control)
        {
            try
            {
                var settings = LocalSettings.Load();
                if (string.IsNullOrWhiteSpace(settings.SharedConfigDir))
                {
                    MessageBox.Show("공유설정 폴더가 지정되지 않아 프로젝트 목록을 열 수 없습니다.\n먼저 [설정]에서 폴더를 지정하세요.",
                        "워크리포트", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                WindowHelper.ShowOverExcel(new ProjectsWindow(settings));
            }
            catch (Exception ex)
            {
                Logger.Error("프로젝트 관리 창 오류", ex);
                MessageBox.Show("프로젝트 관리 창을 열지 못했습니다: " + ex.Message,
                    "워크리포트", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void OnSettings(IRibbonControl control)
        {
            try
            {
                WindowHelper.ShowOverExcel(new SettingsWindow());
            }
            catch (Exception ex)
            {
                Logger.Error("설정 창 오류", ex);
                MessageBox.Show("설정 창을 열지 못했습니다: " + ex.Message,
                    "워크리포트", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void OnOpenLog(IRibbonControl control)
        {
            try
            {
                if (File.Exists(Logger.TodayLogPath)) Process.Start(Logger.TodayLogPath);
                else if (Directory.Exists(Logger.LogDir)) Process.Start(Logger.LogDir);
                else MessageBox.Show("아직 로그가 없습니다.", "워크리포트", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("로그를 열지 못했습니다: " + ex.Message,
                    "워크리포트", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
