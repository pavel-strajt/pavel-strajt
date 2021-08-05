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
    public class ESLP_WebDokumentJUD
    {
        protected string documentName;
        public string DocumentName
        {
            get { return this.documentName; }
        }

        protected ESLP_WebHeader WHeader;

        public string PathOutputFolder;

        private string pathFolder;
        public string PathFolder
        {
            get { return this.pathFolder; }
        }

        private string pathXhtml;
        public string PathXhtml
        {
            get { return this.pathXhtml; }
        }

        private string pathWordXml;
        public string PathWordXml
        {
            get { return this.pathWordXml; }
        }

        private string pathWordXmlXml;
        public string PathWordXmlXml
        {
            get { return this.pathWordXmlXml; }
        }

        private string pathXml;
        public string PathXml
        {
            get { return this.pathXml; }
        }

        public void PrepareSaveWordForExport(XmlNode pXnContent)
        {
            XmlDocument xDoc = new XmlDocument();
            xDoc.LoadXml("<html><head><meta http-equiv=\"Content-Type\" content=\"text/html;charset=UTF-8\"/></head>" + pXnContent.InnerXml + "</html>");

            xDoc.Save(PathXhtml);

            FrmCourts.OpenFileInWordAndSaveInWXml(PathXhtml, PathWordXml);
            File.Copy(PathWordXml, PathWordXmlXml);
        }

        private void SetPathsFiles()
        {
            pathFolder = Path.Combine(PathOutputFolder, documentName);
            pathXhtml = Path.Combine(pathFolder, "HTML_" + documentName + ".htm");
            pathWordXml = Path.Combine(pathFolder, "W_" + documentName + "-0.xml");
            pathWordXmlXml = Path.Combine(pathFolder, "W_" + documentName + "-0.xml.xml");
            pathXml = Path.Combine(pathFolder, documentName + ".xml");
        }

        public bool ExportFromMsWord(ref string pExportErrors)
        {
            XmlDocument d = new XmlDocument();
            d.Load(PathWordXmlXml);
            Regex reg3 = new Regex(@"\s+");
            d.DocumentElement.InnerXml = reg3.Replace(d.DocumentElement.InnerXml, " ");

			// we save the word xml as one line xml file
			XmlWriterSettings xwsSettings = new XmlWriterSettings();
			xwsSettings.Indent = false;
			xwsSettings.Encoding = System.Text.Encoding.UTF8;

			XmlWriter xw = XmlWriter.Create(PathWordXmlXml, xwsSettings);
			d.WriteContentTo(xw);
			xw.Flush();
			xw.Close();

			pExportErrors = "";
			string[] parametry = new string[] { "CZ", PathFolder + "\\" + this.documentName + ".xml", this.documentName, "0", "17" };
			try
			{
				ExportWordXml.ExportWithoutProgress export = new ExportWordXml.ExportWithoutProgress(parametry);
				export.RunExport();
				pExportErrors = export.errors;
			}
            catch (Exception ex)
            {
                if (ex is NullReferenceException)
                {
                    return true;
                }
                else
                {
                    pExportErrors = "Export zcela selhal ! " + ex.Message + ex.InnerException;
                    return false;
                }
            }

            return true;
        }

        public void ZalozDokument(ESLP_WebHeader hlavi)
        {
            WHeader = hlavi;
            /* Kombinace "J", Spisové značky a roku z data rozhodnutí */
            if (!Utility.CreateDocumentName("J", WHeader.CisloStiznosti, WHeader.DatumRozhodnutiDate.Year.ToString(), out this.documentName) ||
                !Utility.CreateDocumentName("J", WHeader.Citace, WHeader.DatumRozhodnutiDate.Year.ToString(), out string judikaturaSectionDokumentName))
            {
                throw new ApplicationException(String.Format("{0}: Document name nebylo vygenerováno!", WHeader.CisloStiznosti));
            }

            /* Výstupní hodnota DokumentName*/
            hlavi.NazevDokumentu = documentName;
            SetPathsFiles();

            if (Directory.Exists(PathFolder))
            {
                throw new ApplicationException(String.Format("Složka pro dokumentName [{0}] již existuje", PathFolder));
            }

            Directory.CreateDirectory(PathFolder);
            XmlDocument CistyVyber = new XmlDocument();
#if LOCAL_TEMPLATES
            CistyVyber.Load("Templates-J-Downloading\\Template_J_ESLP.xml");
#else
            string sPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            CistyVyber.Load(Path.Combine(sPath, @"Templates-J-Downloading\Template_J_ESLP.xml"));
#endif
            string xml = CistyVyber.DocumentElement.InnerXml;

            // povinne - jsou predvyplnene v hlavicce na 100%
            xml = xml.Replace("CITACEVALUE", WHeader.Citace);
            xml = xml.Replace("SPISOVAZNACKAVALUE", WHeader.CisloStiznosti);
            xml = xml.Replace("DRUHVALUE", WHeader.TypRozhodnuti);
            xml = xml.Replace("DATUMVALUE", WHeader.DatumRozhodnuti);
            xml = xml.Replace("IDEXTERNALVALUE", WHeader.IdExternal);
            xml = xml.Replace("VYZNAMNOSTVALUE", WHeader.Vyznamnost);
            xml = xml.Replace("NAZEVSTEZOVATELEVALUE", WHeader.NazevStezovatele);
            xml = xml.Replace("POPISVALUE", WHeader.Popis);

            string syno = "HESLAVALUE";
            string cozaSYN = "";
            if (WHeader.Hesla.Count != 0)
            {
                cozaSYN = "<rejstrik2>";
                foreach (string sRegister in WHeader.Hesla)
                    cozaSYN += "<item>" + sRegister.Replace("&", "&amp;") + "</item>";
                cozaSYN += "</rejstrik2>";
            }
            xml = xml.Replace(syno, cozaSYN);

            CistyVyber.DocumentElement.InnerXml = xml;

            /* Dokument name je vygenerovany nazev*/
            XmlNode rootNode = CistyVyber.SelectSingleNode("//judikatura[@DokumentName]");
            if (rootNode != null)
            {
                rootNode.Attributes["DokumentName"].Value = hlavi.NazevDokumentu;
            }
            XmlNode judikaturaSection = CistyVyber.DocumentElement.SelectSingleNode("//judikatura-section[@id-block]");
            if (judikaturaSection != null)
            {
                judikaturaSection.Attributes["id-block"].Value = judikaturaSectionDokumentName;
            }

            /* Smazání případných prázdných uzlů hlavičky */
            UtilityXml.RemoveEmptyElementsFromHeader(ref CistyVyber);
            CistyVyber.Save(PathXml);
        }

    }
}
