using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.IO;
using UtilityBeck;
using System.Configuration;
using System.Data.SqlClient;
using DataMiningCourts.Properties;
using System.Text.RegularExpressions;

namespace DataMiningCourts
{
    public partial class FrmCourts : Form
    {
        private static int NS_MAXIMUM_NUMBER_SHOWED_RESULTS = 1000;
        /* Pouze pro jedno datum, proto se den/měsíc/rok vyplňuje dvakrát stejný */
        //private static string NS_COURT = @"http://novyweb.nsoud.cz/Judikatura/judikatura_ns.nsf/$$WebSearch1?SearchView&Query=%5Bdatum_predani_na_web%5D%3E%3D{0}%2F{1}%2F{2}%20AND%20%5Bdatum_predani_na_web%5D%3C%3D{0}%2F{1}%2F{2}&SearchMax={3}&Start=1&Count={3}&pohled=1";
		private static string NS_COURT = @"http://www.nsoud.cz/Judikatura/judikatura_ns.nsf/$$WebSearch1?SearchView&Query=%5Bdatum_predani_na_web%5D%3E%3D{0}%2F{1}%2F{2}%20AND%20%5Bdatum_predani_na_web%5D%3C%3D{0}%2F{1}%2F{2}&SearchMax={3}&Start=1&Count={3}&pohled=1";
		private static string NS_COURT_HREF_CONTENT = "WebSearch";
        private static string NS_COURT_HREF_SEARCHING_QUERY_CONTENT = "Query";
		//private static string NS_INDEX = @"http://novyweb.nsoud.cz/";
		private static string NS_INDEX = "https://www.nsoud.cz/";


		private DateTime NS_minimumLoadedDate;
        private DateTime NS_maximumLoadedDate;
        private DateTime NS_actualLoadedDate;

        private List<ParametersOfDataMiningNS> NS_loadedHrefs = new List<ParametersOfDataMiningNS>();

        private Dictionary<int, int> NS_lastCitationByEndOfTheDay;

        private void NS_bgLoadData_DoWork(object sender, DoWorkEventArgs e)
        {
            NS_ThreadPoolDownload.InitializeCitationService(Courts.cNS, NS_lastCitationByEndOfTheDay);

            List<ParametersOfDataMiningNS> listOfHrefsToLoad = e.Argument as List<ParametersOfDataMiningNS>;
            int iTotal = listOfHrefsToLoad.Count;
			if (iTotal > 0)
			{
				int NSOUD_MaxNumThreads = (int)this.NS_nudMaxNumberOfThreads.Value;
				int processed = 0;
				int numThreads = Math.Min(NSOUD_MaxNumThreads, iTotal);
				ManualResetEvent[] resetEvents = new ManualResetEvent[numThreads];
				string sIdNs;
				int iPosition;

				foreach (ParametersOfDataMiningNS par in listOfHrefsToLoad)
				{
					resetEvents[processed % NSOUD_MaxNumThreads] = new ManualResetEvent(false);
					iPosition = par.URL.LastIndexOf('/');
					sIdNs = par.URL.Substring(iPosition + 1);
					iPosition = sIdNs.IndexOf('?');
					sIdNs = sIdNs.Substring(0, iPosition);
					if (!NS_ThreadPoolDownload.CheckForForeignId(sIdNs))
					{
						NS_ThreadPoolDownload tpd = new NS_ThreadPoolDownload(this, resetEvents[processed % NSOUD_MaxNumThreads], par.FullPathDirectory, this.NS_actualLoadedDate, par.SpZnInSearchResults);
						ThreadPool.QueueUserWorkItem(tpd.DownloadDocument, (object)par.URL);

						if (++processed % NSOUD_MaxNumThreads == 0)
						{
							// počkám si, až všechny zkončí...
							WaitHandle.WaitAll(resetEvents);
						}
					}
					else
					{
						/* Z tohoto mraku nezaprší... Neprošel kontrolou... */
						resetEvents[processed++ % NSOUD_MaxNumThreads].Set();
						WriteIntoLogDuplicity("IDExternal [{0}] je v jiz databazi!", sIdNs);
					}

					int percentageProgress = (processed * 100) / iTotal;
					bgLoadingData.ReportProgress(percentageProgress);
				}
				// počkám si, až skutečně všechno skončí...
				WaitHandle.WaitAll(resetEvents);
			}

            /* Do výsledku nastavíme pořadové číslo první nevyužité citace */
            e.Result = NS_ThreadPoolDownload.GetCitationServiceData();
            bgLoadingData.ReportProgress(100);
        }

        private void NS_bgLoadData_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (DateTime.Compare(this.NS_actualLoadedDate.Date, this.NS_maximumLoadedDate.Date) < 0)
            {
                /* aktuální < maximální */
                /* ++ aktuální */
                this.NS_actualLoadedDate = this.NS_actualLoadedDate.AddDays(1);
                BeginProcessActualLoadingDate();
                /* Aktualizuju číslo citace 
                 * Vím, že číslo první nevyužité citace ukládám (sérií menších "workaroundů" do proměnné result
                 */
                this.NS_lastCitationByEndOfTheDay = (Dictionary<int, int>)e.Result;
            }
            else
            {
                /* aktuální >= maximální => projel jsem všechna data */
                FinalizeLogs();
            }
        }

        private string NSOUD_CheckFilledValues()
        {
            StringBuilder sbResult = new StringBuilder();
            /* Greater than zero => This instance is later than value. */
            if (this.NS_dtpDateFrom.Value.CompareTo(this.NS_dtpDateTo.Value) > 0)
            {
                sbResult.AppendLine(String.Format("Datum od [{0}] je větší, než datum do [{1}].", this.NS_dtpDateFrom.Value.ToShortDateString(), this.NS_dtpDateTo.Value.ToShortDateString()));
            }

            /* Je vybrán typ vyhledávání? */
            if (this.NS_cbSearchBy.SelectedIndex == -1)
            {
                sbResult.AppendLine("Vyberte, zda-li chcete vyhledávat dle data rozhodnutí, či data vystavení na web.");
            }

            if (String.IsNullOrWhiteSpace(this.txtWorkingFolder.Text))
            {
                sbResult.AppendLine("Pracovní složka (místo pro uložení surových dat) musí být vybrána.");
            }

            return sbResult.ToString();
        }

        private bool NS_Click()
        {
            string sError = NSOUD_CheckFilledValues();
            if (!String.IsNullOrEmpty(sError))
            {
                MessageBox.Show(this, sError, ERROR_CHECKING_OUTPUT, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return false;
            }

            this.btnMineDocuments.Enabled = false;

            this.NS_minimumLoadedDate = this.NS_dtpDateFrom.Value;
            this.NS_maximumLoadedDate = this.NS_dtpDateTo.Value;

            this.NS_actualLoadedDate = this.NS_minimumLoadedDate;

            this.NS_lastCitationByEndOfTheDay = new Dictionary<int,int>();

            BeginProcessActualLoadingDate();
            return true;
        }

        private void BeginProcessActualLoadingDate()
        {
            NS_loadedHrefs.Clear();

            string sUrl = String.Format(NS_COURT, NS_actualLoadedDate.Day, NS_actualLoadedDate.Month, NS_actualLoadedDate.Year, NS_MAXIMUM_NUMBER_SHOWED_RESULTS);
            if (NS_cbSearchBy.SelectedItem.ToString() == "Datum rozhodnutí")
            {
                sUrl = sUrl.Replace("datum_predani_na_web", "datum_rozhodnuti");
            }

            browser.Navigate(sUrl);
        }

        private void NS_browser_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            if (e.Url.AbsolutePath.Contains("$$WebSearch1"))
            {
                gbProgressBar.Text = String.Format("{0}/{1}: 1/2: Načítání odkazů...", this.NS_actualLoadedDate.ToShortDateString(), this.NS_maximumLoadedDate.ToShortDateString());

                HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(browser.Document.Body.OuterHtml);
                HtmlAgilityPack.HtmlNodeCollection seznamUzluOdkazuDokumentu = doc.DocumentNode.SelectNodes("//a[@href]");
                int iProcessed = 1;
                int iTotal = seznamUzluOdkazuDokumentu.Count;
				string sHref;
                foreach (HtmlAgilityPack.HtmlNode el in seznamUzluOdkazuDokumentu)
                {
                    /* Naštěstí odkaz obsahuje text se spisovou značkou, takže se nemusí nic dalšího hledat ani parsovat*/
                    sHref = el.Attributes["href"].Value;
                    if (sHref.Contains(NS_COURT_HREF_CONTENT) && !sHref.Contains(NS_COURT_HREF_SEARCHING_QUERY_CONTENT))
                    {
                        //href="/JudikaturaNS_new/judikatura_prevedena2.nsf/zip?openagent&amp;SearchView&amp;Query=[datum_predani_na_web]%3E%3D19%2F12%2F2011%20AND%20[datum_predani_na_web]%3C%3D2%2F1%2F2012&amp;SearchMax=0&amp;pohled=1&amp;Start=1&amp;Count=1000&amp;pohled=$$WebSearch1"
                        string url = String.Format("http://novyweb.nsoud.cz{0}", sHref);
                        /* Může se stát, že spisová značka končí "- římské číslo", chci se zbavit mezery.
                         * Orientovat se podle římského čísla by bylo zbytečně komplikované (ale možná na to v budoucnu dojde),
                         * orientujme se podle pomlčky. Pokud tam je pomlčka, tak vše za ní zbavíme mezer zleva a vložíme do výsledku
                         * ...
                         */
                        string spZn = el.InnerHtml.Trim();

                        /* Navíc obecně - I. smažeme, protože to ve výsledku vůbec nechceme mít! */
						// Je to jinak - zde se nic dalšího se spisovou značkou dít nebude
						//spZn = Regex.Replace(spZn, @"\s*-\s*I\.?$", String.Empty);

						//int idxPomlckaSpZn = spZn.LastIndexOf('-');
						//if (idxPomlckaSpZn != -1 && (idxPomlckaSpZn + 1) < spZn.Length)
						//{
						//    /* Je tam pomlčka a není to poslední znak => XXX-YYY uděláme XXX-TrimStart(YYY)*/
						//    spZn = String.Format("{0}-{1}", spZn.Substring(0, idxPomlckaSpZn), spZn.Substring(idxPomlckaSpZn+1).TrimStart());
						//}

                        NS_loadedHrefs.Add(new ParametersOfDataMiningNS(url, this.txtWorkingFolder.Text, spZn));
                    }
                    this.processedBar.Value = iProcessed++ / iTotal;
                }

                gbProgressBar.Text = String.Format("{0}/{1}: 2/2: Načítání dokumentů...", this.NS_actualLoadedDate.ToShortDateString(), this.NS_maximumLoadedDate.ToShortDateString());
                if (NS_loadedHrefs.Count == NS_MAXIMUM_NUMBER_SHOWED_RESULTS)
                {
                    MessageBox.Show(this, String.Format("!!!!!!!!!!!!!!!{0}Počet nalezených odkazů pro den {1} dosáhl maximálního počtu {2}.{0}Skutečný obsah dokumentů bude nejspíše vyšší. Je třeba nahlásit{0}!!!!!!!!!!!!!!!", Environment.NewLine, NS_actualLoadedDate.ToShortDateString(), NS_MAXIMUM_NUMBER_SHOWED_RESULTS), "Stahování judikatury Nejvyššího soudu", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    MessageBox.Show(this, String.Format("Kliknutím na OK spustíte stahování nalezených dokumentů pro datum {0}", NS_actualLoadedDate.ToShortDateString()), "Stahování judikatury Nejvyššího soudu", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                bgLoadingData.RunWorkerAsync(NS_loadedHrefs);
            }
        }
    }
}