using System;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plustek.Services;

namespace Plustek {
    [SupportedOSPlatform("windows")]
    class Program {
        private static readonly bool TEST_STATIC_IMAGE = true;
        private static readonly string TEST_IMAGE_PATH = @"C:\Users\MAH\Desktop\ID.jpg";

        static async Task<int> Main(string[] args) {
            var services = new ServiceCollection();

            services.AddLogging(builder => {
                builder.AddConsole();
                builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
            });

            services.AddSingleton<IImageLoader, OpenCvImageLoader>();
            services.AddSingleton<IBarcodeDetector, OpenCvBarcodeDetector>();
            services.AddSingleton<IOutputWriter, HtmlOutputService>();
            services.AddSingleton<IPlustekScannerService, PlustekWebSocketService>();

            services.AddTransient<Runner>();
            services.AddTransient<PlustekBarcodeRunner>();
            services.AddTransient<BarcodeDecoderTest>();

            var serviceProvider = services.BuildServiceProvider();

            if (TEST_STATIC_IMAGE) {
                Console.WriteLine("═══════════════════════════════════════════════════════════");
                Console.WriteLine("TESTING SPECIFIC IMAGE (STATIC MODE)");
                Console.WriteLine("═══════════════════════════════════════════════════════════");
                Console.WriteLine($"Image: {TEST_IMAGE_PATH}");
                Console.WriteLine();

                var tester = serviceProvider.GetRequiredService<BarcodeDecoderTest>();
                bool success = await tester.TestImageAsync(TEST_IMAGE_PATH);
                return success ? 0 : 1;
            }

            if (args.Length == 0) {
                Console.WriteLine("Starting in SCANNER MODE (interactive)");
                Console.WriteLine();

                var plustekRunner = serviceProvider.GetRequiredService<PlustekBarcodeRunner>();
                return await plustekRunner.RunInteractiveAsync();
            } else if (args[0].ToLower() == "--scan") {
                Console.WriteLine("Starting SINGLE SCAN mode");
                var plustekRunner = serviceProvider.GetRequiredService<PlustekBarcodeRunner>();
                return await plustekRunner.RunSingleScanAsync();
            } else if (args[0].ToLower() == "--test" && args.Length > 1) {
                Console.WriteLine($"Testing barcode decoder on: {args[1]}");
                var tester = serviceProvider.GetRequiredService<BarcodeDecoderTest>();

                if (Directory.Exists(args[1])) {
                    await tester.TestDirectoryAsync(args[1]);
                    return 0;
                } else if (File.Exists(args[1])) {
                    bool success = await tester.TestImageAsync(args[1]);
                    return success ? 0 : 1;
                } else {
                    Console.WriteLine($"❌ Path not found: {args[1]}");
                    return 1;
                }
            } else if (args[0].ToLower() == "--file" && args.Length > 1) {
                Console.WriteLine($"Processing existing image: {args[1]}");
                var runner = serviceProvider.GetRequiredService<Runner>();
                return await runner.RunAsync(args[1]);
            } else if (File.Exists(args[0])) {
                Console.WriteLine($"Processing image file: {args[0]}");
                var runner = serviceProvider.GetRequiredService<Runner>();
                return await runner.RunAsync(args[0]);
            } else {
                ShowUsage();
                return 1;
            }
        }

        static void ShowUsage() {
            Console.WriteLine(@"
╔════════════════════════════════════════════════════════════╗
║   PLUSTEK SYRIAN ID BARCODE DECODER                       ║
╚════════════════════════════════════════════════════════════╝

USAGE:
  
  Interactive Scanner Mode (default):
    PlustekBarcodeScanner.exe
    
  Single Scan Mode:
    PlustekBarcodeScanner.exe --scan
    
  Test Barcode Decoder:
    PlustekBarcodeScanner.exe --test <image_path_or_directory>
    
  Process Existing Image:
    PlustekBarcodeScanner.exe --file <image_path>
    PlustekBarcodeScanner.exe <image_path>

EXAMPLES:
  
  # Start interactive scanning session
  PlustekBarcodeScanner.exe
  
  # Scan once and exit
  PlustekBarcodeScanner.exe --scan
  
  # Test barcode decoder on single image
  PlustekBarcodeScanner.exe --test C:\temp\scan.jpg
  
  # Test barcode decoder on all images in folder
  PlustekBarcodeScanner.exe --test C:\temp\WebFXScan\
  
  # Process a saved image
  PlustekBarcodeScanner.exe C:\scans\id_card.jpg
  PlustekBarcodeScanner.exe --file C:\scans\id_card.jpg

REQUIREMENTS:
  - Plustek SDK installed (PlustekSDK_LDSetup_25243_x64.exe)
  - Plustek scanner connected via USB
  - Scanner drivers installed
  - .NET 6.0 or higher

OUTPUT FILES:
  - HTML report with formatted data
  - JSON file with complete object data
  - TXT file with all fields
  - Original scanned image (in 'scans' folder)
");
        }
    }
}