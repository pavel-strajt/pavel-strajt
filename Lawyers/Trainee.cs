using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

namespace DataMiningCourts.Advokati
{
    public class Trainee : LawyerTrainee
    {
        public static Dictionary<string, Trainee> seznamKoncipientu = new Dictionary<string, Trainee>();

        //Jméno 	Mgr. Patricie Cabalková
        //Evidenční číslo 	36131
        //Stav 	aktivní

        //Jazyk

        //Kontakty
        //www
        //Email 	cabalkova.p [zavináč] seznam.cz

        public Trainee(string id, XmlNode surovyKoncipient) : base(id, surovyKoncipient) { }

        private enum StavZpracovaniSurovehoXml { ZakladniInformace, Jazyk, Kontakty };

        protected override void NactiPolozkyZeSurovehoXml(XmlNode surovyKoncipient)
        {

            var stav = StavZpracovaniSurovehoXml.ZakladniInformace;
            var nodes = surovyKoncipient.ChildNodes[0].SelectNodes("tr");

            foreach (XmlNode tr in nodes)
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

					case "IČ":
						this.ico = tr.LastChild.InnerText.Trim();
						break;

					case "Evidenční číslo":
                        this.idCak = tr.LastChild.InnerText.Trim();
                        break;

                    case "email":
                        var aNodes = tr.LastChild?.SelectNodes("a");
                        foreach (XmlNode a in aNodes)
                        {
                            email += a.InnerXml.Replace("<img src=\"/Content/at.png\" />", "@").Trim() + ";";
                        }
                        email = email.EndsWith(";") ? email.Remove(email.Length - 1) : email;
                        break;

                    case "www":
                        this.www = tr.LastChild.InnerText.Trim();
                        break;

                    case "Telefon":
                        if (!string.IsNullOrWhiteSpace(telefon)) break;
                        CorrectPhone(tr.LastChild.InnerText);
                        break;

                    case "Stav":
                        this.stav = tr.LastChild.InnerText.Trim();
                        break;

                    case "Jazyk":
                        stav = StavZpracovaniSurovehoXml.Jazyk;
                        break;

                    default:
                        if (stav == StavZpracovaniSurovehoXml.Jazyk)
                        {
                            string konkretniJazyk = tr.InnerText.Trim();
                            if (seznamVsechJazyku.Contains(konkretniJazyk))
                            {
                                languages.Add(konkretniJazyk);
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
			sbInnerXml.AppendLine(String.Format("<ico>{0}</ico>", ico));
			sbInnerXml.AppendLine(String.Format("<evidencni-cislo>{0}</evidencni-cislo>", idCak));
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
