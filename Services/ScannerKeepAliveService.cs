// File: Plustek/Services/ScannerKeepAliveService.cs
using Plustek.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Plustek.Services {
    /// <summary>
    /// Service that sends periodic heartbeat to keep scanner connection alive
    /// </summary>
    public class ScannerKeepAliveService : IDisposable {
        private readonly IScanner _scanner;
        private readonly TimeSpan _interval;
        private Timer? _timer;
        private bool _isRunning;

        public ScannerKeepAliveService(IScanner scanner, TimeSpan? interval = null) {
            _scanner = scanner;
            _interval = interval ?? TimeSpan.FromSeconds(30); // Heartbeat every 30 seconds
        }

        public void Start() {
            if (_isRunning) return;

            Console.WriteLine($"[KeepAlive] Starting heartbeat service (interval: {_interval.TotalSeconds}s)");

            _timer = new Timer(
                async _ => await SendHeartbeatAsync(),
                null,
                _interval,
                _interval
            );

            _isRunning = true;
        }

        public void Stop() {
            if (!_isRunning) return;

            Console.WriteLine("[KeepAlive] Stopping heartbeat service");

            _timer?.Dispose();
            _timer = null;
            _isRunning = false;
        }

        private async Task SendHeartbeatAsync() {
            try {
                Console.WriteLine("[KeepAlive] Sending heartbeat...");

                // Try to initialize (lightweight operation to test connection)
                // You can replace this with any lightweight SDK call
                await _scanner.InitializeAsync();

                Console.WriteLine("[KeepAlive] ✓ Heartbeat successful");
            }
            catch (Exception ex) {
                Console.WriteLine($"[KeepAlive] ✗ Heartbeat failed: {ex.Message}");

                // Optionally: Attempt to reconnect
                try {
                    Console.WriteLine("[KeepAlive] Attempting to reconnect...");
                    await _scanner.DisconnectAsync();
                    await Task.Delay(1000);
                    await _scanner.ConnectAsync();
                    await _scanner.InitializeAsync();
                    Console.WriteLine("[KeepAlive] Reconnection successful");
                }
                catch (Exception reconnectEx) {
                    Console.WriteLine($"[KeepAlive] Reconnection failed: {reconnectEx.Message}");
                }
            }
        }

        public void Dispose() {
            Stop();
        }
    }
}