using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

using com.ibm.as400.access;
using java.util;
using java.text;
using java.sql;
using System.Threading;
using System.Data.SqlClient;

namespace OS400Svc
{
    public partial class Os400Svc : ServiceBase
    {

        // eventlog class
        public String Source = "OS400 monitor";
        public LogEventi ev = new LogEventi();

        public String connectionString= @"Server=@INSTANCE@;user=@user@;pwd=@PASSWORD@";
        public String SqlServerInstance = Properties.Settings.Default.SqlServerInstance;
        public String SqlServerUser = Properties.Settings.Default.SqlServerUser;
        public String SqlServerPwd = Properties.Settings.Default.SqlServerPwd;
        public Int32 TimeoutToken = 60 * 1000;


        OS400Check os400 = new OS400Check();// istanzio la classe con le funzioni di controllo

        public List<AS400Server> ls_AS400Server;
        public List<JOB> ls_jobs;
        public CancellationTokenSource ctsource_main = new CancellationTokenSource();// Create a cancellation token from CancellationTokenSource
        public AsynchronousServer socket = new AsynchronousServer();
        public Os400Svc()
        {
            
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            ev.WriteEventToMyLog(Source, "Avvio Servizio OS400 montior", EventLogEntryType.Information, 1);

            // connessione al DB per lettura dati configurazione
            
            connectionString = connectionString
                .Replace("@INSTANCE@", SqlServerInstance)
                .Replace("@user@", SqlServerUser)
                .Replace("@PASSWORD@", SqlServerPwd);
            ev.WriteEventToMyLog(Source, connectionString, EventLogEntryType.Information, 11);
            //////

            Task t_socket = Generazione_Task_Socket();

            Task t_main=  Generazione_Task_Main();

        }

        protected override void OnStop()
        {
        }


        #region FUNZIONI TASK
        public Task Generazione_Task_Socket()
        {
            ev.WriteEventToMyLog(Source, "Avvio Task Main", EventLogEntryType.Information, 2);

            return Task<int>.Factory.StartNew(() => socket.StartListening()); // Creo un task con il token di cancellazione
        }
        public Task Generazione_Task_Main()
        {
            ev.WriteEventToMyLog(Source, "Avvio Task Main", EventLogEntryType.Information, 2);

            var cToken = ctsource_main.Token;

            cToken.Register(() => cancelNotification_Main());// gestione cancellazione notifica

           
            return Task<int>.Factory.StartNew(() => mainTask(cToken), cToken); // Creo un task con il token di cancellazione
        }

        private void cancelNotification_Main()
        {
            ev.WriteEventToMyLog(Source, "Cancellazione Task Main", EventLogEntryType.Warning, 50);
        }


        public int mainTask(CancellationToken ct_main)
        {
            try
            {
                ls_AS400Server = read_AS400_Server();// carico la lista
            }
            catch (Exception ex)
            {

                ev.WriteEventToMyLog(Source, "errore: " + ex.Message, EventLogEntryType.Error, 88);
            }
            ev.WriteEventToMyLog(Source, "Numero server OS400: " + ls_AS400Server.Count.ToString(), EventLogEntryType.Information, 8);

            foreach (AS400Server s in ls_AS400Server)
            {
                AS400 server = Connessione_AS400(s);
                if (server != null)
                {
                    Persistence p = new Persistence { ServerName = s.ServerName, jobs = new List<JobDetails> { } };// persistenza dei job in errore
                    Task t = Task<int>.Factory.StartNew(() => Start_OS400(ct_main, p, server,s), ct_main);// lancio il controllo Os400 come task
                }

            }

            Generazione_Task_PING(); 

             
            return 0;
        }

        private AS400 Connessione_AS400(AS400Server s)
        {

            try
            { 
               
                    AS400 server = new AS400();
                    server.setSystemName(s.ServerName);
                    server.setUserId(s.userid); //utente con diritti *ALLOBJ
                    server.setPassword(s.password);
                    return server;
                 
            }
            catch(Exception ex)
            {
                return null;
            }
        }

        #endregion

        #region  TASK  START OS400

        private int Start_OS400(CancellationToken ct_main, Persistence p,AS400 srv,AS400Server s)
        {

            while (true)// loop infinito
            {
                
                Task t = Generazione_Task_JOBS(s, ls_jobs, p,srv);// questo ha un timeout
                t.Wait();

                Thread.Sleep(30 * 1000);

                if (ct_main.IsCancellationRequested) { break; }// se lancio una richiesta di cancellazione manuale esco dal while
            }
            return 0;
        }

        #endregion


        #region TASK JOBS
        public Task Generazione_Task_JOBS(AS400Server srv, List<JOB> ls_jobs,Persistence p,AS400 s)
        {
            ev.WriteEventToMyLog(Source + " - Taks Jobs", "Avvio Task Jobs per " + srv.ServerName, EventLogEntryType.Information, 4);

            try
            {
                ls_jobs = Read_Job_Monitored();// carico dalla tabella i job da monitorare tutti i giri 
            }
            catch (Exception ex)
            {

                ev.WriteEventToMyLog(Source + " - Taks Jobs", "Errore: " + ex.Message, EventLogEntryType.Error, 44);
            }
                  
           // srv.cTokenSource.Cancel();// per sicurezza lo cancello nel caso non fosse già cancellato
            srv.cTokenSource = new CancellationTokenSource(TimeoutToken);
            var cToken = srv.cTokenSource.Token;// quando non si vuole specificare il tipo per comodità si mette var
            cToken.Register(() => cancelNotification_JOBS());// gestione cancellazione notifica


            // creo il task del controllo jobs e gli passo i par di connessione più il token di eventuale cancellazione          
            Task taskJobs = Task<int>.Factory.StartNew(() => os400.JOBLIST(cToken, srv.ServerName, srv.userid, srv.password, ls_jobs,p,s), cToken);

            return taskJobs;

        }

        // Notify when task is cancelled
        private void cancelNotification_JOBS()
        {
            ev.WriteEventToMyLog(Source, "Cancellazione Task OS400 JOBS", EventLogEntryType.Warning, 12);
        }
        #endregion

        #region TASK PING
        
        public int Generazione_Task_PING()
        {
            try
            {
                ls_AS400Server = read_AS400_Server();// carico la lista
            }
            catch (Exception ex)
            {

                ev.WriteEventToMyLog(Source, "errore: " + ex.Message, EventLogEntryType.Error, 88);
            }

            ev.WriteEventToMyLog(Source, "Numero server OS400: " + ls_AS400Server.Count.ToString(), EventLogEntryType.Information, 8);

            foreach (AS400Server s in ls_AS400Server)// tutti i ping per ogni server
            {
                CancellationTokenSource ct = new CancellationTokenSource(600*1000);

                var cToken = ct.Token;// quando non si vuole specificare il tipo per comodità si mette var
                cToken.Register(() => cancelNotification_PING());// gestione cancellazione notifica

                Task t = Task<Boolean>.Factory.StartNew(() => Ping_OS400(cToken, s),cToken);// lancio il controllo Os400 come task

            }

            ev.WriteEventToMyLog(Source, "Cancellazione Task Main", EventLogEntryType.Warning, 51);
            return 0;

        }

        // Notify when task is cancelled
        private void cancelNotification_PING()
        {
            ev.WriteEventToMyLog(Source, "Cancellazione Task OS400 PING", EventLogEntryType.Warning, 889);
            //Generazione_Task_PING();
        }

        private Boolean Ping_OS400(CancellationToken ct,AS400Server srv)
        {
            PingCls ping = new PingCls();

            while (true)
            {

                #region LOGICA DEL PING
           
                if (ping.isAvailable(srv.ServerName))
                {
                    ev.WriteEventToMyLog(Source + " - PING", "ping a " + srv.ServerName + " OK!", EventLogEntryType.Warning, 888);
                    Boolean isStop = srv.cTokenSource.IsCancellationRequested;

                    if (isStop)
                    {
                        srv.cTokenSource.Cancel();// per sicurezza lo spengo e ne genero un altro altrimenti ne crea di multipli

                        var ct_main = ctsource_main.Token;
                        ct_main.Register(() => cancelNotification_Main());// gestione cancellazione notifica
                        AS400 server = Connessione_AS400(srv);
                        Persistence p = new Persistence { ServerName = srv.ServerName, jobs = new List<JobDetails> { } };// persistenza dei job in errore
                        Task t = Task<int>.Factory.StartNew(() => Start_OS400(ct_main, p, server,srv), ct_main);// lancio il controllo Os400 come task    
                    }
                }
                else
                {
                    ev.WriteEventToMyLog(Source +" - PING", "Ping per " + srv.ServerName + " Fallito!", EventLogEntryType.Error, 999);
                    srv.cTokenSource.Cancel();// spengo il monitor del server

                }
                #endregion

                Thread.Sleep(20 * 1000);// aspetto 10 secondi e pingo ancora

                if (ct.IsCancellationRequested) { break; }// se lancio una richiesta di cancellazione manuale esco dal while

            }

            return true;
        }

        #endregion
         

        #region LETTURA IMPOSTAZIONI DA DATABASE
        public List<JOB> Read_Job_Monitored()
        {
            ev.WriteEventToMyLog(Source + " - Table", "Lettura Jobs su tabella", EventLogEntryType.Information, 3);
            List<JOB> ret_list = new List<JOB> { };
            try
            {
                
               
                SqlConnection cn = new SqlConnection(connectionString);
                SqlCommand cmd = new SqlCommand();
                string query = "select JobName,JobUser from iseries_data.dbo.Job_Monitored where include=1";//solo enabled

                cmd.CommandText = query;
                cmd.Connection = cn;
                cn.Open();
                SqlDataReader rd = cmd.ExecuteReader();

                while (rd.Read())
                {

                    ret_list.Add(new JOB
                    {
                        JobName = rd.GetValue(0).ToString(),
                        JobUser = rd.GetValue(1).ToString()

                    });

                }

                cn.Close();

                ev.WriteEventToMyLog(Source + " - Filtro", "Job caricati: " + ret_list.Count.ToString(), EventLogEntryType.Information, 4);
            }
            catch (Exception ex)
            {

                ev.WriteEventToMyLog(Source + " - Filtro", "Errore: " + ex.Message, EventLogEntryType.Error, 99);
            }

            return ret_list;
        }

        private List<AS400Server> read_AS400_Server()
        {
            List<AS400Server> ret_list = new List<AS400Server> { };
            
             
            
            SqlConnection cn = new SqlConnection(connectionString);
            SqlCommand cmd = new SqlCommand();
            string query = "select servername,ipaddress,userid,password,enabled from iseries_data.dbo.iseries_list where enabled=1";//solo enabled

            cmd.CommandText = query;
            cmd.Connection = cn;
            cn.Open();
            SqlDataReader rd = cmd.ExecuteReader();

            while (rd.Read())
            {

                ret_list.Add(new AS400Server
                {

                    ServerName = rd.GetString(0),
                    Ipaddress = rd.GetString(1),
                    userid = rd.GetString(2),
                    password = rd.GetString(3),
                    cTokenSource = new CancellationTokenSource(TimeoutToken)// creo il proprio token cancellazione TimeSpan.FromSeconds(60)
                });

            }




            cn.Close();


            return ret_list;
        }
        #endregion
    }
}
