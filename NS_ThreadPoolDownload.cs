using BeckLinking;
using System;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using UtilityBeck;

namespace DataMiningCourts
{
    class NS_ThreadPoolDownload : ALL_ThreadPoolDownload
    {
        private static CiselnikDB CiselnikDRUH = new CiselnikDB("Kind", 9, System.Configuration.ConfigurationManager.ConnectionStrings["LexData"].ConnectionString);
        private static CiselnikDB CiselnikAUTOR = new CiselnikDB("Author", 9, System.Configuration.ConfigurationManager.ConnectionStrings["LexData"].ConnectionString);

        private DateTime datePublishing;
        /// <summary>
        /// Spisová značka ve výsledcích vyhledávání
        /// </summary>
        string spisovaZnackaVeVysledcichVyhledavani;

        public NS_ThreadPoolDownload(FrmCourts frm, ManualResetEvent doneEvent, string sDirectoryPathToWriteFileTo, DateTime datVydani, string spzn)
            : base(frm, doneEvent, sDirectoryPathToWriteFileTo)
        {
            this.datePublishing = datVydani;
            this.spisovaZnackaVeVysledcichVyhledavani = spzn.Trim();
        }

        private static int MAX_PORADOVE_CISLO_SPZN = 10;

        /// <summary>
        /// Hledá spisovou značku v textu,
        /// pokud ji najde, tak ji nejen vrátí, ale i upraví do tvaru
        /// Pokud je v textu stejná, jako v hlavičce, vloží spisovou značku z prvního kroku vyhledávání (seznam výsledků)
        /// jinak vloží spisovou značku z textu
        /// </summary>
        /// <param name="pHtmlTextDokumentu"></param>
        /// <param name="pHeaderSpZn"></param>
        /// <returns></returns>
        private XmlNode SpisovaZnackaVhtmlDokumentu(XmlNode pHtmlTextDokumentu, XmlNode pHeaderSpZn)
        {
            if (pHtmlTextDokumentu != null && pHeaderSpZn != null)
            {

                string headerSpZn = Utility.GetReferenceNumberNorm(pHeaderSpZn.InnerText, out string sNormValue2);

                /* Pokud jsou oba odkazy validni, tak:
                 * v Htmltextu postupne prochazim pcka (potomky), nez najdu ten, jehoz obsah se kryje s obsahem spzn
                 * -- zacina stejne, jako spzn.
                 * navic pocitam, kolikaty neprazdny uzel to je.
                 */
                for (int aktualniMoznePoradoveCisloSpZn = 0; aktualniMoznePoradoveCisloSpZn < Math.Min(MAX_PORADOVE_CISLO_SPZN, pHtmlTextDokumentu.ChildNodes.Count); ++aktualniMoznePoradoveCisloSpZn)
                {
                    XmlNode p = pHtmlTextDokumentu.ChildNodes[aktualniMoznePoradoveCisloSpZn];
                    //XmlNodeList textNodes = p.SelectNodes(".//p");
                    //foreach (XmlNode textNode in textNodes)
                    //{
                        string sText = p.InnerText;
                        sText = Utility.GetReferenceNumberNorm(sText, out string sNormValue3);

                        /* Může se stát, že spisová značka v textu je delší než spisová značka dosud používaná.
                         * V tom případě nahradím spisovou značku značku nalezenou v textu
                         * 
                         * Také se může stát, že spisová značka v textu je kratší než spisová značka dosud používaná.
                         * V tom případě zachovám spisovou značku
                         * 
                         * V každém případě končím
                         */

						if (!String.IsNullOrEmpty(sText))
							if (sText.StartsWith(headerSpZn) || headerSpZn.StartsWith(sText))
							{
								/* nalezl */
								return p;
							}
                    //}
                }
            }

            /* nenalezl */
            return null;

        }

        /// <summary>
        /// 29 Od 1/2014-2
        /// 1: 29
        /// 2: Od
        /// 3: 1/2014-2
        /// </summary>
        //private static Regex NS_regReferenceNumber = new Regex(@"\s*(\d+)\s*([a-zA-Z]+)\s*(\d+/\d{4})(-\s*\d+)?\s*");
        private static Regex NS_regReferenceNumber = new Regex(@"(.+)(\s*\-\s*\d+)?$");

        public void DownloadDocument(object o)
        {
            // filename => za posledním /, bez posledního "?openDocument"

            // the URL to download the file from
            string sUrlToReadFileFrom = o.ToString();

            int iPosition = sUrlToReadFileFrom.LastIndexOf('/');
            int iPositionQuestionMark = sUrlToReadFileFrom.IndexOf('?', iPosition);
            string sExternalId = sUrlToReadFileFrom.Substring(iPosition + 1, iPositionQuestionMark - iPosition - 1);
            int documentYear = -1;
            try
            {
                // first, we need to get the exact size (in bytes) of the file we are downloading
                Uri url = new Uri(sUrlToReadFileFrom);
                System.Net.HttpWebRequest request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                string sSource;
                using (StreamReader reader = new StreamReader(request.GetResponse().GetResponseStream(), true))
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

                XmlNode xnDivContent = xmlDoc.SelectSingleNode("//div[@class='main_detail'][last()]");
                if (xnDivContent == null)
                {
                    throw new NS_Exception("Stažený dokument: Nelze nalezt div obsahu na strance dokumentu");
                }

                XmlNode xnTableHeader = xnDivContent.FirstChild;
				if (!xnTableHeader.Name.Equals("table"))
					xnTableHeader = xnDivContent.SelectSingleNode(".//table");
                if (xnTableHeader == null)
                {
                    throw new NS_Exception("Stažený dokument: Nelze nalezt tabulku hlavicky na strance dokumentu");
                }

                //string sTextToBrowser = "<html><head></head>" + xnDivContent.OuterXml + "</html>";

                xnDivContent.RemoveChild(xnTableHeader);
                //DivObsahu.RemoveChild(DivObsahu.LastChild);

                //remove citation
                var citationNode = xnDivContent.FirstChild;
                if (citationNode.Attributes != null
                    && citationNode.Attributes["style"] != null
                    && citationNode.Attributes["style"].Value.ToLower() == "font-weight: normal; line-height: 1")
                {
                    xnDivContent = xnDivContent.SelectSingleNode("//span[@style='font-weight: normal; line-height: 1']/span");
                }

                // remove empty nodes
                while ((xnDivContent.FirstChild != null) && xnDivContent.FirstChild.Name.Equals("br"))
                {
                    var fChildNode = xnDivContent.FirstChild;
                    fChildNode.ParentNode.RemoveChild(fChildNode);
                }

                NS_WebHeader nwHeader = this.LoadHeader(xnTableHeader, sUrlToReadFileFrom, sExternalId);
                documentYear = nwHeader.HDate.Year;

                if (nwHeader.Druh == null)
                {
                    string druh = NS_WebDokumentJUD.NajdiRozsudekRozhodnuti(xnDivContent.InnerText);

                    if (druh == null)
                    {
                        throw new NS_Exception("Stažený dokument: Nepodarilo se najit DRUH dokumentu");
                    }
                    else
                    {
                        nwHeader.Druh = druh;
                    }
                }

                /* Pokud je spisová značka ve výsledcích vyhledávání delší než ta, která je v tabulce, tak ji zaměním */
                if (this.spisovaZnackaVeVysledcichVyhledavani.StartsWith(nwHeader.SpisovaZnacka))
                {
                    nwHeader.SpisovaZnacka = this.spisovaZnackaVeVysledcichVyhledavani;
                }

                //postprocess spis. zn.
                iPosition = nwHeader.SpisovaZnacka.LastIndexOf('-');
                if (iPosition > -1)
                    nwHeader.SpisovaZnacka = nwHeader.SpisovaZnacka.Substring(0, iPosition).TrimEnd() + "-" + nwHeader.SpisovaZnacka.Substring(iPosition + 1).TrimStart();
                if (!nwHeader.SpisovaZnacka.EndsWith(".") && (nwHeader.SpisovaZnacka.EndsWith("I") || nwHeader.SpisovaZnacka.EndsWith("V") || nwHeader.SpisovaZnacka.EndsWith("X")))
                    nwHeader.SpisovaZnacka += ".";
                if (nwHeader.SpisovaZnacka.Contains("NSCR"))
                    nwHeader.SpisovaZnacka = nwHeader.SpisovaZnacka.Replace("NSCR", "NSČR");

				string currentDocumentName;
				var dokJUD = new NS_WebDokumentJUD();
                dokJUD.PathOutputFolder = directoryPathToWriteFileTo;
				using (SqlConnection conn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["LexData"].ConnectionString))
				{
					conn.Open();
					dokJUD.ZalozDokument(nwHeader, conn);

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

					XmlNode xn;
					XmlDocument dOut = new XmlDocument();
					dOut.Load(dokJUD.PathXml);
					dOut.DocumentElement.InnerXml = dOut.DocumentElement.InnerXml.Replace("&amp;nbsp;", " ");
					XmlNode xnDocumentText = dOut.SelectSingleNode("//html-text");
					XmlNode xnSpisovaZnackaHeader = dOut.DocumentElement.FirstChild.SelectSingleNode("./citace");
					//XmlNode xnSpisovaZnackaVDokumentu = SpisovaZnackaVhtmlDokumentu(xnDocumentText, xnSpisovaZnackaHeader);
					currentDocumentName = dokJUD.DocumentName;
					// Finální spisová značka je buď spisová značka, která byla nalezená na začátku nebo spisová značka z dokumentu
					// V obou případech po transformaci formátu
					string spisovaZnackaFinalni = this.spisovaZnackaVeVysledcichVyhledavani;
					//if (xnSpisovaZnackaVDokumentu != null)
					//{
					//	// Pokud je značka v dokumentu delší, tak jin nastavím hjako finální
					//	if (xnSpisovaZnackaVDokumentu.InnerText.StartsWith(spisovaZnackaFinalni))
					//	{
					//		spisovaZnackaFinalni = xnSpisovaZnackaVDokumentu.InnerText;
					//		nwHeader.SpisovaZnacka = xnSpisovaZnackaVDokumentu.InnerText;
					//		xn = dOut.DocumentElement.FirstChild.SelectSingleNode("./citace");
					//		xn.InnerText = xnSpisovaZnackaVDokumentu.InnerText;
					//		Utility.CreateDocumentName("J", xnSpisovaZnackaHeader.InnerText, null, out currentDocumentName);
					//	}
					//}

					iPosition = dokJUD.PathXml.LastIndexOf('\\');
					// dokumentName to sice je, ale i s W_ prefixem...
					string sDocumentName = dokJUD.PathWordXml.Substring(iPosition + 1).Replace(".xml", "");

					//string[] iniciaceRozsudku = new string[] { "ČESKÁ REPUBLIKA", "ROZSUDEK", "JMÉNEM REPUBLIKY" };
					// Pokud je spisová značka jako první uzel, tak ji smažu & všechny následující prázdné řádky & vsechny predchozi radky
					//if (xnSpisovaZnackaVDokumentu == null)
					//{
						/* Spisova znacka v textu nenalezena - to muze byt velky problem
						 * 1. zapsat chybu do logu
						 * 2. zapsat element error do prvniho odstavce html textu
						 */
						//this.parentWindowForm.WriteIntoLogCritical(String.Format("{0}: V textu usneseni nebyla lokalizovana spisova znacka!", nwHeader.DocumentName));

						//XmlElement xeFirstParagraph = xnDocumentText.FirstChild as XmlElement;
						//xeFirstParagraph.SetAttribute("error", "V textu usneseni nebyla lokalizovana spisova znacka!");
					//}

					// smažu celý začátek až po text "Nejvyšší soud rozhodl"
					int i = 0;
					string s;
					Regex rgNumber = new Regex(@"\d+");
					xn = xnDocumentText.FirstChild;
					while((xn != null) && (++i<10))
					{
						s = xn.InnerText.ToLower();
						Utility.RemoveWhiteSpaces(ref s);
						if (String.IsNullOrWhiteSpace(s) || (rgNumber.IsMatch(s) && s.Length<22) || s.Equals("usnesení") || s.Equals("českárepublika")
							 || s.Equals("rozsudek") || s.Equals("jménemrepubliky"))
						{
							xn = xn.NextSibling;
							xnDocumentText.RemoveChild(xnDocumentText.FirstChild);
							continue;
						}
						if (!s.StartsWith("nejvyššísoud"))
							this.parentWindowForm.WriteIntoLogCritical(String.Format("{0}: Vadně identifikovaný začátek textu rozhodnutí!", nwHeader.DocumentName));
						break;
					}

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
					FrmCourts.AddLawArea(conn, dOut.DocumentElement.FirstChild);

                    UtilityXml.RemoveRedundantEmptyRowsInXmlDocument(ref xnDocumentText);
                    dOut.Save(dokJUD.PathXml);
                    conn.Close();
                }

                // výsledek
                string outputDirectoryFullName = String.Format(@"{0}\{1}", this.parentWindowForm.XML_DIRECTORY, currentDocumentName);
                if (Directory.Exists(outputDirectoryFullName))
                {
                    this.parentWindowForm.WriteIntoLogCritical("Složka pro dokumentName [{0}] již existuje. Může se jednat o problém s duplicitními spisovými značkami. Po uložení aktuálně stažených dokumentů do db stáhněte dokumenty za období znovu...", outputDirectoryFullName);
                    citation.RevertCitationForAYear(documentYear);
                }
                else
                {
					Directory.Move(dokJUD.PathFolder, outputDirectoryFullName);
					if (currentDocumentName != dokJUD.DocumentName)
					{
						File.Move(Path.Combine(outputDirectoryFullName, dokJUD.DocumentName + ".xml"), Path.Combine(outputDirectoryFullName, currentDocumentName + ".xml"));
						string sFile1 = Path.Combine(outputDirectoryFullName, "W_" + dokJUD.DocumentName + ".xml");
						if (File.Exists(sFile1))
							File.Move(sFile1, Path.Combine(outputDirectoryFullName, "W_" + currentDocumentName + ".xml"));
					}

                    // Everything is OK, so i can coomit citation number!
                    citation.CommitCitationForAYear(nwHeader.HDate.Year);
                }
            }
            catch (NS_Exception nsex)
            {
                if (documentYear != -1)
                {
                    citation.RevertCitationForAYear(documentYear);
                }
                this.parentWindowForm.WriteIntoLogCritical(nsex.Message);
            }
            catch (DuplicityException dex)
            {
                /* Do not revert because function did not generate a citation! */
                this.parentWindowForm.WriteIntoLogDuplicity(dex.Message);
            }

            doneEvent.Set();
        }

        // ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        private NS_WebHeader LoadHeader(XmlNode pXnTable, string pUrl, string pExternalId)
        {
            NS_WebHeader webhlav = new NS_WebHeader();

            webhlav.URL = pUrl;

            bool NaselDatum = false;
            bool NaselZnacku = false;

            /* Datum vydani přeberu z "rodičovské" třídy */
            webhlav.PublishingDate = this.datePublishing;

            foreach (XmlNode tr in pXnTable.ChildNodes)
            {
                try
                {
                    XmlNode levaTD = tr.FirstChild;
                    XmlNode pravaTD = levaTD.NextSibling;
                    if (levaTD == null || pravaTD == null) continue;

                    var levaINNER = levaTD.InnerText.Trim();
                    var pravaINNER = pravaTD.InnerText.Trim();

                    switch (levaINNER)
                    {
                        case "Dotčené předpisy:":
                            using (SqlConnection conn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["LexData"].ConnectionString))
                            {
                                conn.Open();
                                pravaTD.InnerXml = pravaTD.InnerXml.Replace("<br />", "\n").Replace("<br/>", "\n");
                                webhlav.VztazenePredpisy = ParseRelation.ProcessText(pravaTD.InnerText, conn);
                                conn.Close();
                            }
                            break;

                        case "Hesla:":
                        case "Heslo:":
                            NaplnSeznamStringuOddelenychBR(webhlav.Registers2, pravaTD);
                            break;
                        case "ECLI:":
                            webhlav.ECLI = pravaINNER;
                            break;

                        case "Soud:":
                            webhlav.Author = pravaINNER;
                            break;
                        case "Spisová značka:":
                        case "Senátní značka:":
                            if (NaselZnacku)
                            {
                                this.parentWindowForm.WriteIntoLogCritical(String.Format("Parsování hlavičky: V dokumentu se nacházelo více spisových značek. Překontrolujte doplněnou spisovou značku [{0}]!", webhlav.SpisovaZnacka));
                                break;
                            }
                            webhlav.SpisovaZnacka = pravaINNER;
                            NaselZnacku = true;

                            break;
                        case "Typ rozhodnutí:":
                            webhlav.Druh = pravaINNER.Replace("USNESENÍ", "Usnesení").Replace("ROZSUDEK", "Rozsudek");

                            break;
                        case "Kategorie rozhodnutí:":
                            webhlav.Kategorie = pravaINNER;
                            break;
                        case "Datum rozhodnutí:":

                            if (NaselDatum)
                                break;

                            DateTime dat3000 = new DateTime(3000, 1, 1);
                            webhlav.HDate = dat3000;

                            string[] formaty = new string[] { "d", "dd.MM.yyyy", "d.M.yyyy", "MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd" };
                            foreach (string formatek in formaty)
                                try
                                {
                                    webhlav.HDate = DateTime.ParseExact(pravaINNER, formatek, CultureInfo.InvariantCulture);
                                    //if (pravaINNER.Contains("/"))
                                    //	webhlav.HDate = DateTime.ParseExact(pravaINNER, "MM/dd/yyyy", CultureInfo.InvariantCulture);
                                    //else
                                    //	webhlav.HDate = DateTime.ParseExact(pravaINNER, "dd.MM.yyyy", CultureInfo.InvariantCulture);
                                }
                                catch { };

                            if (webhlav.HDate != dat3000)
                                NaselDatum = true;

                            break;
                    }
                }
                catch { };

            }

            if (!NaselDatum)
            {
                throw new NS_Exception("Nelze precist datum z HTML");
            }

            if (webhlav.SpisovaZnacka == null)
            {
                throw new NS_Exception("Nelze precist spisovou znacku z HTML");
            }
            if (webhlav.Author == null)
            {
                throw new NS_Exception("Nelze precist autora z HTML");
            }


            if (webhlav.Author.ToLower() == "nejvyšší soud čr")
                webhlav.Author = "Nejvyšší soud";
            else if (webhlav.Author.ToLower().Equals("zvláštní senát"))
                webhlav.Author = "Zvláštní senát zřízený podle zákona č. 131/2002 Sb.";


            //if (!CiselnikAUTOR.HasValueIgnoreCase(webhlav.Author))
            //{
            //    throw new NS_Exception("Autor neni v ciselniku : " + webhlav.Author);
            //}

            if (citation.ReferenceNumberIsAlreadyinDb(webhlav.HDate, webhlav.SpisovaZnacka))
            {
                throw new DuplicityException(String.Format("Znacka [{0}] s daným datem rozhodnutí [{1}] je v jiz databazi!", webhlav.SpisovaZnacka, webhlav.HDate));
            }

            /* Rok citace z data rozhodnutí! */
            webhlav.YearCitation = webhlav.HDate.Year;
            webhlav.NumberCitation = citation.GetNextCitation(webhlav.HDate.Year);
            webhlav.Citation = String.Format("NS {0}/{1}", webhlav.NumberCitation, webhlav.YearCitation);
            webhlav.IdExternal = pExternalId;

            return webhlav;
        }
    }
}
