using System;
using System.Windows;

namespace Cloak.App
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                MessageBox.Show($"Unhandled exception:\n{ex?.Message}\n\nStack:\n{ex?.StackTrace}", 
                    "Cloak Crashed", MessageBoxButton.OK, MessageBoxImage.Error);
            };
            
            DispatcherUnhandledException += (sender, args) =>
            {
                MessageBox.Show($"UI exception:\n{args.Exception.Message}\n\nStack:\n{args.Exception.StackTrace}", 
                    "Cloak Error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
        }
    }
}

