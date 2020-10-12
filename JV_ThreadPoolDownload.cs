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
using System.Text.RegularExpressions;

namespace DataMiningCourts
{
    class JV_ThreadPoolDownload : ALL_ThreadPoolDownload
    {
        private string sFilePathToWriteFileTo;
        public JV_ThreadPoolDownload(FrmCourts frm, ManualResetEvent doneEvent, string sDirectoryPathToWriteFileTo, string sFilePathToWriteFileTo) : base(frm, doneEvent, sDirectoryPathToWriteFileTo)
        {
            this.doneEvent = doneEvent;
            this.sFilePathToWriteFileTo = sFilePathToWriteFileTo;
        }

        public void DownloadDocument(object o)
        {
            var sUrlToReadFileFrom = o.ToString();
            try
            {
                var tmpFolder = Path.Combine(sDirectoryPathToWriteFileTo, sFilePathToWriteFileTo);
                if (!Directory.Exists(tmpFolder))
                {
                    Directory.CreateDirectory(tmpFolder);
                }

                var url = new Uri(sUrlToReadFileFrom);
                var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                var sSource = string.Empty;

                using (StreamReader reader = new StreamReader(request.GetResponse().GetResponseStream(), Encoding.GetEncoding("windows-1250")))
                {
                    sSource = reader.ReadToEnd();
                }

                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.OptionOutputAsXml = true;

                doc.LoadHtml(sSource);

                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(doc.DocumentNode.OuterHtml);

                //Process
                var datumNode = xmlDoc.DocumentElement.SelectSingleNode("//legend");
                if (datumNode == null || !DateTime.TryParseExact(datumNode.InnerText, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime datum))
                {
                    throw new ApplicationException("Na stránce nebylo nalezeno datum.");
                }

                //attachment
                var attNodes = xmlDoc.DocumentElement.SelectNodes("//table[@class='lfr-table djv-agenda-table']//tr[td[contains(text(), 'Příloha')]]");
                if (attNodes != null)
                {
                    foreach (XmlNode attNode in attNodes)
                    {
                        var urlNode = attNode.SelectSingleNode("./td//a[.='PDF'][@href]");
                        if (urlNode == null) return;

                        var docUrl = urlNode.Attributes["href"]?.Value;
                        if (!string.IsNullOrWhiteSpace(docUrl))
                        {
                            url = new Uri(string.Format(parentWindowForm.JV_PAGE_PREFIX, docUrl));
                            var popisPrilohy = attNode.SelectSingleNode("./td[5]").InnerText;
                            var cislaPrilohy = Regex.Matches(popisPrilohy, @"\d+");
                            var fileName = "Priloha_Vz_" + datum.Year + "_" + cislaPrilohy[1] + "_UsnV-" + cislaPrilohy[0] + ".pdf";
                            var downloadedFilePath = Path.Combine(tmpFolder, fileName);

                            Download(url, downloadedFilePath);
                        }
                    }

                }

                //docs
                var docNodes = xmlDoc.DocumentElement.SelectNodes("//table[@class='lfr-table djv-agenda-table']//tr[td[contains(text(), 'Usnesení č.')]]");
                if (docNodes != null)
                {
                    foreach (XmlNode docNode in docNodes)
                    {
                        var urlNode = docNode.SelectSingleNode("./td//a[.='DOC'][@href]");
                        if (urlNode == null) return;
                        var cisloUsneseni = docNode.SelectSingleNode("./td[2]").InnerText.Replace("Usnesení č.", string.Empty).Trim();

                        var docUrl = urlNode.Attributes["href"]?.Value;
                        if (!string.IsNullOrWhiteSpace(docUrl))
                        {
                            url = new Uri(string.Format(parentWindowForm.JV_PAGE_PREFIX, docUrl));
                            var splits = docUrl.Split('/');
                            var docNazev = splits[splits.Length - 1];
                            var fileName = docNazev + ".doc";

                            var downloadedFilePath = Path.Combine(tmpFolder, fileName);

                            Download(url, downloadedFilePath);

                            var dokJUD = new JV_WebDokumentJUD();
                            dokJUD.PathOutputFolder = tmpFolder;

                            var hlavicka = LoadHeader(cisloUsneseni, datum);
                            dokJUD.ZalozDokument(hlavicka);

                            File.Move(downloadedFilePath, dokJUD.PathDoc);

                            FrmCourts.OpenFileInWordAndSaveInWXml(dokJUD.PathDoc, dokJUD.PathTmpXml);


                            var sErrors = String.Empty;
                            if (!dokJUD.ExportFromMsWord(ref sErrors))
                            {
                                throw new NS_Exception("Stažený dokument: " + sErrors);
                            }
                            else if (!string.IsNullOrWhiteSpace(sErrors))
                            {
                                this.parentWindowForm.WriteIntoLogExport(sErrors);
                            }

                            //PostProcess
                            var dOut = new XmlDocument();
                            dOut.Load(dokJUD.PathResultXml);

                            var htmlTextNode = dOut.SelectSingleNode("//html-text");
                            if (htmlTextNode != null)
                            {

                                var attFilename = "Priloha_Vz_" + datum.Year + "_" + cisloUsneseni + "_UsnV-*.pdf";
                                var attFiles = new DirectoryInfo(tmpFolder).GetFiles(attFilename).OrderBy(x => x.Name);
                                var counter = 1;
                                foreach (var attFile in attFiles)
                                {
                                    var docFrag = dOut.CreateDocumentFragment();
                                    docFrag.InnerXml = "<priloha href=\"" + attFile.Name + "\" id-block=\"pr" + counter + "\"><title-num>Příloha č." + counter + "</title-num></priloha>";
                                    dOut.InsertAfter(docFrag, htmlTextNode);
                                    File.Move(downloadedFilePath, dokJUD.PathDoc);
                                    counter++;
                                }
                            }
                            var titulNode = dOut.DocumentElement.SelectSingleNode("//hlavicka-vestnik/titul");
                            if (titulNode != null)
                            {
                                var titulTextNode = dOut.DocumentElement.SelectSingleNode("//html-text/p/span[contains(text(), 'k návrhu')]");
                                if (titulTextNode != null && !string.IsNullOrWhiteSpace(titulTextNode.InnerText))
                                {
                                    titulNode.InnerText = titulTextNode.InnerText.First().ToString().ToUpper() + titulTextNode.InnerText.Substring(1);
                                }
                            }


                            var xnDocumentText = dOut.SelectSingleNode("//html-text");
                            UtilityXml.RemoveRedundantEmptyRowsInXmlDocument(ref xnDocumentText);
                            dOut.Save(dokJUD.PathResultXml);
                        }
                    }
                }






                //XmlNode xnDivContent = xmlDoc.SelectSingleNode("//div[@class='det_params']");
                //if (xnDivContent == null)
                //{
                //    throw new NS_Exception("Stažený dokument: Nelze nalezt div obsahu na strance dokumentu");
                //}

                //XmlNodeList xNodesP = xnDivContent.SelectNodes("//p");
                //foreach (XmlNode xnP in xNodesP)
                //{
                //    if (String.IsNullOrWhiteSpace(xnP.InnerXml))
                //    {

                //        XmlNode xnNext = xnP.NextSibling;
                //        while (xnNext != null)
                //        {
                //            if (xnNext.Name == "p" && String.IsNullOrWhiteSpace(xnNext.InnerXml))
                //                xnNext.ParentNode.RemoveChild(xnNext);
                //            else
                //                break;
                //            xnNext = xnNext.NextSibling;
                //        }
                //    }
                //}
                //XmlNode xnTableHeader = xnDivContent.FirstChild;
                //if (xnTableHeader == null)
                //{
                //    throw new ApplicationException("Stažený dokument: Nelze nalezt tabulku hlavicky na strance dokumentu");
                //}

                ////   string sTextToBrowser = "<html><head></head>" + xnDivContent.OuterXml + "</html>";
                //xnDivContent = xnTableHeader.SelectSingleNode(".//tr[last()]/td");

                //var sExternalId = string.Empty;
                //var nwHeader = LoadHeader(xnTableHeader, sUrlToReadFileFrom, sExternalId);
                //var documentYear = nwHeader.DatumRozhodnutiDate.Year;

                //var dokJUD = new ESLP_WebDokumentJUD();
                //dokJUD.PathOutputFolder = sDirectoryPathToWriteFileTo;
                //dokJUD.ZalozDokument(nwHeader);

                //dokJUD.PrepareSaveWordForExport(xnDivContent);

                //string sErrors = String.Empty;
                //if (!dokJUD.ExportFromMsWord(ref sErrors))
                //{
                //    throw new NS_Exception("Stažený dokument: " + sErrors);
                //}
                //else if (!string.IsNullOrWhiteSpace(sErrors))
                //{
                //    this.parentWindowForm.WriteIntoLogExport(sErrors);
                //}

                //XmlDocument dOut = new XmlDocument();
                //dOut.Load(dokJUD.PathXml);
                //dOut.DocumentElement.InnerXml = dOut.DocumentElement.InnerXml.Replace("&amp;nbsp;", " ");
                //XmlNode xnDocumentText = dOut.SelectSingleNode("//html-text");
                //XmlNode xnSpisovaZnackaHeader = dOut.SelectSingleNode("//cislojednaci");
                //var iPosition = 0;
                //string currentDocumentName = dokJUD.DocumentName;
                //iPosition = dokJUD.PathXml.LastIndexOf('\\');
                //// dokumentName to sice je, ale i s W_ prefixem...
                //string sDocumentName = dokJUD.PathWordXml.Substring(iPosition + 1).Replace(".xml", "");

                //using (SqlConnection conn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["LexData"].ConnectionString))
                //{
                //    conn.Open();

                //    // linking
                //    Linking oLinking = new Linking(conn, "cs", false, null);
                //    oLinking.Run(0, sDocumentName, dOut, 17);
                //    dOut = oLinking.LinkedDocument;

                //    try
                //    {
                //        UtilityXml.AddCite(dOut, sDocumentName, conn);
                //    }
                //    catch (Exception ex)
                //    {
                //        this.parentWindowForm.WriteIntoLogCritical(ex.Message);
                //    }
                //    UtilityXml.RemoveRedundantEmptyRowsInXmlDocument(ref xnDocumentText);
                //    dOut.Save(dokJUD.PathXml);
                //    conn.Close();
                //}

                ///* Přesunout stažené do záloh a výsledné do výsledku
                // */
                //// záloha
                //if (!String.IsNullOrEmpty(this.parentWindowForm.SessionBackupDir))
                //{
                //    File.Move(dokJUD.PathXhtml, String.Format(@"{0}\{1}.htm", this.parentWindowForm.SessionBackupDir, sExternalId));
                //}
                //// výsledek
                //string outputDirectoryFullName = String.Format(@"{0}\{1}", this.parentWindowForm.XML_DIRECTORY, dokJUD.DocumentName);
                //if (Directory.Exists(outputDirectoryFullName))
                //{
                //    this.parentWindowForm.WriteIntoLogCritical("Složka pro dokumentName [{0}] již existuje. Může se jednat o problém s duplicitními spisovými značkami. Po uložení aktuálně stažených dokumentů do db stáhněte dokumenty za období znovu...", outputDirectoryFullName);
                //}
                //else
                //{
                //    Directory.Move(dokJUD.PathFolder, outputDirectoryFullName);
                //    // mass rename if necesarry
                //    if (!String.Equals(dokJUD.DocumentName, currentDocumentName, StringComparison.OrdinalIgnoreCase))
                //    {
                //        Utility.MassRename(outputDirectoryFullName, dokJUD.DocumentName, currentDocumentName);
                //    }

                //    // Everything is OK, so i can coomit citation number!
                //    citation.CommitCitationForAYear(nwHeader.DatumRozhodnutiDate.Year);
                //}
            }
            catch (Exception ex)
            {
                this.parentWindowForm.WriteIntoLogCritical(ex.Message);
            }

            doneEvent.Set();
        }


        private void Download(Uri url, string filePath)
        {
            var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            var response = (System.Net.HttpWebResponse)request.GetResponse();
            response.Close();

            var iSize = response.ContentLength;
            var iRunningByteTotal = 0;
            using (System.Net.WebClient client = new System.Net.WebClient())
            {
                using (System.IO.Stream streamRemote = client.OpenRead(url))
                {
                    using (Stream streamLocal = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        int iByteSize = 0;
                        byte[] byteBuffer = new byte[iSize];
                        while ((iByteSize = streamRemote.Read(byteBuffer, 0, byteBuffer.Length)) > 0)
                        {
                            streamLocal.Write(byteBuffer, 0, iByteSize);
                            iRunningByteTotal += iByteSize;
                        }
                        streamLocal.Close();
                    }
                    streamRemote.Close();
                }
            }
        }

        private JV_WebHeader LoadHeader(string cisloUsneseni, DateTime datumSchvaleni)
        {
            var webhlav = new JV_WebHeader();

            webhlav.DatumSchvaleni = datumSchvaleni;
            webhlav.CisloUsneseni = cisloUsneseni;
            webhlav.Citace = string.Format("{0}/{1} UsnV", webhlav.CisloUsneseni, webhlav.DatumSchvaleni.Year);

            if (citation.ReferenceNumberIsAlreadyinDb(webhlav.DatumSchvaleni, webhlav.CisloUsneseni))
            {
                throw new DuplicityException(String.Format("Znacka [{0}] s daným datem rozhodnutí [{1}] je v jiz databazi!", webhlav.CisloUsneseni, webhlav.DatumSchvaleni));
            }
            else
            {
                citation.AddCitationForYear(webhlav.DatumSchvaleni.Year, int.Parse(webhlav.CisloUsneseni), false);
            }
            return webhlav;
        }
    }
}
