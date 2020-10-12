using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

namespace DataMiningSoudy.Advokati
{
    public class Koncipient : AdvokatKoncipient
    {
        public static Dictionary<string, Koncipient> seznamKoncipientu = new Dictionary<string, Koncipient>();

        //Jméno 	Mgr. Patricie Cabalková
        //Evidenční číslo 	36131
        //Stav 	aktivní

        //Jazyk

        //Kontakty
        //www
        //Email 	cabalkova.p [zavináč] seznam.cz

        public Koncipient(string id, XmlNode surovyKoncipient) : base(id, surovyKoncipient) { }

		private enum StavZpracovaniSurovehoXml { ZakladniInformace, Jazyk, Kontakty };

        protected override void NactiPolozkyZeSurovehoXml(XmlNode surovyKoncipient)
        {
            //<table style="width: 100%;">
            //  <tr>
            //    <td class="header" colspan="2">Koncipient</td>
            //  </tr>
            //  <tr class="highlight">
            //    <td>Jméno</td>
            //    <td>Mgr. Jana Abd El Kaderová</td>
            //  </tr>
            //  <tr>
            //    <td>Evidenční číslo</td>
            //    <td>13607</td>
            //  </tr>
            //  <tr>
            //    <td>Stav</td>
            //    <td>aktivní</td>
            //  </tr>
            //  <tr>
            //    <td>
            //      <br />
            //    </td>
            //  </tr>
            //  <tr class="highlight">
            //    <td>Jazyk</td>
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
            //    <div id="ctl00_mainContent_rpt_ctl00_RepeaterKoncipient_ctl00_partKontaktyKoncipient_Panel1">
            //      <tr>
            //        <td colspan="2">Email</td>
            //      </tr>
            //    </div>
            //  </div>
            //</table>

			StavZpracovaniSurovehoXml stav = StavZpracovaniSurovehoXml.ZakladniInformace;
			foreach (XmlNode tr in surovyKoncipient.ChildNodes)
			{
				if (String.IsNullOrWhiteSpace(tr.InnerText))
				{
					continue;
				}

				switch (tr.FirstChild.InnerText.Trim())
				{
					case "Jméno":
						this.jmeno = tr.LastChild.InnerText.Trim();
						break;

					case "Evidenční číslo":
						this.evidencniCislo = tr.LastChild.InnerText.Trim();
						break;

					case "IČ":
						/* Nepoužívá se*/
						break;

					case "ID datové schránky":
						/* Nepoužívá se*/
						break;

					case "Stav":
						this.stav = tr.LastChild.InnerText.Trim();
						break;

					case "Jazyk":
						stav = StavZpracovaniSurovehoXml.Jazyk;
						break;

					default:
						if (tr.Name == "div")
						{
							/* Kontakty*/
							stav = StavZpracovaniSurovehoXml.Kontakty;
							// už jsem na kontaktech :)
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
							//        <a href="javascript:window.location='mailto:'+'ak.jancova' + '@' + 'seznam.cz'">ak.jancova 
							//        <img src="/Images/at.png" alt=" [zavináč] " width="12px" height="12px" />seznam.cz</a>
							//      </td>
							//    </tr>
							//  </div>
							//</table>
							// předposlední
							if (tr.LastChild.PreviousSibling.ChildNodes.Count == 2)
							{
								this.www = tr.LastChild.PreviousSibling.LastChild.InnerText.Trim();
							}
							//      <td>
							//        <a href="javascript:window.location='mailto:'+'ak.jancova' + '@' + 'seznam.cz'">ak.jancova 
							//        <img src="/Images/at.png" alt=" [zavináč] " width="12px" height="12px" />seznam.cz</a>
							//      </td>
							// poslední
							if (tr.LastChild.ChildNodes.Count == 2)
							{
								XmlNode email = tr.LastChild.LastChild;
								this.email = String.Format("{0}@{1}", email.FirstChild.FirstChild.InnerText.Trim(), email.FirstChild.LastChild.InnerText.Trim());
							}
						}
						else if (stav == StavZpracovaniSurovehoXml.Jazyk)
						{
							string konkretniJazyk = tr.InnerText.Trim();
							if (AdvokatKoncipient.seznamVsechJazyku.Contains(konkretniJazyk))
							{
								this.languages.Add(konkretniJazyk);
							}
						}
						break;
				}

			}
        }

        public XmlNode GenerateXml(XmlDocument pDoc)
        {
            XmlElement nKoncipient = pDoc.CreateElement("koncipient");
            nKoncipient.SetAttribute("id", id);

            StringBuilder sbInnerXml = new StringBuilder();
            sbInnerXml.AppendLine(String.Format("<jmeno>{0}</jmeno>", jmeno));
            sbInnerXml.AppendLine(String.Format("<evidencni-cislo>{0}</evidencni-cislo>", evidencniCislo));
            sbInnerXml.AppendLine(String.Format("<stav>{0}</stav>", stav));

            if (languages.Count > 0)
            {
                sbInnerXml.AppendLine("<seznam-jazyku>");
                foreach (string jedenJazyk in languages)
                {
                    sbInnerXml.AppendLine(String.Format("\t<jazyk>{0}</jazyk>", jedenJazyk));
                }
                sbInnerXml.AppendLine("</seznam-jazyku>");
            }

            if (!String.IsNullOrEmpty(www) || !String.IsNullOrEmpty(email))
            {
                sbInnerXml.AppendLine("<kontakty>");
                if (!String.IsNullOrEmpty(www))
                {
                    sbInnerXml.AppendLine(String.Format("\t<www>{0}</www>", www));
                }
                if (!String.IsNullOrEmpty(email))
                {
                    sbInnerXml.AppendLine(String.Format("\t<email>{0}</email>", email));
                }
                sbInnerXml.AppendLine("</kontakty>");
            }

            nKoncipient.InnerXml = sbInnerXml.ToString().Replace("&", "&amp;");
            return nKoncipient;
        }
    }
}
