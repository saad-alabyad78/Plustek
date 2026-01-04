using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Plustek.Models;
using System.Web;

namespace Plustek.Services {
    public interface IOutputWriter {
        Task SaveToFileAsync(SyrianIdData idData, string imagePath);
    }

    public class HtmlOutputService : IOutputWriter {
        private readonly ILogger<HtmlOutputService> _logger;

        public HtmlOutputService(ILogger<HtmlOutputService> logger) {
            _logger = logger;
        }

        public async Task SaveToFileAsync(SyrianIdData idData, string imagePath) {
            try {
                // Get the project directory
                string projectDir = Directory.GetCurrentDirectory();
                while (projectDir.Contains("bin") && Directory.GetParent(projectDir) != null) {
                    projectDir = Directory.GetParent(projectDir).FullName;
                }

                // Create output filename
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string nationalId = idData.NationalId ?? "Unknown";
                string htmlFileName = $"ID_Output_{nationalId}_{timestamp}.html";
                string htmlPath = Path.Combine(projectDir, htmlFileName);

                // Build HTML content
                string htmlContent = GenerateHtml(idData, imagePath, timestamp);

                // Write to file with UTF-8 encoding
                await File.WriteAllTextAsync(htmlPath, htmlContent, new UTF8Encoding(true));

                _logger.LogInformation("HTML output saved to: {Path}", htmlPath);
                Console.WriteLine($"\n✓ Output saved to: {htmlPath}");
                Console.WriteLine($"  Open this file in your web browser to see Arabic text correctly.");

                // Save complete object data as JSON
                string jsonFileName = $"ID_Output_{nationalId}_{timestamp}.json";
                string jsonPath = Path.Combine(projectDir, jsonFileName);
                await File.WriteAllTextAsync(jsonPath, GenerateJsonOutput(idData, imagePath), new UTF8Encoding(true));
                Console.WriteLine($"✓ JSON version saved to: {jsonFileName}");

                // Also save a simple text version with ALL DATA
                string txtFileName = $"ID_Output_{nationalId}_{timestamp}.txt";
                string txtPath = Path.Combine(projectDir, txtFileName);
                await File.WriteAllTextAsync(txtPath, GenerateCompleteTextOutput(idData, imagePath), new UTF8Encoding(true));
                Console.WriteLine($"✓ Complete text version saved to: {txtFileName}");

            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to save output files");
                Console.WriteLine($"\n✗ Failed to save output files: {ex.Message}");
            }
        }

        private string GenerateJsonOutput(SyrianIdData idData, string imagePath) {
            var output = new {
                ScanDate = DateTime.Now.ToString("dd-MM-yyyy HH:mm:ss"),
                SourceImage = Path.GetFileName(imagePath),
                IdData = idData
            };

            var options = new JsonSerializerOptions {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            return JsonSerializer.Serialize(output, options);
        }

        private string GenerateCompleteTextOutput(SyrianIdData idData, string imagePath) {
            var sb = new StringBuilder();
            sb.AppendLine("╔════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║         SYRIAN NATIONAL ID - COMPLETE DATA DUMP            ║");
            sb.AppendLine("╚════════════════════════════════════════════════════════════╝");
            sb.AppendLine();
            sb.AppendLine($"Scan Date: {DateTime.Now:dd-MM-yyyy HH:mm:ss}");
            sb.AppendLine($"Source Image: {Path.GetFileName(imagePath)}");
            sb.AppendLine();

            // Serialize entire object as JSON for complete data representation
            sb.AppendLine("════════════════════════════════════════════════════════════");
            sb.AppendLine("COMPLETE OBJECT DATA (JSON Format):");
            sb.AppendLine("════════════════════════════════════════════════════════════");
            sb.AppendLine();

            var options = new JsonSerializerOptions {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            sb.AppendLine(JsonSerializer.Serialize(idData, options));
            sb.AppendLine();

            sb.AppendLine("════════════════════════════════════════════════════════════");
            sb.AppendLine("RAW BARCODE DATA (Original String):");
            sb.AppendLine("════════════════════════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine(idData.RawData ?? "N/A");
            sb.AppendLine();

            sb.AppendLine("════════════════════════════════════════════════════════════");
            sb.AppendLine("ALL FIELDS (Split by # delimiter):");
            sb.AppendLine("════════════════════════════════════════════════════════════");
            sb.AppendLine();

            for (int i = 0; i < idData.AllFields.Count; i++) {
                sb.AppendLine($"[{i,2}] {idData.AllFields[i]}");
            }

            return sb.ToString();
        }

        private string GenerateHtml(SyrianIdData idData, string imagePath, string timestamp) {
            return $@"<!DOCTYPE html>
<html lang=""ar"" dir=""rtl"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Syrian ID Scan Result - {idData.NationalId}</title>
    <style>
        * {{
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }}
        body {{
            font-family: 'Segoe UI', Tahoma, Arial, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            padding: 40px 20px;
            min-height: 100vh;
        }}
        .container {{
            max-width: 900px;
            margin: 0 auto;
            background: white;
            border-radius: 20px;
            box-shadow: 0 20px 60px rgba(0,0,0,0.3);
            overflow: hidden;
        }}
        .header {{
            background: linear-gradient(135deg, #1e3c72 0%, #2a5298 100%);
            color: white;
            padding: 40px;
            text-align: center;
        }}
        .header h1 {{
            font-size: 28px;
            margin-bottom: 10px;
            text-shadow: 2px 2px 4px rgba(0,0,0,0.3);
        }}
        .header p {{
            opacity: 0.9;
            font-size: 14px;
        }}
        .content {{
            padding: 40px;
        }}
        .section {{
            margin-bottom: 30px;
        }}
        .section-title {{
            font-size: 20px;
            color: #2a5298;
            margin-bottom: 20px;
            padding-bottom: 10px;
            border-bottom: 3px solid #667eea;
            font-weight: bold;
        }}
        .info-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
            gap: 20px;
        }}
        .info-item {{
            background: #f8f9fa;
            padding: 20px;
            border-radius: 10px;
            border-left: 4px solid #667eea;
        }}
        .info-label {{
            font-size: 12px;
            color: #6c757d;
            text-transform: uppercase;
            margin-bottom: 8px;
            font-weight: 600;
        }}
        .info-value {{
            font-size: 18px;
            color: #212529;
            font-weight: 500;
            direction: rtl;
        }}
        .info-value.arabic {{
            font-size: 22px;
            font-family: 'Traditional Arabic', 'Arial', sans-serif;
        }}
        .field-list {{
            background: #f8f9fa;
            padding: 20px;
            border-radius: 10px;
            max-height: 400px;
            overflow-y: auto;
        }}
        .field-item {{
            padding: 10px;
            margin-bottom: 8px;
            background: white;
            border-radius: 5px;
            font-family: 'Courier New', monospace;
            font-size: 13px;
            direction: ltr;
            text-align: left;
        }}
        .field-index {{
            display: inline-block;
            background: #667eea;
            color: white;
            padding: 2px 8px;
            border-radius: 3px;
            font-size: 11px;
            margin-right: 10px;
            font-weight: bold;
        }}
        .raw-data {{
            background: #2d3436;
            color: #00ff00;
            padding: 20px;
            border-radius: 10px;
            font-family: 'Courier New', monospace;
            font-size: 12px;
            overflow-x: auto;
            white-space: pre-wrap;
            word-break: break-all;
        }}
        .json-data {{
            background: #282c34;
            color: #abb2bf;
            padding: 20px;
            border-radius: 10px;
            font-family: 'Courier New', monospace;
            font-size: 12px;
            overflow-x: auto;
            white-space: pre;
            direction: ltr;
            text-align: left;
        }}
        .footer {{
            background: #f8f9fa;
            padding: 20px;
            text-align: center;
            color: #6c757d;
            font-size: 12px;
        }}
        .timestamp {{
            background: #e9ecef;
            padding: 10px;
            border-radius: 5px;
            margin-bottom: 20px;
            text-align: center;
            font-size: 13px;
            color: #495057;
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>🆔 Syrian National ID - Barcode Scan Result</h1>
            <p>Plustek Barcode Scanner v1.0</p>
        </div>
        
        <div class=""content"">
            <div class=""timestamp"">
                📅 Scan Date: {DateTime.Now:dd-MM-yyyy HH:mm:ss} | 📁 Source: {Path.GetFileName(imagePath)}
            </div>

            <div class=""section"">
                <h2 class=""section-title"">📋 ID Information</h2>
                <div class=""info-grid"">
                    <div class=""info-item"">
                        <div class=""info-label"">National ID Number</div>
                        <div class=""info-value"">{idData.NationalId ?? "N/A"}</div>
                    </div>
                    <div class=""info-item"">
                        <div class=""info-label"">Date of Birth</div>
                        <div class=""info-value"">{idData.DateOfBirth?.ToString("dd-MM-yyyy") ?? "N/A"}</div>
                    </div>
                    <div class=""info-item"">
                        <div class=""info-label"">Gender</div>
                        <div class=""info-value"">{idData.Gender ?? "N/A"}</div>
                    </div>
                    <div class=""info-item"">
                        <div class=""info-label"">Full Name (Arabic)</div>
                        <div class=""info-value arabic"">{idData.FullNameArabic ?? "N/A"}</div>
                    </div>
                    <div class=""info-item"">
                        <div class=""info-label"">Full Name (English)</div>
                        <div class=""info-value"">{idData.FullNameEnglish ?? "N/A"}</div>
                    </div>
                    <div class=""info-item"">
                        <div class=""info-label"">Address (Arabic)</div>
                        <div class=""info-value arabic"">{idData.AddressArabic ?? "N/A"}</div>
                    </div>
                </div>
            </div>

            <div class=""section"">
                <h2 class=""section-title"">📦 Complete Object Data (JSON)</h2>
                <div class=""json-data"">{System.Web.HttpUtility.HtmlEncode(GenerateJsonOutput(idData, imagePath))}</div>
            </div>

            <div class=""section"">
                <h2 class=""section-title"">🔍 Barcode Field Details</h2>
                <div class=""field-list"">
                    {GenerateFieldListHtml(idData.AllFields)}
                </div>
            </div>

            <div class=""section"">
                <h2 class=""section-title"">💾 Raw Barcode Data</h2>
                <div class=""raw-data"">{System.Web.HttpUtility.HtmlEncode(idData.RawData)}</div>
            </div>
        </div>

        <div class=""footer"">
            Generated by Plustek Barcode Scanner | {DateTime.Now:yyyy}
        </div>
    </div>
</body>
</html>";
        }

        private string GenerateFieldListHtml(System.Collections.Generic.List<string> fields) {
            var sb = new StringBuilder();
            for (int i = 0; i < fields.Count; i++) {
                sb.AppendLine($@"<div class=""field-item"">
                    <span class=""field-index"">Field {i}</span>
                    {System.Web.HttpUtility.HtmlEncode(fields[i])}
                </div>");
            }
            return sb.ToString();
        }
    }
}