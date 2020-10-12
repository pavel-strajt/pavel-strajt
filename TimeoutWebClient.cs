using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;

namespace DataMiningCourts
{
    public class TimeoutWebClient : WebClient
    {
        private static int DEFAULT_TIMEOUT = 10000;

        private int _timeOut;

        public TimeoutWebClient() :base() 
        {
            this._timeOut = DEFAULT_TIMEOUT;
        }

        public TimeoutWebClient(int timeout)
            : base()
        {
            this._timeOut = timeout;
        }

        public int TimeOut { get { return _timeOut; } set { _timeOut = value; } }

        protected override WebRequest GetWebRequest(Uri address) 
        { 
            WebRequest webRequest = base.GetWebRequest(address); 
            webRequest.Timeout = _timeOut; 
            return webRequest; 
        }
    }

}
