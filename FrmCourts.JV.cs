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
        public string JV_SEARCH_RESULT = @"https://apps.odok.cz/djv-agenda-list?year={0}";
        public string JV_PAGE_PREFIX = @"https://apps.odok.cz{0}";
        public string JV_LINK_CONTENT = "djv-agenda?date=";



        private string JV_CheckFilledValues()
        {
            var sbResult = new StringBuilder();

            if (string.IsNullOrWhiteSpace(this.txtWorkingFolder.Text))
            {
                sbResult.AppendLine("Pracovní složka (místo pro uložení surových dat) musí být vybrána.");
            }

            if (string.IsNullOrWhiteSpace(this.txtOutputFolder.Text))
            {
                sbResult.AppendLine("Výstupní složka (místo pro uložení hotových dat) musí být vybrána.");
            }

            if (string.IsNullOrWhiteSpace(this.txtBackupFolder.Text))
            {
                sbResult.AppendLine("Zálohová složka (místo pro přesunutí již stažených dat) musí být vybrána.");
            }

            return sbResult.ToString();
        }

        private bool JV_Click()
        {
            var sError = JV_CheckFilledValues();
            if (!string.IsNullOrEmpty(sError))
            {
                MessageBox.Show(this, sError, ERROR_CHECKING_OUTPUT, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return false;
            }

            btnMineDocuments.Enabled = false;
            processedBar.Value = 0;

            BeginProcessActualLoadingDateJV();

            return true;
        }

        private void BeginProcessActualLoadingDateJV()
        {
            loadedHrefs.Clear();

            var url = string.Format(JV_SEARCH_RESULT, JV_Year.Value);
            browser.Navigate(url);
        }

        private void JV_bgLoadData_DoWork(object sender, DoWorkEventArgs e)
        {
            JV_ThreadPoolDownload.InitializeCitationService(Courts.cJV);

            var listOfHrefsToLoad = e.Argument as List<ParametersOfDataMining>;
            var total = listOfHrefsToLoad.Count;
            if (total > 0)
            {
                int processed = 0;
                int numThreads = Math.Min((int)this.NS_nudMaxNumberOfThreads.Value, total);
                var resetEvents = new ManualResetEvent[numThreads];

                foreach (ParametersOfDataMining par in listOfHrefsToLoad)
                {
                    resetEvents[processed % numThreads] = new ManualResetEvent(false);
                    var tpd = new JV_ThreadPoolDownload(this, resetEvents[processed % numThreads], par.FullPathDirectory, par.FileName);
                    ThreadPool.QueueUserWorkItem(tpd.DownloadDocument, (object)par.URL);

                    if (++processed % numThreads == 0)
                    {
                        WaitHandle.WaitAll(resetEvents);
                    }

                    var percentageProgress = (processed * 100) / total;
                    bgLoadingData.ReportProgress(percentageProgress);
                }
            }
            bgLoadingData.ReportProgress(100);
        }

        private void JV_bgLoadData_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            FinalizeLogs();
        }

        private void JV_browser_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            if (e.Url.AbsoluteUri.Contains(string.Format("year={0}", JV_Year.Value)))
            {
                gbProgressBar.Text = "1/2: Načítání odkazů...";

                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(browser.Document.Body.OuterHtml);
                var toDownload = doc.DocumentNode.SelectNodes("//div[@class='content-main']//div[span[contains(text(), 'Usnesení')]]//span//a[@href]");

                if (toDownload != null)
                {
                    var processed = 1;
                    var total = toDownload.Count;

                    foreach (HtmlNode el in toDownload)
                    {
                        var link = el.Attributes["href"].Value;
                        if (link.Contains(JV_LINK_CONTENT))
                        {
                            var url = string.Format(JV_PAGE_PREFIX, link);
                            var fileName = url.Substring(url.LastIndexOf('=') + 1);
                            var fullPath = String.Format(@"{0}\{1}.html", this.txtWorkingFolder.Text, fileName);
                            if (!File.Exists(fullPath))
                            {
                                var p = new ParametersOfDataMining(url, txtWorkingFolder.Text);
                                p.FileName = fileName;
                                loadedHrefs.Add(p);
                            }
                        }
                        processedBar.Value = processed++ / total;
                    }
                }

                gbProgressBar.Text = "2/2: Načítání dokumentů...";
                bgLoadingData.RunWorkerAsync(loadedHrefs);
            }
        }
    }
}
