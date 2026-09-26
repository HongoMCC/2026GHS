using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Markup.Xaml;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Text;

namespace PsychoLauncher
{
    public class Game : INotifyPropertyChanged
    {
        public string Title { get; set; } = string.Empty;
        public string Desc { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public Bitmap? IconBitmap { get; set; }
        public string PreviewVideoPath { get; set; } = string.Empty;
        
        public bool HasPreviewVideo => !string.IsNullOrEmpty(PreviewVideoPath);
        public bool IsHtmlGame => Path.EndsWith(".html", StringComparison.OrdinalIgnoreCase);
        public bool ShowWebView => HasPreviewVideo || IsHtmlGame;
        public bool HideWebView => !ShowWebView;

        public Uri? LocalFileUri
        {
            get
            {
                if (HasPreviewVideo) return new Uri(PreviewVideoPath);
                if (IsHtmlGame) return new Uri(Path);
                return null;
            }
        }

        public string ReadmePath { get; set; } = string.Empty;
        public bool HasReadme => !string.IsNullOrEmpty(ReadmePath);
        
        public bool IsPureVideo => Path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                                   Path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
                                   Path.EndsWith(".mov", StringComparison.OrdinalIgnoreCase);

        public bool CanPlay => !IsPureVideo;

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (_isRunning != value)
                {
                    _isRunning = value;
                    OnPropertyChanged();
                    
                    OnPropertyChanged(nameof(PlayButtonText));
                    OnPropertyChanged(nameof(IsPlayEnabled));
                    OnPropertyChanged(nameof(PlayButtonOpacity));
                    OnPropertyChanged(nameof(IsNotRunning));
                }
            }
        }

        public bool IsNotRunning => !IsRunning;

        public string PlayButtonText => IsRunning ? "NOW PLAYING" : "PLAY NOW";
        public bool IsPlayEnabled => !IsRunning;
        public double PlayButtonOpacity => IsRunning ? 0.5 : 1.0;

        private string _launchErrorMessage = string.Empty;
        public string LaunchErrorMessage
        {
            get => _launchErrorMessage;
            set
            {
                if (_launchErrorMessage != value)
                {
                    _launchErrorMessage = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasLaunchError));
                }
            }
        }
        public bool HasLaunchError => !string.IsNullOrEmpty(LaunchErrorMessage);

        private int _playCount;
        public int PlayCount
        {
            get => _playCount;
            set
            {
                if (_playCount != value)
                {
                    _playCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class MainWindow : Window
    {
        private readonly List<Game> _allGames = new List<Game>();
        public ObservableCollection<Game> FilteredGames { get; set; }
        public ObservableCollection<Game> RankingGames { get; set; }
        private readonly DispatcherTimer _monitorTimer;
        private bool _isFirstBoot = true;
        private bool _isLowSpecModeActive = false;
        private readonly Process _currentProcess = Process.GetCurrentProcess();

        public MainWindow()
        {
            InitializeComponent();
            
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            
            FilteredGames = new ObservableCollection<Game>();
            RankingGames = new ObservableCollection<Game>();

            if (this.FindControl<ListBox>("GameListBox") is { } gameListBox)
            {
                gameListBox.ItemsSource = FilteredGames;
            }
            if (this.FindControl<ListBox>("RankingListBox") is { } rankingListBox)
            {
                rankingListBox.ItemsSource = RankingGames;
            }

            string[] searchPaths = 
            {
                Path.Combine(Environment.CurrentDirectory, "games"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "games")
            };
            string gamesDir = searchPaths.FirstOrDefault(Directory.Exists) ?? "";

            _ = LoadGamesAsync(gamesDir);

            _monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _monitorTimer.Tick += UpdateSystemMonitor;
            _monitorTimer.Start();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private string ReadAutoEncodingText(string filePath)
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

            try
            {
                var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
                return utf8Strict.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding("Shift_JIS").GetString(bytes);
            }
        }

        private async Task UnblockGamesFolderAsync(string rootPath)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) return;

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Get-ChildItem -Path '{rootPath}' -Recurse -File | Unblock-File\"",
                    CreateNoWindow = true, 
                    UseShellExecute = false
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ブロック解除エラー: {ex.Message}");
            }
        }

        private async Task LoadGamesAsync(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
            {
                var errorGame = new Game { Title = "ERROR_NO_GAMES", Desc = "gamesフォルダが見つかりません。" };
                _allGames.Add(errorGame);
                FilteredGames.Add(errorGame);
                return;
            }

            await UnblockGamesFolderAsync(rootPath);

            var directories = Directory.GetDirectories(rootPath);
            if (directories.Length == 0) return;

            int delayMs = Math.Min(10000 / directories.Length, 500);

            foreach (var dir in directories)
            {
                Game? game = await Task.Run(() => ProcessSingleGameDirectory(dir));

                if (game != null)
                {
                    _allGames.Add(game);
                    FilteredGames.Add(game);

                    if (FilteredGames.Count == 1)
                    {
                        if (this.FindControl<ListBox>("GameListBox") is { } gameListBox)
                        {
                            gameListBox.SelectedIndex = 0;
                        }
                    }
                }
                await Task.Delay(delayMs);
            }
        }

        private Game? ProcessSingleGameDirectory(string dir)
        {
            string rawTitle = new DirectoryInfo(dir).Name;

            var files = Directory.EnumerateFiles(dir)
                                 .Where(f => !Path.GetFileName(f).StartsWith("._"))
                                 .ToList(); 
                
            var exeFile = files.FirstOrDefault(f => 
            {
                string fileName = Path.GetFileNameWithoutExtension(f);
                string ext = Path.GetExtension(f).ToLower();
                
                bool isAllowedExt = ext == ".exe" || ext == ".html" || ext == ".bat";
                bool isAllowedName = fileName.Equals("main", StringComparison.OrdinalIgnoreCase) || 
                                     fileName.Equals(rawTitle, StringComparison.OrdinalIgnoreCase);
                                     
                return isAllowedExt && isAllowedName;
            });

            var videoFile = files.FirstOrDefault(f => 
            {
                string fileName = Path.GetFileNameWithoutExtension(f);
                string ext = Path.GetExtension(f).ToLower();
                
                bool isAllowedExt = ext == ".mp4" || ext == ".mov" || ext == ".webm";
                bool isAllowedName = fileName.Equals("main", StringComparison.OrdinalIgnoreCase) || 
                                     fileName.Equals(rawTitle, StringComparison.OrdinalIgnoreCase) ||
                                     fileName.Equals("video", StringComparison.OrdinalIgnoreCase) ||
                                     fileName.Equals("動画", StringComparison.OrdinalIgnoreCase) ||
                                     fileName.Equals("preview", StringComparison.OrdinalIgnoreCase);
                                     
                return isAllowedExt && isAllowedName;
            });

            string previewHtmlPath = "";
            
            if (videoFile != null)
            {
                try
                {
                    previewHtmlPath = Path.Combine(dir, "_sys_preview.html");
                    string safeVideoFileName = Uri.EscapeDataString(Path.GetFileName(videoFile)); 
                    string htmlContent = $@"<!DOCTYPE html><html><head><meta charset='utf-8'><style>body {{ margin: 0; padding: 0; background-color: #1A1A24; overflow: hidden; }}video {{ width: 100vw; height: 100vh; object-fit: cover; outline: none; }}</style></head><body><video src='{safeVideoFileName}' autoplay loop muted playsinline controls></video></body></html>";
                    
                    File.WriteAllText(previewHtmlPath, htmlContent);
                }
                catch { }
            }

            if (exeFile == null) exeFile = videoFile;
            if (exeFile == null) return null; 

            var iconFile = files.FirstOrDefault(f => 
                Path.GetFileName(f).Equals("icon.png", StringComparison.OrdinalIgnoreCase) || 
                Path.GetFileName(f).Equals("icon.jpg", StringComparison.OrdinalIgnoreCase));
            
            Bitmap? iconBmp = null;
            if (iconFile != null)
            {
                try 
                { 
                    using var stream = File.OpenRead(iconFile); 
                    int targetWidth = (Environment.ProcessorCount <= 4) ? 64 : 128;
                    iconBmp = Bitmap.DecodeToWidth(stream, targetWidth); 
                } 
                catch { }
            }

            var readmeFile = files.FirstOrDefault(f => Path.GetFileName(f).Equals("readme.txt", StringComparison.OrdinalIgnoreCase));
            string desc = "NO DATA";
            
            if (readmeFile != null)
            {
                try 
                { 
                    string fullText = ReadAutoEncodingText(readmeFile);
                    var lines = fullText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                                        .Where(l => !string.IsNullOrWhiteSpace(l))
                                        .Take(50);
                    if (lines.Any()) desc = string.Join(Environment.NewLine, lines); 
                }
                catch { }
            }

            int loadedPlayCount = 0;
            string playCountFile = Path.Combine(dir, "playcount.txt");
            if (File.Exists(playCountFile))
            {
                try 
                { 
                    string textCount = File.ReadAllText(playCountFile).Trim(); 
                    if (int.TryParse(textCount, out int savedCount)) loadedPlayCount = savedCount; 
                }
                catch { }
            }

            string normalizedTitle = rawTitle.Normalize(NormalizationForm.FormC);
            string normalizedDesc = desc.Normalize(NormalizationForm.FormC);

            return new Game 
            { 
                Title = normalizedTitle, 
                Path = exeFile, 
                DirectoryPath = dir, 
                PreviewVideoPath = previewHtmlPath, 
                Desc = normalizedDesc, 
                ReadmePath = readmeFile ?? "", 
                PlayCount = loadedPlayCount, 
                IconBitmap = iconBmp 
            };
        }

        private void BtnToggleLowSpec_Click(object? sender, RoutedEventArgs e)
        {
            _isLowSpecModeActive = !_isLowSpecModeActive;

            if (this.FindControl<TextBlock>("LowSpecModeText") is { } textBlock)
            {
                textBlock.Text = _isLowSpecModeActive ? "SYSTEM_MODE : ECO (LOW SPEC)" : "SYSTEM_MODE : HIGH PERFORMANCE";
                textBlock.Foreground = new SolidColorBrush(Color.Parse(_isLowSpecModeActive ? "#FFFCEC7E" : "#FFA0AAB2"));
            }
            if (this.FindControl<Avalonia.Controls.Shapes.Path>("LowSpecIcon") is { } icon)
            {
                icon.Fill = new SolidColorBrush(Color.Parse(_isLowSpecModeActive ? "#FFFCEC7E" : "#FF7EF1FC"));
            }
            if (this.FindControl<MatrixBackground>("MatrixBg") is { } matrix)
            {
                matrix.SetEcoMode(_isLowSpecModeActive);
            }
        }

        private async void BtnSystemStart_Click(object? sender, RoutedEventArgs e)
        {
            var home = this.FindControl<Grid>("HomeScreen");
            if (home != null) home.IsVisible = false;

            if (_isFirstBoot)
            {
                var loadingScreen = this.FindControl<Grid>("LoadingScreen");
                if (loadingScreen != null) loadingScreen.IsVisible = true;

                var terminalContent = this.FindControl<TextBlock>("TerminalContent");
                var progressBar = this.FindControl<ProgressBar>("BootProgressBar");
                string[] logs = { "POP_ENGINE: IGNITED...", "COLOR_SYNC: COMPLETE...", "ESTABLISHING_LINK...", "HAVE FUN!" };

                if (terminalContent != null && progressBar != null)
                {
                    terminalContent.Text = "";
                    for (int i = 0; i < logs.Length; i++) 
                    { 
                        terminalContent.Text += $"> {logs[i]}\n"; 
                        progressBar.Value = (i + 1) * (100 / logs.Length);
                        await Task.Delay(400); 
                    }
                    await Task.Delay(500); 
                }
                
                if (loadingScreen != null) loadingScreen.IsVisible = false;
                _isFirstBoot = false; 
            }
            
            var dashboard = this.FindControl<Grid>("DashboardLayer");
            if (dashboard != null) dashboard.IsVisible = true;
        }

        private void BtnBackHome_Click(object? sender, RoutedEventArgs e)
        {
            var title = this.FindControl<StackPanel>("TitleContainer");
            if (title != null) title.IsVisible = true;
            
            var terminal = this.FindControl<StackPanel>("BootTerminal");
            if (terminal != null) terminal.IsVisible = false;
            
            var home = this.FindControl<Grid>("HomeScreen");
            if (home != null) home.IsVisible = true;
            
            var dashboard = this.FindControl<Grid>("DashboardLayer");
            if (dashboard != null) dashboard.IsVisible = false;
        }

        private void BtnQuit_Click(object? sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void SearchBox_KeyUp(object? sender, KeyEventArgs e)
        {
            string rawQuery = (sender as TextBox)?.Text ?? "";
            var query = rawQuery.ToLower().Normalize(NormalizationForm.FormC); 
            
            FilteredGames.Clear(); 
            foreach (var game in _allGames.Where(g => g.Title.ToLower().Contains(query))) FilteredGames.Add(game);
        }

        private void BtnShowRanking_Click(object? sender, RoutedEventArgs e)
        {
            RankingGames.Clear(); 
            foreach (var game in _allGames.OrderByDescending(g => g.PlayCount).Take(10)) RankingGames.Add(game);
            
            if (this.FindControl<Border>("RankingModal") is { } modal) modal.IsVisible = true; 
            if (this.FindControl<Border>("PreviewBorder") is { } preview) preview.IsVisible = false; 
        }

        private void BtnCloseRanking_Click(object? sender, RoutedEventArgs e)
        {
            if (this.FindControl<Border>("RankingModal") is { } modal) modal.IsVisible = false; 
            if (this.FindControl<Border>("PreviewBorder") is { } preview) preview.IsVisible = true; 
        }

        private void BtnShowInfo_Click(object? sender, RoutedEventArgs e)
        {
            if (this.FindControl<Border>("InfoModal") is { } modal) modal.IsVisible = true; 
            if (this.FindControl<Border>("PreviewBorder") is { } preview) preview.IsVisible = false; 
        }

        private void BtnCloseInfo_Click(object? sender, RoutedEventArgs e)
        {
            if (this.FindControl<Border>("InfoModal") is { } modal) modal.IsVisible = false; 
            if (this.FindControl<Border>("PreviewBorder") is { } preview) preview.IsVisible = true; 
        }

        private async void BtnPlayGame_Click(object? sender, RoutedEventArgs e)
        {
            if (this.FindControl<ListBox>("GameListBox")?.SelectedItem is not Game selectedGame || string.IsNullOrEmpty(selectedGame.Path)) return;
            if (selectedGame.IsPureVideo) return;
            
            var launchingOverlay = this.FindControl<Border>("LaunchingOverlay");
            if (launchingOverlay != null) launchingOverlay.IsVisible = true;

            try
            {
                if (selectedGame.IsHtmlGame)
                {
                    var webView = new NativeWebView 
                    { 
                        Source = new Uri(selectedGame.Path), 
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, 
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch 
                    };
                    var gameWindow = new Window 
                    { 
                        Title = selectedGame.Title, 
                        WindowState = WindowState.Maximized, 
                        Width = 1280, 
                        Height = 720, 
                        WindowStartupLocation = WindowStartupLocation.CenterScreen, 
                        Background = new SolidColorBrush(Color.Parse("#FF1A1A24")), 
                        Content = webView 
                    };
                    
                    selectedGame.IsRunning = true;
                    gameWindow.Closed += (s, args) =>
                    {
                        selectedGame.IsRunning = false;
                    };
                    
                    gameWindow.Show();
                    
                    selectedGame.PlayCount++;
                    try { File.WriteAllText(Path.Combine(selectedGame.DirectoryPath, "playcount.txt"), selectedGame.PlayCount.ToString()); } catch { }
                    await Task.Delay(1000);
                    return; 
                }

                var startInfo = new ProcessStartInfo 
                { 
                    FileName = selectedGame.Path, 
                    UseShellExecute = true, 
                    WorkingDirectory = Path.GetDirectoryName(selectedGame.Path) 
                };

                // ★追加：ECOモードがONの場合、ゲーム側に解像度を下げる命令を追加します
                if (_isLowSpecModeActive && !selectedGame.IsHtmlGame)
                {
                    startInfo.Arguments = "-screen-width 1280 -screen-height 720 -window-mode windowed";
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                { 
                    startInfo.FileName = "open"; 
                    // ★Mac用の引数設定
                    if (_isLowSpecModeActive && !selectedGame.IsHtmlGame)
                    {
                        startInfo.Arguments = $"\"{selectedGame.Path}\" --args -screen-width 1280 -screen-height 720 -window-mode windowed";
                    }
                    else
                    {
                        startInfo.Arguments = $"\"{selectedGame.Path}\""; 
                    }
                }
                
                var process = new Process { StartInfo = startInfo };
                process.EnableRaisingEvents = true;

                selectedGame.IsRunning = true;
                
                process.Exited += (s, args) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        selectedGame.IsRunning = false;
                    });
                    process.Dispose();
                };
                
                process.Start();
                
                selectedGame.PlayCount++;
                try { File.WriteAllText(Path.Combine(selectedGame.DirectoryPath, "playcount.txt"), selectedGame.PlayCount.ToString()); } catch { }
                await Task.Delay(1500);
            }
            catch (Exception ex) 
            { 
                Debug.WriteLine($"起動エラー: {ex.Message}"); 
                selectedGame.IsRunning = false;
                
                selectedGame.LaunchErrorMessage = $"SYSTEM ERROR: {ex.Message}\n(※Mac環境ではWindows用ゲームは直接起動できません)";
                
                DispatcherTimer.RunOnce(() =>
                {
                    selectedGame.LaunchErrorMessage = string.Empty;
                }, TimeSpan.FromSeconds(4));
            }
            finally 
            { 
                if (launchingOverlay != null) launchingOverlay.IsVisible = false; 
            }
        }

        private void BtnOpenReadme_Click(object? sender, RoutedEventArgs e)
        {
            if (this.FindControl<ListBox>("GameListBox")?.SelectedItem is not Game selectedGame || string.IsNullOrEmpty(selectedGame.ReadmePath)) return;
            try 
            { 
                var startInfo = new ProcessStartInfo { FileName = selectedGame.ReadmePath, UseShellExecute = true }; 
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) { startInfo.FileName = "open"; startInfo.Arguments = $"\"{selectedGame.ReadmePath}\""; } 
                Process.Start(startInfo); 
            } 
            catch { }
        }

        private void UpdateSystemMonitor(object? sender, EventArgs e)
        {
            _currentProcess.Refresh(); 
            long memoryUsedMB = _currentProcess.WorkingSet64 / (1024 * 1024); 
            if (this.FindControl<TextBlock>("AppMemText") is { } memText) memText.Text = $"MEM {memoryUsedMB:00} MB";
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        }
    }

    public class MatrixBackground : Control 
    { 
        private readonly DispatcherTimer _timer;
        private double[] _drops = Array.Empty<double>();
        private readonly string _chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789ｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾏﾐﾑﾒﾓﾔﾕﾖﾗﾘﾙﾚﾛﾜｦﾝ";
        private readonly Random _rand = new Random();
        private readonly Typeface _typeface = new Typeface("Consolas", FontStyle.Normal, FontWeight.Black);
        private readonly ISolidColorBrush _bgBrush = new SolidColorBrush(Color.FromArgb(70, 100, 123, 125)); 
        private readonly FormattedText[,] _textCache;
        private readonly bool _isLowSpecMode;

        public MatrixBackground() 
        { 
            _isLowSpecMode = Environment.ProcessorCount <= 4;
            _textCache = new FormattedText[_chars.Length, 11];

            Color[] popColors = 
            { 
                Color.Parse("#FF7EF1FC"), Color.Parse("#FFFCEC7E"), Color.Parse("#FFFC7EC6") 
            };

            for (int charIndex = 0; charIndex < _chars.Length; charIndex++)
            {
                string text = _chars[charIndex].ToString();
                Color c = popColors[charIndex % popColors.Length]; 
                
                for (int alphaLevel = 0; alphaLevel <= 10; alphaLevel++)
                {
                    byte alpha = (byte)Math.Max(0, 255 - (alphaLevel * 25));
                    var brush = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B)); 
                    _textCache[charIndex, alphaLevel] = new FormattedText(
                        text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _typeface, 18, brush);
                }
            }

            int intervalMs = _isLowSpecMode ? 100 : 50;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) }; 
            _timer.Tick += (s, e) => InvalidateVisual(); 
            _timer.Start(); 
        }

        public void SetEcoMode(bool isEco)
        {
            if (isEco) { _timer.Stop(); IsVisible = false; }
            else { _timer.Start(); IsVisible = true; }
        }
        
        protected override void OnSizeChanged(SizeChangedEventArgs e) 
        { 
            base.OnSizeChanged(e); 
            int columns = (int)(e.NewSize.Width / 18); 
            if (_isLowSpecMode) columns /= 2;
            _drops = new double[columns]; 
            for (int i = 0; i < columns; i++) _drops[i] = _rand.Next(0, (int)(e.NewSize.Height / 18)); 
        }
        
        public override void Render(DrawingContext context) 
        { 
            if (_drops.Length == 0) return; 
            context.FillRectangle(_bgBrush, Bounds); 
            
            for (int i = 0; i < _drops.Length; i++) 
            { 
                for (int j = 0; j < 10; j++) 
                { 
                    double y = (_drops[i] - j) * 18; 
                    if (y < 0 || y > Bounds.Height) continue; 
                    int charIndex = _rand.Next(_chars.Length); 
                    double xPos = _isLowSpecMode ? i * 36 : i * 18;
                    context.DrawText(_textCache[charIndex, j], new Point(xPos, y)); 
                } 
                if (_drops[i] * 18 > Bounds.Height && _rand.NextDouble() > 0.975) _drops[i] = 0; 
                _drops[i] += 0.5; 
            } 
        }
    }
}