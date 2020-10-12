using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.IO;
using System.Configuration;
using UtilityBeck;
using System.Data.SqlClient;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using DataMiningCourts.Properties;
using BeckLinking;

namespace DataMiningCourts
{
    public class JV_WebDokumentJUD
    {
        protected string documentName;
        public string DocumentName
        {
            get { return this.documentName; }
        }

        protected JV_WebHeader WHeader;

        public string PathOutputFolder;

        private string pathFolder;
        public string PathFolder
        {
            get { return this.pathFolder; }
        }

        private string pathDoc;
        public string PathDoc
        {
            get { return this.pathDoc; }
        }

        private string pathTmpXml;
        public string PathTmpXml
        {
            get { return this.pathTmpXml; }
        }

        private string pathResultXml;
        public string PathResultXml
        {
            get { return this.pathResultXml; }
        }

        private void SetPathsFiles()
        {
            pathFolder = Path.Combine(PathOutputFolder, documentName);
            pathDoc = Path.Combine(pathFolder, documentName + ".doc");
            pathTmpXml = Path.Combine(pathFolder, "W_"+documentName + ".xml.xml");
            pathResultXml = Path.Combine(pathFolder, documentName + ".xml");
        }

        public bool ExportFromMsWord(ref string pExportErrors)
        {
            XmlDocument d = new XmlDocument();
            d.Load(PathTmpXml);
            Regex reg3 = new Regex(@"\s+");
            d.DocumentElement.InnerXml = reg3.Replace(d.DocumentElement.InnerXml, " ");
            d.Save(PathTmpXml);

            pExportErrors = "";

            string[] parametry = new string[] { "CZ", PathFolder, "0", "17" };

            try
            {
                ExportWordXml.FrmExportCz export = new ExportWordXml.FrmExportCz(parametry);
                export.ExportInBackgroud(out pExportErrors);
            }
            catch
            {
                pExportErrors = "Export zcela selhal !";
                return false;
            }

            return true;
        }

        public void ZalozDokument(JV_WebHeader hlavicka)
        {
            WHeader = hlavicka;
            var judikaturaSectionDokumentName = string.Empty;
            /* Kombinace "J", Spisové značky a roku z data rozhodnutí */
            if (!Utility.CreateDocumentName("JV", WHeader.CisloUsneseni, WHeader.DatumSchvaleni.Year.ToString(), out documentName) ||
                !Utility.CreateDocumentName("JV", WHeader.Citace, WHeader.DatumSchvaleni.Year.ToString(), out judikaturaSectionDokumentName))
            {
                throw new ApplicationException(String.Format("{0}: Document name nebylo vygenerováno!", WHeader.CisloUsneseni));
            }

            /* Výstupní hodnota DokumentName*/
            WHeader.NazevDokumentu = documentName;
            SetPathsFiles();

            if (Directory.Exists(PathFolder))
            {
                throw new ApplicationException(String.Format("Složka pro dokumentName [{0}] již existuje", PathFolder));
            }

            Directory.CreateDirectory(PathFolder);
            XmlDocument CistyVyber = new XmlDocument();
#if LOCAL_TEMPLATES
            CistyVyber.Load("Templates-J-Downloading\\Template_J_JV.xml");
#else
            string sPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            CistyVyber.Load(Path.Combine(sPath, @"Templates-J-Downloading\Template_J_JV.xml"));
#endif

            var citaceNode = CistyVyber.SelectSingleNode("//hlavicka-vestnik/citace");
            if (citaceNode != null)
            {
                citaceNode.InnerText = WHeader.Citace;
            }
            var datumSchvaleniNode = CistyVyber.SelectSingleNode("//hlavicka-vestnik/datschvaleni");
            if (datumSchvaleniNode != null)
            {
                datumSchvaleniNode.InnerText = WHeader.DatumSchvaleni.ToString(Utility.DATE_FORMAT);
            }

            /* Smazání případných prázdných uzlů hlavičky */
            UtilityXml.DeleteEmptyNodesFromHeaders(CistyVyber);
            CistyVyber.Save(PathResultXml);
        }
    }
}
