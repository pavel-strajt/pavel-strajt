//SKIP_CHECKING_FOR_DUPLICITIES on => ForeignIdIsAlreadyInDb & ReferenceNumberIsAlreadyinDb & IsAlreadyinDb will ALWAYS return false
//#define SKIP_CHECKING_FOR_DUPLICITIES

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data.SqlClient;
using System.Data;
using System.Threading;

namespace DataMiningCourts
{
    /// <summary>
    /// Class, that generates unique citation id (within the year).
	/// This class check, if the document is already part of Becks database or not as well
	/// 
	/// Multithread safe
    /// 
    /// There is an other possible solution by usign function anotations
    ///             [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.Synchronized)]
    /// </summary>
    internal class CitationService
    {
        /// <summary>
        /// Nic moc, ale hrabat do toho víc nemá smysl
        /// Příznak, zda-li má opravdu docházet ke kontrole duplicit
        /// </summary>
        public static bool DO_CHECK_DUPLICITIES = true;

        /// <summary>
        /// First number for a year
        /// AS WELL AS a Default number if there is no DB connection
        /// </summary>
        public static int DEFAULT_NUMBER = 1;

        /// <summary>
        /// Id of the document, that is already in the DBs
        /// </summary>
        public static int DUPLICATE_DOCUMENT_ID = -1;

        private bool addCheckedValues;

        /// <summary>
        /// Indication whether the checked values are added to the #datesReferenceNumbers or not
        /// </summary>
        public bool AddCheckedValues
        {
            get { return this.addCheckedValues; }
            set { this.addCheckedValues = value; }
        }


        /// <summary>
        /// {0} = Length({1})
        /// {1} = Court prefix
        /// {2} = Year
        /// </summary>
        private static string DB_QUERY_STUB = @"SELECT MAX(ArticleNumber) FROM HeaderSub WHERE Citation LIKE '{1}%/{2}'";

        /// <summary>
        /// Transform Court type to Citation prefix (e.g. NejvyssiSoud => NS , etc)
        /// </summary>
        /// <param name="pCourt"></param>
        /// <returns></returns>
        private static string CourtToCitationPrefix(FrmCourts.Courts pCourt)
        {
            switch (pCourt)
            {
                case FrmCourts.Courts.cNS:
                    return "NS ";

                case FrmCourts.Courts.cNSS:
                    return "NSS ";

                case FrmCourts.Courts.cSNI:
                    return "Výběr VKS ";

                case FrmCourts.Courts.cUOHS:
                    return "ÚOHS ";

                case FrmCourts.Courts.cUPV:
                    return "ÚPV ";

                case FrmCourts.Courts.cUS:
                    return "ÚS ";

                case FrmCourts.Courts.cESLP:
                    return "Výběr ESLP ";

                case FrmCourts.Courts.cINS:
                    return "Výběr INS ";

                case FrmCourts.Courts.cJV:
                    return "";

                case FrmCourts.Courts.cVS:
                    return "Výběr ";
                default:
                    return String.Empty;
            }
        }

        /// <summary>
		/// Transform Court type to Citation prefix (e.g. NejvyssiSoud => NS , etc)
		/// </summary>
		/// <param name="pCourt"></param>
		/// <returns></returns>
		private static string CourtToCitationSuffix(FrmCourts.Courts pCourt)
        {
            switch (pCourt)
            {
                case FrmCourts.Courts.cJV:
                    return "UsnV";

                default:
                    return String.Empty;
            }
        }

        /// <summary>
        /// Subset of court for this citation service class,
        /// this is needed because of testing documents external id, that DO NOT HAVE TO BE unique
        /// within all courts!
        /// </summary>
        private FrmCourts.Courts court;

        // date, list of reference numbers
        private Lazy<SortedDictionary<DateTime, List<string>>> datesReferenceNumbers;

        /// <summary>
        /// List of all Foreign ids for the Court
        /// </summary>
        private Lazy<HashSet<string>> foreignIds;

        /// <summary>
		/// List of all citattions for the Court
		/// </summary>
		private Lazy<HashSet<string>> citationIds;

        /// <summary>
        /// Year, Last free citation number (MAX+1)
		/// Each year has its own Semaphore, that ensures uniquity even if there are a multiple threads!
        /// </summary>
		private Dictionary<int, int> lastCitationOfTheYear;
        /// <summary>
        /// Each year has a different semaphore
        /// </summary>
        private Dictionary<int, SemaphoreSlim> semsOfTheYear;

        /// <summary>
        /// Returns a COPY of the Dictionary Year, Last free citation number
        /// </summary>
        public Dictionary<int, int> LastCitationOfTheYear
        {
            /* Copy */
            get { return new Dictionary<int, int>(this.lastCitationOfTheYear); }
        }

        /// <summary>
        /// Initialization of lists
        /// </summary>
        private CitationService()
        {
            this.datesReferenceNumbers = new Lazy<SortedDictionary<DateTime, List<string>>>(() => LoadDateToReferenceNumbers());
            this.foreignIds = new Lazy<HashSet<string>>(() => LoadForeignIds());
            this.citationIds = new Lazy<HashSet<string>>(() => LoadCitationIds());

            // Add values, that has been checked into this instances internal lists
            this.addCheckedValues = true;
            this.semsOfTheYear = new Dictionary<int, SemaphoreSlim>();
        }

        /// <summary>
        /// Inicialize CitacionService class for a given court
        /// </summary>
        /// <param name="pCourt">Data from this court will be loaded from the DB</param>
        public CitationService(FrmCourts.Courts pCourt) : this()
        {
            this.court = pCourt;
            this.lastCitationOfTheYear = new Dictionary<int, int>();
        }

        /// <summary>
        /// Inicialize CitationService class for a given court and the 
		/// given list that contains first unused value for a year
        /// </summary>
		/// <param name="pCourt">Data from this court will be loaded from the DB</param>
		/// <param name="pOldValues">List that contains first (max) unused value for a year</param>
		public CitationService(FrmCourts.Courts pCourt, Dictionary<int, int> pOldValues) : this()
        {
            this.court = pCourt;
            this.lastCitationOfTheYear = new Dictionary<int, int>(pOldValues);
        }

        /// <summary>
        /// Loads data Reference numbers with dates from DB
        /// </summary>
        /// <returns></returns>
        private SortedDictionary<DateTime, List<string>> LoadDateToReferenceNumbers()
        {
            SortedDictionary<DateTime, List<string>> result = new SortedDictionary<DateTime, List<string>>();
#if !DUMMY_DB
            using (SqlConnection connection = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["LexData"].ConnectionString))
            {
                connection.Open();

                /* Load all Dates, Reference Numbers (truncated, lowered) and External ids for a given Court! */
                SqlCommand cmd = connection.CreateCommand();
                cmd.CommandText = @"SELECT HeaderJ.DateApproval,HeaderJ.ReferenceNumberNorm FROM HeaderJ";

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        /* Data are saved as
                         * 0: DateTime, key to datesReferenceNumbers
                         * 1: String: Reference numbers without Whitespaces diacritics, convert toLower afterwards
                         */
                        DateTime dateApproval = reader.GetDateTime(0);
                        string referenceNumberWithoutWhitespacesDiacritisism = reader.GetString(1).ToLower();

                        /* Reference numbers */

                        /*
                         * If ther is NO list for the date (dateApprovalUni), create it!
                         */
                        if (!result.ContainsKey(dateApproval))
                        {
                            result.Add(dateApproval, new List<string>());
                        }

                        /* Get the List of reference numbers for the date (dateApprovalUni)
                         * List has to exist!
                         */
                        List<string> listOfReferenceNumbersForDate = null;
                        result.TryGetValue(dateApproval, out listOfReferenceNumbersForDate);

                        /* 
                         * Add Reference number to that list.
                         * Do not care about Duplicities!
                         */
                        listOfReferenceNumbersForDate.Add(referenceNumberWithoutWhitespacesDiacritisism);
                    }
                }
            }
#endif

            return result;
        }

        /// <summary>
        /// Loads the foreign ids from DB
        /// </summary>
        /// <returns></returns>
        private HashSet<string> LoadForeignIds()
        {
            HashSet<string> result = new HashSet<string>();
#if !DUMMY_DB
            using (SqlConnection connection = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["LexData"].ConnectionString))
            {
                connection.Open();

                /* Load all Dates, Reference Numbers (truncated, lowered) and External ids for a given Court! */
                SqlCommand cmd = connection.CreateCommand();
                cmd.CommandText = String.Format(
                                    @"SELECT HeaderSub.IDExternal 
									FROM HeaderSub
									WHERE HeaderSub.Citation LIKE '{0}%'", CourtToCitationPrefix(this.court));

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        /* Data are saved as
                         * 0: String: Foreign Id AS IS to list of foreignIds
                         */

                        if (!reader.IsDBNull(0))
                        {
                            string foreignId = reader.GetString(0);
                            /* ForeignKey; DONE*/
                            result.Add(foreignId);
                        }
                    }
                }
            }

#endif
            return result;
        }

        /// <summary>
        /// Loads the foreign ids from DB
        /// </summary>
        /// <returns></returns>
        private HashSet<string> LoadCitationIds()
        {
            HashSet<string> result = new HashSet<string>();
#if !DUMMY_DB
            using (SqlConnection connection = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["LexData"].ConnectionString))
            {
                connection.Open();

                var cmd = connection.CreateCommand();
                cmd.CommandText = String.Format(
                                    @"SELECT Citation 
									FROM Dokument
									WHERE Citation LIKE '%{0}'", CourtToCitationSuffix(this.court));

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (!reader.IsDBNull(0))
                        {
                            string foreignId = reader.GetString(0);
                            result.Add(foreignId);
                        }
                    }
                }
            }

#endif
            return result;
        }

        /// <summary>
        /// Fetch the first(max) free citation number from the DB for a given year
        /// </summary>
        /// <param name="pYear"></param>
        private void GetNumberFromDB(int pYear)
        {
            int iResult = CitationService.DEFAULT_NUMBER;
#if !DUMMY_DB
            using (SqlConnection conn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["LexData"].ConnectionString))
            {
                conn.Open();

                string sCitationPrefix = CourtToCitationPrefix(this.court);
                string sQuery = String.Format(DB_QUERY_STUB, sCitationPrefix.Length, sCitationPrefix, pYear);
                SqlCommand cmd = new SqlCommand(sQuery, conn);

                object oResult = cmd.ExecuteScalar();
                string sValue = oResult.ToString();

                if (!String.IsNullOrEmpty(sValue))
                {
                    iResult = Int32.Parse(sValue) + 1;
                }
            }
#endif

            /* Add Citation for a year in any case */
            this.AddCitationForYear(pYear, iResult, true);
        }


        /// <summary>
		/// Function, that adds an order number for a given year
		/// if there is no number for a given year OR the pDoOverride variable is set to true
        /// </summary>
        /// <param name="pYear"></param>
        /// <param name="pCitation">Order number, that will be used on the next call of the GetNextCitation(year) function (if pDoOverride is true OR there is no number for the year)</param>
        /// <param name="pDoOverride">Indicates wheter save the number even if there is already a order number for a given year</param>
        /// <returns>True, if the number was successfully inserted</returns>
        public bool AddCitationForYear(int pYear, int pCitation, bool pDoOverride)
        {
            if (this.lastCitationOfTheYear.ContainsKey(pYear))
            {
                /* Order number already exists for a given year */
                if (!pDoOverride)
                {
                    /* Do not override => do not insert => return false */
                    return false;
                }

                /* Do override */
                this.lastCitationOfTheYear[pYear] = pCitation;
            }
            else
            {
                /* Insert new */
                this.lastCitationOfTheYear.Add(pYear, pCitation);
            }

            /* Overrided or inserted */
            return true;
        }

        /// <summary>
        /// Funkce, která slouží k pevnému přidání pořadového čísla pro daný rok.
        /// Toto pořadové číslo může být nastavenou pouze pokud zatím neexistuje ŽÁDNÉ pořadové číslo pro daný rok
        /// </summary>
        /// <param name="pYear">Rok</param>
        /// <param name="pCitation">Číslo citace, které má být vloženo pro daný rok. Toto číslo bude první vyzvednuté číslo pro daný rok</param>
        /// <returns>True, pokud bylo číslo úspěšně vloženo</returns>
        public bool AddCitationForYear(int pYear, int pCitation)
        {
            return AddCitationForYear(pYear, pCitation, false);
        }

        /// <summary>
        /// Check, if there is a record in the DB with the given foreignId in the DB
        /// EG: It only connects for the first time. Values are stored internally
        /// </summary>
        /// <param name="pForeignId">Foreign id to checked</param>
        /// <returns>True, if there is already a record with the foreignID otherwise false</returns>
        public bool ForeignIdIsAlreadyInDb(string pForeignId)
        {
            bool result = false;
            /* Check Foreign Ids */
            if (DO_CHECK_DUPLICITIES && !String.IsNullOrEmpty(pForeignId))
            {
                result |= this.foreignIds.Value.Contains(pForeignId);
            }
            return result;
        }

        /// <summary>
        /// Check, If there is a record in the DB with the combination of the given date and reference number
        /// </summary>
        /// <param name="pDate"></param>
        /// <param name="pReferenceNumber"></param>
        /// <returns>True, if there is already a record with the combination of the given date and reference number otherwise false</returns>
        public bool ReferenceNumberIsAlreadyinDb(DateTime pDate, string pReferenceNumber)
        {
            bool bResult = false;
            /* Check Reference Numbers */
            List<string> listOfReferenceNumbersForDate = null;
            if (DO_CHECK_DUPLICITIES && this.datesReferenceNumbers.Value.TryGetValue(pDate, out listOfReferenceNumbersForDate))
            {
                string referenceNumbersToLowerNoWhitespaces = pReferenceNumber.ToLower();
                referenceNumbersToLowerNoWhitespaces = UtilityBeck.Utility.GetReferenceNumberNorm(referenceNumbersToLowerNoWhitespaces, out string sNormValue2);
                /* List search */
                bResult |= listOfReferenceNumbersForDate.Contains(referenceNumbersToLowerNoWhitespaces);
            }
            return bResult;
        }

        public bool CitationIsAlreadyinDb(string citation)
        {
            bool result = false;
            /* Check Foreign Ids */
            if (DO_CHECK_DUPLICITIES && !String.IsNullOrEmpty(citation))
            {
                result |= this.citationIds.Value.Contains(citation);
            }
            return result;
        }

        /// <summary>
        /// Check, If the document with given foreignId or the combination of the Reference number and Date is in the DB
        /// EG: It only connects for the first time. Values are stored internally
        /// </summary>
        /// <param name="pDate"></param>
        /// <param name="pReferenceNumber"></param>
        /// <param name="pForeignId"></param>
        /// <returns></returns>
        public bool IsAlreadyinDb(DateTime pDate, string pReferenceNumber, string pForeignId)
        {
            return ReferenceNumberIsAlreadyinDb(pDate, pReferenceNumber) || ForeignIdIsAlreadyInDb(pForeignId);
        }

        /// <summary>
        /// Function for generating unique citation number for a given year.
		/// Threadsafe function.
		/// Function has to be commited (via CommitCitationForAYear) or reverted (via RevertCitationForAYear) to avoid deathlocks!
		/// 
		/// EQ: This function may connect to the DB
        /// </summary>
        /// <param name="year">Funcion is loooking for a unique number for THIS year</param>
        /// <returns>Unique citation number for a given year</returns>
        public int GetNextCitation(int year)
        {
            lock (typeof(CitationService))
            {
                if (!this.lastCitationOfTheYear.ContainsKey(year))
                {
                    /* Unique number have to be filled first - fetched from the DB */
                    GetNumberFromDB(year);
                }

                /* Lock Sem for a year! */
                if (!this.semsOfTheYear.ContainsKey(year))
                {
                    this.semsOfTheYear.Add(year, new SemaphoreSlim(1, 1));
                }

                // Protect actual value! 
                semsOfTheYear[year].Wait();
                // Contents of method
                return this.lastCitationOfTheYear[year];
            }
        }

        /// <summary>
        /// Commit the using of the last generated citation number for the given year
        /// </summary>
        /// <param name="year"></param>
        public void CommitCitationForAYear(int year)
        {
            /* Increase a number for a year! */
            ++this.lastCitationOfTheYear[year];
            /* release the lock*/
            semsOfTheYear[year].Release();
        }

        /// <summary>
        /// Revert the using of the last generated citation number for the given year
        /// </summary>
        /// <param name="year"></param>
        public void RevertCitationForAYear(int year)
        {
            /* DO not Increase a number & Release the lock*/
            semsOfTheYear[year].Release();
        }

        public string GetCitationPrefix(FrmCourts.Courts court)
        {
            return CourtToCitationPrefix(court);
        }
    }
}
