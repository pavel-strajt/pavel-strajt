using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Windows.Forms;
using System.Data.SqlClient;
using System.Xml;
using System.Collections;
using UtilityBeck;
using System.Threading.Tasks;
using HtmlAgilityPack;
using BeckLinking;
using System.ComponentModel;
using System.Threading;

namespace DataMiningCourts
{
    public partial class FrmCourts : Form
    {
        private static string ESLP_SEARCH_RESULT = @"http://eslp.justice.cz/justice/judikatura_eslp.nsf/$$WebSearch1?SearchView&Query=%5BDatumRozhodnuti%5D%3E%3D{0}%2F{1}%2F{2}%20AND%20%5BDatumRozhodnuti%5D%3C%3D{3}%2F{4}%2F{5}&SearchMax=0Start=1&Count=1500&pohled=1&searchorder=4";
        private static string ESLP_LINK_CONTENT = "WebSearch";
        private static string ESLP_PAGE_PREFIX = "http://eslp.justice.cz{0}";
        private static string ESLP_PAGE = "http://eslp.justice.cz";

        private string ESLP_CheckFilledValues()
        {
            StringBuilder sbResult = new StringBuilder();
            /* Greater than zero => This instance is later than value. */
            if (this.ESLP_dtpDateFrom.Value.CompareTo(this.ESLP_dtpDateTo.Value) > 0)
            {
                sbResult.AppendLine(String.Format("Datum od [{0}] je větší, než datum do [{1}].", this.US_dtpDateFrom.Value.ToShortDateString(), this.US_dtpDateTo.Value.ToShortDateString()));
            }

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

        private bool ESLP_Click()
        {
            var sError = ESLP_CheckFilledValues();
            if (!string.IsNullOrEmpty(sError))
            {
                MessageBox.Show(this, sError, ERROR_CHECKING_OUTPUT, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return false;
            }

            btnMineDocuments.Enabled = false;
            processedBar.Value = 0;

            BeginProcessActualLoadingDateESLP();

            return true;
        }

        private void BeginProcessActualLoadingDateESLP()
        {
            loadedHrefs.Clear();

            var url = string.Format(ESLP_SEARCH_RESULT, ESLP_dtpDateFrom.Value.Day, ESLP_dtpDateFrom.Value.Month, ESLP_dtpDateFrom.Value.Year, ESLP_dtpDateTo.Value.Day, ESLP_dtpDateTo.Value.Month, ESLP_dtpDateTo.Value.Year);
            browser.Navigate(url);
        }

        private void ESLP_bgLoadData_DoWork(object sender, DoWorkEventArgs e)
        {
            ESLP_ThreadPoolDownload.InitializeCitationService(Courts.cESLP);

            var listOfHrefsToLoad = e.Argument as List<ParametersOfDataMining>;
            var total = listOfHrefsToLoad.Count;
            if (total > 0)
            {
                int processed = 0;
                int numThreads = Math.Min((int)this.NS_nudMaxNumberOfThreads.Value, total);
                var resetEvents = new ManualResetEvent[numThreads];

                string sIdNs;
                int iPosition;

                foreach (ParametersOfDataMining par in listOfHrefsToLoad)
                {
                    resetEvents[processed % numThreads] = new ManualResetEvent(false);
                    iPosition = par.URL.LastIndexOf('/');
                    sIdNs = par.URL.Substring(iPosition + 1);
                    iPosition = sIdNs.IndexOf('?');
                    sIdNs = sIdNs.Substring(0, iPosition);
                    if (!ESLP_ThreadPoolDownload.CheckForForeignId(sIdNs))
                    {
                        var tpd = new ESLP_ThreadPoolDownload(this, resetEvents[processed % numThreads], par.FullPathDirectory, par.FileName);
                        ThreadPool.QueueUserWorkItem(tpd.DownloadDocument, (object)par.URL);

                        if (++processed % numThreads == 0)
                        {
                            // počkám si, až všechny zkončí...
                            WaitHandle.WaitAll(resetEvents);
                        }
                    }
                    else
                    {
                        /* Z tohoto mraku nezaprší... Neprošel kontrolou... */
                        resetEvents[processed++ % numThreads].Set();
                        WriteIntoLogDuplicity("IDExternal [{0}] je v jiz databazi!", sIdNs);
                    }

                    int percentageProgress = (processed * 100) / total;
                    bgLoadingData.ReportProgress(percentageProgress);
                }
            }
            bgLoadingData.ReportProgress(100);
        }

        private void ESLP_bgLoadData_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            FinalizeLogs(false);
        }

        private void ESLP_browser_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            if (e.Url.AbsolutePath.Contains("$$WebSearch1"))
            {
                gbProgressBar.Text = "1/2: Načítání odkazů...";

                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(browser.Document.Body.OuterHtml);
                var seznamUzluOdkazuDokumentu = doc.DocumentNode.SelectNodes("//a[@href]");
                var processed = 1;
                var total = seznamUzluOdkazuDokumentu.Count;

                foreach (HtmlNode el in seznamUzluOdkazuDokumentu)
                {
                    var link = el.Attributes["href"].Value;
                    if (link.Contains(ESLP_LINK_CONTENT))
                    {
                        var url = string.Format(ESLP_PAGE_PREFIX, link);
                        var idxFilename = url.LastIndexOf('/') + 1;
                        var iQuestonMark = url.LastIndexOf('?');
                        var fileName = url.Substring(idxFilename, iQuestonMark - idxFilename);
                        var fullPath = String.Format(@"{0}\{1}.html", this.txtWorkingFolder.Text, fileName);
                        if (!File.Exists(fullPath))
                        {
                            // Foreign id will be checked later
                            var p = new ParametersOfDataMining(url, txtWorkingFolder.Text);
                            p.FileName = fileName;
                            loadedHrefs.Add(p);
                        }
                    }
                    processedBar.Value = processed++ / total;
                }

                gbProgressBar.Text = "2/2: Načítání dokumentů...";
                bgLoadingData.RunWorkerAsync(loadedHrefs);
            }
        }
    }
}
