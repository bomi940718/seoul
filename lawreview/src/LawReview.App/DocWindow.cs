using System.Text.Json;
using LawReview.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LawReview.App;

/// <summary>
/// 서류 발급 창 — 정부24·토지이음을 앱 안에서 열고, 화면에 뜬 서류를 PDF로 저장해 서류 대조에 넘긴다.
///
/// 로그인(본인인증)과 발급 신청은 사용자가 직접 한다 — 프로그램이 인증을 대신할 수 없고 대신해서도 안 된다.
/// 프로그램은 그 뒤의 일(화면을 PDF로 저장 → 파일 위치 기억 → 서류 대조표에 반영)만 한다.
///
/// 정부24는 서류를 새 창(팝업)으로 띄우는 경우가 많아, 팝업도 같은 모양의 창(저장 버튼 포함)으로 연다.
/// </summary>
public sealed class DocWindow : Form
{
    private readonly string _kind;          // building / land / plan
    private readonly string _label;         // 건축물대장 / 토지대장 / 토지이용계획확인원
    private readonly Action<string, string> _onSaved;   // (kind, pdfPath)
    private readonly Label _status = new() { AutoSize = true, Padding = new Padding(8, 9, 0, 0) };
    public WebView2 View { get; } = new() { Dock = DockStyle.Fill };

    public DocWindow(string kind, string label, Action<string, string> onSaved)
    {
        _kind = kind;
        _label = label;
        _onSaved = onSaved;
        Text = $"{label} — 서류 발급 창";
        Size = new Size(1180, 900);
        StartPosition = FormStartPosition.CenterScreen;

        var save = new Button { Text = "이 화면을 PDF로 저장 → 서류 대조에 반영", AutoSize = true, Margin = new Padding(6) };
        save.Click += async (_, _) => await SaveAsync();
        var guide = new Label
        {
            AutoSize = true, Padding = new Padding(8, 9, 0, 0),
            Text = "① 로그인·발급(열람)은 직접 진행   ② 서류가 화면에 보이면 오른쪽 버튼",
        };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false };
        bar.Controls.AddRange(new Control[] { guide, save, _status });
        Controls.Add(View);
        Controls.Add(bar);
    }

    /// <summary>창을 띄우고 주소를 연다. 팝업 창이면 url 대신 NewWindowRequested 쪽에서 붙인다.</summary>
    public async Task InitAsync(string? url)
    {
        await View.EnsureCoreWebView2Async();
        View.CoreWebView2.NewWindowRequested += OnNewWindow;
        if (url is not null) View.Source = new Uri(url);
    }

    private async void OnNewWindow(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // 서류 미리보기·출력이 새 창으로 뜬다 — 같은 저장 버튼이 있는 창으로 받는다.
        var deferral = e.GetDeferral();
        var popup = new DocWindow(_kind, _label, _onSaved);
        popup.Show(this);
        await popup.InitAsync(null);
        e.NewWindow = popup.View.CoreWebView2;
        e.Handled = true;
        deferral.Complete();
    }

    private async Task SaveAsync()
    {
        try
        {
            var dir = Path.Combine(DataLocation.Root, "docs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{_label}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
            _status.Text = "저장 중…";
            var settings = View.CoreWebView2.Environment.CreatePrintSettings();
            settings.ShouldPrintBackgrounds = true;
            var ok = await View.CoreWebView2.PrintToPdfAsync(path, settings);
            if (!ok || !File.Exists(path)) { _status.Text = "저장하지 못했습니다 — 서류가 화면에 다 뜬 뒤 다시 누르세요."; return; }
            _status.Text = $"저장됨: {Path.GetFileName(path)}";
            _onSaved(_kind, path);
        }
        catch (Exception ex)
        {
            _status.Text = "저장 실패: " + ex.Message;
        }
    }

    /// <summary>화면(app.js)이 보낸 {"type":"openDoc","kind":..,"label":..,"url":..}을 해석한다.</summary>
    internal static (string Kind, string Label, string Url)? ParseOpenRequest(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            var r = d.RootElement;
            if (r.GetProperty("type").GetString() != "openDoc") return null;
            var url = r.GetProperty("url").GetString() ?? "";
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps
                || !AllowedHosts.Contains(u.Host)) return null;
            return (r.GetProperty("kind").GetString() ?? "", r.GetProperty("label").GetString() ?? "서류", u.AbsoluteUri);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        { return null; }
    }

    /// <summary>처음 여는 주소는 정해 둔 발급·열람 사이트만. (창 안에서 이동하는 건 사용자 몫)</summary>
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
        { "www.gov.kr", "plus.gov.kr", "www.eum.go.kr", "cloud.eais.go.kr" };
}
