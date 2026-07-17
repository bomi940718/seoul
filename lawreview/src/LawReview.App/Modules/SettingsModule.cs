using LawReview.Core;

namespace LawReview.App.Modules;

/// <summary>
/// 설정 모듈: API 키 입력·저장. 키는 이 PC의 %APPDATA%\LawReview에만 저장된다.
/// </summary>
public sealed class SettingsModule : IAppModule
{
    public string Title => "설정";

    private readonly TextBox _molegKey = new() { UseSystemPasswordChar = true };
    private readonly TextBox _claudeKey = new() { UseSystemPasswordChar = true };
    private readonly TextBox _claudeModel = new();

    public Control CreateControl()
    {
        var settings = AppSettings.Load();
        _molegKey.Text = settings.MolegApiKey;
        _claudeKey.Text = settings.ClaudeApiKey;
        _claudeModel.Text = settings.ClaudeModel;

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(string label, Control input, string? help = null)
        {
            panel.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, panel.RowCount);
            input.Dock = DockStyle.Fill;
            panel.Controls.Add(input, 1, panel.RowCount);
            panel.RowCount++;
            if (help is not null)
            {
                panel.Controls.Add(new Label(), 0, panel.RowCount);
                panel.Controls.Add(new Label
                {
                    Text = help, Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText, AutoSize = true,
                }, 1, panel.RowCount);
                panel.RowCount++;
            }
        }

        AddRow("법제처 Open API 키 (OC)", _molegKey,
            "open.law.go.kr 회원가입 후 'Open API 사용 신청'으로 무료 발급. 조문 원문 조회에 사용됩니다.");
        AddRow("Claude API 키", _claudeKey,
            "console.anthropic.com에서 발급. 적용/해당없음 판정에 사용되며 호출량만큼 과금됩니다.");
        AddRow("Claude 모델", _claudeModel, "기본값: claude-sonnet-5");

        var save = new Button { Text = "저장", Width = 120, Height = 34 };
        save.Click += (_, _) =>
        {
            var s = new AppSettings
            {
                MolegApiKey = _molegKey.Text.Trim(),
                ClaudeApiKey = _claudeKey.Text.Trim(),
                ClaudeModel = _claudeModel.Text.Trim().Length > 0 ? _claudeModel.Text.Trim() : "claude-sonnet-5",
            };
            s.Save();
            MessageBox.Show("설정을 저장했습니다.", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        panel.Controls.Add(new Label(), 0, panel.RowCount);
        panel.Controls.Add(save, 1, panel.RowCount);

        return panel;
    }
}
