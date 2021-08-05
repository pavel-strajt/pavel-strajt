using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UtilityBeck.Generic;
using System.Xml;

namespace DataMiningCourts.Advokati
{
    public abstract class LawyerTrainee
    {
        public static List<string> seznamVsechZamereni = new List<string>();
        public static List<string> seznamVsechJazyku = new List<string>();

        public static string HeaderCSVZamereniJazykySloupce()
        {
            StringBuilder sbHeader = new StringBuilder();
            sbHeader.Append("Statute;LawyerName;Ico;IDCak;State;LawyerEmail;WebAddress;Phone;");
            sbHeader.Append("CompanyName;CompanyIco;Street;City;PostalCode;CompanyEmails;WebPage;PhoneNumber;CellPhoneNumber;Fax;");
            sbHeader.Append("a-způsob výkonu advokacie;Zaměření;Jazyk;");

            /* Delete last ;*/
            --sbHeader.Length;
            sbHeader.AppendLine();
            return sbHeader.ToString();
        }

        private static char ODDELOVAC_DAT_JEDNE_BUNKY = '|';

        protected string id;

        public string ID
        {
            get { return this.id; }
        }

        protected string jmeno;
		protected string ico;

		protected string idCak;

        protected string stav;

        public string Stav
        {
            get { return this.stav; }
        }

        protected Set<string> languages;

        protected string www, email, telefon;

        protected AssociationCompany sdruzeniFirma;

        public AssociationCompany PrislusneSdruzeniFirma
        {
            set
            {
                this.sdruzeniFirma = value;
            }
        }

        public LawyerTrainee(string id, XmlNode surovaData)
        {
            this.id = id;
            this.languages = new Set<string>();

            this.email = this.idCak = this.jmeno = this.ico = this.stav = this.www = telefon = String.Empty;

            NactiPolozkyZeSurovehoXml(surovaData);
        }

        protected abstract void NactiPolozkyZeSurovehoXml(XmlNode surovaData);

        public string VypisDleHeaderCSVZamereniJazykySloupce()
        {
            // zpracování
            // jméno, evidenční číslo, stav
            StringBuilder sbRow = new StringBuilder();
            sbRow.AppendFormat("{0};", this.GetType().Name);
            sbRow.AppendFormat("{0};", HtmlAgilityPack.HtmlEntity.DeEntitize(this.jmeno).Trim());
			sbRow.AppendFormat("{0};", HtmlAgilityPack.HtmlEntity.DeEntitize(this.ico).Trim());
			sbRow.AppendFormat("{0};", HtmlAgilityPack.HtmlEntity.DeEntitize(this.idCak).Trim());
            sbRow.AppendFormat("{0};", HtmlAgilityPack.HtmlEntity.DeEntitize(this.stav).Trim());

            // email
            sbRow.AppendFormat("{0};", HtmlAgilityPack.HtmlEntity.DeEntitize(this.email)?.Trim().Replace(';', ODDELOVAC_DAT_JEDNE_BUNKY));
            // www
            sbRow.AppendFormat("{0};", HtmlAgilityPack.HtmlEntity.DeEntitize(this.www)?.Trim().Replace(';', ODDELOVAC_DAT_JEDNE_BUNKY));
            // telefon
            sbRow.AppendFormat("{0};", HtmlAgilityPack.HtmlEntity.DeEntitize(this.telefon)?.Trim().Replace(';', ODDELOVAC_DAT_JEDNE_BUNKY));

            // sdružení/firma
            // název
            if (sdruzeniFirma == null) sdruzeniFirma = new Association(null);

            //sbRow.AppendFormat("{0};", this.sdruzeniFirma.GetType().Name);
            sbRow.AppendFormat("{0};", HtmlAgilityPack.HtmlEntity.DeEntitize(sdruzeniFirma.Nazev).Trim());

            // IČ - u Firmy
            if (this.sdruzeniFirma is Company)
            {
                sbRow.AppendFormat("{0};", HtmlAgilityPack.HtmlEntity.DeEntitize((this.sdruzeniFirma as Company).IC).Trim());
            }
            else
            {
                sbRow.Append(";");
            }

            // ulice, mesto, psc
            sbRow.AppendFormat("{0};{1};{2};", HtmlAgilityPack.HtmlEntity.DeEntitize(this.sdruzeniFirma.Ulice).Trim(), HtmlAgilityPack.HtmlEntity.DeEntitize(this.sdruzeniFirma.Mesto).Trim(), HtmlAgilityPack.HtmlEntity.DeEntitize(this.sdruzeniFirma.PSC).Trim());

            // mail, www, telefon, mobil, fax
            if (this.sdruzeniFirma.emaily.Count > 0)
            {
                sbRow.AppendFormat("{0}", HtmlAgilityPack.HtmlEntity.DeEntitize(this.sdruzeniFirma.emaily[0]).Trim().Replace(';', ODDELOVAC_DAT_JEDNE_BUNKY));
                for (int maily = 1; maily < this.sdruzeniFirma.emaily.Count; ++maily)
                {
                    sbRow.AppendFormat(" {0} {1}", ODDELOVAC_DAT_JEDNE_BUNKY, HtmlAgilityPack.HtmlEntity.DeEntitize(this.sdruzeniFirma.emaily[maily]).Trim().Replace(';', ODDELOVAC_DAT_JEDNE_BUNKY));
                }
            }
            sbRow.AppendFormat(";");

            // www
            sbRow.AppendFormat("{0};", HtmlAgilityPack.HtmlEntity.DeEntitize(sdruzeniFirma.WWW).Trim());

            if (this.sdruzeniFirma.telefony.Count > 0)
            {
                sbRow.AppendFormat("{0}", HtmlAgilityPack.HtmlEntity.DeEntitize(this.sdruzeniFirma.telefony[0]).Trim().Replace(';', ODDELOVAC_DAT_JEDNE_BUNKY));
                for (int telefony = 1; telefony < this.sdruzeniFirma.telefony.Count; ++telefony)
                {
                    sbRow.AppendFormat(" {0} {1}", ODDELOVAC_DAT_JEDNE_BUNKY, HtmlAgilityPack.HtmlEntity.DeEntitize(this.sdruzeniFirma.telefony[telefony]).Trim().Replace(';', ODDELOVAC_DAT_JEDNE_BUNKY));
                }
            }
            sbRow.AppendFormat(";");

            if (this.sdruzeniFirma.mobily.Count > 0)
            {
                sbRow.AppendFormat("{0}", HtmlAgilityPack.HtmlEntity.DeEntitize(this.sdruzeniFirma.mobily[0]).Trim().Replace(';', ODDELOVAC_DAT_JEDNE_BUNKY));
                for (int mobily = 1; mobily < this.sdruzeniFirma.mobily.Count; ++mobily)
                {
                    sbRow.AppendFormat(" {0} {1}", ODDELOVAC_DAT_JEDNE_BUNKY, HtmlAgilityPack.HtmlEntity.DeEntitize(this.sdruzeniFirma.mobily[mobily]).Trim().Replace(';', ODDELOVAC_DAT_JEDNE_BUNKY));
                }
            }
            sbRow.AppendFormat(";");

            if (this.sdruzeniFirma.faxy.Count > 0)
            {
                sbRow.AppendFormat("{0}", HtmlAgilityPack.HtmlEntity.DeEntitize(this.sdruzeniFirma.faxy[0]).Trim().Replace(';', ODDELOVAC_DAT_JEDNE_BUNKY));
                for (int faxy = 1; faxy < this.sdruzeniFirma.faxy.Count; ++faxy)
                {
                    sbRow.AppendFormat(" {0} {1}", ODDELOVAC_DAT_JEDNE_BUNKY, HtmlAgilityPack.HtmlEntity.DeEntitize(this.sdruzeniFirma.faxy[faxy]).Trim().Replace(';', ODDELOVAC_DAT_JEDNE_BUNKY));
                }
            }
            sbRow.AppendFormat(";");


            // a- způsob výkonu advokacie
            if (this is Lawyer)
            {
                sbRow.AppendFormat("{0};", HtmlAgilityPack.HtmlEntity.DeEntitize((this as Lawyer).ZpusobVykonuAdvokacie).Trim());
            }
            else
            {
                sbRow.Append(";");
            }

            // zaměření
            // zaměření má jen advokát
            var toAppend = string.Empty;
            if (this is Lawyer)
            {
                Lawyer adv = this as Lawyer;
                toAppend = string.Empty;
                foreach (var zamereni in adv.zamereni)
                {
                    toAppend += zamereni + ODDELOVAC_DAT_JEDNE_BUNKY;
                }
                if (toAppend.Length > 1)
                {
                    toAppend = toAppend.Remove(toAppend.Length - 1, 1) + ";";
                }
                sbRow.Append(toAppend);
            }
            // jazyky
            toAppend = string.Empty;
            foreach (var lang in languages)
            {
                toAppend += lang + ODDELOVAC_DAT_JEDNE_BUNKY;
            }
            if (toAppend.Length > 1)
            {
                toAppend = toAppend.Remove(toAppend.Length - 1, 1) + ";";
            }
            sbRow.Append(toAppend);

            return sbRow.ToString();
        }

        protected void CorrectPhone(string phones)
        {
            var result = string.Empty;

            var splits = phones.Trim().Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList();
            if (splits.Count > 0)
            {
                foreach (var split in splits)
                {
                    if (split.StartsWith("+420"))
                    {
                        telefon += split + ";";
                    }
                    else if (split.StartsWith("420"))
                    {
                        telefon += "+" + split + ";";
                    }
                    else
                    {
                        telefon += "+420" + split + ";";
                    }
                }
                telefon = telefon.EndsWith(";") ? telefon.Remove(telefon.Length - 1) : telefon;

            }
        }
    }
}
