using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UtilityHtml
{
    public class XmlParser
    {
        /// <summary>
        /// Třída, která obsahuje kombinaci nalezených správných tagů a nalezených špatných tagů
        /// 
        /// Nalezené správné tagy jsou reprezentovány dvojicí počátku a konce v rámci dokumentu
        /// Nalezené špatné tagy jsou reprezentovány počátkem špatného tagu
        /// </summary>
        public class ParseInfo
        {
            /// <summary>
            /// Třída, která obsahuje hranice tagů
            /// </summary>
            internal class TagInfo
            {
                private int start;

                public int Start
                {
                    get { return this.start; }
                }

                private int end;

                public int End
                {
                    get { return this.end; }
                }

                public TagInfo(int start, int end)
                {
                    this.start = start;
                    this.end = end;
                }
            }

            /// <summary>
            /// Jméno zkoumaného tagu - bez užitku
            /// </summary>
            private string tagName;

            /// <summary>
            /// Seznam nalezených správných tagů
            /// </summary>
            private List<TagInfo> parsedTags;

            /// <summary>
            /// Přidej do seznamu nalezených správných tagů
            /// </summary>
            /// <param name="start">počátek tagu</param>
            /// <param name="end">konec tagu v rámci předaného dokumentu</param>
            public void AddParsedTag(int start, int end)
            {
                this.parsedTags.Add(new TagInfo(start, end));
            }

            /// <summary>
            /// Vrátí seznam nalezených tagů
            /// </summary>
            internal List<TagInfo> ParsedTags
            {
                get { return this.parsedTags; }
            }

            /// <summary>
            /// Seznam nalezených špatných tagů
            /// </summary>
            private List<int> errorTagsIdxs;

            /// <summary>
            /// Přidá do seznamu nalezených špatných tagů
            /// </summary>
            /// <param name="idxs">index špatného tagu</param>
            public void AddErrorTagIds(params int[] idxs)
            {
                this.errorTagsIdxs.AddRange(idxs);
            }

            /// <summary>
            /// Vrátí seznam nalezených špatných tagů (respektive indexů v rámci dokumentu)
            /// </summary>
            public List<int> ErrorTagsIdxs
            {
                get { return new List<int>(this.errorTagsIdxs); }
            }


            public ParseInfo(string tagName)
            {
                this.errorTagsIdxs = new List<int>();
                this.parsedTags = new List<TagInfo>();

                this.tagName = tagName;
            }

            /// <summary>
            /// Odstraní všechny množiny, které jsou nadmnožinaMI JINÝCH MNOŽIN :)
            /// </summary>
            /// <param name="ti"></param>
            public void EliminateSuperSets()
            {
                List<int> idxsToDelete = new List<int>();

                for (int i = 0; i < parsedTags.Count; ++i)
                {
                    for (int j = 0; j < parsedTags.Count; ++j)
                    {
                        if (i == j) continue;
                        // je to nadmnožina?
                        if (parsedTags[i].Start < parsedTags[j].Start && parsedTags[i].End > parsedTags[j].End)
                        {
                            idxsToDelete.Add(i);
                            break;
                        }
                    }
                }

                for (int k = idxsToDelete.Count - 1; k >= 0; --k)
                {
                    parsedTags.RemoveAt(idxsToDelete[k]);
                }
            }
        }


        /// <summary>
        /// Parsuje předaný dokument. Hledá tag daného jména
        /// </summary>
        /// <param name="tagName">Tag hledaný v dokumentu</param>
        /// <returns>Třída obsahující jak indexy správných, tak i špatných tagů</returns>
        public ParseInfo ParseTag(string tagName)
        {
            Stack<int> idxsTableStart = new Stack<int>();
            int lastIdxTableStart = 0;
            int idxTableStart;
            while ((idxTableStart = html.IndexOf(String.Format("<{0}", tagName), lastIdxTableStart)) != -1)
            {
                idxsTableStart.Push(idxTableStart);
                lastIdxTableStart = idxTableStart + 1;
            }

            List<int> idxsTableEnd = new List<int>();
            int lastIdxTableEnd = 0;
            int idxTableEnd;
            while ((idxTableEnd = html.IndexOf(String.Format("</{0}>", tagName), lastIdxTableEnd)) != -1)
            {
                idxsTableEnd.Add(idxTableEnd);
                lastIdxTableEnd = idxTableEnd + 1;
            }

            ParseInfo result = new ParseInfo(tagName);
            // ziskam dvojice start; end
            while (idxsTableStart.Count != 0)
            {
                int zkoumanyStart = idxsTableStart.Pop();
                // hledam nejblizsi end!
                int prislusnyEnd = -1;
                foreach (int zkoumanyEnd in idxsTableEnd)
                {
                    if (zkoumanyEnd > zkoumanyStart)
                    {
                        // mam ho!
                        prislusnyEnd = zkoumanyEnd;
                        break;
                    }
                }

                if (prislusnyEnd == -1)
                {
                    // nenašel jsem k otvíráku zavírák!
                    result.AddErrorTagIds(zkoumanyStart);
                }
                else
                {
                    // našel jsem dvojičku
                    // vyhodim ho, protoze uz ho nemuzu znovu pouzit...
                    idxsTableEnd.Remove(prislusnyEnd);
                    // vložím do výsledku - odkaz dám na POSLEDNI prvek
                    result.AddParsedTag(zkoumanyStart, prislusnyEnd + 3 + tagName.Length);
                 }
            }

            // co se nepodařilo spárovat z konce, to dám jako error
            result.AddErrorTagIds(idxsTableEnd.ToArray());

            if (this.eliminateSuperSets)
            {
                result.EliminateSuperSets();
            }

            return result;
        }

        /// <summary>
        /// Parsuje předaný dokument. Hledá tag daného jména
        /// </summary>
        /// <param name="tag">Tag hledaný v dokumentu</param>
        /// <param name="fixHtmlErrors">Příznak, zda-li se mají opravit chyby předaného dokumentu v rámci tohoto tagu</param>
        /// <returns>textový seznam tagů (přímo obsah)</returns>
        public List<string> ParseTag(string tag, bool fixHtmlErrors)
        {
            ParseInfo tags = ParseTag(tag);

            if (fixHtmlErrors)
            {
                this.html = CorrectXml(this.html, tags.ErrorTagsIdxs);
            }

            List<string> result = new List<string>(tags.ParsedTags.Count);
            foreach (ParseInfo.TagInfo ti in tags.ParsedTags)
            {
                result.Add(html.Substring(ti.Start, ti.End - ti.Start));
            }

            return result;
        }

        /// <summary>
        /// validní html dokument (který nemusí být validním xml dokumentem)
        /// </summary>
        private string html;

        public string Html
        {
            get { return this.html; }
        }

        /// <summary>
        /// Příznak, zda-li eliminovat nadřazené tagy stejného jména
        /// Př:
        /// <tag> -- tento tag bude odstraněn
        /// ...
        ///   <tag>
        ///   </tag>
        /// </tag>
        /// </summary>
        private bool eliminateSuperSets;

        public XmlParser(string html, bool eliminateSupersets)
        {
            this.html = html;
            this.eliminateSuperSets = eliminateSupersets;
        }

        /// <summary>
        /// Vezme staré xml a indexy špatných tagů
        /// Postupně lepí nové xml s tím, že vynechává špatné tagy (přeskočí je)
        /// </summary>
        /// <param name="xml">xml k úpravě</param>
        /// <param name="idxErrors">indexy špatných tagů (klidně i z různých tagů)</param>
        /// <returns>opravené xml (neobsahuje identifikované špatné tagy, ale řídí se pouze předaným seznamem...)</returns>
        static string CorrectXml(string xml, List<int> idxErrors)
        {
            StringBuilder sbResult = new StringBuilder(xml.Length);
            idxErrors.Sort();

            // postupně budu lepit výsledek...
            int idxStart = 0;
            foreach (int idxError in idxErrors)
            {
                sbResult.Append(xml.Substring(idxStart, idxError - idxStart));
                // první znak po uzavírací závorce
                idxStart = xml.IndexOf('>', idxError) + 1;
            }

            sbResult.Append(xml.Substring(idxStart));

            return sbResult.ToString();
        }
    }
}
