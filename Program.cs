using System;
using System.IO;
using Avalonia;

namespace PsychoLauncher
{
    class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // エラー捕獲の罠（try-catch）を開始します
            try
            {
                // 1. WebView2用のデータ保存先を設定
                string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PsychoLauncher", "WebViewData");
                if (!Directory.Exists(userDataFolder))
                {
                    Directory.CreateDirectory(userDataFolder);
                }
                Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", userDataFolder, EnvironmentVariableTarget.Process);

                // 2. アプリの起動
                BuildAvaloniaApp()
                    .StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                // ★起動中にエラーが起きた場合、ここが実行されます

                // デスクトップのパス（場所）を自動取得
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                // 保存するファイル名を決定
                string logFilePath = Path.Combine(desktopPath, "PsychoLauncher_ErrorLog.txt");
                
                // エラーの詳細な理由（ex.ToString()）をテキストファイルとして書き出す
                File.WriteAllText(logFilePath, "【起動時クリティカルエラー】\n" + ex.ToString());
            }
        }

        // Avalonia（画面の仕組み）の初期設定
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}