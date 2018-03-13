using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;



namespace OS400Svc
{
    public class ComandiSocket
    {
        public LogEventi ev = new LogEventi();
        public String Source = "OS400 - Comandi Socket";
        public void Execute(String Command)
        {



            ev.WriteEventToMyLog(Source, "Comando da parsificare " + Command, EventLogEntryType.Information, 55);


        }
    }
}
