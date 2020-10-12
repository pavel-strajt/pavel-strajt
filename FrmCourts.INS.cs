#define LOCAL_TEMPLATES

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.Net;
using System.Threading;
using System.Globalization;
using UtilityBeck;
using System.Xml;
using System.Threading.Tasks;
using System.Data.SqlClient;
using System.IO;
using BeckLinking;

namespace DataMiningCourts
{
    public static class StringExtensions
    {
        private static readonly Regex regWhiteSpace = new Regex(@"\s+");
        private static readonly string WHITESPACES_REPLACEMENT = " ";

        public static string RemoveMultiWhitespaces(this string s)
        {
            return regWhiteSpace.Replace(s, WHITESPACES_REPLACEMENT);
        }
    }

    public partial class FrmCourts : Form
    {
        private static readonly string INS_INDEX_PARAM = @"https://isir.justice.cz/isir/ueu/vysledek_lustrace.do?nazev_osoby=&vyhledat_pouze_podle_zacatku=on&podpora_vyhledat_pouze_podle_zacatku=true&jmeno_osoby=&ic=&datum_narozeni=&rc=&mesto=&cislo_senatu=&bc_vec=&rocnik=&id_osoby_puvodce=&druh_stav_konkursu=&datum_stav_od=&datum_stav_do=&aktualnost=AKTUALNI_I_UKONCENA&druh_kod_udalost=&datum_akce_od=&datum_akce_do=&nazev_osoby_f=&cislo_senatu_vsns=&druh_vec_vsns=&bc_vec_vsns=&rocnik_vsns=&cislo_senatu_icm=&bc_vec_icm=&rocnik_icm=&rowsAtOnce=50&captcha_answer=&spis_znacky_datum={0}&spis_znacky_obdobi=14DNI";

        private static readonly string INS_REG_LINK_DETAIL = @"//table[@class='vysledekLustrace']//a[contains(@href, 'evidence_upadcu_detail.do?id')]";

        private static readonly string INS_DETAIL_ROOT = @"https://isir.justice.cz";
        
        private static readonly string INS_BASE_ADDRESS = @"https://isir.justice.cz/isir/ueu/";

        private static readonly Regex INS_regEventNumber = new Regex(@"(\d+)\.$", RegexOptions.RightToLeft);

        private static readonly Regex INS_regInfo3 = new Regex("id=(.+)$", RegexOptions.RightToLeft);

        /// <summary>
        /// Order number of event within section -> Suffix of reference number
        /// </summary>
        private static int INS_TAB_EVENT_NUMBER = 0;
        /// <summary>
        /// Publication date -> Ze dne
        /// </summary>
        private static int INS_TAB_EVENT_PUBLICATION_DATE = 1;
        /// <summary>
        /// Event description -> Popis události
        /// EG: "Vyhláška o svolání přezkumného jednání"
        /// </summary>
        private static int INS_TAB_EVENT_DESCRIPTION = 3;

        /// <summary>
        /// Link to the pdf document, that contains text related to the event
        /// EG: Text of decision
        /// </summary>
        private static int INS_TAB_EVENT_DOCUMENT = 5;

        /// <summary>
        /// Reference number, that was assigned in court of appeal (NS/VS)
        /// </summary>
        private static int INS_TAB_EVENT_NSVS_REFERENCE_NUMBER = 8;

        /// <summary>
        /// Klíčové slovo popisku pro předání spisu VS/NS
        /// </summary>
        private static readonly string INS_TRANSFER_OF_RECORD = "Převzetí";

        /// <summary>
        /// www.cak.cz/assets/files/2562/INSOLVEN_N__Z_KON-_Mgr._Jan_Koz_k.ppt
        /// SLIDE 4
        /// </summary>
        private static readonly string[] INS_COURT_ABBREVATIONS = 
            new string[] {
            "MSPH", // Městský soud v Praze
            "KSPH", //	Krajský soud v Praze
            "KSCB", //	Krajský soud v Českých Budějovicích
            "KSTB", //	Krajský soud v Českých Budějovicích – pobočka v Táboře*
            "KSPL", //	Krajský soud v Plzni
            "KSKV", //	Krajský soud v Plzni – pobočka v Karlových Varech*
            "KSUL", //	Krajský soud v Ústí nad Labem
            "KSLB", //	Krajský soud v Ústí nad Labem – pobočka v Liberci*
            "KSHK", //	Krajský soud v Hradci Králové
            "KSPA", //	Krajský soud v Hradci Králové – pobočka v Pardubicích
            "KSBR", //	Krajský soud v Brně
            "KSJI", //	Krajský soud v Brně – pobočka v Jihlavě*
            "KSZL", //	Krajský soud v Brně – pobočka ve Zlíně*
            "KSOS", //	Krajský soud v Ostravě
            "KSOL", //	Krajský soud v Ostravě – pobočka v Olomouci*
            "VSPH", //	Vrchní soud v Praze
            "VSOL", //	Vrchní soud v Olomouci
            "NSCR"  //	Nejvyšší soud   
            };

        /// <summary>
        /// Full default reference number
        /// </summary>
        private static readonly Regex INS_regFullInsReferenceNumber;

        /// <summary>
        /// Reference number without suffix
        /// </summary>
        private static readonly Regex INS_regfullInsReferenceNumberWithoutUnitSuffix;

        /// <summary>
        /// Higher court instance - in case of brief
        /// Number, court, number/year
        /// </summary>
        private static readonly Regex INS_regFullInsHigherInstance = new Regex(@"^\d+\s*(?:VSPH|VSOL|NSCR)\s*\d/\d{4}$", RegexOptions.IgnoreCase);

        /// <summary>
        /// Text of reference number
        /// </summary>
        private static readonly Regex INS_regReferenceNumberText = new Regex(@"^(?:spisová\s*značka|sp\.\s*zn\.|č\.\s*j\.)\s*:?\s*", RegexOptions.IgnoreCase);

        /// <summary>
        /// Only unit suffix!
        /// </summary>
        private static readonly Regex INS_regUnitSuffix = new Regex(@"^[ABC]\s*-\s*\d+$", RegexOptions.IgnoreCase);

        /// <summary>
        /// For page number in header matching
        /// It may be just the nuber or number with '-' before and after itself
        /// </summary>
        private static readonly Regex INS_regPageNumberHeader = new Regex(@"^-?\s*(\d+)\s*-?$");

        /// <summary>
        /// Number of nodes, that should be examined to find out real document decision date (which is in the text)
        /// </summary>
        private static readonly int INS_REAL_DATE_NODES_TO_EXAMINE = 7;

        /// <summary>
        /// Texts, that is used in the page header (that needs to be deleted!)
        /// </summary>
        private static readonly List<string> INS_PAGE_HEADER_STARTSWITH = new List<string>(new string[] { "pokračování", "„pokračování“", "pokračování:", "- pokračování -", "„pokračování" });

        /// <summary>
        /// Vybere všechny uzly (tr), které
        /// Jsou potomky
        /// div[@id='zalozkaA']//table[@class='evidenceUpadcuDetailTable']
        /// div[@id='zalozkaB']//table[@class='evidenceUpadcuDetailTable']
        /// div[@id='zalozkaC']//table[@class='evidenceUpadcuDetailTable']
        /// A zároveň
        /// Obsahují element td na čtvrtém místě, který v sobě obsahuje text začínající na
        /// Usnesení, Rozsudek, Rozhodnutí nebo Převzetí
        /// </summary>
        private static readonly string INS_XPATH_SECTION_RECORDS_TO_SAVE = @"//div[@id='{0}']/table[@class='evidenceUpadcuDetailTable']//tr/td[4 and (starts-with(normalize-space(), 'Usnesení') or starts-with(normalize-space(), 'Rozsudek') or starts-with(normalize-space(), 'Rozhodnutí') or starts-with(normalize-space(), 'Převzetí'))]/..";

        /// <summary>
        /// Přímí odkaz na uzel obsahující informace o spisové značce
        /// </summary>
        private static readonly string INS_XPATH_REFERENCE_NUMBER_INFORMATION = @"//table[@class='evidenceUpadcuDetail']//tr[3]/td[2]/strong[1]";

        /// <summary>
        /// Přímý odkaz na uzel obsahující informace o soudu
        /// </summary>
        private static readonly string INS_XPATH_COURT_INFORMATION = @"//table[@class='evidenceUpadcuDetail']//tr[3]/td[2]/strong[2]";

        private int INS_actualNumberOfVerdictToProcess, INS_totalNumberOfVerdictToProcess;

        /// <summary>
        /// Příznak, který indikuje, zda-li došlo ke stažení všech dokumentů. (po stisknutí tlačítka načíst)
        /// </summary>
        bool INS_DocumentsWereDownloaded = false;

        /// <summary>
        /// Function, that is used for computing progress. Computations are based on
        /// 1) Number of created Xml documents  (up to 100%)
        /// </summary>
        /// <returns>0-100</returns>
        private int INS_ComputeActualDownloadProgress()
        {
            int partFromCreatedXML = (INS_actualNumberOfVerdictToProcess + 1) * 100 / (INS_totalNumberOfVerdictToProcess + 1);
            return partFromCreatedXML;
        }

        /// <summary>
        /// Funkce pro update progress baru GUI vlákna, kterou lze použít v jiném vlákně (přes delegát)
        /// </summary>
        /// <param name="forceComplete">Parametr, který říká, že je prostě hotovo...</param>
        void INS_UpdateDownloadProgressSafe(bool forceComplete)
        {
            int value = INS_ComputeActualDownloadProgress();
            if (forceComplete)
            {
                value = this.processedBar.Maximum;
            }

            this.processedBar.Value = Math.Min(value, this.processedBar.Maximum);
#if DEBUG
            this.gbProgressBar.Text = String.Format("Zpracováno {0}/{1} XML => {2}%",
                INS_actualNumberOfVerdictToProcess, INS_totalNumberOfVerdictToProcess, this.processedBar.Value);
            Application.DoEvents();
#endif
            bool maximumReached = (this.processedBar.Value == this.processedBar.Maximum);
            // If everything is done, enable new Data Mining
            this.btnMineDocuments.Enabled = this.INS_DocumentsWereDownloaded = maximumReached;
        }

        /// <summary>
        /// Delegát pro update progress baru GUI vlákna, který lze volat v jiném vlákně
        /// Pro aktualizaci na základě předané hodnoty
        /// </summary>
        /// <param name="value"></param>
        void INS_UpdateProgressSafe(int value)
        {
            this.processedBar.Value = value;
            if (value == this.processedBar.Maximum)
            {
                MessageBox.Show(this, "Hotovo", "Převod INS", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private static List<KeyValuePair<string, string>> INS_courtAbbrevationToCourtList;
        private static Dictionary<string, string> INS_sectionsToRegex;
        private static Dictionary<string, string> INS_CourtNameTransformation;
        static FrmCourts()
        {
            INS_sectionsToRegex = new Dictionary<string, string>();
            INS_sectionsToRegex.Add("A", "zalozkaA");
            INS_sectionsToRegex.Add("B", "zalozkaB");
            INS_sectionsToRegex.Add("C", "zalozkaC");

            INS_courtAbbrevationToCourtList = new List<KeyValuePair<string, string>>();
            INS_courtAbbrevationToCourtList.Add(new KeyValuePair<string,string>("VSPH", "Vrchní soud v Praze"));
            INS_courtAbbrevationToCourtList.Add(new KeyValuePair<string,string>("VSOL", "Vrchní soud v Olomouci"));
            INS_courtAbbrevationToCourtList.Add(new KeyValuePair<string,string>("NSCR", "Nejvyšší soud v Brně"));

            INS_CourtNameTransformation = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            INS_CourtNameTransformation.Add("Krajského soudu", "Krajský soud");
            INS_CourtNameTransformation.Add("Městského soudu", "Městský soud");
            /* Pobočka's*/
            INS_CourtNameTransformation.Add("pobočka v Táboře", "pobočka Tábor");
            INS_CourtNameTransformation.Add("pobočka v Karlových Varech", "pobočka Karlovy Vary");
            INS_CourtNameTransformation.Add("pobočka v Liberci", "pobočka Liberec");
            INS_CourtNameTransformation.Add("pobočka v Pardubicích", "pobočka Pardubice");
            INS_CourtNameTransformation.Add("pobočka v Jihlavě", "pobočka Jihlava");
            INS_CourtNameTransformation.Add("pobočka ve Zlíně", "pobočka Zlín");
            INS_CourtNameTransformation.Add("pobočka v Olomouci", "pobočka Olomouc");

            string allCourtsJoined = String.Join("|", INS_COURT_ABBREVATIONS);
            // ppt, page 5
            /* Court, department no, INS, order number/Year-unit-document number 
             * KSBR 39 INS 4/2008-A-1
             */
            INS_regFullInsReferenceNumber = new Regex(String.Concat(String.Format(@"^(?:{0}", allCourtsJoined),@")\s*\d+\s*INS\s*\d+/\d{4}\s*-\s*[ABC]\s*-\s*\d+$"), RegexOptions.IgnoreCase);

            INS_regfullInsReferenceNumberWithoutUnitSuffix = new Regex(String.Concat(String.Format(@"^(?:{0}", allCourtsJoined), @")\s*\d+\s*INS\s*\d+/\d{4}(?:\s*-\s*(?:[ABC](?:\s*-\s*\d+)?)?)?"), RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Třída, ve které jsou uloženy poslední pořadová čísla dokumentů (podle roků)
        /// </summary>
        CitationService INS_CitationService;

        void INS_GenerateDocumentsFromDetail(WebClient client, string addressToDetail)
        {
            /* stáhnu a zpracuju */
            string htmlDetail = String.Empty;
            try
            {
                htmlDetail = client.DownloadString(addressToDetail);
            }
            catch (WebException wex)
            {
                WriteIntoLogCritical("[{0}]: {1}", addressToDetail, wex.Message);
                return;
            }

            HtmlAgilityPack.HtmlDocument docDetail = new HtmlAgilityPack.HtmlDocument();
            docDetail.LoadHtml(htmlDetail);
            /*
             * Defaultní položky hlavičky
             * 1) idExternal == foreignId => vše za '=' v odkazu
             * 2) Základní spisová značka (je buď rozšířena nebo bude obsahovat suffix)
             * 3) Základní soud (mimo odvolání) --> Detail/Spisová značka
             */
            /* Budu generovat dokumenty, má získávat defaultní položky hlavičky */
            Match matchIdExternal = INS_regInfo3.Match(addressToDetail);
            string idExternal = matchIdExternal.Success ? matchIdExternal.Groups[1].Value : String.Empty;
            HtmlAgilityPack.HtmlNode nodeReferenceNumberPrefix = docDetail.DocumentNode.SelectSingleNode(INS_XPATH_REFERENCE_NUMBER_INFORMATION);
            string referenceNumberPrefix = nodeReferenceNumberPrefix != null ? nodeReferenceNumberPrefix.InnerText.RemoveMultiWhitespaces().Trim() : String.Empty;
            HtmlAgilityPack.HtmlNode nodeCourt = docDetail.DocumentNode.SelectSingleNode(INS_XPATH_COURT_INFORMATION);
            string author = nodeCourt != null ? nodeCourt.InnerText.RemoveMultiWhitespaces().Trim() : String.Empty;

            /* Při převzetí věci se mění autor a spisová značka -> až do dalšího rozhodnutí*/
            string temporaryReferenceNumberPrefix = String.Empty;
            string temporaryAuthor = String.Empty;

            /* Potom další položky dle karet A,B,C
             * 4) Spisová značka -> Rozšíření (v případě odvolání/dovolání) nebo suffix
             * 5) DokumentName ze spisové značky
             * 6) Autor je buď základní soud nebo odvolací soud
             * 7) Ze dne /Okamžik zveřejnění (DD.MM.YYYY)
             * 8) Citace => Výběr INS YYYY
             * 9) Částka => Výběr YYYY
             * ================================
             * Hlavička + pdf dokument == výsledek
             *
             * Jedná se o dokumenty, jejiž popis začíná na 'Usnesení', 'Rozsudek', 'Rozhodnutí' nebo 'Převzetí'
             */
            foreach (string section in INS_sectionsToRegex.Keys)
            {
                HtmlAgilityPack.HtmlNodeCollection recordsToCheck = docDetail.DocumentNode.SelectNodes(String.Format(INS_XPATH_SECTION_RECORDS_TO_SAVE, INS_sectionsToRegex[section]));
                if (recordsToCheck != null)
                {
                    foreach (HtmlAgilityPack.HtmlNode recordToCheck in recordsToCheck)
                    {
                        /* Trochu ohledu na webovou stránku... */
                        Thread.Sleep(500);
                        HtmlAgilityPack.HtmlNodeCollection tds = recordToCheck.SelectNodes("./td");
                        string description = tds[INS_TAB_EVENT_DESCRIPTION].InnerText.RemoveMultiWhitespaces().Trim();
                        string[] descriptionSplitted = description.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        string druh = descriptionSplitted.First();
                        /* Pokud se jedná o Převzetí, nastavím dočasné položky */
                        if (druh == INS_TRANSFER_OF_RECORD)
                        {
                            /* Získám spisovou značku v novém soudu a nakombinuji */
                            string referenceNumberTransfered = WebUtility.HtmlDecode(tds[INS_TAB_EVENT_NSVS_REFERENCE_NUMBER].InnerText).RemoveMultiWhitespaces().Trim();
                            if (String.IsNullOrEmpty(referenceNumberTransfered))
                            {
                                // někde je problém
                                continue;
                            }
                            // Spisová značka je předané značky a staré značky
                            temporaryReferenceNumberPrefix = String.Format("{0} {1}", referenceNumberPrefix, referenceNumberTransfered);
                            // ze spisové značky lze zjistit i autora
                            foreach (KeyValuePair<string, string> courtAbbrevationToCourt in INS_courtAbbrevationToCourtList)
                            {
                                if (referenceNumberTransfered.Contains(courtAbbrevationToCourt.Key))
                                {
                                    temporaryAuthor = courtAbbrevationToCourt.Value;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            XmlDocument dOut = new XmlDocument();
#if LOCAL_TEMPLATES
                            dOut.Load(@"Templates-J-Downloading\Template_J_INS.xml");
#else
                            dOut.Load(UtilityBeck.Res_Constants.rs_FolderProgram + @"Templates-J-Downloading\Template_J_INS.xml");
#endif
                            /* titul */
                            XmlNode xnTitul = dOut.DocumentElement.SelectSingleNode("//titul");
                            xnTitul.InnerText = description;

                            /* druh */
                            XmlNode xnDruh = dOut.DocumentElement.SelectSingleNode("//druh");
                            xnDruh.InnerText = druh;

                            /*idExternal*/
                            XmlNode xnIdExternal = dOut.DocumentElement.SelectSingleNode("//id-external");
                            xnIdExternal.InnerText = idExternal;

                            /* Rozsudek, Rozhodnutí nebo Usnesení */
                            /* Spisová značka => spisová značka prefix - section-číslo*/
                            Match matchEventNumber = INS_regEventNumber.Match(tds[INS_TAB_EVENT_NUMBER].InnerText.Trim());
                            int referenceNumberSuffix = matchEventNumber.Success ? Int32.Parse(matchEventNumber.Groups[1].Value) : -1;
                            string sSpZn = String.Format("{0}-{1}-{2}", String.IsNullOrEmpty(temporaryReferenceNumberPrefix) ? referenceNumberPrefix : temporaryReferenceNumberPrefix, section, referenceNumberSuffix);
                            XmlNode xnReferenceNumber = dOut.DocumentElement.FirstChild.SelectSingleNode("./citace");
							xnReferenceNumber.InnerText = sSpZn;

                            /*autor*/
                            /*
                             * "Krajského soudu" -> "Krajský soud"
                             * "Městského soudu" -> "Městský soud"
                             */
                            string finalAuthor = String.IsNullOrEmpty(temporaryAuthor) ? author : temporaryAuthor;
                            finalAuthor = Utility.ReplaceWithDictionary(finalAuthor, INS_CourtNameTransformation);
                            XmlNode xnAuthor = dOut.DocumentElement.SelectSingleNode("//autor/item");
                            xnAuthor.InnerText = finalAuthor;

                            /* Ze dne */
                            DateTime dtFromDay;
                            if (!DateTime.TryParseExact(tds[INS_TAB_EVENT_PUBLICATION_DATE].InnerText.Trim(), "dd.MM.yyyy", null, DateTimeStyles.None, out dtFromDay))
                            {
                                WriteIntoLogCritical("V řetězci [{0}] nebylo rozpoznáno datum...", tds[INS_TAB_EVENT_PUBLICATION_DATE].InnerText.Trim());
                                continue;
                            }

#if !DEBUG
                            /* Duplicity check. It is not 100%, because reference number can change if it does not match reference number in pdf document...*/
                            if (this.INS_CitationService.IsAlreadyinDb(dtFromDay, sSpZn, idExternal))
                            {
                                WriteIntoLogDuplicity(String.Format("Znacka [{0}] s daným datem rozhodnutí [{1}] je v jiz databazi!", sSpZn, dtFromDay));
                                continue;
                            }
#endif

							// in the source this is datum zveřejnění column
                            XmlNode xnApprovalDate = dOut.DocumentElement.SelectSingleNode("//datvydani");
                            xnApprovalDate.InnerText = dtFromDay.ToString(Utility.DATE_FORMAT);
                            /* Because of export, fill something in" */
                            XmlNode xnDatSchvaleni = dOut.SelectSingleNode("//datschvaleni");
                            xnDatSchvaleni.InnerText = dtFromDay.ToString(Utility.DATE_FORMAT);

                            /* dokumentName -> ze spisové značky pomocí utility */
                            string sDocumentName;
                            if (!Utility.CreateDocumentName("J", sSpZn, dtFromDay.Year.ToString(), out sDocumentName))
                            {
                                WriteIntoLogCritical("Document name have not been created from reference number [{0}]", sSpZn);
                                continue;
                            }
                            dOut.DocumentElement.Attributes["DokumentName"].Value = sDocumentName;

                            /* Citace - třída na generování + commit */
                            int citationNumber = this.INS_CitationService.GetNextCitation(dtFromDay.Year);
                            string sCitation = String.Format("Výběr INS {0}/{1}", citationNumber, dtFromDay.Year);
                            XmlNode xnCitation = dOut.DocumentElement.SelectSingleNode("./judikatura-section/header-j/citace");
							xnCitation.InnerText = sCitation;

                            /* DokumentName z citace */
                            string sDocumentNameCitation;
                            if (!Utility.CreateDocumentName("J", sCitation, dtFromDay.Year.ToString(), out sDocumentNameCitation))
                            {
                                WriteIntoLogCritical("Document name have not been created from citation [{0}]", sCitation);
                                continue;
                            }
                            XmlNode xnJudikaturaSection = dOut.SelectSingleNode("//judikatura-section");
                            xnJudikaturaSection.Attributes["id-block"].Value = sDocumentNameCitation;

                            /* odkaz na pdf ke stažení */
                            HtmlAgilityPack.HtmlNode nodeDokumentTextLink = tds[INS_TAB_EVENT_DOCUMENT].SelectSingleNode(".//a[contains(@href, 'PDF')]");
                            if (nodeDokumentTextLink != null)
                            {
                                string documentTextLink = String.Format("{0}{1}", INS_DETAIL_ROOT, nodeDokumentTextLink.Attributes["href"].Value);
                                string fileNamePdf = String.Format(@"{0}\{1}.pdf", this.txtWorkingFolder.Text, sDocumentName);
                                try
                                {
                                    client.DownloadFile(documentTextLink, fileNamePdf);
                                }
                                catch (WebException wex)
                                {
                                    WriteIntoLogCritical(wex.Message);
                                }

                            }

                            string fileNameXml = String.Format(@"{0}\{1}.xml", this.txtWorkingFolder.Text, sDocumentName);
                            dOut.Save(fileNameXml);

                            this.Log.InfoFormat("{0}", sSpZn);
                            this.INS_CitationService.CommitCitationForAYear(dtFromDay.Year);
                            temporaryAuthor = temporaryReferenceNumberPrefix = String.Empty;
                        }
                    }
                }
            }
        }

        void INS_DownloadDocuments(DateTime datum)
        {
            using (WebClient client = new WebClient())
            {
                client.Encoding = Encoding.GetEncoding("windows-1250");

				string addressIndex = String.Format(INS_INDEX_PARAM, datum.ToShortDateString());
				string htmlIndex = String.Empty;
				try
				{
					htmlIndex = client.DownloadString(addressIndex);
				}
				catch (WebException wex)
				{
					WriteIntoLogCritical("INS: Dokument index has not been downloaded because of WebException [{0}]", wex.Message);
					return;
				}

				/* Webpage was succesfully loaded => Initialize html document */
				HtmlAgilityPack.HtmlDocument docIndex = new HtmlAgilityPack.HtmlDocument();
				docIndex.LoadHtml(htmlIndex);

				/* Initialize list of documents! */
				HtmlAgilityPack.HtmlNodeCollection nodesLinkToDetail = docIndex.DocumentNode.SelectNodes(INS_REG_LINK_DETAIL);
				if (nodesLinkToDetail != null)
				{
					Random r = new Random();
					this.INS_totalNumberOfVerdictToProcess = nodesLinkToDetail.Count;
					foreach (HtmlAgilityPack.HtmlNode nodeLinkToDetail in nodesLinkToDetail)
					{
						/* PreviousSibling of Parent of nodeLinkToDetail should contains datetime representing start of court session */
						HtmlAgilityPack.HtmlNode nStartOfCourtSession = nodeLinkToDetail.SelectSingleNode(@"../preceding-sibling::td[1]");
						string startOfCourtSession = nStartOfCourtSession != null ? nStartOfCourtSession.InnerText.Trim() : String.Empty;
						/* Try to parse it as DateTime */
						DateTime dtStartOfCourtSession;
						/* Datetime format example: 23.12.2014 - 14:25 */
						if (DateTime.TryParseExact(startOfCourtSession, "dd.MM.yyyy - HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out dtStartOfCourtSession))
						{
							/* If the parsed datetime is outside demanded range, skip it */
							if ((this.INS_demandedDateFrom.HasValue && dtStartOfCourtSession.Date.Ticks < this.INS_demandedDateFrom.Value.Date.Ticks) ||
								(this.INS_demandedDateTo.HasValue && dtStartOfCourtSession.Date.Ticks > this.INS_demandedDateTo.Value.Date.Ticks))
							{
								++this.INS_actualNumberOfVerdictToProcess;
								continue;
							}
						}


						string addressToDetail = String.Format("{0}{1}", INS_BASE_ADDRESS, nodeLinkToDetail.Attributes["href"].Value.Replace("&#61;", "="));
						Log.Info(String.Format("Otevírám detail řízení ... {0}", addressToDetail));
						/* Počkám chvilku */
						Thread.Sleep(r.Next(200, 1500));
						INS_GenerateDocumentsFromDetail(client, addressToDetail);

						++this.INS_actualNumberOfVerdictToProcess;
						/* Update progress */
						INS_UpdateDownloadProgressSafe(false);
					}
                }

                /* Update progress => Konec */
                INS_UpdateDownloadProgressSafe(true);
                FinalizeLogs();
            }
        }

        private DateTime? INS_demandedDateFrom;
        private DateTime? INS_demandedDateTo;

        private bool INS_Click()
        {
            string sError = INS_CheckFilledValues();
            if (!String.IsNullOrEmpty(sError))
            {
                MessageBox.Show(this, sError, ERROR_CHECKING_OUTPUT, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return false;
            }

            if (!this.citationNumberGenerator.ContainsKey(Courts.cINS))
            {
                this.citationNumberGenerator.Add(Courts.cINS, new CitationService(Courts.cINS));
            }
            this.INS_CitationService = this.citationNumberGenerator[Courts.cINS];

            this.INS_actualNumberOfVerdictToProcess = this.INS_totalNumberOfVerdictToProcess = 0;

            this.INS_DocumentsWereDownloaded = false;
            this.INS_demandedDateFrom = this.INS_dtpDateFrom.Value;
            this.INS_demandedDateTo = this.INS_dtpDateTo.Value;
            INS_DownloadDocuments(this.INS_dtpDateTo.Value);
            return true;
        }

        private string INS_CheckFilledValues()
        {
            StringBuilder sbErrors = new StringBuilder();

            /* maximální rozsah je 14 Dní! */
            if ((this.INS_dtpDateTo.Value - this.INS_dtpDateFrom.Value).TotalDays > 14)
            {
                sbErrors.AppendLine("Časový rámec může být maximálně 14 dní");
            }

            return sbErrors.ToString();
        }

        /// <summary>
        /// splittedPageNumberTextToExamine text should contains pagenumber and reference number. we need to split it right!
        /// Pagenumber takes members of arrays until it does not match pageNumber
        /// </summary>
        /// <param name="splittedPageNumberTextToExamine"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pReferenceNumber"></param>
        private void SetPageNumberAndReferenceNumber(string[] splittedPageNumberTextToExamine, ref string pageNumber, ref string pReferenceNumber)
        {
            /* splittedPageNumberTextToExamine text should contains pagenumber and reference number. we need to split it right! */
            /* pagenumber is not match and can be splitted! */
            int maximalValidPageNumber = 0;
            pageNumber = splittedPageNumberTextToExamine[0];
            for (int k = 0; k < splittedPageNumberTextToExamine.Length; ++k)
            {
                string pageNumberCandidate = String.Join(" ", splittedPageNumberTextToExamine.Take(k + 1));
                if (INS_regPageNumberHeader.IsMatch(pageNumberCandidate))
                {
                    pageNumber = pageNumberCandidate;
                    maximalValidPageNumber = k;
                }
            }

            /* everything else is reference number! */
            pReferenceNumber = String.Join(" ", splittedPageNumberTextToExamine.Skip(maximalValidPageNumber + 1).ToArray());
        }

        /// <summary>
        /// Funkce, která převede jeden soubor jednoznačně určený atributy @pathFolder a @dokumentName
        /// z formátu .doc na formát .xml
        /// 
        /// 1. Export
        /// 2. Linkování
        /// 3. Smazání prázdných řádků a výcenásobných mezer
        /// 
        /// 4. Nalezení pravého data schválení v textu (datum na konci)
        /// 5. Kontola správnosti spisové značky - na začátku
        /// 6. Případné smazání konvertovaných záhlaví...
		/// 
		/// 7. Pokud existují odkazy, kteří jsou sourozenci a zároveň obsahují stejný odkaz (@href) => sloučit
        /// </summary>
        /// <param name="pPathFolder">Složka, ve které se nachází dokument k převedení</param>
        /// <param name="sDocumentName">Jméno dokumentu k převedení</param>
        /// <param name="pConn">Aktivní připojení k databázi (pro účely doplnění pole cituje</param>
        /// <returns></returns>
        private bool INS_ConvertOneDocToXml(string pPathFolder, string sDocumentName, SqlConnection pConn)
        {
			string sExportErrors = String.Empty;
			string[] parametry = new string[] { "CZ", pPathFolder + "\\" + sDocumentName + ".xml", sDocumentName, "0", "17" };
			try
			{
				ExportWordXml.ExportWithoutProgress export = new ExportWordXml.ExportWithoutProgress(parametry);
				export.RunExport();
			}
            catch (Exception ex)
            {
                WriteIntoLogCritical("Export zcela selhal! " + sExportErrors);
                WriteIntoLogCritical(String.Format("\tException message: [{0}]", ex.Message));
                return false;
            }
            if (!String.IsNullOrEmpty(sExportErrors))
            {
                WriteIntoLogExport(sDocumentName + "\r\n" + sExportErrors + "\r\n*****************************");
            }
            // loading the xml document
			string sPathResultXml = String.Format(@"{0}\{1}.xml", pPathFolder, sDocumentName);
			XmlDocument dOut = new XmlDocument();
			dOut.Load(sPathResultXml);

			// linking
			Linking oLinking = new Linking(pConn, "cs", null);
			oLinking.Run(0, sDocumentName, dOut, 17);
			dOut = oLinking.LinkedDocument;

            // Oříznou se odstavce a zruší zbytečné mezery
            XmlNode xnHtml = dOut.SelectSingleNode("//html-text");
            // ošetří se kdyby export selhal a nic nevyexportoval
            if (String.IsNullOrWhiteSpace(xnHtml.InnerText))
                return false;
            UtilityXml.RemoveMultiSpaces(ref xnHtml);

            UtilityXml.RemoveRedundantEmptyRowsInXmlDocument(ref xnHtml);
            UtilityXml.AddCite(dOut, sDocumentName, pConn);

            /*
             * 4, 5, 6
             * 
             */
            XmlNodeList allTextNodes = xnHtml.SelectNodes(@".//p[normalize-space()]");
            DateTime datSchvaleni = DateTime.Now;
            bool datSchvaleniFound = false;
            /* next page number, that should be in header */
            int expectedNextHeadePageNumber = 2;
            List<XmlNode> headerNodesToDelete = new List<XmlNode>();
            /* if anything wrong (page number and expected one will differ)  happen with header nodes identification, no header nodes shall be deleted!*/
            bool headerNodesIdentifiedCorrectly = true;

            XmlNode xnReferenceNumber = dOut.DocumentElement.FirstChild.SelectSingleNode("./citace");
			string realReferenceNumber = String.Empty;
            string sReferenceNumber = xnReferenceNumber.InnerText;

            for (int i = 0; i < allTextNodes.Count; ++i)
            {
                XmlNode nodeToExamine = allTextNodes[i];
                /* Text to lower is examined... */
                string textToExamine = nodeToExamine.InnerText.ToLower();

                if (INS_PAGE_HEADER_STARTSWITH.Where(s => textToExamine.StartsWith(s)).Count() > 0)
                {
                    int endI = i;
                    string pageNumber = String.Empty;
                    string textReferenceNumber = String.Empty;
                    string higherReferenceNumber = String.Empty;

                    if (INS_PAGE_HEADER_STARTSWITH.Where(s => textToExamine == s).Count() > 0)
                    {
                        int j = 1;
                        /* we are expecting page number, reference number and higher instance reference number (optional) */
                        string pageNumberTextToExamine = (i + j < allTextNodes.Count ? allTextNodes[i + j].InnerText : String.Empty);
                        if (!INS_regPageNumberHeader.IsMatch(pageNumberTextToExamine) && pageNumberTextToExamine.Contains(' '))
                        {
                            /* pagenumber is not match and can be splitted! */
                            string[] splittedPageNumberTextToExamine = pageNumberTextToExamine.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                            SetPageNumberAndReferenceNumber(splittedPageNumberTextToExamine, ref pageNumber, ref textReferenceNumber);
                            j = 2;
                        }
                        else
                        {
                            /* pagenumber is ok */
                            pageNumber = pageNumberTextToExamine.Trim();
                            j = 2;
                            textReferenceNumber = (i + j < allTextNodes.Count ? allTextNodes[i + j].InnerText : String.Empty);
                            j = 3;
                        }

                        higherReferenceNumber = (i+j < allTextNodes.Count ? allTextNodes[i+j].InnerText : String.Empty);

                        /* + optional */
                        endI = i + j;
                    }
                    else
                    {
                        /* all in one node!, skip "pokračování" */
                        string[] splittedTextToExamine = textToExamine.Split(new char[] {' '}, StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
                        SetPageNumberAndReferenceNumber(splittedTextToExamine, ref pageNumber, ref textReferenceNumber);
                    }

                    /* Checks! */

                    /* page number */
                    Match matchPageNumber = INS_regPageNumberHeader.Match(pageNumber);
                    headerNodesIdentifiedCorrectly &= matchPageNumber.Success;
                    if (matchPageNumber.Success)
                    {
                        headerNodesIdentifiedCorrectly &= (Int32.Parse(matchPageNumber.Groups[1].Value) == expectedNextHeadePageNumber++);
                    }

                    /* Check reference number! */
                    /* Remove text of reference number */
                    textReferenceNumber = INS_regReferenceNumberText.Replace(textReferenceNumber, String.Empty);

                    /* Musí to matchovat alespoň na jeden způsob!*/
                    bool isMatch = INS_regfullInsReferenceNumberWithoutUnitSuffix.IsMatch(textReferenceNumber) || INS_regFullInsHigherInstance.IsMatch(textReferenceNumber) || INS_regUnitSuffix.IsMatch(textReferenceNumber);
                    headerNodesIdentifiedCorrectly &= isMatch;
                    if (isMatch)
                    {
                        string referenceNumberToLowerWOWhitespace = sReferenceNumber.RemoveWhitespaceAndLower();
                        string textReferenceNumberToLowerWOWhitespace = textReferenceNumber.RemoveWhitespaceAndLower();
                        /* If the reference number in text is a part of reference number crawled, it SHOULD be ok */
                        /* +Obráceně, pokud bych chtěl pokrýt i /číslo stránky. Na druhou stranu bych omylem mohl smazat větší porci textu a to za to asi nestojí...*/
                        if (!referenceNumberToLowerWOWhitespace.Contains(textReferenceNumberToLowerWOWhitespace))
                        {
                            WriteIntoLogCritical("[{0}]: Vygenerovaná spisová značka [{1}] neodpovídá spisové značce v textu! [{2}]", sDocumentName, sReferenceNumber, textReferenceNumber);
                            /* to be sure, rather keep header nodes*/
                            headerNodesIdentifiedCorrectly = false;
                        }
                    }

                    /* Higher reference number (optional) */
                    if (INS_regFullInsHigherInstance.IsMatch(higherReferenceNumber.RemoveWhitespaceAndLower()))
                    {
                        /* Skutečné odvolání, musím upravit spisovou značku! */
                        realReferenceNumber = String.Format("{0} {1}", sReferenceNumber, higherReferenceNumber);
                    } else if (!INS_regfullInsReferenceNumberWithoutUnitSuffix.IsMatch(higherReferenceNumber.RemoveWhitespaceAndLower()) && endI > i)
                    {
                        /* do not remove last node */
                        --endI;
                    }

                    /* add nodes to remove, modify i (skip processed nodes!) */
                    for (; i <= endI && i < allTextNodes.Count; ++i)
                    {
                        headerNodesToDelete.Add(allTextNodes[i]);
                    }
                } /* If it is not a special node, it may be the date */
                else if (i + INS_REAL_DATE_NODES_TO_EXAMINE >= allTextNodes.Count)
                {
                    /* try to set datSchvaleni */
                    datSchvaleniFound |= CzechDatetime.DeclinedCzechDateToDateTime(textToExamine, ref datSchvaleni);
                }
            }

			string datSchvaleniNodeContent = datSchvaleniFound ? datSchvaleni.ToString(Utility.DATE_FORMAT) : String.Empty;
            /* Write datSchvaleni into document!*/
            XmlNode xnDatSchvaleni = dOut.SelectSingleNode("//datschvaleni");
            xnDatSchvaleni.InnerText = datSchvaleniNodeContent;

            if (!datSchvaleniFound)
            {
                WriteIntoLogCritical("V dokumentu [{0}] nebylo nalezeno datum schválení!", sDocumentName);
            }

            /* Upravím spisovou značku */
            if (!String.IsNullOrEmpty(realReferenceNumber))
            {
                xnReferenceNumber.InnerText = realReferenceNumber;
            }

            /* if header found withou errors, delete header nodes */
            if (headerNodesIdentifiedCorrectly)
            {
                for (int i = headerNodesToDelete.Count - 1; i >= 0; --i)
                {
                    headerNodesToDelete[i].ParentNode.RemoveChild(headerNodesToDelete[i]);
                }
            }

			/* 7. 
			 * a) Get all link[@href] nodes that has a link[@href] following-sibling
			 */
			string siblingsWithHref = @"//link[@href]/preceding-sibling::*[1][self::link][@href]";
			XmlNodeList nodesWithSiblings = dOut.DocumentElement.SelectNodes(siblingsWithHref);
			List<XmlNode> duplicityLinksNodes = new List<XmlNode>();
			foreach (XmlNode firstNode in nodesWithSiblings)
			{
				/* If node & sibling contains href with same value
				 * <link href='XXX'>YYY</link>
				 * <link href='XXX'>ZZZ</link>
				 * then join links
				 * - content of the first link will be YYYZZZ
				 * - add second link to the list of duplicityLinksNodes
				 */
				if (String.Equals(firstNode.Attributes["href"].Value, firstNode.NextSibling.Attributes["href"].Value, StringComparison.OrdinalIgnoreCase))
				{
					string oldFirstNode = firstNode.OuterXml;
					firstNode.InnerText = String.Format("{0}{1}", firstNode.InnerText, firstNode.NextSibling.InnerText);
					firstNode.Attributes.RemoveNamedItem("error");
					
					WriteIntoLogDuplicity("Dokument obsahoval duplicitní odkazy{0}{1}{0}{2}{0}které byly sloučeny jako{0}{3}", Environment.NewLine, oldFirstNode, firstNode.NextSibling.OuterXml, firstNode.OuterXml);
					duplicityLinksNodes.Add(firstNode.NextSibling);
				}
			}
			/* b. delete all duplicity nodes */
			for (int i = duplicityLinksNodes.Count - 1; i >= 0; --i)
			{
				duplicityLinksNodes[i].ParentNode.RemoveChild(duplicityLinksNodes[i]);
			}

            // uložení
            dOut.Save(sPathResultXml);
            return true;
        }

        private void INS_btnWordToXml_Click(object sender, EventArgs e)
        {
            if (!INS_DocumentsWereDownloaded &&
                 MessageBox.Show(this, "Nedošlo ke stažení dokumentů z webu, přejete si přesto převést obsah pracovní složky?", "NSS", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == System.Windows.Forms.DialogResult.No)
            {
                return;
            }
            this.INS_btnWordToXml.Enabled = false;
            // jedeme od nuly...
            this.processedBar.Value = 0;

            Task t = Task.Factory.StartNew(() =>
            {
                UpdateConversionProgressDelegate UpdateProgress = new UpdateConversionProgressDelegate(INS_UpdateProgressSafe);
                ConvertDelegate Convert = new ConvertDelegate(INS_ConvertOneDocToXml);
                TransformDocInWorkingFolderToXml(UpdateProgress, Convert);
            });

            while (t.Status != TaskStatus.RanToCompletion)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(1000);
            }
            this.INS_btnWordToXml.Enabled = true;
        }
    }
}
