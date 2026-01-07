using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Plustek.Configuration;
using Plustek.Interfaces;
using Plustek.Services;
using Plustek.Runner;
using Plustek.ViewModels;
using Plustek.Views;

namespace Plustek {
    class Program {
        // Windows API to hide/show console window
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_HIDE = 0;
        const int SW_SHOW = 5;

        [STAThread]
        static int Main(string[] args) {
            var services = ConfigureServices();

            // GUI mode (no arguments or --gui)
            if (args.Length == 0 || (args.Length == 1 && args[0].Equals("--gui", StringComparison.OrdinalIgnoreCase))) {
                HideConsoleWindow();
                return RunGuiMode(services);
            }

            // Console modes - keep console visible
            ShowConsoleWindow();
            var runner = services.GetRequiredService<ScannerRunner>();

            return args[0].ToLower() switch {
                "--console" => runner.RunInteractiveAsync().GetAwaiter().GetResult(),
                "--scan" => runner.RunSingleScanAsync().GetAwaiter().GetResult(),
                "--test" when args.Length > 1 => runner.TestImageAsync(args[1]).GetAwaiter().GetResult(),
                _ => ShowUsage()
            };
        }

        static int RunGuiMode(ServiceProvider services) {
            var app = new System.Windows.Application();
            var viewModel = services.GetRequiredService<ScannerViewModel>();
            var mainWindow = new MainWindow(viewModel);

            return app.Run(mainWindow);
        }

        static ServiceProvider ConfigureServices() {
            var services = new ServiceCollection();

            // Settings
            services.AddSingleton<AppSettings>();

            // Services
            services.AddSingleton<IBarcodeDecoder, BarcodeDecoderService>();
            services.AddSingleton<IScanner, ScannerService>();
            services.AddSingleton<IOutputWriter, OutputWriterService>();

            // Console Runner
            services.AddTransient<ScannerRunner>();

            // GUI ViewModel
            services.AddTransient<ScannerViewModel>();

            return services.BuildServiceProvider();
        }

        static void HideConsoleWindow() {
            var handle = GetConsoleWindow();
            if (handle != IntPtr.Zero) {
                ShowWindow(handle, SW_HIDE);
            }
        }

        static void ShowConsoleWindow() {
            var handle = GetConsoleWindow();
            if (handle != IntPtr.Zero) {
                ShowWindow(handle, SW_SHOW);
            }
        }

        static int ShowUsage() {
            Console.WriteLine(@"
╔════════════════════════════════════════════════════════╗
║   PLUSTEK SYRIAN ID SCANNER                           ║
╚════════════════════════════════════════════════════════╝

USAGE:
  PlustekScanner.exe                # GUI mode (default)
  PlustekScanner.exe --gui          # GUI mode
  PlustekScanner.exe --console      # Console interactive mode
  PlustekScanner.exe --scan         # Console single scan
  PlustekScanner.exe --test <path>  # Test image file
");
            return 1;
        }
    }
}