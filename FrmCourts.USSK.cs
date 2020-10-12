using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Reflection;
using System.Data.SqlClient;
using System.Xml;
using System.IO;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace DataMiningCourts
{
	partial class FrmCourts
	{
		private int dokumentuCelkem;
		private int aktualneZpracovavanyDokument;
		private static string USSK_PAGE = "https://www.ustavnysud.sk/zbierka-nalezov-a-uzneseni";

		private void llWebUsSk_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			System.Diagnostics.Process.Start(USSK_PAGE);
		}

		private void btnHtmlFile_Click(object sender, EventArgs e)
		{
			OpenFileDialog ofd = new OpenFileDialog();
			ofd.Filter = "Html soubory|*.html;*.htm";
			if (ofd.ShowDialog(this) == DialogResult.OK)
				this.txtHtmlFile.Text = ofd.FileName;
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
		private bool USSK_Click()
		{
			if (string.IsNullOrEmpty(this.txtHtmlFile.Text))
			{
				MessageBox.Show(this, "Musí být vybrán html soubor!", ERROR_CHECKING_OUTPUT, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				return false;
			}
			if (string.IsNullOrWhiteSpace(this.txtYear.Text))
			{
				MessageBox.Show(this, "Musí být zadán rok!", ERROR_CHECKING_OUTPUT, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				return false;
			}
			this.btnMineDocuments.Enabled = false;
			this.USSK_btnWordToXml.Enabled = false;
			this.processedBar.Value = 0;

			this.USSK_SpustVlaknoProStazeniDokumentu();

			return true;
		}

		private void USSK_SpustVlaknoProStazeniDokumentu()
		{
			Task.Factory.StartNew(() =>
			{
				HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
				doc.Load(this.txtHtmlFile.Text, Encoding.UTF8);
				HtmlAgilityPack.HtmlNodeCollection nodes = doc.DocumentNode.SelectNodes("//a[starts-with(@href,'https://www.ustavnysud.sk/docDownload')]");
				this.dokumentuCelkem = nodes.Count;
				this.aktualneZpracovavanyDokument = 0;

				// The delegate member for progress bar update
				var UpdateProgress = new UpdateDownloadProgressDelegate(USSK_UpdateDownloadProgressSafe);

				using (SqlConnection conn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["SlovLex"].ConnectionString))
				{
					conn.Open();
					string sError;
					for (int i = 0; i < nodes.Count; i++)
					{
						++this.aktualneZpracovavanyDokument;
						if (i == (nodes.Count - 1))
							this.processedBar.BeginInvoke(UpdateProgress, new object[] { true });
						else
							this.processedBar.BeginInvoke(UpdateProgress, new object[] { false });
						CreateDocument(conn, nodes[i], out sError);
					}
					conn.Close();
				}
				MessageBox.Show("Stažení bylo dokončeno.", "Stažení ÚS SK", MessageBoxButtons.OK, MessageBoxIcon.Information);
				//this.FinalizeLogs();
			});
		}

		/// <summary>
		/// Funkce pro update progress baru GUI vlákna, kterou lze použít v jiném vlákně (přes delegát)
		/// </summary>
		/// <param name="forceComplete">Parametr, který říká, že je prostě hotovo...</param>
		void USSK_UpdateDownloadProgressSafe(bool forceComplete)
		{
#if DEBUG
			this.gbProgressBar.Text = String.Format("Zpracováno {0}/{1} dokumentů => {2}%", this.aktualneZpracovavanyDokument, this.dokumentuCelkem, this.processedBar.Value);
#endif
			int value;
			if (forceComplete)
			{
				value = this.processedBar.Maximum;
			}
			else
			{
				double d = (double)this.aktualneZpracovavanyDokument / (double)this.dokumentuCelkem;
				value = (int)Math.Ceiling(d * 100);
			}
			this.processedBar.Value = value;
			if (this.processedBar.Value == this.processedBar.Maximum)
			{
				this.btnMineDocuments.Enabled = true;
				this.USSK_btnWordToXml.Enabled = true;
				this.processedBar.Value = 0;
				this.tcCourts.Enabled = true;
			}
		}

		// //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		/// <summary>
		/// je třeba kvůli zastaralému protokolu webové stránky ÚS
		/// </summary>
		/// <returns></returns>
		public static bool SetAllowUnsafeHeaderParsing()
		{
			//Get the assembly that contains the internal class
			Assembly aNetAssembly = Assembly.GetAssembly(typeof(System.Net.Configuration.SettingsSection));
			if (aNetAssembly != null)
			{
				//Use the assembly in order to get the internal type for the internal class
				Type aSettingsType = aNetAssembly.GetType("System.Net.Configuration.SettingsSectionInternal");
				if (aSettingsType != null)
				{
					//Use the internal static property to get an instance of the internal settings class.
					//If the static instance isn't created allready the property will create it for us.
					object anInstance = aSettingsType.InvokeMember("Section",
					  BindingFlags.Static | BindingFlags.GetProperty | BindingFlags.NonPublic, null, null, new object[] { });

					if (anInstance != null)
					{
						//Locate the private bool field that tells the framework is unsafe header parsing should be allowed or not
						FieldInfo aUseUnsafeHeaderParsing = aSettingsType.GetField("useUnsafeHeaderParsing", BindingFlags.NonPublic | BindingFlags.Instance);
						if (aUseUnsafeHeaderParsing != null)
						{
							aUseUnsafeHeaderParsing.SetValue(anInstance, true);
							return true;
						}
					}
				}
			}
			return false;
		}

		// //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		private void CreateDocument(SqlConnection pConn, HtmlAgilityPack.HtmlNode pHn, out string pError)
		{
			pError = null;
			string sNumber, sReferenceNumber, sDocumentName = null;
			string sYear = this.txtYear.Text.Trim();
			sReferenceNumber = pHn.InnerText;
			int iPosition = sReferenceNumber.IndexOf('-');
			if (iPosition == -1)    // Zoznam rozhodnutí podľa spisovej značky
				return;
			sNumber = sReferenceNumber.Substring(0, iPosition);
			sNumber = sNumber.Replace("č.", "").Trim();
			sReferenceNumber = sReferenceNumber.Substring(iPosition + 1).Trim();
			HtmlAgilityPack.HtmlNode hn2;
			string s;

			XmlDocument dResult = new XmlDocument();
			dResult.Load(Path.Combine(System.Configuration.ConfigurationManager.AppSettings["ApplicationFolder"], @"Templates\TemplateNew_Jsk.xml"));
			XmlNode xn = dResult.DocumentElement.FirstChild.FirstChild;
			while (xn != null)
			{
				switch (xn.Name)
				{
					case "autor":
						xn.FirstChild.InnerText = "Ústavný súd Slovenskej republiky";
						break;
					case "castka":
						xn = xn.NextSibling;
						xn.ParentNode.RemoveChild(xn.PreviousSibling);
						continue;
					case "cislojednaci":
						xn.InnerText = sReferenceNumber;
						break;
					case "citace":
						xn.InnerText = sNumber + "/" + sYear + " Zb.Us.";
						sDocumentName = "Jsk_" + sYear + "_" + sNumber + "_ZbUs";
						dResult.DocumentElement.Attributes["DokumentName"].Value = sDocumentName;
						break;
					case "datschvaleni":
						break;
					case "download-link":
						xn.InnerText = pHn.Attributes["href"].Value.Trim();
						if (this.cbCheckDuplicities.Checked)
						{
							SqlCommand cmd = pConn.CreateCommand();
							cmd.CommandText = "SELECT 1 FROM Header WHERE DownloadLink='" + xn.InnerText + "'";
							Object oResult = cmd.ExecuteScalar();
							if (oResult != null)
								return;
							else
							{
								cmd.CommandText = "SELECT 1 FROM Dokument WHERE DokumentName='" + sDocumentName + "'";
								oResult = cmd.ExecuteScalar();
								if (oResult != null)
									return;
							}
						}
						using (System.Net.WebClient client = new System.Net.WebClient())
						{
							//client.Headers[""]
							// Download the Web resource and save it into the current filesystem folder.
							client.DownloadFile(xn.InnerText, this.txtWorkingFolder.Text + "\\" + sDocumentName + ".pdf");
						}
						XmlAttribute a = dResult.CreateAttribute("source-file");
						a.Value = "Original_" + sDocumentName + ".pdf";
						dResult.DocumentElement.Attributes.Append(a);
						break;
					case "druh":
						hn2 = pHn.SelectSingleNode("./ancestor::li[contains(@class,'type')]");
						s = hn2.InnerText.ToLower();
						iPosition = s.IndexOf('\n');
						if (iPosition > -1)
							s = s.Substring(0, iPosition);
						if (s.Contains("nález"))
							xn.InnerText = "Nález";
						else if (s.Contains("uzneseni"))
							xn.InnerText = "Uznesenie";
						else
							pError = "Nenalezen druh!";
						break;
					case "kategorie":
						xn.InnerText = "Zbierka nálezov a uznesení Ústavného súdu SR";
						break;
					case "titul":
						hn2 = pHn.SelectSingleNode("./parent::*/ul");
						if (hn2 != null)
							xn.InnerText = hn2.InnerText.Trim();
						break;
				}
				xn = xn.NextSibling;
			}
			hn2 = pHn.SelectSingleNode("./ancestor::li[contains(@class,'decision')]/text()");
			XmlElement el = dResult.CreateElement("zakladnipredpis");
			el.InnerXml = "<item>" + hn2.InnerText.Trim() + "</item>";
			dResult.DocumentElement.FirstChild.AppendChild(el);
			dResult.Save(this.txtWorkingFolder.Text + "\\" + sDocumentName + ".xml");
		}

		#region Processing documents
		// //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		private void USSK_btnWordToXml_Click(object sender, EventArgs e)
		{
			this.btnMineDocuments.Enabled = false;
			this.USSK_btnWordToXml.Enabled = false;
			this.processedBar.Value = 0;

			this.USSK_SpustVlaknoProZpracovani();
		}

		private void USSK_SpustVlaknoProZpracovani()
		{
			Task.Factory.StartNew(() =>
			{
				string sDocumentName;
				string[] files = Directory.GetFiles(this.txtWorkingFolder.Text, "*.xml");
				this.dokumentuCelkem = files.Length;
				this.aktualneZpracovavanyDokument = 0;
				// The delegate member for progress bar update
				var UpdateProgress = new UpdateDownloadProgressDelegate(USSK_UpdateDownloadProgressSafe);

				for (int i = 0; i < files.Length; i++)
				{
					++this.aktualneZpracovavanyDokument;
					if (i == (files.Length - 1))
						this.processedBar.BeginInvoke(UpdateProgress, new object[] { true });
					else
						this.processedBar.BeginInvoke(UpdateProgress, new object[] { false });
					sDocumentName = Path.GetFileNameWithoutExtension(files[i]);
					GenerateOneDocument(files[i], sDocumentName);
				}
				this.FinalizeLogs();
			});
		}

		// //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		private void GenerateOneDocument(string pInputXmlPath, string pDocumentName)
		{
			string sFolderPath = Path.Combine(this.txtOutputFolder.Text, pDocumentName);
			Directory.CreateDirectory(sFolderPath);
			File.Move(pInputXmlPath, Path.Combine(sFolderPath, pDocumentName + ".xml"));


			string sDocPath = Path.Combine(this.txtWorkingFolder.Text, pDocumentName + ".docx");
			if (!File.Exists(sDocPath))
				sDocPath = Path.Combine(this.txtWorkingFolder.Text, pDocumentName + ".doc");
			string sWordXmlPath = Path.Combine(sFolderPath, "W_" + pDocumentName + ".xml");
			if (!File.Exists(sDocPath))
			{
				this.WriteIntoLogCritical("Nenalezen soubor Wordu!");
				return;
			}
			ResaveDocumentAsXml(sDocPath, sWordXmlPath);

			// úprava MS Wordu
			XmlDocument dWord = new XmlDocument();
			dWord.Load(sWordXmlPath);
			XmlNamespaceManager nsmgr = new XmlNamespaceManager(dWord.NameTable);
			nsmgr.AddNamespace("w", "http://schemas.microsoft.com/office/word/2003/wordml");
			nsmgr.AddNamespace("wx", "http://schemas.microsoft.com/office/word/2003/auxHint");

			XmlNode xn = dWord.SelectSingleNode("/w:wordDocument/w:body/*[1]", nsmgr);
			if (String.IsNullOrWhiteSpace(xn.InnerText))
				xn.ParentNode.RemoveChild(xn);

			xn = dWord.SelectSingleNode("/w:wordDocument/w:body//w:p", nsmgr);
			while (String.IsNullOrWhiteSpace(xn.InnerText))
				xn = xn.NextSibling;
			Regex rg = new Regex(@"\d+/\d{4}");     // č. 10/2007II. ÚS 148/06
			if (rg.IsMatch(xn.InnerText))
			{
				xn.ParentNode.RemoveChild(xn);
				dWord.Save(sWordXmlPath);
			}
			else
				this.WriteIntoLogCritical("Nedetekován začátek dokumentu!");
			dWord.Save(sWordXmlPath + ".xml");

			// Adding some information into header
			// title, kind, date approval, law sentence
			Console.WriteLine("Načtení dalších hlavičkových informací");
			XmlDocument d = new XmlDocument();
			d.Load(Path.Combine(sFolderPath, pDocumentName + ".xml"));
			xn = d.DocumentElement.FirstChild.SelectSingleNode("./titul");
			//string sTitleHeader;
			if (xn == null)
			{
				this.WriteIntoLogCritical("Nenalezen titulek v hlavičce!");
				//sTitleHeader = String.Empty;
			}
			//else
			//	sTitleHeader = xn.InnerText.ToLower();
			//UtilityBeck.Utility.RemoveWhiteSpaces(ref sTitleHeader);
			bool bTitleFound = false;
			//XmlNodeList xNodes = dWord.SelectNodes("/w:wordDocument/w:body/wx:sect/w:p | /w:wordDocument/w:body/wx:sect/wx:sub-section/w:p", nsmgr);
			int iNumberOfProcessed = 0;
			xn = dWord.SelectSingleNode("/w:wordDocument/w:body", nsmgr).FirstChild;
			this.ProcessNodeForHeaderInfo(ref xn, ref iNumberOfProcessed, ref bTitleFound, nsmgr, ref d);

			if (!bTitleFound)
				this.WriteIntoLogCritical("Nenalezen titulek v textu!");
			d.Save(Path.Combine(sFolderPath, pDocumentName + ".xml"));
			File.Move(Path.Combine(this.txtWorkingFolder.Text, pDocumentName + ".pdf"), Path.Combine(sFolderPath, "Original_" + pDocumentName + ".pdf"));

			Console.WriteLine("Export z Wordu");
			string sExportErrors = String.Empty;
			string[] parametry = new string[] { "CZ", sFolderPath + "\\" + pDocumentName + ".xml", pDocumentName, "-1", "1" };
			try
			{
				ExportWordXml.ExportWithoutProgress export = new ExportWordXml.ExportWithoutProgress(parametry);
				export.RunExport();
				sExportErrors = export.errors;
			}
			catch (Exception ex)
			{
				if (!(ex is NullReferenceException))
				{
					this.WriteIntoLogCritical("Export zcela selhal ! " + ex.Message + ex.InnerException);
					return;
				}
			}
			File.Delete(sDocPath);
		}

		// ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		public static void ResaveDocumentAsXml(string pDocPath, string pWordXmlPath)
		{
			Microsoft.Office.Interop.Word.Application oWord = null;
			Microsoft.Office.Interop.Word.Document oWordDocument = null;
			object oFalseValue = false;
			object oTrueValue = true;
			object oMissing = Type.Missing;
			try
			{
				/// trying to read the active instance of Word.exe
				oWord = (Microsoft.Office.Interop.Word.Application)System.Runtime.InteropServices.Marshal.GetActiveObject("Word.Application");
			}
			catch (COMException)
			{
				// no active instance, we create the new one
				oWord = new Microsoft.Office.Interop.Word.Application();
			}

			// opening the document
			Object oWordFile = (object)pDocPath;
			oWordDocument = oWord.Documents.Open(ref oWordFile, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing
					, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing);
			oWordDocument.set_AttachedTemplate(Path.Combine(System.Configuration.ConfigurationManager.AppSettings["ApplicationFolder"], "Jesterka2010.dotm"));

			// showing, activating word and the document
			oWord.Visible = false;
			//oWordDocument.ActiveWindow.WindowState = Microsoft.Office.Interop.Word.WdWindowState.wdWindowStateMinimize;
			//oWord.Activate();
			//oWordDocument.Activate();

			// resaving as .xml
			object oDocumentType = Microsoft.Office.Interop.Word.WdSaveFormat.wdFormatXML;
			oWordFile = pWordXmlPath;
			oWordDocument.SaveAs(ref oWordFile, ref oDocumentType, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oFalseValue, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing);
			oWordDocument.Close();
		}

		private void ProcessNodeForHeaderInfo(ref XmlNode pXn, ref int pNumberOfProcessed, ref bool pTitleFound, XmlNamespaceManager nsmgr, ref XmlDocument pD)
		{
			if (++pNumberOfProcessed > 20)
			{
				this.WriteIntoLogCritical("Zkontrolujte titulek, větu a ze dne!");
				return;
			}
			if (pXn == null)
				return;
			if (pXn.Name.Equals("wx:sect") || pXn.Name.Equals("wx:sub-section"))
			{
				pXn = pXn.FirstChild;
				this.ProcessNodeForHeaderInfo(ref pXn, ref pNumberOfProcessed, ref pTitleFound, nsmgr, ref pD);
			}
			else
			{
				if (!String.IsNullOrWhiteSpace(pXn.InnerText))
				{
					if (pXn.InnerText.ToUpper().Equals(pXn.InnerText) || (pXn.PreviousSibling == null))    // it is title
						pTitleFound = true;
					else
					{
						Regex rgInfo = new Regex(@"^\((Nález|Uznesenie)\s+Ústavného súdu Slovenskej republiky.+(?<date1>\d+\.\s*\p{L}+\s+\d{4})");
						Regex rgInfo2 = new Regex(@"^\((Nález|Uznesenie)\s+Ústavného súdu Slovenskej republiky.+");
						if (rgInfo.IsMatch(pXn.InnerText))
						{
							XmlNode xnDateApproval = pD.DocumentElement.FirstChild.SelectSingleNode("./datschvaleni");
							string s = rgInfo.Match(pXn.InnerText).Groups["date1"].Value;
							xnDateApproval.InnerText = UtilityBeck.Utility.ConvertLongDateIntoUniversalDate(s);
							pNumberOfProcessed = 21;
							return;
						}
						else if (rgInfo2.IsMatch(pXn.InnerText))
						{
							this.WriteIntoLogCritical("Nenalezeno ze dne a možná neúplná právní věta!");
							pNumberOfProcessed = 21;
							return;
						}
						else if (pXn.SelectSingleNode(".//w:b | .//w:b-cs", nsmgr) == null)
						{
							XmlNode xnStyle = pXn.SelectSingleNode("./w:pPr/w:pStyle", nsmgr);
							if (xnStyle == null)
							{
								this.WriteIntoLogCritical("Nenalezeno ze dne a zřejmě ani právní věta!");
								return;
							}
							XmlNode xn2 = pXn.SelectSingleNode("/*/w:styles/w:style[@w:styleId='" + xnStyle.Attributes["w:val"].Value + "']", nsmgr);
							if (xn2 == null)
							{
								this.WriteIntoLogCritical("Nenalezeno ze dne a zřejmě ani právní věta!");
								return;
							}
							else
							{
								xn2 = xn2.SelectSingleNode(".//w:b | .//w:b-cs", nsmgr);
								if (xn2 == null)
								{
									this.WriteIntoLogCritical("Nenalezeno ze dne a zřejmě ani právní věta!");
									return;
								}
							}
						}
						XmlNode xnVeta = pD.DocumentElement.FirstChild.SelectSingleNode("./veta");
						XmlElement elP = pD.CreateElement("p");
						elP.InnerText = pXn.InnerText;
						xnVeta.AppendChild(elP);
					}
				}
				while (pXn.NextSibling == null)
					pXn = pXn.ParentNode;
				pXn = pXn.NextSibling;
				this.ProcessNodeForHeaderInfo(ref pXn, ref pNumberOfProcessed, ref pTitleFound, nsmgr, ref pD);
			}
		}
		#endregion
	}
}
