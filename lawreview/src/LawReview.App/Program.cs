namespace LawReview.App;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        // 이전 WinForms 화면으로 실행하고 싶을 때: LawReview.App.exe --winforms
        if (args.Contains("--winforms"))
        {
            Application.Run(new MainForm());
            return;
        }

        // 화면은 로컬 웹 호스트가 제공한다(인터넷 불필요, 127.0.0.1 고정 바인딩).
        try
        {
            var (host, url) = LawReview.Web.WebHostRunner.StartAsync().GetAwaiter().GetResult();
            try { Application.Run(new ShellForm(url)); }
            finally { host.StopAsync().GetAwaiter().GetResult(); }
        }
        catch (Exception ex)
        {
            // 원인 추적이 가능해야 한다 — 조용히 예전 화면으로 넘어가면 전환 실패를 못 알아챈다.
            var log = Path.Combine(Path.GetTempPath(), "lawreview-startup-error.log");
            try { File.WriteAllText(log, ex.ToString()); } catch { /* 로그 실패는 무시 */ }
            MessageBox.Show($"화면 호스트를 시작하지 못했습니다.\n{ex.Message}\n\n자세한 내용: {log}\n이전 화면으로 실행합니다.",
                "법규검토서 생성기", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Application.Run(new MainForm());
        }
    }
}
