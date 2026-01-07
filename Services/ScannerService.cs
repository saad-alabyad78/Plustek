using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Plustek.Configuration;
using Plustek.Interfaces;
using Plustek.Models;

namespace Plustek.Services {
    public class ScannerService : IScanner {
        private readonly AppSettings _settings;
        private ClientWebSocket? _webSocket;
        private readonly CancellationTokenSource _cts = new();
        private string[]? _cachedDeviceList;

        public ScannerService(AppSettings settings) {
            _settings = settings;
        }

        public async Task<bool> ConnectAsync() {
            try {
                Console.WriteLine($"[DEBUG] Connecting to {_settings.WebSocketUrl}");
                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri(_settings.WebSocketUrl), _cts.Token);

                if (_webSocket.State == WebSocketState.Open) {
                    Console.WriteLine($"[DEBUG] Connected - State: {_webSocket.State}");
                    return true;
                }
                return false;
            }
            catch (Exception ex) {
                Console.WriteLine($"[ERROR] Connection error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> InitializeAsync() {
            try {
                Console.WriteLine("[DEBUG] Initializing scanner...");
                var request = new { type = "call", func = "LibWFX_Init" };
                var response = await SendRequestAsync<InitResponse>(request);

                Console.WriteLine($"[DEBUG] Init response: err_code={response?.data?.err_code}");

                if (response?.data?.err_code == 0 ||
                    (response?.data?.err_code >= 1001 && response?.data?.err_code <= 1003)) {
                    Console.WriteLine("[DEBUG] Scanner initialized successfully");
                    return true;
                }

                Console.WriteLine($"[ERROR] Init failed with error code: {response?.data?.err_code}");
                return false;
            }
            catch (Exception ex) {
                Console.WriteLine($"[ERROR] Initialize error: {ex.Message}");
                return false;
            }
        }

        public async Task<ScanResult?> ScanAsync(string outputPath) {
            try {
                Console.WriteLine("[DEBUG] Getting device list...");
                var deviceList = await GetDeviceListAsync();

                if (deviceList == null || deviceList.Length == 0) {
                    return new ScanResult {
                        Success = false,
                        Error = "No scanner devices found"
                    };
                }

                string deviceName = deviceList[0];
                Console.WriteLine($"[DEBUG] Using device: {deviceName}");

                // Configure scanner - IMPORTANT: Use Dictionary with hyphenated keys
                var scanData = new Dictionary<string, object> {
                    { "device-name", deviceName },
                    { "source", "Camera" },
                    { "paper-size", _settings.PaperSize },
                    { "resolution", _settings.ScanResolution },
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

                Console.WriteLine("[DEBUG] Configuring scanner...");
                var configResponse = await SendRequestAsync<ServerResponse>(scanConfig);

                Console.WriteLine($"[DEBUG] Config response: err_code={configResponse?.data?.err_code}");

                if (configResponse?.data?.err_code != 0) {
                    return new ScanResult {
                        Success = false,
                        Error = $"Config failed: {configResponse?.data?.err_code}"
                    };
                }

                Console.WriteLine("[DEBUG] Starting scan...");
                var scanRequest = new { type = "call", func = "LibWFX_AsynchronizeScan" };
                var scanResponse = await SendRequestAsync<ScanResponse>(scanRequest, _settings.ScanTimeout);

                Console.WriteLine($"[DEBUG] Scan response: err_code={scanResponse?.data?.err_code}");

                if (scanResponse?.data?.err_code != 0) {
                    return new ScanResult {
                        Success = false,
                        Error = $"Scan failed: {scanResponse?.data?.err_code}"
                    };
                }

                Console.WriteLine("[DEBUG] Scan completed, waiting for file...");
                await Task.Delay(_settings.FileWaitDelay);

                // Get newest file
                if (!Directory.Exists(_settings.TempScanDirectory)) {
                    return new ScanResult {
                        Success = false,
                        Error = $"Temp directory not found: {_settings.TempScanDirectory}"
                    };
                }

                var allFiles = Directory.GetFiles(_settings.TempScanDirectory, "IMG_*.jpg");
                Console.WriteLine($"[DEBUG] Found {allFiles.Length} files");

                if (allFiles.Length == 0) {
                    return new ScanResult {
                        Success = false,
                        Error = "No scan file found"
                    };
                }

                var newestFile = allFiles
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(fi => fi.LastWriteTime)
                    .First();

                Console.WriteLine($"[DEBUG] Newest file: {newestFile.Name}");

                File.Copy(newestFile.FullName, outputPath, overwrite: true);
                Console.WriteLine($"[DEBUG] File copied to: {outputPath}");

                return new ScanResult {
                    Success = true,
                    ImagePath = outputPath
                };
            }
            catch (Exception ex) {
                Console.WriteLine($"[ERROR] Scan error: {ex.Message}");
                return new ScanResult {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task DisconnectAsync() {
            try {
                if (_webSocket != null && _webSocket.State == WebSocketState.Open) {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", _cts.Token);
                }
            }
            catch { }
            finally {
                _webSocket?.Dispose();
                _webSocket = null;
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
                Console.WriteLine($"[DEBUG] Device message: {messageJson}");

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
                        Console.WriteLine($"[ERROR] Failed to parse device message: {ex.Message}");
                    }
                }

                if (deviceMessage?.szDevicesList != null) {
                    _cachedDeviceList = deviceMessage.szDevicesList;
                    Console.WriteLine($"[DEBUG] Found {_cachedDeviceList.Length} devices");

                    for (int i = 0; i < _cachedDeviceList.Length; i++) {
                        Console.WriteLine($"  - {_cachedDeviceList[i]}");
                    }
                }

                return deviceMessage?.szDevicesList;
            }
            return null;
        }

        // KEY FIX: This SendRequestAsync is based on your WORKING code
        private async Task<T?> SendRequestAsync<T>(object request, int timeout = 10000) where T : class {
            if (_webSocket == null || _webSocket.State != WebSocketState.Open) {
                throw new InvalidOperationException("WebSocket is not connected");
            }

            string jsonRequest = JsonSerializer.Serialize(request);
            byte[] requestBytes = Encoding.UTF8.GetBytes(jsonRequest);

            Console.WriteLine($"[DEBUG] Sending: {jsonRequest}");

            await _webSocket.SendAsync(
                new ArraySegment<byte>(requestBytes),
                WebSocketMessageType.Text,
                true,
                _cts.Token
            );

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, timeoutCts.Token);

            var buffer = new byte[1024 * 64];
            var responseBuilder = new StringBuilder();

            try {
                while (true) {
                    var result = await _webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        linkedCts.Token
                    );

                    if (result.MessageType == WebSocketMessageType.Close) {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        throw new Exception("WebSocket closed by server");
                    }

                    responseBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    if (result.EndOfMessage) {
                        break;
                    }
                }

                string jsonResponse = responseBuilder.ToString();
                Console.WriteLine($"[DEBUG] Received: {jsonResponse}");

                return JsonSerializer.Deserialize<T>(jsonResponse);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested) {
                throw new TimeoutException($"Request timed out after {timeout}ms");
            }
        }

        #region Response Models

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

        #endregion
    }
}