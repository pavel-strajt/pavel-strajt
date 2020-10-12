using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BeckLinking;
using System.Xml;
using System.Data.SqlClient;

namespace DataMiningCourts
{
    public class LinkingHelper
    {
        public static bool AddBaseLaws(XmlDocument document, string lawsLine, XmlNode xnBaseLawsElement)
        {
            bool success = false;
            DocumentRelation[] resultRelation;
            using (SqlConnection conn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["LexData"].ConnectionString))
            {
                conn.Open();
                resultRelation = ParseRelation.ProcessText(lawsLine, conn);
                conn.Close();
                conn.Dispose();
            }
            // The result
            foreach (DocumentRelation drel in resultRelation)
            {
                XmlNode law = document.CreateElement("item");

                if (!string.IsNullOrEmpty(drel.LinkText))
                {
                    XmlNode link = document.CreateElement("link");
                    link.InnerText = drel.LinkText;
                    link.Attributes.Append(document.CreateAttribute("href"));
                    link.Attributes["href"].Value = drel.href;
                    law.AppendChild(link);
                }

                if (!string.IsNullOrEmpty(drel.Note))
                {
                    law.AppendChild(document.CreateTextNode(drel.Note));
                }
                xnBaseLawsElement.AppendChild(law);
                success = true;
            }
            return success;
        }
    }
}
