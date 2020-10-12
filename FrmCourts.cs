
//#define LOCAL_TEMPLATES
//#define DUMMY_DB

using log4net;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.Xml;

namespace DataMiningCourts
{
    public partial class FrmCourts : Form
    {
        ILog Log, ExportLogger, DuplicityLogger, CriticalLogger;

        private static readonly string FILENAME_LOG_EXPORT = "chyby-exportu.txt";
        private static readonly string FILENAME_LOG_DUPLICITY = "chyby-duplicity.txt";
        private static readonly string FILENAME_LOG_CRITICAL = "chyby-kriticke.txt";

        private bool occuredDuplicityError, occuredExportError, occuredCriticalError;

        private static string ERROR_PROCESSING = String.Format("Došlo k chybě při načítání dat.{0}Přejete si zobrazit chybový log?", Environment.NewLine);
        private static string ERROR_CHECKING_OUTPUT = "Chyba na vstupu!";

        public string WORKING_DIRECTORY
        {
            get { return this.txtWorkingFolder.Text; }
        }

        public string XML_DIRECTORY
        {
            get { return this.txtOutputFolder.Text; }
        }

        /// <summary>
        /// List of all posibble courts,
        /// used mainly in DuplicityCheck class
        /// </summary>
        internal enum Courts { cNS, cNSS, cSNI, cUOHS, cUPV, cUS, cESLP, cINS, cJV, cVS };

        /// <summary>
        /// Dictionary for a generation of unique citation numbers
        /// Each court has its own class
        /// </summary>
        Dictionary<Courts, CitationService> citationNumberGenerator = new Dictionary<Courts, CitationService>();

        #region  Related with backgroud work
        private void bgLoadingData_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            processedBar.Value = e.ProgressPercentage;
        }

        protected virtual void bgLoadingData_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            this.btnMineDocuments.Enabled = false;
            gbProgressBar.Text = "Postup načítání výsledků...";
        }
        #endregion


        private class ParametersOfDataMining
        {
            string url;

            public string URL
            {
                get { return this.url; }
            }

            private string fileName;
            public string FileName
            {
                get { return this.fileName; }
                set { this.fileName = value; }
            }


            string fullPathDirectory;

            public string FullPathDirectory
            {
                get { return this.fullPathDirectory; }
            }

            public ParametersOfDataMining(string url, string fullPath)
            {
                this.url = url;
                this.fullPathDirectory = fullPath;
            }
        }

        /// <summary>
        /// We need to pass when downloading from NS
        /// URL,
        /// FileName
        /// Reference number, that has been displayed in the search results
        /// </summary>
        private class ParametersOfDataMiningNS : ParametersOfDataMining
        {
            string referenceNumberInSearchResults;

            public string SpZnInSearchResults
            {
                get { return this.referenceNumberInSearchResults; }
            }

            public ParametersOfDataMiningNS(string url, string fullPath, string pReferenceNumber) : base(url, fullPath)
            {
                this.referenceNumberInSearchResults = pReferenceNumber;
            }
        }

        /// <summary>
        /// Delegete, which is used to update the value of progress bar in the main window from any Thread
        /// </summary>
        /// <param name="forceComplete">If set true, set progress to maximum value</param>
        delegate void UpdateDownloadProgressDelegate(bool forceComplete);

        /// <summary>
        /// Delegete, which is used to set the value of progress bar in the main window from any Thread
        /// </summary>
        /// <param name="value">New value of progress bar</param>
        delegate void UpdateConversionProgressDelegate(int value);

        /// <summary>
        /// Delegate, which is used to convert word document into xml document
        /// </summary>
        /// <returns></returns>
        delegate bool ConvertDelegate(string pPathFolder, string sDocumentName, SqlConnection pConn);

        /// <summary>
        /// Delegate, which is used to update Enabled attribute of some button that is a part of the main form from any Thread
        /// </summary>
        /// <param name="value"></param>
        delegate void SetControlEnable(bool value);

        /// <summary>
        /// Delegate, which is used to update Enabled attribute of tcCourts from any Thread
        /// </summary>
        /// <param name="value"></param>
        void DMC_TcCourtsSetEnable(bool value)
        {
            this.tcCourts.Enabled = value;
        }

        /// <summary>
        /// This function initialize logs and all related variables ...
        /// </summary>
        private void InitializeLogs()
        {
            this.occuredDuplicityError = this.occuredExportError = this.occuredCriticalError = false;
            log4net.GlobalContext.Properties["LogFileName"] = this.txtWorkingFolder.Text;
            log4net.Config.XmlConfigurator.Configure();
            this.Log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
            this.DuplicityLogger = log4net.LogManager.GetLogger("DuplicityLogger");
            this.CriticalLogger = log4net.LogManager.GetLogger("CriticalLogger");
            this.ExportLogger = log4net.LogManager.GetLogger("ExportLogger");
        }

        public void WriteIntoLogDuplicity(string format, params object[] args)
        {
            this.DuplicityLogger.InfoFormat(format, args);
            this.occuredDuplicityError = true;
        }

        public void WriteIntoLogExport(string format, params object[] args)
        {
            this.ExportLogger.InfoFormat(format, args);
            this.occuredExportError = true;
        }

        public void WriteIntoLogCritical(string format, params object[] args)
        {
            this.CriticalLogger.ErrorFormat(format, args);
            this.occuredCriticalError = true;
        }

        /// <summary>
        /// This function close log file and in the case, that in was written into log file something, it enables users to show the content of the log file
        /// </summary>
        private void FinalizeLogs()
        {

            //TODO: overit existenci souboru pred zobrazenim

            MessageBox.Show(String.Format("Stahování dokončeno...{0}{0}Chyby duplicit ... {1}{0}Chyby exportu ... {2}{0}Chyby kritické ... {3}{0}{0}V případě chyb budou po stitsknutí tlačítka OK zobrazeny chybové logy...", Environment.NewLine, this.occuredDuplicityError ? "ANO" : "NE", this.occuredExportError ? "ANO" : "NE", this.occuredCriticalError ? "ANO" : "NE"), "Stažení/Konverze", MessageBoxButtons.OK, MessageBoxIcon.Information);

            if (occuredDuplicityError)
            {
                string filename = Path.Combine(this.txtWorkingFolder.Text, FILENAME_LOG_DUPLICITY);
                if (File.Exists(filename))
                    Process.Start(filename);
            }

            if (occuredExportError)
            {
                string filename = Path.Combine(this.txtWorkingFolder.Text, FILENAME_LOG_EXPORT);
                Process.Start(filename);
            }

            if (occuredCriticalError)
            {
                string filename = Path.Combine(this.txtWorkingFolder.Text, FILENAME_LOG_CRITICAL);
                Process.Start(filename);
            }

            /*
 * Actuall implementation is vulnerable to switch of tabs within the data mining,
 * thats why it is important to disable the option of switching when mining
 * 
 * On the log finalization, the tab switching is enabled
 *	(not the best solution, but functional)
 *
 * Delegate is used, because this function is used from various number of threads
 * 
 * Invoke wherever you need to in another thread
 * First parameter is the delegate, Second parameter is the value;
 */
            SetControlEnable dTcCourtsEnabled = new SetControlEnable(DMC_TcCourtsSetEnable);
            this.tcCourts.BeginInvoke(dTcCourtsEnabled, new object[] { true });
        }

        private SqlConnection dbConnection;


        private void MyCommonExceptionHandlingMethod(object sender, ThreadExceptionEventArgs t)
        {
            //Exception handling...
            WriteIntoLogCritical("Nastala nezpracovaná vyjímka: [{0}]", t.Exception.Message);
            WriteIntoLogCritical(t.Exception.StackTrace);
            Application.Exit();
        }

        public FrmCourts()
        {
            InitializeComponent();
            Microsoft.Win32.RegistryKey rgKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\\DataMiningCourts");
            this.txtWorkingFolder.Text = (string)rgKey.GetValue("FolderWork", "C:");
            this.txtOutputFolder.Text = (string)rgKey.GetValue("FolderOutput", "C:");
            rgKey.Close();
            this.NS_dtpDateFrom.Value = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            this.US_dtpDateFrom.Value = this.NS_dtpDateFrom.Value;
            this.NSS_dtpDateFrom.Value = this.NS_dtpDateFrom.Value;
            this.SNI_dtpDateFrom.Value = this.NS_dtpDateFrom.Value;
            this.dbConnection = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["LexData"].ConnectionString);

            this.tcCourts.SelectedIndex = 1;
            this.tcCourts_SelectedIndexChanged(null, null);

            // Zachycení všech možných vyjímek...
            Application.ThreadException += new ThreadExceptionEventHandler(MyCommonExceptionHandlingMethod);
        }

        /// <summary>
        /// Delegate that represents a function that starts crawling
        /// </summary>
        /// <returns>True, if crawling actually started, otherwise false (wrong input params for example)</returns>
        public delegate bool DoStartCrawl();

        /// <summary>
        /// Handler that is invoked, when the button for Data Mining is pressed
        /// </summary>
        private DoStartCrawl startAction;

        private WebBrowserDocumentCompletedEventHandler webBrowserHandler;

        private void btnLoad_Click(object sender, EventArgs e)
        {
            if (!String.IsNullOrEmpty(this.txtWorkingFolder.Text))
            {
                //DirectoryInfo diWorkingFolder = new DirectoryInfo(this.txtWorkingFolder.Text);
                //if (diWorkingFolder.GetDirectories().Length >= 0 &&
                //		!String.IsNullOrWhiteSpace(this.txtBackupFolder.Text))
                //{
                //	// Create path to a backup directory
                //	DateTime dtNow = DateTime.Now;
                //	this.SessionBackupDir = this.txtBackupFolder.Text + "\\" + dtNow.ToString("yyyy-MM-dd") + "-" + dtNow.Hour.ToString().PadLeft(2, '0') + dtNow.Minute.ToString().PadLeft(2, '0') + dtNow.Second.ToString().PadLeft(2, '0');
                //	// If there is no directory with in that specific path, create it
                //	if (!Directory.Exists(this.SessionBackupDir))
                //	{
                //		Directory.CreateDirectory(this.SessionBackupDir);
                //	}

                //	foreach (DirectoryInfo di in diWorkingFolder.GetDirectories())
                //	{
                //		string sDestination = String.Format(@"{0}\{1}", this.SessionBackupDir, di.Name);
                //		if (!Directory.Exists(sDestination))
                //		{
                //			// move
                //			Directory.Move(di.FullName, sDestination);
                //		}
                //	}
                //}
            }


#if !DUMMY_DB
            if (this.dbConnection.State == System.Data.ConnectionState.Closed)
            {
                this.dbConnection.Open();
            }
#endif
            if (startAction())
            {
                this.tcCourts.Enabled = false;
            }
        }

        private void browser_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            if (this.webBrowserHandler != null)
            {
                this.webBrowserHandler(sender, e);
            }
        }

        /// <summary>
        /// Set event handlers pursuant of the chosen tab
        /// </summary>
        private void tcCourts_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.browser.Visible = (tcCourts.SelectedTab == tpWeb);

            /* Odregistrování eventu pro kliknutí */
            this.startAction = null;
            /* Odregistrování eventu pro webbrowser */
            this.webBrowserHandler = null;

            if (tcCourts.SelectedTab == tpSNI)
            {
                this.startAction += new DoStartCrawl(this.SNI_Click);

                this.webBrowserHandler += new WebBrowserDocumentCompletedEventHandler(this.SNI_browser_DocumentCompleted);

                this.bgLoadingData.DoWork -= new System.ComponentModel.DoWorkEventHandler(this.NS_bgLoadData_DoWork);
                this.bgLoadingData.DoWork += new System.ComponentModel.DoWorkEventHandler(this.SNI_bgLoadData_DoWork);

                this.bgLoadingData.RunWorkerCompleted -= new System.ComponentModel.RunWorkerCompletedEventHandler(this.NS_bgLoadData_RunWorkerCompleted);
            }
            else if (tcCourts.SelectedTab == tpUS)
            {
                this.startAction += new DoStartCrawl(this.US_Click);

                this.webBrowserHandler += new WebBrowserDocumentCompletedEventHandler(this.US_browser_DocumentCompleted);
            }
            else if (tcCourts.SelectedTab == tpNSS)
            {
                this.startAction += new DoStartCrawl(this.NSS_Click);

                this.webBrowserHandler += new WebBrowserDocumentCompletedEventHandler(this.NSS_browser_DocumentCompleted);
            }
            else if (tcCourts.SelectedTab == tpNS)
            {
                this.startAction += new DoStartCrawl(this.NS_Click);

                this.webBrowserHandler += new WebBrowserDocumentCompletedEventHandler(this.NS_browser_DocumentCompleted);

                this.bgLoadingData.DoWork += new System.ComponentModel.DoWorkEventHandler(this.NS_bgLoadData_DoWork);
                this.bgLoadingData.DoWork -= new System.ComponentModel.DoWorkEventHandler(this.SNI_bgLoadData_DoWork);

                this.bgLoadingData.RunWorkerCompleted += new System.ComponentModel.RunWorkerCompletedEventHandler(this.NS_bgLoadData_RunWorkerCompleted);
            }
            else if (tcCourts.SelectedTab == tpUOHS)
            {
                this.startAction += new DoStartCrawl(this.UOHS_Click);

                this.webBrowserHandler += new WebBrowserDocumentCompletedEventHandler(this.UOHS_browser_DocumentCompleted);
            }
            else if (tcCourts.SelectedTab == tpUPV)
            {
                this.startAction += new DoStartCrawl(this.UPV_Click);

                this.webBrowserHandler += new WebBrowserDocumentCompletedEventHandler(this.UPV_browser_DocumentCompleted);
            }
            else if (tcCourts.SelectedTab == tpESLP)
            {
                startAction += new DoStartCrawl(this.ESLP_Click);

                webBrowserHandler += new WebBrowserDocumentCompletedEventHandler(this.ESLP_browser_DocumentCompleted);

                bgLoadingData.DoWork += new System.ComponentModel.DoWorkEventHandler(this.ESLP_bgLoadData_DoWork);

                bgLoadingData.RunWorkerCompleted += new System.ComponentModel.RunWorkerCompletedEventHandler(this.NS_bgLoadData_RunWorkerCompleted);
            }
            else if (tcCourts.SelectedTab == tpJV)
            {
                startAction += new DoStartCrawl(this.VZ_Click);

                webBrowserHandler += new WebBrowserDocumentCompletedEventHandler(this.VZ_browser_DocumentCompleted);

                bgLoadingData.DoWork += new System.ComponentModel.DoWorkEventHandler(this.VZ_bgLoadData_DoWork);

                bgLoadingData.RunWorkerCompleted += new System.ComponentModel.RunWorkerCompletedEventHandler(this.VZ_bgLoadData_RunWorkerCompleted);
            }
            else if (tcCourts.SelectedTab == tpINS)
            {
                this.startAction += new DoStartCrawl(this.INS_Click);
                /* Insolvenční rejstřík nevyužívá komponentu webbrowser -> neregistruje žádnou událost */
            }
            else if (tcCourts.SelectedTab == tpUsSk)
            {
                this.startAction += new DoStartCrawl(this.USSK_Click);
            }
            else if (tcCourts.SelectedTab == tpVS)
            {
                this.startAction += new DoStartCrawl(this.VS_Click);

                this.webBrowserHandler += new WebBrowserDocumentCompletedEventHandler(this.VS_browser_DocumentCompleted);
            }
        }

        private void downloadPageLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                var link = string.Empty;
                if (tcCourts.SelectedTab == tpSNI)
                {
                    link = SNI_PAGE_LINKPAGE;
                }
                else if (tcCourts.SelectedTab == tpUS)
                {
                    link = NALUS_INDEX;
                }
                else if (tcCourts.SelectedTab == tpNSS)
                {
                    link = NSS_INDEX;
                }
                else if (tcCourts.SelectedTab == tpNS)
                {
                    link = NS_INDEX;
                }
                else if (tcCourts.SelectedTab == tpUOHS)
                {
                    link = UOHS_INDEX;
                }
                else if (tcCourts.SelectedTab == tpUPV)
                {
                    link = "https://isdv.upv.cz/webapp/rozhodnuti.SeznamRozhodnuti";
                }
                else if (tcCourts.SelectedTab == tpESLP)
                {
                    link = ESLP_PAGE;
                }
                else if (tcCourts.SelectedTab == tpJV)
                {
                    link = VZ_INDEX;
                }
                else if (tcCourts.SelectedTab == tpINS)
                {
                    link = INS_BASE_ADDRESS;
                }
                else if (tcCourts.SelectedTab == tpUsSk)
                {
                    link = USSK_PAGE;
                }
                else if (tcCourts.SelectedTab == tpVS)
                {
                    link = VS_INDEX;
                }

                if (!string.IsNullOrWhiteSpace(link))
                {
                    Process.Start(link);
                }
                else
                {
                    throw new Exception("Odkaz na stránku je prázdný.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Nelze otevřít odkaz. Chyba: " + ex.Message);
            }
        }

        private void btnWorkingFolder_Click(object sender, EventArgs e)
        {
            fbd.SelectedPath = this.txtWorkingFolder.Text;
            if (fbd.ShowDialog(this) == DialogResult.OK)
                this.txtWorkingFolder.Text = fbd.SelectedPath;
        }

        private void btnOutputFolder_Click(object sender, EventArgs e)
        {
            fbd.SelectedPath = this.txtOutputFolder.Text;
            if (fbd.ShowDialog(this) == DialogResult.OK)
                this.txtOutputFolder.Text = fbd.SelectedPath;
        }

        private void FrmCourts_FormClosed(object sender, FormClosedEventArgs e)
        {
            Microsoft.Win32.RegistryKey rgKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\\DataMiningCourts");
            rgKey.SetValue("FolderWork", this.txtWorkingFolder.Text);
            rgKey.SetValue("FolderOutput", this.txtOutputFolder.Text);
            rgKey.Close();
            if (this.dbConnection.State == System.Data.ConnectionState.Open)
                this.dbConnection.Close();
            this.dbConnection.Dispose();
        }

        private void FrmCourts_Load(object sender, EventArgs e)
        {
            /* NS is searched by date of the publication on the web. */
            this.NS_cbSearchBy.SelectedIndex = 1;
            /* INS is set to current date*/
            this.INS_dtpDateTo.Value = DateTime.Now;

            InitializeLogs();

            /* Button for doc->xml transformation (NSS) is enabled when debugging - testing purposes */
#if DEBUG
            this.NSS_btnWordToXml.Enabled = true;
#endif
        }

        /// <summary>
        /// Function, that opens a word (doc) file at pPathSource location and saves is as Xml Word to the pPathTarget
        /// </summary>
        /// <param name="pPathSource">The file, which we open</param>
        /// <param name="pPathTarget"></param>
        public static void OpenFileInWordAndSaveInWXml(string pPathSource, string pPathTarget)
        {
            Microsoft.Office.Interop.Word.Application oWord = null;
            Microsoft.Office.Interop.Word.Document oWordDocument = null;
            object oFalseValue = false;
            object oTrueValue = true;
            object oMissing = Type.Missing;
            try
            {
                //1. Try to load an active instance of the MS Word
                oWord = (Microsoft.Office.Interop.Word.Application)System.Runtime.InteropServices.Marshal.GetActiveObject("Word.Application");
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                //1a - Active instantion has not been found -> open a new one!
                oWord = new Microsoft.Office.Interop.Word.Application();
            }
            //oWord.Visible = true;
            //oWord.ScreenUpdating = true;
            //oWord.Interactive = true;
            //oWord.IgnoreRemoteRequests = false;

            //2. Load&Save the document
            Object oWordFile = (object)(pPathSource);
            oWordDocument = oWord.Documents.Open(ref oWordFile, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing
                            , ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing);
            object oDocumentType = Microsoft.Office.Interop.Word.WdSaveFormat.wdFormatXML;
            oWordFile = pPathTarget;
            oWordDocument.set_AttachedTemplate(Path.Combine(System.Configuration.ConfigurationManager.AppSettings["ApplicationFolder"], "Jesterka2010.dotm"));

            oWordDocument.SaveAs(ref oWordFile, ref oDocumentType, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oFalseValue, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing);
            Object oSaveChanges = (object)false;
            Object oOriginalFormat = (object)false;
            Object oRouteDocument = (object)false;
            ((Microsoft.Office.Interop.Word._Document)oWordDocument).Close(ref oSaveChanges, ref oOriginalFormat, ref oRouteDocument);
            // we create else the copy of the document for the normal open in Jesterka
            if (pPathTarget.EndsWith(".xml.xml"))
            {
                string sTarget1 = pPathTarget.Replace(".xml.xml", ".xml");
                if (!File.Exists(sTarget1))
                    File.Copy(pPathTarget, pPathTarget.Replace(".xml.xml", ".xml"));
            }
        }

        // a již je v utilitách?
        public static string ClearStringFromSpecialHtmlChars(string pInput)
        {
            /* Replace a special html chars to standart chars, that can be used in Xml strings
					Á &Aacute;  á &aacute;
					Č &#268;    č &#269;
					Ď &#270;    ď &#271;
					É &Eacute;  é &eacute;
					Ě &#282;    ě &#283;
					Í &Iacute;  í &iacute;
					Ň &#327;    ň &#328;
					Ó &Oacute;  ó &oacute;
					Ř &#344;    ř &#345;
					Š &#352;    š &#353;
					Ť &#356;    ť &#357;
					Ú &Uacute;  ú &uacute;
					Ů &#366;    ů &#367;
					Ý &Yacute;  ý &yacute;
					Ž &#381;    ž &#382;
					&nbsp; - mezera
			 * By using a standart system function System.Net.WebUtility.HtmlDecode
			 * see http://stackoverflow.com/questions/122641/how-can-i-decode-html-characters-in-c
			 * 
			 * It also add an escape characters when needed &
			 * see http://weblogs.sqlteam.com/mladenp/archive/2008/10/21/Different-ways-how-to-escape-an-XML-string-in-C.aspx
			 */
            string sPlainText = System.Net.WebUtility.HtmlDecode(pInput);
            string xmlCapable = System.Security.SecurityElement.Escape(sPlainText);
            return xmlCapable.Trim();
        }

        public bool IsUserAdministrator()
        {
            bool isAdmin;
            try
            {
                WindowsIdentity user = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(user);
                isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (UnauthorizedAccessException)
            {
                isAdmin = false;
            }
            catch (Exception)
            {
                isAdmin = false;
            }
            return isAdmin;
        }

        private void tsmiSetCompatibilityMode_Click(object sender, EventArgs e)
        {
            if (IsUserAdministrator())
            {
                Microsoft.Win32.RegistryKey key;
                key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION");
                //App name: http://stackoverflow.com/questions/616584/how-do-i-get-the-name-of-the-current-executable-in-c
                //Value for an App: http://msdn.microsoft.com/en-us/library/ee330730%28v=vs.85%29.aspx#browser_emulation
                key.SetValue(System.AppDomain.CurrentDomain.FriendlyName, 8000, Microsoft.Win32.RegistryValueKind.DWord);
                key.Close();
                MessageBox.Show("Nastaveno");
            }
        }

        private void tsmiRemoveCompatibilityMode_Click(object sender, EventArgs e)
        {
            if (IsUserAdministrator())
            {
                Microsoft.Win32.RegistryKey key;
                key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION");
                key.DeleteValue(System.AppDomain.CurrentDomain.FriendlyName, false);
                key.Close();
                MessageBox.Show("Smazáno");
            }
        }

        private void cbCheckDuplicities_CheckedChanged(object sender, EventArgs e)
        {
            CitationService.DO_CHECK_DUPLICITIES = this.cbCheckDuplicities.Checked;
        }

        private string TermSpaces(Match match)
        {
            string s = match.Value;
            UtilityBeck.Utility.RemoveWhiteSpaces(ref s);
            return s;
        }

        public static void AddLawArea(SqlConnection pConn, XmlNode pXnHeader)
        {
            XmlNode xn;
            List<string> lLawAreas = new List<string>();
            //XmlNodeList xNodes = CistyVyber.DocumentElement.FirstChild.SelectNodes("./zakladnipredpis/item/link");
            XmlNodeList xNodes = pXnHeader.SelectNodes("./zakladnipredpis/item/link");
            if (xNodes.Count > 0)
            {
                string sHref, sIdBlock, sLawArea;
                int iPosition;
                bool bFoundLawArea;
                SqlCommand cmd = pConn.CreateCommand();
                foreach (XmlNode xnLink in xNodes)
                {
                    sHref = xnLink.Attributes["href"].Value;
                    iPosition = sHref.IndexOf('&');
                    if (iPosition > -1)
                    {
                        sIdBlock = sHref.Substring(iPosition + 1);
                        sHref = sHref.Substring(0, iPosition);
                    }
                    else
                        sIdBlock = null;
                    bFoundLawArea = false;
                    if (sIdBlock != null)
                    {
                        cmd.CommandText = "SELECT ContentdText.query('//*[@id-block=\"" + sIdBlock + " and position()=last()\"]/hlavicka/lawarea') FROM Contentd INNER JOIN Dokument ON (Contentd.IDDokument=Dokument.IDDokument) WHERE Dokument.DokumentName='" + sHref + "'";
                        object oResult = cmd.ExecuteScalar();
                        XmlDocument d = new XmlDocument();
                        string s = oResult.ToString();
                        if (!String.IsNullOrEmpty(s))
                        {
                            bFoundLawArea = true;
                            d.LoadXml(oResult.ToString());
                            xNodes = d.DocumentElement.SelectNodes("./item");
                            foreach (XmlNode xn1 in xNodes)
                                if (!lLawAreas.Contains(xn1.InnerText))
                                    lLawAreas.Add(xn1.InnerText);
                        }
                        if (!bFoundLawArea)
                        {
                            XmlNode xnLawArea = null;
                            cmd.CommandText = "SELECT ContentdText FROM Contentd INNER JOIN Dokument ON (Contentd.IDDokument=Dokument.IDDokument) WHERE Dokument.DokumentName='" + sHref + "'";
                            XmlReader xr = cmd.ExecuteXmlReader();
                            xr.ReadOuterXml();
                            d = new XmlDocument();
                            xn = d.ReadNode(xr);
                            xr.Close();
                            d.AppendChild(xn);
                            xn = d.SelectSingleNode("//*[@id-block='" + sIdBlock + "'][last()]");
                            do
                            {
                                xn = xn.ParentNode;
                                xnLawArea = xn.SelectSingleNode("./hlavicka/lawarea");
                                if (xnLawArea != null)
                                {
                                    bFoundLawArea = true;
                                    xNodes = xnLawArea.SelectNodes("./item");
                                    foreach (XmlNode xn1 in xNodes)
                                        if (!lLawAreas.Contains(xn1.InnerText))
                                            lLawAreas.Add(xn1.InnerText);
                                    break;
                                }
                            }
                            while (xn.ParentNode != null);
                        }
                    }
                    if (!bFoundLawArea)
                    {
                        cmd.CommandText = "SELECT TLawAreaName FROM Dokument INNER JOIN DokumentLawArea ON (Dokument.IDDokument=DokumentLawArea.IDDokument) INNER JOIN TLawArea ON (DokumentLawArea.IDTLawArea=TLawArea.IDTLawArea) WHERE Dokument.DokumentName='" + sHref + "'";
                        using (SqlDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                sLawArea = dr.GetString(0);
                                if (!lLawAreas.Contains(sLawArea))
                                    lLawAreas.Add(sLawArea);
                            }
                            dr.Close();
                            dr.Dispose();
                        }
                    }
                }
            }
            Regex rg = new Regex(@"\sTdo?\s");
            xn = pXnHeader.SelectSingleNode("./citace");
            //if (rg.IsMatch(hlavi.SpisovaZnacka))
            if (rg.IsMatch(xn.InnerText))
            {
                if (!lLawAreas.Contains("Trestní právo"))
                    lLawAreas.Add("Trestní právo");
            }
            else if (xn.InnerText.Contains("MSPH") || xn.InnerText.Contains("KSPH") || xn.InnerText.Contains("KSCB")
                || xn.InnerText.Contains("KSTB") || xn.InnerText.Contains("KSPL") || xn.InnerText.Contains("KSKV")
                || xn.InnerText.Contains("KSUL") || xn.InnerText.Contains("KSLB") || xn.InnerText.Contains("KSHK")
                || xn.InnerText.Contains("KSPA") || xn.InnerText.Contains("KSBR") || xn.InnerText.Contains("KSJI")
                || xn.InnerText.Contains("KSZL") || xn.InnerText.Contains("KSOS") || xn.InnerText.Contains("KSOL")
                || xn.InnerText.Contains("VSPH") || xn.InnerText.Contains("VSOL") || xn.InnerText.Contains("NSCR")
                || xn.InnerText.Contains("NSČR") || xn.InnerText.Contains("ICdo"))
            {
                if (!lLawAreas.Contains("Insolvenční právo"))
                    lLawAreas.Add("Insolvenční právo");
            }
            if (lLawAreas.Count > 0)
            {
                XmlElement el = pXnHeader.OwnerDocument.CreateElement("lawarea");
                foreach (string s1 in lLawAreas)
                    el.InnerXml += "<item>" + s1 + "</item>";
                UtilityBeck.UtilityXml.InsertElementInAlphabeticalOrder(pXnHeader, el);
            }
        }
    }
}
