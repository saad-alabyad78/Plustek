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
            var options = new JsonSerializerOptions {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var json = JsonSerializer.Serialize(data, options);
            await File.WriteAllTextAsync(path, json, Encoding.UTF8);
        }

        private async Task SaveTextAsync(SyrianIdData data, string path) {
            var text = new StringBuilder();
            text.AppendLine("=== SYRIAN ID CARD - RAW FIELDS ===");
            text.AppendLine();
            text.AppendLine($"Total Fields: {data.Fields.Count}");
            text.AppendLine();

            for (int i = 0; i < data.Fields.Count; i++) {
                text.AppendLine($"Field [{i:D2}]: {data.Fields[i]}");
            }

            await File.WriteAllTextAsync(path, text.ToString(), Encoding.UTF8);
        }

        private async Task SaveHtmlAsync(SyrianIdData data, string path) {
            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html>");
            html.AppendLine("<head>");
            html.AppendLine("    <meta charset='UTF-8'>");
            html.AppendLine("    <title>Syrian ID Card</title>");
            html.AppendLine("    <style>");
            html.AppendLine("        body { font-family: Arial; margin: 20px; direction: rtl; }");
            html.AppendLine("        .field { margin: 10px 0; padding: 10px; background: #f5f5f5; border-radius: 5px; }");
            html.AppendLine("        .label { font-weight: bold; color: #333; }");
            html.AppendLine("        h1 { color: #2c3e50; }");
            html.AppendLine("    </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("    <h1>Syrian National ID - All Fields</h1>");
            html.AppendLine($"    <p><strong>Total Fields:</strong> {data.Fields.Count}</p>");

            for (int i = 0; i < data.Fields.Count; i++) {
                html.AppendLine($"    <div class='field'>");
                html.AppendLine($"        <span class='label'>Field [{i:D2}]:</span> {data.Fields[i]}");
                html.AppendLine($"    </div>");
            }

            html.AppendLine("</body>");
            html.AppendLine("</html>");

            await File.WriteAllTextAsync(path, html.ToString(), Encoding.UTF8);
        }
    }
}