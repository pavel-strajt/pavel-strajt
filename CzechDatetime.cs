using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DataMiningCourts
{
    public static class CzechDatetime
    {
        /// <summary>
        /// Declined month names, that can be used to get a order number of month by using index-of function
        /// </summary>
        private static readonly List<string> DECLINED_MONTH_NAMES = new List<string>( 
            new string[] { "_0_", "ledna", "února", "března", "dubna", "května", "června", "července", "srpna", "září", "října", "listopadu", "prosince" });

        /// <summary>
        /// Regural expression representing declined czech date
		/// month by number OR expression word
        /// </summary>
        private static readonly string REG_CZECH_DECLINED_DATE = @"(\d{1,2})\.\s*(ledna|února|března|dubna|května|června|července|srpna|září|října|listopadu|prosince|\d{1,2})\s*\.?\s*(\d{4})";

        /// <summary>
        /// Instance of regural expression class that is matching on strings, that are declined czech dates
        /// </summary>
        private static readonly Regex regCzechDeclinedDate = new Regex(REG_CZECH_DECLINED_DATE);

        /// <summary>
        /// Converts string representation of czech declined date to DateTime representation
        /// </summary>
        /// <param name="pDeclinedDate"></param>
        /// <param name="pResult"></param>
        /// <returns>true, if conversion was sucessfull, otherwise false</returns>
        public static bool DeclinedCzechDateToDateTime(string pDeclinedDate, ref DateTime pResult)
        {
            bool wasParsed = false;
            Match matchRegCzechDeclinedDate = regCzechDeclinedDate.Match(pDeclinedDate.ToLower());
            if (matchRegCzechDeclinedDate.Success)
            {
                int day = Int32.Parse(matchRegCzechDeclinedDate.Groups[1].Value);
				int month;
				/* Try to parse as number */
				if (!Int32.TryParse(matchRegCzechDeclinedDate.Groups[2].Value, out month))
				{
					/* Try to parse as expression */
					month = DECLINED_MONTH_NAMES.IndexOf(matchRegCzechDeclinedDate.Groups[2].Value);
				}
                int year = Int32.Parse(matchRegCzechDeclinedDate.Groups[3].Value);
                if (month > 0)
                {
					try
					{
						pResult = new DateTime(year, month, day);
						wasParsed = true;
					}
					catch (ArgumentOutOfRangeException) { }
                }
            }

            return wasParsed;
        }
    }
}
