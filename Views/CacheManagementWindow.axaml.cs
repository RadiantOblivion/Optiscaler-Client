using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using OptiscalerClient.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;

namespace OptiscalerClient.Views
{
    public partial class CacheManagementWindow : Window
    {
        private readonly ComponentManagementService _componentService;

        public CacheManagementWindow()
        {
            InitializeComponent();
            _componentService = new ComponentManagementService();
        }

        public CacheManagementWindow(Window owner)
        {
            InitializeComponent();
            _componentService = new ComponentManagementService();

            // Flicker-free startup strategy
            this.Opacity = 0;
            
            var titleBar = this.FindControl<Border>("TitleBar");
            if (titleBar != null)
            {
                titleBar.PointerPressed += (s, e) => this.BeginMoveDrag(e);
            }

            this.Opened += (s, e) => this.Opacity = 1;

            LoadCacheItems();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void LoadCacheItems()
        {
            var pnlVersions = this.FindControl<StackPanel>("PnlVersions");
            if (pnlVersions == null) return;

            pnlVersions.Children.Clear();
            var versions = _componentService.GetDownloadedOptiScalerVersions();
            
            var txtCacheInfo = this.FindControl<TextBlock>("TxtCacheInfo");
            if (txtCacheInfo != null)
            {
                txtCacheInfo.Text = $"{versions.Count} versions stored locally.";
            }

            if (!versions.Any())
            {
                pnlVersions.Children.Add(new TextBlock 
                { 
                    Text = "No versions cached yet.", 
                    Foreground = Brushes.Gray,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 40, 0, 0)
                });
                return;
            }

            foreach (var ver in versions)
            {
                var card = CreateVersionCard(ver);
                pnlVersions.Children.Add(card);
            }
        }

        private Border CreateVersionCard(string version)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*, Auto"),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            var stack = new StackPanel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            stack.Children.Add(new TextBlock 
            { 
                Text = version, 
                FontWeight = FontWeight.Bold,
                Foreground = Application.Current?.FindResource("BrTextPrimary") as IBrush ?? Brushes.White
            });
            
            if (version == _componentService.OptiScalerVersion)
            {
                stack.Children.Add(new TextBlock 
                { 
                    Text = "Currently selected", 
                    FontSize = 10,
                    Foreground = Application.Current?.FindResource("BrAccent") as IBrush ?? Brushes.DeepSkyBlue
                });
            }

            grid.Children.Add(stack);
            Grid.SetColumn(stack, 0);

            var btnDelete = new Button
            {
                Content = "Delete",
                Classes = { "BtnSecondary" },
                Padding = new Thickness(12, 4),
                FontSize = 11,
                Tag = version
            };
            btnDelete.Click += BtnDelete_Click;

            grid.Children.Add(btnDelete);
            Grid.SetColumn(btnDelete, 1);

            return new Border
            {
                Background = Application.Current?.FindResource("BrBgCard") as IBrush ?? Brushes.Transparent,
                BorderBrush = Application.Current?.FindResource("BrBorderSubtle") as IBrush ?? Brushes.DimGray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 10),
                Child = grid
            };
        }

        private async void BtnDelete_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string ver)
            {
                var title = "Delete Version";
                var msg = $"Are you sure you want to delete OptiScaler {ver} from cache?";
                
                var dialog = new ConfirmDialog(this, title, msg, false);
                var result = await dialog.ShowDialog<bool>(this);

                if (result)
                {
                    try
                    {
                        _componentService.DeleteOptiScalerCache(ver);
                        LoadCacheItems();
                    }
                    catch (Exception ex)
                    {
                        await new ConfirmDialog(this, "Error", $"Failed to delete version: {ex.Message}").ShowDialog<object>(this);
                    }
                }
            }
        }

        private void BtnClose_Click(object? sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
