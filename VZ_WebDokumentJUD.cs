using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using UtilityBeck;

namespace DataMiningCourts
{
    public class VZ_WebDokumentJUD
    {
        protected string documentName;
        public string DocumentName
        {
            get { return this.documentName; }
        }

        protected VZ_WebHeader WHeader;

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
            pathTmpXml = Path.Combine(pathFolder, "W_" + documentName + ".xml.xml");
            pathResultXml = Path.Combine(pathFolder, documentName + ".xml");
        }

        public bool ExportFromMsWord(ref string pExportErrors)
        {
            XmlDocument d = new XmlDocument();
            d.Load(PathTmpXml);
            Regex reg3 = new Regex(@"\s+");
            d.DocumentElement.InnerXml = reg3.Replace(d.DocumentElement.InnerXml, " ");

			// we save the word xml as one line xml file
			XmlWriterSettings xwsSettings = new XmlWriterSettings();
			xwsSettings.Indent = false;
			xwsSettings.Encoding = System.Text.Encoding.UTF8;

			XmlWriter xw = XmlWriter.Create(PathTmpXml, xwsSettings);
			d.WriteContentTo(xw);
			xw.Flush();
			xw.Close();
			//d.Save(PathTmpXml);

			pExportErrors = String.Empty;
			string[] parametry = new string[] { "CZ", PathFolder + "\\" + this.documentName + ".xml", this.documentName, "-1", "17" };
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
					pExportErrors = this.documentName + "\tExport zcela selhal ! " + ex.Message + ex.InnerException;
					return false;
				}
			}

			if (!String.IsNullOrEmpty(pExportErrors))
				pExportErrors = this.documentName + ":\tEXPORT:\t" + pExportErrors;

			return true;
        }

        public void ZalozDokument(VZ_WebHeader hlavicka)
        {
            WHeader = hlavicka;
            var judikaturaSectionDokumentName = string.Empty;
            /* Kombinace "J", Spisové značky a roku z data rozhodnutí */
            if (!Utility.CreateDocumentName("Vz", WHeader.CisloUsneseni, WHeader.DatumSchvaleni.Year.ToString(), out documentName) ||
                !Utility.CreateDocumentName("Vz", WHeader.Citace, WHeader.DatumSchvaleni.Year.ToString(), out judikaturaSectionDokumentName))
            {
                throw new ApplicationException(String.Format("{0}: Document name nebylo vygenerováno!", WHeader.CisloUsneseni));
            }

            documentName = documentName + "_UsnV";
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
            CistyVyber.Load("Templates-J-Downloading\\Template_J_Vz.xml");
#else
            string sPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            CistyVyber.Load(Path.Combine(sPath, @"Templates-J-Downloading\Template_J_Vz.xml"));
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
				if (WHeader.DatumSchvaleni>new DateTime(2020,3,9))
				{
					// 25.3.2020 usnesení vlády korona virus
					XmlElement el = CistyVyber.CreateElement("module");
					el.InnerXml = "<item>X - mFree</item>";
					UtilityXml.InsertElementInAlphabeticalOrder(CistyVyber.DocumentElement.FirstChild, el);
				}
            }
            CistyVyber.Save(PathResultXml);
        }
    }
}
