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
using System.Web;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using UtilityBeck;

namespace DataMiningCourts
{
    partial class FrmCourts
    {
        private static string VS_INDEX = @"https://www.justice.cz/web/vrchni-soud-v-praze/ruzne?clanek=2307505";

        private static string ROOT_URL = @"https://www.justice.cz/";

        private static readonly Regex regDate = new Regex(@"(Praha|Olomouc)\s(?<date>\d{1,2}.\s\w*\s\d{4})");

        private static readonly Regex regSpZn = new Regex(@"^(?<SpZn>Ncp \d*/\d{4})");

        BlockingCollection<HtmlAgilityPack.HtmlNode> VS_seznamElementuKeZpracovani;

        BlockingCollection<PdfToDownload> VS_seznamPdfKeStazeni;

        private enum VSAkce { vsPrvniUlozeniOdkazu };

        private VSAkce aktualniVSAkce;

        private int VS_aktualniStrankaKeZpracovani = 0;

        private int VS_celkemStranekKeZpracovani;

        private int VS_XML_celkemZaznamuKeZpracovani = 1;

        private int VS_PDF_aktualniZaznamKeZpracovani = 1;

        private int VS_PDF_celkemZaznamuKeZpracovani;

        private int VS_XML_aktualniZaznamKeZpracovani;

        bool VS_DocumentsWereDownloaded = false;

        CitationService VS_citationService;

        private void VS_SpustVlaknoProZpracovaniElementu()
        {
            Task.Factory.StartNew(() =>
            {
                HtmlNode data = null;
                using (SqlConnection conn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["LexData"].ConnectionString))
                {
#if !DUMMY_DB
                    conn.Open();
#endif

                    while (!VS_seznamElementuKeZpracovani.IsCompleted)
                    {
                        while (!VS_seznamElementuKeZpracovani.IsCompleted && !VS_seznamElementuKeZpracovani.TryTake(out data, 2000)) ;
                        if (data != null && data.Attributes["href"] != null)
                        {
                            var spZnFileName = data.InnerText.Trim();
                            var spZn = spZnFileName;
                            var matchSpZn = regSpZn.Match(spZn);
                            if (matchSpZn.Success)
                            {
                                spZn = matchSpZn.Groups["SpZn"].Value;
                            }

                            Utility.CreateDocumentName("J", spZnFileName, null, out string fileFullPath);
                            fileFullPath = string.Format(@"{0}\{1}", this.txtWorkingFolder.Text, fileFullPath);

                            var extUrl = ROOT_URL + data.Attributes["href"].Value;
                            VS_seznamPdfKeStazeni.Add(new PdfToDownload(extUrl, fileFullPath + ".pdf"));
                            VS_VytvoreniXml(spZn, extUrl, fileFullPath + ".xml", spZnFileName);
                        }
                    }
                }
            });
        }

        private void VS_VytvoreniXml(string spZn, string extUrl, string xmlFullPath, string fullSpZn)
        {
            var newXmlDocument = new XmlDocument();
#if LOCAL_TEMPLATES
                                newXmlDocument.Load(@"Templates-J-Downloading\Template_J_VS.xml");
#else
            string sPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            newXmlDocument.Load(Path.Combine(sPath, @"Templates-J-Downloading\Template_J_VS.xml"));
#endif

            var xnExt = newXmlDocument.SelectSingleNode("//id-external");
            xnExt.InnerText = extUrl;

            var xnAuthor = newXmlDocument.DocumentElement.SelectSingleNode("//autor/item");
            xnAuthor.InnerText = fullSpZn.Contains("VS v Olomouci") ? "Vrchní soud v Olomouci" : "Vrchní soud v Praze";

            var xnCitace = newXmlDocument.DocumentElement.SelectSingleNode("//citace");
            xnCitace.InnerText = spZn;

            var xnDatSchvaleni = newXmlDocument.DocumentElement.SelectSingleNode("//datschvaleni");
            xnDatSchvaleni.InnerText = DateTime.MinValue.ToString("yyyy-MM-dd");

            newXmlDocument.Save(xmlFullPath);
            ++VS_XML_aktualniZaznamKeZpracovani;
        }

        private void VS_SpustVlaknoProZpracovaniPdf()
        {
            Task.Factory.StartNew(() =>
            {
                var UpdateProgress = new UpdateDownloadProgressDelegate(VS_UpdateDownloadProgressSafe);

                PdfToDownload data = null;
                while (!VS_seznamPdfKeStazeni.IsCompleted)
                {
                    while (!VS_seznamPdfKeStazeni.IsCompleted && !VS_seznamPdfKeStazeni.TryTake(out data, 2000)) ;
                    if (data != null)
                    {
                        if (!File.Exists(data.pdf))
                        {
                            Uri uriPdfToDownload = new Uri(data.url);
                            try
                            {
                                clientPdfDownload.DownloadFile(uriPdfToDownload, data.pdf);
                            }
                            catch (WebException ex)
                            {
                                WriteIntoLogCritical(String.Format("PDF dokumentu se z webové stránky {0} nepodařilo stáhnout.{1}\t[{2}]", data.url, Environment.NewLine, ex.Message));
                            }
                        }
                        this.processedBar.Invoke(UpdateProgress, new object[] { false });
                        ++VS_PDF_aktualniZaznamKeZpracovani;
                    }
                }
                this.processedBar.BeginInvoke(UpdateProgress, new object[] { true });
                FinalizeLogs();
                MessageBox.Show("1) Spusťte program pro převod pdf dokumentů na doc dokumenty!\r\n2) Vyberte všechny stažené doc dokumenty a převeďte je!\r\n3) Stiskněte tlačítko doc->xml", "Dokončení převodu VS", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
        }

        private int VS_ComputeActuallDownloadProgress()
        {
            return (VS_XML_aktualniZaznamKeZpracovani + VS_PDF_aktualniZaznamKeZpracovani) / (VS_XML_celkemZaznamuKeZpracovani + VS_PDF_celkemZaznamuKeZpracovani) * 100;
        }

        void VS_UpdateDownloadProgressSafe(bool forceComplete)
        {

#if DEBUG
            this.gbProgressBar.Text = String.Format("Zpracováno {0}/{1} stránek | {2}/{3} XML | {4}/{5} PDF => {6}%",
                VS_aktualniStrankaKeZpracovani, VS_celkemStranekKeZpracovani, VS_XML_aktualniZaznamKeZpracovani,
                VS_XML_celkemZaznamuKeZpracovani, VS_PDF_aktualniZaznamKeZpracovani, VS_PDF_celkemZaznamuKeZpracovani, this.processedBar.Value);
#endif
            int value = VS_ComputeActuallDownloadProgress();
            if (forceComplete)
            {
                value = this.processedBar.Maximum;
            }
            this.processedBar.Value = value;
            var maximumReached = (this.processedBar.Value == this.processedBar.Maximum);
            this.btnMineDocuments.Enabled = this.VS_btnWordToXml.Enabled = this.tcCourts.Enabled = VS_DocumentsWereDownloaded = maximumReached;
        }

        void VS_UpdateProgressSafe(int value)
        {
            this.processedBar.Value = value;
            if (value == this.processedBar.Maximum)
            {
                MessageBox.Show(this, "Hotovo", "Převod VS", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private string VS_CheckFilledValues()
        {
            var sbResult = new StringBuilder();
            if (String.IsNullOrWhiteSpace(this.txtWorkingFolder.Text))
            {
                sbResult.AppendLine("Pracovní složka (místo pro uložení surových dat) musí být vybrána.");
            }
            return sbResult.ToString();
        }

        private bool VS_Click()
        {
            var sError = VS_CheckFilledValues();
            if (!String.IsNullOrEmpty(sError))
            {
                MessageBox.Show(this, sError, ERROR_CHECKING_OUTPUT, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return false;
            }

            this.btnMineDocuments.Enabled = false;
            this.VS_DocumentsWereDownloaded = false;
            this.aktualniVSAkce = VSAkce.vsPrvniUlozeniOdkazu;
            this.processedBar.Value = 0;

            VS_seznamElementuKeZpracovani = new BlockingCollection<HtmlNode>();
            VS_seznamPdfKeStazeni = new BlockingCollection<PdfToDownload>();

            VS_SpustVlaknoProZpracovaniElementu();
            VS_SpustVlaknoProZpracovaniPdf();

            browser.Navigate(VS_INDEX);
            return true;
        }

        private void VS_btnToXml_Click(object sender, EventArgs e)
        {
            if (!VS_DocumentsWereDownloaded &&
                     MessageBox.Show(this, "Nedošlo ke stažení dokumentů z webu, přejete si přesto převést obsah pracovní složky?", "VS", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == System.Windows.Forms.DialogResult.No)
            {
                return;
            }
            this.VS_btnWordToXml.Enabled = false;
            this.processedBar.Value = 0;

            if (!this.citationNumberGenerator.ContainsKey(Courts.cVS))
            {
                this.citationNumberGenerator.Add(Courts.cVS, new CitationService(Courts.cVS));
            }
            this.VS_citationService = this.citationNumberGenerator[Courts.cVS];

            UpdateConversionProgressDelegate UpdateProgress = new UpdateConversionProgressDelegate(VS_UpdateProgressSafe);
            ConvertDelegate Convert = new ConvertDelegate(VS_ConvertOneDocToXml);

            TransformVSDocInWorkingFolderToXml(UpdateProgress, Convert);

            this.VS_btnWordToXml.Enabled = true;
        }

        private void TransformVSDocInWorkingFolderToXml(UpdateConversionProgressDelegate UpdateProgress, ConvertDelegate ConvertDelegate)
        {
            DirectoryInfo di = new DirectoryInfo(this.txtWorkingFolder.Text);
            FileInfo[] docs = di.GetFiles().Where(f => (f.Extension == ".doc" || f.Extension == ".rtf" || f.Extension == ".docx") && f.Name.StartsWith("J")).ToArray();

            var taskCounter = 1;
            var totalCount = docs.Length + 1;
            foreach (FileInfo fi in docs)
            {
                string docFullName = fi.FullName;

                using (SqlConnection conn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["LexData"].ConnectionString))
                {
                    conn.Open();
                    string sDocumentName = Path.GetFileNameWithoutExtension(docFullName);
                    string sOutputDocumentFolder = String.Format(@"{0}\{1}", this.txtOutputFolder.Text, sDocumentName);
                    if (!Directory.Exists(sOutputDocumentFolder))
                    {
                        Directory.CreateDirectory(sOutputDocumentFolder);
                    }
                    string sPathXmlHeader = String.Format(@"{0}\{1}.xml", Path.GetDirectoryName(docFullName), sDocumentName);
                    string sPathWordXml = String.Format(@"{0}\W_{1}-0.xml", sOutputDocumentFolder, sDocumentName);
                    string sPathWordXmlXml = String.Format(@"{0}\W_{1}-0.xml.xml", sOutputDocumentFolder, sDocumentName);
                    OpenFileInWordAndSaveInWXml(docFullName, sPathWordXml);
                    File.Copy(sPathWordXml, sPathWordXmlXml, true);
                    string sPathOutputXml = String.Format(@"{0}\{1}.xml", sOutputDocumentFolder, sDocumentName);
                    File.Copy(sPathXmlHeader, sPathOutputXml, true);
                    string sPathPdfDocument = String.Format(@"{0}\{1}.pdf", Path.GetDirectoryName(docFullName), sDocumentName);
                    try
                    {
                        if (ConvertDelegate(sOutputDocumentFolder, sDocumentName, conn))
                        {
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

                                XmlAttribute a = dOut.CreateAttribute("source-file");
                                a.Value = string.Format("Original_{0}-1.pdf", sDocumentName);
                                dOut.DocumentElement.Attributes.Append(a);
                                dOut.Save(sPathOutputXml);
                            }
                            else
                            {
                                WriteIntoLogCritical("{0}: Originální pdf dokument nebyl, v pracovní složce, nalezen!", sDocumentName);
                            }

                            File.Delete(sPathWordXmlXml);
                            File.Delete(docFullName);
                            File.Delete(sPathXmlHeader);
                        }
                        else
                        {
                            if (Directory.Exists(sOutputDocumentFolder))
                            {
                                Directory.Delete(sOutputDocumentFolder, true);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteIntoLogCritical("{0}: Exception=[{1}]", sDocumentName, ex.Message);
                    }
                }
                var progress = (taskCounter * 100 / (totalCount));
                this.processedBar.Invoke(UpdateProgress, new object[] { progress });
                taskCounter++;
            }

            if (this.processedBar.Value != 100)
            {
                this.processedBar.BeginInvoke(UpdateProgress, new object[] { 100 });
            }
            FinalizeLogs();
        }

        private void ProvedVSAkciUlozeniOdkazu()
        {
            HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(browser.Document.Body.OuterHtml);

            VS_PDF_celkemZaznamuKeZpracovani = VS_XML_celkemZaznamuKeZpracovani = VS_celkemStranekKeZpracovani = 1;
            var nodes = doc.DocumentNode.SelectNodes(".//div[contains(@class,'main-article')]/p/a");
            VS_PDF_celkemZaznamuKeZpracovani = VS_XML_celkemZaznamuKeZpracovani = nodes.Count;

            if (VS_XML_celkemZaznamuKeZpracovani > 0) ;
            {
                foreach (HtmlNode uzelKPridani in nodes)
                {
                    VS_seznamElementuKeZpracovani.Add(uzelKPridani);
                }
            }

            if (VS_aktualniStrankaKeZpracovani == VS_celkemStranekKeZpracovani)
            {
                VS_seznamElementuKeZpracovani.CompleteAdding();
                return;
            }
        }

        private void VS_browser_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            if (VS_seznamElementuKeZpracovani.IsAddingCompleted)
            {
                return;
            }

            switch (aktualniVSAkce)
            {
                case VSAkce.vsPrvniUlozeniOdkazu:
                    ProvedVSAkciUlozeniOdkazu();
                    break;
            }
            VS_UpdateDownloadProgressSafe(false);
        }

        private bool VS_ConvertOneDocToXml(string pPathFolder, string sDocumentName, SqlConnection pConn)
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

            var sPathResultXml = String.Format(@"{0}\{1}.xml", pPathFolder, sDocumentName);
            XmlDocument dOut = new XmlDocument();
            dOut.Load(sPathResultXml);

            // linking
            Linking oLinking = new Linking(pConn, "cs", null);
            oLinking.Run(0, sDocumentName, dOut, 17);
            dOut = oLinking.LinkedDocument;

            var xnHtml = dOut.SelectSingleNode("//html-text");
            if (string.IsNullOrWhiteSpace(xnHtml.InnerText))
            {
                return false;
            }
            UtilityXml.RemoveMultiSpaces(ref xnHtml);

            string sSpisovaZnacka = dOut.DocumentElement.FirstChild.SelectSingleNode("./citace").InnerText.ToLower();
            sSpisovaZnacka = Utility.GetReferenceNumberNorm(sSpisovaZnacka, out string sNormValue2);

            int i = 0;
            XmlNode xn = xnHtml.FirstChild;
            var toDelete = new List<XmlNode>();
            var zakladniPredpisy = new List<string>();
            var veta = string.Empty;

            var xnCitace = dOut.DocumentElement.SelectSingleNode("//citace");
            var specifikaceHit = false;
            while ((++i < 15) && (xn != null))
            {
                var innerText = xn.InnerText.Trim();
                if (innerText == "USNESENÍ")
                {
                    break;
                }
                else
                {
                    toDelete.Add(xn);
                }

                if (innerText == xnCitace.InnerText.Trim())
                {
                    continue;
                }
                if (specifikaceHit && !string.IsNullOrWhiteSpace(innerText))
                {
                    zakladniPredpisy.Add(innerText);
                }
                if (innerText.StartsWith("specifikace:"))
                {
                    veta = innerText.Replace("specifikace:", string.Empty).Trim();
                    specifikaceHit = true;
                }
                xn = xn.NextSibling;
            }

            foreach (var item in toDelete)
            {
                if (item.ParentNode != null)
                {
                    item.ParentNode.RemoveChild(item);
                }
            }

            this.CorrectionDocumentVS(ref dOut);

            var xnVeta = dOut.SelectSingleNode("//veta");
            var xnP = dOut.CreateElement("p");
            xnVeta.AppendChild(xnP);
            xnP.InnerText = veta;

            if (zakladniPredpisy.Count > 0)
            {
                var xnZaklPredpis = dOut.SelectSingleNode("//zakladnipredpis");
                LinkingHelper.AddBaseLaws(dOut, string.Join(Environment.NewLine, zakladniPredpisy), xnZaklPredpis);
            }

            var date = DateTime.MinValue;
            var matchDate = regDate.Match(dOut.InnerText);
            if (matchDate.Success)
            {
                var xnDateApproval = dOut.SelectSingleNode("//datschvaleni");
                if (!DateTime.TryParse(Utility.ConvertLongDateIntoUniversalDate(matchDate.Groups["date"].Value), out date))
                {
                    date = DateTime.MinValue;
                }
                xnDateApproval.InnerText = date.ToString("yyyy-MM-dd");
            }

            var citationNumber = this.VS_citationService.GetNextCitation(date.Year);
            var sCitation = String.Format("Výběr {0}/{1}", citationNumber, date.Year);
            var xnCitation = dOut.DocumentElement.SelectSingleNode("./judikatura-section/header-j/citace");
            xnCitation.InnerText = sCitation;

            var htmlText = dOut.SelectSingleNode("//html-text");

            this.RemoveOrphanedMlTags(htmlText);

            this.CorrectNestedParagraphs(htmlText.ChildNodes);

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
            VS_citationService.CommitCitationForAYear(date.Year);
            return true;
        }

        private void CorrectionDocumentVS(ref XmlDocument pD)
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
    }
}