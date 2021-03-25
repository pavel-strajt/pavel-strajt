using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Xml;
using DataMiningCourts.Properties;
using System.Data.SqlClient;
using UtilityBeck;
using BeckLinking;
using System.Text.RegularExpressions;

namespace DataMiningCourts
{
    class VZ_ThreadPoolDownload : ALL_ThreadPoolDownload
    {
        private string sFilePathToWriteFileTo;
        public VZ_ThreadPoolDownload(FrmCourts frm, ManualResetEvent doneEvent, string sDirectoryPathToWriteFileTo, string sFilePathToWriteFileTo) : base(frm, doneEvent, sDirectoryPathToWriteFileTo)
        {
            this.doneEvent = doneEvent;
            this.sFilePathToWriteFileTo = sFilePathToWriteFileTo;
        }

        public void DownloadDocument(object o)
        {
            VZ_WebDokumentJUD dokJUD = null;
            XmlNode xn;
            XmlDocument xmlDoc = null;
            string tmpFolder = null, downloadedFilePath = null;
            Uri url = null;
            DateTime datum = DateTime.MinValue;
            var sUrlToReadFileFrom = o.ToString();
            try
            {
                tmpFolder = Path.Combine(directoryPathToWriteFileTo, sFilePathToWriteFileTo);
                if (!Directory.Exists(tmpFolder))
                {
                    Directory.CreateDirectory(tmpFolder);
                }

                url = new Uri(sUrlToReadFileFrom);
                var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                var sSource = string.Empty;

                using (StreamReader reader = new StreamReader(request.GetResponse().GetResponseStream(), Encoding.GetEncoding("windows-1250")))
                {
                    sSource = reader.ReadToEnd();
                }

                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.OptionOutputAsXml = true;

                doc.LoadHtml(sSource);

                xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(doc.DocumentNode.OuterHtml);

                //Process
                var datumNode = xmlDoc.DocumentElement.SelectSingleNode("//legend");
                if (datumNode == null || !DateTime.TryParseExact(datumNode.InnerText, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out datum))
                {
                    throw new ApplicationException("Na stránce nebylo nalezeno datum.");
                }

                //attachment
                var attNodes = xmlDoc.DocumentElement.SelectNodes("//table[@class='lfr-table djv-agenda-table']//tr[td[contains(text(), 'Příloha')]]");
                if (attNodes != null)
                {
                    foreach (XmlNode attNode in attNodes)
                    {
                        var urlNode = attNode.SelectSingleNode("./td//a[.='PDF'][@href]");
                        if (urlNode == null) return;

                        var docUrl = urlNode.Attributes["href"]?.Value;
                        if (!string.IsNullOrWhiteSpace(docUrl))
                        {
                            url = new Uri(string.Format(parentWindowForm.VZ_PAGE_PREFIX, docUrl));
                            var popisPrilohy = attNode.SelectSingleNode("./td[5]").InnerText;
                            var cislaPrilohy = Regex.Matches(popisPrilohy, @"\d+");
                            var fileName = "Priloha_Vz_" + datum.Year + "_" + cislaPrilohy[1] + "_UsnV-" + cislaPrilohy[0] + ".pdf";
                            downloadedFilePath = Path.Combine(tmpFolder, fileName);

                            Download(url, downloadedFilePath);
                        }
                    }

                }
            }
            catch (Exception ex)
            {
                this.parentWindowForm.WriteIntoLogCritical("Download document 1:\t" + ex.Message);
            }

			//docs
			if (xmlDoc != null)
			{
				var docNodes = xmlDoc.DocumentElement.SelectNodes("//table[@class='lfr-table djv-agenda-table']//tr[td[contains(text(), 'Usnesení č.')]]");
				if (docNodes != null)
				{
					foreach (XmlNode docNode in docNodes)
					{
						var urlNode = docNode.SelectSingleNode("./td//a[.='DOC'][@href]");
						if (urlNode == null) continue;

						xn = docNode.SelectSingleNode("./td[2]");
						if (xn == null)
							throw new ApplicationException("Nebyla nelezena buňka s číslem usnesení.");

						var cisloUsneseni = Regex.Replace(xn.InnerText.Replace("Usnesení č.", string.Empty), @"\s+", string.Empty);
						var docUrl = urlNode.Attributes["href"]?.Value;
						if (!string.IsNullOrWhiteSpace(docUrl))
						{
							try
							{
								url = new Uri(string.Format(parentWindowForm.VZ_PAGE_PREFIX, docUrl));
								var splits = docUrl.Split('/');
								var docNazev = splits[splits.Length - 1];
								var fileName = docNazev + ".doc";

								downloadedFilePath = Path.Combine(tmpFolder, fileName);

								Download(url, downloadedFilePath);

								dokJUD = new VZ_WebDokumentJUD();
								dokJUD.PathOutputFolder = tmpFolder;
							}
							catch (Exception ex)
							{
								this.parentWindowForm.WriteIntoLogCritical("Download document 2:\t" + ex.Message);
								continue;
							}
							try
							{
								var hlavicka = LoadHeader(cisloUsneseni, datum);
								dokJUD.ZalozDokument(hlavicka);
							}
							catch (DuplicityException ex)
							{
								this.parentWindowForm.WriteIntoLogDuplicity(ex.Message);
								continue;
							}
							catch (Exception ex)
							{
								this.parentWindowForm.WriteIntoLogCritical(cisloUsneseni + "Create header:\t" + ex.Message);
								continue;
							}

							try
							{
								File.Move(downloadedFilePath, dokJUD.PathDoc);
								FrmCourts.OpenFileInWordAndSaveInWXml(dokJUD.PathDoc, dokJUD.PathTmpXml);
							}
							catch (Exception ex)
							{
								this.parentWindowForm.WriteIntoLogCritical("Download document 3:\t" + ex.Message);
								continue;
							}

							try
							{
								var sErrors = String.Empty;
								if (!dokJUD.ExportFromMsWord(ref sErrors))
								{
									this.parentWindowForm.WriteIntoLogExport(sErrors);
									continue;
								}
								if (!string.IsNullOrWhiteSpace(sErrors))
								{
									this.parentWindowForm.WriteIntoLogExport(sErrors);
								}
								//PostProcess
								var dOut = new XmlDocument();
								dOut.Load(dokJUD.PathResultXml);

								var htmlTextNode = dOut.SelectSingleNode("//html-text");
								if (htmlTextNode != null)
								{

									var attFilename = "Priloha_Vz_" + datum.Year + "_" + cisloUsneseni + "_UsnV-*.pdf";
									var attFiles = new DirectoryInfo(tmpFolder).GetFiles(attFilename).OrderBy(x => x.Name);
									var counter = 1;
									foreach (var attFile in attFiles)
									{
										var docFrag = dOut.CreateDocumentFragment();
										docFrag.InnerXml = "<priloha href=\"" + attFile.Name + "\" id-block=\"pr" + counter + "\"><title-num>Příloha č." + counter + "</title-num></priloha>";

										dOut.DocumentElement.InsertAfter(docFrag, htmlTextNode);
										File.Move(attFile.FullName, Path.Combine(dokJUD.PathFolder, attFile.Name));
										counter++;
									}
								}
								var vladaNode = htmlTextNode.SelectSingleNode("//p/span[contains(text(), 'VLÁDA ČESKÉ REPUBLIKY')]");
								if (vladaNode != null)
								{
									htmlTextNode.RemoveChild(vladaNode.ParentNode);
								}
								var imgNode = htmlTextNode.SelectSingleNode("//img[1]");
								if (imgNode != null)
								{
									imgNode.ParentNode.RemoveChild(imgNode);
								}
								var castkaNode = dOut.DocumentElement.SelectSingleNode("//hlavicka-vestnik/castka");
								if (castkaNode != null)
								{
									castkaNode.InnerText = cisloUsneseni + "/" + datum.Year;
								}
								var titulNode = dOut.DocumentElement.SelectSingleNode("//hlavicka-vestnik/titul");
								if (titulNode != null)
								{
									var pNodes = htmlTextNode.SelectNodes("//p");
									var titulText = string.Empty;
									var process = false;
									var boldDetected = false;
									var firstBoldDetected = false;
									foreach (XmlNode pNode in pNodes)
									{
										if (pNode.InnerText == "Vláda")
										{
											break;
										}
										if (process)
										{
											boldDetected = pNode.HasChildNodes && pNode.ChildNodes.Count == 1 && pNode.FirstChild.Name.ToLower() == "b";
											titulText += pNode.InnerText.Trim() + " ";
											if (!firstBoldDetected && boldDetected)
											{
												firstBoldDetected = true;
											}
											if (!boldDetected && firstBoldDetected)
											{
												break;
											}
											continue;
										}
										if (pNode.InnerText == "USNESENÍ")
										{
											titulText += "Usnesení ";
											continue;
										}
										if (pNode.InnerText == "VLÁDY ČESKÉ REPUBLIKY")
										{
											process = true;
											titulText += "vlády České republiky ";
											continue;
										}
									}
									titulNode.InnerText = titulText.Trim();
								}

								var xnDocumentText = dOut.SelectSingleNode("//html-text");
								UtilityXml.RemoveRedundantEmptyRowsInXmlDocument(ref xnDocumentText);
								dOut.Save(dokJUD.PathResultXml);

								// výsledek
								var currentDocumentName = dokJUD.DocumentName;

								string outputDirectoryFullName = String.Format(@"{0}\{1}", this.parentWindowForm.XML_DIRECTORY, dokJUD.DocumentName);
								if (Directory.Exists(outputDirectoryFullName))
								{
									this.parentWindowForm.WriteIntoLogCritical("Složka pro dokumentName [{0}] již existuje. Může se jednat o problém s duplicitními spisovými značkami. Po uložení aktuálně stažených dokumentů do db stáhněte dokumenty za období znovu...", outputDirectoryFullName);
								}
								else
								{
									Directory.Move(dokJUD.PathFolder, outputDirectoryFullName);
									// mass rename if necesarry
									if (!String.Equals(dokJUD.DocumentName, currentDocumentName, StringComparison.OrdinalIgnoreCase))
									{
										Utility.MassRename(outputDirectoryFullName, dokJUD.DocumentName, currentDocumentName);
									}
								}
							}
							catch (Exception ex)
							{
								this.parentWindowForm.WriteIntoLogCritical("Download document 4:\t" + ex.Message);
							}
						}
					}
				}
			}
			doneEvent.Set();
        }

        private void Download(Uri url, string filePath)
        {
            try
            {
                var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                var response = (System.Net.HttpWebResponse)request.GetResponse();
                response.Close();

                var iSize = response.ContentLength;
                var iRunningByteTotal = 0;
                using (System.Net.WebClient client = new System.Net.WebClient())
                {
                    using (System.IO.Stream streamRemote = client.OpenRead(url))
                    {
                        using (Stream streamLocal = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            int iByteSize = 0;
                            byte[] byteBuffer = new byte[iSize];
                            while ((iByteSize = streamRemote.Read(byteBuffer, 0, byteBuffer.Length)) > 0)
                            {
                                streamLocal.Write(byteBuffer, 0, iByteSize);
                                iRunningByteTotal += iByteSize;
                            }
                            streamLocal.Close();
                        }
                        streamRemote.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                this.parentWindowForm.WriteIntoLogCritical("Download 2:" + ex.Message);
            }
        }

        private VZ_WebHeader LoadHeader(string cisloUsneseni, DateTime datumSchvaleni)
        {
            var webhlav = new VZ_WebHeader();

            webhlav.DatumSchvaleni = datumSchvaleni;
            webhlav.CisloUsneseni = cisloUsneseni;
            var citace = string.Format("{0}/{1}", cisloUsneseni, webhlav.DatumSchvaleni.Year);
            webhlav.Citace = string.Format("{0} UsnV", citace);
            if (citation.CitationIsAlreadyinDb(webhlav.Citace))
            {
                throw new DuplicityException(String.Format("Znacka [{0}] s daným datem rozhodnutí [{1}] je v jiz databazi!", webhlav.CisloUsneseni, webhlav.DatumSchvaleni));
            }

            return webhlav;
        }
    }
}
