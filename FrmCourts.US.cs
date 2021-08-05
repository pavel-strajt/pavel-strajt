using BeckLinking;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using UtilityBeck;

namespace DataMiningCourts
{
    public partial class FrmCourts : Form
    {
        private static string NALUS_INDEX = @"http://nalus.usoud.cz/Search/Search.aspx";
        private static string NALUS_ODKAZ_PREFIX = @"http://nalus.usoud.cz/Search/";
        private static string NALUS_ODKAZ_CONTENT = "ResultDetail";
        private static string NALUS_ODKAZ_VYSLEDKY_CONTENT = "Results.aspx?page=";
        //private static string NALUS_DOKUMENT_ABSTRAKT = @"http://nalus.usoud.cz/Search/GetAbstract.aspx?sz=";
        //private static string NALUS_DOKUMENT_TEXT = @"http://nalus.usoud.cz/Search/GetText.aspx?sz=";
        private static string NALUS_VYSLEDKY = @"http://nalus.usoud.cz/Search/Results.aspx?page=";

        /// <summary>
        /// Třída, ve které jsou uloženy poslední pořadová čísla dokumentů (podle roků)
        /// </summary>
        CitationService NALUS_CitationService;

        private static int NALUS_RESULT_NUMBER_IN_PAGE = 80;

        private string Nalus_CheckFilledValues()
        {
            StringBuilder sbResult = new StringBuilder();
            /* Greater than zero => This instance is later than value. */
            if (this.US_dtpDateFrom.Value.CompareTo(this.US_dtpDateTo.Value) > 0)
            {
                sbResult.AppendLine(String.Format("Datum od [{0}] je větší, než datum do [{1}].", this.US_dtpDateFrom.Value.ToShortDateString(), this.US_dtpDateTo.Value.ToShortDateString()));
            }

            if (this.US_dtpDateFrom.Value.Year != this.US_dtpDateTo.Value.Year)
                sbResult.AppendLine(String.Format("Rok od [{0}] je jiný, nežli rok do [{1}].", this.US_dtpDateFrom.Value.Year, this.US_dtpDateTo.Value.Year));

            if (String.IsNullOrWhiteSpace(this.txtWorkingFolder.Text))
            {
                sbResult.AppendLine("Pracovní složka (místo pro uložení surových dat) musí být vybrána.");
            }

            if (String.IsNullOrWhiteSpace(this.txtOutputFolder.Text))
            {
                sbResult.AppendLine("Výstupní složka (místo pro uložení hotových dat) musí být vybrána.");
            }

            return sbResult.ToString();
        }

        private bool US_Click()
        {
            string sError = Nalus_CheckFilledValues();
            if (!String.IsNullOrEmpty(sError))
            {
                MessageBox.Show(this, sError, ERROR_CHECKING_OUTPUT, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return false;
            }

            this.btnMineDocuments.Enabled = false;
            loadedHrefs.Clear();
            this.processedBar.Value = 0;
            if (!this.citationNumberGenerator.ContainsKey(Courts.cUS))
            {
                this.citationNumberGenerator.Add(Courts.cUS, new CitationService(Courts.cUS));
            }
            this.NALUS_CitationService = this.citationNumberGenerator[Courts.cUS];

            seznamNalusAkci.Push(NalusAkce.naVyhledaniDat);
            browser.Navigate(NALUS_INDEX);
            return true;
        }

        /// <summary>
        /// Seznam možných akcí v módu NALUS
        /// naVyhledaniDat - Vyplní vyhledávací formulář, stiskne tlačítko vyhledat a nastaví další akci na naPrvniUlozeniOdkazu
        /// naPrvniUlozeniOdkazu - Načte seznam stránek výsledků jako naUlozeniOdkazu (od poslední), dále se chová jako naUlozeniOdkazu
        /// naUlozeniOdkazu - Získá seznam odkazů. Pro každý odkaz vytvoří akci naUlozeniObsahu
        /// naUlozeniObsahu - Uloží obsah web browseru do pracovní složky
        /// </summary>
        private enum NalusAkce { naVyhledaniDat, naPrvniUlozeniOdkazu, naUlozeniOdkazu, naUlozeniObsahu };

        /// <summary>
        /// Zásobník NALUS akcí k provedení
        /// </summary>
        private Stack<NalusAkce> seznamNalusAkci = new Stack<NalusAkce>();
        /// <summary>
        /// Zásobník NALUS odkazů k načtení
        /// </summary>
        private Stack<string> seznamOdkazuKeZpracovani = new Stack<string>();


        /// <summary>
        /// Vykoná akci naVyhledaniDat
        /// Vyplní vyhledávací formulář, stiskne tlačítko vyhledat a nastaví další akci na naPrvniUlozeniOdkazu
        /// </summary>
        private void ProvedNalusAkciVyhledaniDat()
        {
            /*
			 * Do této akce jsem se dostal navigací do vyhledávacího formuláře
			 * 
			 * Nastavím parametry vyhledávání, nastavím další akci ke zpracování & stisknu vyhledávací button
			 */
            // zadám pole do vyhledávacího formuláře
            browser.Document.GetElementById("ctl00_MainContent_decidedFrom").SetAttribute("value", this.US_dtpDateFrom.Value.ToShortDateString());
            browser.Document.GetElementById("ctl00_MainContent_decidedTo").SetAttribute("value", this.US_dtpDateTo.Value.ToShortDateString());
            // maximální počet výsledků na stránku...
            browser.Document.GetElementById("ctl00_MainContent_resultsPageSize").SetAttribute("value", NALUS_RESULT_NUMBER_IN_PAGE.ToString());

            seznamNalusAkci.Push(NalusAkce.naPrvniUlozeniOdkazu);

            HtmlElement el = this.browser.Document.All["ctl00_MainContent_but_search"];
            el.InvokeMember("click");
        }

        /// <summary>
        /// Vykoná akci naPrvniUlozeniOdkazu, nebo naPrvniUlozeniOdkazu (dle parametru)
        /// naPrvniUlozeniOdkazu - Načte seznam stránek výsledků jako naUlozeniOdkazu (od poslední) - parametr je true +
        /// naUlozeniOdkazu - Získá seznam odkazů. Pro každý odkaz vytvoří akci naUlozeniObsahu - vždy
        /// </summary>
        private void ProvedNalusAkciUlozeniOdkazu(bool firstInvoke)
        {
            HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(browser.Document.Body.OuterHtml);
            // obsahuje odkazy na výsledky a odkazy na dokumenty... pěkně postupně...
            HtmlAgilityPack.HtmlNodeCollection seznamUzluOdkazuDokumentu = doc.DocumentNode.SelectNodes("//a[@href]");
            // musím vyzjistit počet stránek a naplnit frontu akcí na jejich procházení...
            if (firstInvoke)
            {
                /*
				 * číslo stránky získám následovně
				 * odkazy jsou seřazené ve smyslu
				 *  odkazy na stránky výsledků
				 *  odkazy na dokumenty na této stránce
				 * Pokud je pouze jedna stránka s výsledky, neexistují odkazy na stránky výsledků.
				 * Získám odkaz na první odkaz dokument. Odkaz na poslední stránku má index o dva menší! (poslední stránka|následující stránka|odkaz dokument -> ten získám
				 */
                int prvniOdkazNaDokumenty = 0;
                for (; prvniOdkazNaDokumenty < seznamUzluOdkazuDokumentu.Count; ++prvniOdkazNaDokumenty)
                {
                    if (!seznamUzluOdkazuDokumentu[prvniOdkazNaDokumenty].Attributes["href"].Value.Contains(NALUS_ODKAZ_VYSLEDKY_CONTENT))
                    {
                        break;
                    }
                }

                // vyplnění čísla poslední stránky
                NALUS_celkovyPocetStranek = 0;
                NALUS_celkovyPocetZaznamu = 1; // musím se vyhnout dělení nulou!
                Regex regNalusZaznam = new Regex(@"Výsledky\s+\d+\s+-\s+\d+\s+z\s+celkem\s+(\d+)");
                // rodič odkazů na stránky a jeho první syn
                Match matchNalusZaznam = regNalusZaznam.Match(browser.Document.Body.InnerText);
                if (matchNalusZaznam.Success)
                {
                    NALUS_celkovyPocetZaznamu = Int32.Parse(matchNalusZaznam.Groups[1].Value);
                }
                if (prvniOdkazNaDokumenty != 0)
                {
                    // posunu se o dvě pozice zpět a vyextrahuji číslo stránky (viz výše)
                    string odkazNaPosledniStranku = seznamUzluOdkazuDokumentu[prvniOdkazNaDokumenty - 2].Attributes["href"].Value;
                    Regex regNalusStranka = new Regex(@"\?page=(\d+).*");
                    Match matchNalusStranka = regNalusStranka.Match(odkazNaPosledniStranku);
                    if (matchNalusStranka.Success)
                    {
                        NALUS_celkovyPocetStranek = Int32.Parse(matchNalusStranka.Groups[1].Value);
                    }
                }

                // naplním zásobník akcemi pro procházení stránek...; končím stránkou číslo 1, což je následující stránka
                for (int stranka = NALUS_celkovyPocetStranek; stranka >= 1; --stranka)
                {
                    seznamNalusAkci.Push(NalusAkce.naUlozeniOdkazu);
                    // stránky jsou číslovány od jedničky, takže aktuálněProcházená je v odkazu následující...
                    seznamOdkazuKeZpracovani.Push(String.Format("{0}{1}", NALUS_VYSLEDKY, stranka));
                }
            }

            string castOdkazu, url, fullPath, sExternalId;
            foreach (HtmlAgilityPack.HtmlNode jedenUzelOdkazu in seznamUzluOdkazuDokumentu)
            {
                castOdkazu = jedenUzelOdkazu.Attributes["href"].Value;
                // pravé NalusOdkazy obsahují jistý řetězec
                if (castOdkazu.Contains(NALUS_ODKAZ_CONTENT))
                {
                    // pokud jsme soubor již nestáhli a pokud ho nemáme v databázi, tak ho přidáme k souborům ke stažení
                    url = String.Format("{0}{1}", NALUS_ODKAZ_PREFIX, jedenUzelOdkazu.Attributes["href"].Value);
                    // získám cestu k souboru. nebudu zpracovávat ty, co už mám...; kontrola file exists
                    fullPath = String.Format(@"{0}\{1}.html", this.txtWorkingFolder.Text, PrevedNalusOdkazDokumentuNaJmeno(url));

                    // kontrola db
                    sExternalId = PrevedNalusOdkazDokumentuNaJmeno(url);
                    if (NALUS_CitationService.ForeignIdIsAlreadyInDb(sExternalId))
                    {
                        WriteIntoLogDuplicity("IdExternal [{0}] je v jiz databazi!", sExternalId);
                        continue;
                    }

                    if (!File.Exists(fullPath))
                    {
                        // vytvořím akci naUlozeniObsahu a předám odkaz do seznamu odkazů ke zpracování
                        seznamNalusAkci.Push(NalusAkce.naUlozeniObsahu);
                        seznamOdkazuKeZpracovani.Push(url);
                    }
                }
            }
        }

        Regex regNalusOdkaz = new Regex(@"\?id=(\d+).*");
        /// <summary>
        /// Vyextrahuje z url id pro nalus (pomocí regNalusOdkaz)
        /// </summary>
        private string PrevedNalusOdkazDokumentuNaJmeno(string odkazDokumentu)
        {
            // "id=71359..."
            // chci extrahovat id
            Match matchNalusJmeno = regNalusOdkaz.Match(odkazDokumentu);

            if (matchNalusJmeno.Success)
            {
                return matchNalusJmeno.Groups[1].Value;
            }

            return "ostatni";
        }

        /// <summary>
        /// Vykoná akci naUlozeniObsahu
        /// Nejprve získá cestu, kam by obsah uložil. Pokud soubor neexistuje, vezme obsah webBrowseru a uloží ho
        /// </summary>
        private void ProvedNalusAkciUlozeniObsahu()
        {
            // jsem na nějakém url
            string sHtmFileName = PrevedNalusOdkazDokumentuNaJmeno(browser.Url.AbsoluteUri);

            // získám jméno souboru
            string sFullPath = String.Format(@"{0}\{1}.html", this.txtWorkingFolder.Text, sHtmFileName);

            // vytvořím soubor & uložím data
            using (StreamWriter sw = new StreamWriter(sFullPath, false, Encoding.UTF8))
            {
                sw.Write(browser.Document.Body.OuterHtml);
                sw.Flush();
                sw.Close();
            }
			if (Zpracovat1HtmUsDokument(sFullPath))
			{
				File.Delete(sFullPath);
			}

			++NALUS_aktualneZpracovavanyZaznam;
            this.processedBar.Value = Nalus_ComputeActuallProgress();
        }

        // pouze proto, že jsem pohodlnej - statistika
        int NALUS_aktualneZpracovavanaStranka;
        int NALUS_celkovyPocetStranek;
        int NALUS_aktualneZpracovavanyZaznam;
        int NALUS_celkovyPocetZaznamu;
        List<ParametersOfDataMining> loadedHrefs = new List<ParametersOfDataMining>();

        // < je tam kvůli tomu, aby se mi nezdvojovala shoda kvůli (?:#(\d))? při multimatchi
        Regex regNalusQueryString = new Regex(@"((?:II?I?|IV|Pl)\.\s*ÚS(?:-st)?)\s(\d+)/(\d+)\s(?:#(\d))?\s*<");

        /// <summary>
        /// Ze spisové značky získá odkaz na dokument s danou spisovou značkou, respektive jeho část za parametrem sz=
        /// Již se nepoužívá, stahuje se obsah po vyhledávání
        /// </summary>
        private string getNalusQueryString(string sz)
        {
            /*
			Na konkrétní rozhodnutí či jeho abstrakt můžete odkazovat pomocí tzv. query stringu. Pro text rozhodnutí je formát URL v podobě http://nalus.usoud.cz/Search/GetText.aspx?sz=3-1076-07_1. 
			Za parametrem sz se uvede spisová značka rozhodnutí v předdefinovaném formátu s rozlišovacím indexem, např. pro III. ÚS 1076/07 #1 ve formátu 3-1076-07_1, 
			pro Pl. ÚS 45/06 #2 ve formátu Pl-45-06_2, pro stanovisko pléna Pl. ÚS-st 25/08 #1 ve formátu St-25-08_1. 
			Pro odkazy na abstrakt rozhodnutí pomocí stejně tvořeného query stringu použijte URL ve formátu http://nalus.usoud.cz/Search/GetAbstract.aspx?sz=3-1076-07_1. 
			*/
            Match matchSz = regNalusQueryString.Match(sz);
            StringBuilder sbResult = new StringBuilder();
            if (matchSz.Success)
            {
                string prefix = matchSz.Groups[1].Value;
                if (prefix.StartsWith("IV."))
                {
                    sbResult.Append("4");
                }
                else if (prefix.StartsWith("III."))
                {
                    sbResult.Append("3");
                }
                else if (prefix.StartsWith("II."))
                {
                    sbResult.Append("2");
                }
                else if (prefix.StartsWith("I."))
                {
                    sbResult.Append("1");
                }
                else if (prefix.Contains("-st"))
                {
                    // stanovisko pléna
                    sbResult.Append("St");
                }
                else
                {
                    //plénum
                    sbResult.Append("Pl");
                }

                // dále následuje formát -d+-d+
                sbResult.AppendFormat("-{0}-{1}", matchSz.Groups[2].Value, matchSz.Groups[3].Value);
                // pokud tam je suffix #d, tak přidat _d
                if (matchSz.Groups.Count == 5)
                {
                    sbResult.AppendFormat("_{0}", matchSz.Groups[4].Value);
                }
            }


            return sbResult.ToString();
        }

        /// <summary>
        /// Funkce, která spočítá aktuální progress na základě
        /// 1) Přečtených stran webu 30%
        /// 2) Stažených xml 70%
        /// </summary>
        /// <returns></returns>
        private int Nalus_ComputeActuallProgress()
        {
            int podilPrectenychStran = ((NALUS_aktualneZpracovavanaStranka - 1) * 30) / (NALUS_celkovyPocetStranek + 1);
            int podilStazenychXml = (NALUS_aktualneZpracovavanyZaznam - 1) * 70 / NALUS_celkovyPocetZaznamu;
            return podilPrectenychStran + podilStazenychXml;
        }

        /// <summary>
        /// Událost načtení dokumentu
        /// Vyzvedne akci, kterou má provést a provede ji
        /// Pokud akce nebyla vyhledání dat, provede accounting
        /// Pokud je v zásobníku odkazů nějaký odkaz, přejde na něj
        /// </summary>
        private void US_browser_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            // načetl jsem dokument, načtu akci a zjistím, co dál
            NalusAkce akce = seznamNalusAkci.Pop();
            switch (akce)
            {
                case NalusAkce.naPrvniUlozeniOdkazu:
                    // Hodnoty nastavím již zde, abych se vyhnul při dělení nulou při výpočtu posuvníku...
                    NALUS_aktualneZpracovavanaStranka = 1;
                    NALUS_celkovyPocetZaznamu = NALUS_aktualneZpracovavanyZaznam = 1;
                    // pokud bylo nalezeno. tak mohu pokračovat...
                    if (!browser.Document.Body.InnerText.Contains("Pro zadaná kritéria nebyly nalezeny žádné záznamy."))
                    {
                        ProvedNalusAkciUlozeniOdkazu(true);
                    }
                    break;

                case NalusAkce.naUlozeniObsahu:
                    ProvedNalusAkciUlozeniObsahu();
                    break;

                case NalusAkce.naUlozeniOdkazu:
                    NALUS_aktualneZpracovavanaStranka++;
                    ProvedNalusAkciUlozeniOdkazu(false);
                    break;

                case NalusAkce.naVyhledaniDat:
                    ProvedNalusAkciVyhledaniDat();
                    break;
            }

            // pokud jsem ted neměl pouze vyhledat data (tzn už něco zpracovávám)
            if (akce != NalusAkce.naVyhledaniDat)
            {
                // popojedu s progress barem; aktuální stránku čísluju od 1, poslední od 0, takže ji (celek) musím posunout, aby mi to sedělo...
                // navíc stránku musím započítat až po jejím zpracování, tzn po načtení další stránky

                this.processedBar.Value = Nalus_ComputeActuallProgress();
                // pokud už nemám co zpracovat
                if (seznamOdkazuKeZpracovani.Count == 0)
                {
                    // tak končím
                    if (this.dbConnection.State == System.Data.ConnectionState.Open)
                        this.dbConnection.Close();

                    this.btnMineDocuments.Enabled = true;
                    this.processedBar.Value = 100;
                    FinalizeLogs(false);
                }
            }

            // pokud existuje odkaz, tak se na něj znaviguju...
            if (seznamOdkazuKeZpracovani.Count > 0)
            {
				browser.Navigate(seznamOdkazuKeZpracovani.Pop());
			}
        }

        public static readonly string REGEX_ROMAN_NUMBER = @"M{0,3}(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3})";

        //Regex - checking for valid Roman numerals (Validátor římských čísel)
        public static Regex roman_regex = new Regex(String.Format(@"^{0}$", REGEX_ROMAN_NUMBER));

        /// <summary>
        /// Spisová značka ve tvaru římské číslo.XXX se má předělat do tvaru římské číslo. XXX
        /// tzn. přidat mezeru
        /// </summary>
        /// <param name="spZn"></param>
        /// <returns>True, pokud se podařilo transformovat jinak false</returns>
        private bool TransformujSpisovouZnacku(ref string spZn)
        {
            // odstranění mezer okolo pomlčky
            spZn = spZn.Replace("–", "-");
            Regex rg = new Regex(@"\s*\-\s*\d+$");
            MatchEvaluator evaluator = new MatchEvaluator(TermSpaces);
            if (rg.IsMatch(spZn))
                spZn = rg.Replace(spZn, evaluator);

            if (spZn.StartsWith("Pl"))
            {
                spZn = spZn.Replace("Pl.ÚS", "Pl. ÚS");
                return true;
            }

            /* Postup:
			 * 1. Substring před první tečkou nevčetně + idx na tečku
			 * 2. Pokud je 1. validní římské číslo (i když ús používá pouze I, II, III, IV), tak přidej tečku jinak ne
			 */

            int idxTecka = spZn.IndexOf('.');
            /* Existuje tečka a není to poslední znak */
            if (idxTecka != -1 && (idxTecka + 1) < spZn.Length)
            {
                string potencialniRimskeCislo = spZn.Substring(0, idxTecka);
                if (roman_regex.IsMatch(potencialniRimskeCislo))
                {
                    /* Přidáme tečku */
                    spZn = String.Format("{0}. {1}", potencialniRimskeCislo, spZn.Substring(idxTecka + 1));
                    return true;
                }
            }

            return false;
        }

        private bool Zpracovat1HtmUsDokument(string pPath)
        {
            XmlDocument dIn = new XmlDocument(), dOut = new XmlDocument();
            string sSpZn = null, sFromDay = null, sValue, sText1, sText2, druh = null;
            XmlNode xn, xnTr, xnTd1, xnTd2, xn2;
            Object oResult = null;
            int iPosition2, iNumber;
            SqlCommand cmd = this.dbConnection.CreateCommand();
            string zeDneNonUni = null;

            HtmlAgilityPack.HtmlDocument dHtm = new HtmlAgilityPack.HtmlDocument();
            dHtm.OptionOutputAsXml = true;
            dHtm.Load(pPath);
            try
            {
                dIn.LoadXml(dHtm.DocumentNode.OuterHtml.Replace("&amp;nbsp;", " "));
            }
            catch (XmlException ex)
            {
                WriteIntoLogCritical("XmlException: {0}", ex.Message);
                return false;
            }
#if LOCAL_TEMPLATES
            dOut.Load(@"Templates-J-Downloading\Template_J_US.xml");
#else
            string sPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            dOut.Load(Path.Combine(sPath, @"Templates-J-Downloading\Template_J_US.xml"));
#endif
            // spisová značka je první neprázdná buňka s hodnotami v tabulce s id=tableDocumentHeader na řádku s class=resultData0
            xn = dIn.SelectSingleNode("//table[@id='tableDocumentHeader']//tr[@class='resultData0']/td[normalize-space(.)]/text()");
            if (xn == null)
            {
                Log.InfoFormat("{0}: Spisová značka byla vyhledána až na kartě záznamu!", pPath);
                // spisová značka v rámci tabulky "Karta záznamu
                xn = dIn.SelectSingleNode("//table[@class='recordCardTable']/tbody/tr/td[2]");
                if (!xn.PreviousSibling.InnerText.Equals("Spisová značka"))
                {
                    WriteIntoLogCritical(String.Format("{0}: Nenalezena spisová značka.", pPath));
                    return false;
                }
            }

            sSpZn = xn.InnerText.Trim();
            iPosition2 = sSpZn.IndexOf(',');
            if (iPosition2 > -1)
            {
                sSpZn = sSpZn.Substring(0, iPosition2);
            }
            sSpZn = Regex.Replace(sSpZn, @"\s*#\s*1\.?$", String.Empty).Replace('#', '-');

            xnTr = dIn.SelectSingleNode("//div[@id='recordCardPanel']/table/tbody/tr[1]");
            while (xnTr != null)
            {
                xnTd1 = xnTr.FirstChild;
                xnTd2 = xnTd1.NextSibling;
                switch (xnTd1.InnerText.Trim())
                {
                    case "Spisová značka":      // spisová značka
                        sValue = xnTd2.InnerText.Trim();
                        if (!sSpZn.Contains(sValue))
                        {
                            WriteIntoLogCritical(String.Format("{0}: Zmatek ve spisové značce: {1}", pPath, sSpZn));
                            return false;
                        }

                        /* Transformuji spzn, přidám mezeru za počáteční římské číslo */
                        if (!TransformujSpisovouZnacku(ref sSpZn))
                        {
                            /* Chybka? */
                            WriteIntoLogCritical(String.Format("Nekritická chyba, ve spisové značce {0} nebylo identifikováno počáteční římské číslo, a proto nebyla přidána mezera...", sSpZn));
                        }

                        xn = dOut.DocumentElement.FirstChild.SelectSingleNode("./citace");
                        xn.InnerText = sSpZn;
                        break;
                    case "Identifikátor evropské judikatury":
                        if (!String.IsNullOrWhiteSpace(xnTd2.InnerText))
                        {
                            xn = dOut.SelectSingleNode("//ecli");
                            xn.InnerText = xnTd2.InnerText.Trim();
                        }
                        break;

                    case "Paralelní citace (Sbírka zákonů)":    // Tyto pole se přeskakují
                    case "Paralelní citace (Sbírka nálezů a usnesení)":
                    case "Datum vyhlášení":
                    case "Datum podání":
                    case "Navrhovatel":
                    case "Dotčený orgán":
                    case "Soudce zpravodaj":
                    case "Napadený akt":
                    case "Předmět řízení":
                    case "Název soudu":
                    case "Datum zpřístupnění":
                    case "Jazyk rozhodnutí":
                    case "URL adresa":
                        break;
                    case "Populární název":     // titul
                        if (!String.IsNullOrWhiteSpace(xnTd2.InnerText))
                        {
                            xn = dOut.SelectSingleNode("//titul");
                            xn.InnerText = xnTd2.InnerText.Replace("\r\n", "").Trim();
                            while (xn.InnerText.IndexOf("  ") > -1)
                                xn.InnerText = xn.InnerText.Replace("  ", " ");
                        }
                        break;
                    case "Datum rozhodnutí":    // Ze dne
                        zeDneNonUni = xnTd2.InnerText.Trim();
                        sFromDay = Utility.ConvertDateIntoUniversalFormat(zeDneNonUni, out DateTime? dt);
                        xn = dOut.SelectSingleNode("//datschvaleni");
                        xn.InnerText = sFromDay;
                        break;
                    case "Forma rozhodnutí":    // Druh
                        if (!String.IsNullOrWhiteSpace(xnTd2.InnerText))
                        {
                            xn = dOut.SelectSingleNode("//druh");
                            druh = xnTd2.InnerText.Trim().Replace("Stanovisko pléna", "Stanovisko");
                            xn.InnerText = druh;
                        }
                        break;
                    case "Typ řízení":
                        if (!String.IsNullOrWhiteSpace(xnTd2.InnerText))
                        {
                            xn = dOut.SelectSingleNode("//info3");
                            xn.InnerText = xnTd2.InnerText.Trim();
                        }
                        break;
                    case "Význam":
                        if (!String.IsNullOrWhiteSpace(xnTd2.InnerText))
                        {
                            xn = dOut.SelectSingleNode("//info1");
                            xn.InnerText = xnTd2.InnerText.Trim();
                        }
                        break;
                    case "Typ výroku":
                        if (!String.IsNullOrWhiteSpace(xnTd2.InnerText))
                        {
                            xn = dOut.SelectSingleNode("//info4xml");
                            if (xn.InnerXml.Equals("<item></item>"))
                                xn.InnerXml = String.Empty;
                            xn2 = xnTd2.FirstChild;
                            while (xn2 != null)
                            {
                                if (xn2.NodeType == XmlNodeType.Text)
                                {
                                    if (!String.IsNullOrWhiteSpace(xn2.InnerText))
                                        xn.InnerXml += "<item>" + xn2.InnerText.Trim() + "</item>";
                                }
                                else if (!xn2.Name.Equals("br"))
                                {
                                    WriteIntoLogCritical(String.Format("{0}: Neznámý element v Typu výroku !", pPath));
                                    return false;
                                }
                                xn2 = xn2.NextSibling;
                            }
                            if (String.IsNullOrEmpty(xn.InnerXml))
                                xn.InnerXml = "<item></item>";
                        }
                        break;
                    case "Dotčené ústavní zákony":
                    case "Dotčené mezinárodní smlouvy":
                    case "Ostatní dotčené předpisy":
                    case "Dotčené ústavní zákony a mezinárodní smlouvy":
                        if (Properties.Settings.Default.PROCESS_ZAKLADNI_PREDPIS && !String.IsNullOrWhiteSpace(xnTd2.InnerText))
                        {
                            xn = dOut.DocumentElement.FirstChild.SelectSingleNode("./zakladnipredpis");
                            if (xn == null)
                            {
                                dOut.DocumentElement.FirstChild.AppendChild(dOut.CreateElement("zakladnipredpis"));
                                xn = dOut.DocumentElement.FirstChild.SelectSingleNode("./zakladnipredpis");
                            }
                            LinkingHelper.AddBaseLaws(dOut, xnTd2.InnerText, xn);
                        }
                        break;
                    case "Odlišné stanovisko":
                        if (!String.IsNullOrWhiteSpace(xnTd2.InnerText))
                        {
                            xn = dOut.SelectSingleNode("//info5xml");
                            if (xn.InnerXml.Equals("<item></item>"))
                                xn.InnerXml = String.Empty;
                            xn2 = xnTd2.FirstChild;
                            while (xn2 != null)
                            {
                                if (xn2.NodeType == XmlNodeType.Text)
                                {
                                    if (!String.IsNullOrWhiteSpace(xn2.InnerText))
                                        xn.InnerXml += "<item>" + xn2.InnerText.Trim() + "</item>";
                                }
                                else if (!xn2.Name.Equals("br"))
                                {
                                    WriteIntoLogCritical(String.Format("{0}: Neznámý element v Typu výroku !", pPath));
                                    return false;
                                }
                                xn2 = xn2.NextSibling;
                            }
                            if (String.IsNullOrEmpty(xn.InnerXml))
                                xn.InnerXml = "<item></item>";
                        }
                        break;
                    case "Věcný rejstřík":
                        if (!String.IsNullOrWhiteSpace(xnTd2.InnerText))
                        {
                            if (xn.InnerXml.Equals("<item></item>"))
                                xn.InnerXml = String.Empty;
                            xn2 = xnTd2.FirstChild;
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
                                        xn.InnerXml += "<item>" + sValue + "</item>";
                                    }
                                }
                                else if (!xn2.Name.Equals("br"))
                                {
                                    WriteIntoLogCritical(String.Format("{0}: Neznámý element v rejstříku !", pPath));
                                    return false;
                                }
                                xn2 = xn2.NextSibling;
                            }
                            if (String.IsNullOrEmpty(xn.InnerXml))
                                xn.InnerXml = "<item></item>";
                        }
                        break;
                    case "Poznámka":
                        if (!String.IsNullOrWhiteSpace(xnTd2.InnerText))
                        {
                            xn = dOut.SelectSingleNode("//poznamka");
                            //xn.InnerXml += "<p>" + xnTd2.InnerText.Trim() + "</p>";
                            xn.InnerXml += "<p>" + xnTd2.InnerText.Trim() + "</p>";
                        }
                        break;

                    default:
                        if (!String.IsNullOrWhiteSpace(xnTd2.InnerText))
                        {
                            WriteIntoLogCritical(String.Format("{0}: Neočekávané pole v htm: {1}{2}\t...pole se přeskakuje...", pPath, xnTr.FirstChild.InnerText.Trim(), Environment.NewLine));
                        }
                        break;
                }
                xnTr = xnTr.NextSibling;
            }

            // doplnění ID z jiného systému
            iPosition2 = pPath.LastIndexOf('\\');
            sValue = pPath.Substring(iPosition2 + 1);
            iPosition2 = sValue.LastIndexOf('.');
            string foreignId = sValue.Substring(0, iPosition2);
            xn = dOut.SelectSingleNode("//id-external");
            xn.InnerText = foreignId;

            druh = druh.ToLower();

            // doplnění čísla sešitu
            string sYear = sFromDay.Substring(0, 4);
            DateTime dtFromDay = DateTime.Parse(sFromDay);
            // doplnění citace
            xn = dOut.DocumentElement.SelectSingleNode("./judikatura-section/header-j/citace");
            // abych poznal problémy, nastavím rok na nesmyslnou hodnotu!
            int iYear = 9999;
            Int32.TryParse(sYear, out iYear);
            if (this.NALUS_CitationService.IsAlreadyinDb(dtFromDay, sSpZn, foreignId))
            {
                WriteIntoLogDuplicity(String.Format("Znacka [{0}] s daným datem rozhodnutí [{1}] je v jiz databazi!", sSpZn, zeDneNonUni));
                // dokument již máme
                return true;
            }

            iNumber = this.NALUS_CitationService.GetNextCitation(dtFromDay.Year);
            string sCitation = "ÚS " + (iNumber).ToString() + "/" + sYear;
            xn.InnerText = sCitation;

			// odstranění prázdných elementů z hlavičky
			UtilityXml.RemoveEmptyElementsFromHeader(ref dOut);

			AddLawArea(this.dbConnection, dOut.DocumentElement.FirstChild, false);

            // doplnění textu
            xn2 = dOut.SelectSingleNode("//html-text");

            //replace a with span
            //PostProcessReplaceTagWithSpanClass(ref dIn, "//a[contains(translate(@href,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'), '://nalus.usoud.cz')]", "span", "");
            PostProcessRemoveTagA(dIn);
            PostProcessReplaceTagWithSpanClass(ref dIn, "//b", "span", "font-weight:bold");

            xn = dIn.SelectSingleNode("//div[@id='docContentPanel']");
            sText1 = xn.InnerText;
            Utility.RemoveWhiteSpaces(ref sText1);
            xn = xn.FirstChild.FirstChild.FirstChild.FirstChild.FirstChild.FirstChild;
            while (xn != null)
            {
                if (xn.NodeType == XmlNodeType.Text)
                {
                    //xn2.InnerXml += "<p>" + xn.InnerText.TrimStart().Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;") + " </p>";
                    xn2.InnerXml += "<p>" + xn.InnerText.TrimStart().Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;") + " </p>";
                    if ((xn.NextSibling != null) && xn.NextSibling.Name.Equals("br"))
                        xn = xn.NextSibling;
                }
                else if (xn.Name.Equals("span"))
                {
                    if (xn.HasChildNodes)
                    {
                        if (!ZpracovatSmisenySpanUzel(xn, xn2, "font-weight:bold"))
                        {
                            WriteIntoLogCritical(String.Format("{0}: Neočekávaný element: {1}", pPath, xn.OuterXml));
                            this.NALUS_CitationService.RevertCitationForAYear(dtFromDay.Year);
                            return false;
                        }

                    }
                    else
                    {
                        if (xn.Attributes["style"] != null && xn.Attributes["style"].Value == "font-weight:bold")
                            xn2.InnerXml += "<p><b>" + xn.InnerXml + "</b></p>";
                        else
                            xn2.InnerXml += "<p>" + xn.InnerXml + "</p>";
                        if ((xn.NextSibling != null) && xn.NextSibling.Name.Equals("br"))
                            xn2.InnerXml += "<p/>";
                    }
                }
                else if (xn.Name.Equals("br") || xn.Name.Equals("hr"))
                {
                    if (xn2.LastChild != null)
                        xn2.LastChild.InnerXml = xn2.LastChild.InnerXml.TrimEnd();
                    xn2.InnerXml += "<p/>";
                }
                else if (xn.Name.Equals("b") && (xn.Attributes.Count == 0))
                {
                    if (!ZpracovatSmisenyUzel(xn, xn2, "font-weight:bold"))
                    {
                        WriteIntoLogCritical(String.Format("{0}: Neočekávaný element: {1}", pPath, xn.OuterXml));
                        this.NALUS_CitationService.RevertCitationForAYear(dtFromDay.Year);
                        return false;
                    }
                }
                else if (xn.Name.Equals("a"))
                {
                    if (!ZpracovatSmisenyUzel(xn, xn2, null))
                    {
                        WriteIntoLogCritical(String.Format("{0}: Neočekávaný element: {1}", pPath, xn.OuterXml));
                        this.NALUS_CitationService.RevertCitationForAYear(dtFromDay.Year);
                        return false;
                    }
                }
                else
                {
                    WriteIntoLogCritical(String.Format("{0}: Neočekávaný element: {1}", pPath, xn.OuterXml));
                    this.NALUS_CitationService.RevertCitationForAYear(dtFromDay.Year);
                    return false;
                }
                xn = xn.NextSibling;
            }
            // vytvoření složky výsledného xml
            if (!Utility.CreateDocumentName("J", sSpZn, sYear, out string sDocumentName) ||
                    !Utility.CreateDocumentName("J", sCitation, sYear, out string judikaturaSectionDokumentName))
            {
                WriteIntoLogCritical(String.Format("{0}: Nevytvořen název dokumentu!", pPath));
                this.NALUS_CitationService.RevertCitationForAYear(dtFromDay.Year);
                return false;
            }
            if (Directory.Exists(Path.Combine(this.txtOutputFolder.Text, sDocumentName)))
            {
                WriteIntoLogCritical(String.Format("{0}: Výstupní dokument již existuje!", pPath));
                this.NALUS_CitationService.RevertCitationForAYear(dtFromDay.Year);
                return false;
            }

            Directory.CreateDirectory(Path.Combine(this.txtOutputFolder.Text, sDocumentName));
            // Judikatura-section - nastavení DokumentName
            XmlNode judikaturaSection = dOut.SelectSingleNode("//judikatura-section");
            judikaturaSection.Attributes["id-block"].Value = judikaturaSectionDokumentName;
            dOut.DocumentElement.Attributes["DokumentName"].Value = sDocumentName;

            // linking
            Linking oLinking = new Linking(dbConnection, "cs", null);
            oLinking.Run(0, sDocumentName, dOut, 17);
            dOut = oLinking.LinkedDocument;

			// výrok, odůvodněni
			string s;
			XmlAttribute a;
			XmlElement elHtmlOduvodneni = null;
			XmlNode xnHtmlVyrok = dOut.SelectSingleNode("/*/judikatura-section/html-text");
			xn = xnHtmlVyrok.FirstChild;
			while(xn != null)
			{
				if (elHtmlOduvodneni != null)
				{
					xn2 = xn.NextSibling;
					elHtmlOduvodneni.AppendChild(xn);
					xn = xn2;
				}
				else
				{
					s = xn.InnerText.ToLower();
					Utility.RemoveWhiteSpaces(ref s);
					if (s.Equals("odůvodnění") || s.Equals("odůvodnění:"))
					{
						elHtmlOduvodneni = dOut.CreateElement("html-text");
						elHtmlOduvodneni.SetAttribute("role", "oduvodneni");
						xn2 = xn.NextSibling;
						a = dOut.CreateAttribute("class");
						a.Value = "center";
						xn.Attributes.Append(a);
						elHtmlOduvodneni.AppendChild(xn);
						xn = xn2;
					}
					else
						xn = xn.NextSibling;
				}
			}
			if (elHtmlOduvodneni != null)
			{
				dOut.DocumentElement.FirstChild.NextSibling.AppendChild(elHtmlOduvodneni);
				a = dOut.CreateAttribute("role");
				a.Value = "vyrok";
				xnHtmlVyrok.Attributes.Append(a);
			}

			// uložení
			sText2 = "";
            XmlNodeList xNodes = dOut.SelectNodes("//html-text");
			for (int i = 0; i < xNodes.Count; i++)
			{
				xn2 = xNodes[i];
				UtilityXml.RemoveRedundantEmptyRowsInXmlDocument(ref xn2);
				sText2 += xn2.InnerText;
				Utility.RemoveWhiteSpaces(ref sText2);
			}
			this.AddPrezkoumava(dbConnection, ref dOut);
			UtilityXml.AddCite(dOut, sDocumentName, this.dbConnection);     // zohlední pouze linky v odůvodnění a přeskočí ty v přezkoumává

            /* Před uložením odstaraníme prázdné elementy hlavičky */
            UtilityXml.RemoveEmptyElementsFromHeader(ref dOut);
            /* Protože se nedělá export, je nutné přidat funkci na opravu http linků */
            UtilityXml.RepairHttpLinks(ref dOut);

            dOut.Save(Path.Combine(this.txtOutputFolder.Text, sDocumentName, sDocumentName) + ".xml");
            // porovnání obsahu původního htm a výsleného xml
            if (!sText1.Equals(sText2))
            {
                WriteIntoLogCritical(String.Format("{0}: Nesouhlas výstupního textu!", pPath));
                this.NALUS_CitationService.RevertCitationForAYear(dtFromDay.Year);
                return false;
            }

            this.NALUS_CitationService.CommitCitationForAYear(dtFromDay.Year);
            return true;
        }

        private static void PostProcessReplaceTagWithSpanClass(ref XmlDocument doc, string xpath, string element, string style)
        {
            var nodes = doc.SelectNodes(xpath);
            foreach (XmlNode node in nodes)
            {
                var tmp = doc.CreateElement(element);
                if (!string.IsNullOrWhiteSpace(style))
                {
                    var attStyle = doc.CreateAttribute("style");
                    attStyle.Value = style;
                    tmp.Attributes.Append(attStyle);
                }
                tmp.InnerXml = node.InnerXml;
                node.ParentNode.ReplaceChild(tmp, node);
            }
        }

        private static void PostProcessRemoveTagA(XmlDocument doc)
        {
            string tmpDoc = doc.InnerXml;
            Regex regex = new Regex("<a.+?href=\".+?nalus\\.usoud\\.cz.+?>([^<]*)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);
            MatchCollection matches = regex.Matches(tmpDoc);
            foreach (Match m in matches)
            {
                if (m.Groups.Count > 1)
                {
                    string tmp = m.Groups[1].Value;
                    string pattern = m.Value.Replace(".", "\\.").Replace("?", "\\?");
                    tmpDoc = Regex.Replace(tmpDoc, pattern, tmp);
                }
            }
            doc.InnerXml = tmpDoc;
        }

        private static bool ZpracovatSmisenyUzel(XmlNode pXnInHtml, XmlNode pXnOut, string pStyl)
        {
            XmlNode xn = pXnInHtml.FirstChild;
            string sValue;
            while (xn != null)
            {
                if (xn.NodeType == XmlNodeType.Text)
                {
                    sValue = xn.InnerText.TrimStart().Replace("&", "&amp;");
                    if (!String.IsNullOrEmpty(pStyl))
                    {
                        if (pStyl.Contains("bold") && pStyl.Contains("italic"))
                        {
                            pXnOut.InnerXml += "<p><span style=\"" + pStyl + "\">" + sValue + " </span></p>";
                        }
                        else if (pStyl.Contains("bold"))
                        {
                            pXnOut.InnerXml += "<p><b>" + sValue + " </b></p>";
                        }
                        else if (pStyl.Contains("italic"))
                        {
                            pXnOut.InnerXml += "<p><i>" + sValue + " </i></p>";
                        }
                        else
                        {
                            pXnOut.InnerXml += "<p><span style=\"" + pStyl + "\">" + sValue + " </span></p>";
                        }
                    }
                    else
                        pXnOut.InnerXml += "<p>" + sValue + " </p>";
                }
                else if (xn.Name.Equals("br"))
                {
                    if (pXnOut.LastChild != null)
                        pXnOut.LastChild.InnerXml = pXnOut.LastChild.InnerXml.TrimEnd();
                    pXnOut.InnerXml += "<p/>";
                }
                else
                    return false;
                xn = xn.NextSibling;
            }
            return true;
        }
        private static bool ZpracovatSmisenySpanUzel(XmlNode pXnInHtml, XmlNode pXnOut, string pStyl)
        {
            XmlNode xn = pXnInHtml.FirstChild;
            string sValue;
            while (xn != null)
            {
                if (xn.NodeType == XmlNodeType.Text)
                {
                    sValue = xn.InnerText.TrimStart().Replace("&", "&amp;");
                    if (!String.IsNullOrEmpty(pStyl))
                    {
                        if (pStyl.Contains("bold") && pStyl.Contains("italic"))
                        {
                            pXnOut.InnerXml += "<p><span style=\"" + pStyl + "\">" + sValue + " </span></p>";
                        }
                        else if (pStyl.Contains("bold"))
                        {
                            pXnOut.InnerXml += "<p><b>" + sValue + " </b></p>";
                        }
                        else if (pStyl.Contains("italic"))
                        {
                            pXnOut.InnerXml += "<p><i>" + sValue + " </i></p>";
                        }
                        else
                        {
                            pXnOut.InnerXml += "<p><span style=\"" + pStyl + "\">" + sValue + " </span></p>";
                        }
                    }
                    else
                    {
                        pXnOut.InnerXml += "<p>" + sValue + " </p>";
                    }
                }
                else if (xn.Name.Equals("br"))
                {
                    if (pXnOut.LastChild != null)
                        pXnOut.LastChild.InnerXml = pXnOut.LastChild.InnerXml.TrimEnd();
                    pXnOut.InnerXml += "<p/>";
                }
                else if (xn.Name.Equals("span") || xn.Name.Equals("a"))
                {
                    if (xn.Attributes["style"] != null && xn.Attributes["style"].Value == "font-weight:bold")
                        pXnOut.InnerXml += "<p><b>" + xn.InnerXml + "</b></p>";
                    else
                        pXnOut.InnerXml += "<p>" + xn.InnerXml + "</p>";
                    if ((xn.NextSibling != null) && xn.NextSibling.Name.Equals("br"))
                        pXnOut.InnerXml += "<p/>";
                }
                else
                    return false;
                xn = xn.NextSibling;
            }
            return true;
        }

		private void AddPrezkoumava(SqlConnection pConn, ref XmlDocument pD)
		{
			// Pokud je ve Stažené info 2 (alespoň) "vyhověno", přiřadí se výsledek Zrušeno; v ostatních případech se přiřadí výsledek Nezměněno.
			string sVysledek;
			XmlNode xn = pD.SelectSingleNode("/*/judikatura-section/header-j/info4xml/item");
			if (xn == null)
				return;
			string sInfo4Vyrok = xn.InnerText.ToLower().TrimStart();
			if (sInfo4Vyrok.StartsWith("procesní"))
				return;
			if (xn.InnerText.ToLower().Contains("vyhověno"))
				sVysledek = "Zrušeno";
			else
				sVysledek = "Nezměněno";
			xn = pD.SelectSingleNode("/*/judikatura-section/html-text[@role='vyrok']");
			if (xn == null)
				return;

			// Do Přezkoumává se automaticky doplní judikáty prolinkované ve výrokové části vyjma rozhodnutí Ústavního soudu
			List<string> lhrefs = new List<string>();
			SqlCommand cmd = pConn.CreateCommand();
			XmlElement elPrezkoumava = pD.CreateElement("prezkoumava");
			XmlElement elItem, el;
			XmlNodeList xNodes = xn.SelectNodes(".//link[starts-with(@href,'J') and not(type='multi')]");
			foreach(XmlNode xn1 in xNodes)
			{
				if (!lhrefs.Contains(xn1.Attributes["href"].Value))
				{
					cmd.CommandText = @"SELECT Dokument.Citation,HeaderJ.DateApproval,TKind.TKindName,TAuthor.TAuthorName FROM Dokument
	INNER JOIN HeaderJ ON Dokument.IDDokument=HeaderJ.IDDokument
	INNER JOIN DokumentAuthor ON Dokument.IDDokument=DokumentAuthor.IDDokument
	INNER JOIN TKind ON HeaderJ.IDTKind=TKind.IDTKind
	INNER JOIN TAuthor ON DokumentAuthor.IDTAuthor=TAuthor.IDTAuthor
	WHERE DokumentAuthor.IDTAuthor<>494 AND Dokument.DokumentName='" + xn1.Attributes["href"].Value + "'";
					using (SqlDataReader dr = cmd.ExecuteReader())
					{
						if (dr.HasRows)
						{
							dr.Read();
							elItem = pD.CreateElement("item-prezkum");
							el = pD.CreateElement("cislojednaci");
							el.SetAttribute("href", xn1.Attributes["href"].Value);
							el.InnerText = dr.GetString(0);
							elItem.AppendChild(el);
							el = pD.CreateElement("datschvaleni");
							el.InnerText = dr.GetDateTime(1).ToString("yyyy-MM-dd");
							elItem.AppendChild(el);
							el = pD.CreateElement("druh");
							el.InnerText = dr.GetString(2);
							elItem.AppendChild(el);
							el = pD.CreateElement("soud");
							el.InnerText = dr.GetString(3);
							elItem.AppendChild(el);
							elItem.InnerXml += "<vysledek>" + sVysledek + "</vysledek>";
							elPrezkoumava.AppendChild(elItem);
						}
					}
				}
			}
			if (elPrezkoumava.ChildNodes.Count > 0)
				UtilityXml.InsertElementInAlphabeticalOrder(pD.DocumentElement.FirstChild, elPrezkoumava);
		}
	}
}
