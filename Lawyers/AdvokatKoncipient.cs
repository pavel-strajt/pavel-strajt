using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UtilityBeck.Generic;
using System.Xml;

namespace DataMiningSoudy.Advokati
{
    public abstract class AdvokatKoncipient
    {
        public static List<string> seznamVsechZamereni = new List<string>();
        public static List<string> seznamVsechJazyku = new List<string>();

        public static string HeaderCSVZamereniJazykySloupce()
        {
            StringBuilder sbHeader = new StringBuilder();
            sbHeader.Append("a-Advokát/Koncipient; a-Titul, Jméno a přijmení; a-Evidenční číslo ČAK; a-Stav; a-Kontaktní email; a-Webové stránky;");
            sbHeader.Append("f-Firma/Sdružení; f-Název; f-IČ; f-Ulice; f-Město; f-PSČ; f-Kontaktní emaily; f-Webové stránky; f-Kontaktní telefony; f-Kontaktní mobilní telefony; f-Kontaktní faxy;");
            sbHeader.Append("a-způsob výkonu advokacie;");
            foreach (string jednoZamereni in seznamVsechZamereni)
            {
                sbHeader.AppendFormat("{0}; ", jednoZamereni);
            }

            foreach (string jedenJazyk in seznamVsechJazyku)
            {
                sbHeader.AppendFormat("{0}; ", jedenJazyk);
            }

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

        protected string evidencniCislo;

        protected string stav;

        public string Stav
        {
            get { return this.stav; }
        }

        protected Set<string> languages;

        protected string www, email;

        protected SdruzeniFirma sdruzeniFirma;

        public SdruzeniFirma PrislusneSdruzeniFirma
        {
            set
            {
                this.sdruzeniFirma = value;
            }
        }

        public AdvokatKoncipient(string id, XmlNode surovaData)
        {
            this.id = id;
            this.languages = new Set<string>();

            this.email = this.evidencniCislo = this.jmeno = this.stav = this.www = String.Empty;

            NactiPolozkyZeSurovehoXml(surovaData);
        }

        protected abstract void NactiPolozkyZeSurovehoXml(XmlNode surovaData);

        public string VypisDleHeaderCSVZamereniJazykySloupce()
        {
            // zpracování
            // jméno, evidenční číslo, stav
            StringBuilder sbRow = new StringBuilder();
            sbRow.AppendFormat("{0};", this.GetType().Name);
            sbRow.AppendFormat("{0};", this.jmeno);
            sbRow.AppendFormat("{0};", this.evidencniCislo);
            sbRow.AppendFormat("{0};", this.stav);
            
            // email
            sbRow.AppendFormat("{0};", this.email.Replace(';', ODDELOVAC_DAT_JEDNE_BUNKY));
            // www
            sbRow.AppendFormat("{0};", this.www.Replace(';', ODDELOVAC_DAT_JEDNE_BUNKY));

            // sdružení/firma
            // název
            sbRow.AppendFormat("{0};", this.sdruzeniFirma.GetType().Name);
            sbRow.AppendFormat("{0};", sdruzeniFirma.Nazev);

            // IČ - u Firmy
            if (this.sdruzeniFirma is Firma)
            {
                sbRow.AppendFormat("{0};", (this.sdruzeniFirma as Firma).IC);
            }
            else
            {
                sbRow.Append(";");
            }

            // ulice, mesto, psc
            sbRow.AppendFormat("{0};{1};{2};", this.sdruzeniFirma.Ulice, this.sdruzeniFirma.Mesto, this.sdruzeniFirma.PSC);

            // mail, www, telefon, mobil, fax
            if (this.sdruzeniFirma.emaily.Count > 0)
            {
                sbRow.AppendFormat("{0}", this.sdruzeniFirma.emaily[0].Replace(';', ODDELOVAC_DAT_JEDNE_BUNKY));
                for (int maily = 1; maily < this.sdruzeniFirma.emaily.Count; ++maily)
                {
                    sbRow.AppendFormat(" {0} {1}", ODDELOVAC_DAT_JEDNE_BUNKY, this.sdruzeniFirma.emaily[maily].Replace(';', ODDELOVAC_DAT_JEDNE_BUNKY));
                }
            }
            sbRow.AppendFormat(";");

            // www
            sbRow.AppendFormat("{0};", sdruzeniFirma.WWW);

            if (this.sdruzeniFirma.telefony.Count > 0)
            {
                sbRow.AppendFormat("{0}", this.sdruzeniFirma.telefony[0].Replace(';', ODDELOVAC_DAT_JEDNE_BUNKY));
                for (int telefony = 1; telefony < this.sdruzeniFirma.telefony.Count; ++telefony)
                {
                    sbRow.AppendFormat(" {0} {1}", ODDELOVAC_DAT_JEDNE_BUNKY, this.sdruzeniFirma.telefony[telefony].Replace(';', ODDELOVAC_DAT_JEDNE_BUNKY));
                }
            }
            sbRow.AppendFormat(";");

            if (this.sdruzeniFirma.mobily.Count > 0)
            {
                sbRow.AppendFormat("{0}", this.sdruzeniFirma.mobily[0].Replace(';', ODDELOVAC_DAT_JEDNE_BUNKY));
                for (int mobily = 1; mobily < this.sdruzeniFirma.mobily.Count; ++mobily)
                {
                    sbRow.AppendFormat(" {0} {1}", ODDELOVAC_DAT_JEDNE_BUNKY, this.sdruzeniFirma.mobily[mobily].Replace(';', ODDELOVAC_DAT_JEDNE_BUNKY));
                }
            }
            sbRow.AppendFormat(";");

            if (this.sdruzeniFirma.faxy.Count > 0)
            {
                sbRow.AppendFormat("{0}", this.sdruzeniFirma.faxy[0].Replace(';', ODDELOVAC_DAT_JEDNE_BUNKY));
                for (int faxy = 1; faxy < this.sdruzeniFirma.faxy.Count; ++faxy)
                {
                    sbRow.AppendFormat(" {0} {1}", ODDELOVAC_DAT_JEDNE_BUNKY, this.sdruzeniFirma.faxy[faxy].Replace(';', ODDELOVAC_DAT_JEDNE_BUNKY));
                }
            }
            sbRow.AppendFormat(";");


            // a- způsob výkonu advokacie
            if (this is Lawyer)
            {
                sbRow.AppendFormat("{0};", (this as Lawyer).ZpusobVykonuAdvokacie);
            }
            else
            {
                sbRow.Append(";");
            }

            // zaměření
            int posledniZpracovaneZamereni = 1;
            // zaměření má jen advokát
            if (this is Lawyer)
            {
                Lawyer adv = this as Lawyer;
                foreach (string zamereniAdvokata in adv.zamereni)
                {
                    /* 1. Vyplním středníky mezi číslem aktuálně zpracovávaného zaměření a posledním zpracovaným
                     * 2. Zapíšu ano;
                     * 3. nastavím poslední zpracované zaměření + 1
                     */
                    // zaměření se číslují od jedničky
                    int aktualneZpracovavaneZamereni = AdvokatKoncipient.seznamVsechZamereni.IndexOf(zamereniAdvokata) + 1;
                    // 1
                    for (; posledniZpracovaneZamereni < aktualneZpracovavaneZamereni; ++posledniZpracovaneZamereni)
                    {
                        sbRow.Append(';');
                    }
                    // 2
                    sbRow.Append("ano;");
                    // 3
                    posledniZpracovaneZamereni = aktualneZpracovavaneZamereni + 1;
                }
            }

            for (; posledniZpracovaneZamereni <= AdvokatKoncipient.seznamVsechZamereni.Count; ++posledniZpracovaneZamereni)
            {
                sbRow.Append(';');
            }

            // jazyky
            int posledniZpracovanyJazyk = 1;
            foreach (string jazykAdvokata in this.languages)
            {
                /* 1. Vyplním středníky mezi číslem aktuálně zpracovávaného zaměření a posledním zpracovaným
                    * 2. Zapíšu ano;
                    * 3. nastavím poslední zpracované zaměření + 1
                    */
                // zaměření se číslují od jedničky
                int aktualneZpracovavanyJazyk = AdvokatKoncipient.seznamVsechJazyku.IndexOf(jazykAdvokata) + 1;
                // 1
                for (; posledniZpracovanyJazyk < aktualneZpracovavanyJazyk; ++posledniZpracovanyJazyk)
                {
                    sbRow.Append(';');
                }
                // 2
                sbRow.Append("ano;");
                // 3
                posledniZpracovanyJazyk = aktualneZpracovavanyJazyk + 1;
            }

            for (; posledniZpracovanyJazyk <= AdvokatKoncipient.seznamVsechJazyku.Count; ++posledniZpracovanyJazyk)
            {
                sbRow.Append(';');
            }

            return sbRow.ToString();
        }
    }
}
