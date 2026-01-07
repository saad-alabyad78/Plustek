using System;
using System.Collections.Generic;

namespace Plustek.Models {
    public class SyrianIdData {
        public string? RawBarcodeData { get; set; }
        public List<string> Fields { get; set; } = new List<string>();
    }
}