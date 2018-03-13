using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace ConsolePing
{
    class Program
    {
        static void Main(string[] args)
        {
            PingCls ping = new PingCls();

            while (true)
            {

                #region LOGICA DEL PING

                if (ping.isAvailable("BLADE"))
                {

                    Console.WriteLine("BLADE OK");

                }
                else
                {

                    Console.WriteLine("BLADE NO");

                }
                #endregion

                Thread.Sleep(2 * 1000);// aspetto 10 secondi e pingo ancora

            }
        }





    }
}
