using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ExcelDna.Integration;
using WorkReport.AddIn.Services;

namespace WorkReport.AddIn.UI
{
    internal static class WindowHelper
    {
        /// <summary>
        /// Excel 메인 창을 소유자로 지정해 모달로 띄운다 (창이 Excel 뒤로 숨는 것 방지).
        /// 창이 떠 있는 동안에는 처리되지 않은 UI 예외를 가로채 로그로 남긴다 —
        /// 그대로 두면 Excel 프로세스가 함께 종료된다.
        /// </summary>
        public static bool? ShowOverExcel(Window window)
        {
            try
            {
                new WindowInteropHelper(window).Owner = ExcelDnaUtil.WindowHandle;
            }
            catch (Exception ex)
            {
                Logger.Warn("Excel 창 소유자 지정 실패: " + ex.Message);
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            DispatcherUnhandledExceptionEventHandler guard = (s, e) =>
            {
                Logger.Error("창에서 처리되지 않은 오류", e.Exception);
                MessageBox.Show(
                    "예상치 못한 오류가 발생했습니다.\n\n" + e.Exception.Message +
                    "\n\n자세한 내용은 [로그 열기]로 확인하세요.",
                    "워크리포트", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };

            var dispatcher = window.Dispatcher;
            dispatcher.UnhandledException += guard;
            try
            {
                return window.ShowDialog();
            }
            finally
            {
                dispatcher.UnhandledException -= guard;
            }
        }
    }
}
