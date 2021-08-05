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
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Globalization;
using HtmlAgilityPack;

namespace DataMiningCourts
{
	public partial class FrmCourts : Form
	{
		private const string JUSTICE_INDEX = "https://rozhodnuti.justice.cz/soudnirozhodnuti/";

		private const int JUSTICE_MAX_EXT_ID = 1000000;

		/// <summary>
		/// Velikost bufferu blokující kolekce shromaždující odkazy.
		/// Větší velikost = (větší) propustnost a paměťové nároky
		/// </summary>
		private const int JUSTICE_BUFFER_SIZE_HREF = 10000;

		/// <summary>
		/// Velikost bufferu blokující kolekce shromaždující stažené dokumenty.
		/// Větší velikost = (větší) propustnost a paměťové nároky
		/// </summary>
		private const int JUSTICE_BUFFER_SIZE_DOCUMENT = 50;


		private int justice_MaxEmptyExtIdInARow = 0;

		/// <summary>
		/// Seznam webových stránek s rozhodnutími ke stažení
		/// Stránky jsou stahovány až v následujícím kroku (JUSTICE_SpustVlaknoProStazeniRozhodnuti)
		/// </summary>
		private BlockingCollection<string> justice_listOfHrefsToDownload;

		/// <summary>
		/// Seznam webových stránek se staženými rozhodnutími
		/// Rozhodnutí jsou generována až v následujícím kroku (JUSTICE_SpustVlaknoProZpracovaníRozhodnuti)
		/// </summary>
		private BlockingCollection<HtmlAgilityPack.HtmlDocument> justice_listOfDecisionsToProcess;


		/// <summary>
		/// Kolik záznamů (výsledků) vyhledávání je celkem za všechny iterace
		/// </summary>
		private int justice_XML_totalRecordsToProcess;

		/// <summary>
		/// Jaký záznam (XML) aktuálně zpracováváme (kvůli progress baru)
		/// </summary>
		private int justice_XML_actualRecordToProcess;

		private void justice_cbLastFromDb_CheckedChanged(object sender, EventArgs e)
		{
			nudJusticeExtNumber.Enabled = nudJusticeExtNumberDo.Enabled = !cbJusticeLastFromDb.Checked;
		}

		private bool Justice_Click()
		{
			var sError = Justice_CheckFilledValues();
			if (!string.IsNullOrEmpty(sError))
			{
				MessageBox.Show(this, sError, ERROR_CHECKING_OUTPUT, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				return false;
			}

			this.btnMineDocuments.Enabled = false;
			this.processedBar.Value = 0;

			this.justice_listOfHrefsToDownload = new BlockingCollection<string>(JUSTICE_BUFFER_SIZE_HREF);
			this.justice_listOfDecisionsToProcess = new BlockingCollection<HtmlAgilityPack.HtmlDocument>(JUSTICE_BUFFER_SIZE_DOCUMENT);

			justice_XML_totalRecordsToProcess = 0;
			justice_XML_actualRecordToProcess = 0;

			justice_MaxEmptyExtIdInARow = 0;

			JUSTICE_ActionSavingHref();

			JUSTICE_LaunchThreadToDownloadDecision();
			JUSTICE_StartThreadForProcessingDecision();

			JUSTICE_UpdateDownloadProgressSafe(false);

			return true;
		}

		private string Justice_CheckFilledValues()
		{
			var sbResult = new StringBuilder();
			if (string.IsNullOrWhiteSpace(this.txtWorkingFolder.Text))
			{
				sbResult.AppendLine("Pracovní složka (místo pro uložení surových dat, musí být vybrána.");
			}

			return sbResult.ToString();
		}

		private void JUSTICE_LaunchThreadToDownloadDecision()
		{
			Task.Factory.StartNew(() =>
			{
				string urlToDownload = null;
				while (!justice_listOfHrefsToDownload.IsCompleted)
				{
					if ((int)nudJusticeMaxFailedAttempts.Value == justice_MaxEmptyExtIdInARow)
					{
						WriteIntoLogCritical(string.Format("Bylo dosaženo limitu ({0}) na počet chybných extid v řadě.", nudJusticeMaxFailedAttempts.Value));
						while (justice_listOfHrefsToDownload.TryTake(out _)) { }
						justice_listOfHrefsToDownload.CompleteAdding();
					}

					while (!justice_listOfHrefsToDownload.IsCompleted && !justice_listOfHrefsToDownload.TryTake(out urlToDownload, 2000)) ;

					if (urlToDownload != null)
					{
						clientVerdictDownload.Encoding = Encoding.UTF8;
						HtmlAgilityPack.HtmlDocument decisionDoc = new HtmlAgilityPack.HtmlDocument();
						try
						{
							var uriToDownload = new Uri(urlToDownload, UriKind.Absolute);
							var bytePage = clientHeaderDownload.DownloadData(uriToDownload);
							var stringPage = Encoding.UTF8.GetString(bytePage);

							if (stringPage.Contains("Omlouváme se, požadované rozhodnutí nebylo v databázi nalezeno."))
							{
								--justice_XML_totalRecordsToProcess;
								justice_MaxEmptyExtIdInARow++;
								WriteIntoLogCritical(string.Format("Požadované rozhodnutí {0} nebylo nalezeno", urlToDownload));
								continue;
							}
							else
							{
								decisionDoc.LoadHtml(WebUtility.HtmlDecode(stringPage));
								justice_MaxEmptyExtIdInARow = 0;
							}
						}
						catch (WebException ex)
						{
							WriteIntoLogCritical(String.Format("Data dokumentu se z webové stránky {0} nepodařilo stáhnout.{1}\t[{2}]", urlToDownload, Environment.NewLine, ex.Message));
							--justice_XML_totalRecordsToProcess;
							justice_MaxEmptyExtIdInARow++;
							continue;
						}

						decisionDoc.Save(string.Format(@"{0}\{1}", this.txtWorkingFolder.Text, urlToDownload.Substring(urlToDownload.LastIndexOf('/') + 1)), Encoding.UTF8);
						decisionDoc.DocumentNode.Attributes.Add("id", urlToDownload.Split('/').Last());

						justice_listOfDecisionsToProcess.Add(decisionDoc);
					}
				}
				justice_listOfDecisionsToProcess.CompleteAdding();
			});
		}

		private void JUSTICE_StartThreadForProcessingDecision()
		{
			Task.Factory.StartNew(() =>
			{
				var UpdateProgress = new UpdateDownloadProgressDelegate(JUSTICE_UpdateDownloadProgressSafe);

				HtmlAgilityPack.HtmlDocument docToProcess = null;
				while (!justice_listOfDecisionsToProcess.IsCompleted)
				{
					while (!justice_listOfDecisionsToProcess.IsCompleted && !justice_listOfDecisionsToProcess.TryTake(out docToProcess, 2000)) ;
					++justice_XML_actualRecordToProcess;
					if (docToProcess != null)
					{
						var referenceNumber = string.Empty;
						var spZnNode = docToProcess.DocumentNode.SelectSingleNode("//dl/dt[text()='Jednací číslo:']/../dd");
						if (spZnNode != null)
						{
							var tInfo = new CultureInfo("cs-CZ", false).TextInfo;
							referenceNumber = tInfo.ToTitleCase(spZnNode.InnerText.Trim());
						}

						var resultDoc = new LawDocumentInfo(referenceNumber, "www.justice.cz"); ;
						resultDoc.ExternalId = docToProcess.DocumentNode.Attributes["id"].Value;
						resultDoc.Category = "Databáze rozhodnutí okresních, krajských a vrchních soudů";

						var autorNode = docToProcess.DocumentNode.SelectSingleNode("//dl/dt[text()='Soud:']/../dd");
						if (autorNode != null)
						{
							resultDoc.Court = autorNode.InnerText.Trim();
						}

						var year = 1900;
						var zeDneNode = docToProcess.DocumentNode.SelectSingleNode("//dl/dt[text()='Datum vydání:']/../dd");
						if (zeDneNode != null)
						{
							if (DateTime.TryParseExact(zeDneNode.InnerText.Replace(" ", string.Empty).Trim(), "dd.MM.yyyy", null, DateTimeStyles.None, out DateTime zeDne))
							{
								resultDoc.ZeDne = zeDne.ToString(Utility.DATE_FORMAT);
								year = zeDne.Year;
							}
						}

						var ecliNode = docToProcess.DocumentNode.SelectSingleNode("//dl/dt[text()='Identifikátor ECLI:']/../dd");
						if (ecliNode != null)
							resultDoc.Ecli = ecliNode.InnerText.Trim();
						else
						{
							this.WriteIntoLogCritical("IDExternal: {0} - nenalezeno Ecli!");
							continue;
						}

						// máme už rozhodnutí v db?
						if (!this.cbCheckDuplicities.Checked)
							if (!this.JUSTICE_CheckIfExtIdExists(resultDoc.ExternalId, resultDoc.Ecli))
							{
								var predmetRizeniNode = docToProcess.DocumentNode.SelectSingleNode("//dl/dt[text()='Předmět řízení:']/../dd");
								if (predmetRizeniNode != null)
								{
									resultDoc.Info3 = predmetRizeniNode.InnerText.Trim();
								}

								var datumVydaniNode = docToProcess.DocumentNode.SelectSingleNode("//dl/dt[text()='Datum zveřejnění:']/../dd");
								if (datumVydaniNode != null)
								{
									if (DateTime.TryParseExact(datumVydaniNode.InnerText.Replace(" ", string.Empty).Trim(), "dd.MM.yyyy", null, DateTimeStyles.None, out DateTime datumVydani))
									{
										resultDoc.DatePublication = datumVydani.ToString(Utility.DATE_FORMAT);
									}
								}

								var dalsiHeslaNode = docToProcess.DocumentNode.SelectSingleNode("//dl/dt[text()='Klíčová slova:']/../dd");
								if (dalsiHeslaNode != null)
								{
									var r2 = dalsiHeslaNode.SelectNodes("./div[contains(@class,'compactCard')]").Select(x => x.InnerText).ToList();
									if (r2.Count > 0)
									{
										resultDoc.Rejstrik2 = r2;
									}
								}

								Utility.CreateDocumentName("J", referenceNumber, year.ToString(), out string docName);
								if (string.IsNullOrEmpty(docName))
								{
									this.WriteIntoLogCritical("IDExternal: {0} - nepodařilo se vytvořit DokumentName!");
									continue;
								}

								resultDoc.DocumentName = docName;

								/* Vytvoření těla dokumentu... */
								var mainHtmlContentNode = docToProcess.DocumentNode.SelectSingleNode("(//div[@id='PrintDiv']//center)[1]");
								var naveti = string.Empty;
								var vyrok = string.Empty;
								var oduvodneni = string.Empty;
								var akce = 0;
								while (mainHtmlContentNode != null)
								{
									if (mainHtmlContentNode.Name.Equals("center"))
									{
										if (mainHtmlContentNode.InnerText.Contains("JMÉNEM REPUBLIKY"))
										{
											if (mainHtmlContentNode.InnerText.ToLower().Contains("rozsudek"))
											{
												resultDoc.Druh = "Rozsudek";
											}
											else if (mainHtmlContentNode.InnerText.ToLower().Contains("stanovisko"))
											{
												resultDoc.Druh = "Stanovisko";
											}
											else if (mainHtmlContentNode.InnerText.ToLower().Contains("usnesení"))
											{
												resultDoc.Druh = "Usnesení";
											}
											else
											{
												WriteIntoLogCritical("Pro ExternalId={0} nebyl nalezen druh.", resultDoc.ExternalId);
											}
											akce = 1;
											mainHtmlContentNode = mainHtmlContentNode.NextSibling;
											continue;
										}
										else if (mainHtmlContentNode.InnerText.Contains("takto:"))
										{
											akce = 2;
										}
										else if (mainHtmlContentNode.InnerText.Contains("Odůvodnění:"))
										{
											akce = 3;
										}

										var pCenterNode = HtmlNode.CreateNode(@"<p class=""center""><b>" + mainHtmlContentNode.InnerText + "</b></p>");
										mainHtmlContentNode.ParentNode.ReplaceChild(pCenterNode, mainHtmlContentNode);
										mainHtmlContentNode = pCenterNode;
									}

									var tmptext = mainHtmlContentNode.Name == "p"
										? mainHtmlContentNode.OuterHtml
										: mainHtmlContentNode.InnerHtml;

									if (akce == 1)
									{
										naveti += tmptext;
									}
									else if (akce == 2)
									{
										vyrok += tmptext;
									}
									else if (akce == 3)
									{
										oduvodneni += tmptext;
									}

									mainHtmlContentNode = mainHtmlContentNode.NextSibling;
								}

								var xnHtmlText = resultDoc.xd.SelectSingleNode("//html-text");
								xnHtmlText.InnerXml = string.Empty;

								AddHtmlText(resultDoc.xd, xnHtmlText, "naveti", naveti);
								AddHtmlText(resultDoc.xd, xnHtmlText, "vyrok", vyrok);
								AddHtmlText(resultDoc.xd, xnHtmlText, "oduvodneni", oduvodneni);

								xnHtmlText.ParentNode.RemoveChild(xnHtmlText);

								// úprava základní předpis
								var nadrPredpisyNode = docToProcess.DocumentNode.SelectSingleNode("//dl/dt[text()='Zmíněná ustanovení:']/../dd");
								if (nadrPredpisyNode != null)
								{
									var zakladniPredpisy = nadrPredpisyNode.SelectNodes("./div[contains(@class,'compactCard')]").ToList()
									.Where(x => x.InnerText.StartsWith("z. č.") || x.InnerText.StartsWith("nař.vl. č.") || x.InnerText.StartsWith("§")).Select(x => x.InnerText).ToList();

									if (zakladniPredpisy.Count > 0)
									{
										foreach (string sText in zakladniPredpisy)
										{
											// přeskočíme vazby na předpisy 99/1963 Sb., 177/1996 Sb., 549/1991 Sb., 351/2013 Sb., 141/1961 Sb.
											if (sText.Contains("99/1963 Sb") || sText.Contains("177/1996 Sb") || sText.Contains("549/1991 Sb") || sText.Contains("351/2013 Sb") || sText.Contains("141/1961 Sb"))
												continue;
											var lRelations = BeckLinking.ParseRelation.ProcessOneTextPart(sText, this.dbConnection);
											BeckLinking.ParseRelation.WriteIntoXml("zakladnipredpis", resultDoc.xd, lRelations, true);
										}
									}
								}

								// oblasti práva
								AddLawArea(this.dbConnection, resultDoc.xd.DocumentElement.FirstChild, false);

								// linking
								BeckLinking.Linking oLinking = new BeckLinking.Linking(this.dbConnection, "cs", null);
								oLinking.Run(0, resultDoc.DocumentName, resultDoc.xd, 17);
								resultDoc.xd = oLinking.LinkedDocument;

								try
								{
									UtilityXml.AddCite(resultDoc.xd, resultDoc.DocumentName, this.dbConnection);
								}
								catch (Exception ex)
								{
									this.WriteIntoLogCritical(ex.Message);
								}

								resultDoc.SaveResultNewDocument(null, this.txtOutputFolder.Text);
							}
							else
								this.WriteIntoLogDuplicity("Ecli {0} je již v databázi!", resultDoc.ExternalId);

						File.Delete(Path.Combine(this.txtWorkingFolder.Text, resultDoc.ExternalId));

						this.processedBar.BeginInvoke(UpdateProgress, new object[] { false });
					}
				}

				/* Hotovo*/
				this.processedBar.BeginInvoke(UpdateProgress, new object[] { true });
				FinalizeLogs(false);
			});
		}

		private void AddHtmlText(XmlDocument xmlDoc, XmlNode xmlNode, string roleName, string content)
		{
			content = content.Trim();
			if (!string.IsNullOrWhiteSpace(content))
			{
				var el = xmlDoc.CreateElement("html-text");
				el.SetAttribute("role", roleName);
				if (!content.StartsWith("<p"))
				{
					content = "<p>" + content + "</p>";
				}
				el.InnerXml = ParseContent(content);
				this.CorrectNestedParagraphs(el.ChildNodes);
				xmlNode.ParentNode.InsertBefore(el, xmlNode);
			}
		}

		protected string ParseContent(string content)
		{
			if (content != null)
			{
				//content = content.Replace("&", "&amp;");

				var document = new HtmlAgilityPack.HtmlDocument();
				document.LoadHtml(content);

				var markNodes = document.DocumentNode.Descendants("mark").ToList();
				if (markNodes.Count() > 0)
				{
					foreach (var markNode in markNodes)
					{
						var newNode = document.CreateElement("span");
						newNode.SetAttributeValue("class", "anonymous");
						newNode.InnerHtml = markNode.InnerHtml;
						markNode.ParentNode.InsertAfter(newNode, markNode);
						markNode.Remove();
					}
					//return WebUtility.HtmlDecode(document.DocumentNode.OuterHtml.Replace("&", "&amp;"));
				}

				var attNodes = document.DocumentNode.Descendants().Where(x => x.NodeType == HtmlNodeType.Element && x.Attributes.Any()).ToList();
				foreach (var eachNode in attNodes)
				{
					if (eachNode.Attributes.Contains("class"))
					{
						var result = new List<string>();
						var classes = eachNode.Attributes["class"].Value.Split(' ').ToList();

						foreach (var classValue in classes)
						{
							if (eachNode.Name == "span")
							{
								if (classValue.StartsWith("small") || classValue.StartsWith("vyplnit") || classValue.StartsWith("anonymous"))
								{
									result.Add(classValue);
								}
							}
							else
							{
								if (!classValue.StartsWith("mb")
									&& !classValue.StartsWith("mx")
									&& !classValue.StartsWith("mt")
									&& !classValue.StartsWith("text-")
									&& !classValue.StartsWith("p-")
									&& !classValue.StartsWith("m-"))
								{
									result.Add(classValue);
								}
							}
						}
						var newValue = string.Join(" ", result);
						if (!string.IsNullOrWhiteSpace(newValue))
						{
							eachNode.Attributes["class"].Value = newValue;
						}
						else
						{
							eachNode.Attributes["class"].Remove();
						}
					}
					if (eachNode.Attributes.Contains("style"))
					{
						eachNode.Attributes["style"].Remove();
					}
				}
				return WebUtility.HtmlDecode(document.DocumentNode.OuterHtml.Replace("&", "&amp;"));
			}
			return null;
		}

		private void JUSTICE_ActionSavingHref()
		{
			var startId = cbJusticeLastFromDb.Checked ? JUSTICE_GetMaxExtId() : (int)nudJusticeExtNumber.Value;
			var endId = cbJusticeLastFromDb.Checked ? JUSTICE_MAX_EXT_ID : (int)nudJusticeExtNumberDo.Value;
			//generate links https://rozhodnuti.justice.cz/rozhodnuti/113835 - the brute force way
			var generatedLinks = Enumerable.Range(startId, endId - startId + 1)
						.Select(n => string.Format("https://rozhodnuti.justice.cz/rozhodnuti/{0}", n))
						.ToList();
			justice_listOfHrefsToDownload = new BlockingCollection<string>(new ConcurrentQueue<string>(generatedLinks));
			justice_XML_totalRecordsToProcess = justice_listOfHrefsToDownload.Count;
		}

		private int JUSTICE_GetMaxExtId()
		{
			var result = 1;
			var cmd = dbConnection.CreateCommand();
			cmd.CommandText = @"SELECT isnull(Max(HeaderSub.IDExternal),1) FROM Dokument INNER JOIN HeaderSub ON Dokument.IDDokument = HeaderSub.IDDokument WHERE IDTJournal = 256";
			if (dbConnection.State == System.Data.ConnectionState.Closed)
			{
				dbConnection.Open();
			}
			using (SqlDataReader dr = cmd.ExecuteReader())
			{
				if (dr.HasRows)
				{
					dr.Read();
					result = int.Parse(dr.GetString(0));
				}
				dr.Close();
			}
			return result;
		}

		private bool JUSTICE_CheckIfExtIdExists(string idExternal, string pEcli)
		{
			var result = false;
			var cmd = dbConnection.CreateCommand();
			cmd.CommandText = @"SELECT 1 FROM HeaderSub WHERE IDTJournal=256 AND HeaderSub.IDExternal='" + idExternal + "'";
			if (dbConnection.State == System.Data.ConnectionState.Closed)
			{
				dbConnection.Open();
			}
			using (SqlDataReader dr = cmd.ExecuteReader())
			{
				result = dr.HasRows ? true : false;
				dr.Close();
			}
			if (!result)
			{
				cmd.CommandText = @"SELECT 1 FROM HeaderSub WHERE IDTJournal=256 AND HeaderSub.Ecli='" + pEcli + "'";
				using (SqlDataReader dr = cmd.ExecuteReader())
				{
					if (dr.HasRows)
						result = true;
					dr.Close();
				}
			}
			return result;
		}

		public void JUSTICE_UpdateDownloadProgressSafe(bool forceComplete)
		{
			var value = 0;
			if (justice_XML_totalRecordsToProcess == 0)
			{
				forceComplete = true;
			}
			else
			{
				value = (justice_XML_actualRecordToProcess / justice_XML_totalRecordsToProcess) * 100;
			}

			if (forceComplete)
			{
				value = this.processedBar.Maximum;
			}

			this.processedBar.Value = value;
#if DEBUG
			this.gbProgressBar.Text = String.Format("Procházím {0}/{1} XML => {2}%", justice_XML_actualRecordToProcess, justice_XML_totalRecordsToProcess, this.processedBar.Value);
#endif
			var maximumReached = (this.processedBar.Value == this.processedBar.Maximum);
			this.btnMineDocuments.Enabled = maximumReached;
		}
	}
}
