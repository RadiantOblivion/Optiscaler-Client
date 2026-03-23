using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using System;

namespace OptiscalerClient.Views
{
    public partial class GuideWindow : Window
    {
        public GuideWindow()
        {
            InitializeComponent();
        }

        public GuideWindow(Window? owner)
        {
            InitializeComponent();
            
            // 100% Flicker-free startup strategy:
            this.Opacity = 0;
            
            if (owner != null)
            {
                var scaling = owner.DesktopScaling;
                double dialogW = 550 * scaling;
                double dialogH = 620 * scaling;

                var x = owner.Position.X + (owner.Bounds.Width * scaling - dialogW) / 2;
                var y = owner.Position.Y + (owner.Bounds.Height * scaling - dialogH) / 2;
                
                this.Position = new PixelPoint((int)Math.Max(0, x), (int)Math.Max(0, y));
            }

            var titleBar = this.FindControl<Border>("TitleBar");
            if (titleBar != null)
            {
                titleBar.PointerPressed += (s, e) => 
                {
                    this.BeginMoveDrag(e);
                };
            }

            this.Opened += (s, e) => 
            {
                this.Opacity = 1;
            };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
