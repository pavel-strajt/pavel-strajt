using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;
using System.Data.SqlClient;
using DataMiningCourts.Properties;
using System.Text.RegularExpressions;
using System.Xml;

namespace DataMiningCourts
{
    abstract class ALL_ThreadPoolDownload
    {
        /// <summary>
        /// Dirty trick, that will provide:
		/// Access to the logs and
        /// Access to the directories (working, backup, output)
        /// 
        /// </summary>
        protected FrmCourts parentWindowForm;

        public static void InitializeCitationService(FrmCourts.Courts pCourt)
        {
            citation = new CitationService(pCourt);
        }

		public static void InitializeCitationService(FrmCourts.Courts pCourt, Dictionary<int, int> pOldCitation)
        {
            citation = new CitationService(pCourt, pOldCitation);
        }

		public static bool CheckForForeignId(string pForeignId)
		{
			if (citation == null)
			{
				return false;
			}
			return citation.ForeignIdIsAlreadyInDb(pForeignId);
		}


        public static Dictionary<int, int> GetCitationServiceData()
        {
            return citation.LastCitationOfTheYear;
        }

        /// <summary>
        /// Generating unique citation id & checking wheter the download document is already in the DB
        /// </summary>
        protected static CitationService citation;

        protected ManualResetEvent doneEvent;

        protected string directoryPathToWriteFileTo;

        public ALL_ThreadPoolDownload(FrmCourts frm, ManualResetEvent doneEvent, string sDirectoryPathToWriteFileTo)
        {
            this.parentWindowForm = frm;
            this.doneEvent = doneEvent;
            this.directoryPathToWriteFileTo = sDirectoryPathToWriteFileTo;
        }

        protected void NaplnSeznamStringuOddelenychBR(List<string> pList, XmlNode pXnTd)
        {
            Regex rg = new Regex("<br\\s*/>");
            XmlNode xnWithoutBr = pXnTd.Clone();
            string s = xnWithoutBr.InnerXml.ToLower();
            xnWithoutBr.InnerXml = rg.Replace(s, "|");
            foreach (string sRow in xnWithoutBr.InnerText.Split('|'))
                if (!string.IsNullOrWhiteSpace(sRow))
                    pList.Add(sRow.Trim());
        }
    }
}
