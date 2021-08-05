using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Data.SqlClient;
using System.IO;
using System.Net;
using HtmlAgilityPack;
using System.Xml;

namespace DataMiningCourts
{
	public partial class FrmCourts : Form
	{
		private static string ESO_INDEX = "https://eso.ochrance.cz/";
		private static string ESO_ODKAZ_DOKUMENT = "https://eso.ochrance.cz/Nalezene/Edit/";

		private enum EsoAkce { naVyhledaniDat, naPrvniUlozeniOdkazu, naUlozeniOdkazu/*, naDokument*/ };

		/// <summary>
		/// Zásobník ESO akcí k provedení
		/// </summary>
		private EsoAkce aktualniEsoAkce;
		private List<string> existingIds;
		private List<string> dokumentNames;
		private int ESO_progressStrankaPodil;
		//private int lastCitationNumber;

		private string Eso_CheckFilledValues()
		{
			StringBuilder sbResult = new StringBuilder();
			/* Greater than zero => This instance is later than value. */
			if (this.dtpDateEsoFrom.Value.CompareTo(this.dtpDateEsoUntil.Value) > 0)
				sbResult.AppendLine(String.Format("Datum od [{0}] je větší, než datum do [{1}]!", this.dtpDateEsoFrom.Value.ToShortDateString(), this.dtpDateEsoUntil.Value.ToShortDateString()));

			//if (this.dtpDateEsoFrom.Value.Year != this.dtpDateEsoUntil.Value.Year)
			//	sbResult.AppendLine(String.Format("Rok od [{0}] je jiný, nežli rok do [{1}]!", this.dtpDateEsoFrom.Value.Year, this.dtpDateEsoUntil.Value.Year));

			if (String.IsNullOrEmpty(this.txtWorkingFolder.Text))
				sbResult.AppendLine("Pracovní složka musí být vybrána!");
			if (String.IsNullOrEmpty(this.txtOutputFolder.Text))
				sbResult.AppendLine("Výstupní složka (místo pro uložení hotových dat) musí být vybrána!");
			return sbResult.ToString();
		}

		private bool ESO_Click()
		{
			string sError = this.Eso_CheckFilledValues();
			if (!String.IsNullOrEmpty(sError))
			{
				MessageBox.Show(this, sError, ERROR_CHECKING_OUTPUT, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				return false;
			}
			this.btnMineDocuments.Enabled = false;
			this.processedBar.Value = 0;
			this.aktualneZpracovavanyDokument = 0;
			this.existingIds = new List<string>();
			this.dokumentNames = new List<string>();
			this.seznamOdkazuKeZpracovani.Clear();
			if (this.dbConnection.State == System.Data.ConnectionState.Closed)
				this.dbConnection.Open();
			if (this.cbCheckDuplicities.Checked)
			{
				SqlCommand cmd = this.dbConnection.CreateCommand();
				cmd.CommandText = @"SELECT HeaderSub.IDExternal,Dokument.DokumentName FROM Dokument INNER JOIN HeaderSub ON Dokument.IDDokument=HeaderSub.IDDokument WHERE IDTJournal=254";
				using (SqlDataReader dr = cmd.ExecuteReader())
				{
					while (dr.Read())
					{
						this.existingIds.Add(dr.GetString(0));
						this.dokumentNames.Add(dr.GetString(1));
					}
				}
				int iPosition;
				string s;
				cmd.CommandText = @"SELECT HeaderL.WebAddress,Dokument.DokumentName FROM Dokument
INNER JOIN HeaderL ON Dokument.IDDokument=HeaderL.IDDokument WHERE Dokument.IDTSector=3 AND HeaderL.IDTJournal=255";
				using (SqlDataReader dr = cmd.ExecuteReader())
				{
					while (dr.Read())
					{
						s = dr.GetString(0);
						iPosition = s.LastIndexOf('/');
						this.existingIds.Add(s.Substring(iPosition + 1));
						this.dokumentNames.Add(dr.GetString(1));
					}
				}

				//cmd.CommandText = "SELECT MAX(ArticleNumber) FROM HeaderSub WHERE IDTJournal=254 AND Citation LIKE '%/" + this.dtpDateEsoFrom.Value.Year.ToString() + "%'";
				//object oResult = cmd.ExecuteScalar();
				//if (oResult != null)
				//	this.lastCitationNumber = Convert.ToInt32(oResult);
			}
			this.aktualniEsoAkce = EsoAkce.naVyhledaniDat;
			browser.Navigate(ESO_INDEX);
			return true;
		}

		private void ESO_KonecStahovani()
		{
			if (this.dbConnection.State == System.Data.ConnectionState.Open)
				this.dbConnection.Close();
			this.tcCourts.Enabled = true;
			this.btnMineDocuments.Enabled = true;
			this.processedBar.Value = 100;
			FinalizeLogs(false);
		}

		private void ESO_browser_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
		{
			// načetl jsem dokument, načtu akci a zjistím, co dál
			switch (this.aktualniEsoAkce)
			{
				case EsoAkce.naPrvniUlozeniOdkazu:
					// Hodnoty nastavím již zde, abych se vyhnul při dělení nulou při výpočtu posuvníku...
					//NALUS_aktualneZpracovavanaStranka = 1;
					//NALUS_celkovyPocetZaznamu = NALUS_aktualneZpracovavanyZaznam = 1;
					// pokud bylo nalezeno. tak mohu pokračovat...
					if (!browser.Document.Body.InnerText.Contains("Nenalezeny žádné odpovídající záznamy"))
						ProvedEsoAkciUlozeniOdkazu(true);
					else
						this.ESO_KonecStahovani();
					break;

				//case EsoAkce.naDokument:
				//	this.ProvedEsoAkciNaDokument();
				//	break;

				case EsoAkce.naUlozeniOdkazu:
					//	NALUS_aktualneZpracovavanaStranka++;
					ProvedEsoAkciUlozeniOdkazu(false);
					break;

				case EsoAkce.naVyhledaniDat:
					this.ProvedEsoAkciVyhledaniDat();
					break;
			}
		}

		/// <summary>
		/// Vykoná akci naVyhledaniDat
		/// Vyplní vyhledávací formulář, stiskne tlačítko vyhledat a nastaví další akci na naPrvniUlozeniOdkazu
		/// </summary>
		private void ProvedEsoAkciVyhledaniDat()
		{
			/*
			 * Do této akce jsem se dostal navigací do vyhledávacího formuláře
			 * 
			 * Nastavím parametry vyhledávání, nastavím další akci ke zpracování & stisknu vyhledávací button
			 */
			// zadám pole do vyhledávacího formuláře
			browser.Document.GetElementById("DatumVydaniOd").SetAttribute("value", this.dtpDateEsoFrom.Value.ToShortDateString() + " 0:00:00");
			browser.Document.GetElementById("DatumVydaniDo").SetAttribute("value", this.dtpDateEsoUntil.Value.ToShortDateString() + " 0:00:00");
			this.aktualniEsoAkce = EsoAkce.naPrvniUlozeniOdkazu;

			HtmlElement el = this.browser.Document.All["search_btn"];
			el.InvokeMember("click");
			//HtmlElement el = this.browser.Document.All["searchForm"];
			//el.InvokeMember("submit");
			this.processedBar.Value = 10;
		}

		public void WaitForSecond(int sec)
		{
			System.Windows.Forms.Timer timer1 = new System.Windows.Forms.Timer();
			if (sec == 0 || sec < 0) return;
			timer1.Interval = sec * 1000;
			timer1.Enabled = true;
			timer1.Start();
			timer1.Tick += (s, e) =>
			{
				timer1.Enabled = false;
				timer1.Stop();

			};
			while (timer1.Enabled)
			{
				Application.DoEvents();
			}
		}


		/// <summary>
		/// Vykoná akci naPrvniUlozeniOdkazu, nebo naPrvniUlozeniOdkazu (dle parametru)
		/// naPrvniUlozeniOdkazu - Načte seznam stránek výsledků jako naUlozeniOdkazu (od poslední) - parametr je true +
		/// naUlozeniOdkazu - Získá seznam odkazů. Pro každý odkaz vytvoří akci naUlozeniObsahu - vždy
		/// </summary>
		private void ProvedEsoAkciUlozeniOdkazu(bool firstInvoke)
		{
			this.WaitForSecond(1);  // musíme nechat zpracovat události okna, aby bylo načtení stránky kompletní s celým obsahem, jinak bude stránka neúplná
			int iPosition;
			HtmlNode hn;
			string s, sExternalId;
			HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
			doc.LoadHtml(browser.Document.Body.OuterHtml);
			// musím vyzjistit počet stránek
			if (firstInvoke)
			{
				hn = doc.DocumentNode.SelectSingleNode("//li[@class='page-info']");
				//	1 - 50 / 56
				s = hn.InnerText;
				iPosition = s.IndexOf('/');
				s = s.Substring(iPosition + 1);
				this.dokumentuCelkem = Int32.Parse(s);
				double d = this.dokumentuCelkem / 50;
				int celkovyPocetStranek = (int)Math.Floor(d);
				if (celkovyPocetStranek == 0)
					celkovyPocetStranek = 1;
				this.ESO_progressStrankaPodil = (int)Math.Ceiling((double)80/(double)celkovyPocetStranek);
				if (this.ESO_progressStrankaPodil == 0)
					this.ESO_progressStrankaPodil = 1;
			}
			if ((this.processedBar.Value + this.ESO_progressStrankaPodil) <= 100)
				this.processedBar.Value += this.ESO_progressStrankaPodil;
			else
				this.processedBar.Value = this.ESO_progressStrankaPodil;

			HtmlAgilityPack.HtmlNodeCollection rows = doc.DocumentNode.SelectNodes("//table[@id='nalezene_tbl']/tbody/tr");
			foreach (HtmlAgilityPack.HtmlNode hnTr in rows)
			{
				hn = hnTr.SelectSingleNode("./td[5]");
				if (hn != null)
				{
					s = hn.InnerText.Trim();    // budeme stahovat jen některé formy
					if (s.Equals("Odložení") || s.Equals("Sankce - § 20") || s.Equals("Sankce (návštěva zařízení, sledování vyhoštění)- § 20")
						|| s.Equals("Závěrečné stanovisko - § 19") || s.Equals("Zpráva o šetření - § 17") || s.Equals("Zpráva o šetření - § 18")
						|| s.Equals("Zpráva o zjištění diskriminace - § 21b") || s.Equals("Zpráva o nezjištění diskriminace - § 21b") || s.Equals("Zpráva z návštěvy zařízení - §21a")
						|| s.Equals("Výzkumná zpráva (činnost úřadů)") || s.Equals("Doporučení (diskriminace) - § 21b") || s.Equals("Stanovisko (diskriminace) - § 21b")
						|| s.Equals("Výzkumná zpráva (diskriminace) - § 21b") || s.Equals("Souhrnná zpráva z návštěv zařízení - § 21a") || s.Equals("Výzkumná zpráva (detence)")
						|| s.Equals("Doporučení ke změně předpisů - § 22") || s.Equals("Doporučení (práva osob se zdravotním postižením) - § 21c") || s.Equals("Stanovisko (práva osob se zdravotním postižením) - § 21c")
						|| s.Equals("Výzkumná zpráva (práva osob se zdravotním postižením) - § 21c") || s.Equals("Zpráva (práva osob se zdravotním postižením) - § 21c"))
					{
						hn = hnTr.SelectSingleNode("./td[2]/a");
						iPosition = hn.Attributes["href"].Value.LastIndexOf('/');
						sExternalId = hn.Attributes["href"].Value.Substring(iPosition + 1);
						if (this.existingIds.Contains(sExternalId))
						{
							WriteIntoLogDuplicity("IdExternal [{0}] je v již v databázi!", sExternalId);
							continue;
						}
						seznamOdkazuKeZpracovani.Push(ESO_ODKAZ_DOKUMENT + sExternalId);
					}
				}
			}
			hn = doc.DocumentNode.SelectSingleNode("//nav//a[contains(@href,'fnNextPage')]");	// další stránka s výsledky
			if (hn != null)
			{
				this.aktualniEsoAkce = EsoAkce.naUlozeniOdkazu;
				this.browser.Document.InvokeScript("fnNextPage");   // toto pouze změní obsah stránky, ale nevyvolá událost DocumentCompleted
				this.WaitForSecond(1);
				this.ESO_browser_DocumentCompleted(null, null);
				//HtmlElementCollection col = this.browser.Document.GetElementsByTagName("a");
				//foreach (HtmlElement el in col)
				//{
				//	if (el.GetAttribute("href").Equals("javascript:fnNextPage()"))
				//	{
				//		el.InvokeMember("click");
				//		this.WaitForSecond(1);
				//		break;
				//	}
				//}
			}
			else
			{
				this.processedBar.Value = 0;
				if (this.seznamOdkazuKeZpracovani.Count == 0)
					this.FinalizeLogs(true);
				else
					this.SpustVlaknoProZpracovaniOdkazu();
			}
		}

		private void SpustVlaknoProZpracovaniOdkazu()
		{
			Task.Factory.StartNew(() =>
			{
				int iPosition;
				string sExternalId;
				// The delegate member for progress bar update
				var UpdateProgress = new UpdateDownloadProgressDelegate(USSK_UpdateDownloadProgressSafe);

				while (this.seznamOdkazuKeZpracovani.Count > 0)
				{
					// downloading the own document
					string sHtml, sAddress = String.Empty;
					using (System.Net.WebClient client = new System.Net.WebClient())
					{
						client.Encoding = Encoding.UTF8;
						try
						{
							sAddress = this.seznamOdkazuKeZpracovani.Pop();
							sHtml = client.DownloadString(sAddress);
						}
						catch (WebException wex)
						{
							WriteIntoLogCritical("[{0}]: {1}", sAddress, wex.Message);
							return;
						}
					}
					HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
					//doc.OptionDefaultStreamEncoding = Encoding.UTF8;

					doc.LoadHtml(sHtml);
					iPosition = sAddress.LastIndexOf('/');
					sExternalId = sAddress.Substring(iPosition + 1);
					this.Process1EsoDocument(doc, sAddress, sExternalId);

					if (this.seznamOdkazuKeZpracovani.Count == 0)
						this.processedBar.BeginInvoke(UpdateProgress, new object[] { true });
					else
						this.processedBar.BeginInvoke(UpdateProgress, new object[] { false });
					++this.aktualneZpracovavanyDokument;
				}
			});
		}

		private void Process1EsoDocument(HtmlAgilityPack.HtmlDocument pDoc, string pHref, string pIdExternal)
		{
			XmlNode xn;
			string[] values;
			string sSpZn = null, sDruh = null, sDatumVydani=null, sValue, sSector=null, sVec = null;
			List<string> lZakladniPredpis = new List<string>();
			List<string> lSouvisiEu = new List<string>();
			HtmlNode hnKey, hn;
			HtmlNodeCollection nodes = pDoc.DocumentNode.SelectNodes("//div[@role='rowgroup']/div[@role='row']");
			foreach(HtmlNode hnRow in nodes)
			{
				hnKey = hnRow.SelectSingleNode("./span[@role='columnheader']");
				if (hnKey!=null)
				{
					sValue = String.Empty;
					hn = hnKey.NextSibling;
					while(hn!=null)
					{
						sValue += HtmlAgilityPack.HtmlEntity.DeEntitize(hn.InnerText);
						hn = hn.NextSibling;
					}
					sValue = sValue.Trim();
					switch (hnKey.InnerText.Trim())
					{
						case "Spisová značka":
							sSpZn = sValue;
							break;
						case "Forma zjištění ochránce":
							switch(sValue)
							{
								case "Odložení":
								case "Sankce - § 20":
								case "Sankce (návštěva zařízení, sledování vyhoštění)- § 20":
									sSector = "J";
									sDruh = "Přípis";
									break;
								case "Závěrečné stanovisko - § 19":
									sSector = "J";
									sDruh = "Stanovisko";
									break;
								case "Zpráva o šetření - § 17":
								case "Zpráva o šetření - § 18":
								case "Zpráva o zjištění diskriminace - § 21b":
								case "Zpráva o nezjištění diskriminace - § 21b":
								case "Zpráva z návštěvy zařízení - §21a":
									sSector = "J";
									sDruh = "Zpráva";
									break;
								case "Výzkumná zpráva (činnost úřadů)":
								case "Doporučení (diskriminace) - § 21b":
								case "Stanovisko (diskriminace) - § 21b":
								case "Výzkumná zpráva (diskriminace) - § 21b":
								case "Souhrnná zpráva z návštěv zařízení - § 21a":
								case "Výzkumná zpráva (detence)":
								case "Doporučení ke změně předpisů - § 22":
								case "Doporučení (práva osob se zdravotním postižením) - § 21c":
								case "Stanovisko (práva osob se zdravotním postižením) - § 21c":
								case "Výzkumná zpráva (práva osob se zdravotním postižením) - § 21c":
								case "Zpráva (práva osob se zdravotním postižením) - § 21c":
									sSector = "L";
									break;
							}
							break;
						case "Datum vydání":
							sDatumVydani = UtilityBeck.Utility.ConvertDateIntoUniversalFormat(sValue, out DateTime? dt);
							break;
						case "Vztah k českým právním předpisům":
							values = sValue.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
							foreach (string s1 in values)
								if (!String.IsNullOrWhiteSpace(s1))
									lZakladniPredpis.Add(s1.Trim());
							break;
						case "Vztah k evropským právním předpisům":
							values = sValue.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
							foreach (string s1 in values)
								lSouvisiEu.Add(s1.Trim());
							break;
						case "Věc":
							sVec = sValue.Replace("\r", "").Replace("\n", ", ");
							//while (sVec.Contains("  "))
							//	sVec = sVec.Replace("  ", " ");
							sVec = sVec.Substring(0, 1).ToUpper() + sVec.Substring(1);
							break;
					}
				}
			}
			if ((sSector==null) || (sSpZn==null))
			{
				WriteIntoLogDuplicity("Došlo k závažné chybě při zpracování dokumentu: {0}", pHref);
				return;
			}

			string s;
			List<string> lVety = new List<string>();
			List<string> lText = new List<string>();
			HtmlNode hnSection = pDoc.DocumentNode.SelectSingleNode("//div[@class='content_filtr_botd']");
			hn = hnSection.FirstChild;
			while(hn != null)
			{
				if (hn.Name.Equals("div") && hn.Attributes["class"].Value.Equals("pravni_vety"))
				{
					values = hn.InnerText.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
					foreach (string s1 in values)
					{
						s = s1.Trim();
						if (!String.IsNullOrEmpty(s))
							lVety.Add(s);
					}
				}
				else if (hn.Name.Equals("p") && hn.Attributes["class"].Value.Equals("text_dokumentu"))
				{
					values = hn.InnerText.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
					foreach (string s1 in values)
					{
						s = s1.Trim();
						if (!String.IsNullOrEmpty(s))
							lText.Add(s);
					}
				}
				hn = hn.NextSibling;
			}

			UtilityBeck.LawDocumentInfo resultDoc;
			if (sSector.Equals("J"))
			{
				resultDoc = new UtilityBeck.LawDocumentInfo(sSpZn, "eso.ochrance.cz");
				resultDoc.ExternalId = pIdExternal;
				resultDoc.Druh = sDruh;
				resultDoc.ZeDne = sDatumVydani;
				resultDoc.Court = "Veřejný ochránce práv";
				resultDoc.Info3 = sVec;
				if (lVety.Count > 0)
					resultDoc.IsSentenceInText = true;
			}
			else
			{
				resultDoc = new UtilityBeck.LawDocumentInfo(sSector, sSpZn, "eso.ochrance.cz");
				// L_2006_24_NZ -> L_2006_VOP_24_NZ
				resultDoc.DocumentName = resultDoc.DocumentName.Substring(0, 7) + "VOP_" + resultDoc.DocumentName.Substring(7);
				resultDoc.DatePublication = sDatumVydani;
				resultDoc.Title = sVec.Substring(0, 1).ToUpper() + sVec.Substring(1);
				resultDoc.Vydavatel = "Veřejný ochránce práv";
				resultDoc.WebAddress = pHref;
				resultDoc.AddAuthorLiteratureItem(null, "Veřejný ochránce práv");
			}
			resultDoc.Druh = sDruh;
			resultDoc.SouvisiEu = lSouvisiEu;
			resultDoc.AddVeta(lVety);

			if (this.dokumentNames.Contains(resultDoc.DocumentName))
			{
				resultDoc.DocumentName += "_I";
				if (this.dokumentNames.Contains(resultDoc.DocumentName))
				{
					resultDoc.DocumentName += "I";
					if (this.dokumentNames.Contains(resultDoc.DocumentName))
						resultDoc.DocumentName += "I";
				}
			}

			// the text of the document
			XmlElement el;
			XmlNode xnHtmlText = resultDoc.xd.SelectSingleNode("//html-text");
			xnHtmlText.InnerXml = String.Empty;
			foreach (string s1 in lText)
			{
				el = resultDoc.xd.CreateElement("p");
				el.InnerText = s1;
				xnHtmlText.AppendChild(el);
			}

			// úprava základní předpis
			foreach(string sText in lZakladniPredpis)
			{
				List<BeckLinking.DocumentRelation> lRelations = BeckLinking.ParseRelation.ProcessOneTextPart(sText, this.dbConnection);
				BeckLinking.ParseRelation.WriteIntoXml("zakladnipredpis", resultDoc.xd, lRelations, true);
			}

			// oblasti práva
			AddLawArea(this.dbConnection, resultDoc.xd.DocumentElement.FirstChild, false);

			// linking
			BeckLinking.Linking oLinking = new BeckLinking.Linking(this.dbConnection, "cs", null);
			oLinking.Run(0, resultDoc.DocumentName, resultDoc.xd, 17);
			resultDoc.xd = oLinking.LinkedDocument;

			// souvisejíci dokumenty EU
			XmlNode xnSouvisiEu = resultDoc.xd.DocumentElement.FirstChild.SelectSingleNode("./souvisi_eu");
			if (xnSouvisiEu.ChildNodes.Count>0)
			{
				string sLink;
				oLinking.Run(0, resultDoc.DocumentName, resultDoc.xd, ref xnSouvisiEu, 17);
				foreach (XmlNode xnItem in xnSouvisiEu.ChildNodes)
				{
					if (xnItem.InnerXml.Contains("<link") && !xnItem.FirstChild.Name.Equals("link"))     // link přesuneme na první pozici
					{
						s = String.Empty;
						sLink = String.Empty;
						xn = xnItem.FirstChild;
						while(xn != null)
						{
							if (xn.Name.Equals("link"))
								sLink = xn.OuterXml;
							else
								s += xn.OuterXml;
							xn = xn.NextSibling;
						}
						xnItem.InnerXml = sLink + s;
					}
				}
			}

			try
			{
				UtilityBeck.UtilityXml.AddCite(resultDoc.xd, resultDoc.DocumentName, this.dbConnection);
			}
			catch (Exception ex)
			{
				this.WriteIntoLogCritical(ex.Message);
			}
			//UtilityXml.RemoveRedundantEmptyRowsInXmlDocument(ref xnDocumentText);

			resultDoc.SaveResultNewDocument(null, this.txtOutputFolder.Text);
		}
	}
}
