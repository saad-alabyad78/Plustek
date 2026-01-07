using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Plustek.Configuration;
using Plustek.Interfaces;
using Plustek.Services;
using Plustek.Runner;

namespace Plustek {
    class Program {
        static async Task<int> Main(string[] args) {
            var services = ConfigureServices();
            var runner = services.GetRequiredService<ScannerRunner>();

            if (args.Length == 0) {
                return await runner.RunInteractiveAsync();
            }

            return args[0].ToLower() switch {
                "--scan" => await runner.RunSingleScanAsync(),
                "--test" when args.Length > 1 => await runner.TestImageAsync(args[1]),
                _ => ShowUsage()
            };
        }

        static ServiceProvider ConfigureServices() {
            var services = new ServiceCollection();

            services.AddSingleton<AppSettings>();
            services.AddSingleton<IBarcodeDecoder, BarcodeDecoderService>();
            services.AddSingleton<IScanner, ScannerService>();
            services.AddSingleton<IOutputWriter, OutputWriterService>();
            services.AddTransient<ScannerRunner>();

            return services.BuildServiceProvider();
        }

        static int ShowUsage() {
            Console.WriteLine(@"
╔════════════════════════════════════════════════════════╗
║   PLUSTEK SYRIAN ID SCANNER                           ║
╚════════════════════════════════════════════════════════╝

USAGE:
  PlustekScanner.exe              # Interactive mode
  PlustekScanner.exe --scan       # Single scan
  PlustekScanner.exe --test <path> # Test image
");
            return 1;
        }
    }
}