using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.ComponentModel;
using System.Windows.Forms;
using System.Data.SqlClient;
using DataMiningCourts.Properties;
using UtilityBeck;

namespace DataMiningCourts
{
    public partial class FrmCourts : Form
    {
        private static string SNI_SEARCH_RESULT = @"http://www.nsoud.cz/Judikaturans_new/judikatura_vks.nsf/$$WebSearch1?SearchView&Query=[datum_rozhodnuti]%3E%3D{0}%2F{1}%2F{2}%20AND%20[datum_rozhodnuti]%3C%3D{3}%2F{4}%2F{5}&SearchMax=0Start=1&Count=1000&pohled=1&searchOrder=4";
        private static string SNI_LINK_CONTENT = "WebSearch";
        private static string SNI_PAGE_PREFIX = "http://www.nsoud.cz{0}";
        private static string SNI_PAGE_LINKPAGE = "http://www.nsoud.cz/Judikaturans_new/judikatura_vks.nsf/WebSpreadSearch";

        private bool SNI_Click()
        {
            string sError = SNI_CheckFilledValues();
            if (!String.IsNullOrEmpty(sError))
            {
                MessageBox.Show(this, sError, ERROR_CHECKING_OUTPUT, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return false;
            }

            btnMineDocuments.Enabled = false;
            loadedHrefs.Clear();

            var url = String.Format(SNI_SEARCH_RESULT, SNI_dtpDateFrom.Value.Day, SNI_dtpDateFrom.Value.Month, SNI_dtpDateFrom.Value.Year, SNI_dtpDateTo.Value.Day, SNI_dtpDateTo.Value.Month, SNI_dtpDateTo.Value.Year);
            browser.Navigate(url);

            return true;
        }

        private string SNI_CheckFilledValues()
        {
            var sbResult = new StringBuilder();
            /* Greater than zero => This instance is later than value. */
            if (SNI_dtpDateFrom.Value.CompareTo(this.SNI_dtpDateTo.Value) > 0)
            {
                sbResult.AppendLine(String.Format("Datum od [{0}] je větší, než datum do [{1}].", this.SNI_dtpDateFrom.Value.ToShortDateString(), this.SNI_dtpDateTo.Value.ToShortDateString()));
            }

            if (string.IsNullOrWhiteSpace(this.txtWorkingFolder.Text))
            {
                sbResult.AppendLine("Pracovní složka (místo pro uložení surových dat, musí být vybrána.");
            }

            return sbResult.ToString();
        }

        private void SNI_bgLoadData_DoWork(object sender, DoWorkEventArgs e)
        {
            var listOfHrefsToLoad = e.Argument as List<ParametersOfDataMining>;
            int total = listOfHrefsToLoad.Count;
            if (total > 0)
            {
                int processed = 0;
                int numThreads = Math.Min((int)this.NS_nudMaxNumberOfThreads.Value, total);
                ManualResetEvent[] resetEvents = new ManualResetEvent[numThreads];
                /* Vložím dotaz pro získávání čísel citací z DB*/
                SNI_ThreadPoolDownload.InitializeCitationService(Courts.cSNI);

                foreach (ParametersOfDataMining par in listOfHrefsToLoad)
                {
                    if (SNI_ThreadPoolDownload.CheckForForeignId(par.FileName))
                    {
                        /* Z tohohle mraku nezaprší! */
                        --total;
                        WriteIntoLogDuplicity("IdExternal [{0}] je v jiz databazi!", par.FileName);
                        continue;
                    }

                    resetEvents[processed % numThreads] = new ManualResetEvent(false);

                    var tpd = new SNI_ThreadPoolDownload(this, resetEvents[processed % numThreads], par.FullPathDirectory, par.FileName);
                    ThreadPool.QueueUserWorkItem(tpd.DownloadDocument, (object)par.URL);

                    if (++processed % numThreads == 0)
                    {
                        // počkám si, až všechny zkončí...
                        WaitHandle.WaitAll(resetEvents);
                    }

                    int percentageProgress = (processed * 100) / total;
                    bgLoadingData.ReportProgress(percentageProgress);
                }
            }
            FinalizeLogs();
            bgLoadingData.ReportProgress(100);
        }

        private void SNI_browser_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            if (e.Url.AbsolutePath.Contains("$$WebSearch1"))
            {
                gbProgressBar.Text = "1/2: Načítání odkazů...";

                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(browser.Document.Body.OuterHtml);
                HtmlAgilityPack.HtmlNodeCollection seznamUzluOdkazuDokumentu = doc.DocumentNode.SelectNodes("//a[@href]");
                int processed = 1;
                int total = seznamUzluOdkazuDokumentu.Count;

                foreach (HtmlAgilityPack.HtmlNode el in seznamUzluOdkazuDokumentu)
                {
                    var odkaz = el.Attributes["href"].Value;
                    if (odkaz.Contains(SNI_LINK_CONTENT))
                    {
                        var url = string.Format(SNI_PAGE_PREFIX, odkaz);
                        var idxFilename = url.LastIndexOf('/') + 1;
                        var iQuestonMark = url.LastIndexOf('?');
                        var fileName = url.Substring(idxFilename, iQuestonMark - idxFilename);
                        var fullPath = String.Format(@"{0}\{1}.html", this.txtWorkingFolder.Text, fileName);
                        if (!File.Exists(fullPath))
                        {
                            // Foreign id will be checked later
                            var p = new ParametersOfDataMining(url, this.txtWorkingFolder.Text);
                            p.FileName = fileName;
                            loadedHrefs.Add(p);
                        }
                    }
                    this.processedBar.Value = processed++ / total;
                }

                gbProgressBar.Text = "2/2: Načítání dokumentů...";
                bgLoadingData.RunWorkerAsync(loadedHrefs);
            }
        }
    }
}
