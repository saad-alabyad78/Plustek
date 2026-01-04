using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Plustek.Models;

namespace Plustek.Services {
    // Interface definition
    public interface IPlustekScannerService {
        Task<bool> ConnectAsync();
        Task<bool> InitializeAsync();
        Task<SyrianIdData?> ScanAndParseAsync(string outputPath);
        Task<bool> IsDeviceConnectedAsync();
        Task DisconnectAsync();
    }

    // Implementation
    public class PlustekWebSocketService : IPlustekScannerService {
        private readonly ILogger<PlustekWebSocketService> _logger;
        private readonly IBarcodeDetector _barcodeDetector;
        private ClientWebSocket? _webSocket;
        private readonly string _serverUrl;
        private readonly CancellationTokenSource _cts = new();
        private string[]? _cachedDeviceList = null;

        public PlustekWebSocketService(
            ILogger<PlustekWebSocketService> logger,
            IBarcodeDetector barcodeDetector,
            string serverUrl = "ws://127.0.0.1:17778/webscan2") {
            _logger = logger;
            _barcodeDetector = barcodeDetector;
            _serverUrl = serverUrl;
        }

        public async Task<bool> ConnectAsync() {
            try {
                _logger.LogInformation("Connecting to Plustek WebFXScan server at {Url}", _serverUrl);
                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri(_serverUrl), _cts.Token);

                if (_webSocket.State == WebSocketState.Open) {
                    _logger.LogInformation("✓ Connected to Plustek server");
                    return true;
                }
                return false;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to connect to Plustek server");
                Console.WriteLine($"\n❌ Connection Error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> InitializeAsync() {
            try {
                _logger.LogInformation("Initializing scanner...");
                var request = new { type = "call", func = "LibWFX_Init" };
                var response = await SendRequestAsync<InitResponse>(request);

                if (response?.data?.err_code == 0 || (response?.data?.err_code >= 1001 && response?.data?.err_code <= 1003)) {
                    _logger.LogInformation("✓ Scanner initialized successfully");
                    return true;
                }
                return false;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Exception during initialization");
                return false;
            }
        }

        public async Task<bool> IsDeviceConnectedAsync() {
            try {
                var request = new { type = "call", func = "LibWFX_GetDeviesList" };
                var response = await SendRequestAsync<DeviceListResponse>(request);

                if (response?.data?.err_code == 0) {
                    string messageJson = response.data.message.GetRawText();
                    _logger.LogInformation("Raw message JSON: {Json}", messageJson);

                    DeviceMessage? deviceMessage = null;

                    try {
                        deviceMessage = JsonSerializer.Deserialize<DeviceMessage>(messageJson);
                    }
                    catch {
                        try {
                            string unescapedJson = JsonSerializer.Deserialize<string>(messageJson) ?? "";
                            deviceMessage = JsonSerializer.Deserialize<DeviceMessage>(unescapedJson);
                        }
                        catch (Exception ex) {
                            _logger.LogError(ex, "Failed to parse device message");
                        }
                    }

                    if (deviceMessage?.szDevicesList?.Length > 0) {
                        _logger.LogInformation("Found {Count} device(s)", deviceMessage.szDevicesList.Length);
                        _cachedDeviceList = deviceMessage.szDevicesList;

                        for (int i = 0; i < deviceMessage.szDevicesList.Length; i++) {
                            Console.WriteLine($"  - {deviceMessage.szDevicesList[i]} (Serial: {deviceMessage.szSerialList?[i] ?? "Unknown"})");
                        }
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error checking device connection");
                return false;
            }
        }

        public async Task<SyrianIdData?> ScanAndParseAsync(string outputPath) {
            try {
                _logger.LogInformation("Starting scan operation...");

                var deviceList = await GetDeviceListAsync();
                if (deviceList == null || deviceList.Length == 0) {
                    _logger.LogError("No scanner devices available");
                    return null;
                }

                string deviceName = deviceList[0];
                _logger.LogInformation("Using device: {Device}", deviceName);

                // Get temp folder where scanner saves files
                string tempScanFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Temp", "WebFXScan"
                );

                _logger.LogInformation("Temp folder: {Folder}", tempScanFolder);

                // Configure scanner - WORKING configuration
                var scanData = new Dictionary<string, object> {
                    { "device-name", deviceName },
                    { "source", "Camera" },
                    { "paper-size", "2592x1944" },
                    { "resolution", 600 },
                    { "mode", "color" },
                    { "imagefmt", "jpg" },
                    { "recognize-type", "barcode" },
                    { "quality", 100 },
                    { "base64enc", false }
                };

                var scanConfig = new {
                    type = "call",
                    func = "LibWFX_SetProperty",
                    data = scanData
                };

                var configResponse = await SendRequestAsync<ServerResponse>(scanConfig);

                _logger.LogInformation("Config response: err_code={Code}, message={Message}",
                    configResponse?.data?.err_code,
                    configResponse?.data?.message);

                if (configResponse?.data?.err_code != 0) {
                    _logger.LogError("Failed to configure scanner. Error code: {Code}, Message: {Message}",
                        configResponse?.data?.err_code,
                        configResponse?.data?.message);
                    return null;
                }

                _logger.LogInformation("✓ Scanner configured");

                var scanRequest = new { type = "call", func = "LibWFX_AsynchronizeScan" };
                Console.WriteLine("📸 Scanning... Please wait...");

                var scanResponse = await SendRequestAsync<ScanResponse>(scanRequest, timeout: 30000);

                _logger.LogInformation("Scan response: err_code={Code}", scanResponse?.data?.err_code);

                // Debug: Check what the scan response contains
                if (scanResponse?.data?.message != null) {
                    _logger.LogInformation("Scan response message: {Message}", scanResponse.data.message);
                }

                if (scanResponse?.data?.err_code != 0) {
                    _logger.LogError("Scan failed with error code: {Code}", scanResponse?.data?.err_code);
                    return null;
                }

                _logger.LogInformation("✓ Scan completed");
                Console.WriteLine("✓ Scan completed, looking for newest file...");

                // Wait longer for file to be written to disk (camera scanners can be slow)
                await Task.Delay(3000);

                // Simply get the most recently modified file
                if (!Directory.Exists(tempScanFolder)) {
                    _logger.LogError("Temp scan folder not found: {Folder}", tempScanFolder);
                    return null;
                }

                var allFiles = Directory.GetFiles(tempScanFolder, "IMG_*.jpg");

                if (allFiles.Length == 0) {
                    _logger.LogError("No files found in {Folder}", tempScanFolder);
                    Console.WriteLine($"⚠️ No files found in {tempScanFolder}");
                    return null;
                }

                // Get the most recently modified file
                var newestFile = allFiles
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(fi => fi.LastWriteTime)
                    .First();

                string scannedFile = newestFile.FullName;
                long fileSize = newestFile.Length;

                _logger.LogInformation("Using newest file: {File} ({Size} bytes), Modified: {Time}",
                    Path.GetFileName(scannedFile), fileSize, newestFile.LastWriteTime);
                Console.WriteLine($"✓ Found newest: {Path.GetFileName(scannedFile)} ({fileSize:N0} bytes)");

                // Copy to output path
                File.Copy(scannedFile, outputPath, overwrite: true);

                _logger.LogInformation("✓ Image saved: {Path} ({Size:N0} bytes)", outputPath, fileSize);
                Console.WriteLine($"✓ Image saved: {outputPath}");

                // Now detect and decode the barcode
                Console.WriteLine("🔍 Detecting barcode...");

                Mat? image = null;
                try {
                    image = Cv2.ImRead(outputPath, ImreadModes.Color);
                    if (image == null || image.Empty()) {
                        _logger.LogError("Failed to load image for barcode detection");
                        Console.WriteLine("❌ Failed to load image");
                        return null;
                    }

                    var rawBarcode = await _barcodeDetector.DetectAndDecodeAsync(image);

                    if (string.IsNullOrEmpty(rawBarcode)) {
                        _logger.LogWarning("No barcode detected in scanned image");
                        Console.WriteLine("❌ No barcode detected in the scanned image");
                        Console.WriteLine("   Tips:");
                        Console.WriteLine("   - Ensure the ID card is flat on the scanner");
                        Console.WriteLine("   - Make sure the barcode is visible and in focus");
                        Console.WriteLine("   - Try adjusting the card position");

                        // Try with a known good test image for debugging
                        string testImagePath = @"D:\Plustek\test_images\known_good_id.jpg";
                        if (File.Exists(testImagePath)) {
                            Console.WriteLine();
                            Console.WriteLine($"🧪 Testing with known good image: {testImagePath}");
                            _logger.LogInformation("Attempting barcode detection with test image: {Path}", testImagePath);

                            try {
                                using var testImage = Cv2.ImRead(testImagePath, ImreadModes.Color);
                                if (testImage != null && !testImage.Empty()) {
                                    Console.WriteLine("   Loading test image...");
                                    var testBarcode = await _barcodeDetector.DetectAndDecodeAsync(testImage);

                                    if (!string.IsNullOrEmpty(testBarcode)) {
                                        Console.WriteLine($"   ✓ Test image barcode decoded ({testBarcode.Length} characters)");
                                        Console.WriteLine("   ℹ️ This confirms the barcode decoder is working correctly");
                                        Console.WriteLine("   ℹ️ The issue is with the scanned image quality/positioning");
                                        _logger.LogInformation("✓ Test image processed successfully - barcode decoder is working");

                                        var testIdData = SyrianIdParser.Parse(testBarcode);
                                        Console.WriteLine();
                                        Console.WriteLine("   [TEST IMAGE DATA - For Comparison]");
                                        Console.WriteLine($"   Name: {testIdData.FullNameArabic ?? "N/A"}");
                                        Console.WriteLine($"   ID: {testIdData.NationalId ?? "N/A"}");
                                    } else {
                                        Console.WriteLine("   ❌ Test image also failed");
                                        Console.WriteLine("   ⚠️ This indicates a possible barcode decoder configuration issue");
                                        _logger.LogWarning("Test image barcode detection also failed");
                                    }
                                } else {
                                    Console.WriteLine("   ❌ Failed to load test image");
                                }
                            }
                            catch (Exception testEx) {
                                _logger.LogError(testEx, "Test image processing failed");
                                Console.WriteLine($"   ❌ Test image error: {testEx.Message}");
                            }
                        } else {
                            Console.WriteLine();
                            Console.WriteLine($"💡 To test if barcode decoder is working:");
                            Console.WriteLine($"   1. Place a known good ID image at: {testImagePath}");
                            Console.WriteLine($"   2. Or use: Plustek.exe --test <path_to_image>");
                        }

                        return null;
                    }

                    _logger.LogInformation("✓ Barcode decoded successfully. Length: {Length} characters", rawBarcode.Length);
                    Console.WriteLine($"✓ Barcode decoded ({rawBarcode.Length} characters)");

                    // Parse the Syrian ID data
                    var idData = SyrianIdParser.Parse(rawBarcode);

                    // Display to console
                    Console.WriteLine("\n" + idData.ToString());

                    // Save to text file
                    string txtPath = Path.ChangeExtension(outputPath, ".txt");
                    await File.WriteAllTextAsync(txtPath, idData.ToString(), Encoding.UTF8);
                    Console.WriteLine($"\n✓ Data saved to: {txtPath}");

                    return idData;
                }
                catch (Exception ex) {
                    _logger.LogError(ex, "Exception during barcode detection");
                    Console.WriteLine($"\n⚠️ Barcode detection failed: {ex.Message}");

                    // Try with a known good test image for debugging
                    string testImagePath = @"D:\Plustek\test_images\known_good_id.jpg";
                    if (File.Exists(testImagePath)) {
                        Console.WriteLine($"\n🧪 Trying with test image: {testImagePath}");
                        _logger.LogInformation("Attempting barcode detection with test image: {Path}", testImagePath);

                        try {
                            using var testImage = Cv2.ImRead(testImagePath, ImreadModes.Color);
                            if (testImage != null && !testImage.Empty()) {
                                var testBarcode = await _barcodeDetector.DetectAndDecodeAsync(testImage);

                                if (!string.IsNullOrEmpty(testBarcode)) {
                                    Console.WriteLine($"✓ Test image barcode decoded ({testBarcode.Length} characters)");
                                    var testIdData = SyrianIdParser.Parse(testBarcode);
                                    Console.WriteLine("\n[TEST IMAGE RESULT]");
                                    Console.WriteLine(testIdData.ToString());
                                    _logger.LogInformation("✓ Test image processed successfully - barcode decoder is working");
                                } else {
                                    Console.WriteLine("❌ Test image also failed - possible barcode decoder issue");
                                    _logger.LogWarning("Test image barcode detection also failed");
                                }
                            }
                        }
                        catch (Exception testEx) {
                            _logger.LogError(testEx, "Test image processing also failed");
                            Console.WriteLine($"❌ Test image error: {testEx.Message}");
                        }
                    } else {
                        Console.WriteLine($"💡 To test barcode decoder, place a known good ID image at: {testImagePath}");
                    }

                    return null;
                }
                finally {
                    image?.Dispose();
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Exception during scan and parse operation");
                return null;
            }
        }

        private async Task<string[]?> GetDeviceListAsync() {
            if (_cachedDeviceList != null && _cachedDeviceList.Length > 0) {
                return _cachedDeviceList;
            }

            var request = new { type = "call", func = "LibWFX_GetDeviesList" };
            var response = await SendRequestAsync<DeviceListResponse>(request);

            if (response?.data?.err_code == 0) {
                string messageJson = response.data.message.GetRawText();

                DeviceMessage? deviceMessage = null;
                try {
                    deviceMessage = JsonSerializer.Deserialize<DeviceMessage>(messageJson);
                }
                catch {
                    try {
                        string unescapedJson = JsonSerializer.Deserialize<string>(messageJson) ?? "";
                        deviceMessage = JsonSerializer.Deserialize<DeviceMessage>(unescapedJson);
                    }
                    catch (Exception ex) {
                        _logger.LogError(ex, "Failed to parse device list");
                    }
                }

                if (deviceMessage?.szDevicesList != null) {
                    _cachedDeviceList = deviceMessage.szDevicesList;
                }

                return deviceMessage?.szDevicesList;
            }
            return null;
        }

        public async Task DisconnectAsync() {
            try {
                if (_webSocket != null && _webSocket.State == WebSocketState.Open) {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", _cts.Token);
                    _logger.LogInformation("Disconnected from Plustek server");
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error during disconnect");
            }
            finally {
                _webSocket?.Dispose();
                _webSocket = null;
            }
        }

        /// <summary>
        /// Test method - Process an existing image file for barcode detection
        /// </summary>
        public async Task<SyrianIdData?> TestImageAsync(string imagePath) {
            if (!File.Exists(imagePath)) {
                _logger.LogError("Image file not found: {Path}", imagePath);
                Console.WriteLine($"❌ Image not found: {imagePath}");
                return null;
            }

            Console.WriteLine($"🔍 Testing image: {imagePath}");

            Mat? image = null;
            try {
                image = Cv2.ImRead(imagePath, ImreadModes.Color);
                if (image == null || image.Empty()) {
                    _logger.LogError("Failed to load image");
                    Console.WriteLine("❌ Failed to load image");
                    return null;
                }

                Console.WriteLine($"✓ Image loaded: {image.Width}x{image.Height}");
                Console.WriteLine("🔍 Detecting barcode...");

                var rawBarcode = await _barcodeDetector.DetectAndDecodeAsync(image);

                if (string.IsNullOrEmpty(rawBarcode)) {
                    _logger.LogWarning("No barcode detected");
                    Console.WriteLine("❌ No barcode detected");
                    return null;
                }

                _logger.LogInformation("✓ Barcode decoded: {Length} characters", rawBarcode.Length);
                Console.WriteLine($"✓ Barcode decoded ({rawBarcode.Length} characters)");

                // Parse the Syrian ID data
                var idData = SyrianIdParser.Parse(rawBarcode);

                // Display to console
                Console.WriteLine("\n" + idData.ToString());

                // Save to text file
                string txtPath = Path.ChangeExtension(imagePath, ".txt");
                await File.WriteAllTextAsync(txtPath, idData.ToString(), Encoding.UTF8);
                Console.WriteLine($"\n✓ Data saved to: {txtPath}");

                return idData;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error testing image");
                Console.WriteLine($"❌ Error: {ex.Message}");
                return null;
            }
            finally {
                image?.Dispose();
            }
        }

        private async Task<T?> SendRequestAsync<T>(object request, int timeout = 10000) where T : class {
            if (_webSocket == null || _webSocket.State != WebSocketState.Open) {
                throw new InvalidOperationException("WebSocket is not connected");
            }

            string jsonRequest = JsonSerializer.Serialize(request);
            byte[] requestBytes = Encoding.UTF8.GetBytes(jsonRequest);
            _logger.LogDebug("Sending request: {Request}", jsonRequest);

            await _webSocket.SendAsync(new ArraySegment<byte>(requestBytes), WebSocketMessageType.Text, true, _cts.Token);

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, timeoutCts.Token);

            var buffer = new byte[1024 * 64];
            var responseBuilder = new StringBuilder();

            try {
                while (true) {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), linkedCts.Token);

                    if (result.MessageType == WebSocketMessageType.Close) {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        throw new Exception("WebSocket closed by server");
                    }

                    responseBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (result.EndOfMessage) break;
                }

                string jsonResponse = responseBuilder.ToString();
                _logger.LogDebug("Received response: {Response}", jsonResponse);
                return JsonSerializer.Deserialize<T>(jsonResponse);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested) {
                throw new TimeoutException($"Request timed out after {timeout}ms");
            }
        }

        // Response models
        private class ServerResponse {
            public string type { get; set; } = "";
            public string func { get; set; } = "";
            public ResponseData? data { get; set; }
        }

        private class ResponseData {
            public int err_code { get; set; }
            public object? message { get; set; }
        }

        private class InitResponse : ServerResponse {
            public new InitData? data { get; set; }
        }

        private class InitData {
            public int err_code { get; set; }
            public string? message { get; set; }
        }

        private class DeviceListResponse : ServerResponse {
            public new DeviceListData? data { get; set; }
        }

        private class DeviceListData {
            public int err_code { get; set; }
            public JsonElement message { get; set; }
        }

        private class DeviceMessage {
            public string[]? szDevicesList { get; set; }
            public string[]? szSerialList { get; set; }
        }

        private class ScanResponse : ServerResponse {
            public new ResponseData? data { get; set; }
        }
    }
}