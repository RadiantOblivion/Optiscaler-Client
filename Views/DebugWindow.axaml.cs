using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.Text;

namespace OptiscalerClient.Views
{
    public partial class DebugWindow : Window
    {
        private readonly StringBuilder _logContent = new StringBuilder();
        private static DebugWindow? _instance;
        public static DebugWindow? Instance => _instance;

        public DebugWindow()
        {
            InitializeComponent();
            _instance = this;
            
            this.Closed += (s, e) => _instance = null;
            
            Log("Debug Window Initialized");
        }

        public DebugWindow(bool isStartup) : this()
        {
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public static void Log(string message)
        {
            if (_instance == null) return;

            Dispatcher.UIThread.Post(() =>
            {
                var txtLogs = _instance.FindControl<SelectableTextBlock>("TxtLogs");
                var scroll = _instance.FindControl<ScrollViewer>("LogScrollViewer");

                if (txtLogs != null)
                {
                    string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                    string line = $"[{timestamp}] {message}{Environment.NewLine}";
                    _instance._logContent.Append(line);
                    txtLogs.Text = _instance._logContent.ToString();
                    
                    if (scroll != null) scroll.ScrollToEnd();
                }
            });
        }

        private void BtnClear_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _logContent.Clear();
            var txtLogs = this.FindControl<SelectableTextBlock>("TxtLogs");
            if (txtLogs != null) txtLogs.Text = "";
        }

        private async void BtnCopy_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await TopLevel.GetTopLevel(this)!.Clipboard!.SetTextAsync(_logContent.ToString());
        }
    }
}
