using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;
using System.Data.SqlClient;

namespace DataMiningCourts
{
    public class CiselnikDB
    {
        
        SortedDictionary<string,int> dic = new SortedDictionary<string,int>();
        SortedDictionary<string, int> dicLWR = new SortedDictionary<string, int>();
        
        public CiselnikDB(string Name, int IdtSector, string ConStr)
        {

			string sQuery = string.Format("SELECT T{0}name,IDT{0} from T{0} WHERE IDTSector= {1}", Name, IdtSector);
            using (SqlConnection con = new SqlConnection(ConStr))
            {
                con.Open();
                SqlDataAdapter da = new SqlDataAdapter(sQuery, con);
                DataTable tabulka = new DataTable();
                da.Fill(tabulka);
                foreach (DataRow dr in tabulka.Rows)
                {
                    dic.Add((string)dr[0], (int)dr[1]);
                    dicLWR.Add(((string)dr[0]).ToLower(), (int)dr[1]);
                }
            }
        }

        public bool HasValue(string text)
        {
            return dic.ContainsKey(text);
        }
        
        public bool HasValueIgnoreCase(string text)
        {
            return dicLWR.ContainsKey(text.ToLower());
        }


    }
}
