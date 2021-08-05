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
    public class NS_WebDokumentJUD
    {
        protected string documentName;
        public string DocumentName
        {
            get { return this.documentName; }
        }

        protected NS_WebHeader WHeader;

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
            //XmlDeclaration de = dok.CreateXmlDeclaration("1.0", "utf-8", null);
            //dok.AppendChild(de);
            //XmlNode bod = dok.CreateElement("body");
            //bod.InnerXml = Obsah.OuterXml;
            //dok.AppendChild(bod);
            xDoc.LoadXml("<html><head><meta http-equiv=\"Content-Type\" content=\"text/html;charset=UTF-8\"/></head>" + pXnContent.OuterXml + "</html>");

            // oprava spatne nactenych uvozovek
            //Utility.CorrectionOfQuotationMark(xDoc);

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

			pExportErrors = String.Empty;
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

        public static string NajdiRozsudekRozhodnuti(string pText)
        {
            string s = pText.ToLower();

            string textUS = "usnesení";
            string textRS = "rozsudek";
            string textUSmez = "u s n e s e n í";
            string textRSmez = "r o z s u d e k";

            int pozUS = s.IndexOf(textUS);
            int pozRS = s.IndexOf(textRS);

            int pozUSmez = s.IndexOf(textUSmez);
            int pozRSmez = s.IndexOf(textRSmez);

            if (pozUS < 0 && pozUSmez >= 0)
                pozUS = pozUSmez;
            if (pozRS < 0 && pozRSmez >= 0)
                pozRS = pozRSmez;



            if (pozUS >= 0 && pozUSmez >= 0)
                pozUS = Math.Min(pozUS, pozUSmez);
            if (pozRS >= 0 && pozRSmez >= 0)
                pozRS = Math.Min(pozRS, pozRSmez);


            if (pozUS < 0 && pozRS < 0)
                return null;
            if (pozUS >= 0 && pozRS < 0)
                return textUS;
            if (pozUS < 0 && pozRS >= 0)
                return textRS;

            return pozUS < pozRS ? textUS : textRS;

        }

        public void ZalozDokument(NS_WebHeader hlavi, SqlConnection pConn)
        {
            WHeader = hlavi;
            /* Kombinace "J", Spisové značky a roku z data rozhodnutí */
            if (!Utility.CreateDocumentName("J", WHeader.SpisovaZnacka, WHeader.HDate.Year.ToString(), out this.documentName) ||
                !Utility.CreateDocumentName("J", WHeader.Citation, WHeader.HDate.Year.ToString(), out string judikaturaSectionDokumentName))
            {
                throw new NS_Exception(String.Format("{0}: Document name nebylo vygenerováno!", WHeader.SpisovaZnacka));
            }

            /* Výstupní hodnota DokumentName*/
            hlavi.DocumentName = documentName;
            SetPathsFiles();

            if (Directory.Exists(PathFolder))
            {
                throw new NS_Exception(String.Format("Složka pro dokumentName [{0}] již existuje", PathFolder));
            }

            Directory.CreateDirectory(PathFolder);
            XmlDocument CistyVyber = new XmlDocument();
#if LOCAL_TEMPLATES
            CistyVyber.Load("Templates-J-Downloading\\Template_J_NS.xml");
#else
            string sPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            CistyVyber.Load(Path.Combine(sPath, @"Templates-J-Downloading\Template_J_NS.xml"));
#endif
            string xml = CistyVyber.DocumentElement.InnerXml;

            // povinne - jsou predvyplnene v hlavicce na 100%
            xml = xml.Replace("AUTOR", WHeader.Author);
            xml = xml.Replace("CITACE", WHeader.Citation);
            xml = xml.Replace("SPISOVAZNACKA", WHeader.SpisovaZnacka);
            xml = xml.Replace("DRUH", WHeader.Druh);
            xml = xml.Replace("DATUM", WHeader.HDate.ToString("yyyy-MM-dd"));
            xml = xml.Replace("IDEXTERNAL", WHeader.IdExternal);
            xml = xml.Replace("DATVYDANI", WHeader.PublishingDate.ToString("yyyy-MM-dd"));
            xml = xml.Replace("ECLI", WHeader.ECLI);

            // nepovinne
            xml = xml.Replace("<info1>KATEGORIE</info1>", !String.IsNullOrWhiteSpace(WHeader.Kategorie) ? "<info1>" + WHeader.Kategorie + "</info1>" : "");

            // nepovinne
            string syno = "SYNONYMA";
            string cozaSYN = "";
            if (WHeader.Registers2.Count != 0)
            {
                cozaSYN = "<rejstrik2>";
                foreach (string sRegister in WHeader.Registers2)
                    cozaSYN += "<item>" + sRegister.Replace("&", "&amp;") + "</item>";
                cozaSYN += "</rejstrik2>";
            }
            xml = xml.Replace(syno, cozaSYN);

            CistyVyber.DocumentElement.InnerXml = xml;

            /* Dokument name je vygenerovany nazev*/
            XmlNode rootNode = CistyVyber.SelectSingleNode("//judikatura[@DokumentName]");
            if (rootNode != null)
            {
                rootNode.Attributes["DokumentName"].Value = hlavi.DocumentName;
            }
            XmlNode judikaturaSection = CistyVyber.DocumentElement.SelectSingleNode("//judikatura-section[@id-block]");
            if (judikaturaSection != null)
            {
                judikaturaSection.Attributes["id-block"].Value = judikaturaSectionDokumentName;
            }

            //List<string> hrefyZP = new List<string>();
            //string zp = "ZAKLADNIPREDPISY";
            //string cozazp = "";
            if (Properties.Settings.Default.PROCESS_ZAKLADNI_PREDPIS && (WHeader.VztazenePredpisy != null) && (WHeader.VztazenePredpisy.Count() != 0))
            {
                XmlNode xn = CistyVyber.SelectSingleNode("/*/child::*[1]/zakladnipredpis");
				 if (WHeader.VztazenePredpisy != null)
					foreach (DocumentRelation drel in WHeader.VztazenePredpisy)
					{
						string law = "";

						if (!string.IsNullOrEmpty(drel.LinkText))
						{
							law += "<link href=\"" + drel.href + "\">" + drel.LinkText + "</link>";
						}
						if (!string.IsNullOrEmpty(drel.Note))
						{
							law += drel.Note;
						}
						law = law.Replace("&", "&amp;");

						if (law.Contains("99/1963") || law.Contains("141/1961"))
						{
							continue;
						}

						xn.InnerXml += "<item>" + law + "</item>";

					}
            }

			

			/* Smazání případných prázdných uzlů hlavičky */
			UtilityXml.RemoveEmptyElementsFromHeader(ref CistyVyber);
            CistyVyber.Save(PathXml);
        }

    }
}
