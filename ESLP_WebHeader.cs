using System;
using System.Collections.Generic;

namespace DataMiningCourts
{
    public class ESLP_WebHeader
    {
        public string NazevDokumentu { get; set; }
        public string CisloStiznosti { get; set; }
        public string NazevStezovatele { get; set; }
        public string TypRozhodnuti { get; set; }
        public string IdExternal { get; set; }
        public string DatumRozhodnuti { get; set; }
        public DateTime DatumRozhodnutiDate { get; set; }
        public string Vyznamnost { get; set; }
        public List<string> Hesla { get; set; } = new List<string>();
        public string Popis { get; set; }
        public string Citace { get; set; }
    }
}
