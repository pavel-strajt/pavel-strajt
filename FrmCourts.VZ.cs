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
        public string VZ_SEARCH_RESULT = @"https://apps.odok.cz/djv-agenda-list?year={0}";
        public string VZ_PAGE_PREFIX = @"https://apps.odok.cz{0}";
        public string VZ_LINK_CONTENT = "djv-agenda?date=";
        public string VZ_INDEX = @"https://apps.odok.cz/djv-agenda-list";


        private string VZ_CheckFilledValues()
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

            return sbResult.ToString();
        }

        private bool VZ_Click()
        {
            var sError = VZ_CheckFilledValues();
            if (!string.IsNullOrEmpty(sError))
            {
                MessageBox.Show(this, sError, ERROR_CHECKING_OUTPUT, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return false;
            }

            btnMineDocuments.Enabled = false;
            processedBar.Value = 0;

            BeginProcessActualLoadingDateVZ();

            return true;
        }

        private void BeginProcessActualLoadingDateVZ()
        {
            loadedHrefs.Clear();

            var url = string.Format(VZ_SEARCH_RESULT, VZ_Year.Value);
            browser.Navigate(url);
        }

        private void VZ_bgLoadData_DoWork(object sender, DoWorkEventArgs e)
        {
            VZ_ThreadPoolDownload.InitializeCitationService(Courts.cJV);

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
                    var tpd = new VZ_ThreadPoolDownload(this, resetEvents[processed % numThreads], par.FullPathDirectory, par.FileName);
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

        private void VZ_bgLoadData_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            FinalizeLogs(false);
        }

        private void VZ_browser_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            if (e.Url.AbsoluteUri.Contains(string.Format("year={0}", VZ_Year.Value)))
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
                        if (link.Contains(VZ_LINK_CONTENT))
                        {
                            var url = string.Format(VZ_PAGE_PREFIX, link);
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
