using BeckLinking;
using HtmlAgilityPack;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using UtilityBeck;

namespace DataMiningCourts
{
	partial class FrmCourts
	{
		/// <summary>
		/// Základní webové stránky - vyhledávací formulář
		/// </summary>
		private static string NSS_INDEX =
		@"http://www.nssoud.cz/main0Col.aspx?cls=JudikaturaBasicSearch&menu=262";
		/// <summary>
		/// Webové stránky pro jednotlivý dokument rozhodnutí. Každý má "svoje", určují se pomocí id dokumentu
		/// (které lze získat z vyhledávacího formuláře).
		/// </summary>
		private static string NSS_DETAIL_ROZHODNUTI =
		@"http://www.nssoud.cz/mainc.aspx?cls=InfoSoud&kau_id=";

		private static string NSS_VETA_ROZHODNUTI =
		@"http://www.nssoud.cz/mainc.aspx?cls=EvidencniListVety&evl_id=";

		private static string NSS_TEXT_OF_PDF_TO_DOWNLOAD = "anonymizovanáverzerozhodnutí";


		private static int NSS_DETAIL_SPISOVA_ZNACKA_NODE = 3;
		private static int NSS_DETAIL_DRUH_NODE = 5;
		private static int NSS_DETAIL_AUTHOR_NODE = 7;
		private static int NSS_DETAIL_DATUM_SCHVALENI_NODE = 9;
		private static int NSS_DETAIL_PREJUDIKATURA = 15;

		// Výsledky vyhledávání již neobsahují populární název!
		//private static int NSS_DETAIL_POPULARNI_NAZEV_NODE = 19;

		/// <summary>
		/// Seznam elementů HtmlNode z vyhledávacího formuláře
		/// Elementy jsou parsovány až v následujícím kroku (NSS_SpustVlaknoProZpracovaniElementu)
		/// </summary>
		BlockingCollection<HtmlAgilityPack.HtmlNode> NSS_seznamElementuKeZpracovani;

		/// <summary>
		/// Seznam html dokumentů, do kterých už byly zařazeny některé prvky z  HtmlNode.
		/// Je to výstup NSS_SpustVlaknoProZpracovaniElementu, zároveň je vstupem do NSS_SpustVlaknoProZpracovaniMezivysledku
		/// </summary>
		BlockingCollection<XmlDocument> NSS_seznamCastecnychVysledku;

		/// <summary>
		/// Třída reprezentující odkaz na pdf ke stažení a místo na disku, kam má být staženo
		/// </summary>
		private class PdfToDownload
		{
			public string url, pdf;

			public PdfToDownload(string url, string pdf)
			{
				this.url = url;
				this.pdf = pdf;
			}
		}

		/// <summary>
		/// Seznam pdf dokumentů ke stažení...
		/// </summary>
		BlockingCollection<PdfToDownload> NSS_seznamPdfKeStazeni;

		/// <summary>
		/// Seznam PDF k převedení do DOC
		/// Nepoužívá se, používá se externí aplikace. Nicméně ta se možná bude spouštět odsud, tedy
		/// je dobré zachovat infrastrukturu... progress se updatuje z vlákna pro stahování...
		/// </summary>
		//BlockingCollection<string> NSS_seznamPdfKPrevedeni;

		private enum NSSAkce { nssaVyhledaniDat, nssaPrvniUlozeniOdkazu, nssaUlozeniOdkazu };
		/// <summary>
		/// Příznak, zda-li jsem už vyhledal data & načítám je, nebo ne...
		/// </summary>
		private NSSAkce aktuaniNSSAkce;

		/// <summary>
		/// Na jaké aktuální stránce v zobrazování požadovaných výsledků se nacházíme
		/// </summary>
		private int NSS_aktualniStrankaKeZpracovani;
		/// <summary>
		/// Kolik stránek s výsledky je celkem
		/// </summary>
		private int NSS_celkemStranekKeZpracovani;
		/// <summary>
		/// Kolik záznamů (výsledků) vyhledávání je celkem
		/// </summary>
		private int NSS_XML_celkemZaznamuKeZpracovani;
		/// <summary>
		/// Jaký záznam aktuálně zpracováváme (kvůli progress baru)
		/// </summary>
		private int NSS_PDF_aktualniZaznamKeZpracovani;

		/// <summary>
		/// Kolik dokumentů pdf je celkem ke stažení...
		/// </summary>
		private int NSS_PDF_celkemZaznamuKeZpracovani;

		/// <summary>
		/// Jaký záznam (XML) aktuálně zpracováváme (kvůli progress baru)
		/// </summary>
		private int NSS_XML_aktualniZaznamKeZpracovani;

		/// <summary>
		/// Rok, který se váže k poslednímu dokumentu, jemuž bylo přiděleno číslo!
		/// Kdybych překročil rok, tak musím "vyjedničkovat" přidělené číslo...
		/// </summary>
		private string NSS_rokPoslednihoZpracovanehoDokumentu = String.Empty;

		/// <summary>
		/// Regulární výraz kterým získáme pro konkrétní vyhledaný dokument jeho id
		/// </summary>
		Regex NSS_regOdkazNaDetail = new Regex(@";kau_id=(\d+)");

		/// <summary>
		/// Příznak, který určuje, zda-li načítat pouze dokumenty, k nimž existuje anonymizovaná verze rozhodnutí...
		/// </summary>
		private bool NSS_onlyWithPDF;

		/// <summary>
		/// Příznak, který indikuje, zda-li došlo ke stažení všech dokumentů. (po stisknutí tlačítka načíst)
		/// </summary>
		bool NSS_DocumentsWereDownloaded = false;

		/// <summary>
		/// Checking duplicity of the documents and generating unique id
		/// </summary>
		CitationService NSS_citationService;


		/// <summary>
		/// Funkce, která spustí vlákno, které vybírá ze seznamu NSS_seznamElementuKeZpracovani
		/// Data takto vybraná zpracuje:
		/// Naparsuje některé elementy a vloží je do nově vytvořeného XmlDokumentu
		/// Data získaná vloží do seznamu NSS_seznamCastecnychVysledku.
		/// Převod je 1:1
		/// </summary>
		private void NSS_SpustVlaknoProZpracovaniElementu()
		{
			Task.Factory.StartNew(() =>
			{
				int iPosition1, iPosition2;
				HtmlAgilityPack.HtmlNode data = null;
				HtmlAgilityPack.HtmlNode hVeta;
				using (SqlConnection conn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["LexData"].ConnectionString))
				{
#if !DUMMY_DB
					conn.Open();
#endif

					while (!NSS_seznamElementuKeZpracovani.IsCompleted)
					{
						// dokud je co brát... (aktivní blokování)
						while (!NSS_seznamElementuKeZpracovani.IsCompleted && !NSS_seznamElementuKeZpracovani.TryTake(out data, 2000)) ;

						if (data != null)
						{
							HtmlAgilityPack.HtmlNode nodeSpZn = data.ChildNodes[NSS_DETAIL_SPISOVA_ZNACKA_NODE];
							bool vytvoritNovyDokument = !String.IsNullOrWhiteSpace(nodeSpZn.InnerText);
							string spzn = String.Empty;
							string datSchvaleniNonUni = String.Empty;
							string sIdVeta = null, sStyle;

							if (vytvoritNovyDokument)
							{
								hVeta = data.SelectSingleNode("./td/img");
								sStyle = hVeta.Attributes["style"].Value.ToLower();
								UtilityBeck.Utility.RemoveWhiteSpaces(ref sStyle);
								if (!sStyle.Equals("display:none"))
								{
									iPosition1 = hVeta.OuterHtml.IndexOf("evl_id=");
									iPosition2 = hVeta.OuterHtml.IndexOf("'", iPosition1 + 7);
									sIdVeta = hVeta.OuterHtml.Substring(iPosition1 + 7, iPosition2 - iPosition1 - 7);
								}

								XmlDocument newXmlDocument = new XmlDocument();
#if LOCAL_TEMPLATES
                                newXmlDocument.Load(@"Templates-J-Downloading\Template_J_NSS.xml");
#else
								string sPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
								newXmlDocument.Load(Path.Combine(sPath, @"Templates-J-Downloading\Template_J_NSS.xml"));
#endif
								// sloupce v "tabulce" jsou následující:
								//CHECK BOX, Spis.značka Forma/Způsob rozhodnutí Soud Datum rozhodnutí Publikováno ve Sb. NSS Účastníci řízení Opravný prostředek a výsledek řízení o něm Prejudikatura Populární název
								//"window.open('/mainc.aspx?cls=InfoSoud&amp;kau_id=110583')"
								//data.ChildNodes[3].SelectSingleNode(".//img[@title='Informace o řízení']").Attributes["onclick"].Value
								HtmlAgilityPack.HtmlNode nodeOdkazDetail = nodeSpZn.SelectSingleNode(".//img[@title='Informace o řízení']");
								Match NSS_matchOdkazNaDetail = NSS_regOdkazNaDetail.Match(nodeOdkazDetail.Attributes["onclick"].Value);

								XmlNode xn = newXmlDocument.SelectSingleNode("//id-external");
								xn.InnerText = NSS_matchOdkazNaDetail.Groups[1].Value;

								/* Check for duplicities */
								spzn = nodeSpZn.InnerText.Trim();
								datSchvaleniNonUni = data.ChildNodes[NSS_DETAIL_DATUM_SCHVALENI_NODE].InnerText.Trim();
								DateTime datSchvaleniDateTime = DateTime.Parse(UtilityBeck.Utility.ConvertDateIntoUniversalFormat(datSchvaleniNonUni));
								// pokud již existuje, tak nic
								//if (NSS_citationService.IsAlreadyinDb(datSchvaleniDateTime, spzn, xn.InnerText))
								// nebudeme kontrolovat podle jejich ID - nespolehlivé
								if (NSS_citationService.IsAlreadyinDb(datSchvaleniDateTime, spzn, null))
								{
									WriteIntoLogDuplicity(String.Format("Značka [{0}] s daným datem rozhodnutí [{1}] je v jiz databazi!", spzn, datSchvaleniDateTime.ToShortDateString()));
									// z tohohle mraku nezaprší... (není přidělena spisová značka)
									++NSS_PDF_aktualniZaznamKeZpracovani;
									++NSS_XML_aktualniZaznamKeZpracovani;
									continue;
								}

								// uložíme odkaz na pdf
								HtmlAgilityPack.HtmlNodeCollection nodePdf = data.ChildNodes[NSS_DETAIL_SPISOVA_ZNACKA_NODE].SelectNodes("./a[@href]");
								if (nodePdf != null)
								{
									string s;
									foreach (HtmlAgilityPack.HtmlNode hn in nodePdf)
									{
										/* Hledám v odkazu text Anonymizovaná verze rozhodnutí a navíc v odkazu musí být soubor s koncovkou pdf */
										if (hn.Attributes["title"] != null && hn.Attributes["href"] != null &&
															hn.Attributes["href"].Value.EndsWith(".pdf"))
										{
											s = hn.Attributes["title"].Value.ToLower();
											Utility.RemoveWhiteSpaces(ref s);
											if (s.Equals(NSS_TEXT_OF_PDF_TO_DOWNLOAD))
											{
												XmlAttribute a = newXmlDocument.CreateAttribute("HrefPdf");
												a.Value = hn.Attributes["href"].Value;
												newXmlDocument.DocumentElement.Attributes.Append(a);
												break;
											}
										}
									}
								}

								// spisova značka = element citace v hlavní hlavičce
								// odstranění mezer okolo pomlčky
								spzn = spzn.Replace("–", "-");
								Regex rg = new Regex(@"\s*\-\s*\d+$");
								MatchEvaluator evaluator = new MatchEvaluator(TermSpaces);
								if (rg.IsMatch(spzn))
									spzn = rg.Replace(spzn, evaluator);

								newXmlDocument.DocumentElement.FirstChild.SelectSingleNode("./citace").InnerText = spzn;
								// forma/způsob rozhodnutí = druh
								newXmlDocument.DocumentElement.SelectSingleNode("//druh").InnerText = data.ChildNodes[NSS_DETAIL_DRUH_NODE].FirstChild.InnerText.Trim();
								// autor/item
								XmlNode xnAuthor = newXmlDocument.DocumentElement.SelectSingleNode("//autor/item");
								string sAuthor = data.ChildNodes[NSS_DETAIL_AUTHOR_NODE].InnerText.Trim();
								if (spzn.Contains("Konf"))
									sAuthor = "Zvláštní senát zřízený podle zákona č. 131/2002 Sb.";
								else
								{
									switch (sAuthor)
									{
										case "Krajský soud v Hradci Králové, pobočka Pardubice":
											sAuthor = "Krajský soud v Hradci Králové - pobočka Pardubice";
											break;
										case "Krajský soud v Ostravě, pobočka Olomouc":
											sAuthor = "Krajský soud v Ostravě - pobočka Olomouc";
											break;
										case "Krajský soud v Ústí nad Labem, pobočka Liberec":
											sAuthor = "Krajský soud v Ústí nad Labem - pobočka Liberec";
											break;
									}
								}
								xnAuthor.InnerText = sAuthor;
								// datschvaleni RRRR-MM-DD
								string datSchvaleni = Utility.ConvertDateIntoUniversalFormat(datSchvaleniNonUni);
								newXmlDocument.DocumentElement.SelectSingleNode("//datschvaleni").InnerText = datSchvaleni;
								//string rokSchvaleni = datSchvaleni.Substring(0, 4);
								// právní věta
								if (!String.IsNullOrEmpty(sIdVeta))
								{
									newXmlDocument.DocumentElement.SelectSingleNode("//issentence-intext").InnerText = sIdVeta;
								}
#if !DUMMY_DB
								// info = <!-- prejudikatura - oddělit entery -->
								StringBuilder sbPrejudikatura = new StringBuilder();
								HtmlAgilityPack.HtmlNodeCollection vydavatelElements = data.ChildNodes[NSS_DETAIL_PREJUDIKATURA].SelectNodes("span");
								if (vydavatelElements != null && vydavatelElements.Count > 0)
								{
									for (int i = 0; i < vydavatelElements.Count; ++i)
									{
										if (!String.IsNullOrWhiteSpace(vydavatelElements[i].InnerText))
										{
											sbPrejudikatura.AppendLine(UtilityXml.CreateAnItemedLinkForJudicature(ref newXmlDocument, vydavatelElements[i].InnerText.Trim(), conn).OuterXml);
										}
									}
								}
								if (!String.IsNullOrEmpty(sbPrejudikatura.ToString()))
									newXmlDocument.DocumentElement.SelectSingleNode("//prejudikatura").InnerXml = sbPrejudikatura.ToString();
#endif

								// populární název = titul
								/* Vyhledávání 1.1.2014
								 * Výsledky vyhledávání již neobsahují sloupec populární název, tzn není ho odkud brát
								 * takže zde (radši) zakomentuji
								 * 
								 */
								//if (data.ChildNodes.Count > NSS_DETAIL_POPULARNI_NAZEV_NODE && data.ChildNodes[NSS_DETAIL_POPULARNI_NAZEV_NODE] != null)
								//{
								//    newXmlDocument.DocumentElement.SelectSingleNode("//titul").InnerText = data.ChildNodes[NSS_DETAIL_POPULARNI_NAZEV_NODE].InnerText.Trim();
								//}

								NSS_seznamCastecnychVysledku.Add(newXmlDocument);

							}
						}
					}
				}

				NSS_seznamCastecnychVysledku.CompleteAdding();
			});
		}

		/// <summary>
		/// Returns the content of a given web adress as string.
		/// </summary>
		/// <param name="Url">URL of the webpage</param>
		/// <returns>Website content</returns>
		[System.Obsolete("Use TimeoutWebClient")]
		public string DownloadWebPage(string Url)
		{
			// Open a connection
			HttpWebRequest WebRequestObject = (HttpWebRequest)HttpWebRequest.Create(Url);

			// Request response:
			WebResponse Response = WebRequestObject.GetResponse();

			// Open data stream:
			Stream WebStream = Response.GetResponseStream();

			// Create reader object:
			StreamReader Reader = new StreamReader(WebStream);

			// Read the entire stream content:
			string PageContent = Reader.ReadToEnd();

			// Cleanup
			Reader.Close();
			WebStream.Close();
			Response.Close();

			return PageContent;
		}

		/// <summary>
		/// Web client pro stahování PDF souborů, jeden na celý program...
		/// </summary>
		private static TimeoutWebClient clientPdfDownload = new TimeoutWebClient(10000);
		private static TimeoutWebClient clientHeaderDownload = new TimeoutWebClient(5000);

		/// <summary>
		/// Funkce, která spustí vlákno, které vybírá ze seznamu NSS_seznamCastecnychVysledku
		/// Data takto vybraná zpracuje:
		/// Načte webovou stránku pro konrétní dokument
		/// Z této stránky získá odkaz na anonymizovanou verzi rozhodnutí ve formátu pdf, kterou umístí do seznamu NSS_seznamPdfKeStazeni
		/// A navíc získá další informace, které vloží do načteného XmlDokumentu
		/// Nakonec tento dokument uloží
		/// </summary>
		private void NSS_SpustVlaknoProZpracovaniMezivysledku()
		{
			Task.Factory.StartNew(() =>
			{
				XmlDocument data = null;
				string urlToDownload = null;
				while (!NSS_seznamCastecnychVysledku.IsCompleted)
				{
					// dokud je co brát... (aktivní blokování)
					while (!NSS_seznamCastecnychVysledku.IsCompleted && !NSS_seznamCastecnychVysledku.TryTake(out data, 2000)) ;

					if (data != null)
					{
						clientHeaderDownload.Encoding = Encoding.UTF8;
						// zkusím stáhnout právní větu
						XmlNode xn = data.DocumentElement.SelectSingleNode("//issentence-intext");
						if (!String.IsNullOrEmpty(xn.InnerText))
						{
							urlToDownload = String.Format("{0}{1}", NSS_VETA_ROZHODNUTI, xn.InnerText);
							HtmlAgilityPack.HtmlDocument vetaDoc = new HtmlAgilityPack.HtmlDocument();
							try
							{
								vetaDoc.LoadHtml(clientHeaderDownload.DownloadString(urlToDownload));
								xn.InnerText = "1";
								xn = data.DocumentElement.FirstChild.SelectSingleNode("//veta");
								HtmlAgilityPack.HtmlNode hVeta = vetaDoc.DocumentNode.SelectSingleNode("//table");
								hVeta = hVeta.FirstChild;
								while (hVeta != null)
								{
									if (hVeta.Name.Equals("tr") && !String.IsNullOrWhiteSpace(hVeta.InnerText))
									{
										if (hVeta.InnerText.TrimStart().StartsWith("("))
										{
											if (!hVeta.OuterHtml.Contains(" style="))
												throw new WebException();
										}
										else
											xn.InnerXml += "<p><span>" + hVeta.InnerText.Trim() + "</span></p>";
									}
									hVeta = hVeta.NextSibling;
								}
							}
							catch (WebException ex)
							{
								WriteIntoLogCritical(String.Format("Data dokumentu se z webové stránky {0} nepodařilo stáhnout.{1}\t[{2}]", urlToDownload, Environment.NewLine, ex.Message));
								// XML prostě nebudu řešit...
								++NSS_XML_aktualniZaznamKeZpracovani;
								continue;
							}
						}

						// stáhne se hlavička
						XmlNode xnIdExternal = data.SelectSingleNode("//id-external");
						urlToDownload = String.Format("{0}{1}", NSS_DETAIL_ROZHODNUTI, xnIdExternal.InnerText);
						HtmlAgilityPack.HtmlDocument detailDoc = new HtmlAgilityPack.HtmlDocument();
						try
						{
							detailDoc.LoadHtml(clientHeaderDownload.DownloadString(urlToDownload));
						}
						catch (WebException ex)
						{
							WriteIntoLogCritical(String.Format("Data dokumentu se z webové stránky {0} nepodařilo stáhnout.{1}\t[{2}]", urlToDownload, Environment.NewLine, ex.Message));
							// XML prostě nebudu řešit...
							++NSS_XML_aktualniZaznamKeZpracovani;
							continue;
						}

						// grabnu annonymizovanou verzi rozhodnutí (jediný odkaz na stránce)
						//HtmlAgilityPack.HtmlNode anonymizovanaVerzeRozhodnuti = detailDoc.DocumentNode.SelectSingleNode("//a[@href]");
						//if (anonymizovanaVerzeRozhodnuti == null)
						if (data.DocumentElement.Attributes.GetNamedItem("HrefPdf") == null)
						{
							// pdf prostě nebudu řešit...
							++NSS_PDF_aktualniZaznamKeZpracovani;
							// pokud mě zajímají dokumety pouze pokud k nim existuje anonymizovaná verze rozhodnutí
							if (this.NSS_onlyWithPDF)
							{
								// a ta neexistuje, tak mě nezajímají :)
								++NSS_XML_aktualniZaznamKeZpracovani;
								continue;
							}
						}

						// anonymizovanaVerzeRozhodnuti != null || this.cbStahovatPouzeSPDF.Checked == false
						string rokSchvaleni = data.DocumentElement.SelectSingleNode("//datschvaleni").InnerText.Substring(0, 4);
						// citation = NSS 22/2012
						int iRokSchvaleni = Int32.Parse(rokSchvaleni);
						int NSS_cisloDBPoslednihoZpracovanehoDokumentu = NSS_citationService.GetNextCitation(iRokSchvaleni);

						// citation = NSS 22/2012
						string sCitation = String.Format("NSS {0}/{1}", NSS_cisloDBPoslednihoZpracovanehoDokumentu, rokSchvaleni);

						data.DocumentElement.SelectSingleNode("./judikatura-section/header-j/citace").InnerText = sCitation;

						// pathPathWithoutExtension = DocumentName
						string sReferenceNumber = data.DocumentElement.FirstChild.SelectSingleNode("./citace").InnerText;
						string sDocumentName;
						string judikaturaSectionDokumentName;
						Utility.CreateDocumentName("J", sReferenceNumber, rokSchvaleni, out sDocumentName);
						Utility.CreateDocumentName("J", sCitation, rokSchvaleni, out judikaturaSectionDokumentName);
						/* Dokumentname je součástí JudikaturaSection. */
						XmlNode judikaturaSection = data.SelectSingleNode("//judikatura-section");
						judikaturaSection.Attributes["id-block"].Value = judikaturaSectionDokumentName;
						data.DocumentElement.Attributes["DokumentName"].Value = sDocumentName;

						string sFullPathWithoutExtension = String.Format(@"{0}\{1}", this.txtWorkingFolder.Text, sDocumentName);
						// pokud mám anonymizovanou verzi rozhodnutí, tak jí pošlu do dalšího seznamu
						//if (anonymizovanaVerzeRozhodnuti != null)
						if (data.DocumentElement.Attributes.GetNamedItem("HrefPdf") != null)
						{
							string pdfFullPath = String.Format(@"{0}.pdf", sFullPathWithoutExtension);
							//NSS_seznamPdfKeStazeni.Add(new PdfKeStazeni(String.Format(@"http://www.nssoud.cz/{0}", anonymizovanaVerzeRozhodnuti.Attributes["href"].Value), pdfFullPath));
							NSS_seznamPdfKeStazeni.Add(new PdfToDownload(String.Format(@"http://www.nssoud.cz/{0}", data.DocumentElement.Attributes["HrefPdf"].Value), pdfFullPath));
							data.DocumentElement.Attributes.RemoveNamedItem("HrefPdf");
						}

						// mezitím si sám zpracuji XML

						//Z údajů v druhé skupině jsou zajímavé a důležité tyto hodnoty:
						//• Účastník řízení (seznam)
						//• Věc
						//• Název krajského soudu
						//• Č.j. krajského soudu

						/* Všechny tyto sloupce (řádky) jsou součástí jedné tabulky
						* Popis každého řádku je součástí atributu th, který je potomkem daného řádku
						* Jeho sourozenec je obsah, který vyžadujeme
						* Pokud nalezneme popis sloupce, který chceme zapsat, tak přes sourozence (td) se dostaneme až k samotné hodnotě, kterou zapíšeme do xml...
						*/
						HtmlAgilityPack.HtmlNodeCollection seznamRelevantnichPopisuSloupcu = detailDoc.DocumentNode.SelectNodes("//th");
						StringBuilder sbUcastniciRizeni = new StringBuilder();
						string s;
						HtmlAgilityPack.HtmlNode hn;
						foreach (HtmlAgilityPack.HtmlNode relevantniPopis in seznamRelevantnichPopisuSloupcu)
						{
							switch (relevantniPopis.InnerText.Trim())
							{
								case "Účastník řízení":
									hn = relevantniPopis.NextSibling;
									while (hn != null)
									{
										if (!String.IsNullOrWhiteSpace(hn.InnerText))
											sbUcastniciRizeni.Append("<item>" + hn.InnerText.Trim() + "</item>");
										hn = hn.NextSibling;
									}
									break;

								case "Věc":
									// = info3
									s = null;
									if (!String.IsNullOrWhiteSpace(relevantniPopis.NextSibling.InnerText))
										s = relevantniPopis.NextSibling.InnerText.Replace("&nbsp;", " ").Trim();
									else if (!String.IsNullOrWhiteSpace(relevantniPopis.NextSibling.NextSibling.InnerText))
										s = relevantniPopis.NextSibling.NextSibling.InnerText.Replace("&nbsp;", " ").Trim();
									if (!String.IsNullOrEmpty(s))
										data.DocumentElement.SelectSingleNode("//info3").InnerText = s;
									break;

								case "Název krajského soudu":
									// nezkoumám...
									break;

								case "Č.j. krajského soudu":
								case "Č.j. správního orgánu":
									// = vydavatel
									s = relevantniPopis.NextSibling.InnerText.Replace("&nbsp;", " ").Trim();
									if (!String.IsNullOrWhiteSpace(s))
										data.DocumentElement.SelectSingleNode("//authority-number").InnerText = s;
									break;
							}
						}

						// nastavím info4xml = účastníky řízení
						s = sbUcastniciRizeni.ToString().Replace("&", "&amp;");
						if (!String.IsNullOrEmpty(s))
							data.DocumentElement.SelectSingleNode("//info4xml").InnerXml = s;

						// formát souboru => J_RRRR_NSS_číslo
						string sHeaderFullPath = String.Format(@"{0}.xml", sFullPathWithoutExtension);
						if (File.Exists(sHeaderFullPath))
						{
							this.WriteIntoLogCritical(String.Format("Soubor pro dokumentName [{0}] již existuje", sDocumentName));
							// XML prostě nebudu řešit...
							++NSS_XML_aktualniZaznamKeZpracovani;
							NSS_citationService.RevertCitationForAYear(iRokSchvaleni);
							continue;
						}

						// odstranění prázdných elementů z hlavičky
						xn = data.DocumentElement.FirstChild.FirstChild;
						XmlNode xn2;
						while (xn != null)
						{
							if (String.IsNullOrWhiteSpace(xn.InnerText))
							{
								xn2 = xn.NextSibling;
								xn.ParentNode.RemoveChild(xn);
								xn = xn2;
								continue;
							}
							xn = xn.NextSibling;
						}
						/* Před uložením dokumentu protřídíme obsah hlavičky */
						UtilityXml.DeleteEmptyNodesFromHeaders(data);

						data.Save(sHeaderFullPath);
						NSS_citationService.CommitCitationForAYear(iRokSchvaleni);
						// Přejdu ke zpracování další stránky...
						++NSS_XML_aktualniZaznamKeZpracovani;
					}
				}

				// Už nebudu přidávat žádné pdf ke stažení...
				NSS_seznamPdfKeStazeni.CompleteAdding();
			});
		}

		/// <summary>
		/// Funkce, která spustí vlákno, které vybírá ze seznamu NSS_seznamPdfKeStazeni
		/// Stahuje pdf ze zadané url
		/// 
		/// Posune progressbar
		/// 
		/// Převod PDF 2 DOC se dělá externí aplikací ke které v tuto chvíli neexistuje API - tzn dělá se to ručně v aplikaci
		/// </summary>
		private void NSS_SpustVlaknoProZpracovaniPdf()
		{
			Task.Factory.StartNew(() =>
			{
				// The delegate member for progress bar update
				var UpdateProgress = new UpdateDownloadProgressDelegate(NSS_UpdateDownloadProgressSafe);

				PdfToDownload data = null;
				while (!NSS_seznamPdfKeStazeni.IsCompleted)
				{
					// dokud je co brát... (aktivní blokování)
					while (!NSS_seznamPdfKeStazeni.IsCompleted && !NSS_seznamPdfKeStazeni.TryTake(out data, 2000)) ;

					if (data != null)
					{
						if (!File.Exists(data.pdf))
						{
							/* Note: Although you use asynchronous method, it can block the main thread for a while. 
							* It's because before the async download itself, it checks the DNS name 
							* (in this case „mysite.com“) and this check is done internally by blocking function. 
							* If you use directly IP instead of domain name, the DownloadFileAsync method will 
							* be fully asynchronous.
							* http://www.csharp-examples.net/download-files/
							*/
							Uri uriPdfToDownload = new Uri(data.url);
							try
							{
								// stahuji ve spešl vlákně, protože nechci zahltit hlavní vlákno...
								clientPdfDownload.DownloadFile(uriPdfToDownload, data.pdf);
							}
							catch (WebException ex)
							{
								WriteIntoLogCritical(String.Format("PDF dokumentu se z webové stránky {0} nepodařilo stáhnout.{1}\t[{2}]", data.url, Environment.NewLine, ex.Message));
							}
						}

						/*
						* progress bar update
						* Invoke wherever you need to in another thread
						* First parameter is the delegate
						* Second parameter is the value;
						*/
						this.processedBar.Invoke(UpdateProgress, new object[] { false });
						// abych měl průběžné pořadí, ale abych zároveň nedosáhl maxima...
						++NSS_PDF_aktualniZaznamKeZpracovani;
					}
				}

				if (IsRunningFromFile)
				{
					var toProcess = FileDocumentIds.FirstOrDefault();
					if (!string.IsNullOrWhiteSpace(toProcess.Key))
					{
						var val = string.Empty;
						FileDocumentIds.TryRemove(toProcess.Key, out val);
					}
					if (FileDocumentIds.Count == 0)
					{
						// už je všechno hotovo; To vím, protože abych mohl zpracovat pdf, musím mít xml, ...
						this.processedBar.BeginInvoke(UpdateProgress, new object[] { true });
						FinalizeLogs();
						MessageBox.Show("1) Spusťte program pro převod pdf dokumentů na doc dokumenty!\r\n2) Vyberte všechny stažené doc dokumenty a převeďte je!\r\n3) Stiskněte tlačítko doc->xml", "Dokončení převodu NSS", MessageBoxButtons.OK, MessageBoxIcon.Information);

					}
					else
					{
						RunFile();
					}
				}
				else
				{
					// už je všechno hotovo; To vím, protože abych mohl zpracovat pdf, musím mít xml, ...
					this.processedBar.BeginInvoke(UpdateProgress, new object[] { true });
					FinalizeLogs();
					MessageBox.Show("1) Spusťte program pro převod pdf dokumentů na doc dokumenty!\r\n2) Vyberte všechny stažené doc dokumenty a převeďte je!\r\n3) Stiskněte tlačítko doc->xml", "Dokončení převodu NSS", MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
			});
		}

		/// <summary>
		/// Funkce, která spočítá aktuální progress na základě
		/// 1) Přečtených stran webu 15%
		/// 2) Vytvořených XML 25%
		/// 3) Stažených pdf 60%
		/// </summary>
		/// <returns></returns>
		private int NSS_ComputeActuallDownloadProgress()
		{
			int podilPrectenychStran = NSS_aktualniStrankaKeZpracovani * 15 / NSS_celkemStranekKeZpracovani;
			int podilVytvorenychXML = NSS_XML_aktualniZaznamKeZpracovani * 25 / NSS_XML_celkemZaznamuKeZpracovani;
			int podilStazenychPDF = NSS_PDF_aktualniZaznamKeZpracovani * 60 / NSS_PDF_celkemZaznamuKeZpracovani;
			return podilPrectenychStran + podilVytvorenychXML + podilStazenychPDF;
		}

		/// <summary>
		/// Funkce pro update progress baru GUI vlákna, kterou lze použít v jiném vlákně (přes delegát)
		/// </summary>
		/// <param name="forceComplete">Parametr, který říká, že je prostě hotovo...</param>
		void NSS_UpdateDownloadProgressSafe(bool forceComplete)
		{
			if (IsRunningFromFile)
			{
				var value = 0;
				if (forceComplete)
				{
					value = this.processedBar.Maximum;
					this.gbProgressBar.Text = "Dokončeno.";
				}
				else
				{
					var toProcess = FileDocumentIds.FirstOrDefault();
					if (!string.IsNullOrWhiteSpace(toProcess.Key))
					{
						value = (int)(((double)(FileDocumentCount - FileDocumentIds.Count) / FileDocumentCount) * 100);
						this.gbProgressBar.Text = String.Format("Zpracovávám záznam {0}. Celkem {1}%", toProcess.Key, value);
					}
				}

				this.processedBar.Value = value;
				bool maximumReached = (this.processedBar.Value == this.processedBar.Maximum);
				// pokud jsem "reachnul" maximum, povolím opětovné načítání položek...
				this.btnMineDocuments.Enabled = NSS_DocumentsWereDownloaded = maximumReached;
			}
			else
			{
#if DEBUG
				this.gbProgressBar.Text = String.Format("Zpracováno {0}/{1} stránek | {2}/{3} XML | {4}/{5} PDF => {6}%", NSS_aktualniStrankaKeZpracovani, NSS_celkemStranekKeZpracovani, NSS_XML_aktualniZaznamKeZpracovani, NSS_XML_celkemZaznamuKeZpracovani, NSS_PDF_aktualniZaznamKeZpracovani, NSS_PDF_celkemZaznamuKeZpracovani, this.processedBar.Value);
#endif
				int value = NSS_ComputeActuallDownloadProgress();
				if (forceComplete)
				{
					value = this.processedBar.Maximum;
				}
				this.processedBar.Value = value;
				bool maximumReached = (this.processedBar.Value == this.processedBar.Maximum);
				// pokud jsem "reachnul" maximum, povolím opětovné načítání položek...
				this.btnMineDocuments.Enabled = NSS_DocumentsWereDownloaded = maximumReached;
			}
		}

		void NSS_UpdateFileDownloadProgressSafe()
		{

		}

		/// <summary>
		/// Delegát pro nastavení enable/disable tlačítka btnNaXml
		/// </summary>
		/// <param name="value"></param>
		void NSS_BtnNaXmlSetEnable(bool value)
		{
			this.NSS_btnWordToXml.Enabled = value;
		}

		/// <summary>
		/// Delegát pro update progress baru GUI vlákna, který lze volat v jiném vlákně
		/// Pro aktualizaci na základě předané hodnoty
		/// </summary>
		/// <param name="value"></param>
		void NSS_UpdateProgressSafe(int value)
		{
			this.processedBar.Value = value;
			if (value == this.processedBar.Maximum)
			{
				MessageBox.Show(this, "Hotovo", "Převod NSS", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}

		private string NSS_CheckFilledValues()
		{
			StringBuilder sbResult = new StringBuilder();
			/* Greater than zero => This instance is later than value. */
			if (this.NSS_dtpDateFrom.Value.CompareTo(this.NSS_dtpDateTo.Value) > 0)
			{
				sbResult.AppendLine(String.Format("Datum od [{0}] je větší, než datum do [{1}].", this.NSS_dtpDateFrom.Value.ToShortDateString(), this.NSS_dtpDateTo.Value.ToShortDateString()));
			}

			if (this.NSS_dtpDateFrom.Value.Year != this.NSS_dtpDateTo.Value.Year)
				sbResult.AppendLine(String.Format("Rok od [{0}] je jiný, nežli rok do [{1}].", this.NSS_dtpDateFrom.Value.Year, this.NSS_dtpDateTo.Value.Year));

			if (String.IsNullOrWhiteSpace(this.txtWorkingFolder.Text))
			{
				sbResult.AppendLine("Pracovní složka (místo pro uložení surových dat) musí být vybrána.");
			}

			return sbResult.ToString();
		}

		/// <summary>
		/// Kliknutí na načtení dokumentů
		/// Znemožní se další kliknutí
		/// Aktuální akce se nastaví na "vyhledání dat"
		/// Načtené odkazy se vynulují
		/// Vytvoří se blokující seznamy a vlákna s nimi pracující
		/// Naviguje se na hlavní stránku vyhledávání NSS
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private bool NSS_Click()
		{
			if (!string.IsNullOrWhiteSpace(NSSFileWithNSSDialog.FileName))
			{
				string sError = string.Empty;

				if (String.IsNullOrWhiteSpace(this.txtWorkingFolder.Text))
				{
					sError = "Pracovní složka (místo pro uložení surových dat) musí být vybrána.";
				}

				if (!string.IsNullOrEmpty(sError))
				{
					MessageBox.Show(this, sError, ERROR_CHECKING_OUTPUT, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
					return false;
				}

				this.btnMineDocuments.Enabled = false;
				this.NSS_DocumentsWereDownloaded = false;
				this.NSS_onlyWithPDF = this.NSS_cbDownloadOnlyDocumentsWithPDF.Checked;
				this.aktuaniNSSAkce = NSSAkce.nssaVyhledaniDat;
				this.processedBar.Value = 0;

				using (var sr = new StreamReader(NSSFileWithNSSDialog.FileName))
				{
					var fileDocumentIds = sr.ReadToEnd().Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Distinct();
					UpdateIsRunningFromFile(true);

					foreach (var line in fileDocumentIds)
					{
						FileDocumentIds.TryAdd(line.Trim(), line.Trim());
					}
				}

				FileDocumentCount = FileDocumentIds.Count;
				NSSFileWithNSSDialog.Reset();
				if (FileDocumentCount == 0) return true;

				if (!this.citationNumberGenerator.ContainsKey(Courts.cNSS))
				{
					this.citationNumberGenerator.Add(Courts.cNSS, new CitationService(Courts.cNSS));
				}
				this.NSS_citationService = this.citationNumberGenerator[Courts.cNSS];

				return RunFile();
			}
			else
			{
				string sError = NSS_CheckFilledValues();
				if (!String.IsNullOrEmpty(sError))
				{
					MessageBox.Show(this, sError, ERROR_CHECKING_OUTPUT, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
					return false;
				}

				this.btnMineDocuments.Enabled = false;
				this.NSS_DocumentsWereDownloaded = false;
				this.NSS_onlyWithPDF = this.NSS_cbDownloadOnlyDocumentsWithPDF.Checked;
				this.aktuaniNSSAkce = NSSAkce.nssaVyhledaniDat;
				this.processedBar.Value = 0;

				if (!this.citationNumberGenerator.ContainsKey(Courts.cNSS))
				{
					this.citationNumberGenerator.Add(Courts.cNSS, new CitationService(Courts.cNSS));
				}
				this.NSS_citationService = this.citationNumberGenerator[Courts.cNSS];

				NSS_seznamElementuKeZpracovani = new BlockingCollection<HtmlAgilityPack.HtmlNode>(100);
				NSS_seznamCastecnychVysledku = new BlockingCollection<XmlDocument>(30);
				NSS_seznamPdfKeStazeni = new BlockingCollection<PdfToDownload>(50);

				/*
				* Vytvořím vlákna pro
				* Vybírá data ze seznamu "html" elementů - výsledků z crawlera, ukládá XML soubor s informacemi o jednotlivých výsledích a plní odkazy na detail (celý XmlDocument, jedna - zatím poslední položka je odkaz na detail)
				* Vybírá data ze seznamu odkazyNaDetail - uloží zbytek informací do načteného xml
				* Vybírá data ze seznamu pdf ke stažení a stáhne je
				*/
				NSS_SpustVlaknoProZpracovaniElementu();
				NSS_SpustVlaknoProZpracovaniMezivysledku();
				NSS_SpustVlaknoProZpracovaniPdf();

				browser.Navigate(NSS_INDEX);
				return true;
			}
		}

		/// <summary>
		/// Provede akci vyhledání
		/// Předpoklad: Právě stojím na hlavní stránce vyhledávání
		/// Získá odkaz na elementy data - vyplní je
		/// Získá odkaz na tlačítko vyhledej a vyvolá jeho metodu "click"
		/// Dále nastaví aktuální stránku/záznam ke zpracování na 1
		/// </summary>
		private void ProvedNSSAkciVyhledaniDat()
		{
			/*
			* Do této akce jsem se dostal navigací do vyhledávacího formuláře
			*
			* Nastavím parametry vyhledávání, nastavím další akci ke zpracování & stisknu vyhledávací button
			*/
			// zadám pole do vyhledávacího formuláře
			if (IsRunningFromFile)
			{
				var toProcess = FileDocumentIds.FirstOrDefault();
				if (!string.IsNullOrWhiteSpace(toProcess.Key))
				{
					var splits = toProcess.Key.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
					var firstSplits = splits[0].Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

					browser.Document
					.GetElementById("_ctl0_ContentPlaceMasterPage__ctl0_txtSenat")
					.SetAttribute("value", firstSplits[0].Trim());

					var ddlElement = browser.Document.GetElementById("_ctl0_ContentPlaceMasterPage__ctl0_ddlRejstrik");
					var elCol = ddlElement.GetElementsByTagName("option");
					var valIdx = 0;
					foreach (HtmlElement op in elCol)
					{
						if (op.InnerText == firstSplits[1].Trim())
						{
							ddlElement.SetAttribute("selectedIndex", valIdx.ToString());
							break;
						}
						valIdx++;
					}

					HtmlElement txtCislo = browser.Document.GetElementById("_ctl0_ContentPlaceMasterPage__ctl0_txtCislo");

					txtCislo.SetAttribute("value", firstSplits[firstSplits.Length - 1].Trim());

					//browser.Document
					//.GetElementById("_ctl0_ContentPlaceMasterPage__ctl0_txtCislo")
					//.SetAttribute("value", firstSplits[2].Trim());

					var secondSplits = splits[1].Split(new char[] { '-' }, StringSplitOptions.RemoveEmptyEntries);

					browser.Document
					.GetElementById("_ctl0_ContentPlaceMasterPage__ctl0_txtRok")
					.SetAttribute("value", secondSplits[0].Trim());

					if (secondSplits.Length > 1)
					{
						browser.Document
						.GetElementById("_ctl0_ContentPlaceMasterPage__ctl0_txtCisloJednaci")
						.SetAttribute("value", secondSplits[1].Trim());
					}
				}
				else
				{
					return;
				}
			}
			else
			{
				browser.Document.GetElementById("_ctl0_ContentPlaceMasterPage__ctl0_txtDatumOd").SetAttribute("value",
				this.NSS_dtpDateFrom.Value.ToString("dd.MM.yyyy"));

				browser.Document.GetElementById("_ctl0_ContentPlaceMasterPage__ctl0_txtDatumDo").SetAttribute("value",
				this.NSS_dtpDateTo.Value.ToString("dd.MM.yyyy"));
			}
			// výsledky chci seřadit dle data rozhodnutí; implicitně je tak seřazeno...
			HtmlElement elcbSortName = browser.Document.GetElementById("_ctl0_ContentPlaceMasterPage__ctl0_ddlSortName");
			elcbSortName.Children[2].SetAttribute("selected", "x");
			// a Vzestupně
			HtmlElement elcbDirection = browser.Document.GetElementById("_ctl0_ContentPlaceMasterPage__ctl0_ddlSortDirection");
			elcbDirection.Children[0].SetAttribute("selected", "x");

			// nastavím příznak, že už jsem data dal vyhledat...
			aktuaniNSSAkce = NSSAkce.nssaPrvniUlozeniOdkazu;

			NSS_aktualniStrankaKeZpracovani = 1;
			// čísluji od nuly, aby mi to hezky vycházelo (když to dokončím)
			NSS_PDF_aktualniZaznamKeZpracovani = 0;
			NSS_XML_aktualniZaznamKeZpracovani = 0;

			HtmlElement el = this.browser.Document.All["_ctl0_ContentPlaceMasterPage__ctl0_btnFind"];
			el.InvokeMember("click");
			//object obj = el.DomElement;
			//System.Reflection.MethodInfo mi = obj.GetType().GetMethod("click");
			//mi.Invoke(obj, new object[0]);
		}

		/// <summary>
		/// Akce uložení odkazu
		/// Předpoklad: Stojím na stránce s výsledky
		/// Uzly (Html) výsledků procházím a všechny neprázdné (a neobsahující element th jako poslední potomek uzlu výsledku)
		/// vložím do seznamu NSS_seznamElementuKeZpracovani (kde je o něj postaráno separátním vláknem)
		///
		/// Pokud jsem na poslední stránce, dokunčím přidávání do seznamu NSS_seznamElementuKeZpracovani a vyskočím z funkce
		///
		/// Jinak najdu odkaz (tlačítko) na následující stránku ve vyhledávání a vyvolám na ní událost "click" (stejný princip jako pro prvotní vyhledávání).
		/// </summary>
		/// <param name="firstInvoke">Je tato akce volána poprvé? Pokud ano, zjištuje se celkový počet nalezených záznamů (stránek)</param>
		private void ProvedNSSAkciUlozeniOdkazu(bool firstInvoke)
		{
			HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
			doc.LoadHtml(browser.Document.Body.OuterHtml);

			if (firstInvoke)
			{
				HtmlAgilityPack.HtmlNode divPagingBox = doc.DocumentNode.SelectSingleNode("//div[@id='PagingBox1']");
				NSS_PDF_celkemZaznamuKeZpracovani = NSS_XML_celkemZaznamuKeZpracovani = NSS_celkemStranekKeZpracovani = 1;
				if (divPagingBox.FirstChild != null)
				{
					HtmlAgilityPack.HtmlNode resultCountB = divPagingBox.FirstChild.LastChild;
					NSS_PDF_celkemZaznamuKeZpracovani = NSS_XML_celkemZaznamuKeZpracovani = Int32.Parse(resultCountB.InnerText);
					if ((IsRunningFromFile && NSS_XML_celkemZaznamuKeZpracovani == 1))
					{
						NSS_XML_celkemZaznamuKeZpracovani = 2;
					}
					NSS_celkemStranekKeZpracovani = NSS_XML_celkemZaznamuKeZpracovani / 10 + (NSS_XML_celkemZaznamuKeZpracovani % 10 != 0 ? 1 : 0);
					// od příště už nebudu zjišťovat počet stránek...
					aktuaniNSSAkce = NSSAkce.nssaUlozeniOdkazu;
				}
			}

			// pokud je co zpracovávat, tak zpracovávám...
			if (NSS_XML_celkemZaznamuKeZpracovani != 1)
			{
				HtmlNodeCollection tabulkaSVysledky = doc.DocumentNode.SelectNodes("//table[@id='_ctl0_ContentPlaceMasterPage__ctl0_grwA']//tr[not(child::th) and normalize-space()]");
				foreach (HtmlAgilityPack.HtmlNode uzelKPridani in tabulkaSVysledky)
				{
					NSS_seznamElementuKeZpracovani.Add(uzelKPridani);
				}
			}

			if (NSS_aktualniStrankaKeZpracovani == NSS_celkemStranekKeZpracovani)
			{
				// už nebudu nic nového přidávat...
				NSS_seznamElementuKeZpracovani.CompleteAdding();
				return;
			}
			/* navigace na další stránku.
			* Nemůžu hledat moc napřímo, protože ty pravidla sice nejsou nepřekonatelně složitá, ale tímto algoritmem získám větší jistotu
			* (i do budoucna)
			* Najdu si předka tlačítek se stránkami a procházím jednotlivá tlačítka hledajíc řetězec čísla stránky
			*/
			++NSS_aktualniStrankaKeZpracovani;
			HtmlElement el = this.browser.Document.All["PagingBox2"];
			foreach (HtmlElement prochazenaPage in el.Children)
			{
				if (prochazenaPage.TagName.ToUpper() == "A" &&
				prochazenaPage.InnerText == NSS_aktualniStrankaKeZpracovani.ToString())
				{
					prochazenaPage.InvokeMember("click");
					// a to je vše přátelé...
					return;
				}
			}

			// GUARD
			MessageBox.Show(this, "Nenalezl jsem další stránku, vyhledání bude neúplné!", "Načtení dokumentů NSS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}

		/// <summary>
		/// Došlo k načtení dokumentu.
		/// V závislosti na tom, jakou mam nastavenu aktuální akci zavolám "obsluhu"
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void NSS_browser_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
		{
			if (NSS_seznamElementuKeZpracovani.IsAddingCompleted)
			{
				return;
			}

			// budu hodný a chvíli počkám, než znovu zaklepám...
			System.Threading.Thread.Sleep(2500);
			switch (aktuaniNSSAkce)
			{
				case NSSAkce.nssaVyhledaniDat:
					ProvedNSSAkciVyhledaniDat();
					break;

				case NSSAkce.nssaPrvniUlozeniOdkazu:
					ProvedNSSAkciUlozeniOdkazu(true);
					break;

				case NSSAkce.nssaUlozeniOdkazu:
					ProvedNSSAkciUlozeniOdkazu(false);
					break;
			}

			if (NSS_celkemStranekKeZpracovani >= NSS_aktualniStrankaKeZpracovani)
			{
				// aktualizace posuvníku
				if (IsRunningFromFile) NSS_UpdateFileDownloadProgressSafe();
				else NSS_UpdateDownloadProgressSafe(false);
			}
		}

		private void NSS_btnToXml_Click(object sender, EventArgs e)
		{
			if (!NSS_DocumentsWereDownloaded &&
					 MessageBox.Show(this, "Nedošlo ke stažení dokumentů z webu, přejete si přesto převést obsah pracovní složky?", "NSS", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == System.Windows.Forms.DialogResult.No)
			{
				return;
			}
			this.NSS_btnWordToXml.Enabled = false;
			// jedeme od nuly...
			this.processedBar.Value = 0;

			//Task t = Task.Factory.StartNew(() =>
			//{
			//    UpdateConversionProgressDelegate UpdateProgress = new UpdateConversionProgressDelegate(NSS_UpdateProgressSafe);
			//    ConvertDelegate Convert = new ConvertDelegate(NSS_ConvertOneDocToXml);
			//    TransformDocInWorkingFolderToXml(UpdateProgress, Convert);
			//});

			//while (t.Status != TaskStatus.RanToCompletion)
			//{
			//    Application.DoEvents();
			//    System.Threading.Thread.Sleep(1000);
			//}

			// Tasks jsou zde zrušeny, protože to dělá problémy s MS Word

			UpdateConversionProgressDelegate UpdateProgress = new UpdateConversionProgressDelegate(NSS_UpdateProgressSafe);
			ConvertDelegate Convert = new ConvertDelegate(NSS_ConvertOneDocToXml);
			TransformDocInWorkingFolderToXml(UpdateProgress, Convert);

			this.NSS_btnWordToXml.Enabled = true;
		}

		/// <summary>
		/// Funkce, která převede všechny dokumenty MS word v aktuálně vybrané pracovní složce na
		/// dokumenty .xml (do hotového stavu)
		/// </summary>
		private void TransformDocInWorkingFolderToXml(UpdateConversionProgressDelegate UpdateProgress, ConvertDelegate ConvertDelegate)
		{
			// Tasks jsou zde zrušeny, protože to dělá problémy s MS Word

			DirectoryInfo di = new DirectoryInfo(this.txtWorkingFolder.Text);
			//FileInfo[] docs = di.GetFiles().Where(f => (f.Extension == ".doc" || f.Extension == ".rtf" || f.Extension == ".docx" || f.Extension == ".xml") && f.Name.StartsWith("J")).ToArray();
			FileInfo[] docs = di.GetFiles().Where(f => (f.Extension == ".doc" || f.Extension == ".rtf" || f.Extension == ".docx") && f.Name.StartsWith("J")).ToArray();
			/* Foreach document one task*/
			//Task[] conversionTasks = new Task[docs.Length];
			/* Vytvoření tasků */
			var taskCounter = 1;
			var totalCount = docs.Length + 1;
			foreach (FileInfo fi in docs)
			{
				string docFullName = fi.FullName;

				//conversionTasks[taskNo++] = Task.Factory.StartNew(() =>
				//{
				using (SqlConnection conn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["LexData"].ConnectionString))
				{
					conn.Open();
					// 8 Ca 402_2007 - J_RRRR_Vyber_NS_Č.xml, .pdf (staženo) a .doc (převedeno z pdf)
					string sDocumentName = Path.GetFileNameWithoutExtension(docFullName);
					// vytvořím výstupní složku a přesunu do ní hlavičku & word
					string sOutputDocumentFolder = String.Format(@"{0}\{1}", this.txtOutputFolder.Text, sDocumentName);
					if (!Directory.Exists(sOutputDocumentFolder))
					{
						Directory.CreateDirectory(sOutputDocumentFolder);
					}
					// přesunu hlavičku & word
					string sPathXmlHeader = String.Format(@"{0}\{1}.xml", Path.GetDirectoryName(docFullName), sDocumentName);
					string sPathWordXml = String.Format(@"{0}\W_{1}-0.xml", sOutputDocumentFolder, sDocumentName);
					string sPathWordXmlXml = String.Format(@"{0}\W_{1}-0.xml.xml", sOutputDocumentFolder, sDocumentName);
					OpenFileInWordAndSaveInWXml(docFullName, sPathWordXml);
					File.Copy(sPathWordXml, sPathWordXmlXml, true);
					string sPathOutputXml = String.Format(@"{0}\{1}.xml", sOutputDocumentFolder, sDocumentName);
					File.Copy(sPathXmlHeader, sPathOutputXml, true);
					// Kopíruju hlavičku & pdf
					string sPathPdfDocument = String.Format(@"{0}\{1}.pdf", Path.GetDirectoryName(docFullName), sDocumentName);
					// zpracovani...
					try
					{
						if (ConvertDelegate(sOutputDocumentFolder, sDocumentName, conn))
						{
							/* Uložím kopii do složky Prilohy */
							if (File.Exists(sPathPdfDocument))
							{
								XmlDocument dOut = new XmlDocument();
								dOut.Load(sPathOutputXml);

								if (!Directory.Exists(sOutputDocumentFolder))
								{
									Directory.CreateDirectory(sOutputDocumentFolder);
								}
								string filenamePrilohyCopy = String.Format(@"{0}\Original_{1}-1.pdf", sOutputDocumentFolder, sDocumentName);
								File.Move(sPathPdfDocument, filenamePrilohyCopy);

								/* Přidám info o originálním souboru do dokumentu! */
								XmlAttribute a = dOut.CreateAttribute("source-file");
								a.Value = String.Format("Original_{0}-1.pdf", sDocumentName);
								dOut.DocumentElement.Attributes.Append(a);
								dOut.Save(sPathOutputXml);
							}
							else
							{
								WriteIntoLogCritical("{0}: Originální pdf dokument nebyl, v pracovní složce, nalezen!", sDocumentName);
							}

							// smažu W_ ... .xml.xml
							File.Delete(sPathWordXmlXml);
							// a doc.
							File.Delete(docFullName);
							// a .xml hlavičku
							File.Delete(sPathXmlHeader);
						}
						else
						{
							// musím smazat výstupní složku
							if (Directory.Exists(sOutputDocumentFolder))
							{
								Directory.Delete(sOutputDocumentFolder, true);
							}
						}
					}
					catch (Exception ex)
					{
						WriteIntoLogCritical("{0}: Exception=[{1}]", sDocumentName, ex.Message);
					} /* !!! */
				}
				//});

				//    // update progress baru
				var progress = (taskCounter * 100 / (totalCount));
				//    // nechci dosáhnout 100%, aby mi to nevypsalo hlášku dvakrát...
				this.processedBar.Invoke(UpdateProgress, new object[] { progress });
				taskCounter++;
			}

			/* (+1)*/
			//int totalNumberOfTasks = taskNo;
			//int tasksCompleted = 0;
			//while (tasksCompleted != totalNumberOfTasks)
			//{
			//    /* When some task is completed */
			//    int i = Task.WaitAny(conversionTasks);
			//    ++tasksCompleted;

			//    // update progress baru
			//    int progress = (tasksCompleted * 100 / (totalNumberOfTasks + 1));
			//    // nechci dosáhnout 100%, aby mi to nevypsalo hlášku dvakrát...
			//    this.processedBar.BeginInvoke(UpdateProgress, new object[] { progress });

			//    /* Delete completed tasks from the list */
			//    var temp = conversionTasks.ToList();
			//    temp.RemoveAt(i);
			//    conversionTasks = temp.ToArray();
			//}

			if (this.processedBar.Value != 100)
			{
				this.processedBar.BeginInvoke(UpdateProgress, new object[] { 100 });
			}
			FinalizeLogs();
		}

		/// <summary>
		/// Funkce, která převede jeden soubor jednoznačně určený atributy @pathFolder a @dokumentName
		/// z formátu .doc na formát .xml
		/// 
		/// 1. Export
		/// 2. Linkování
		/// 3. Smazání prázdných řádků a výcenásobných mezer
		/// 4- Smazání všeho až po text končící jménemrepubliky
		/// 5. Odstranění zbytečně prázdných řádků
		/// 6. DOplnění pole cituje (s využitím @connection)
		/// </summary>
		/// <param name="pPathFolder">Složka, ve které se nachází dokument k převedení</param>
		/// <param name="sDocumentName">Jméno dokumentu k převedení</param>
		/// <param name="pConn">Aktivní připojení k databázi (pro účely doplnění pole cituje</param>
		/// <returns></returns>
		private bool NSS_ConvertOneDocToXml(string pPathFolder, string sDocumentName, SqlConnection pConn)
		{
			string sExportErrors = String.Empty;
			string[] parametry = new string[] { "CZ", pPathFolder + "\\" + sDocumentName + ".xml", sDocumentName, "0", "17" };
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

			/*
			 * Cílem je odstranit
			 * <obrázek státního znaku>
			 * česká republika
			 * rozsudek jménem republiky
			 * 
			 * 
			 * Problém je, že se nedá 100% spolehnout na strukturu.
			 * Vycházím z toho, že se tato oblast nachází naúplném začátku dokumentu.
			 * 
			 * Hledám text, který končí na jménem republiky.
			 * Pokud ho najdu, tak všechny uzly, které mu předcházejí, odstraním
			 * V každém případě se snažím odstranit obrázek...
			 */

			List<string> TEXT_TO_DELETE_LOWER_NO_SPACES = new List<string>();
			//	Jménem republiky
			TEXT_TO_DELETE_LOWER_NO_SPACES.Add("jmenemrepubliky");



			string sSpisovaZnacka = dOut.DocumentElement.FirstChild.SelectSingleNode("./citace").InnerText.ToLower();
			sSpisovaZnacka = Utility.GetReferenceNumberNorm(sSpisovaZnacka, out string sNormValue2);

			int i = 0;
			XmlNode xn = xnHtml.FirstChild;
			while ((++i < 15) && (xn != null))
			{
				//	Smazání obrázku na začátku 
				if (xn.InnerXml.Contains("img"))
				{
					xn.InnerXml = "";
				}


				string innerText = xn.InnerText.ToLower();
				innerText = Utility.GetReferenceNumberNorm(innerText, out string sNormValue3);

				//	Smazání čísla jednacího v každém případě
				if (innerText.Equals(sSpisovaZnacka))
				{
					xn.InnerXml = "";
				}

				//	Nalezl jsem text končící vybraným slovem 
				if (TEXT_TO_DELETE_LOWER_NO_SPACES.Any(text => innerText.EndsWith(text)))
				{
					//	Smažu všechny předchozí uzly! 
					XmlNode nodeToDelete = xnHtml.FirstChild;
					while (nodeToDelete != xn)
					{
						//	Posunu se na další 
						nodeToDelete = nodeToDelete.NextSibling;
						//	Smažu předchozí 
						xnHtml.RemoveChild(nodeToDelete.PreviousSibling);
					}
					//	Smažu aktuální uzel
					xnHtml.RemoveChild(xn);

					break;
				}

				xn = xn.NextSibling;
			}

			/*
			bool isFirstParagraph = false;
			XmlNode node = xnHtml.FirstChild;
			while (!isFirstParagraph)
			{
				isFirstParagraph = (node.InnerText.Contains("Nejvyšší správní soud rozhodl")) || (node == null);

				if ((!isFirstParagraph) && (node.Name.Equals("p")))
				{
					//if (node.InnerXml.ToLower().Contains("img"))
					//{
					//	xnHtml.RemoveChild(node);
					//	continue;
					//}

					//if ((String.IsNullOrEmpty(node.InnerText)) || (String.IsNullOrEmpty(node.InnerXml))
					//{
					//	xnHtml.RemoveChild(node);
					//	continue;
					//}
					xnHtml.RemoveChild(node);
					node = xnHtml.FirstChild;
				}
				//else
				//{
				//	node = xnHtml.NextSibling;
				//}
			}
			*/


			//if (!this.OpravaDokumentuNSS(ref dOut))
			//{
			//    ZapisChybuDoLoguKriticke("Vadně ukončené řádky a stránky v dokumentu !");
			//    return false;
			//}
			this.CorrectionDocumentNSS(ref dOut);

			XmlNode htmlText = dOut.SelectSingleNode("//html-text");

			// odstraneni <ml> tagu bez <li>
			this.RemoveOrphanedMlTags(htmlText);

			// odstraneni zahnizdenych <p> tagu
			this.CorrectNestedParagraphs(htmlText.ChildNodes);

			//odstraneni U S N E S E N Í
			XmlNode usneseni = htmlText.ChildNodes.Cast<XmlNode>()
				.Where(n => n.InnerXml.Contains("U S N E S E N Í"))
				.FirstOrDefault();
			if (usneseni != null)
				htmlText.RemoveChild(usneseni);

			// odstraneni prazdnych radku na zacatku textu
			List<XmlNode> emptyRows = new List<XmlNode>();
			foreach (XmlNode node in htmlText.ChildNodes)
			{
				if (node.Name.Equals("p"))
				{
					if (String.IsNullOrEmpty(node.InnerXml) && String.IsNullOrEmpty(node.InnerText))
					{
						emptyRows.Add(node);
					}
					else
					{
						break;
					}
				}
			}
			emptyRows.ForEach(n => htmlText.RemoveChild(n));

			UtilityXml.RemoveRedundantEmptyRowsInXmlDocument(ref xnHtml);
			UtilityXml.AddCite(dOut, sDocumentName, pConn);

			// uložení
			dOut.Save(sPathResultXml);
			return true;
		}

		private void RemoveOrphanedMlTags(XmlNode parent)
		{
			List<XmlNode> nodesToDelete = new List<XmlNode>();

			foreach (XmlNode mlNode in parent.ChildNodes)
			{
				XmlNode nextNode = mlNode;

				if (mlNode.Name.Equals("ml"))
				{
					if ((mlNode.HasChildNodes) && (!mlNode.ChildNodes[0].Name.Equals("li")))
					{
						XmlElement pElm = parent.OwnerDocument.CreateElement("p");
						pElm.InnerXml = mlNode.InnerXml;
						parent.InsertBefore(pElm, mlNode);
						nodesToDelete.Add(mlNode);
						nextNode = pElm;
					}
				}

				this.RemoveOrphanedMlTags(nextNode);
			}

			nodesToDelete.ForEach(n => parent.RemoveChild(n));
		}

		private void CorrectNestedParagraphs(XmlNodeList nodes)
		{
			foreach (XmlNode node in nodes)
			{
				if (node.Name.Equals("p"))
				{
					IEnumerable<XmlNode> nested = node.ChildNodes.OfType<XmlNode>().Where(n => n.Name.Equals("p"));

					List<XmlNode> parsToDelete = new List<XmlNode>();
					XmlNode prevNode = node;
					foreach (XmlNode par in nested)
					{
						XmlNode tmp = par.Clone();
						node.ParentNode.InsertAfter(tmp, prevNode);
						parsToDelete.Add(par);
						prevNode = tmp;
					}

					parsToDelete.ForEach(p => node.RemoveChild(p));
				}

				this.CorrectNestedParagraphs(node.ChildNodes);
			}
		}

		private void CorrectionDocumentNSS(ref XmlDocument pD)
		{
			bool byloOduvodneni = false, zarovnatDoBloku = false, bylVzorPodpis = false;
			bool bMerge;
			Regex vzorPodpisDne = new Regex(@"^V\s+.+\s+(dne\s+)?.+[1-2][0-9][0-9][0-9]$");
			Regex vzorPulDatum = new Regex(@"\D[1,2,3]?[0-9]\.\s*1?[0-9]\.$");
			Regex rgDayDate = new Regex(@"\s+dne\s+[1,2,3]?[0-9]\.$");
			Regex rgYear = new Regex(@"\s*[1-2][0-9][0-9][0-9]\D");
			Regex rgMonthYear = new Regex(@"1?[0-9]\.\s*[1-2][0-9][0-9][0-9]\D");
			Regex rgPageNumber = new Regex(@"^-\s*\d+\s*-$");
			Regex rgMoney = new Regex(@"\d+\.?\d*,?\s?-?-?Kč\s*$");
			Regex rgDateBegining = new Regex(@"^\s*[1,2,3]?[0-9]\.\s*1?[0-9]\.\s*[1-2][0-9][0-9][0-9]\D");
			string s, s2, s3, sPart1, sPart2, sStyle, sYear;
			int iPosition, i = 0, iPosition2;
			XmlElement el;
			XmlAttribute a;
			UtilityBeck.TypyText tt;
			string sId = null, sDruh = null;
			XmlNode xn2 = null, xn3, xn4, xn = pD.DocumentElement.FirstChild.SelectSingleNode("./citace");
			XmlNode xnTakto = null, uDruh = pD.DocumentElement.FirstChild.SelectSingleNode("./druh");
			string sSpisovaZnacka = xn.InnerText.ToLower(), sSpisovaZnackaZkracene;
			Utility.RemoveWhiteSpaces(ref sSpisovaZnacka);
			if (uDruh != null)
			{
				sDruh = uDruh.InnerText.ToLower();
			}
			// hledám pomlčku, pokud ji najdu, tak do zkrácené verze čísla jednacího dám text před pomlčkou
			iPosition = sSpisovaZnacka.IndexOf('-');
			if (iPosition > -1)
			{
				sSpisovaZnackaZkracene = sSpisovaZnacka.Substring(0, iPosition);
			}
			else
			{
				sSpisovaZnackaZkracene = sSpisovaZnacka;
			}
			/* V číslech jednacích v textu je obvykle poněkud více pomlček, lepší je tedy porovnat bez nich */
			sSpisovaZnacka = sSpisovaZnacka.Replace("-", String.Empty);

			iPosition = sSpisovaZnackaZkracene.IndexOf('/');
			sYear = sSpisovaZnackaZkracene.Substring(iPosition + 1, 4);
			XmlNodeList xNodes = pD.SelectNodes("link");
			/* do xn nastavíme first child html-textu */
			xn = pD.SelectSingleNode("//html-text").FirstChild;
			while (xn != null)
			{
				if (UtilityBeck.UtilityXml.IsEmptyNode(xn))
				{
					xn.Attributes.RemoveNamedItem("class");
					xn = xn.NextSibling;
					continue;
				}
				// odstranění odkazů na web
				xn2 = xn.FirstChild;
				while (xn2 != null)
				{
					if (xn2.Name.Equals("link") && xn2.Attributes["href"].Value.StartsWith("http"))
					{
						el = pD.CreateElement("span");
						el.InnerText = xn2.InnerText;
						if (xn2.Attributes.GetNamedItem("style") != null)
							el.SetAttribute("style", xn2.Attributes["style"].Value);
						xn2.ParentNode.InsertAfter(el, xn2);
						xn3 = xn2.NextSibling;
						xn2.ParentNode.RemoveChild(xn2);
						xn2 = xn3;
						continue;
					}
					xn2 = xn2.NextSibling;
				}
				s = xn.InnerText.ToLower();
				UtilityBeck.Utility.RemoveWhiteSpaces(ref s);
				if (s.Equals("odůvodnění") || s.Equals("odůvodnění:"))
					byloOduvodneni = true;
				else if (byloOduvodneni)
					zarovnatDoBloku = true;
				if (vzorPodpisDne.IsMatch(xn.InnerText))
				{
					xn.Attributes.RemoveNamedItem("style");
					a = pD.CreateAttribute("class");
					a.Value = "left";
					xn.Attributes.Append(a);
					bylVzorPodpis = true;
					if (zarovnatDoBloku)
					{
						zarovnatDoBloku = false;
						byloOduvodneni = false;
					}
				}

				#region oprava takto: a spisová značka na začátku
				if (++i < 15)       // co je za takto: bude na novém řádku
				{
					if (i < 5)
					{
						s3 = s.Replace("číslojednací", "");
						s3 = s3.Replace(":", "").Replace("-", String.Empty);
						if (s3.Equals(sSpisovaZnacka) || s3.Equals(sSpisovaZnackaZkracene))
						{
							xn2 = xn.NextSibling;
							xn.ParentNode.RemoveChild(xn);
							while (xn2 != null)
							{
								if (String.IsNullOrWhiteSpace(xn2.InnerText))
								{
									xn = xn2.NextSibling;
									xn2.ParentNode.RemoveChild(xn2);
									xn2 = xn;
								}
								else
								{
									xn = xn2;
									break;
								}
							}
						}
					}
					if (s.StartsWith("takto:"))
					{
						if (xnTakto != null)
						{
							xnTakto = null;
							xn = xn.NextSibling;
							continue;
						}
						if (!s.Equals("takto:"))
						{
							if (!xn.Name.Equals("p"))
							{
								a = pD.CreateAttribute("error");
								a.Value = "Zde by měl být jednoduchý odstavec ?";
								xn.Attributes.Append(a);
								continue;
							}
							if (xn.FirstChild.Attributes.GetNamedItem("style") != null)
								sStyle = xn.FirstChild.Attributes["style"].Value;
							else
								sStyle = null;
							iPosition = xn.InnerXml.IndexOf('>');
							iPosition = xn.InnerXml.IndexOf(':', iPosition + 5);
							sPart1 = xn.InnerXml.Substring(0, iPosition + 1).Trim() + "</b>";
							if (sStyle != null)
								sPart2 = "<span style=\"" + sStyle + "\">" + xn.InnerXml.Substring(iPosition + 1).Trim();
							else
								sPart2 = "<b>" + xn.InnerXml.Substring(iPosition + 1).Trim();
							xn.InnerXml = sPart1;
							a = pD.CreateAttribute("class");
							a.Value = "center";
							xn.Attributes.Append(a);
							el = pD.CreateElement("p"); // prázdný řádek
							xn.ParentNode.InsertAfter(el, xn);
							el = pD.CreateElement("p");
							el.InnerXml = sPart2;
							xn.ParentNode.InsertAfter(el, xn.NextSibling);
							xn = el;
							// nyní se to vezme znova od začátku, aby se spravil předchozí text
							xnTakto = xn;
							xn = pD.SelectSingleNode("//html-text").FirstChild;
							continue;
						}
					}
				}
				#endregion
				#region oprava pokračování
				if (rgPageNumber.IsMatch(xn.InnerText))
				{
					if (String.IsNullOrWhiteSpace(xn.NextSibling.InnerText))
						xn.ParentNode.RemoveChild(xn.NextSibling);
					xn2 = xn.PreviousSibling;
					xn3 = xn2.PreviousSibling;
					xn.ParentNode.RemoveChild(xn);
					if (String.IsNullOrWhiteSpace(xn2.InnerText))
					{
						xn2.ParentNode.RemoveChild(xn2);
						xn = xn3.NextSibling;
					}
					else
						xn = xn2.NextSibling;
					continue;
				}
				if (s.StartsWith("pokračování"))
				{
					if (s.Length < 35)
					{
						if (s.Length > 11)
							s3 = s.Substring(11).Replace("-", "");
						else
							s3 = "1";
						if (s3.Equals("1") || s3.Contains(sSpisovaZnackaZkracene))
						{
							if (Char.IsDigit(s3, s3.Length - 1))
							{
								if ((xn.NextSibling != null) && String.IsNullOrWhiteSpace(xn.NextSibling.InnerText))
									xn.ParentNode.RemoveChild(xn.NextSibling);
								xn2 = xn.PreviousSibling;
								xn3 = xn2.PreviousSibling;
								xn.ParentNode.RemoveChild(xn);
								if (String.IsNullOrWhiteSpace(xn2.InnerText))
								{
									xn2.ParentNode.RemoveChild(xn2);
									xn = xn3.NextSibling;
								}
								else
									xn = xn2.NextSibling;
								continue;
							}
							else
							{
								a = pD.CreateAttribute("error");
								a.Value = "Odstranit divné pokračování";
								xn.Attributes.Append(a);
							}
						}
						else
						{
							a = pD.CreateAttribute("error");
							a.Value = "Odstranit divné pokračování";
							xn.Attributes.Append(a);
						}
					}
				}
				else if (s.Contains("pokračování") && xn.Name.Equals("p"))
				{
					iPosition = xn.InnerXml.IndexOf("pokračování");
					if (iPosition == -1)
					{
						a = pD.CreateAttribute("error");
						a.Value = "Možná je zde text pokračování stránky";
						xn.Attributes.Append(a);
					}
					iPosition2 = iPosition + 11;
					iPosition2 = xn.InnerXml.IndexOf("/" + sYear);
					if (iPosition2 > 12)
					{
						this.SplitRowAccordingToTheText(ref pD, ref xn, "pokračování");
						xn = xn.PreviousSibling;
						continue;
					}
				}
				#endregion
				#region oprava předseda senátu
				if (xn.InnerText.EndsWith("předseda senátu"))
					this.SplitRowAccordingToTheText(ref pD, ref xn, "předseda senátu");
				else if (xn.InnerText.EndsWith("předseda zvláštního senátu"))
					this.SplitRowAccordingToTheText(ref pD, ref xn, "předseda zvláštního senátu");
				#endregion
				#region oprava sloučených řádku a), b), c)
				else if (xn.InnerText.Contains("a)") && !xn.InnerText.StartsWith("a)"))
				{
					xn2 = UtilityXml.FollowingNonEmptyNode(xn);
					if (xn2 == null)
					{
						a = pD.CreateAttribute("error");
						a.Value = "Divné";
						xn.Attributes.Append(a);
					}
					else if (xn2.InnerText.StartsWith("b)"))
						this.SplitRowAccordingToTheText(ref pD, ref xn, "a)");
				}
				else if (xn.InnerText.Contains("b)") && xn.InnerText.StartsWith("a)"))
					this.SplitRowAccordingToTheText(ref pD, ref xn, "b)");
				else if (xn.InnerText.Contains("c)") && xn.InnerText.StartsWith("b)"))
					this.SplitRowAccordingToTheText(ref pD, ref xn, "c)");
				else if (xn.InnerText.Contains("d)") && xn.InnerText.StartsWith("c)"))
					this.SplitRowAccordingToTheText(ref pD, ref xn, "d)");
				#endregion
				#region oprava sloučených řádků s body I., II., III., ...
				if (xn.InnerText.Contains("III.") && xn.InnerText.StartsWith("II."))
					this.SplitRowAccordingToTheText(ref pD, ref xn, "III.");
				else if (xn.InnerText.Contains("II.") && xn.InnerText.StartsWith("I."))
					this.SplitRowAccordingToTheText(ref pD, ref xn, "II.");
				else if (xn.InnerText.Contains("IV.") && xn.InnerText.StartsWith("III."))
					this.SplitRowAccordingToTheText(ref pD, ref xn, "IV.");
				else if (xn.InnerText.Contains("I."))
				{
					xn2 = UtilityXml.FollowingNonEmptyNode(xn);
					if ((xn2 != null) && xn2.InnerText.StartsWith("II."))
						this.SplitRowAccordingToTheText(ref pD, ref xn, "II.");
				}
				#endregion
				tt = UtilityBeck.UtilityXml.GetTypeOfItem(ref xn, ref sId, -1, true);
				#region oprava vadné class
				if (xn.Name.Equals("p") && (xn.Attributes.GetNamedItem("class") != null) && (zarovnatDoBloku || xnTakto != null))
					if (!xn.Attributes["class"].Value.Equals("ind1"))
					{
						if (sDruh == null)
							xn.Attributes.RemoveNamedItem("class");
						else if (!sDruh.Equals(s) && (!xn.Attributes["class"].Value.Equals("center") || (tt == TypyText.TEXT)))
							xn.Attributes.RemoveNamedItem("class");
					}
				#endregion
				#region sloučení rozdělených stránek
				bMerge = false;
				s2 = null;
				xn2 = xn.PreviousSibling;
				while (xn2 != null)
				{
					if (!UtilityBeck.UtilityXml.IsEmptyNode(xn2))
					{
						s2 = xn2.InnerText.ToLower();
						UtilityBeck.Utility.RemoveWhiteSpaces(ref s2);
						break;
					}
					xn2 = xn2.PreviousSibling;
				}
				if (!bylVzorPodpis && !s.Equals("odůvodnění:") && !s.Equals("takto:") && (xn2 != null))
				{
					if ((tt == TypyText.TEXT) && !String.IsNullOrEmpty(xn2.InnerText))
					{
						if ((xn2.Attributes.GetNamedItem("class") == null) || xn2.Attributes["class"].Value.Equals("ind1"))
						{
							if (xn2.InnerText.EndsWith("-") || xn2.InnerText.EndsWith(","))
								bMerge = true;
							else if (xn2.InnerText.EndsWith("§"))
								bMerge = true;
							else if (xn2.InnerText.EndsWith(" srov.") || xn2.InnerText.EndsWith("(srov."))
								bMerge = true;
							else if (Char.IsLower(xn2.InnerText, xn2.InnerText.Length - 1) || xn2.InnerText.EndsWith(" PK MPSV"))
								bMerge = true;
							else if (Char.IsNumber(xn2.InnerText, xn2.InnerText.Length - 1) && !vzorPodpisDne.IsMatch(xn2.InnerText) && !s.Equals("rozsudek") && !s.Equals("usnesení"))
								bMerge = true;
							else if ((xn2.InnerText.EndsWith(" č.") || xn2.InnerText.EndsWith(" odst.")) && Char.IsNumber(xn.InnerText, 0))
								bMerge = true;
						}
					}
					if (!bMerge)
					{
						if (s.StartsWith("s.ř.s."))
							bMerge = true;
						else if (s2.EndsWith("č.j."))
							bMerge = true;
						else if (xn2.InnerText.EndsWith(" zn."))
							bMerge = true;
						else if (rgMoney.IsMatch(xn.InnerText))
							bMerge = true;
						else if (rgYear.IsMatch(xn.InnerText) && vzorPulDatum.IsMatch(xn2.InnerText))
							bMerge = true;
						else if (rgMonthYear.IsMatch(xn.InnerText) && rgDayDate.IsMatch(xn2.InnerText))
							bMerge = true;
						else if (rgDateBegining.IsMatch(xn.InnerText) && xn2.InnerText.EndsWith("ze dne"))
							bMerge = true;
					}
				}
				if (bMerge)
				{
					if (xn2.Name.Equals("ml"))
					{
						xn3 = xn;
						xn = xn2;
						xn2 = xn2.LastChild;
						if (xn2.Name.Equals("li"))
						{
							xn2 = xn2.LastChild;
							if (UtilityXml.IsEmptyNode(xn2))
								xn2 = UtilityXml.PreviousNonEmptyNode(xn2);
						}
						xn2.LastChild.InnerText += " ";
						xn2.InnerXml += xn3.InnerXml;
						xn2 = xn.NextSibling;
						xn3 = xn3.NextSibling;
						do
						{
							xn4 = xn2.NextSibling;
							if (xn2.Attributes.GetNamedItem("error") != null)
								if (xn.Attributes.GetNamedItem("error") != null)
									xn.Attributes["error"].Value += " " + xn2.Attributes["error"].Value;
								else
								{
									a = pD.CreateAttribute("error");
									a.Value = xn2.Attributes["error"].Value;
									xn.Attributes.Append(a);
								}
							xn2.ParentNode.RemoveChild(xn2);
							xn2 = xn4;
						} while ((xn2 != null) && !xn2.Equals(xn3));
						continue;
					}
					else
					{
						xn2.LastChild.InnerText += " ";
						xn2.InnerXml += xn.InnerXml;
						xn3 = xn.NextSibling;
						xn = xn2;
						xn2 = xn2.NextSibling;
						do
						{
							xn4 = xn2.NextSibling;
							if (xn2.Attributes.GetNamedItem("error") != null)
								if (xn.Attributes.GetNamedItem("error") != null)
									xn.Attributes["error"].Value += " " + xn2.Attributes["error"].Value;
								else
								{
									a = pD.CreateAttribute("error");
									a.Value = xn2.Attributes["error"].Value;
									xn.Attributes.Append(a);
								}
							xn2.ParentNode.RemoveChild(xn2);
							xn2 = xn4;
						} while ((xn2 != null) && !xn2.Equals(xn3));
					}
				}
				if (tt == TypyText.SMALL_LETTER)    // odstraní se předchozí prázdné řádky
				{
					xn2 = xn.PreviousSibling;
					while ((xn2 != null) && UtilityXml.IsEmptyNode(xn2))
					{
						xn3 = xn2.PreviousSibling;
						xn2.ParentNode.RemoveChild(xn2);
						xn2 = xn3;
					}
				}
				#endregion
				#region nahrazení textu s vadnými mezerami
				if (s.Contains("seodmítá"))
				{
					xn2 = xn.FirstChild;
					while (xn2 != null)
					{
						s3 = xn2.InnerText;
						UtilityBeck.Utility.RemoveWhiteSpaces(ref s3);
						if (s3.Equals("seodmítá") && (xn2.Attributes.GetNamedItem("style") != null) && xn2.Attributes["style"].Value.Equals("font-weight:bold"))
						{
							xn2.InnerText = xn2.InnerText = "s e o d m í t á";
							if ((xn2.PreviousSibling != null) && !xn2.PreviousSibling.InnerText.EndsWith(" "))
								xn2.InnerText = " " + xn2.InnerText;
							if ((xn2.NextSibling != null) && !xn2.NextSibling.InnerText.StartsWith(" "))
								xn2.InnerText += " ";
							break;
						}
						xn2 = xn2.NextSibling;
					}
				}
				if (s.Contains("nemá"))
				{
					xn2 = xn.FirstChild;
					while (xn2 != null)
					{
						s3 = xn2.InnerText;
						UtilityBeck.Utility.RemoveWhiteSpaces(ref s3);
						if (s3.Equals("nemá") && (xn2.Attributes.GetNamedItem("style") != null) && xn2.Attributes["style"].Value.Equals("font-weight:bold"))
						{
							xn2.InnerText = "n e m á";
							if ((xn2.PreviousSibling != null) && !xn2.PreviousSibling.InnerText.EndsWith(" "))
								xn2.InnerText = " " + xn2.InnerText;
							if ((xn2.NextSibling != null) && !xn2.NextSibling.InnerText.StartsWith(" "))
								xn2.InnerText += " ";
							break;
						}
						xn2 = xn2.NextSibling;
					}
				}
				if (s.Contains("poučení:"))
				{
					xn2 = xn.FirstChild;
					while (xn2 != null)
					{
						s3 = xn2.InnerText;
						UtilityBeck.Utility.RemoveWhiteSpaces(ref s3);
						if (s3.Equals("Poučení:") && (xn2.Attributes.GetNamedItem("style") != null) && xn2.Attributes["style"].Value.Equals("font-weight:bold"))
						{
							xn2.InnerText = "P o u č e n í :";
							if ((xn2.PreviousSibling != null) && !xn2.PreviousSibling.InnerText.EndsWith(" "))
								xn2.InnerText = " " + xn2.InnerText;
							if ((xn2.NextSibling != null) && !xn2.NextSibling.InnerText.StartsWith(" "))
								xn2.InnerText += " ";
							break;
						}
						xn2 = xn2.NextSibling;
					}
				}
				if (s.Contains("senahrazují"))
				{
					xn2 = xn.FirstChild;
					while (xn2 != null)
					{
						s3 = xn2.InnerText;
						UtilityBeck.Utility.RemoveWhiteSpaces(ref s3);
						if (s3.Equals("senahrazují") && (xn2.Attributes.GetNamedItem("style") != null) && xn2.Attributes["style"].Value.Equals("font-weight:bold"))
						{
							xn2.InnerText = "s e n a h r a z u j í";
							if ((xn2.PreviousSibling != null) && !xn2.PreviousSibling.InnerText.EndsWith(" "))
								xn2.InnerText = " " + xn2.InnerText;
							if ((xn2.NextSibling != null) && !xn2.NextSibling.InnerText.StartsWith(" "))
								xn2.InnerText += " ";
							break;
						}
						xn2 = xn2.NextSibling;
					}
				}
				if (s.Contains("sepřiznává"))
				{
					xn2 = xn.FirstChild;
					while (xn2 != null)
					{
						s3 = xn2.InnerText;
						UtilityBeck.Utility.RemoveWhiteSpaces(ref s3);
						if (s3.Equals("sepřiznává") && (xn2.Attributes.GetNamedItem("style") != null) && xn2.Attributes["style"].Value.Equals("font-weight:bold"))
						{
							xn2.InnerText = "s e p ř i z n á v á";
							if ((xn2.PreviousSibling != null) && !xn2.PreviousSibling.InnerText.EndsWith(" "))
								xn2.InnerText = " " + xn2.InnerText;
							if ((xn2.NextSibling != null) && !xn2.NextSibling.InnerText.StartsWith(" "))
								xn2.InnerText += " ";
							break;
						}
						xn2 = xn2.NextSibling;
					}
				}
				if (s.Contains("senepřiznává"))
				{
					xn2 = xn.FirstChild;
					while (xn2 != null)
					{
						s3 = xn2.InnerText;
						UtilityBeck.Utility.RemoveWhiteSpaces(ref s3);
						if (s3.Equals("senepřiznává") && (xn2.Attributes.GetNamedItem("style") != null) && xn2.Attributes["style"].Value.Equals("font-weight:bold"))
						{
							xn2.InnerText = "s e n e p ř i z n á v á";
							if ((xn2.PreviousSibling != null) && !xn2.PreviousSibling.InnerText.EndsWith(" "))
								xn2.InnerText = " " + xn2.InnerText;
							if ((xn2.NextSibling != null) && !xn2.NextSibling.InnerText.StartsWith(" "))
								xn2.InnerText += " ";
							break;
						}
						xn2 = xn2.NextSibling;
					}
				}
				if (s.Contains("sezamítá"))
				{
					xn2 = xn.FirstChild;
					while (xn2 != null)
					{
						s3 = xn2.InnerText;
						UtilityBeck.Utility.RemoveWhiteSpaces(ref s3);
						if (s3.Equals("sezamítá") && (xn2.Attributes.GetNamedItem("style") != null) && xn2.Attributes["style"].Value.Equals("font-weight:bold"))
						{
							xn2.InnerText = "s e z a m í t á";
							if ((xn2.PreviousSibling != null) && !xn2.PreviousSibling.InnerText.EndsWith(" "))
								xn2.InnerText = " " + xn2.InnerText;
							if ((xn2.NextSibling != null) && !xn2.NextSibling.InnerText.StartsWith(" "))
								xn2.InnerText += " ";
							break;
						}
						xn2 = xn2.NextSibling;
					}
				}
				if (s.Contains("nejsou"))
				{
					xn2 = xn.FirstChild;
					while (xn2 != null)
					{
						s3 = xn2.InnerText;
						UtilityBeck.Utility.RemoveWhiteSpaces(ref s3);
						if (s3.Equals("nejsou") && (xn2.Attributes.GetNamedItem("style") != null) && xn2.Attributes["style"].Value.Equals("font-weight:bold"))
						{
							xn2.InnerText = "n e j s o u";
							if ((xn2.PreviousSibling != null) && !xn2.PreviousSibling.InnerText.EndsWith(" "))
								xn2.InnerText = " " + xn2.InnerText;
							if ((xn2.NextSibling != null) && !xn2.NextSibling.InnerText.StartsWith(" "))
								xn2.InnerText += " ";
							break;
						}
						xn2 = xn2.NextSibling;
					}
				}
				#endregion
				#region některé texty budou na střed a volné řádky okolo
				if (s.Equals("rozsudek") || s.Equals("rozhodnutí") || s.Equals("opravnéusnesení") || s.Equals("usnesení") || s.Equals("takto:") || s.Equals("odůvodnění:") || s.Equals("odůvodnění"))
				{
					if (!UtilityXml.IsEmptyNode(xn.NextSibling))
					{
						el = pD.CreateElement("p");
						xn.ParentNode.InsertAfter(el, xn);
					}
					switch (s)
					{
						case "takto:":
							if (!UtilityXml.IsEmptyNode(xn.PreviousSibling))
							{
								el = pD.CreateElement("p");
								xn.ParentNode.InsertBefore(el, xn);
							}
							xn.Attributes.RemoveNamedItem("style");
							//xn.InnerXml = "<span style=\"font-weight:bold\">t a k t o :</span>";
							xn.InnerXml = "<b>t a k t o :</b>";
							break;
						case "odůvodnění":
						case "odůvodnění:":
							if (!UtilityXml.IsEmptyNode(xn.PreviousSibling))
							{
								el = pD.CreateElement("p");
								xn.ParentNode.InsertBefore(el, xn);
							}
							xn.InnerXml = "<b>O d ů v o d n ě n í :</b>";
							break;
						case "usnesení":
							xn.InnerXml = "<b>U S N E S E N Í</b>";
							break;
						case "opravnéusnesení":
							xn.InnerXml = "<b>O P R A V N É U S N E S E N Í</b>";
							break;
						case "rozsudek":
							xn.InnerXml = "<b>R O Z S U D E K</b>";
							break;
						case "rozhodnutí":
							xn.InnerXml = "<b>R O Z H O D N U T Í</b>";
							break;
					}
					a = pD.CreateAttribute("class");
					a.Value = "center";
					xn.Attributes.Append(a);
				}
				#endregion
				xn = xn.NextSibling;
			}

			#region oprava zarovnání podpis
			if (bylVzorPodpis)
			{
				xn = pD.DocumentElement.LastChild.LastChild;
				if (xn.Attributes.GetNamedItem("class") != null)
				{
					s3 = xn.Attributes["class"].Value;
					while (xn != null)
					{
						if (vzorPodpisDne.IsMatch(xn.InnerText))
							break;
						if (!UtilityXml.IsEmptyNode(xn))
						{
							a = pD.CreateAttribute("class");
							a.Value = s3;
							xn.Attributes.Append(a);
						}
						xn = xn.PreviousSibling;
					}
				}
			}
			/* Poslední potomek html-text*/
			xn = pD.SelectSingleNode("//html-text").LastChild;
			if (!xn.Name.Equals("p"))
			{
				a = pD.CreateAttribute("error");
				a.Value = "Divná struktura";
				xn.Attributes.Append(a);
				return;
			}
			i = 0;
			while (i++ < 6)
			{
				iPosition = xn.InnerText.ToLower().LastIndexOf("předsedkyně senátu");
				if (iPosition > 0)
					this.SplitRowAccordingToTheText(ref pD, ref xn, "předsedkyně senátu");
				else
				{
					iPosition = xn.InnerText.ToLower().LastIndexOf("samosoudkyně");
					if (iPosition > 0)
						this.SplitRowAccordingToTheText(ref pD, ref xn, "samosoudkyně");
					else
					{
						iPosition = xn.InnerText.ToLower().LastIndexOf("samosoudce");
						if (iPosition > 0)
							this.SplitRowAccordingToTheText(ref pD, ref xn, "samosoudce");
						else
						{
							iPosition = xn.InnerText.ToLower().LastIndexOf("za správnost vyhotovení");
							if (iPosition > 0)
								this.SplitRowAccordingToTheText(ref pD, ref xn, "za správnost vyhotovení");
							else
							{
								iPosition = xn.InnerText.ToLower().LastIndexOf("předsedkyně kárného senátu");
								if (iPosition > 0)
									this.SplitRowAccordingToTheText(ref pD, ref xn, "předsedkyně kárného senátu");
							}
						}
					}
				}
				xn = xn.PreviousSibling;
			}
			#endregion
			this.CorrectionOfLists(ref pD);
		}

		private void SplitRowAccordingToTheText(ref XmlDocument pD, ref XmlNode pXn, string pTextNewLine)
		{
			if (pXn.Name.Equals("li") || pXn.Name.Equals("table") || pXn.Name.Equals("html-text"))
			{
				XmlAttribute a = pD.CreateAttribute("error");
				a.Value = "Obsahuje sloučený text";
				pXn.Attributes.Append(a);
				return;
			}
			if (!pXn.InnerText.ToLower().StartsWith(pTextNewLine) && !pXn.Name.Equals("ml"))
			{
				string sStyleSpan = null;

				if (pXn.FirstChild.Attributes != null)
				{
					if (pXn.FirstChild.Attributes.GetNamedItem("style") != null)
						sStyleSpan = pXn.FirstChild.Attributes["style"].Value;
					else
						sStyleSpan = null;
				}
				//int iPosition = pXn.InnerXml.ToLower().LastIndexOf(pTextNewLine);
				//if (iPosition > -1)
				//{
				//	sPart1 = pXn.InnerXml.Substring(0, iPosition).Trim() + "</span>";
				//	int iPosition2 = sPart1.LastIndexOf("<span");
				//	int iPosition3 = sPart1.LastIndexOf("<link");
				//	if (iPosition3 > iPosition2)
				//		return;
				//	sPart1 = sPart1.Replace("<span></span>", "");
				//	if (sStyleSpan != null)
				//		sPart2 = "<span style=\"" + sStyleSpan + "\">" + pXn.InnerXml.Substring(iPosition).Trim();
				//	else
				//		sPart2 = "<span>" + pXn.InnerXml.Substring(iPosition).Trim();
				//	pXn.InnerXml = sPart1;
				//	XmlElement el = pD.CreateElement("p");
				//	if (pXn.Attributes.GetNamedItem("class") != null)
				//	{
				//		XmlAttribute a = pD.CreateAttribute("class");
				//		a.Value = pXn.Attributes["class"].Value;
				//		el.Attributes.Append(a);
				//	}
				//	el.InnerXml = sPart2;
				//	pXn.ParentNode.InsertAfter(el, pXn);
				//}
			}
		}

		private void CorrectionOfLists(ref XmlDocument pD)
		{
			TypyText tt;
			string sId = null, sError = null, s, s1, s2;
			bool bWasList = false;
			XmlElement elP1, elP2;
			XmlAttribute a;
			XmlNode xn2, xn3, xn4, xnMl = null, xnFollowing, xnTr, xnTd;
			XmlNode xn = pD.DocumentElement.FirstChild.NextSibling.FirstChild;
			while (xn != null)
			{
				xnFollowing = xn.NextSibling;
				if (UtilityXml.IsEmptyNode(xn))
				{
					xn = xnFollowing;
					continue;
				}
				s = xn.InnerText.ToLower();
				Utility.RemoveWhiteSpaces(ref s);
				if (s.Equals("odůvodnění") || s.Equals("odůvodnění:"))      // tady to musíme přerušit, kdyby se vyskytly nadpisy s římskými číslicemi zarovnanými na střed
					break;
				#region oprava, zda-li p neobsahuje li a li neobsahuje více bodů
				if (xn.Name.Equals("p"))
				{
					xn2 = xn.FirstChild;
					while (xn2 != null)
					{
						if (xn2.Name.Equals("li"))
						{
							xn.ParentNode.InsertAfter(xn2, xn);
							xnFollowing = xn2;
							break;
						}
						xn2 = xn2.NextSibling;
					}
				}
				else if (xn.Name.Equals("li") || xn.Name.Equals("ml"))
				{
					if (xn.Name.Equals("li"))
						xn2 = xn.LastChild;
					else
						xn2 = xn.LastChild.LastChild;
					while ((xn2 != null) && (xn2.PreviousSibling != null) && !xn2.PreviousSibling.Name.Equals("name"))
					{
						xn3 = xn2.PreviousSibling;
						s = xn2.InnerText.ToLower();
						Utility.RemoveWhiteSpaces(ref s);
						if (UtilityXml.IsEmptyNode(xn2))
						{
							xn.ParentNode.InsertAfter(xn2, xn);
							xnFollowing = xn2;
						}
						else if (s.Equals("odůvodnění") || s.Equals("odůvodnění:"))
						{
							xn2.InnerXml = "<b>O d ů v o d n ě n í :</b>";
							xn.ParentNode.InsertAfter(xn2, xn);
							xnFollowing = xn2;
						}
						else
						{
							tt = UtilityBeck.UtilityXml.GetTypeOfItem(ref xn2, ref sId, -1, true);
							if (tt == TypyText.CAPITAL_ROMANIAN_NUMBER)
							{
								xn.ParentNode.InsertAfter(xn2, xn);
								xnFollowing = xn2;
							}
						}
						xn2 = xn3;
					}
				}
				#endregion
				sId = null;
				tt = UtilityBeck.UtilityXml.GetTypeOfItem(ref xn, ref sId, -1, true);
				if (tt == TypyText.CAPITAL_ROMANIAN_NUMBER)
				{
					if (xn.Name.Equals("ml"))       // přesuneme pouze poslení volný řádek
					{
						xn2 = xn.LastChild.LastChild;
						if (UtilityXml.IsEmptyNode(xn2))
							xn.ParentNode.InsertAfter(xn2, xn);
						else
						{
							s = xn2.InnerText.ToLower();
							Utility.RemoveWhiteSpaces(ref s);
							if (s.Equals("odůvodnění") || s.Equals("odůvodnění:"))
							{
								xn.ParentNode.InsertAfter(xn2, xn);
								xnFollowing = xn2;
							}
						}
						if (!bWasList)
							xnMl = xn;
						else        // první bod je již v ml
						{
							xn2 = xn.FirstChild;
							while (xn2 != null)
							{
								xn3 = xn2.NextSibling;
								xnMl.AppendChild(xn2);
								xn2 = xn3;
							}
							if (UtilityXml.IsEmptyNode(xn))
							{
								xn.ParentNode.RemoveChild(xn);
								xn = xnFollowing;
								continue;
							}
						}
						bWasList = true;
					}
					else if (xn.Name.Equals("p"))
					{
						xn.Attributes.RemoveNamedItem("class");
						if (!bWasList)
						{
							bWasList = true;
							xnMl = pD.CreateElement("ml");
							xn.ParentNode.InsertBefore(xnMl, xn);
						}
						UtilityXml.CreateLiItem(ref pD, ref xnMl, xn, sId, -1, false, ref sError);
					}
					else if (xn.Name.Equals("li"))
					{
						if (!bWasList)
						{
							bWasList = true;
							xnMl = pD.CreateElement("ml");
							xn.ParentNode.InsertBefore(xnMl, xn);
							xnMl.AppendChild(xn);
						}
						else
							xnMl.AppendChild(xn);
					}
					else if (xn.Name.Equals("table"))
					{
						xnFollowing = xn.PreviousSibling;
						xnTr = xn.FirstChild;
						while (xnTr != null)
						{
							xn2 = xnTr;
							elP1 = pD.CreateElement("p");
							elP2 = pD.CreateElement("p");
							xnTd = xnTr.FirstChild;
							while (xnTd != null)
							{
								xn3 = xnTd.FirstChild;
								while (xn3 != null)
								{
									xn4 = xn3.NextSibling;
									if (UtilityXml.IsEmptyNode(xn3))
										xnTd.RemoveChild(xn3);
									xn3 = xn4;
								}
								if (xnTd.ChildNodes.Count > 0)
								{
									elP1.InnerXml += xnTd.FirstChild.InnerXml;
									elP1.LastChild.InnerText += " ";
									if (xnTd.FirstChild.NextSibling != null)
									{
										elP2.InnerXml += xnTd.FirstChild.NextSibling.InnerXml;
										elP2.LastChild.InnerText += " ";
									}
									if (xnTd.ChildNodes.Count > 2)
									{
										a = pD.CreateAttribute("error");
										a.Value = "Opravit !!";
										xnTr.Attributes.Append(a);
									}
								}
								xnTd = xnTd.NextSibling;
							}
							if (!String.IsNullOrWhiteSpace(elP2.InnerText))
								elP1.InnerXml += elP2.InnerXml;
							if (elP1.LastChild != null)
								elP1.LastChild.InnerText = elP1.LastChild.InnerText.TrimEnd();
							xn.ParentNode.InsertBefore(elP1, xn);
							s1 = xnTr.InnerText;
							Utility.RemoveWhiteSpaces(ref s1);
							xnTr = xnTr.NextSibling;
							if (xn2.Attributes.GetNamedItem("error") == null)
							{
								s2 = elP1.InnerText;
								Utility.RemoveWhiteSpaces(ref s2);
								if (s1.Length != s2.Length)
								{
									a = pD.CreateAttribute("error");
									a.Value = "1";
									xn2.Attributes.Append(a);
								}
								else
									xn.RemoveChild(xn2);
							}
						}
						if (UtilityXml.IsEmptyNode(xn))
							xn.ParentNode.RemoveChild(xn);
						else
							xnFollowing = xn.NextSibling;
					}
					else
					{
						a = pD.CreateAttribute("error");
						a.Value = "1";
						pD.DocumentElement.Attributes.Append(a);
					}
				}
				else if (bWasList)      // jsme za seznamem, skončíme
					break;
				xn = xnFollowing;
			}
		}

		private void NSSSelectFilesButton_Click(object sender, EventArgs e)
		{
			DialogResult result = NSSFileWithNSSDialog.ShowDialog();
		}

		private static volatile bool IsRunningFromFile = false;
		private static int FileDocumentCount;
		private ConcurrentDictionary<string, string> FileDocumentIds = new ConcurrentDictionary<string, string>();
		private readonly object locker = new object();
		void UpdateIsRunningFromFile(bool value)
		{
			lock (locker)
			{
				IsRunningFromFile = value;
			}
		}


		private bool RunFile()
		{
			this.aktuaniNSSAkce = NSSAkce.nssaVyhledaniDat;

			NSS_seznamElementuKeZpracovani = new BlockingCollection<HtmlAgilityPack.HtmlNode>(100);
			NSS_seznamCastecnychVysledku = new BlockingCollection<XmlDocument>(30);
			NSS_seznamPdfKeStazeni = new BlockingCollection<PdfToDownload>(50);

			NSS_SpustVlaknoProZpracovaniElementu();
			NSS_SpustVlaknoProZpracovaniMezivysledku();
			NSS_SpustVlaknoProZpracovaniPdf();

			browser.Navigate(NSS_INDEX);
			return true;
		}
	}
}