using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BeckLinking;

namespace DataMiningCourts
{
    public class NS_WebHeader
    {
        public string Author;
        public int NumberCitation;
        public int YearCitation;
        public string Citation; 
        public string SpisovaZnacka;
        public string Druh;
		public string IdExternal;
        public string Kategorie;
        public IEnumerable<DocumentRelation> VztazenePredpisy;
        public List<string> Registers2 = new List<string>();
        public DateTime HDate;        
        public string URL;
        public string ECLI;

		public string DocumentName;
        public DateTime PublishingDate;
    }
}
