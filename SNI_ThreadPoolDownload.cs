using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Xml;
using DataMiningCourts.Properties;
using System.Data.SqlClient;
using UtilityBeck;
using BeckLinking;

namespace DataMiningCourts
{
    class SNI_ThreadPoolDownload : ALL_ThreadPoolDownload
    {
        private string sFilePathToWriteFileTo;

        /// <summary>
        /// Spisová značka dokumentu, která se odstranuje z textu
        /// </summary>
        private string spisovaZnackaPorovnani;

        private bool falseStart
        {
            get { return File.Exists(sFilePathToWriteFileTo); }
        }

        /// <summary>
        /// Dafuelt = false
        /// </summary>
        private bool alwaysOverride;

        public bool AlwaysOverride
        {
            set { this.alwaysOverride = value; }
        }

        public SNI_ThreadPoolDownload(FrmCourts frm, ManualResetEvent doneEvent, string sDirectoryPathToWriteFileTo, string sFilePathToWriteFileTo)
            : base(frm, doneEvent, sDirectoryPathToWriteFileTo)
        {
            this.doneEvent = doneEvent;
            this.sFilePathToWriteFileTo = sFilePathToWriteFileTo;
        }

        public void DownloadDocument(object o)
        {
            string fullHtmlPath = String.Format(@"{0}\{1}.html", directoryPathToWriteFileTo, sFilePathToWriteFileTo);
            if (alwaysOverride || !falseStart)
            {
                // filename => za posledním /, bez posledního "?openDocument"

                // the URL to download the file from
                string sUrlToReadFileFrom = o.ToString();

                // first, we need to get the exact size (in bytes) of the file we are downloading
                Uri url = new Uri(sUrlToReadFileFrom);
                System.Net.HttpWebRequest request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                System.Net.HttpWebResponse response = (System.Net.HttpWebResponse)request.GetResponse();
                response.Close();

                // gets the size of the file in bytes
                Int64 iSize = response.ContentLength;

                // keeps track of the total bytes downloaded so we can update the progress bar
                Int64 iRunningByteTotal = 0;
                // use the webclient object to download the file
                using (System.Net.WebClient client = new System.Net.WebClient())
                {
                    // open the file at the remote URL for reading
                    using (System.IO.Stream streamRemote = client.OpenRead(new Uri(sUrlToReadFileFrom)))
                    {
                        // using the FileStream object, we can write the downloaded bytes to the file system
                        using (Stream streamLocal = new FileStream(fullHtmlPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            // loop the stream and get the file into the byte buffer
                            int iByteSize = 0;
                            byte[] byteBuffer = new byte[iSize];
                            while ((iByteSize = streamRemote.Read(byteBuffer, 0, byteBuffer.Length)) > 0)
                            {
                                // write the bytes to the file system at the file path specified
                                streamLocal.Write(byteBuffer, 0, iByteSize);
                                iRunningByteTotal += iByteSize;
                            }
                            // clean up the file stream
                            streamLocal.Close();
                        }
                        // close the connection to the remote server
                        streamRemote.Close();
                    }
                }
            }

            using (SqlConnection conn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["LexData"].ConnectionString))
            {
#if !DUMMY_DB
                conn.Open();
#endif
                // musím vytvořit hlavičku
                string sError = String.Empty;
                string sDocumentName;
                if (!CreateHeader(fullHtmlPath, conn, ref sError, out sDocumentName))
                {
                    this.parentWindowForm.WriteIntoLogCritical(String.Format("{0}::Chyby při vytváření hlavičky: {1}", fullHtmlPath, sError));
                    // It is DONE... :)
                    doneEvent.Set();
                    return;
                }
                HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
                doc.Load(fullHtmlPath, Encoding.UTF8);

                var contentNode = doc.DocumentNode.SelectSingleNode("//div[@class='det_params']");
                var node = contentNode.SelectSingleNode("//div[@id='sidebar']");
                if (node != null) node.Remove();
                node = contentNode.SelectSingleNode("//table[@id='box-table-a']");
                if (node != null) node.Remove();

                var xDoc = new HtmlAgilityPack.HtmlDocument();
                xDoc.LoadHtml("<html><head><meta http-equiv=\"Content-Type\" content=\"text/html;charset=UTF-8\"/></head><body>" + contentNode.InnerHtml + "</body></html>");
                xDoc.Save(fullHtmlPath);

                string sPathFolder = String.Format(@"{0}\{1}", directoryPathToWriteFileTo, sDocumentName);
                string sPathWordXml = String.Format(@"{0}\W_{1}-0.xml", sPathFolder, sDocumentName);
                FrmCourts.OpenFileInWordAndSaveInWXml(fullHtmlPath, sPathWordXml);
                File.Copy(sPathWordXml, String.Format(@"{0}\W_{1}-0.xml.xml", sPathFolder, sDocumentName), true);

				string sExportErrors = String.Empty;
				string[] parametry = new string[] { "CZ", sPathFolder + "\\" + sDocumentName + ".xml", sDocumentName, "0", "17" };
				try
				{
					ExportWordXml.ExportWithoutProgress export = new ExportWordXml.ExportWithoutProgress(parametry);
					export.RunExport();
					sExportErrors = export.errors;
				}
                catch (Exception ex)
                {
                    this.parentWindowForm.WriteIntoLogCritical(String.Format("{0}: Export zcela selhal! {1}", sDocumentName, ex.Message));
                    // It is DONE... :)
                    doneEvent.Set();
                    return;
                }
                if (!String.IsNullOrEmpty(sExportErrors))
                {
                    this.parentWindowForm.WriteIntoLogExport(sFilePathToWriteFileTo + "\r\n" + sExportErrors + "\r\n*****************************");
                }

                string sPathResultXml = String.Format(@"{0}\{1}.xml", sPathFolder, sDocumentName);
                XmlDocument dOut = new XmlDocument();
                dOut.Load(sPathResultXml);

#if !DUMMY_DB // Pokud export nic neexportuje, tak nemá smysl to "nic" upravovat
                // odstraní se začátek dokumentu
                XmlNode xnHtml = dOut.SelectSingleNode("//html-text");
                XmlNode xn = xnHtml.FirstChild;
                while (xn != null)
                {
                    if (xn.Name.Equals("table"))
                        break;
                    xn = xn.NextSibling;
                }
                //if ((xn == null) || !xn.LastChild.FirstChild.InnerText.Trim().Equals("Kategorie rozhodnutí:")
                //        || !xn.PreviousSibling.InnerText.Trim().Equals("<< zpět na zadání dotazu"))
                //{
                //    this.parentWindowForm.WriteIntoLogCritical(String.Format("{0}: Chyba v postprocessingu: Nenalezen začátek dokumentu !", sDocumentName));
                //}
                XmlNode xn2;
                while (xn != null)
                {
                    xn2 = xn.PreviousSibling;
                    xn.ParentNode.RemoveChild(xn);
                    xn = xn2;
                }
                // odstraní se konec dokumentu
                int iCount = 0;
                xn = xnHtml.LastChild;
                while (xn != null)
                {
                    if (xn.InnerText.Trim().Equals("Konec formuláře"))
                        ++iCount;
                    else if ((xn.FirstChild != null) && xn.FirstChild.Name.Equals("img"))
                        ++iCount;
                    else
                        break;
                    if (iCount == 3)
                        break;
                    xn = xn.PreviousSibling;
                }
                if ((iCount != 0) || (xn == null))
                {
                    this.parentWindowForm.WriteIntoLogCritical("Chyba v postprocessingu: Nenalezen konec dokumentu !");
                }
                while (xn != null)
                {
                    xn2 = xn.NextSibling;
                    xn.ParentNode.RemoveChild(xn);
                    xn = xn2;
                }

                // linking
                Linking oLinking = new Linking(conn, "cs", null);
                oLinking.Run(0, sDocumentName, dOut, 17);
                dOut = oLinking.LinkedDocument;

                // Oříznou se odstavce a zruší zbytečné mezery
                xnHtml = dOut.SelectSingleNode("//html-text");
                // ošetří se kdyby export selhal a nic nevyexportoval
                if (String.IsNullOrWhiteSpace(xnHtml.InnerText))
                {
                    this.parentWindowForm.WriteIntoLogCritical(String.Format("Export dokumentu [{0}] NIC nevyexportoval!", sDocumentName));
                    return;
                }

                UtilityXml.RemoveRedundantEmptyRowsInXmlDocument(ref xnHtml);
                UtilityXml.RemoveMultiSpaces(ref xnHtml);

                // na začátku se odstraní možná spisová značka
                xn = xnHtml.FirstChild;
                string sValue;
                do
                {
                    sValue = xn.InnerText.ToLower();
                    xn = xn.NextSibling;
                }
                while (String.IsNullOrWhiteSpace(sValue));

                Utility.RemoveWhiteSpaces(ref sValue);
                sValue = sValue.Replace(".", "");
                if (sValue.Contains(spisovaZnackaPorovnani) || (sValue.Contains(spisovaZnackaPorovnani) && (sValue.StartsWith("čj:") || sValue.StartsWith("číslojednací:"))) && (sValue.Length < 40))
                {
                    xnHtml.RemoveChild(xn.PreviousSibling);
                }

                // odstraní se potenciální obrázek a text Česká republika
                // odstraní se vše až do textu rozsudek jménem republiky
                // problémem je, že musím najít začátek tohoto souvislého bloku, protože se nenachází na začátku dokumentu!
                bool neOdstranenoJmenemRepubliky = true;
                // textu česká republika (a dohledávám obrázek zpětně)
                bool probihaOdstranovani = false;
                bool jeToObrazek;
                xn = xnHtml.FirstChild;
                while (xn != null && neOdstranenoJmenemRepubliky)
                {
                    string innerText = xn.InnerText.ToLower().Trim();
                    Utility.RemoveWhiteSpaces(ref innerText);
                    if (!probihaOdstranovani && innerText == "českárepublika")
                    {
                        probihaOdstranovani = true;
                    }

                    jeToObrazek = xn.SelectSingleNode("./img") != null;

                    if (innerText.EndsWith("jménemrepubliky"))
                    {
                        neOdstranenoJmenemRepubliky = false;
                    }

                    /* posunu se dále a pokud probíhá odstraňování, tak odstraním uzel
                     * Pokud bych byl na poslední uzlu textu, tak ten uzel nevymažu, nicméně to by bylo stejnak špatně, takže mi nevadí,
                     * že se bude chovat špatně, když je dokument jistě špatně...
                     */
                    xn = xn.NextSibling;
                    if (xn != null && (probihaOdstranovani || jeToObrazek)) xnHtml.RemoveChild(xn.PreviousSibling);
                }

                UtilityXml.RemoveRedundantEmptyRowsInXmlDocument(ref xnHtml);
                UtilityXml.AddCite(dOut, sDocumentName, conn);
#endif
                /* Odstraním prázdné části z hlavičky! */
                UtilityBeck.UtilityXml.RemoveEmptyElementsFromHeader(ref dOut);
                // uložení
                dOut.Save(sPathResultXml);
#if !DUMMY_DB
                conn.Close();
#endif
                // výsledek
                string sMoveTo = String.Format(@"{0}\{1}", this.parentWindowForm.XML_DIRECTORY, sDocumentName);
                /* If there is a directory with a same name -> delete it with all content within... */
                if (Directory.Exists(sMoveTo))
                {
                    Directory.Delete(sMoveTo, true);
                }
                Directory.Move(sPathFolder, sMoveTo);
            }


            doneEvent.Set();
        }

        private bool CreateHeader(string pDownloadedDocumentPath, SqlConnection pConn, ref string pError, out string pDocumentName)
        {
            XmlDocument dIn = new XmlDocument(), dOut = new XmlDocument();
            string sReferenceNumber = null, zeDne = null, sValue, druh = null;
			DateTime? dtZeDne = null;
			// string text1 = "", text2;
			XmlNode xn, xnTr, xnTd1, xnTd2, xnTable, xn2;
            Object oResult = null;
            int iPosition;
            var zeDneNonUni = String.Empty;
            var cmd = pConn.CreateCommand();

            pDocumentName = null;
            HtmlAgilityPack.HtmlDocument dHtm = new HtmlAgilityPack.HtmlDocument();
            dHtm.OptionOutputAsXml = true;
            dHtm.Load(pDownloadedDocumentPath, Encoding.UTF8);
            dIn.LoadXml(dHtm.DocumentNode.OuterHtml.Replace("&amp;nbsp;", " "));
#if LOCAL_TEMPLATES
            dOut.Load("Templates-J-Downloading\\Template_J_SNI.xml");
#else
            string sPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            dOut.Load(Path.Combine(sPath, @"Templates-J-Downloading\Template_J_SNI.xml"));
#endif

            xnTable = dIn.SelectSingleNode("//table[@id='box-table-a']");
            xnTr = xnTable.FirstChild;
            while (xnTr != null)
            {
                xnTd1 = xnTr.FirstChild;
                xnTd2 = xnTd1.NextSibling;
                switch (xnTd1.InnerText.Trim())
                {
                    case "Spisová značka:":			// spisová značka
                        sReferenceNumber = xnTd2.InnerText.Trim();
                        this.spisovaZnackaPorovnani = sReferenceNumber;
                        // chybí-li mezera, doplní se za první číslo mezera
                        char[] znaky = sReferenceNumber.ToCharArray();
                        if (Char.IsDigit(znaky[0]))
                        {
                            bool bFill = false;
                            for (iPosition = 0; iPosition < sReferenceNumber.Length; iPosition++)
                            {
                                if (znaky[iPosition] == ' ')
                                    break;
                                else if (!Char.IsDigit(znaky[iPosition]))
                                {
                                    bFill = true;
                                    break;
                                }
                            }
                            if (bFill)
                                sReferenceNumber = sReferenceNumber.Insert(iPosition, " ");
                        }
                        xn = dOut.DocumentElement.FirstChild.SelectSingleNode("./citace");
						xn.InnerText = sReferenceNumber;
                        break;
                    case "Právní věta:":
                        if (!String.IsNullOrWhiteSpace(xnTd2.InnerText))
                        {
                            xn = dOut.SelectSingleNode("/*/judikatura-section/header-j/veta");
                            sValue = xnTd2.InnerText.Trim();
                            sValue = sValue.Replace('\r', ' ');
                            sValue = sValue.Replace('\n', ' ');
                            while (sValue.IndexOf("  ") > -1)
                                sValue = sValue.Replace("  ", " ");
                            XmlElement el = dOut.CreateElement("p");
                            xn.AppendChild(el);
                            el = dOut.CreateElement("span");
                            xn.FirstChild.AppendChild(el);
                            xn.FirstChild.FirstChild.InnerText = sValue;
                        }
                        break;
                    case "ECLI:":
                        if (!String.IsNullOrWhiteSpace(xnTd2.InnerText))
                        {
                            xn = dOut.SelectSingleNode("//ecli");
                            xn.InnerText = xnTd2.InnerText.Trim();
                        }
                        break;
                    case "Soud:":			// author
                        xn = dOut.SelectSingleNode("//autor/item");
                        xn.InnerText = xnTd2.InnerText.Trim().Replace("Krajský soud v Ústí nad Labem - pobočka v Liberci", "Krajský soud v Ústí nad Labem - pobočka Liberec");
                        break;
                    case "Datum rozhodnutí:":		// Ze dne
                        zeDneNonUni = xnTd2.InnerText.Trim();
                        zeDne = Utility.ConvertDateIntoUniversalFormat(zeDneNonUni, out dtZeDne);
                        xn = dOut.SelectSingleNode("//datschvaleni");
                        xn.InnerText = zeDne;
                        break;
                    case "Forma rozhodnutí:":		// Druh
                        if (!String.IsNullOrWhiteSpace(xnTd2.InnerText))
                        {
                            xn = dOut.SelectSingleNode("//druh");
                            druh = xnTd2.InnerText.Trim();
                            xn.InnerText = druh;
                        }
                        break;
                    case "Heslo:":
                        if (!String.IsNullOrWhiteSpace(xnTd2.InnerText))
                        {
                            if (xnTd2.ChildNodes.Count != 1)
                            {
                                pError = "Neočekávaná struktura v rejstříku !";
                                return false;
                            }
                            xn2 = xnTd2.FirstChild.FirstChild;
                            while (xn2 != null)
                            {
                                if (xn2.NodeType == XmlNodeType.Text)
                                {
                                    if (!String.IsNullOrWhiteSpace(xn2.InnerText))
                                    {
                                        sValue = xn2.InnerText.Trim();
#if !DUMMY_DB
                                        cmd.CommandText = "SELECT 1 FROM TRegister WHERE TRegisterName='" + sValue + "'";
                                        oResult = cmd.ExecuteScalar();
#endif
                                        if (oResult != null)
                                        {
                                            xn = dOut.SelectSingleNode("//rejstrik");
                                        }
                                        else
                                        {
                                            xn = dOut.SelectSingleNode("//rejstrik2");
                                        }

                                        if (sValue.Contains("&"))
                                        {
                                            sValue = sValue.Replace("&", "&amp;");
                                        }
                                        else
                                        {

                                            xn.InnerXml += "<item>" + sValue.Replace("&", "") + "</item>";
                                        }
                                    }
                                }
                                else if (!xn2.Name.Equals("br"))
                                {
                                    pError = "Neznámý element v rejstříku !";
                                    return false;
                                }
                                xn2 = xn2.NextSibling;
                            }
                        }
                        break;
                    case "Dotčené předpisy:":
                        if (Properties.Settings.Default.PROCESS_ZAKLADNI_PREDPIS && !String.IsNullOrWhiteSpace(xnTd2.InnerText))
                        {
                            xn = dOut.SelectSingleNode("//zakladnipredpis");
                            if (xn == null)
                            {
                                xn = dOut.SelectSingleNode("//hlavicka-judikatura");
                                xn.AppendChild(dOut.CreateElement("zakladnipredpis"));
                                xn = dOut.SelectSingleNode("//zakladnipredpis");
                            }
                            LinkingHelper.AddBaseLaws(dOut, xnTd2.InnerText, xn);
                        }
                        break;
                    case "Kategorie rozhodnutí:":
                        if (!String.IsNullOrWhiteSpace(xnTd2.InnerText))
                        {
                            xn = dOut.SelectSingleNode("//info1");
                            xn.InnerText = xnTd2.InnerText.Trim();
                        }
                        break;

                    case "Publikováno ve sbírce pod číslem:":			// přeskakuje se
                        break;

                    default:
                        if (!String.IsNullOrWhiteSpace(xnTd2.InnerText))
                        {
                            pError = "Neočekávané pole v htm: " + xnTr.FirstChild.InnerText.Trim();
                            return false;
                        }
                        break;
                }
                xnTr = xnTr.NextSibling;
            }

            // doplnění čísla sešitu
            if (citation.ReferenceNumberIsAlreadyinDb(dtZeDne.Value, sReferenceNumber))
            {
                /*+ForeignId*/
                pError = String.Format("Znacka [{0}] s daným datem rozhodnutí [{1}] je v jiz databazi!", sReferenceNumber, zeDne);
                return false;
            }

            xn = dOut.SelectSingleNode("//id-external");
            xn.InnerText = this.sFilePathToWriteFileTo;
            // fill in citation
            xn = dOut.DocumentElement.SelectSingleNode("./judikatura-section/header-j/citace");
            int iNumberInCitation = citation.GetNextCitation(dtZeDne.Value.Year);

            string sCitation = String.Format("Výběr VKS {0}/{1}", iNumberInCitation, dtZeDne.Value.Year);
            xn.InnerText = sCitation;
			// odstranění prázdných elementů z hlavičky
			UtilityXml.RemoveEmptyElementsFromHeader(ref dOut);

			// vytvoření složky výsledného xml
			string judikaturaSectionDokumentName;
            if (!Utility.CreateDocumentName("J", sReferenceNumber, dtZeDne.Value.Year.ToString(), out pDocumentName) ||
                !Utility.CreateDocumentName("J", sCitation, dtZeDne.Value.Year.ToString(), out judikaturaSectionDokumentName))
            {
                pError = "Nevytvořen název dokumentu !";
                citation.RevertCitationForAYear(dtZeDne.Value.Year);
                return false;
            }

            if (Directory.Exists(directoryPathToWriteFileTo + "\\" + pDocumentName))
            {
                pError = "Výstupní dokument již existuje !";
                citation.RevertCitationForAYear(dtZeDne.Value.Year);
                return false;
            }

            Directory.CreateDirectory(directoryPathToWriteFileTo + "\\" + pDocumentName);

            XmlNode judikaturaSection = dOut.SelectSingleNode("//judikatura-section");
            judikaturaSection.Attributes["id-block"].Value = judikaturaSectionDokumentName;
            dOut.DocumentElement.Attributes["DokumentName"].Value = pDocumentName;

            UtilityBeck.UtilityXml.RemoveEmptyElementsFromHeader(ref dOut);

            dOut.Save(directoryPathToWriteFileTo + "\\" + pDocumentName + "\\" + pDocumentName + ".xml");
            citation.CommitCitationForAYear(dtZeDne.Value.Year);
            return true;
        }
    }
}
