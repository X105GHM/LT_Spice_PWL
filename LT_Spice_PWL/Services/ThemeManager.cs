using System;
using System.Windows;

namespace PwlEditor.Services
{
    public static class ThemeManager
    {
        public const string Dark = "Dark";
        public const string Light = "Light";

        public static void ApplyTheme(string themeName)
        {
            string source = themeName switch
            {
                Light => "Themes/LightTheme.xaml",
                _ => "Themes/DarkTheme.xaml"
            };

            var dictionary = new ResourceDictionary
            {
                Source = new Uri(source, UriKind.Relative)
            };

            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(dictionary);
        }
    }
}