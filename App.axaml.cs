using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;

namespace OptiscalerClient
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);

            // Ensure trace and console output go to Terminal
            System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(Console.Out));
            
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                System.IO.File.WriteAllText("crash.log", args.ExceptionObject.ToString());
            };
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new Views.MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }

        public static string CurrentLanguage = "en";

        public static void ChangeLanguage(string langCode)
        {
            try
            {
                var uri = new Uri($"avares://OptiscalerClient/Languages/Strings.{langCode}.axaml");
                var include = new ResourceInclude(new Uri("avares://OptiscalerClient/App.axaml"))
                {
                    Source = uri
                };

                var dictionaries = Application.Current?.Resources.MergedDictionaries;
                if (dictionaries != null)
                {
                    // In App.axaml, we only have one MergedDictionary at index 0 which is Strings.*.axaml
                    if (dictionaries.Count > 0)
                    {
                        var oldDict = dictionaries[0];
                        dictionaries.Remove(oldDict);
                    }
                    dictionaries.Insert(0, include);
                }
                CurrentLanguage = langCode;
            }
            catch
            {
                // Fallback or ignore
            }
        }
    }
}