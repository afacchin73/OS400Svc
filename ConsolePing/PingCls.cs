using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace ConsolePing
{
    
    class PingCls
    {
      
        public Boolean isAvailable(String Server)
        {
            Ping pingSender = new Ping();
            PingOptions options = new PingOptions();

            // Use the default Ttl value which is 128,
            // but change the fragmentation behavior.
            options.DontFragment = true;

            // Create a buffer of 32 bytes of data to be transmitted.
            string data = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            byte[] buffer = Encoding.ASCII.GetBytes(data);
            int timeout = 5;
            PingReply reply = pingSender.Send(Server, timeout, buffer, options);
            
            if (reply.Status == IPStatus.Success)
            {
                return true;
            }
            return false;
        }
    }
}
