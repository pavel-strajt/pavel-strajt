using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Collections.Concurrent;
using System.Data.SqlClient;
using System.Threading.Tasks;
using System.Xml;
using System.Net;
using System.Text.RegularExpressions;
using System.IO;

namespace DataMiningCourts
{
    public partial class FrmCourts : Form
    {
        //<judikatura quality="4" VersionLink="0" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="file://Squirrel/Programy/Xsd/Beck-praha-general.xsd">
        //    <hlavicka-judikatura>
        //        <autor>
        //            <item>Úřad pro ochranu hospodářšké soutěže</item>
        //        </autor>
        //        <citace/><!-- Rozhodnutí: -->
        //        <datschvaleni/><!-- Datum nabytí právní moci -->
        //        <druh>Rozhodnutí</druh>
        //    </hlavicka-judikatura>
        //    <judikatura-section id-block="js1" quality="17">
        //        <header-j>
        //            <casopis>Sbírka rozhodnutí ÚOHS</casopis>
        //            <citace/><!-- ve tvaru pořadové číslo/rok Úohs -->
        //      <id-external/> <!--Cizí id dokumentu -->
        //            <info1/><!-- Instance -->
        //            <info3/><!-- Typ správního řízení -->
        //            <info4xml>
        //                <item></item><!-- rozhodnutí -->
        //            </info4xml>
        //            <info5xml/><!-- Účastníci, co položka, to jeden item -->
        //            <titul/><!-- Věc -->
        //        </header-j>
        //        <html-text/>
        //    </judikatura-section>
        //</judikatura>

        private static string UOHS_INDEX = @"http://www.uohs.cz/cs/%TYP%/sbirky-rozhodnuti.html";

        private string ActualIndex
        {
            get
            {
                return UOHS_INDEX.Replace("%TYP%", this.UOHS_Iteration[this.UOHS_ActualIterationNumber].Item1);
            }
        }

        private static string UOHS_RESULT_PREFIX = @"http://www.uohs.cz";

        private static string UOHS_RESULTS_PREFIX = @"http://www.uohs.cz/cs/%TYP%/sbirky-rozhodnuti";

        private string ActualPrefix
        {
            get
            {
                return UOHS_RESULTS_PREFIX.Replace("%TYP%", this.UOHS_Iteration[this.UOHS_ActualIterationNumber].Item1);
            }
        }

        private static string UOHS_ENTITIES_REGEX_PATTERN = @"^Nalezeno (\d+) rozhodnutí\.$";

        private static int UOHS_RESULT_NUMBER_IN_PAGE = 10;

		/// <summary>
		/// Velikost bufferu blokující kolekce shromaždující odkazy.
		/// Větší velikost = (větší) propustnost a paměťové nároky
		/// </summary>
		private static readonly int UOHS_BUFFER_SIZE_HREF = 1000;

		/// <summary>
		/// Velikost bufferu blokující kolekce shromaždující stažené dokumenty.
		/// Větší velikost = (větší) propustnost a paměťové nároky
		/// </summary>
		private static readonly int UOHS_BUFFER_SIZE_DOCUMENT = 50;

        private Tuple<string, string>[] UOHS_Iteration;

		/// <summary>
		/// XParh Elementu obsahující samotný text rozhodnutí v rámci stažené stránky
		/// </summary>
		private static string PAGE_CONTENT_XPATH = @"//div[@id='pagecont']/div[@id='page']/div[@id='content']/div[@id='text_l']/div[@class='res_text']";
        
        /// <summary>
        /// Web je udělaný hloupě,
        /// všechny typy rozhodnutí nelze získat v jedné iteraci, proto je potřeba uchovávat aktuální iteraci
        /// </summary>
        private int UOHS_ActualIterationNumber;

        /// <summary>
        /// Akce určující chování webBrowseru
        /// </summary>
        private enum UOHSAction { uhosaFindingData, uhosaFirstSavingOfHref, uhosaSavingHref };

        private UOHSAction actualUOHSAction;

        /// <summary>
        /// Na jaké aktuální stránce v zobrazování požadovaných výsledků se nacházíme
        /// </summary>
        private int UOHS_actualPageToProcess;

        /// <summary>
        /// Kolik stránek s výsledky je celkem
        /// </summary>
        private int UOHS_totalPagesToProcess;
        
        /// <summary>
        /// Kolik záznamů (výsledků) vyhledávání je celkem za všechny iterace
        /// </summary>
        private int UOHS_XML_totalRecordsToProcess;

        private int UOHS_XML_totalRecordsToProcessIteration;

        /// <summary>
        /// Jaký záznam (XML) aktuálně zpracováváme (kvůli progress baru)
        /// </summary>
        private int UOHS_XML_actualRecordToProcess;

		private CitationService UOHS_citationService;

		Regex UOHS_regDetailNoParser = new Regex(@"detail-(\d+).html");

        /// <summary>
        /// Seznam webových stránek s rozhodnutími ke stažení
        /// Stránky jsou stahovány až v následujícím kroku (UOHS_SpustVlaknoProStazeniRozhodnuti)
        /// </summary>
        BlockingCollection<string> UOHS_listOfHrefsToDownload;


        /// <summary>
        /// Seznam webových stránek se staženými rozhodnutími
        /// Rozhodnutí jsou generována až v následujícím kroku (UOHS_SpustVlaknoProZpracovaníRozhodnuti)
        /// </summary>
        BlockingCollection<HtmlAgilityPack.HtmlDocument> OUHS_listOfDecisionsToProcess;

        /// <summary>
        /// Web client pro stahování souborů rozhodnutí, jeden na celý program...
        /// </summary>
        private static TimeoutWebClient clientVerdictDownload = new TimeoutWebClient(5000);

        /// <summary>
        /// Funkce, která spustí vlákno, které vybírá ze seznamu OUHS_seznamRozhodnutiKeZpracovani
        /// Data takto vybraná zpracuje:
        /// Načte webovou stránku pro konrétní dokument, který uloží do dalšího seznamu
        /// </summary>
        private void UOHS_LaunchThreadToDownloadDecision()
        {
            Task.Factory.StartNew(() =>
            {
                string urlToDownload = null;
                while (!UOHS_listOfHrefsToDownload.IsCompleted)
                {
                    // dokud je co brát... (aktivní blokování)
                    while (!UOHS_listOfHrefsToDownload.IsCompleted && !UOHS_listOfHrefsToDownload.TryTake(out urlToDownload, 2000)) ;

                    if (urlToDownload != null)
                    {
                        clientVerdictDownload.Encoding = Encoding.UTF8;
                        // zkusím stáhnout dokument rozhodnutí
                        HtmlAgilityPack.HtmlDocument rozhodnutiDoc = new HtmlAgilityPack.HtmlDocument();
                        try
                        {
                            Uri uriToDownload = new Uri(urlToDownload, UriKind.Absolute);
                            byte[] b = clientHeaderDownload.DownloadData(uriToDownload);
                            String s = System.Text.Encoding.UTF8.GetString(b);
                            rozhodnutiDoc.LoadHtml(s);
                        }
                        catch (WebException ex)
                        {
                            WriteIntoLogCritical(String.Format("Data dokumentu se z webové stránky {0} nepodařilo stáhnout.{1}\t[{2}]", urlToDownload, Environment.NewLine, ex.Message));
                            // XML prostě nebudu řešit...
                            --UOHS_XML_totalRecordsToProcess;
                            continue;
                        }

                        /* Uložím do wd */
                        string documentName = urlToDownload.Substring(urlToDownload.LastIndexOf('/') + 1);
                        string fullPathWithExtension = String.Format(@"{0}\{1}", this.txtWorkingFolder.Text, documentName);
                        rozhodnutiDoc.Save(fullPathWithExtension, Encoding.UTF8);

                        /* přidám id do dokumentu, které je rovno číslu dokumentu v UOHS db */
                        Match matchCislo = UOHS_regDetailNoParser.Match(documentName);
                        if (matchCislo.Success)
                        {
                            rozhodnutiDoc.DocumentNode.Attributes.Add("id", matchCislo.Groups[1].Value);
                        }
                        /* Předám do dalšího seznamu ke zpracování*/
                        OUHS_listOfDecisionsToProcess.Add(rozhodnutiDoc);       
                    }
                }

                // Už nebudu přidávat žádné html k transformaci...
                OUHS_listOfDecisionsToProcess.CompleteAdding();
            });
        }

        /// <summary>
        /// Funkce, která transformuje obsah hlavičky staženého html dokumentu do výstupního xml dokumentu
        /// </summary>
        /// <param name="pInput"></param>
        /// <param name="pOutput"></param>
        /// <returns>Název výstupního souboru, pokud je dokument v pořádku, jinak prázdný řetězec</returns>
        private string TransformHtmlHeaderToXml(HtmlAgilityPack.HtmlDocument pInput, XmlDocument pOutput)
        {
            /* Získám číslo jednací z elementu h1 */
            HtmlAgilityPack.HtmlNode divTextL = pInput.DocumentNode.SelectSingleNode("//div[@id='text_l']");
            HtmlAgilityPack.HtmlNode h1ReferenceNumber = divTextL.SelectSingleNode("./h1");
			/* Z obsahu textu vypreparuji samotné spisové značky; Vše za první mezerou + trim */
			//string sReferenceNumber = h1ReferenceNumber.InnerText.Substring(h1ReferenceNumber.InnerText.IndexOf(' ')).Trim();
			// <h1>číslo jednací: <strong>06851/2020/321/ZSř</strong><br>spisová značka: <strong>R0004/2020/VZ</strong></h1>
			string sReferenceNumber = h1ReferenceNumber.InnerText;
			int iPosition = sReferenceNumber.IndexOf("spisová značka:");
			if (iPosition > -1)
				sReferenceNumber = sReferenceNumber.Substring(iPosition + 15);
			else
			{
				iPosition = sReferenceNumber.IndexOf("číslo jednací:");
				if (iPosition > -1)
					sReferenceNumber = sReferenceNumber.Substring(iPosition + 14);
			}

			sReferenceNumber = sReferenceNumber.Trim();

			/* V opačném případě bude dokument vytvořen; nastavím element citace*/
			pOutput.DocumentElement.FirstChild.SelectSingleNode("./citace").InnerText = sReferenceNumber;
            /* vyzvednu předané id dokumentu */
			string foreignId = pInput.DocumentNode.Attributes["id"].Value;
            pOutput.DocumentElement.SelectSingleNode("//id-external").InnerText = foreignId;
            /* Vyzvednu typ řízení */
            //pOutput.DocumentElement.FirstChild.SelectSingleNode("./info??").InnerText = pInput.DocumentNode.SelectSingleNode("//span[@class='section']").InnerText;

            /* Získám hlavičku rozhodnutí reprezentovanou elementem table id="resolution_detail*/
            HtmlAgilityPack.HtmlNodeCollection tableResolutionDetailRows = divTextL.SelectNodes("./table[@id='resolution_detail']//tr");
            /* Obsah hlavičky tabulky
                <tr>
                    <th>Instance</th>
                    <td>I.</td>
                </tr>
                <tr>
                    <th>Věc</th>
                    <td>Spojení soutěžitelů Benteler Distribution Czech Republic, spol. s r.o., a BMB OCEL s.r.o.</td>
                </tr>
                ...
             */

            // formát souboru => J_RRRR_NSS_číslo
            string sResultFileName = String.Empty, sDecisionType = null;
			int iYear = -1;
            foreach (HtmlAgilityPack.HtmlNode hnDecisionParametr in tableResolutionDetailRows)
            {
                /* Příklad jednoho parametru:
                    <tr>
                        <th>Instance</th>
                        <td>I.</td>
                    </tr>
                 */
                /* FirstChild, ale více odolné na drobné změny */
                string sDecisionParametrName = hnDecisionParametr.SelectSingleNode("./th").InnerText;
                switch (sDecisionParametrName)
                {
                    case "Instance":
                        string instance = hnDecisionParametr.SelectSingleNode("./td").InnerText;
                        pOutput.DocumentElement.SelectSingleNode("//info1").InnerText = instance;
                        break;

                    case "Věc":
                        string vec = ClearStringFromSpecialHtmlChars(hnDecisionParametr.SelectSingleNode("./td").InnerText);
                        pOutput.DocumentElement.SelectSingleNode("//titul").InnerText = vec;
                        break;

                    case "Účastníci":
                        /* položky seznamu li přetavím do položek typu item*/
                        HtmlAgilityPack.HtmlNodeCollection seznamUcastniku = hnDecisionParametr.SelectNodes(".//li");
                        /* Co účastník, to položka typu item */
                        XmlNode info5xml = pOutput.DocumentElement.SelectSingleNode("//info5xml");
                        foreach (HtmlAgilityPack.HtmlNode ucastnik in seznamUcastniku)
                        {
                            string ucastnikText = ClearStringFromSpecialHtmlChars(ucastnik.InnerText);
                            info5xml.InnerXml += String.Format("<item>{0}</item>", ucastnikText);
                        }
                        break;

                    case "Typ správního řízení":
                        string typSR = hnDecisionParametr.SelectSingleNode("./td").InnerText;
                        pOutput.DocumentElement.SelectSingleNode("//info3").InnerText = typSR;
                        break;

                    case "Rok":
                        string sYear = hnDecisionParametr.SelectSingleNode("./td").InnerText;
						iYear = Int32.Parse(sYear);                  
                        break;

                    case "Datum nabytí právní moci":
                        // Vyzvednu datum, smažu z něj pevné mezery
                        string datumNPM = hnDecisionParametr.SelectSingleNode("./td").InnerText;
						string dateApprovalUniFormat = UtilityBeck.Utility.ConvertDateIntoUniversalFormat(datumNPM);
						pOutput.DocumentElement.SelectSingleNode("//datschvaleni").InnerText = dateApprovalUniFormat;
						DateTime dApproval = DateTime.Parse(dateApprovalUniFormat);
                        /* Navíc provedu kontrolu toho, jestli se už daná spisová značka nenachází v DB*/
						/* Zkontroluji, zda-li už takové číslo jednací nemám v DB; Pokud ano, dále nezpracovávám */
						
						if (UOHS_citationService.ReferenceNumberIsAlreadyinDb(dApproval, sReferenceNumber))
						{
							WriteIntoLogDuplicity(String.Format("Znacka [{0}] s daným datem rozhodnutí [{1}] je v jiz databazi!", sReferenceNumber, dApproval.ToShortDateString()));
							// dokument již máme
							return String.Empty;
						}
						break;

                    case "Typ rozhodnutí":
                        sDecisionType = hnDecisionParametr.SelectSingleNode("./td").InnerText;
                        pOutput.DocumentElement.SelectSingleNode("//info4xml/item").InnerText = sDecisionType;
                        break;
                }
            }

			/* Year has been fetched from the document*/
			if (iYear != -1)
			{
				int UOHS_cisloDBPoslednihoZpracovanehoDokumentu = UOHS_citationService.GetNextCitation(iYear);
				string citace = String.Format("ÚOHS {0}/{1}", UOHS_cisloDBPoslednihoZpracovanehoDokumentu, iYear);
				pOutput.DocumentElement.SelectSingleNode("./judikatura-section/header-j/citace").InnerText = citace;
				UtilityBeck.Utility.CreateDocumentName("J", sReferenceNumber, iYear.ToString(), out sResultFileName);
				if (String.IsNullOrEmpty(sResultFileName))
				{
					/* In case, that the CreateDocumentName function would return an emty string*/
					UOHS_citationService.RevertCitationForAYear(iYear);
				}
			}

			// set zakladni predpis
			if (!String.IsNullOrEmpty(sDecisionType))
			{
				XmlNode xnZaklPredpis = pOutput.DocumentElement.FirstChild.SelectSingleNode("./zakladnipredpis");
				if (xnZaklPredpis == null)
				{
					XmlElement el = pOutput.CreateElement("zakladnipredpis");
					pOutput.DocumentElement.FirstChild.AppendChild(el);
					xnZaklPredpis = el;
				}
				if (sDecisionType.Contains("§") && sDecisionType.Contains("zák"))
				{
                    LinkingHelper.AddBaseLaws(pOutput, sDecisionType, xnZaklPredpis);
				}
			}

            // Pokračuj v generování dokumentu
            return sResultFileName;
        }

        /// <summary>
        /// Kontrola, zda-li jsou všechny hodnoty zadané správně
        /// </summary>
        /// <returns>chybová hláška</returns>
        private string UOHS_CheckFilledValues()
        {
            StringBuilder sbResult = new StringBuilder();

            if (String.IsNullOrWhiteSpace(this.txtWorkingFolder.Text))
            {
                sbResult.AppendLine("Pracovní složka (místo pro uložení surových dat) musí být vybrána.");
            }

            return sbResult.ToString();
        }

        private void TransformHtmlDetailBeforeExport(HtmlAgilityPack.HtmlDocument pDocument)
        {
            /* Zachovám
			 * element head
			 * element body
			 *	element div id=pagecont
			 *		element div id=page
			 *			element div id=content
			 *				element div id=text_l
			 *	Zbytek smazu
			 */
			
			List<HtmlAgilityPack.HtmlNode> listNodesToDelete = new List<HtmlAgilityPack.HtmlNode>();
			HtmlAgilityPack.HtmlNode nodeBody = pDocument.DocumentNode.SelectSingleNode(@"//body");
			HtmlAgilityPack.HtmlNode nodeContent = pDocument.DocumentNode.SelectSingleNode(PAGE_CONTENT_XPATH);

			if (nodeContent == null)
			{
				/* Struktura dokumentu je celá špatně (např toho času VO II/S101/99) 
				 - 1) Zahlásím chybovou hlášku
				 - 2) Obsah dokumentu bude "prázdno"
				 - 3) Ukončím fci
				 */
				/* 1 */
				WriteIntoLogCritical(String.Format("[{0}]: Struktura dokumentu neodpovídá předpokládané struktuře... Vytvářím prázdný dokument", pDocument.DocumentNode.Attributes["id"].Value));
				/* 2 */
				nodeBody.InnerHtml = "<p error=\"ChybnaStrukturaVstupnihoHtml\"></p>";
				/* 3 */
				return;
			}

			/*
			 * Uzel, který reprezentuje hlavičku rozhodnutí ve tvaru
			 * spisová značka, dne
			 * Toto není součástí výsledného dokumentu, a proto je uzel přidán do seznamu uzlů ke smazání
			 * 
			 * Bohužel formát dokumentů se liší rok od roku, takže se nedá spolehnout na některý z
			 * atributů. Zdá se však, že největší množina elementů je pokryta pokud jsou vymazány
			 * všechny elementy až do prvního NEPRÁZDNÉHO elementáu <p>
			 */
			int iDeletedInPageHeader = 0;
			foreach (HtmlAgilityPack.HtmlNode p in nodeContent.ChildNodes)
			{
				if (p.Name == "p" &&
					!String.IsNullOrWhiteSpace(p.InnerText))
				{
					break;
				}

				++iDeletedInPageHeader;
				listNodesToDelete.Add(p);
			}

			if (iDeletedInPageHeader == 0)
			{
				/* Zaloguju chybu */
				WriteIntoLogCritical(String.Format("[{0}]: Hlavička dokumentu nenalezena ... a nesmazána", pDocument.DocumentNode.Attributes["id"].Value));
			}
			
			/* Smažu všechny uzly p, které se vyskytují za slovem "Obdrží"
			 * Procházím všechny, protože chci mít jistotu neproházení pořadí
			 * Nevyberu jen elementy jména p dtto
			 */
			bool bDeleteAll = false;
			foreach (HtmlAgilityPack.HtmlNode p in nodeContent.ChildNodes)
			{
				if (p.Name != "p")
				{
					continue;
				}

				string obsah = ClearStringFromSpecialHtmlChars(p.InnerText);
				/* Nalezl jsem klíčové slovo*/
				if (obsah == "Obdrží:" || obsah == "Obdrží")
				{
					bDeleteAll = true;
				}

				/* Mazu*/
				if (bDeleteAll)
				{
					listNodesToDelete.Add(p);
				}
			}

			if (!bDeleteAll)
			{ 
				/* Zalogovat chybu */
				WriteIntoLogCritical(String.Format("[{0}]: Patička (Obdrží:...) dokumentu nenalezena ... a nesmazána", pDocument.DocumentNode.Attributes["id"].Value));
			}

			foreach (HtmlAgilityPack.HtmlNode d in listNodesToDelete)
			{
				d.ParentNode.RemoveChild(d);
			}

			/* Upravim content */
			nodeBody.InnerHtml = nodeContent.OuterHtml;
        }

        private bool ConvertHrefsIntoFootnotes(string pDocumentName)
        {
            string sFullPathToResultFolder = String.Format(@"{0}\{1}", this.txtOutputFolder.Text, pDocumentName);
            string sFullPathToResultFile = String.Format(@"{0}\{1}.xml", sFullPathToResultFolder, pDocumentName);

            bool result = true;
            /* Obsahuje odkazy na elementy reprezentující (budoucí) poznámky v textu => noteref */
            Dictionary<int, List<XmlNode>> noterefs = new Dictionary<int, List<XmlNode>>();
            /* Obsahuje odkazy na elementy reprezentující (budoucí) poznámky pod čarou (note)
             * Vzhledem k principu vytváření dokumentů UOHS jsou to všechny poslední zmínky dané poznámky
             */
            Dictionary<int, XmlNode> notes = new Dictionary<int, XmlNode>();

            XmlDocument doc = new XmlDocument();
            // Nejproblematičtější dokument, nakonci v cyklu!
            doc.Load(sFullPathToResultFile);
            
            // načtu všechny odkazy
            XmlNodeList xAllHrefs = doc.SelectNodes("//a[@href]");
            // projdu odkazy a vložím odkazy, které jsou ve skutečnosti poznámky pod čarou
            // => jejich obsah je [XX], kde XX je zpravidla číslo 
            //(IMO nemusí platit na sto, ALE pro jednoduchost to tak beru! Dá se použít SortedDictionary s IComparer metodou pro složitější typy poznámek...)
            //  http://msdn.microsoft.com/cs-cz/library/a045f865(v=vs.110).aspx
            Regex regRealFootnote = new Regex(@"^\[(\d+)\]$");
            foreach (XmlNode xnHref in xAllHrefs)
            {
                // otestuj, zda-li se jedná o faktickou poznámku pod čarou
                string sHrefContent = xnHref.InnerText.Trim();
                Match matchRealFootnote = regRealFootnote.Match(sHrefContent);
                if (matchRealFootnote.Success)
                {
                    // je to ono (nejspíš)
                    int iNotes = Int32.Parse(matchRealFootnote.Groups[1].Value);
                    // nejprve jsou poznámky v textu a až na konci! jsou poznámky
                    // Pokud už existuje poznámka, tak to ve skutečnosti byl odkaz na poznámku, tzn:
                    // přendat poslední poznámku jako odkaz na poznámku, tento uzel "je" poznámka
                    if (notes.ContainsKey(iNotes))
                    {
                        noterefs[iNotes].Add(notes[iNotes]);
                        notes[iNotes] = xnHref;
                    }
                    else
                    {
                        // Poznámka pod čarou neexistuje:
                        // Pokud neexistuje odkaz v textu, je to poznámka v textu jinak poznámka pod čarou
                        if (!noterefs.ContainsKey(iNotes))
                        {
                            // je to poznámka v textu
                            List<XmlNode> notesWithTheId = new List<XmlNode>();
                            notesWithTheId.Add(xnHref);
                            noterefs.Add(iNotes, notesWithTheId);
                        }
                        else
                        {
                            // existuje odkaz v textu, ale zároveň neexistuje poznámka na konci
                            // => přidám poznámku na konci
                            notes.Add(iNotes, xnHref);
                        }
                    }
                }
            }

            /* Situace:
             * Mám v dokumentu zjištěné všechny poznámky v textu a všechny poznámky na konci, tzn zbývá mi jen provést transformaci
             * Transformace na note a na noteref je různá.
             * 
             * NOTEREF:
             * <a href="Priloha_J_2013_UOHS_5-p20">[XX]</a>
             * =>
             * <noteref href="fnXX" />
             * 
             * NOTE:
             * <p>
             *  <a href="Priloha_J_2013_UOHS_5-p155">[XX]</a>
             *  <span> Viz příloha č. 8 stanoviska, které účastník řízení Úřadu zaslal v reakci na sdělení výhrad.</span>
             * </p>
             * =>
             * <note id-block="fnXX">
             *  <name>XX</name>
             *  <p>
             *      <span> Viz příloha č. 8 stanoviska, které účastník řízení Úřadu zaslal v reakci na sdělení výhrad.</span>
             *  </p>
             * </note>
             * +LINKOVÁNÍ?
             */
            /* note typů a noteref typů by mělo být ideálně stejně... */
            if (noterefs.Count != notes.Count)
            {
                /* Zahlas chybu - (ale pokračuj) */
                this.WriteIntoLogCritical(String.Format("[{0}]: Počet odkazů na poznámek v textu[{1}] se liší od počtu poznámek pod čarou [{2}]", pDocumentName, noterefs.Count, notes.Count));
                result = false;
            }

            if (notes.Count > 0)
            {
                XmlNode xnNotes = doc.CreateElement("notes");
                /* Vložení notes za html-text */
                XmlNode nodeHtmlText = doc.SelectSingleNode("//html-text");
                /* html-text má rodiče, takže vložení za něj lze realizovat i takto: */
                nodeHtmlText.ParentNode.InsertAfter(xnNotes, nodeHtmlText);

                /* Do poznámek (noteref) jde převést jen to, co mám jako (note)*/
                foreach (int iNoteToConvert in notes.Keys)
                {
                    /*NOTE: */
                    /* Z uzlu notes (který má být transformován na element note)
                     * Odstraním element <a> = odkaz a jeho rodiče vložím do těla toho
                     * nového elementu note
                     */
                    XmlNode xnNoteToConvert = notes[iNoteToConvert];
                    XmlNode parentNodeNoteToConvert = xnNoteToConvert.ParentNode; /* Měl by být p*/
                    if (parentNodeNoteToConvert.Name != "p")
                    {
                        // zahlas chybu a pokračuj
                        this.WriteIntoLogCritical(String.Format("[{0}]: Poznámku [{1}] nelze převést, protože její rodič není element p!", pDocumentName, iNoteToConvert));
                        result = false;
                        continue;
                    }
                    /* Smažu <a> */
                    parentNodeNoteToConvert.RemoveChild(xnNoteToConvert);


                    XmlElement elNote = doc.CreateElement("note");
                    xnNotes.AppendChild(elNote);

                    XmlAttribute atBlokId = doc.CreateAttribute("id-block");
                    atBlokId.InnerText = String.Format("fn{0}", iNoteToConvert);
                    elNote.Attributes.Append(atBlokId);

                    /* Transformace, note obsahuje element name s obsahem [id] a potom obsahuje pčko, které je rodičem "falešné" poznámky bez odkazu (a)*/
                    elNote.InnerXml = String.Format("<name>[{0}]</name>{1}{2}", iNoteToConvert, Environment.NewLine, parentNodeNoteToConvert.OuterXml);

                    /*NOTEREF*/
                    /* Mám note, můžu vytvořit noterefy
                     * Noterefy vytvořím sólo a pak je vložím za <a>, ty nakonec smažu
                     */
                    List<XmlNode> noterefsToConvert = noterefs[iNoteToConvert];
                    foreach (XmlNode xn2 in noterefsToConvert)
                    {
                        /*Vytvořím noterefy, které vložím na správné místo */
                        XmlElement elNoteref = doc.CreateElement("noteref");
                        XmlAttribute atNoteHref = doc.CreateAttribute("href");
                        atNoteHref.InnerText = String.Format("fn{0}", iNoteToConvert);
                        
                        elNoteref.Attributes.Append(atNoteHref);

                        /* Vložím ZA <a>*/
                        xn2.ParentNode.InsertAfter(elNoteref, xn2);
                    }
                    /* Smažu všechny noterefy k převedení */
                    for (int i = 0; i < noterefsToConvert.Count; ++i)
                    {
                        noterefsToConvert[i].ParentNode.RemoveChild(noterefsToConvert[i]);
                    }
                }

                /* Pokud jsem něco měnil, tak přeuložím */
                doc.Save(sFullPathToResultFile);
            }

            return result;
        }

        /// <summary>
        /// Funkce, která spustí vlákno, které vybírá ze seznamu OUHS_seznamRozhodnutiKeZpracovani
        /// Data takto vybraná zpracuje:
        /// Načte webovou stránku pro konrétní dokument
        /// Z této stránky získá informace, které vloží do načteného XmlDokumentu
        /// Nakonec tento dokument uloží
        /// </summary>
        private void UOHS_StartThreadForProcessingDecision()
        {
            // uložím ?
            /*
             * DokumentName dle UOHS 
             * Např pro odkaz http://www.uohs.cz/cs/hospodarska-soutez/sbirky-rozhodnuti/detail-10202.html
             * Je documentName to za posledním lomítkem
             */

            Task.Factory.StartNew(() =>
            {
                // The delegate member for progress bar update
                UpdateDownloadProgressDelegate UpdateProgress = new UpdateDownloadProgressDelegate(UOHS_UpdateDownloadProgressSafe);

                HtmlAgilityPack.HtmlDocument docToProcess = null;
                while (!OUHS_listOfDecisionsToProcess.IsCompleted)
                {
                    // dokud je co brát... (aktivní blokování)
                    while (!OUHS_listOfDecisionsToProcess.IsCompleted && !OUHS_listOfDecisionsToProcess.TryTake(out docToProcess, 2000)) ;

					// Další soubor zpracovávám...
					++UOHS_XML_actualRecordToProcess;

                    if (docToProcess != null)
                    {
                        /*
                         * Zpracování staženého dokumentu
                         * 1. vygeneruji prázdnou hlavičku
                         * 2. Doplním údaje do hlavičky
                         * 2a. Zkontroluji, zda-li se dokument již nenachází v DB
                         * 3. Doplním tělo dokumentu
                         * 4. Transformuji chybné odkazy na poznámky pod čarou (splňují-li pravidla)
                         * 5. Uložím
                         */
                        XmlDocument newXmlDocument = new XmlDocument();
                        #if LOCAL_TEMPLATES
			                newXmlDocument.Load(@"Templates-J-Downloading\Template_J_UOHS.xml");
                        #else
							string sPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
							newXmlDocument.Load(Path.Combine(sPath, @"Templates-J-Downloading\Template_J_UOHS.xml"));
                        #endif
                        /* Vygenerování hlavičky dokumentu */
                        string sDocumentName = TransformHtmlHeaderToXml(docToProcess, newXmlDocument);
                        if (String.IsNullOrEmpty(sDocumentName))
                        {
                            // Dokument se nemá vytvářet
                            continue;
                        }

						string citace = newXmlDocument.DocumentElement.SelectSingleNode("./judikatura-section/header-j/citace").InnerText;
						int idx = citace.LastIndexOf('/');
						int iYear = Int32.Parse(citace.Substring(idx + 1));

                        // Nastavím DokumentName
                        string judikaturaSectionDokumentName;
                        UtilityBeck.Utility.CreateDocumentName("J", citace, iYear.ToString(), out judikaturaSectionDokumentName);
                        XmlNode judikaturaSection = newXmlDocument.SelectSingleNode("//judikatura-section");
                        judikaturaSection.Attributes["id-block"].Value = judikaturaSectionDokumentName;
                        newXmlDocument.DocumentElement.Attributes["DokumentName"].Value = sDocumentName;

                        /* Vytvoření těla dokumentu... */
						TransformHtmlDetailBeforeExport(docToProcess);
						/* Preulozim... */
						string sFullHtmlPath = String.Format(@"{0}\detail-{1}.html", this.txtWorkingFolder.Text, docToProcess.DocumentNode.Attributes["id"].Value);
						docToProcess.Save(sFullHtmlPath);
                                               
                        string sFullPathToResultFolder = String.Format(@"{0}\{1}", this.txtOutputFolder.Text, sDocumentName);
                        string sFullPathToResultFile = String.Format(@"{0}\{1}.xml", sFullPathToResultFolder, sDocumentName);    
                        if (Directory.Exists(sFullPathToResultFolder))
                        {
                            UOHS_citationService.RevertCitationForAYear(iYear);
                            this.WriteIntoLogCritical(String.Format("Složka pro dokumentName [{0}] již existuje", sDocumentName));
                            continue; 
                        }

                        Directory.CreateDirectory(sFullPathToResultFolder);
                        /* Odstraním prázdné části z hlavičky! */
                        UtilityBeck.UtilityXml.DeleteEmptyNodesFromHeaders(newXmlDocument);
                        newXmlDocument.Save(sFullPathToResultFile);

                        string sPathWordXml = String.Format(@"{0}\W_{1}-0.xml", sFullPathToResultFolder, sDocumentName);
                        FrmCourts.OpenFileInWordAndSaveInWXml(sFullHtmlPath, sPathWordXml);
                        File.Copy(sPathWordXml, String.Format(@"{0}.xml", sPathWordXml), true);
#if !DUMMY_DB
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
							UOHS_citationService.RevertCitationForAYear(iYear);
                            this.WriteIntoLogCritical(String.Format("{0}: Export zcela selhal! {1}{2}\tEXC[{3}]", docToProcess.DocumentNode.Attributes["id"].Value, sExportErrors, Environment.NewLine, ex.Message));
                            continue;
                        }
                        #endif
						/* Posun posuvníku 
						 * První použití BeginInvoke místo Invoke - prevence zamrznutí
						 * http://kristofverbiest.blogspot.cz/2007/02/avoid-invoke-prefer-begininvoke.html
						 */
						this.processedBar.BeginInvoke(UpdateProgress, new object[] { false });
                        // Vše proběhlo ok
                        UOHS_citationService.CommitCitationForAYear(iYear);
                    }
                }

                /* Hotovo*/
                this.processedBar.BeginInvoke(UpdateProgress, new object[] { true });
				FinalizeLogs();
            });
        }

        /// <summary>
        /// Funkce, která spočítá aktuální progress na základě
        /// 1) Přečtených stran webu 30%
        /// 2) Vytvořených XML 70%
        /// </summary>
        /// <returns></returns>
        private int UOHS_ComputeActuallDownloadProgress()
        {
            int iRationLoadedPages = Math.Min(30, ((1 + UOHS_ActualIterationNumber) * UOHS_actualPageToProcess * 30) / ((UOHS_totalPagesToProcess +1) * UOHS_Iteration.Length));
            int iRationCreatedXML = Math.Min(70, UOHS_XML_actualRecordToProcess * 70 / UOHS_XML_totalRecordsToProcess);
            return iRationLoadedPages + iRationCreatedXML;
        }

        /// <summary>
        /// Funkce pro update progress baru GUI vlákna, kterou lze použít v jiném vlákně (přes delegát)
        /// </summary>
        /// <param name="forceComplete">Parametr, který říká, že je prostě hotovo...</param>
        void UOHS_UpdateDownloadProgressSafe(bool forceComplete)
        {
            int value = UOHS_ComputeActuallDownloadProgress();
            if (forceComplete)
            {
                value = this.processedBar.Maximum;
            }
            this.processedBar.Value = value;
#if DEBUG
            this.gbProgressBar.Text = String.Format("Procházím {0}/{1} Zpracováno {2}/{3} stránek | {4}/{5} XML => {6}%",
                UOHS_ActualIterationNumber + 1, UOHS_Iteration.Length, UOHS_actualPageToProcess, UOHS_totalPagesToProcess, UOHS_XML_actualRecordToProcess, UOHS_XML_totalRecordsToProcess, this.processedBar.Value);
#endif
            bool maximumReached = (this.processedBar.Value == this.processedBar.Maximum);
            // pokud jsem "reachnul" maximum, povolím opětovné načítání položek...
            this.btnMineDocuments.Enabled = maximumReached;

        }

        /// <summary>
        /// Kliknutí na načtení dokumentů
        /// Znemožní se další kliknutí
        /// Aktuální akce se nastaví na "vyhledání dat"
        /// Načtené odkazy se vynulují
        /// Vytvoří se blokující seznamy a vlákna s nimi pracující
        /// Naviguje se na hlavní stránku vyhledávání UOHS
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private bool UOHS_Click()
        {
            string sError = UOHS_CheckFilledValues();
            if (!String.IsNullOrEmpty(sError))
            {
                MessageBox.Show(this, sError, ERROR_CHECKING_OUTPUT, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return false;
            }

            this.btnMineDocuments.Enabled = false;

            // potřebuji hledat spzn

            this.actualUOHSAction = UOHSAction.uhosaFindingData;
            this.processedBar.Value = 0;

			if (!this.citationNumberGenerator.ContainsKey(Courts.cUOHS))
			{
				this.citationNumberGenerator.Add(Courts.cUOHS, new CitationService(Courts.cUOHS));
			}
			this.UOHS_citationService = this.citationNumberGenerator[Courts.cUOHS];

			this.UOHS_listOfHrefsToDownload = new BlockingCollection<string>(UOHS_BUFFER_SIZE_HREF);
            this.OUHS_listOfDecisionsToProcess = new BlockingCollection<HtmlAgilityPack.HtmlDocument>(UOHS_BUFFER_SIZE_DOCUMENT);
            this.UOHS_Iteration = new Tuple<string, string>[4];
            this.UOHS_Iteration[0] = new Tuple<string, string>("hospodarska-soutez", "Hospodářská soutěž");
            this.UOHS_Iteration[1] = new Tuple<string, string>("verejne-zakazky", "Veřejné zakázky");
            this.UOHS_Iteration[2] = new Tuple<string, string>("verejna-podpora", "Veřejná podpora");
			this.UOHS_Iteration[3] = new Tuple<string, string>("vyznamna-trzni-sila", "Významná tržní síla");

            this.UOHS_ActualIterationNumber = 0;
            UOHS_XML_totalRecordsToProcess = 1;
            UOHS_XML_actualRecordToProcess = 1;

            /* 
             * Celkem stránek - Zpracovávám vždy alespoŇ jednu stránku, i když v ní nejsou žádné záznamy.
             * Preventuji tím dělení nulou...
             */
            UOHS_totalPagesToProcess = 1;

            /*
            * Vytvořím vlákna pro
            * Vybírá ze seznamu dokumentů ke stažení; Tyto dokumenty stahuje
            * Vybírá ze seznamu stažených dokumentů a vytváří xml
            */
            UOHS_LaunchThreadToDownloadDecision();
            UOHS_StartThreadForProcessingDecision();

            browser.Navigate(ActualIndex);
            return true;
        }

        private void EndIteration(bool pForceQuit)
        {
            // Existuje ještě nějaká budoucí iterace?
            if (!pForceQuit && ((this.UOHS_ActualIterationNumber + 1) < this.UOHS_Iteration.Length))
            {
                // Iteruji
                ++this.UOHS_ActualIterationNumber;
                // přesunu se do další kategorie
                this.actualUOHSAction = UOHSAction.uhosaFindingData;
                browser.Navigate(ActualIndex);
            }
            else
            {
                // už nebudu nic přidávat, všechny kategorie jsou přidané, končím
                UOHS_listOfHrefsToDownload.CompleteAdding();
            }
        }

        private void EndIteration()
        {
            EndIteration(false);
        }

        /// <summary>
        /// Došlo k načtení dokumentu.
        /// V závislosti na tom, jakou mam nastavenu aktuální akci zavolám "obsluhu"
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UOHS_browser_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            if (UOHS_listOfHrefsToDownload.IsAddingCompleted)
            {
                return;
            }

            // budu hodný a chvíli počkám, než znovu zaklepám...
            System.Threading.Thread.Sleep(2500);
            switch (actualUOHSAction)
            {
                case UOHSAction.uhosaFindingData:
                    /* Nastavím vyhledávací formulář */
                    if (!DoUOHSActionFindingData())
                    {
                        EndIteration(true);
                    }
                    break;

                case UOHSAction.uhosaFirstSavingOfHref:
                    /* Získám počty dokumentů */
                    DoUOHSActionLoadNumberOfRecords();
                    // od příště už nebudu zjišťovat počet stránek...
                    actualUOHSAction = UOHSAction.uhosaSavingHref;
                    goto case UOHSAction.uhosaSavingHref;

                case UOHSAction.uhosaSavingHref:
                    /* Uložím odkazy */
                    DoUOHSActionSavingHref();
                    /* Update posuvníku */
                    UOHS_UpdateDownloadProgressSafe(false);
                    /* Posun na další stránku? */
                    if (UOHS_actualPageToProcess == UOHS_totalPagesToProcess)
                    {
                        EndIteration();
                        return;
                    }

                    /* navigace na další stránku*/
                    browser.Navigate(String.Format("{0}/{1}.html", ActualPrefix, ++UOHS_actualPageToProcess));
                    break;
            }
        }

        private bool DoUOHSActionFindingData()
        {
            /* Nastavím aktuálně zpracovávanou (budoucí) stránku a záznam */
            UOHS_actualPageToProcess = 1;

            /*
            * Do této akce jsem se dostal navigací do vyhledávacího formuláře
            *
            * Nastavím parametry vyhledávání, nastavím další akci ke zpracování & stisknu vyhledávací button
            */
            // zadám pole do vyhledávacího formuláře

            // Rok podání k ÚOHS
            HtmlElement heYearElement = browser.Document.GetElementById("rok");
            HtmlElementCollection hcYearOptions = heYearElement.GetElementsByTagName("option");
            /* Kontrola roku, zda-li ho vůbec lze vybrat! */
            List<string> allYearOptions = new List<string>();
            foreach (HtmlElement rokOption in hcYearOptions)
            {
                allYearOptions.Add(rokOption.GetAttribute("value"));
            }

            string sYearToBrowse = ((int)this.UOHS_nudYearToDownload.Value).ToString();
            if (!allYearOptions.Contains(sYearToBrowse))
            {
                MessageBox.Show(this, String.Format("Zadaný rok [{0}] nebylo vloženo žádné rozhodnutí!", sYearToBrowse), "Vyhledávání judikatury ÚOHS", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return false;
            }

            heYearElement.SetAttribute("value", sYearToBrowse);

            // nastavím příznak, že už jsem data dal vyhledat...
            actualUOHSAction = UOHSAction.uhosaFirstSavingOfHref;

            // Najdu odesílací tlačítko
            HtmlElementCollection inputs = browser.Document.GetElementsByTagName("input");
            foreach (HtmlElement input in inputs)
            {
                string attValue = input.GetAttribute("value");
                // Našel jsem vyhledávací tlačítko
                if (attValue != null && attValue == "vyhledat")
                {
                    // kliknu na něj
                    input.InvokeMember("click");
                    break;
                }
            }

            return true;
        }

        /// <summary>
        /// Akce: Spočítání počtu nalezených výsledků
        /// Předpoklad: stojím na stránce s výsledky
        /// Najdu text obsahující číslovku s výsledky & vypreparuji
        /// </summary>
        private void DoUOHSActionLoadNumberOfRecords()
        {
            HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(browser.Document.Body.OuterHtml);

            HtmlAgilityPack.HtmlNode hnNumberOfRecords = doc.DocumentNode.SelectSingleNode("//div[@id='text_l']/p");

            /*
             * Počet záznamů ke zpracování může být i nulový => Na stránce nejsou žádné záznamy ke zpracování
             * => neovlivňuje žádné výsledky
             */
            UOHS_XML_totalRecordsToProcessIteration = 0;
            if (hnNumberOfRecords != null)
            {
                Regex regCislo = new Regex(UOHS_ENTITIES_REGEX_PATTERN);
                Match matchCislo = regCislo.Match(hnNumberOfRecords.InnerText);
                if (matchCislo.Success)
                {
                    /* Získám počet záznamů v tabulce */
                    UOHS_XML_totalRecordsToProcessIteration = Int32.Parse(matchCislo.Groups[1].Value);
                    UOHS_XML_totalRecordsToProcess += UOHS_XML_totalRecordsToProcessIteration;
                    UOHS_totalPagesToProcess = UOHS_XML_totalRecordsToProcessIteration / UOHS_RESULT_NUMBER_IN_PAGE + (UOHS_XML_totalRecordsToProcessIteration % UOHS_RESULT_NUMBER_IN_PAGE != 0 ? 1 : 0);
                }
            }
        }

        /// <summary>
        /// Akce uložení odkazu
        /// Předpoklad: Stojím na stránce s výsledky
        /// Uzly (Html) výsledků procházím a všechny neprázdné
        /// vložím do seznamu OUHS_seznamRozhodnutiKeZpracovani (kde je o něj postaráno separátním vláknem)
        /// </summary>
        private void DoUOHSActionSavingHref()
        {
            HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(browser.Document.Body.OuterHtml);

            // pokud je co zpracovávat, tak zpracovávám...
            if (UOHS_XML_totalRecordsToProcessIteration > 0)
            {
                HtmlAgilityPack.HtmlNode hnTableWithResults = doc.DocumentNode.SelectSingleNode("//table[@id='resolutions_list']/tbody");

                foreach (HtmlAgilityPack.HtmlNode hnRowTable in hnTableWithResults.ChildNodes)
                {
                    /* Prázdný řádek, nebo řádek obsahující element th = hlavička tabulky*/
                    if (String.IsNullOrWhiteSpace(hnRowTable.OuterHtml) ||
                        hnRowTable.SelectSingleNode("./th") != null)
                    {
                        continue;
                    }

                    /* Najdu odkaz a přidám do seznamu výsledků */
                    HtmlAgilityPack.HtmlNode hnHrefNode = hnRowTable.SelectSingleNode(".//a[@href]");
					
					/* přidám id do dokumentu, které je rovno číslu dokumentu v UOHS db */
					string sHrefToDownloadValue = hnHrefNode.Attributes["href"].Value;
					Match matchDetailNoParser = UOHS_regDetailNoParser.Match(sHrefToDownloadValue);
					if (matchDetailNoParser.Success)
					{
						string foreignId = matchDetailNoParser.Groups[1].Value;
						/* Check foreign ID in the DB!*/
						if (UOHS_citationService.ForeignIdIsAlreadyInDb(foreignId))
						{
							WriteIntoLogDuplicity("IdExternal [{0}] je v jiz databazi!", foreignId);
							--UOHS_XML_totalRecordsToProcess;
						} else {
							UOHS_listOfHrefsToDownload.Add(String.Format("{0}{1}", UOHS_RESULT_PREFIX, sHrefToDownloadValue));
						}
					}
                    /* Aktuálně zpracovávaný záznam je posunut AŽ po stažení */
                }
            }
        }

    }
}
