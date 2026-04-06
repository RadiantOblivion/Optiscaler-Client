using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using OptiscalerClient.Services;

namespace OptiscalerClient.Helpers
{
    public static class AnimationHelper
    {
        public static TimeSpan GetPanelAnimationDuration()
        {
            var config = new ComponentManagementService().Config;
            return config.AnimationsEnabled ? TimeSpan.FromMilliseconds(220) : TimeSpan.Zero;
        }

        public static void SetupPanelTransition(Panel panel)
        {
            if (panel == null) return;
            
            var duration = GetPanelAnimationDuration();
            panel.Transitions = new Transitions();
            
            if (duration > TimeSpan.Zero)
            {
                panel.Transitions.Add(new DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = duration,
                    Easing = new CubicEaseOut()
                });
            }
        }
    }
}
