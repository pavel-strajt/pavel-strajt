using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using UtilityBeck.Generic;

namespace DataMiningCourts.Advokati
{
    public class Company : AssociationCompany
    {
        public static Dictionary<string, Company> seznamFirem = new Dictionary<string, Company>();

        string ic;
        public string IC
        {
            set { this.ic = value; }
            get { return this.ic; }
        }

        string zpusobVykonuAdvokacie;

        public string ZpusobVykonuAdvokacie
        {
            set { this.zpusobVykonuAdvokacie = value; }
        }

        public Company(XmlNode surovaFirma)
            : base(surovaFirma) { }

        protected override void NactiPolozkyZeSurovehoXml(XmlNode surovaFirma)
        {
            this.zpusobVykonuAdvokacie = this.ic = String.Empty;

            var nodes = surovaFirma.SelectNodes(".//tbody//tr");

            var idNode = surovaFirma.ChildNodes[0].SelectSingleNode("//tbody//tr[2]//td//a");
            if (nodes.Count == 1 || idNode == null)
				return;
            XmlAttribute aId = idNode.Attributes["href"];
            this.id = HrefToId(aId);

            // for example 140 00 Praha 4
            System.Text.RegularExpressions.Regex rgMestoPsc = new System.Text.RegularExpressions.Regex(@"^(\d{3}\s?\d{2})\s*(\S+.*)");
            string s;
            System.Text.RegularExpressions.MatchCollection mc;

            int i = 1;
            for (; i < nodes.Count; ++i)
            {
                XmlNode uzelKeZpracovani = nodes[i];

                switch (uzelKeZpracovani.FirstChild.InnerText.Trim())
                {
                    case "Název":
                        this.nazev = uzelKeZpracovani.LastChild.InnerText.Trim();
                        break;

                    case "IČ":
                        this.ic = uzelKeZpracovani.LastChild.InnerText.Trim();
                        break;

                    case "Způsob výkonu advokacie":
                        this.zpusobVykonuAdvokacie = uzelKeZpracovani.LastChild.InnerText.Trim();
                        break;

                    case "Adresa": // od 2013 je místo buněk město a psc jedna neoznačená buňka
                        this.ulice = uzelKeZpracovani.LastChild.InnerText.Trim();
                        break;

                    case "":
                        if (!String.IsNullOrWhiteSpace(uzelKeZpracovani.InnerText))
                        {
                            s = uzelKeZpracovani.LastChild.InnerText.Trim();
                            if (rgMestoPsc.IsMatch(s))
                            {
                                mc = rgMestoPsc.Matches(s);
                                this.psc = mc[0].Groups[1].Value;
                                this.mesto = mc[0].Groups[2].Value;
                            }
                        }
                        break;

                    case "email":
                    case "další emaily":
                        var aNodes = uzelKeZpracovani.LastChild?.SelectNodes("a");
                        foreach (XmlNode a in aNodes)
                        {
                            this.emaily.Add(a.InnerXml.Replace("<img src=\"/Content/at.png\" />", "@").Trim());
                        }
                        break;

                    case "www":
                        this.www = uzelKeZpracovani.LastChild.InnerText.Trim();
                        break;

                    case "Telefon":
                    case "další telefony":
                        var splits = uzelKeZpracovani.LastChild.InnerText.Trim().Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList();
                        CorrectPhoneNumber(splits, telefony);
                        break;

                    case "Mobil":
                        splits = uzelKeZpracovani.LastChild.InnerText.Trim().Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList();
                        CorrectPhoneNumber(splits, mobily);
                        break;

                    case "Fax":
                        splits = uzelKeZpracovani.LastChild.InnerText.Trim().Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList();
                        CorrectPhoneNumber(splits, faxy);
                        break;
                }
            }
        }

        private void CorrectPhoneNumber(List<string> splits, Set<string> dest)
        {

            if (splits.Count > 0)
            {
                foreach (var split in splits)
                {
                    if (split.StartsWith("+420"))
                    {
                        dest.Add(split);
                    }
                    else if (split.StartsWith("420"))
                    {
                        dest.Add("+" + split);
                    }
                    else
                    {
                        dest.Add("+420" + split);
                    }
                }
            }
        }
    }
}
