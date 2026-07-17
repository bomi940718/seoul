namespace LawReview.App;

/// <summary>
/// 메인 창. 모듈(탭) 컨테이너 역할만 하고, 기능은 각 모듈이 담당한다.
/// 추후 CAD 변환 등 새 기능은 Modules 목록에 IAppModule 구현을 추가하면 된다.
/// </summary>
public sealed class MainForm : Form
{
    private static readonly IReadOnlyList<IAppModule> Modules = new IAppModule[]
    {
        new Modules.ReviewModule(),
        new Modules.SettingsModule(),
    };

    public MainForm()
    {
        Text = "법규검토서 생성기";
        MinimumSize = new Size(1000, 720);
        StartPosition = FormStartPosition.CenterScreen;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        foreach (var module in Modules)
        {
            var page = new TabPage(module.Title);
            var control = module.CreateControl();
            control.Dock = DockStyle.Fill;
            page.Controls.Add(control);
            tabs.TabPages.Add(page);
        }
        Controls.Add(tabs);
    }
}
