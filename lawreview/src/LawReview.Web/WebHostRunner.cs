using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace LawReview.Web;

/// <summary>
/// 화면을 제공하는 로컬 웹 호스트.
///
/// 이 구조를 쓰는 이유: WebView2 런타임이 없는 PC에서도 **같은 주소를 기본 브라우저로 열면**
/// 그대로 동작한다. 화면 코드가 갈라지지 않는다.
/// 바인딩은 127.0.0.1 고정이라 외부에서 접근할 수 없고, 인터넷 없이도 동작한다.
/// </summary>
public static class WebHostRunner
{
    /// <summary>
    /// 로컬 포트에서 호스트를 띄우고 접속 주소를 돌려준다.
    /// fixedPort를 주지 않으면 비어 있는 포트를 골라 쓴다(앱 실행 시 기본).
    /// </summary>
    /// <param name="wwwrootPath">
    /// 개발용. 화면 파일을 이 폴더에서 읽는다(지정하지 않으면 어셈블리에 내장된 사본).
    /// 배포된 exe는 내장 사본을 쓰므로 <b>화면을 고쳐도 다시 빌드하기 전에는 반영되지 않는다</b> —
    /// 개발 중에 그 함정을 피하려고 있는 통로다 (LawReview.Cli --web).
    /// </param>
    public static async Task<(WebApplication App, string Url)> StartAsync(
        CancellationToken ct = default, int? fixedPort = null, string? wwwrootPath = null)
    {
        var port = fixedPort ?? FindFreePort();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        var app = builder.Build();

        // wwwroot는 기본적으로 어셈블리 내장 리소스에서 제공한다(단일 exe 배포 대응).
        IFileProvider files = wwwrootPath is { Length: > 0 } dir && Directory.Exists(dir)
            ? new PhysicalFileProvider(Path.GetFullPath(dir))
            : new ManifestEmbeddedFileProvider(Assembly.GetExecutingAssembly(), "wwwroot");
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = files });

        ApiEndpoints.Map(app);

        await app.StartAsync(ct);
        return (app, $"http://127.0.0.1:{port}");
    }

    private static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
