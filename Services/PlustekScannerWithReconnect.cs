using Plustek.Interfaces;
using Plustek.Models;
using System;
using System.Threading.Tasks;

namespace Plustek.Services {
    /// <summary>
    /// Wrapper around IScanner that automatically handles reconnection on connection errors
    /// </summary>
    public class PlustekScannerWithReconnect : IScanner {
        private readonly IScanner _innerScanner;
        private readonly int _maxRetries;
        private readonly int _retryDelayMs;

        public PlustekScannerWithReconnect(IScanner innerScanner, int maxRetries = 3, int retryDelayMs = 2000) {
            _innerScanner = innerScanner;
            _maxRetries = maxRetries;
            _retryDelayMs = retryDelayMs;
        }

        public async Task<bool> ConnectAsync() {
            return await ExecuteWithRetryAsync(
                async () => await _innerScanner.ConnectAsync(),
                "ConnectAsync",
                shouldReconnect: false // Don't reconnect for connection attempts
            );
        }

        public async Task<bool> InitializeAsync() {
            return await ExecuteWithRetryAsync(
                async () => await _innerScanner.InitializeAsync(),
                "InitializeAsync"
            );
        }

        public async Task<string?> GetDeviceSerialNumberAsync() {
            return await ExecuteWithRetryAsync(
                async () => await _innerScanner.GetDeviceSerialNumberAsync(),
                "GetDeviceSerialNumberAsync"
            );
        }

        public async Task<ScanResult?> ScanAsync(string outputPath) {
            return await ExecuteWithRetryAsync(
                async () => await _innerScanner.ScanAsync(outputPath),
                "ScanAsync"
            );
        }

        public async Task DisconnectAsync() {
            try {
                await _innerScanner.DisconnectAsync();
            }
            catch (Exception ex) {
                Console.WriteLine($"[PlustekWrapper] Disconnect error (ignored): {ex.Message}");
            }
        }

        private async Task<T> ExecuteWithRetryAsync<T>(
            Func<Task<T>> operation,
            string operationName,
            bool shouldReconnect = true) {

            int attemptCount = 0;

            while (attemptCount < _maxRetries) {
                try {
                    attemptCount++;
                    return await operation();
                }
                catch (Exception ex) {
                    Console.WriteLine($"[PlustekWrapper] {operationName} failed (attempt {attemptCount}/{_maxRetries}): {ex.Message}");

                    if (attemptCount >= _maxRetries) {
                        Console.WriteLine($"[PlustekWrapper] Max retries reached for {operationName}. Giving up.");
                        throw;
                    }

                    if (!shouldReconnect) {
                        Console.WriteLine($"[PlustekWrapper] Not attempting reconnection for {operationName}");
                        throw;
                    }

                    // Connection error - attempt to reconnect
                    Console.WriteLine($"[PlustekWrapper] Attempting to reconnect... (retry {attemptCount}/{_maxRetries})");
                    await Task.Delay(_retryDelayMs);

                    try {
                        // Disconnect first
                        await _innerScanner.DisconnectAsync();
                    }
                    catch {
                        // Ignore disconnect errors
                    }

                    // Try to reconnect
                    if (!await _innerScanner.ConnectAsync()) {
                        Console.WriteLine($"[PlustekWrapper] Reconnection failed");
                        continue;
                    }

                    Console.WriteLine($"[PlustekWrapper] Reconnected successfully");

                    // Reinitialize
                    if (!await _innerScanner.InitializeAsync()) {
                        Console.WriteLine($"[PlustekWrapper] Re-initialization failed");
                        continue;
                    }

                    Console.WriteLine($"[PlustekWrapper] Re-initialized successfully. Retrying {operationName}...");
                }
            }

            throw new InvalidOperationException($"Failed to execute {operationName} after {_maxRetries} attempts");
        }
    }
}