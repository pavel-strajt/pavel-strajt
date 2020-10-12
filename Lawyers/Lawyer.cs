using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using UtilityBeck.Generic;

namespace DataMiningCourts.Advokati
{
    public class Lawyer : LawyerTrainee
    {
        public static Dictionary<string, Lawyer> seznamAdvokatu = new Dictionary<string, Lawyer>();

        string zpusobVykonuAdvokacie;

        public string ZpusobVykonuAdvokacie
        {
            get { return this.zpusobVykonuAdvokacie; }
        }

        string ustanoveniExOffo;

        public string UstanoveniExOffo
        {
            get { return this.ustanoveniExOffo; }
        }

        internal Set<string> zamereni;

        public Lawyer(string id, XmlNode surovyAdvokat)
            : base(id, surovyAdvokat)
        {
        }

        private enum StavZpracovaniSurovehoXml { ZakladniInformace, Zamereni, Jazyk, Kontakty };

        protected override void NactiPolozkyZeSurovehoXml(XmlNode surovyAdvokat)
        {
            zpusobVykonuAdvokacie = this.ustanoveniExOffo = String.Empty;

            // nejprve vytvořím odkaz na zaměření...
            zamereni = new Set<string>();

            var stav = StavZpracovaniSurovehoXml.ZakladniInformace;
            var nodes = surovyAdvokat.ChildNodes[0].SelectNodes("tr");

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

                    case "Způsob výkonu advokacie":
                        this.zpusobVykonuAdvokacie = tr.LastChild.InnerText.Trim(); ;
                        break;

                    case "Ustanovování ex-offo":
                        this.ustanoveniExOffo = tr.LastChild.InnerText.Trim();
                        break;

                    case "Zaměření":
                        stav = StavZpracovaniSurovehoXml.Zamereni;
                        break;

                    case "Jazyk":
                        stav = StavZpracovaniSurovehoXml.Jazyk;
                        break;

                    default:
                        if (stav == StavZpracovaniSurovehoXml.Zamereni)
                        {
                            string konkretniZamereni = UtilityBeck.Utility.ReplaceMultipleWhitespaceWithSingleOne(tr.InnerText.Trim());
                            if (LawyerTrainee.seznamVsechZamereni.Contains(konkretniZamereni))
                            {
                                this.zamereni.Add(konkretniZamereni);
                            }
                        }
                        else if (stav == StavZpracovaniSurovehoXml.Jazyk)
                        {
                            string konkretniJazyk = tr.InnerText.Trim();
                            if (LawyerTrainee.seznamVsechJazyku.Contains(konkretniJazyk))
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
            XmlElement nAdvokat = pDoc.CreateElement("advokat");
            nAdvokat.SetAttribute("id", id);

            StringBuilder sbInnerXml = new StringBuilder();
            sbInnerXml.AppendLine(String.Format("<jmeno>{0}</jmeno>", jmeno));
			sbInnerXml.AppendLine(String.Format("<ico>{0}</ico>", ico));
			sbInnerXml.AppendLine(String.Format("<evidencni-cislo>{0}</evidencni-cislo>", idCak));
            sbInnerXml.AppendLine(String.Format("<stav>{0}</stav>", stav));
            if (!String.IsNullOrEmpty(zpusobVykonuAdvokacie))
            {
                sbInnerXml.AppendLine(String.Format("<zpusob-vykonu-advokacie>{0}</zpusob-vykonu-advokacie>", zpusobVykonuAdvokacie));
            }
            if (!String.IsNullOrEmpty(ustanoveniExOffo))
            {
                sbInnerXml.AppendLine(String.Format("<ex-offo>{0}</ex-offo>", ustanoveniExOffo));
            }
            if (zamereni.Count > 0)
            {
                sbInnerXml.AppendLine("<seznam-zamereni>");
                foreach (string jednoZamereni in zamereni)
                {
                    sbInnerXml.AppendLine(String.Format("\t<zamereni>{0}</zamereni>", jednoZamereni));
                }
                sbInnerXml.AppendLine("</seznam-zamereni>");
            }

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
                if (!String.IsNullOrEmpty(telefon))
                {
                    sbInnerXml.AppendLine(String.Format("\t<telefon>{0}</telefon>", email));
                }
                sbInnerXml.AppendLine("</kontakty>");
            }

            nAdvokat.InnerXml = sbInnerXml.ToString().Replace("&", "&amp;");

            return nAdvokat;
        }
    }
}
