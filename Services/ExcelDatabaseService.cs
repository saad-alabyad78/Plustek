// File: Plustek/Services/ExcelDatabaseService.cs
using ClosedXML.Excel;
using Plustek.Configuration;
using Plustek.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Plustek.Services {
    public class ExcelDatabaseService {
        private readonly AppSettings _settings;
        private readonly string _databasePath;

        public ExcelDatabaseService(AppSettings settings) {
            _settings = settings;
            _databasePath = Path.Combine(_settings.OutputDirectory, "SyrianIDDatabase.xlsx");

            EnsureDatabaseExists();
        }

        private void EnsureDatabaseExists() {
            if (File.Exists(_databasePath)) {
                return;
            }

            Console.WriteLine($"[ExcelDB] Creating new database: {_databasePath}");

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Syrian IDs");

            // Create headers
            worksheet.Cell(1, 1).Value = "National ID";
            worksheet.Cell(1, 2).Value = "First Name";
            worksheet.Cell(1, 3).Value = "Father Name";
            worksheet.Cell(1, 4).Value = "Last Name";
            worksheet.Cell(1, 5).Value = "Mother Name";
            worksheet.Cell(1, 6).Value = "Birth Info";
            worksheet.Cell(1, 7).Value = "Scanned At";
            //worksheet.Cell(1, 8).Value = "Front Face Path";
            //worksheet.Cell(1, 9).Value = "Back Face Path";

            // Style headers
            var headerRange = worksheet.Range(1, 1, 1, 9);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // Auto-fit columns
            worksheet.Columns().AdjustToContents();

            workbook.SaveAs(_databasePath);
            Console.WriteLine($"[ExcelDB] Database created successfully");
        }

        public async Task SaveRecordAsync(SyrianIdData idData, string? frontFacePath, string? backFacePath) {
            await Task.Run(() => {
                try {
                    Console.WriteLine($"[ExcelDB] Saving record for National ID: {idData.NationalId}");

                    using var workbook = new XLWorkbook(_databasePath);
                    var worksheet = workbook.Worksheet(1);

                    // Check if record already exists
                    var existingRow = FindRecordRow(worksheet, idData.NationalId);

                    bool shouldCreate = false;

                    if (existingRow > 0) {
                        // Record exists, check if scanned at is older than 5 minutes
                        var scannedAtCell = worksheet.Cell(existingRow, 7).GetString();

                        if (DateTime.TryParse(scannedAtCell, out DateTime lastScannedAt)) {
                            var fiveMinutesAgo = DateTime.Now.AddMinutes(-5);

                            if (lastScannedAt < fiveMinutesAgo) {
                                Console.WriteLine($"[ExcelDB] Last scan was more than 5 minutes ago ({lastScannedAt}). Creating new record.");
                                shouldCreate = true;
                            } else {
                                Console.WriteLine($"[ExcelDB] Record was scanned recently ({lastScannedAt}). Skipping creation.");
                                return;
                            }
                        } else {
                            // If we can't parse the date, create a new record
                            Console.WriteLine($"[ExcelDB] Could not parse scanned date. Creating new record.");
                            shouldCreate = true;
                        }
                    } else {
                        Console.WriteLine($"[ExcelDB] Record not found. Creating new record.");
                        shouldCreate = true;
                    }

                    if (shouldCreate) {
                        // Always add as new row (never update existing)
                        int row = worksheet.LastRowUsed().RowNumber() + 1;

                        // Fill data
                        worksheet.Cell(row, 1).Value = idData.NationalId;
                        worksheet.Cell(row, 2).Value = idData.FirstName;
                        worksheet.Cell(row, 3).Value = idData.FatherName;
                        worksheet.Cell(row, 4).Value = idData.LastName;
                        worksheet.Cell(row, 5).Value = idData.MotherName;
                        worksheet.Cell(row, 6).Value = idData.BirthInfo;
                        worksheet.Cell(row, 7).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        //worksheet.Cell(row, 8).Value = frontFacePath ?? "";
                        //worksheet.Cell(row, 9).Value = backFacePath ?? "";

                        // Auto-fit columns
                        worksheet.Columns().AdjustToContents();

                        workbook.Save();
                        Console.WriteLine($"[ExcelDB] Record saved successfully at row {row}");
                    }
                }
                catch (Exception ex) {
                    Console.WriteLine($"[ExcelDB] ERROR saving record: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task<string> ExportDatabaseAsync(string exportPath) {
            return await Task.Run(() => {
                try {
                    Console.WriteLine($"[ExcelDB] Exporting database to: {exportPath}");

                    if (!File.Exists(_databasePath)) {
                        throw new FileNotFoundException("Database file not found", _databasePath);
                    }

                    File.Copy(_databasePath, exportPath, overwrite: true);

                    Console.WriteLine($"[ExcelDB] Database exported successfully");
                    return exportPath;
                }
                catch (Exception ex) {
                    Console.WriteLine($"[ExcelDB] ERROR exporting database: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task<int> GetRecordCountAsync() {
            return await Task.Run(() => {
                try {
                    if (!File.Exists(_databasePath)) {
                        return 0;
                    }

                    using var workbook = new XLWorkbook(_databasePath);
                    var worksheet = workbook.Worksheet(1);

                    // Subtract 1 for header row
                    return worksheet.LastRowUsed().RowNumber() - 1;
                }
                catch {
                    return 0;
                }
            });
        }

        public string GetDatabasePath() {
            return _databasePath;
        }

        private int FindRecordRow(IXLWorksheet worksheet, string nationalId) {
            var lastRow = worksheet.LastRowUsed().RowNumber();
            int foundRow = 0;
            DateTime latestScannedAt = DateTime.MinValue;

            for (int row = 2; row <= lastRow; row++) {
                var cellValue = worksheet.Cell(row, 1).GetString();
                if (cellValue == nationalId) {
                    // Found a matching National ID, check if it's the latest
                    var scannedAtCell = worksheet.Cell(row, 7).GetString();

                    if (DateTime.TryParse(scannedAtCell, out DateTime scannedAt)) {
                        if (scannedAt > latestScannedAt) {
                            latestScannedAt = scannedAt;
                            foundRow = row;
                        }
                    } else {
                        // If we can't parse the date, still consider this row if we haven't found any valid date yet
                        if (foundRow == 0) {
                            foundRow = row;
                        }
                    }
                }
            }

            return foundRow; // Returns 0 if not found, or the row with the latest scanned date
        }
    }
}