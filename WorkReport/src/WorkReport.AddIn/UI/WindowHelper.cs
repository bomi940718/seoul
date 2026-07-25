using System;
using System.Windows;
using System.Windows.Interop;
using ExcelDna.Integration;
using WorkReport.AddIn.Services;

namespace WorkReport.AddIn.UI
{
    internal static class WindowHelper
    {
        /// <summary>Excel 메인 창을 소유자로 지정해 모달로 띄운다 (창이 Excel 뒤로 숨는 것 방지).</summary>
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
            return window.ShowDialog();
        }
    }
}
