using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

namespace DataMiningCourts.Advokati
{
    public class Association : AssociationCompany
    {
        public static Dictionary<string, Association> seznamSdruzeni = new Dictionary<string, Association>();

        public Association(XmlNode suroveSdruzeni) : base(suroveSdruzeni) { }

        protected override void NactiPolozkyZeSurovehoXml(XmlNode suroveSdruzeni)
        {
            // dtto firma, ale pár věcí nemá...
            if (suroveSdruzeni == null || suroveSdruzeni.ChildNodes.Count == 1)
            {
                // prázdná firma...
                return;
            }

            XmlAttribute aId = suroveSdruzeni.ChildNodes[1].LastChild.FirstChild.Attributes["href"];
            this.id = HrefToId(aId);

            // for example 140 00 Praha 4
            System.Text.RegularExpressions.Regex rgMestoPsc = new System.Text.RegularExpressions.Regex(@"^(\d{3}\s?\d{2})\s*(\S+.*)");
            string s;
            System.Text.RegularExpressions.MatchCollection mc;

            int i = 1;
            for (; i < suroveSdruzeni.ChildNodes.Count; ++i)
            {
                XmlNode uzelKeZpracovani = suroveSdruzeni.ChildNodes[i];

                if (uzelKeZpracovani.Name == "div")
                {
                    break;
                }

                switch (uzelKeZpracovani.FirstChild.InnerText.Trim())
                {
                    case "Název":
                        this.nazev = uzelKeZpracovani.LastChild.InnerText.Trim().Replace(Environment.NewLine, String.Empty);
                        break;

                    case "Ulice":
                    case "Adresa": // od 2013 je místo buněk město a psc jedna neoznačená buňka
                        this.ulice = uzelKeZpracovani.LastChild.InnerText.Trim();
                        break;

                    case "Město":
                        this.mesto = uzelKeZpracovani.LastChild.InnerText.Trim();
                        break;

                    case "PSČ":
                        this.psc = uzelKeZpracovani.LastChild.InnerText.Trim();
                        break;

                    case "":
                        // od 2013 je místo buněk město a psc jedna neoznačená buňka
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
                }
            }

            XmlNode uzelKontakty = suroveSdruzeni.ChildNodes[i];
            if (uzelKontakty != null)
            {
                if (uzelKontakty.ChildNodes[1].ChildNodes.Count == 2)
                {
                    this.www = uzelKontakty.ChildNodes[1].LastChild.InnerText.Trim();
                }

                // můžu mít víc emailů... a pak je průůser!
                int j = 2;
                for (; j < uzelKontakty.ChildNodes.Count; ++j)
                {
                    if (uzelKontakty.ChildNodes[j].InnerText.Contains("Telefon"))
                    {
                        break;
                    }

                    //      <td>
                    //        <a href="javascript:window.location='mailto:'+'ak.jancova' + '@' + 'seznam.cz'">ak.jancova 
                    //        <img src="/Images/at.png" alt=" [zavináč] " width="12px" height="12px" />seznam.cz</a>
                    //      </td>
                    // poslední
                    if (uzelKontakty.ChildNodes[j].ChildNodes.Count == 2)
                    {
                        XmlNode email = uzelKontakty.ChildNodes[j].LastChild;
                        this.emaily.Add(String.Format("{0}@{1}", email.FirstChild.FirstChild.InnerText.Trim(), email.FirstChild.LastChild.InnerText.Trim()));
                    }
                }

                // skupina tel/mob/fax
                foreach (XmlNode kontaktniTelefony in uzelKontakty.ChildNodes[j].ChildNodes)
                {
                    if (kontaktniTelefony.ChildNodes.Count == 1)
                    {
                        continue;
                    }

                    switch (kontaktniTelefony.FirstChild.InnerText.Trim())
                    {
                        case "Telefon":
                            this.telefony.Add(kontaktniTelefony.LastChild.InnerText.Trim());
                            break;

                        case "Mobil":
                            this.mobily.Add(kontaktniTelefony.LastChild.InnerText.Trim());
                            break;

                        case "Fax":
                            this.faxy.Add(kontaktniTelefony.LastChild.InnerText.Trim());
                            break;
                    }
                }
            }
        }
    }
}
