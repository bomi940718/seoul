namespace LawReview.Core;

/// <summary>
/// 사용자 데이터 폴더(%APPDATA%\LawReview) — 설정(API 키)과 저장 프로젝트가 여기 있다.
///
/// <para><b>함정 — 실제로 겪은 일 (2026-11):</b> Windows는 스토어(MSIX) 앱이 띄운 프로세스가
/// %APPDATA%에 쓴 파일을 그 앱 전용 가상 폴더
/// (<c>%LOCALAPPDATA%\Packages\{앱}\LocalCache\Roaming</c>)로 돌려 놓는다. 그래서 스토어 앱
/// (예: Claude Desktop) 안에서 이 프로그램을 띄워 키를 입력하면 키가 그 가상 폴더에만 저장되고,
/// 탐색기에서 exe를 더블클릭하면 <b>빈 설정으로 뜬다.</b> 화면에는 "VWorld 키를 먼저 입력하세요"가
/// 상태줄에만 조용히 떠서 "주소 조회가 안 된다"로 보였다.</para>
///
/// <para>그래서 실제 폴더에 설정이 없을 때 한 번, 가상 폴더에 남은 사본을 찾아 가져온다.
/// 이미 있는 파일은 덮어쓰지 않는다(사용자가 실제 폴더에서 고친 값이 이긴다).
/// 일반 PC(협력사)에는 그런 사본이 없으니 아무 일도 일어나지 않는다.</para>
/// </summary>
public static class DataLocation
{
    private const string AppFolder = "LawReview";
    private static readonly object Gate = new();
    private static bool _checked;

    /// <summary>%APPDATA%\LawReview. 처음 쓸 때 가상 폴더에 남은 설정을 한 번 가져온다.</summary>
    public static string Root
    {
        get
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolder);
            lock (Gate)
            {
                if (!_checked)
                {
                    _checked = true;
                    TryImportFromPackages(root, PackagesDirectory());
                }
            }
            return root;
        }
    }

    private static string PackagesDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");

    /// <summary>
    /// 실제 폴더에 settings.json이 없으면 스토어 앱 가상 폴더들에서 가장 최근 사본을 찾아
    /// 설정과 프로젝트를 복사한다. 가져온 사본의 경로를 돌려준다(없으면 null).
    /// </summary>
    internal static string? TryImportFromPackages(string root, string packagesDir)
    {
        try
        {
            if (File.Exists(Path.Combine(root, "settings.json"))) return null;
            if (!Directory.Exists(packagesDir)) return null;

            var source = Directory.EnumerateDirectories(packagesDir)
                .Select(p => Path.Combine(p, "LocalCache", "Roaming", AppFolder))
                .Where(d => File.Exists(Path.Combine(d, "settings.json")))
                .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, "settings.json")))
                .FirstOrDefault();
            if (source is null) return null;

            CopyMissing(source, root);
            return source;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 가져오기 실패는 치명적이지 않다 — 설정 화면에서 키를 새로 넣으면 된다.
            return null;
        }
    }

    /// <summary>원본 폴더의 파일을 대상에 없는 것만 복사한다(하위 폴더 포함).</summary>
    private static void CopyMissing(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
        {
            var target = Path.Combine(to, Path.GetFileName(file));
            if (!File.Exists(target)) File.Copy(file, target);
        }
        foreach (var dir in Directory.EnumerateDirectories(from))
            CopyMissing(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
