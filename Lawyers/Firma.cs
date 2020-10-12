using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

namespace DataMiningSoudy.Advokati
{
    public class Firma : SdruzeniFirma
    {
        public static Dictionary<string, Firma> seznamFirem = new Dictionary<string, Firma>();

        //Název 	Klimeš Zdeněk, JUDr., advokát
        //IČ 	11564849
        //Způsob výkonu advokacie 	ve sdružení

        //Ulice 	Horní 2a/1433
        //Město 	Havířov-Město
        //PSČ 	73601

        //Kontakty
        //www
        //Email 	zdenek.klimes [zavináč] advokati-havirov.cz
        //Telefon
        //Mobil
        //Fax 
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

        public Firma(XmlNode surovaFirma)
            :base(surovaFirma) {}

        protected override void NactiPolozkyZeSurovehoXml(XmlNode surovaFirma)
        {
            this.zpusobVykonuAdvokacie = this.ic = String.Empty;

            //<table style="width: 100%;">
            //  <tr>
            //    <td class="header" colspan="2">Firma</td>
            //  </tr>
            //  <tr class="highlight">
            //    <td>Název</td>
            //    <td>
            //      <a href="/Units/_Search/Details/detailFirma.aspx?id=178d7ca203154642afb11dd00f7b9e4d">Dvorná
            //      Helena, advokátka</a>
            //    </td>
            //  </tr>
            //  <tr>
            //    <td>IČ</td>
            //    <td>11213876</td>
            //  </tr>
            //  <tr>
            //    <td>Způsob výkonu advokacie</td>
            //    <td>samostatný advokát</td>
            //  </tr>
            //  <tr>
            //    <td>
            //      <br />
            //    </td>
            //  </tr>
            //  <tr>
            //    <td>Ulice</td>
            //    <td>Hurbanova 11, P.O. Box 81</td>
            //  </tr>
            //  <tr>
            //    <td>Město</td>
            //    <td>Praha - Krč</td>
            //  </tr>
            //  <tr>
            //    <td>PSČ</td>
            //    <td>14200</td>
            //  </tr>
            //  <tr>
            //    <td>
            //      <br />
            //    </td>
            //  </tr>
            //  <div>
            //    <tr class="highlight">
            //      <td>Kontakty</td>
            //    </tr>
            //    <div>
            //      <tr>
            //        <td colspan="2">www</td>
            //      </tr>
            //    </div>
            //    <tr>
            //      <td>Email</td>
            //      <td>
            //        <a href="javascript:window.location='mailto:'+'Helena.Dvorna' + '@' + 'seznam.cz'">
            //        Helena.Dvorna 
            //        <img src="/Images/at.png" alt=" [zavináč] " width="12px" height="12px" />seznam.cz</a>
            //      </td>
            //    </tr>
            //    <div>
            //      <tr>
            //        <td>Telefon</td>
            //        <td>222 519 782</td>
            //      </tr>
            //      <tr>
            //        <td>Mobil</td>
            //        <td>602 232 788</td>
            //      </tr>
            //      <tr>
            //        <td>Fax</td>
            //        <td>222 519 782</td>
            //      </tr>
            //    </div>
            //  </div>
            //</table>
            if (surovaFirma.ChildNodes.Count == 1)
            {
                // prázdná firma...
                return;
            }

            XmlAttribute aId = surovaFirma.ChildNodes[1].LastChild.FirstChild.Attributes["href"];
            this.id = HrefToId(aId);

			// for example 140 00 Praha 4
			System.Text.RegularExpressions.Regex rgMestoPsc = new System.Text.RegularExpressions.Regex(@"^(\d{3}\s?\d{2})\s*(\S+.*)");
			string s;
			System.Text.RegularExpressions.MatchCollection mc;

            int i = 1;
            for (; i < surovaFirma.ChildNodes.Count; ++i)
            {
                XmlNode uzelKeZpracovani = surovaFirma.ChildNodes[i];

                if (uzelKeZpracovani.Name == "div")
                {
                    break;
                }

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

            XmlNode uzelKontakty = surovaFirma.ChildNodes[i];
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
