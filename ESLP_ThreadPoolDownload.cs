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
    class ESLP_ThreadPoolDownload : ALL_ThreadPoolDownload
    {
        //private static string ESLP_PAGE_PREFIX = "http://eslp.justice.cz{0}";
        //private string sFilePathToWriteFileTo;
        //private static TimeoutWebClient clientPdfDownload = new TimeoutWebClient(10000);

        ///// <summary>
        ///// Spisová značka dokumentu, která se odstranuje z textu
        ///// </summary>
        //private string spisovaZnackaPorovnani;

        //private bool falseStart
        //{
        //    get { return File.Exists(sFilePathToWriteFileTo); }
        //}

        ///// <summary>
        ///// Dafuelt = false
        ///// </summary>
        //private bool alwaysOverride;

        //public bool AlwaysOverride
        //{
        //    set { this.alwaysOverride = value; }
        //}

        public ESLP_ThreadPoolDownload(FrmCourts frm, ManualResetEvent doneEvent, string sDirectoryPathToWriteFileTo, string sFilePathToWriteFileTo)
            : base(frm, doneEvent, sDirectoryPathToWriteFileTo)
        {
            this.doneEvent = doneEvent;
            //this.sFilePathToWriteFileTo = sFilePathToWriteFileTo;
        }

        public void DownloadDocument(object o)
        {
            string sUrlToReadFileFrom = o.ToString();

            int iPosition = sUrlToReadFileFrom.LastIndexOf('/');
            int iPositionQuestionMark = sUrlToReadFileFrom.IndexOf('?', iPosition);
            string sExternalId = sUrlToReadFileFrom.Substring(iPosition + 1, iPositionQuestionMark - iPosition - 1);

            try
            {
                // first, we need to get the exact size (in bytes) of the file we are downloading
                var url = new Uri(sUrlToReadFileFrom);
                var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                var sSource = string.Empty;
                using (StreamReader reader = new StreamReader(request.GetResponse().GetResponseStream(), Encoding.GetEncoding("windows-1250")))
                {
                    sSource = reader.ReadToEnd();
                }

                HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
                doc.OptionOutputAsXml = true;

                doc.LoadHtml(sSource);

                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(doc.DocumentNode.OuterHtml);

                // smažu zpětné odkazy, jako například tlačítko nahoru, aj.
                XmlNodeList backReferences = xmlDoc.DocumentElement.SelectNodes("//a[@href='#main']");
                foreach (XmlNode backReference in backReferences)
                {
                    backReference.ParentNode.RemoveChild(backReference);
                }

                XmlNode xnDivContent = xmlDoc.SelectSingleNode("//div[@class='det_params']");
                if (xnDivContent == null)
                {
                    throw new NS_Exception("Stažený dokument: Nelze nalezt div obsahu na strance dokumentu");
                }

                XmlNodeList xNodesP = xnDivContent.SelectNodes("//p");
                foreach (XmlNode xnP in xNodesP)
                {
                    if (String.IsNullOrWhiteSpace(xnP.InnerXml))
                    {

                        XmlNode xnNext = xnP.NextSibling;
                        while (xnNext != null)
                        {
                            if (xnNext.Name == "p" && String.IsNullOrWhiteSpace(xnNext.InnerXml))
                                xnNext.ParentNode.RemoveChild(xnNext);
                            else
                                break;
                            xnNext = xnNext.NextSibling;
                        }
                    }
                }
                XmlNode xnTableHeader = xnDivContent.FirstChild;
                if (xnTableHeader == null)
                {
                    throw new ApplicationException("Stažený dokument: Nelze nalezt tabulku hlavicky na strance dokumentu");
                }

                //   string sTextToBrowser = "<html><head></head>" + xnDivContent.OuterXml + "</html>";
                xnDivContent = xnTableHeader.SelectSingleNode(".//tr[last()]/td");


                var nwHeader = LoadHeader(xnTableHeader, sUrlToReadFileFrom, sExternalId);
                var documentYear = nwHeader.DatumRozhodnutiDate.Year;

                var dokJUD = new ESLP_WebDokumentJUD();
                dokJUD.PathOutputFolder = directoryPathToWriteFileTo;
                dokJUD.ZalozDokument(nwHeader);

                dokJUD.PrepareSaveWordForExport(xnDivContent);

                string sErrors = String.Empty;
                if (!dokJUD.ExportFromMsWord(ref sErrors))
                {
                    throw new NS_Exception("Stažený dokument: " + sErrors);
                }
                else if (!string.IsNullOrWhiteSpace(sErrors))
                {
                    this.parentWindowForm.WriteIntoLogExport(sErrors);
                }

                XmlDocument dOut = new XmlDocument();
                dOut.Load(dokJUD.PathXml);
                dOut.DocumentElement.InnerXml = dOut.DocumentElement.InnerXml.Replace("&amp;nbsp;", " ");
                XmlNode xnDocumentText = dOut.SelectSingleNode("//html-text");
                XmlNode xnSpisovaZnackaHeader = dOut.DocumentElement.FirstChild.SelectSingleNode("./citace");

				string currentDocumentName = dokJUD.DocumentName;
                iPosition = dokJUD.PathXml.LastIndexOf('\\');
                // dokumentName to sice je, ale i s W_ prefixem...
                string sDocumentName = dokJUD.PathWordXml.Substring(iPosition + 1).Replace(".xml", "");

                using (SqlConnection conn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["LexData"].ConnectionString))
                {
                    conn.Open();

                    // linking
                    Linking oLinking = new Linking(conn, "cs", null);
                    oLinking.Run(0, sDocumentName, dOut, 17);
                    dOut = oLinking.LinkedDocument;

                    try
                    {
                        UtilityXml.AddCite(dOut, sDocumentName, conn);
                    }
                    catch (Exception ex)
                    {
                        this.parentWindowForm.WriteIntoLogCritical(ex.Message);
                    }
                    UtilityXml.RemoveRedundantEmptyRowsInXmlDocument(ref xnDocumentText);
                    dOut.Save(dokJUD.PathXml);
                    conn.Close();
                }

                // výsledek
                string outputDirectoryFullName = String.Format(@"{0}\{1}", this.parentWindowForm.XML_DIRECTORY, dokJUD.DocumentName);
                if (Directory.Exists(outputDirectoryFullName))
                {
                    this.parentWindowForm.WriteIntoLogCritical("Složka pro dokumentName [{0}] již existuje. Může se jednat o problém s duplicitními spisovými značkami. Po uložení aktuálně stažených dokumentů do db stáhněte dokumenty za období znovu...", outputDirectoryFullName);
                }
                else
                {
                    Directory.Move(dokJUD.PathFolder, outputDirectoryFullName);
                    // mass rename if necesarry
                    if (!String.Equals(dokJUD.DocumentName, currentDocumentName, StringComparison.OrdinalIgnoreCase))
                    {
                        Utility.MassRename(outputDirectoryFullName, dokJUD.DocumentName, currentDocumentName);
                    }

                    // Everything is OK, so i can coomit citation number!
                    citation.CommitCitationForAYear(nwHeader.DatumRozhodnutiDate.Year);
                }
            }
            catch (Exception ex)
            {
                this.parentWindowForm.WriteIntoLogCritical(ex.Message);
            }

            doneEvent.Set();
        }

        private ESLP_WebHeader LoadHeader(XmlNode pXnTable, string pUrl, string pExternalId)
        {
            var webhlav = new ESLP_WebHeader();

            foreach (XmlNode tr in pXnTable.ChildNodes)
            {
                try
                {
                    var levaTD = tr.FirstChild;
                    var pravaTD = levaTD.NextSibling;

                    if (levaTD == null || pravaTD == null) continue;

                    var levaINNER = levaTD.InnerText.Trim();
                    var pravaINNER = pravaTD.InnerText.Trim();

                    switch (levaINNER)
                    {
                        case "Číslo stížnosti:":
                            webhlav.CisloStiznosti = pravaINNER;
                            break;
                        case "Jméno / název stěžovatele:":
                            webhlav.NazevStezovatele = pravaINNER;
                            break;
                        case "Typ rozhodnutí:":
                            webhlav.TypRozhodnuti = pravaINNER;
                            break;
                        case "Datum rozhodnutí:":
                            webhlav.DatumRozhodnutiDate = DateTime.ParseExact(pravaINNER, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);
                            webhlav.DatumRozhodnuti = webhlav.DatumRozhodnutiDate.ToString(Utility.DATE_FORMAT);
                            break;
                        case "Významnost:":
                            webhlav.Vyznamnost = pravaINNER;
                            break;

                        case "Hesla:":
                        case "Heslo:":
                            NaplnSeznamStringuOddelenychBR(webhlav.Hesla, pravaTD);
                            break;
                        case "Popis:":
                            webhlav.Popis = pravaINNER;
                            break;
                    }
                }
                catch { };
            }

            var citationNumber = citation.GetNextCitation(webhlav.DatumRozhodnutiDate.Year);
            webhlav.Citace = string.Format("Výběr ESLP {0}/{1}", citationNumber, webhlav.DatumRozhodnutiDate.Year);

            if (citation.ReferenceNumberIsAlreadyinDb(webhlav.DatumRozhodnutiDate, webhlav.CisloStiznosti))
            {
                throw new DuplicityException(String.Format("Znacka [{0}] s daným datem rozhodnutí [{1}] je v jiz databazi!", webhlav.CisloStiznosti, webhlav.DatumRozhodnuti));
            }

            webhlav.IdExternal = pExternalId;

            return webhlav;
        }
    }
}
