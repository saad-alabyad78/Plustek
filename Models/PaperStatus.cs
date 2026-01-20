using System;

namespace Plustek.Models {
    public class PaperStatus {
        public int EventCode { get; set; }
        public string Message { get; set; } = "";

        public bool IsPaperPresent => EventCode == 0 || EventCode == 11 || EventCode == 12 || EventCode == 13;
        public bool IsNoPaper => EventCode == 1;
    }
}