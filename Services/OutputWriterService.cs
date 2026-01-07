using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Plustek.Interfaces;
using Plustek.Models;

namespace Plustek.Services {
    public class OutputWriterService : IOutputWriter {
        public async Task SaveAsync(SyrianIdData data, string imagePath) {
            string basePath = Path.ChangeExtension(imagePath, null);

            await SaveJsonAsync(data, basePath + ".json");
            await SaveTextAsync(data, basePath + ".txt");
            await SaveHtmlAsync(data, basePath + ".html");
        }

        private async Task SaveJsonAsync(SyrianIdData data, string path) {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(path, json, Encoding.UTF8);
        }

        private async Task SaveTextAsync(SyrianIdData data, string path) {
            var text = new StringBuilder();
            text.AppendLine("=== SYRIAN ID CARD ===");
            text.AppendLine($"National ID: {data.NationalId}");
            text.AppendLine($"Name (Arabic): {data.FullNameArabic}");
            text.AppendLine($"Name (English): {data.FullNameEnglish}");
            text.AppendLine($"Date of Birth: {data.DateOfBirth:dd-MM-yyyy}");
            text.AppendLine($"Gender: {data.Gender}");
            text.AppendLine($"Address: {data.AddressArabic}");

            await File.WriteAllTextAsync(path, text.ToString(), Encoding.UTF8);
        }

        private async Task SaveHtmlAsync(SyrianIdData data, string path) {
            var html = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <title>Syrian ID Card</title>
    <style>
        body {{ font-family: Arial; margin: 20px; }}
        .field {{ margin: 10px 0; }}
        .label {{ font-weight: bold; }}
    </style>
</head>
<body>
    <h1>Syrian National ID</h1>
    <div class='field'><span class='label'>National ID:</span> {data.NationalId}</div>
    <div class='field'><span class='label'>Name (Arabic):</span> {data.FullNameArabic}</div>
    <div class='field'><span class='label'>Name (English):</span> {data.FullNameEnglish}</div>
    <div class='field'><span class='label'>Date of Birth:</span> {data.DateOfBirth:dd-MM-yyyy}</div>
    <div class='field'><span class='label'>Gender:</span> {data.Gender}</div>
    <div class='field'><span class='label'>Address:</span> {data.AddressArabic}</div>
</body>
</html>";

            await File.WriteAllTextAsync(path, html, Encoding.UTF8);
        }
    }
}