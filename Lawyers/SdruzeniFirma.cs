using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UtilityBeck.Generic;
using System.Xml;

namespace DataMiningSoudy.Advokati
{
    public abstract class SdruzeniFirma
    {
        protected string id;

        public string ID
        {
            get { return this.id; }
        }

        protected string nazev;

        public string Nazev
        {
            get { return this.nazev; }
        }

        protected string ulice, mesto, psc;

        public string Ulice
        {
            get { return this.ulice; }
        }

        public string Mesto
        {
            get { return this.mesto; }
        }

        public string PSC
        {
            get { return this.psc; }
        }

        protected string www;

        public string WWW
        {
            get { return this.www; }
        }

        internal Set<string> emaily;
        internal Set<string> telefony;
        internal Set<string> faxy;
        internal Set<string> mobily;

        public SdruzeniFirma(XmlNode surovaData)
        {
            this.emaily = new Set<string>();
            this.telefony = new Set<string>();
            this.mobily = new Set<string>();
            this.faxy = new Set<string>();
            this.nazev = this.mesto = this.psc = this.ulice = this.www = String.Empty;

            NactiPolozkyZeSurovehoXml(surovaData);
        }

        protected string HrefToId(XmlAttribute href)
        {
            int idxEq = href.Value.IndexOf('=');
            string id = href.Value.Substring(idxEq + 1);
            return id;
        }

        protected abstract void NactiPolozkyZeSurovehoXml(XmlNode surovaData);
    }
}
