using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Collections.Concurrent;
using System.Xml;
using System.Threading.Tasks;
using System.Net;
using System.IO;
using System.Data.SqlClient;
using UtilityBeck;
using System.Text.RegularExpressions;
using System.Diagnostics;
using BeckLinking;

namespace DataMiningCourts
{
    public static class StringExtension
    {
        public static string RemoveWhitespaceAndLower(this string input)
        {
            return new string(input.ToCharArray()
                .Where(c => !Char.IsWhiteSpace(c))
                .ToArray()).ToLower();
        }

        public static string ClearStringFromSpecialHtmlChars(this string pInput)
        {
            /* Replace a special html chars to standart chars, that can be used in Xml strings                
             * By using a standard system function System.Net.WebUtility.HtmlDecode
             * see http://stackoverflow.com/questions/122641/how-can-i-decode-html-characters-in-c
             * 
             * It also add an escape characters when needed &
             * see http://weblogs.sqlteam.com/mladenp/archive/2008/10/21/Different-ways-how-to-escape-an-XML-string-in-C.aspx
             */
            string sPlainText = System.Net.WebUtility.HtmlDecode(pInput);
            string xmlCapable = System.Security.SecurityElement.Escape(sPlainText);
            return xmlCapable.Trim();
        }
    }


    internal class UPVDownloader
    {
        private static string UPV_SEARCH_URL = @"https://isdv.upv.cz/webapp/rozhodnuti.SeznamRozhodnuti";
        private static string UPV_SEARCH_POST_STRING = @"TreeSelV=&CisloSpisu=&Nazev=&DatumOd={0}&DatumDo={1}&FullText=&Paragraf=&ParagrafOper=&KWords=&KWordsOper=&RVydal=&RVydalOper=&SelKateg=";

        /// <summary>
        /// After we post the search data we need to parse session id and use this url to fetch more data, in default, the website only shows 50 search results.
        /// parameter {0} should be the session id and {1} is the row the results should start from
        /// </summary>
        private static string UPV_SEARCH_MORE_DOCUMENTS_URL = @"https://isdv.upv.cz/webapp/rozhodnuti.NactiDetailVyhledatRozhodnuti?xPar={0}&LastRow={1}";

        private static string UPV_SEARCH_DETAIL_URL = @"https://isdv.upv.cz/webapp/rozhodnuti.HledatDetail?idpripad={0}&iddoc=0&hledat=";

        private static string UPV_DOCUMENT_URL = @"https://isdv.upv.cz/webapp/rozhodnuti.showDocP";
        private static string UPV_DOCUMENT_POST_STRING = @"p_id={0}&hledat=";

        /// <summary>
        /// Number of nodes to be searched when trying to find a node with a decisive text inside
        /// </summary>
        private static int COUNT_TO_SEARCH_DECISIVE_NODE = 15;


        /// <summary>
        /// Represents a string, that follows name of the court in the text of the decision
        /// </summary>
        private const string AUTHOR_COURT_FOLLOWING_WORD = "rozhodl";

        private static readonly string[] UPV_COURTS_TO_IGNOTE = new string[] { "Nejvyšší správní soud", "Ústavní soud", "Nejvyšší soud České republiky" };


        /// <summary>
        /// Represents an author, that has to be determined after export
        /// </summary>
        private const string UPV_AUTHOR_COURT = "Soud České republiky";

        private static int UPV_SEARCH_ROWS_STEP = 50;

        private static string POST_HEADERS_CONTENT_TYPE = "application/x-www-form-urlencoded";
        private static int TIMEOUT = 5000;

        private string sSessionId;
        private int iLastSearchRow;
        private int iSearchRowsTotal;

        /// <summary>
        /// List of the decisive words (sentences), which are searched in the Word XML
        /// If found, all foregoing words are removed from the document
        /// </summary>
        private List<string> headerToRemoveEndings;

        private DateTime dtFrom;
        private DateTime dtTo;
        private Action<string> WriteIntoLogDuplicity;
        private Action<string> WriteIntoLogCritical;
        private Action<string> WriteIntoLogExport;
        private Action<int> UpdateProgress;

        /// <summary>
        /// Class, that generates unique citation numbers for UPV
        /// </summary>
        private CitationService csCitationService;

        private string sOutputFolder;
        private string sWorkingFolder;

        internal UPVDownloader(DateTime from, DateTime to, Action<string, object[]> WriteIntoLogDuplicity, Action<string, object[]> WriteIntoLogExport, Action<string, object[]> WriteIntoLogCritical, CitationService UPV_CitationService, string outputFolder, string workingFolder, Action<int> updateProgress)
        {
            this.dtFrom = from;
            this.dtTo = to;

            this.WriteIntoLogDuplicity = (string s) => WriteIntoLogDuplicity(s, new object[0]);
            this.WriteIntoLogCritical = (string s) => WriteIntoLogCritical(s, new object[0]);
            this.WriteIntoLogExport = (string s) => WriteIntoLogExport(s, new object[0]);

            this.UpdateProgress = updateProgress;

            this.csCitationService = UPV_CitationService;

            this.sOutputFolder = outputFolder;
            this.sWorkingFolder = workingFolder;

            InicializeDecisiveWords();
        }


        public void StartDownloading()
        {
            using (TimeoutWebClient downloadClient = new TimeoutWebClient(TIMEOUT))
            {
                downloadClient.Encoding = Encoding.UTF8;

                HtmlAgilityPack.HtmlDocument searchInitPage = RefreshSession(downloadClient);
                string rowsTotal = Regex.Match(searchInitPage.DocumentNode.InnerHtml, @"Zobrazeno&nbsp;\d*&nbsp;z celkem&nbsp;(\d+)&nbsp;rozhodnutí").Groups[1].Value;
                this.iSearchRowsTotal = int.Parse(rowsTotal);
                this.iLastSearchRow = 0;
                while (iLastSearchRow <= iSearchRowsTotal)
                {

                    string searchResultsHtml = downloadClient.DownloadString(String.Format(UPV_SEARCH_MORE_DOCUMENTS_URL, this.sSessionId, this.iLastSearchRow));

                    HtmlAgilityPack.HtmlDocument searchResultsPage = new HtmlAgilityPack.HtmlDocument();
                    searchResultsPage.LoadHtml("<body>" + searchResultsHtml + "</body>");

                    HtmlAgilityPack.HtmlNodeCollection ncVerdictsList = searchResultsPage.DocumentNode.SelectNodes(@"//tr[@class='polozka' or @class='polozka1']");
                    // One record => One document (header)

                    HtmlAgilityPack.HtmlNode hnOneRecord;
                    for (int i = 0; i < ncVerdictsList.Count; ++i)
                    {
                        hnOneRecord = ncVerdictsList[i];
                        ProcessSingleRecord(downloadClient, hnOneRecord);
                        UpdateProgress((int)(100 * (iLastSearchRow + i + 1) / (1.0 * iSearchRowsTotal)));
                    }//for

                    // Lets find the next set of results.
                    iLastSearchRow += UPV_SEARCH_ROWS_STEP;
                }
            }
        }

        private void ProcessSingleRecord(TimeoutWebClient downloadClient, HtmlAgilityPack.HtmlNode hnOneRecord)
        {
            XmlDocument newDocument = CreateHeaderPartOne(hnOneRecord);
            if (newDocument != null)
            {
                /* Generation of Xml element Citace & Dokumentname
                * From elements id-external & datschvaleni*/
                XmlNode xnIdExternal = newDocument.DocumentElement.SelectSingleNode("//id-external");
                XmlNode xnReferenceNumber = newDocument.DocumentElement.FirstChild.SelectSingleNode("./citace");
				XmlNode xnDateApproved = newDocument.DocumentElement.SelectSingleNode("//datschvaleni");

                string sVerdictsReferenceNumber = xnReferenceNumber.InnerText;

                HtmlAgilityPack.HtmlNode detailTrElement = DownloadDetail(downloadClient, xnIdExternal, sVerdictsReferenceNumber);
                if (detailTrElement == null)
                {
                    return;
                }

                string sWordFilePath;
                if (!CreateHeaderPartTwo(newDocument, detailTrElement, out sWordFilePath))
                {
                    WriteIntoLogCritical("Obsah dokumentu z webové stránky detailu neobsahuje všechna potřebná data pro vygenerování dokumentu!");
                    // Header was not created -> Skip the document
                    return;
                }
                DateTime dtDateApproved = DateTime.Parse(xnDateApproved.InnerText);

                string sDocumentName = CreateDocumentNames(newDocument, sVerdictsReferenceNumber, dtDateApproved);

                /* Nastavení cest */
                string sFullPathToResultFolder = String.Format(@"{0}\{1}", this.sOutputFolder, sDocumentName);
                string sFullPathToResultFile = String.Format(@"{0}\{1}.xml", sFullPathToResultFolder, sDocumentName);
                if (Directory.Exists(sFullPathToResultFolder))
                {
                    this.WriteIntoLogCritical(String.Format("Složka pro dokumentName [{0}] již existuje", sDocumentName));
                    // Word document cannot be downloaded (text of verdict -> skip the document
                    // Citation number has not been used
                    this.csCitationService.RevertCitationForAYear(dtDateApproved.Year);
                    return;
                }

                string sPathWordXml = "";
                if (!DownloadTextAndExportToXML(downloadClient, sWordFilePath, dtDateApproved, sDocumentName, sFullPathToResultFolder, sVerdictsReferenceNumber, ref sPathWordXml))
                {
                    return;
                }

                /* Save Xml Header */
                newDocument.Save(sFullPathToResultFile);

                XmlDocument doc = ExportDocument(dtDateApproved, sDocumentName, sFullPathToResultFolder, sFullPathToResultFile, sPathWordXml);
                if (doc == null)
                {
                    return;
                }

                ValidateLinks(doc);

                XmlNodeList lines = doc.SelectNodes("//p");
                int count = 0; bool success = false;
                foreach (XmlNode line in lines)
                {
                    if (count > 40)
                    {
                        break;
                    }
                    if (line.InnerText.RemoveWhitespaceAndLower().StartsWith("odůvodnění"))
                    {
                        string lawsLine = line.NextSibling.InnerText;
                        int i = lawsLine.ToLower().IndexOf("podle ustanovení");
                        if (i >= 0)
                        {
                            lawsLine = lawsLine.Substring(i + "podle ustanovení".Length);
                        }
                        else
                        {
                            i = lawsLine.ToLower().IndexOf("podle");
                            if (i >= 0)
                            {
                                lawsLine = lawsLine.Substring(i + "podle".Length);
                            }
                            else
                            {
                                i = lawsLine.ToLower().IndexOf("s odvoláním na ustanovení");
                                if (i >= 0)
                                {
                                    lawsLine = lawsLine.Substring(i + "s odvoláním na ustanovení".Length);
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }

                        i = lawsLine.IndexOf("Sb.");
                        if (i < 0) { break; }
                        lawsLine = lawsLine.Substring(0, i + 3);

                        

                        XmlNode xn = doc.SelectSingleNode("//zakladnipredpis");
                        if (xn == null)
                        {
                            xn = doc.SelectSingleNode("//hlavicka-judikatura");
                            xn.AppendChild(doc.CreateElement("zakladnipredpis"));
                            xn = doc.SelectSingleNode("//zakladnipredpis");
                        }
                        success = LinkingHelper.AddBaseLaws(doc, lawsLine, xn);
                        break;
                    }
                }
                if (!success)
                {
                    WriteIntoLogExport(String.Format("V dokumentu {0} se nepodařilo detekovat základní předpis.", sDocumentName));
                }

                IdentifyAuthorAndSave(dtDateApproved, sDocumentName, sFullPathToResultFolder, sFullPathToResultFile, doc);

            }
        }

        

        private HtmlAgilityPack.HtmlNode DownloadDetail(TimeoutWebClient downloadClient, XmlNode xnIdExternal, string sVerdictsReferenceNumber)
        {
            // Try to download a detail of verdict
            HtmlAgilityPack.HtmlDocument verdictDoc = new HtmlAgilityPack.HtmlDocument();
            string urlToDownload = String.Format(UPV_SEARCH_DETAIL_URL, xnIdExternal.InnerText);
            try
            {
                string htmlCode = downloadClient.DownloadString(urlToDownload);
                verdictDoc.LoadHtml(htmlCode);
            }
            catch (WebException wex)
            {
                WriteIntoLogCritical(String.Format("Data dokumentu se z webové stránky {0} nepodařilo stáhnout.{1}\t[{2}]", urlToDownload, Environment.NewLine, wex.Message));
                return null;
            }

            /* All verdicts related to the one case
             * It is essential to find a verdict to the header that has been created
             * Orientation by the reference number
             */
            HtmlAgilityPack.HtmlNode td = verdictDoc.DocumentNode.SelectSingleNode(".//td[text()='" + sVerdictsReferenceNumber + "']");

            if (td == null)
            {
                WriteIntoLogCritical(String.Format("Obsah dokumentu z webové stránky {0} neobsahuje položku, jejíž spisová značka=[{1}].", urlToDownload, sVerdictsReferenceNumber));
                return null;
            }
            return td.ParentNode.ParentNode.ParentNode.ParentNode;
        }

        private XmlDocument ExportDocument(DateTime dtDateApproved, string sDocumentName, string sFullPathToResultFolder, string sFullPathToResultFile, string sPathWordXml)
        {
            string sPathExportWord = String.Format(@"{0}\W_{1}-0.xml.xml", sFullPathToResultFolder, sDocumentName);
            File.Copy(sPathWordXml, sPathExportWord, true);

			// Export
			// 9= judikatura, 0 = verze, 17 = kvalita
			string sExportErrors = String.Empty;
			string[] parametry = new string[] { "CZ", sFullPathToResultFolder + "\\" + sDocumentName + ".xml", sDocumentName, "0", "17" };
			try
			{
				ExportWordXml.ExportWithoutProgress export = new ExportWordXml.ExportWithoutProgress(parametry);
				export.RunExport();
				sExportErrors = export.errors;
			}
			catch (Exception ex)
			{
				WriteIntoLogCritical("Export zcela selhal! ");
				WriteIntoLogCritical(String.Format("\tException message: [{0}]", ex.Message));
				this.csCitationService.RevertCitationForAYear(dtDateApproved.Year);
				return null;
			}

            XmlDocument xDocAfterExport = new XmlDocument();
            try
            {
				xDocAfterExport.Load(sFullPathToResultFile);
                /* Nodes from header, which has an empty InnerText */
                UtilityBeck.UtilityXml.RemoveEmptyElementsFromHeader(ref xDocAfterExport);
                /* Nodes from document named "span", which has an empty InnerText (They would cause validation issues) */
                UtilityBeck.UtilityXml.DeleteEmptyNodesByName(xDocAfterExport, "span");

                if (!String.IsNullOrEmpty(sExportErrors))
                {
                    this.WriteIntoLogExport(String.Format("{0}:{1}", sDocumentName, sExportErrors));
                }
            }
            catch (Exception)
            {
                this.WriteIntoLogCritical("Export zcela selhal! " + sExportErrors);
                this.csCitationService.RevertCitationForAYear(dtDateApproved.Year);
                return null;
            }
            return xDocAfterExport;
        }

        private void IdentifyAuthorAndSave(DateTime dtDateApproved, string sDocumentName, string sFullPathToResultFolder, string sFullPathToResultFile, XmlDocument xDocAfterExport)
        {
            if (IdentifyAuthor(sDocumentName, xDocAfterExport))
            {
                /* Save exported document, It is done */
                xDocAfterExport.Save(sFullPathToResultFile);
                this.csCitationService.CommitCitationForAYear(dtDateApproved.Year);
            }
            else if (Directory.Exists(sFullPathToResultFolder))
            {
                /* Delete directories */
                Directory.Delete(sFullPathToResultFolder, true);
                this.csCitationService.RevertCitationForAYear(dtDateApproved.Year);
            }
        }

        private static void ValidateLinks(XmlDocument xDocAfterExport)
        {

            // wrong http links
            XmlNodeList xNodes = xDocAfterExport.SelectNodes("//link");
            foreach (XmlNode xn in xNodes)
            {
                if (xn.Attributes["href"].Value.StartsWith("http"))
                    if (xn.Attributes["href"].Value.Contains("'"))
                    {
                        XmlAttribute a = xDocAfterExport.CreateAttribute("error");
                        a.Value = "Href nemůže obsahovat apostrof";
                        xn.Attributes.Append(a);
                    }
            }
        }

        private bool DownloadTextAndExportToXML(TimeoutWebClient downloadClient, string sWordFilePath, DateTime dtDateApproved, string sDocumentName, string sFullPathToResultFolder, string pSpZn, ref string sPathWordXml)
        {
            /* Download the Ms Word document, save as Xml Word document & Export XmlWord and header*/
            Directory.CreateDirectory(sFullPathToResultFolder);
            string sPathWordDoc = String.Format(@"{0}\W_{1}.html", this.sWorkingFolder, sDocumentName);
            string htmlResult = string.Empty;
            try
            {
                downloadClient.Headers[HttpRequestHeader.ContentType] = POST_HEADERS_CONTENT_TYPE;
                htmlResult = downloadClient.UploadString(UPV_DOCUMENT_URL, sWordFilePath);
                htmlResult = ProcessTextHTML(htmlResult, pSpZn);
                File.WriteAllText(sPathWordDoc, htmlResult);
                //clientVerdictDownload.DownloadFile(sWordFilePath, sPathWordDoc);
            }
            catch (WebException wex)
            {
                WriteIntoLogCritical(String.Format("Dokument obsahující text rozhodnutí nelze z adresy [{0}] stáhnout! Chyba=[{1}]", sWordFilePath, wex.Message));
                // Word document cannot be downloaded (text of verdict -> skip the document
                // Citation number has not been used
                this.csCitationService.RevertCitationForAYear(dtDateApproved.Year);
                return false;
            }

            sPathWordXml = String.Format(@"{0}\W_{1}.xml", sFullPathToResultFolder, sDocumentName);
            try
            {
                FrmCourts.OpenFileInWordAndSaveInWXml(sPathWordDoc, sPathWordXml);
            }
            catch (Exception ex)
            {
                WriteIntoLogCritical(String.Format("OtevriSouborVeWordUlozitXML selhal! Chyba=[{0}]", ex.Message));
                // Ms Word document has not been saved as Xml Word -> Skip the document
                // Citation number has not been used
                this.csCitationService.RevertCitationForAYear(dtDateApproved.Year);
                return false;
            }
            return true;
        }

        private string ProcessTextHTML(string htmlResult, string pSpZn)
        {
            try
            {
                HtmlAgilityPack.HtmlDocument textDocument = new HtmlAgilityPack.HtmlDocument();
                textDocument.LoadHtml(htmlResult);


                var textNodes = textDocument.DocumentNode.SelectNodes("//div");

                /* There is a limit, this function is going throught a specific amount of nodes */
                int maxNodesToSearched = Math.Min(textNodes.Count, COUNT_TO_SEARCH_DECISIVE_NODE);

                string sSpecialStartWith1 = "Předseda Úřadu průmyslového vlastnictví".RemoveWhitespaceAndLower();
                int i = 0;
                for (i = 0; i < maxNodesToSearched; ++i)
                {
                    string text = textNodes[i].InnerText.RemoveWhitespaceAndLower();
                    if (this.headerToRemoveEndings.Contains(text))
                    {
                        break;
                    }

                    if (text.StartsWith(sSpecialStartWith1))
                    {
                        i--;
                        break;
                    }
                }
                if (i < maxNodesToSearched)
                {
                    for (; i >= 0; i--)
                    {
                        textNodes[i].ParentNode.RemoveChild(textNodes[i]);
                    }
                }


                var styles = textDocument.DocumentNode.SelectNodes("//style");
                foreach (var style in styles)
                {
                    style.InnerHtml = "";
                }

                var hrs = textDocument.DocumentNode.SelectNodes("//hr");
                foreach (var hr in hrs)
                {
                    string siblingText;
                    try
                    {
                        siblingText = hr.ParentNode.NextSibling.InnerText.Trim();
                        while (string.IsNullOrWhiteSpace(siblingText) || Regex.Match(siblingText, @"^\d+$").Success || siblingText.Trim().Equals("pokračování") || siblingText.Trim().Equals("pokračován") || siblingText.Trim().Equals("í") || siblingText.Trim().Equals(pSpZn))
                        {
                            hr.ParentNode.ParentNode.RemoveChild(hr.ParentNode.NextSibling);
                            siblingText = hr.ParentNode.NextSibling.InnerText.Trim();
                        }
                    }
                    catch { }

                    try
                    {
                        siblingText = hr.ParentNode.PreviousSibling.InnerText.Trim();
                        while (string.IsNullOrWhiteSpace(siblingText))
                        {
                            hr.ParentNode.ParentNode.RemoveChild(hr.ParentNode.PreviousSibling);
                            siblingText = hr.ParentNode.PreviousSibling.InnerText.Trim();
                        }
                    }
                    catch { }

                    try
                    {
                        hr.ParentNode.ParentNode.RemoveChild(hr.ParentNode);
                    }
                    catch { }
                }



                var div = textDocument.DocumentNode.SelectSingleNode("//*[contains(@style,'top')]");
                while (div != null)
                {
                    var match = Regex.Match(div.Attributes["style"].Value, @"top:\d+px");
                    if (match.Success)
                    {
                        var sameLineNodes = textDocument.DocumentNode.SelectNodes("//*[contains(@style,'" + match.Value + "')]");
                        foreach (var node in sameLineNodes)
                        {
                            if (div != node)
                            {
                                div.InnerHtml += node.InnerHtml;
                                node.ParentNode.RemoveChild(node);
                            }
                        }
                    }
                    var leftMatch = Regex.Match(div.Attributes["style"].Value, @"left:(\d+)px");
                    div.Attributes["style"].Value = "";
                    int left;
                    if (int.TryParse(leftMatch.Groups[1].Value, out left))
                    {
                        if (div.InnerText.Trim().Equals("O d ů v o d n ě n í:") || div.InnerText.Trim().Equals("O d ů v o d n ě n í") || div.InnerText.Trim().Equals("P o u č e n í") || div.InnerText.Trim().Equals("P o u č e n í:"))
                        {
                            div.Attributes["style"].Value = "text-align:center;font-weight:bold;";
                        }
                        else if (left < 100 &&
                            (!Regex.Match(div.InnerText.Trim(), @"^[\dIVX]+\s?\.").Success || /* a date*/ Regex.Match(div.InnerText.Trim(), @"^\d{1,2}\.\s*\d{1,2}\.\s*\d{4}").Success))
                        {
                            while (div.PreviousSibling.NodeType == HtmlAgilityPack.HtmlNodeType.Text && string.IsNullOrWhiteSpace(div.PreviousSibling.InnerHtml))
                            {
                                div.ParentNode.RemoveChild(div.PreviousSibling);
                            }
                            if (!string.IsNullOrWhiteSpace(div.PreviousSibling.InnerText) && (!div.PreviousSibling.Attributes.Contains("style") || !div.PreviousSibling.Attributes["style"].Value.Contains("center")))
                            {
                                div.PreviousSibling.InnerHtml += div.InnerHtml;
                                div.ParentNode.RemoveChild(div);
                            }
                        }

                    }


                    div = textDocument.DocumentNode.SelectSingleNode("//*[contains(@style,'top')]");
                }

                return textDocument.DocumentNode.OuterHtml;
            }
            catch
            {
                return htmlResult;
            }
        }

        private string CreateDocumentNames(XmlDocument newDocument, string sVerdictsReferenceNumber, DateTime dtDateApproved)
        {
            int nextCitationNumber = this.csCitationService.GetNextCitation(dtDateApproved.Year);
            string sCitation = String.Format("ÚPV {0}/{1}", nextCitationNumber, dtDateApproved.Year);

            XmlNode xnCitation = newDocument.DocumentElement.SelectSingleNode("./judikatura-section/header-j/citace");
            xnCitation.InnerText = sCitation;

            string sDocumentName, judikaturaSectionDokumentName;
            Utility.CreateDocumentName("J", sVerdictsReferenceNumber, dtDateApproved.Year.ToString(), out sDocumentName);
            Utility.CreateDocumentName("J", sCitation, dtDateApproved.Year.ToString(), out judikaturaSectionDokumentName);
            XmlNode xnJudikaturaSection = newDocument.SelectSingleNode("//judikatura-section");
            xnJudikaturaSection.Attributes["id-block"].Value = judikaturaSectionDokumentName;
            newDocument.DocumentElement.Attributes.Append(newDocument.CreateAttribute("DokumentName"));
            newDocument.DocumentElement.Attributes["DokumentName"].Value = sDocumentName;
            return sDocumentName;
        }


        private bool IdentifyAuthor(string sDocumentName, XmlDocument xDocAfterExport)
        {
            /* If author == UPV_AUTHOR_COURT (Soud ČR), find out the real one 
             * If it is one of the courts to ignore => delete whole document
             */
            XmlNode xnAuthor = xDocAfterExport.DocumentElement.SelectSingleNode("//autor/item");
            bool save = true;
            if (xnAuthor.InnerText == UPV_AUTHOR_COURT)
            {
                /* Save only documents from courts <> courts to ignore */
                save = false;
                /* Get the first text node, it should contains a substring representing court */
                XmlNode firstTextNode = xDocAfterExport.DocumentElement.SelectSingleNode(String.Format("//html-text//text()[contains(.,'{0}')]", AUTHOR_COURT_FOLLOWING_WORD));
                if (firstTextNode != null)
                {
                    int idxSuffix = firstTextNode.InnerText.IndexOf(AUTHOR_COURT_FOLLOWING_WORD);
                    string realCourt = firstTextNode.InnerText.Substring(0, idxSuffix).Trim();
                    /* If court is one of the courts, that suppose to be ignored, set ignore flag. Otherwise change author node*/
                    if (!UPV_COURTS_TO_IGNOTE.Contains(realCourt))
                    {
                        xnAuthor.InnerText = realCourt;
                        save = true;
                    }
                }
                else
                {
                    WriteIntoLogCritical(String.Format("{0}: Uzel pro určení autora (konkrétního soudu) nenalezen!", sDocumentName));
                }
            }
            return save;
        }

        private HtmlAgilityPack.HtmlDocument RefreshSession(TimeoutWebClient downloadClient)
        {
            string postString = String.Format(UPV_SEARCH_POST_STRING, this.dtFrom.ToShortDateString(), this.dtTo.ToShortDateString());

            downloadClient.Headers[HttpRequestHeader.ContentType] = POST_HEADERS_CONTENT_TYPE;
            string searchInitialization = downloadClient.UploadString(UPV_SEARCH_URL, postString);

            // Lets find the current sessiont id to be used to get the results
            HtmlAgilityPack.HtmlDocument searchInitPage = new HtmlAgilityPack.HtmlDocument();
            searchInitPage.LoadHtml(searchInitialization);

            var sessionSecret = searchInitPage.DocumentNode.SelectSingleNode(@"//input[@id='SSECRET']");
            this.sSessionId = sessionSecret.Attributes["value"].Value;

            return searchInitPage;
        }



        /// <summary>
        /// Create a new header based on the text of the webpage (list of searched documents)
        ///  Číslo případu  	    =>  id-external & id dokumentu?
        ///  Datum rozhodnutí  	    =>  datschvaleni
        ///  Značka spisu případu  	=>  citace
        ///  Znění/Název  	        =>  titul
        ///  Výsledek rozhodnutí  	=>  info4xml/item
        ///  Rozhodnutí vydal  	    =>  autor/item , but without the text "II. stupeň řízení"
        /// </summary>
        /// <param name="tr"></param>
        private XmlDocument CreateHeaderPartOne(HtmlAgilityPack.HtmlNode hnOneRecord)
        {
            XmlDocument newXmlDocument = new XmlDocument();
#if LOCAL_TEMPLATES
            newXmlDocument.Load(@"Templates-J-Downloading\Template_J_UPV.xml");
#else
			string sPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
			newXmlDocument.Load(Path.Combine(sPath, @"Templates-J-Downloading\Template_J_UPV.xml"));
#endif

            HtmlAgilityPack.HtmlNodeCollection tds = hnOneRecord.SelectNodes("./td");
            if (tds.Count != 6)
            {
                this.WriteIntoLogCritical(String.Format("První část generování hlavičky - počet sloupců výsledku [{0}] není roven sedmi!", tds.Count));
                return null;
            }

            /* Číslo případu  	        =>  id-external & id dokumentu? */
            XmlNode xnIdExternal = newXmlDocument.DocumentElement.SelectSingleNode("//id-external");
            xnIdExternal.InnerText = tds[0].InnerText.Trim().ClearStringFromSpecialHtmlChars();

            /* Datum rozhodnutí  	    =>  datschvaleni */
            XmlNode xnDateApproval = newXmlDocument.DocumentElement.SelectSingleNode("//datschvaleni");
            string sDateApprovalNonUni = tds[1].InnerText.Trim().ClearStringFromSpecialHtmlChars();
            string sDateApprovalUni = UtilityBeck.Utility.ConvertDateIntoUniversalFormat(sDateApprovalNonUni, out DateTime? dateApproval);
            xnDateApproval.InnerText = sDateApprovalUni;

            /* Značka spisu případu  	=>  citace */
            XmlNode xnReferenceNumber = newXmlDocument.DocumentElement.FirstChild.SelectSingleNode("./citace");
			string referenceNumberText = tds[2].InnerText.Trim().ClearStringFromSpecialHtmlChars();
            xnReferenceNumber.InnerText = referenceNumberText;

            /* Check for the duplicities */
            if (this.csCitationService.IsAlreadyinDb(dateApproval.Value, referenceNumberText, string.Empty))
            {
                WriteIntoLogDuplicity(String.Format("Značka [{0}] s daným datem rozhodnutí [{1}] je v jiz databazi!", referenceNumberText, dateApproval.Value.ToShortDateString()));
                /* Document already exists -> Skip the document */
                return null;
            }

            /* Znění/Název  	        =>  titul */
            XmlNode xnTitle = newXmlDocument.DocumentElement.SelectSingleNode("//titul");
            xnTitle.InnerText = tds[3].InnerText.Trim().ClearStringFromSpecialHtmlChars();

            /* Výsledek rozhodnutí  	=>  info4xml/item */
            XmlNode xnInfo4 = newXmlDocument.DocumentElement.SelectSingleNode("//info4xml/item");
            xnInfo4.InnerText = tds[4].InnerText.Trim().ClearStringFromSpecialHtmlChars();

            /* Rozhodnutí vydal  	    =>  autor/item , but without text "II. stupeň řízení" */
            string sAuthor = String.Empty;
            string sAuthorWebPage = tds[5].InnerText.ClearStringFromSpecialHtmlChars().Trim();

            switch (sAuthorWebPage)
            {
                case "II. instance":
                    sAuthor = "Úřad průmyslového vlastnictví";
                    break;

                case UPV_AUTHOR_COURT:
                    sAuthor = UPV_AUTHOR_COURT;
                    break;

                case "Soudní dvůr EU":
                    /* přeskakuji */
                    return null;

                default:
                    this.WriteIntoLogCritical(String.Format("Rozhodnutí id [{0}] se vztahuje k neznámému soudu [{1}]", tds[0].InnerText.ClearStringFromSpecialHtmlChars(), sAuthorWebPage));
                    return null;
            }

            XmlNode xnAuthor = newXmlDocument.DocumentElement.SelectSingleNode("//autor/item");
            xnAuthor.InnerText = sAuthor.ClearStringFromSpecialHtmlChars();

            /* Everything is ok => pass to the next stage of processing */
            return newXmlDocument;
        }



        /// <summary>
        /// Based on the detail file, fill out header nodes, that has not been filled yet
        /// Try to gets the link to the decision text (MS Word document)
        /// </summary>
        /// <param name="pHeaderDoc"></param>
        /// <param name="pHn"></param>
        /// <param name="pPostToWordFile"></param>
        /// <returns>Tru, if the pLinkToWordFile was set, otherwise false</returns>
        private bool CreateHeaderPartTwo(XmlDocument pHeaderDoc, HtmlAgilityPack.HtmlNode pHn, out string pPostToWordFile)
        {
            /* Fill nodes rejstrik2 a zakladnipredpis */
            HtmlAgilityPack.HtmlNodeCollection trs = pHn.SelectNodes("./td/table/tr");

            /*
             * Datum rozhodnutí	        => already filled
             * Značka spisu rozhodnutí	=> already filled
             * Znění/Název	            => already filled
             * Výsledek rozhodnutí	    => already filled
             * Rozhodnutí vydal	        => already filled
             * Paragrafy	            => zakladnipredpis, per lines, do not link
             * Klíčová slova	        => rejstrik2/item for each keyword
             * Soubory                  => Link, that contains a decision body. It will be downloaded and saved as W_generated name of the header.doc
             */
            if (trs.Count != 8)
            {
                this.WriteIntoLogCritical(String.Format("Druhá část generování hlavičky - počet položek výsledku [{0}] není roven osmi!", trs.Count));
                pPostToWordFile = String.Empty;
                /* There suppose to be eight records*/
                return false;
            }

            //if (Properties.Settings.Default.PROCESS_ZAKLADNI_PREDPIS)
            //{
            //    UPV_FillZakladniPredpis(pHeaderDoc, tds[5]);           
            //}

            XmlNode xnRegister2 = pHeaderDoc.DocumentElement.SelectSingleNode("//rejstrik2");
            StringBuilder sbRegister = new StringBuilder();
            foreach (HtmlAgilityPack.HtmlNode hnRegister in trs[6].ChildNodes[1].ChildNodes)
            {
                if (!String.IsNullOrWhiteSpace(hnRegister.InnerText))
                {
                    // one item, one register
                    sbRegister.AppendLine(String.Format("<item>{0}</item>", hnRegister.InnerText.Trim().ClearStringFromSpecialHtmlChars()));
                }
            }
            if (sbRegister.Length > 0)
            {
                xnRegister2.InnerXml = sbRegister.ToString();
            }
            else
            {
                /* nothing was added => deletion */
                xnRegister2.ParentNode.RemoveChild(xnRegister2);
            }

            HtmlAgilityPack.HtmlNode hnHref = pHn.SelectSingleNode("./td/ul/li/a[@onclick]");
            if (hnHref == null)
            {
                this.WriteIntoLogCritical(String.Format("Druhá část generování hlavičky - Odkaz na text rozhodnutí nenalezen!"));
                pPostToWordFile = String.Empty;
                /* No link to the text of the decision => Skip the document */
                return false;
            }
            string docId = Regex.Replace(hnHref.Attributes["onclick"].Value.Trim(), @"ShowDocument\('(.*)','rozhodnuti.showDocP',''\);", "$1");

            pPostToWordFile = String.Format(UPV_DOCUMENT_POST_STRING, docId);
            return true;
        }




        /// <summary>
        /// Decisive words are IRELEVANT and REDUNDANT words, that are usually (always) at the begining of the Word document and should be removed
        /// (all text till decisive word should be removed)
        /// </summary>
        private void InicializeDecisiveWords()
        {
            this.headerToRemoveEndings = new List<string>();
            this.headerToRemoveEndings.Add("Usnesení".RemoveWhitespaceAndLower());
            this.headerToRemoveEndings.Add("Jménem republiky".RemoveWhitespaceAndLower());
            this.headerToRemoveEndings.Add("Rozhodnutí".RemoveWhitespaceAndLower());
            this.headerToRemoveEndings.Add("ROZSUDEK JMÉNEM REPUBLIKY".RemoveWhitespaceAndLower());
            this.headerToRemoveEndings.Add("O Z H O D N U T Í".RemoveWhitespaceAndLower());
            this.headerToRemoveEndings.Add("H O D N U T Í".RemoveWhitespaceAndLower());
            this.headerToRemoveEndings.Add("ROZSUDEK TRIBUNÁLU (prvního senátu)".RemoveWhitespaceAndLower());
            this.headerToRemoveEndings.Add("ROZSUDEK TRIBUNÁLU (druhého senátu)".RemoveWhitespaceAndLower());
            this.headerToRemoveEndings.Add("ROZSUDEK TRIBUNÁLU (třetího senátu)".RemoveWhitespaceAndLower());
            this.headerToRemoveEndings.Add("ROZSUDEK TRIBUNÁLU (čtvrtého senátu)".RemoveWhitespaceAndLower());
            this.headerToRemoveEndings.Add("ROZSUDEK TRIBUNÁLU (pátého senátu)".RemoveWhitespaceAndLower());
        }





    }


    public partial class FrmCourts : Form
    {
        /// <summary>
        /// Class, that generates unique citation numbers for UPV
        /// </summary>
        private CitationService UPV_CitationService;

        private UPVDownloader downloader;

        /// <summary>
        /// Click to Data Mining in the UPV tab
        /// - Disable more clicking on that button
        /// - Initialize logs
        /// - Initialize citation service
        /// - All lists and variables are reset (UPV)
        /// - Create blocking Threads for creating documents
        /// - Navigate to the search result for a given year
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private bool UPV_Click()
        {
            string sError = this.UPV_CheckFilledValues();
            if (!String.IsNullOrEmpty(sError))
            {
                MessageBox.Show(this, sError, ERROR_CHECKING_OUTPUT, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            this.btnMineDocuments.Enabled = false;
            this.UPV_dtFrom.Enabled = false;
            this.UPV_dtTo.Enabled = false;

            this.btnWorkingFolder.Enabled = false;
            this.btnWorkingFolder.Enabled = false;
            this.btnOutputFolder.Enabled = false;

            this.cbCheckDuplicities.Enabled = false;



            /* Initialize generation of unique citation numbers for the UPV*/
            if (!citationNumberGenerator.TryGetValue(Courts.cUPV, out this.UPV_CitationService))
            {
                /* First using of the UPV -> Citation service needs to be initialized */
                this.UPV_CitationService = new CitationService(Courts.cUPV);
                citationNumberGenerator.Add(Courts.cUPV, this.UPV_CitationService);
            }

            this.processedBar.Value = 0;

            // FIFO behavior

            /*
             * Create thread that
             * Pick up the pre processed headers, download the rest of the header & body & process
             */
            //UPV_StartAThreadForAdditionalFillingTheHeaders();



            downloader = new UPVDownloader(this.UPV_dtFrom.Value, this.UPV_dtTo.Value, WriteIntoLogDuplicity, WriteIntoLogExport, WriteIntoLogCritical, UPV_CitationService, this.txtOutputFolder.Text, this.txtWorkingFolder.Text, UpdateProgress);

            Task.Factory.StartNew(() =>
            {
                downloader.StartDownloading();


                this.Invoke((MethodInvoker)(() =>
                {
                    FinalizeLogs(false);

                    this.btnMineDocuments.Enabled = true;
                    this.UPV_dtFrom.Enabled = true;
                    this.UPV_dtTo.Enabled = true;

                    this.btnWorkingFolder.Enabled = true;
                    this.btnWorkingFolder.Enabled = true;
                    this.btnOutputFolder.Enabled = true;

                    this.cbCheckDuplicities.Enabled = true;
                }));
            });
            return true;
        }

        private void UpdateProgress(int i)
        {
            this.Invoke((MethodInvoker)(() => { this.processedBar.Value = Math.Min(this.processedBar.Maximum, i); }));
        }




        /// <summary>
        /// Check, wheater all mandatory fields are filled correctly
        /// </summary>
        /// <returns>Result is error message, so no message = no error</returns>
        private string UPV_CheckFilledValues()
        {
            StringBuilder sbResult = new StringBuilder();

            if (String.IsNullOrWhiteSpace(this.txtWorkingFolder.Text))
            {
                sbResult.AppendLine("Pracovní složka (místo pro uložení surových dat) musí být vybrána.");
            }

            return sbResult.ToString();
        }




        /// <summary>
        /// Search results list was loaded in browser
        /// Process the results and create documents from results, that have a decision text (Ms Word document)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UPV_browser_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {

        }




    }
}
