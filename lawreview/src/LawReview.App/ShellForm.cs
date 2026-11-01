using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LawReview.App;

/// <summary>
/// 화면을 담는 셸 창. 실제 UI는 로컬 웹 호스트가 제공하는 HTML이다.
///
/// WebView2 런타임이 없는 PC(윈도우 업데이트가 오래된 경우)에서는 창 대신
/// **기본 브라우저로 같은 주소를 열어** 그대로 쓸 수 있게 한다. 화면 코드는 동일하다.
/// </summary>
public sealed class ShellForm : Form
{
    private readonly string _url;

    public ShellForm(string url)
    {
        _url = url;
        Text = "법규검토서 생성기";
        MinimumSize = new Size(1100, 760);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        if (!IsWebView2Available())
        {
            OpenInBrowserAndClose(
                "이 PC에 WebView2 런타임이 없어 기본 브라우저로 열었습니다.\n" +
                "창 안에서 쓰시려면 Microsoft Edge WebView2 런타임을 설치하세요.");
            return;
        }

        try
        {
            var view = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(view);
            await view.EnsureCoreWebView2Async();
            view.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            view.CoreWebView2.Settings.IsStatusBarEnabled = false;
            view.Source = new Uri(_url);
        }
        catch (Exception ex)
        {
            OpenInBrowserAndClose($"WebView2를 시작하지 못해 기본 브라우저로 열었습니다.\n({ex.Message})");
        }
    }

    /// <summary>런타임 설치 여부. 없으면 예외가 나므로 이를 판별에 쓴다.</summary>
    internal static bool IsWebView2Available()
    {
        try { return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString()); }
        catch { return false; }
    }

    private void OpenInBrowserAndClose(string message)
    {
        MessageBox.Show(message, "법규검토서 생성기", MessageBoxButtons.OK, MessageBoxIcon.Information);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_url) { UseShellExecute = true });
        // 브라우저로 넘어갔으므로 셸 창은 최소 상태로 남겨 호스트를 살려 둔다.
        WindowState = FormWindowState.Minimized;
        ShowInTaskbar = true;
        Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = $"브라우저에서 사용 중입니다.\n{_url}\n\n이 창을 닫으면 프로그램이 종료됩니다.",
        });
    }
}
