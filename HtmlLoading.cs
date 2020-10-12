using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.IO;

namespace UtilityHtml
{
    public static class HtmlLoading
    {

        /// <summary>
        /// Load content of the url address
        /// </summary>
        /// <param name="pUrl">url address to load</param>
        /// <returns></returns>
        public static string GetWebContent(string pUrl)
        {
            WebRequest request = WebRequest.Create(pUrl);
            HttpWebResponse response = null;
            try
            {
                response = (HttpWebResponse)request.GetResponse();
            }
            catch (Exception)
            {
                return null;
            }

            Stream dataStream = response.GetResponseStream();
            StreamReader reader = new StreamReader(dataStream);
            string responseFromServer = reader.ReadToEnd();

            return responseFromServer;
        }


        /// <summary>
        /// Check if in the web address exists all required strings
        /// </summary>
        /// <param name="pUrl">url address to check</param>
        /// <param name="pPartsOfWeb">Required strings. Output from this functions are nonfounded strings</param>
        /// <returns>number of nonfounded strings=0?</returns>
        public static bool WebValidate(string pUrl, List<string> pPartsOfWeb)
        {
            string webContent = GetWebContent(pUrl);

            List<string> foundParts = new List<string>(pPartsOfWeb);
            foreach (string part in pPartsOfWeb)
            {
                if (webContent.Contains(part))
                {
                    foundParts.Add(part);
                }
            }
            
            foreach (string foundPart in foundParts)
            {
                pPartsOfWeb.Remove(foundPart);
            }

            return pPartsOfWeb.Count == 0;
        }
    }
}
