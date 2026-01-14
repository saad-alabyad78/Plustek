using Plustek.Configuration;
using Plustek.Interfaces;
using Plustek.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Plustek.Services {
    public class ScannerService : IScanner {
        private readonly AppSettings _settings;
        private ClientWebSocket? _webSocket;
        private readonly CancellationTokenSource _cts = new();
        private string[]? _cachedDeviceList;
        private string[]? _cachedSerialList;

        public ScannerService(AppSettings settings) {
            _settings = settings;
        }

        public async Task<bool> ConnectAsync() {
            try {
                Console.WriteLine($"[ScannerService] Connecting to {_settings.WebSocketUrl}");
                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri(_settings.WebSocketUrl), _cts.Token);

                if (_webSocket.State == WebSocketState.Open) {
                    Console.WriteLine($"[ScannerService] ✓ Connected - State: {_webSocket.State}");
                    return true;
                }

                Console.WriteLine($"[ScannerService] ✗ Connection failed - State: {_webSocket.State}");
                return false;
            }
            catch (Exception ex) {
                Console.WriteLine($"[ScannerService] ✗ Connection error: {ex.Message}");
                throw; // Rethrow so wrapper can handle
            }
        }

        public async Task<bool> InitializeAsync() {
            try {
                Console.WriteLine("[ScannerService] Initializing scanner...");
                var request = new { type = "call", func = "LibWFX_Init" };
                var response = await SendRequestAsync<InitResponse>(request);

                Console.WriteLine($"[ScannerService] Init response: err_code={response?.data?.err_code}");

                if (response?.data?.err_code == 0 ||
                    (response?.data?.err_code >= 1001 && response?.data?.err_code <= 1003)) {
                    Console.WriteLine("[ScannerService] ✓ Scanner initialized successfully");
                    return true;
                }

                Console.WriteLine($"[ScannerService] ✗ Init failed with error code: {response?.data?.err_code}");
                return false;
            }
            catch (Exception ex) {
                Console.WriteLine($"[ScannerService] ✗ Initialize error: {ex.GetType().Name} - {ex.Message}");

                // Rethrow connection errors so wrapper can handle reconnection
                if (IsConnectionError(ex)) {
                    Console.WriteLine("[ScannerService] ⚡ Rethrowing connection error for reconnection handler...");
                    throw;
                }

                return false;
            }
        }

        public async Task<string?> GetDeviceSerialNumberAsync() {
            try {
                Console.WriteLine("[ScannerService] Getting device serial number...");

                // First get the device list which also contains serial numbers
                var request = new { type = "call", func = "LibWFX_GetDeviesList" };
                var response = await SendRequestAsync<DeviceListResponse>(request);

                if (response?.data?.err_code == 0) {
                    string messageJson = response.data.message.GetRawText();
                    Console.WriteLine($"[ScannerService] Device message: {messageJson}");

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
                            Console.WriteLine($"[ScannerService] ✗ Failed to parse device message: {ex.Message}");
                        }
                    }

                    if (deviceMessage?.szSerialList != null && deviceMessage.szSerialList.Length > 0) {
                        _cachedSerialList = deviceMessage.szSerialList;
                        string serialNumber = deviceMessage.szSerialList[0];
                        Console.WriteLine($"[ScannerService] Device serial number: {serialNumber}");
                        return serialNumber;
                    }
                }

                Console.WriteLine("[ScannerService] ✗ Could not retrieve serial number");
                return null;
            }
            catch (Exception ex) {
                Console.WriteLine($"[ScannerService] ✗ Error getting serial number: {ex.Message}");
                return null;
            }
        }

        public async Task<ScanResult?> ScanAsync(string outputPath) {
            try {
                Console.WriteLine("[ScannerService] Getting device list...");
                var deviceList = await GetDeviceListAsync();

                if (deviceList == null || deviceList.Length == 0) {
                    return new ScanResult {
                        Success = false,
                        Error = "No scanner devices found"
                    };
                }

                string deviceName = deviceList[0];
                Console.WriteLine($"[ScannerService] Using device: {deviceName}");

                // Configure scanner
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

                Console.WriteLine("[ScannerService] Configuring scanner...");
                var configResponse = await SendRequestAsync<ServerResponse>(scanConfig);

                Console.WriteLine($"[ScannerService] Config response: err_code={configResponse?.data?.err_code}");

                if (configResponse?.data?.err_code != 0) {
                    return new ScanResult {
                        Success = false,
                        Error = $"Config failed: {configResponse?.data?.err_code}"
                    };
                }

                Console.WriteLine("[ScannerService] Starting scan...");
                var scanRequest = new { type = "call", func = "LibWFX_AsynchronizeScan" };
                var scanResponse = await SendRequestAsync<ScanResponse>(scanRequest, _settings.ScanTimeout);

                Console.WriteLine($"[ScannerService] Scan response: err_code={scanResponse?.data?.err_code}");

                if (scanResponse?.data?.err_code != 0) {
                    return new ScanResult {
                        Success = false,
                        Error = $"Scan failed: {scanResponse?.data?.err_code}"
                    };
                }

                Console.WriteLine("[ScannerService] Scan completed, waiting for file...");
                await Task.Delay(_settings.FileWaitDelay);

                // Get newest file
                if (!Directory.Exists(_settings.TempScanDirectory)) {
                    return new ScanResult {
                        Success = false,
                        Error = $"Temp directory not found: {_settings.TempScanDirectory}"
                    };
                }

                var allFiles = Directory.GetFiles(_settings.TempScanDirectory, "IMG_*.jpg");
                Console.WriteLine($"[ScannerService] Found {allFiles.Length} files");

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

                Console.WriteLine($"[ScannerService] Newest file: {newestFile.Name}");

                File.Copy(newestFile.FullName, outputPath, overwrite: true);
                Console.WriteLine($"[ScannerService] ✓ File copied to: {outputPath}");

                return new ScanResult {
                    Success = true,
                    ImagePath = outputPath
                };
            }
            catch (Exception ex) {
                Console.WriteLine($"[ScannerService] ✗ Scan exception: {ex.GetType().Name}");
                Console.WriteLine($"[ScannerService] ✗ Message: {ex.Message}");

                // CRITICAL FIX: Rethrow connection/websocket errors
                if (IsConnectionError(ex)) {
                    Console.WriteLine("[ScannerService] ⚡ Rethrowing connection error for reconnection handler...");
                    throw; // Let PlustekScannerWithReconnect handle it
                }

                // For non-connection errors, return error result
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
                    Console.WriteLine("[ScannerService] Disconnected");
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"[ScannerService] Disconnect error (ignored): {ex.Message}");
            }
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
                Console.WriteLine($"[ScannerService] Device message: {messageJson}");

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
                        Console.WriteLine($"[ScannerService] ✗ Failed to parse device message: {ex.Message}");
                    }
                }

                if (deviceMessage?.szDevicesList != null) {
                    _cachedDeviceList = deviceMessage.szDevicesList;
                    _cachedSerialList = deviceMessage.szSerialList;

                    Console.WriteLine($"[ScannerService] Found {_cachedDeviceList.Length} devices");

                    for (int i = 0; i < _cachedDeviceList.Length; i++) {
                        string serial = _cachedSerialList != null && i < _cachedSerialList.Length
                            ? _cachedSerialList[i]
                            : "Unknown";
                        Console.WriteLine($"  - {_cachedDeviceList[i]} (Serial: {serial})");
                    }
                }

                return deviceMessage?.szDevicesList;
            }
            return null;
        }

        private async Task<T?> SendRequestAsync<T>(object request, int timeout = 10000) where T : class {
            if (_webSocket == null || _webSocket.State != WebSocketState.Open) {
                throw new InvalidOperationException("WebSocket is not connected");
            }

            string jsonRequest = JsonSerializer.Serialize(request);
            byte[] requestBytes = Encoding.UTF8.GetBytes(jsonRequest);

            Console.WriteLine($"[ScannerService] Sending: {jsonRequest}");

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
                        throw new WebSocketException("WebSocket closed by server during receive");
                    }

                    responseBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    if (result.EndOfMessage) {
                        break;
                    }
                }

                string jsonResponse = responseBuilder.ToString();
                Console.WriteLine($"[ScannerService] Received: {jsonResponse}");

                return JsonSerializer.Deserialize<T>(jsonResponse);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested) {
                throw new TimeoutException($"Request timed out after {timeout}ms");
            }
        }

        /// <summary>
        /// Determines if an exception is a connection error that should trigger reconnection
        /// </summary>
        private bool IsConnectionError(Exception ex) {
            var message = ex.Message.ToLower();
            var typeName = ex.GetType().Name.ToLower();

            return message.Contains("websocket") ||
                   message.Contains("web socket") ||
                   message.Contains("handshake") ||
                   message.Contains("close handshake") ||
                   message.Contains("closed by server") ||
                   message.Contains("connection") ||
                   message.Contains("socket") ||
                   message.Contains("timeout") ||
                   message.Contains("network") ||
                   message.Contains("disconnect") ||
                   typeName.Contains("websocket") ||
                   typeName.Contains("socket") ||
                   ex is WebSocketException ||
                   ex is OperationCanceledException;
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