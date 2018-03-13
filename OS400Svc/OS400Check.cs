using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using com.ibm.as400.access;
using java.util;
using java.text;
using java.sql;
using System.Threading;
using System.Diagnostics;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using System.Data;
using System.Data.SqlClient;
using System.ComponentModel;
using System.Configuration;

namespace OS400Svc
{
    class OS400Check
    {
        LogEventi ev = new LogEventi();
        String source = "OS400-";
        List<Persistence> ls_pers = new List<Persistence> { };
        String SqlServerInstance = Properties.Settings.Default.SqlServerInstance;
        String SqlServerUser = Properties.Settings.Default.SqlServerUser;
        String SqlServerPwd = Properties.Settings.Default.SqlServerPwd;

        #region PERFORMANCE
        // QUESTA è LA FUNZIONE PRINCIPALE DOVE MI CONNETTO ALL'ISERIES E FINCHE' NON HO UNA RICHIESTA DI CANCELLAZIONE 
        // CONTINUO A MONITORARE OGNI 10 SECONDI COME IN THREAD.SLEEP
        public int GetServerPerformance(CancellationToken ct, String ServerName, String User, String Pwd)
        {
            AS400 server = null;
            if (server == null)
            {
                server = new AS400();
                server.setSystemName(ServerName);
                server.setUserId(User); //utente con diritti *ALLOBJ
                server.setPassword(Pwd);

            }

            while (true)// loop infinito
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }


                Performance(server, ServerName);// LETTURA PERFORMANCE


                Thread.Sleep(10000);// DETERMINO IL REFRESH DEL CONTROLLO

                if (ct.IsCancellationRequested)
                {
                    break;
                }

            }

            return 0;
        }


        public void Performance(AS400 server, String ServerName)
        {

            ev.WriteEventToMyLog("iSeriesMon", "GET PERFORMANCE per " + ServerName, EventLogEntryType.Information, 11);

            Performance_iSeries perf = read_iSeries_perf(server, ServerName);// LEGGE LE PERF DA ISERIES

            ev.WriteEventToMyLog("iSeriesMon", "Serializza Classe per " + server.getSystemName() + ": " + perf.aspValue, EventLogEntryType.Information, 11);

            String xmlResult = String.Empty;

            xmlResult = Serializza_perf(perf, xmlResult);
            ev.WriteEventToMyLog("iSeriesMon", xmlResult, EventLogEntryType.Information, 1);

            insert_performance(xmlResult, ServerName);

            //SCRIVO IL RISULTATO SU EVENTLOG

            ////////////////////
        }

        private void insert_performance(string xmlResult, String ServerName)
        {
            String connectionString = @"Server=@INSTANCE@;user=@user@;pwd=@PASSWORD@";
            connectionString = connectionString
                .Replace("@INSTANCE@",SqlServerInstance)
                .Replace("@user@",SqlServerUser)
                .Replace("@PASSWORD@", SqlServerPwd);


            SqlConnection cn = new SqlConnection(connectionString);
            SqlCommand cmd = new SqlCommand();
            String query = "insert into iseries_" + ServerName + ".dbo.iSeries_Performance values(getdate(),@xmlResult)";
            cmd.Parameters.Add("@xmlResult", SqlDbType.Xml).Value = xmlResult.Replace("utf-8", "utf-16");// altrimenti non scrive l'xml



            try
            {
                cmd.Connection = cn;
                cmd.CommandText = query;

                if (cn.State == ConnectionState.Closed)
                    cn.Open();

                cmd.ExecuteNonQuery();

                cn.Close();
            }
            catch (Exception ex)
            {
                ev.WriteEventToMyLog("iSeriesMon - InsertPerf", ex.Message, EventLogEntryType.Error, 999);
                throw;
            }

        }

        private string Serializza_perf(Performance_iSeries perf, string xmlResult)
        {
            // SERIALIZZAZIONE DELLA CLASSE PER AVERE UN XML CON IL RISULTATO
            try
            {
                using (var stream = new MemoryStream())
                {
                    using (var writer = XmlWriter.Create(stream))
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof(Performance_iSeries));
                        serializer.Serialize(writer, perf);
                        //new XmlSerializer(perf.GetType()).Serialize(writer, perf);
                        xmlResult = Encoding.UTF8.GetString(stream.ToArray());

                    }
                }
            }
            catch (Exception ex)
            {

                ev.WriteEventToMyLog("iSeriesMon", ex.Message + "\n" + ex.InnerException, EventLogEntryType.Error, 111);
            }
            ///////////////////////////////// FINE ////////////////////////////////////////////
            return xmlResult;
        }

        public Performance_iSeries read_iSeries_perf(AS400 server, String ServerName)
        {
            ////////// GET PERF

            try
            {
                SystemStatus st = new SystemStatus();
                st.setSystem(server);
                String tstamp = DateTime.Now.ToString("MM'/'dd'/'yyyy H':'mm':'ss");// con apici altrimenti default


                tstamp = tstamp.Replace('.', ':').Replace('-', '/');// mi assicuro che il formato sia corretto
                Performance_iSeries perf = new Performance_iSeries
                {
                    ServerName = ServerName,
                    Timewritten = tstamp,
                    aspValue = st.getPercentSystemASPUsed(),
                    cpuValue = st.getPercentProcessingUnitUsed(),
                    PercentPermanentAddresses = st.getPercentPermanentAddresses(),
                    PercentTemporaryAddresses = st.getPercentTemporaryAddresses(),
                    CurrentProcessingCapacity = st.getCurrentProcessingCapacity(),
                    CurrentUnprotectedStorageUsed = st.getCurrentUnprotectedStorageUsed(),
                    PercentCurrentInteractivePerformance = st.getPercentCurrentInteractivePerformance(),
                    PercentDBCapability = st.getPercentDBCapability(),
                    PercentPermanent256MBSegmentsUsed = st.getPercentPermanent256MBSegmentsUsed(),
                    PercentSharedProcessorPoolUsed = st.getPercentSharedProcessorPoolUsed(),
                    PercentTemporary256MBSegmentsUsed = st.getPercentTemporary256MBSegmentsUsed(),
                    PercentTemporary4GBSegmentsUsed = st.getPercentTemporary4GBSegmentsUsed(),
                    PercentUncappedCPUCapacityUsed = st.getPercentUncappedCPUCapacityUsed(),
                    PercentProcessingUnitUsed = st.getPercentProcessingUnitUsed(),
                    TotalAuxiliaryStorage = st.getTotalAuxiliaryStorage(),
                    MainStorageSize = st.getMainStorageSize(),
                    MaximumJobsInSystem = st.getMaximumJobsInSystem(),
                    MaximumUnprotectedStorageUsed = st.getMaximumUnprotectedStorageUsed(),
                    UsersCurrentSignedOn = st.getUsersCurrentSignedOn(),
                    UsersSignedOffWithPrinterOutputWaitingToPrint = st.getUsersSignedOffWithPrinterOutputWaitingToPrint(),
                    UsersSuspendedByGroupJobs = st.getUsersSuspendedByGroupJobs(),
                    UsersSuspendedBySystemRequest = st.getUsersSuspendedBySystemRequest(),
                    UsersTemporarilySignedOff = st.getUsersTemporarilySignedOff(),
                    ActiveJobsInSystem = st.getActiveJobsInSystem(),
                    ActiveThreadsInSystem = st.getActiveThreadsInSystem(),
                    BatchJobsEndedWithPrinterOutputWaitingToPrint = st.getBatchJobsEndedWithPrinterOutputWaitingToPrint(),
                    BatchJobsEnding = st.getBatchJobsEnding(),
                    BatchJobsHeldOnJobQueue = st.getBatchJobsHeldOnJobQueue(),
                    BatchJobsHeldWhileRunning = st.getBatchJobsHeldWhileRunning(),
                    BatchJobsOnAHeldJobQueue = st.getBatchJobsOnAHeldJobQueue(),
                    BatchJobsOnUnassignedJobQueue = st.getBatchJobsOnUnassignedJobQueue(),
                    BatchJobsRunning = st.getBatchJobsRunning(),
                    BatchJobsWaitingForMessage = st.getBatchJobsWaitingForMessage(),
                    NumberOfPartitions = st.getNumberOfPartitions(),
                    NumberOfProcessors = st.getNumberOfProcessors(),
                    PartitionIdentifier = st.getPartitionIdentifier(),
                    BatchJobsWaitingToRunOrAlreadyScheduled = st.getBatchJobsWaitingToRunOrAlreadyScheduled(),
                    JobsInSystem = st.getJobsInSystem(),
                    PoolsNumber = st.getPoolsNumber()
                };




                // serializzo in stringwriter così da poter scrivere su DB
                //Insert_to_Table(LPAR.LPAR, perf);

                return perf;
            }
            catch (Exception ex)
            {
                ev.WriteEventToMyLog("iSeriesMon", ex.Message, EventLogEntryType.Error, 11);

                String tstamp = DateTime.Now.ToString("MM'/'dd'/'yyyy H':'mm':'ss");// con apici altrimenti default
                tstamp = tstamp.Replace('.', ':').Replace('-', '/');// mi assicuro che il formato sia corretto

                Performance_iSeries perf = new Performance_iSeries
                {
                    Timewritten = tstamp,
                    aspValue = -1,
                    cpuValue = -1,
                    PercentPermanentAddresses = -1,
                    PercentTemporaryAddresses = -1,
                    CurrentProcessingCapacity = -1,
                    CurrentUnprotectedStorageUsed = -1,
                    PercentCurrentInteractivePerformance = -1,
                    PercentDBCapability = -1,
                    PercentPermanent256MBSegmentsUsed = -1,
                    PercentSharedProcessorPoolUsed = -1,
                    PercentTemporary256MBSegmentsUsed = -1,
                    PercentTemporary4GBSegmentsUsed = -1,
                    PercentUncappedCPUCapacityUsed = -1,
                    PercentProcessingUnitUsed = -1,
                    TotalAuxiliaryStorage = -1,
                    MainStorageSize = -1,
                    MaximumJobsInSystem = -1,
                    MaximumUnprotectedStorageUsed = -1,
                    UsersCurrentSignedOn = -1,
                    UsersSignedOffWithPrinterOutputWaitingToPrint = -1,
                    UsersSuspendedByGroupJobs = -1,
                    UsersSuspendedBySystemRequest = -1,
                    UsersTemporarilySignedOff = -1,
                    ActiveJobsInSystem = -1,
                    ActiveThreadsInSystem = -1,
                    BatchJobsEndedWithPrinterOutputWaitingToPrint = -1,
                    BatchJobsEnding = -1,
                    BatchJobsHeldOnJobQueue = -1,
                    BatchJobsHeldWhileRunning = -1,
                    BatchJobsOnAHeldJobQueue = -1,
                    BatchJobsOnUnassignedJobQueue = -1,
                    BatchJobsRunning = -1,
                    BatchJobsWaitingForMessage = -1,
                    NumberOfPartitions = -1,
                    NumberOfProcessors = -1,
                    PartitionIdentifier = -1,
                    BatchJobsWaitingToRunOrAlreadyScheduled = -1,
                    JobsInSystem = -1,
                    PoolsNumber = -1
                };



                return perf;
            }

            /////////////////////

        }



        #endregion

        #region LISTA E STATO JOBS
       
        public int JOBLIST(CancellationToken ct, String ServerName, String User, String Pwd, List<JOB> ls_jobs,Persistence p,AS400 server)
        {

            
            try
            {
                 

                Read_JOBS(ServerName, server, p, ls_jobs);

                
            }
            catch (Exception ex)
            {

                return 1;
            }

            return 0;
        }


        /// gettype:
        /// " " - The job is not a valid job. 
        ///    "A" - The job is an autostart job. 
        ///    "B" - The job is a batch job. 
        ///    "I" - The job is an interactive job. 
        ///    "M" - The job is a subsystem monitor job. 
        ///    "R" - The job is a spooled reader job. 
        ///    "S" - The job is a system job. 
        ///    "W" - The job is a spooled writer job. 
        ///    "X" - The job is a SCPF system job.

        private void Read_JOBS(string ServerName, AS400 server, Persistence p, List<JOB> ls_jobs)
        {
            List<JobDetails> results = new List<JobDetails> { };

            try
            {
                JobList list = new JobList(server);
                // list.addJobSelectionCriteria(JobList.SELECTION_JOB_NAME,jobName);// ritorna gli *active
                // Valori multipli
                list.addJobSelectionCriteria(JobList.SELECTION_ACTIVE_JOB_STATUS, Job.ACTIVE_JOB_STATUS_WAIT_MESSAGE);// msgw          
               // list.addJobSelectionCriteria(JobList.SELECTION_ACTIVE_JOB_STATUS, "LCKW");
                //list.addJobSelectionCriteria(JobList.SELECTION_ACTIVE_JOB_STATUS, "EVTW");

                //list.addJobSelectionCriteria(JobList.SELECTION_JOB_TYPE, Job.JOB_TYPE_INTERACTIVE);

                list.addJobSelectionCriteria(JobList.SELECTION_PRIMARY_JOB_STATUS_OUTQ, java.lang.Boolean.FALSE);
                list.addJobSelectionCriteria(JobList.SELECTION_PRIMARY_JOB_STATUS_JOBQ, java.lang.Boolean.FALSE);


                Enumeration items = list.getJobs();

                //ev.WriteEventToMyLog(source +"GETJOBS", "JOBS " + ServerName + " : " + Convert.ToString(items.hasMoreElements())
                //    , EventLogEntryType.Information, 2);

                String Risultato = String.Empty;
                Int32 count = 0;
                while (items.hasMoreElements())
                {
                    Job job = (Job)items.nextElement();

                    job.loadInformation();

                    String Active_Status = job.getValue(Job.ACTIVE_JOB_STATUS).ToString();// stato attuale tipo MSGW e altri
                    String variable = job.getValue(Job.CURRENT_LIBRARY).ToString();

                    String RealUser = job.getValue(Job.CURRENT_USER).ToString().Trim();// real user o current user


                    var details = new JobDetails
                    {
                        ServerName = ServerName,
                        Name = job.getName().Trim(),
                        User = job.getUser().Trim(),
                        RealUser = RealUser,
                        Number = job.getNumber().Trim(),
                        ActiveStatus = Active_Status,
                        Status = job.getStatus().Trim(),
                        Funzione = job.getFunctionName().Trim(),
                        TimeOnStatus = DateTime.Now

                    };

                    // aggiungo il log per il job
                    #region JOBLOG
                    details.JobLog = JOBLOG2(server, details.Name, details.User, details.Number);
                    #endregion


                        // se lo trovo nella lista
                        JOB job_result = ls_jobs.Find(j => (j.JobName == details.Name || j.JobName == "*ALL") && (j.JobUser == details.RealUser || j.JobUser == "*ALL"));


                        //lo inserisco nel risultato
                        if (job_result != null)
                            results.Add(details);
                    

                    count++;
                }


                ev.WriteEventToMyLog(source + "JOBS", ServerName
                    + "\nNumero Jobs Totali: " + count.ToString()
                    + "\nNumero Jobs filtrati: " + results.Count
                    , EventLogEntryType.Warning, 22);

                String res = scansione_job_in_errore(results, p);// controllo se ce ne sono di nuovi

                if (res != String.Empty)// se la scansione ha dei valori
                {


                    Email e = new Email();
                    List<String> to = new List<string> { "andrea.facchin@soluzioniedp.it" };
                    e.SendSMTP("smtp.soluzioniedp.it", "25", "support.T04@soluzioniedp.it", "support20110628", ServerName + " - Jobs in MSGW o LCKW", res, to, "support.T04@soluzioniedp.it", "", false, false);
                    ev.WriteEventToMyLog(source + "JOBS", res.Replace("<br>", "\n"), EventLogEntryType.Error, 6);
                }
                else
                    ev.WriteEventToMyLog(source + "JOBS", ServerName + ": Nessun nuovo JOBS", EventLogEntryType.Information, 6);


                p.jobs = results; // memorizzo lo stato attuale che andrà confrontato successivamente
            }
            catch (Exception)
            {

                 
            }


        }


        public  String JOBLOG2(AS400 server, String name, String user, String number)
        {
            //ev.WriteEventToMyLog(source + "JOBLOG", "Lettura Log per " + name, EventLogEntryType.Information, 7);
            String log = String.Empty;
            try
            {
                JobLog jlog = new JobLog(server, name, user, number);
                Enumeration enlog = jlog.getMessages();
                while (enlog.hasMoreElements()) // joblog
                {
                    QueuedMessage l = (QueuedMessage)enlog.nextElement();
                    log += l.getText() + "\n";
                }
            }
            catch (Exception ex)
            {

                ev.WriteEventToMyLog(source + "JOBLOG", ex.Message, EventLogEntryType.Error, 99);
                return ex.Message;
            }

            return log;
        }


        private string scansione_job_in_errore(List<JobDetails> results, Persistence last_values)
        {
            //ev.WriteEventToMyLog(source + "JOBS", "scansione", EventLogEntryType.Information, 6);

            String JobInError = String.Empty;

 
            try
            {

                List<JobDetails> rossi = (from l in results
                                            join m in last_values.jobs on new {l.User, l.Name, l.Number, l.ServerName }
                                            equals new { m.User, m.Name, m.Number, m.ServerName }
                                            into r
                                            from g in r.DefaultIfEmpty()
                                            where g == null
                                            select l)
                                            .ToList();// nuovi rossi

                List<JobDetails> rossiToVerdi = (from l in last_values.jobs
                                                    join m in results on new { l.User, l.Name, l.Number, l.ServerName }
                                                    equals new { m.User, m.Name, m.Number, m.ServerName }
                                                    into r
                                                    from g in r.DefaultIfEmpty()
                                                    where g == null
                                                    select l)
                                                    .ToList();// da rossi a verdi


                DateTime nowTime = DateTime.Now;
                foreach (var job in rossi)
                {
                   
                    var TimeOnStatus = (nowTime - job.TimeOnStatus).TotalSeconds;

                   // if (TimeOnStatus >= 15)// se maggiori di 30 secondi allora ... vedo se inserirlo
                   // {
                        JobInError += "Server <font style='color:red'>" + job.ServerName 
                            + "<br>Nuovo JOB: " + job.RealUser + "\\" + job.Number + "\\" + job.Name 
                            + " Stato Attuale: " + job.ActiveStatus
                            + "</font><br>Tempo nello stato: " + TimeOnStatus 
                            + "secondi<br />JOB LOG:<br> " + job.JobLog.Replace("\n", "<br>");
                   // }

                }

                foreach (var job in rossiToVerdi)
                {
                    var TimeOnStatus = (nowTime - job.TimeOnStatus).TotalSeconds;

                  //  if (TimeOnStatus >= 15)// se maggiori di 30 secondi allora ... vedo se inserirlo
                  //  {
                        JobInError += "Server <font style='color:green'>" + job.ServerName 
                            + "<br>JOB ripristinato: " + job.RealUser + "\\" + job.Number + "\\" + job.Name 
                            + "<br>Stato precedente: " + job.ActiveStatus
                            + "</font><br>Tempo nello stato precedente: " + TimeOnStatus 
                            + "secondi<br />JOB LOG:<br> " + job.JobLog.Replace("\n", "<br>");
                  //  }

                }

            }
            catch (Exception ex)
            {

                ev.WriteEventToMyLog(source + "Scansione", ex.Message, EventLogEntryType.Error, 99);
                return ex.Message;
            }

            
 


            return JobInError;
        }

        #endregion

        #region MESSAGES QUEUE



        public int MESSAGE_QUEUE(CancellationToken ct, String ServerName, String User, String Pwd, Queues coda)
        {
            AS400 server = null;
            if (server == null)
            {
                server = new AS400();
                server.setSystemName(ServerName);
                server.setUserId(User); //utente con diritti *ALLOBJ
                server.setPassword(Pwd);

            }


            indice_messaggi ultimo_msg = get_lastIndex(ServerName, coda.QueueName);// carico l'ultima chiave della coda

            ev.WriteEventToMyLog("iSeriesMsgQ", coda.QueueName + " - Caricamento ultimo indice precedente:\n"
                + ServerName + " : "
                + ultimo_msg.last_date.ToString()
                + " "
                + ultimo_msg.last_index.ToString()
                , EventLogEntryType.Information, 1);// troppo lunga da scrivere


            while (true)// loop infinito
            {
                if (ct.IsCancellationRequested) { break; }
                ev.WriteEventToMyLog("iSeriesMessQ", "LEGGI MSGQ " + coda.QueueName + " di:" + ServerName, EventLogEntryType.Information, 12);

                // read_message_queue(server);

                try
                {

                    ultimo_msg = read_message_queue(server, ultimo_msg, coda);

                }
                catch (Exception ex)
                {

                    ev.WriteEventToMyLog("iSeriesMessQ", "Lettura MSGQ " + coda.QueueName + " di " + ServerName + " : " + ex.Message, EventLogEntryType.Error, 102);
                }

                Thread.Sleep(10000);// 10 secondi e poi leggo di nuovo

                if (ct.IsCancellationRequested) { break; }

            }

            return 0;
        }

        private indice_messaggi get_lastIndex(String ServerName, String QueueName)
        {
            lock (thisLock)// uso loc per i thread multipli
            {
                indice_messaggi msg = new indice_messaggi { last_index = 0, last_date = DateTime.MinValue };




                String connectionString = @"Server=.;Integrated Security=true;";
                SqlConnection cn = new SqlConnection(connectionString);
                SqlCommand cmd = new SqlCommand();

                String query = String.Empty;

                try
                {
                    query = "select LastDate,LastKey from  [iSeries_" + ServerName + "].[dbo].[iSeries_MsgQ_Index] where servername=@servername and Queuename=@queuename";

                    cmd.Connection = cn;
                    cmd.CommandText = query;
                    cmd.Parameters.Add("@servername", SqlDbType.VarChar).Value = ServerName;
                    cmd.Parameters.Add("@queuename", SqlDbType.VarChar).Value = QueueName;

                    cn.Open();

                    SqlDataReader rd = cmd.ExecuteReader();
                    if (rd.HasRows)
                    {
                        rd.Read();

                        msg.last_date = rd.GetDateTime(0);
                        msg.last_index = Convert.ToInt32(rd.GetString(1));

                        cn.Close();

                        return msg;
                    }
                    else
                    {
                        cn.Close();
                        return msg;

                    }

                }
                catch (Exception ex)
                {

                    if (cn.State == ConnectionState.Open)
                        cn.Close();
                    return msg;
                }
            }

        }

        private Object thisLock = new Object();

        private indice_messaggi read_message_queue(AS400 server, indice_messaggi ultimo_msg, Queues coda)
        {
            List<Message_iSeries> ls_msg = new List<Message_iSeries> { };

            // messaggio singolo
            //MessageFile messageFile = new MessageFile(server);
            //messageFile.setPath("/QSYS.LIB/QCPFMSG.MSGF");
            //AS400Message message = messageFile.getMessage("CPD0170");
            ////////////////////////////

            // QSYSObjectPathName path = new QSYSObjectPathName("%LIBL%",  MessageQ, "MSGQ");
            QSYSObjectPathName path = new QSYSObjectPathName(coda.Library, coda.QueueName, "MSGQ");
            //QSYSObjectPathName path = new QSYSObjectPathName("PMEDDUSR", "PMEDD", "MSGQ");


            MessageQueue q = new MessageQueue(server, path.getPath());// esiste anche MessageQueue.CURRENT
            q.setListDirection(false);// dal nuovo al vecchio default è true dal vecchio al nuovo

            String temp = String.Empty;


            Enumeration enq = q.getMessages();
            String Text = String.Empty;
            String Help = String.Empty;
            String ServerName = String.Empty;
            String QueueName = String.Empty;
            String CurrentUser = String.Empty;
            String FromJobNumber = String.Empty;
            String FromJobName = String.Empty;
            String FromProgram = String.Empty;
            String ReplyStatus = String.Empty;
            String DefaultReply = String.Empty;
            String LibraryName = String.Empty;
            String Queue = String.Empty;
            String AlertOption = String.Empty;
            String HashCode = String.Empty;
            String Message = String.Empty;
            String MessageHelpFormat = String.Empty;
            String Path = String.Empty;
            String ReceivingModuleName = String.Empty;
            String ReceivingProgramName = String.Empty;
            String ReceivingProcedureName = String.Empty;
            String ReceivingType = String.Empty;
            String RequestStatus = String.Empty;
            String RequestLevel = String.Empty;
            String SenderType = String.Empty;
            String SendingModule = String.Empty;
            String SendingProcedure = String.Empty;
            String SendingProgramInstructionNumber = String.Empty;
            String SendingProgramName = String.Empty;
            String SendingUserProfile = String.Empty;
            String SendingUser = String.Empty;

            while (enq.hasMoreElements())
            {

                QueuedMessage mes = (QueuedMessage)enq.nextElement();


                String timewritten = mes.getDate().get(Calendar.YEAR).ToString() + "-"
                                    + mes.getDate().get(Calendar.DAY_OF_MONTH).ToString().PadLeft(2, '0') + "-"
                                    + (mes.getDate().get(Calendar.MONTH) + 1).ToString().PadLeft(2, '0') + " "
                                    + mes.getDate().get(Calendar.HOUR_OF_DAY).ToString().PadLeft(2, '0') + ":"
                                    + mes.getDate().get(Calendar.MINUTE).ToString().PadLeft(2, '0') + ":"
                                    + mes.getDate().get(Calendar.SECOND).ToString().PadLeft(2, '0');

                DateTime dt = new DateTime(mes.getDate().get(Calendar.YEAR),
                    (mes.getDate().get(Calendar.MONTH) + 1),
                    mes.getDate().get(Calendar.DAY_OF_MONTH),
                    mes.getDate().get(Calendar.HOUR_OF_DAY),
                   mes.getDate().get(Calendar.MINUTE),
                   mes.getDate().get(Calendar.SECOND));

                String chiave = string.Empty;

                foreach (byte byteValue in mes.getKey())
                {
                    chiave += Convert.ToInt32(byteValue).ToString().PadLeft(3, '0');
                }


                Boolean isnew_message = false;

                if (Convert.ToInt32(chiave) > ultimo_msg.last_index && dt >= ultimo_msg.last_date)// se l'indice è maggiore sicuramente è nuovo
                {
                    isnew_message = true;
                }
                else if (Convert.ToInt32(chiave) <= ultimo_msg.last_index && dt > ultimo_msg.last_date)// se l'indice è minore o uguale guardo la data
                {
                    isnew_message = true;

                }

                if (isnew_message)
                {
                    Text = mes.getText();
                    if (Text == null)
                        Text = String.Empty;
                    Help = mes.getHelp();
                    if (Help == null)
                        Help = String.Empty;
                    ServerName = server.getSystemName().ToString();
                    if (ServerName == null)
                        ServerName = String.Empty;
                    QueueName = coda.QueueName;
                    if (QueueName == null)
                        QueueName = String.Empty;

                    CurrentUser = mes.getCurrentUser();
                    if (CurrentUser == null)
                        CurrentUser = String.Empty;
                    FromJobNumber = mes.getFromJobNumber();
                    if (FromJobNumber == null)
                        FromJobNumber = String.Empty;
                    FromJobName = mes.getFromJobName();
                    if (FromJobName == null)
                        FromJobName = String.Empty;
                    FromProgram = mes.getFromProgram();
                    if (FromProgram == null)
                        FromProgram = String.Empty;
                    ReplyStatus = mes.getReplyStatus();
                    if (ReplyStatus == null)
                        ReplyStatus = String.Empty;
                    DefaultReply = mes.getDefaultReply();
                    if (DefaultReply == null)
                        DefaultReply = String.Empty;
                    LibraryName = mes.getLibraryName();
                    if (LibraryName == null)
                        LibraryName = String.Empty;
                    Queue = mes.getQueue().ToString();
                    if (Queue == null)
                        Queue = String.Empty;
                    AlertOption = mes.getAlertOption();
                    if (AlertOption == null)
                        AlertOption = String.Empty;
                    HashCode = mes.GetHashCode().ToString();
                    if (HashCode == null)
                        HashCode = String.Empty;
                    Message = mes.getMessage();
                    if (Message == null)
                        Message = String.Empty;
                    MessageHelpFormat = mes.getMessageHelpFormat();
                    if (MessageHelpFormat == null)
                        MessageHelpFormat = String.Empty;
                    Path = mes.getPath();
                    if (Path == null)
                        Path = String.Empty;
                    ReceivingModuleName = mes.getReceivingModuleName();
                    if (ReceivingModuleName == null)
                        ReceivingModuleName = String.Empty;
                    ReceivingProgramName = mes.getReceivingProgramName();
                    if (ReceivingProgramName == null)
                        ReceivingProgramName = String.Empty;
                    ReceivingProcedureName = mes.getReceivingProcedureName();
                    if (ReceivingProcedureName == null)
                        ReceivingProcedureName = String.Empty;
                    ReceivingType = mes.getReceivingType();
                    if (ReceivingType == null)
                        ReceivingType = String.Empty;
                    RequestStatus = mes.getRequestStatus();
                    if (RequestStatus == null)
                        RequestStatus = String.Empty;

                    SenderType = mes.getSenderType();
                    if (SenderType == null)
                        SenderType = String.Empty;
                    SendingModule = mes.getSendingModuleName();
                    if (SendingModule == null)
                        SendingModule = String.Empty;
                    SendingProcedure = mes.getSendingProcedureName();
                    if (SendingProcedure == null)
                        SendingProcedure = String.Empty;
                    SendingProgramInstructionNumber = mes.getSendingProgramInstructionNumber();
                    if (SendingProgramInstructionNumber == null)
                        SendingProgramInstructionNumber = String.Empty;
                    SendingProgramName = mes.getSendingProgramName();
                    if (SendingProgramName == null)
                        SendingProgramName = String.Empty;
                    SendingUserProfile = mes.getSendingUserProfile();
                    if (SendingUserProfile == null)
                        SendingUserProfile = String.Empty;
                    SendingUser = mes.getUser();
                    if (SendingUser == null)
                        SendingUser = String.Empty;

                    try
                    {
                        RequestLevel = Convert.ToString(mes.getRequestLevel());
                    }
                    catch
                    { RequestLevel = String.Empty; }


                    Message_iSeries MSG = new Message_iSeries
                    {
                        Key_id = chiave,
                        Key_message = mes.getKey(),
                        Timewritten = dt,
                        msgid = mes.getID(),
                        Severity = mes.getSeverity().ToString(),
                        mText = Text,
                        Help = Help,
                        ServerName = ServerName,
                        QueueName = QueueName,
                        CurrentUser = CurrentUser,
                        FromJobNumber = FromJobNumber,
                        FromJobName = FromJobName,
                        FromProgram = FromProgram,
                        ReplyStatus = ReplyStatus,
                        DefaultReply = DefaultReply,
                        LibraryName = LibraryName,
                        Queue = Queue,
                        AlertOption = AlertOption,
                        HashCode = HashCode,
                        Message = Message,
                        MessageHelpFormat = MessageHelpFormat,
                        Path = Path,
                        ReceivingModuleName = ReceivingModuleName,
                        ReceivingProgramName = ReceivingProgramName,
                        ReceivingProcedureName = ReceivingProcedureName,
                        ReceivingType = ReceivingType,
                        RequestStatus = RequestStatus,
                        RequestLevel = RequestLevel,
                        SenderType = SenderType,
                        SendingModule = SendingModule,
                        SendingProcedure = SendingProcedure,
                        SendingProgramInstructionNumber = SendingProgramInstructionNumber,
                        SendingProgramName = SendingProgramName,
                        SendingUserProfile = SendingUserProfile,
                        SendingUser = SendingUser

                    };

                    ls_msg.Add(MSG);

                }

            }


            if (ls_msg.Count > 0)
            {

                // salvo l'indice
                ultimo_msg.last_index = Convert.ToInt32(ls_msg[0].Key_id);// essendo in ordine dal + grande al + piccolo il primo ha la chiave maggiore
                ultimo_msg.last_date = ls_msg[0].Timewritten;




                ev.WriteEventToMyLog("iSeriesMessQ", QueueName + " - Messaggi "
                    + server.getSystemName() + " da: "
                    + ls_msg[ls_msg.Count - 1].Key_id
                    + "(" + ls_msg[ls_msg.Count - 1].Timewritten.ToString() + ")"
                    + " --> a: "
                    + ls_msg[0].Key_id + "(" + ls_msg[0].Timewritten.ToString() + ")"
                    , EventLogEntryType.Warning, 100);

                lock (thisLock)// uso loc per i thread multipli
                    try
                    {
                        insert_msg_bulk(ls_msg, ServerName);
                        update_index_msgq(ultimo_msg, ls_msg[0].ServerName, coda.QueueName);// aggiorno l'indice
                    }
                    catch (Exception ex)
                    {

                        ev.WriteEventToMyLog("iSeriesMsgQ - INSERT", ex.Message + "\n" + ex.InnerException, EventLogEntryType.Error, 999);// troppo lunga da scrivere;
                    }


                //SCRIVO IL RISULTATO SU EVENTLOG SERIALIZZATO MA ESSENDO ENORME E SUPERA I 32677 char meglio di no
                //String xmlResult = String.Empty;
                //xmlResult = Serializza_Message_queue(ls_msg, xmlResult);
                // ev.WriteEventToMyLog("iSeriesMessQ", xmlResult, EventLogEntryType.Information, 1);// troppo lunga da scrivere
                ////////////////////
            }
            return ultimo_msg;
        }

        private void update_index_msgq(indice_messaggi ultimo_msg, String ServerName, String QueueName)
        {
            String connectionString = @"Server=.;Integrated Security=true;";
            SqlConnection cn = new SqlConnection(connectionString);
            SqlCommand cmd = new SqlCommand();

            String query = String.Empty;

            try
            {
                query = "update  [iSeries_" + ServerName + "].[dbo].[iSeries_MsgQ_Index] SET "
                        + "LastDate=@LastDate,LastKey=@LastKey"
                        + " where servername=@servername and queuename=@queuename";

                cmd.Parameters.Clear();
                cmd.Parameters.Add("@LastDate", SqlDbType.DateTime).Value = ultimo_msg.last_date;
                cmd.Parameters.Add("@LastKey", SqlDbType.NVarChar).Value = ultimo_msg.last_index;
                cmd.Parameters.Add("@servername", SqlDbType.NVarChar).Value = ServerName;
                cmd.Parameters.Add("@queuename", SqlDbType.NVarChar).Value = QueueName;


                cmd.Connection = cn;
                cmd.CommandText = query;

                cn.Open();
                int r_affect = (int)cmd.ExecuteNonQuery();
                if (r_affect == 0)
                    insert_index(ultimo_msg, ServerName, cn, cmd, QueueName);

                cn.Close();
            }
            catch (Exception ex)
            {
                ev.WriteEventToMyLog("iSeriesMessQ", "update index " + ServerName + " : " + ex.Message, EventLogEntryType.Error, 103);



            }
            finally
            {
                if (cn.State == ConnectionState.Open)
                    cn.Close();
            }

        }

        private static void insert_index(indice_messaggi ultimo_msg, string ServerName, SqlConnection cn, SqlCommand cmd, string QueueName)
        {
            string query = "INSERT INTO  [iSeries_" + ServerName + "].[dbo].[iSeries_MsgQ_Index] ([LastKey],[LastDate],ServerName,QueueName)"
            + " VALUES (@LastKey,@LastDate,@servername,@queuename)";
            cmd.Parameters.Clear();
            cmd.Parameters.Add("@LastDate", SqlDbType.DateTime).Value = ultimo_msg.last_date;
            cmd.Parameters.Add("@LastKey", SqlDbType.NVarChar).Value = ultimo_msg.last_index;
            cmd.Parameters.Add("@servername", SqlDbType.NVarChar).Value = ServerName;
            cmd.Parameters.Add("@queuename", SqlDbType.NVarChar).Value = QueueName;

            cmd.Connection = cn;
            cmd.CommandText = query;

            if (cn.State == ConnectionState.Closed)
                cn.Open();

            cmd.ExecuteNonQuery();

            cn.Close();

        }

        // inserimento classico
        private void insert_msg(List<Message_iSeries> ls_messages, String ServerName)
        {
            String connectionString = @"Server=.;Integrated Security=true;";
            SqlConnection cn = new SqlConnection(connectionString);
            SqlCommand cmd = new SqlCommand();
            String query = " insert into[iSeries_" + ServerName + "].[dbo].[MessageQueue]([Timewritten] ,[QueueName] ,[msgid] ,[severity] ,[ServerName] ,[CurrentUser]"
                + ",[FromJobNumber] ,[FromJobName] ,[FromProgram] ,[mtext] ,[help] ,[ReplyStatus] ,[DefaultReply] ,[LibraryName] ,[Queue] ,[AlertOption] ,"
                + "[HashCode] ,[Message] ,[MessageHelpFormat] ,[Path] ,[ReceivingModuleName] ,[ReceivingProgramName] ,[ReceivingProcedureName] ,[ReceivingType]"
                + ",[RequestStatus] ,[RequestLevel] ,[SenderType] ,[SendingModule] ,[SendingProcedure] ,[SendingUserProfile],[SendingUser],"
                + "[SendingProgramInstructionNumber],[SendingProgramName],[key_message],key_id)"
                + " values(@timewritten, @queuename, @msgid, @severity, @servername, @CurrentUser,"
                + "@FromJobNumber, @FromJobName, @FromProgram, @mtext, @help, @ReplyStatus, @DefaultReply, @LibraryName, @Queue, @AlertOption, @HashCode,"
                + "@Message, @MessageHelpFormat, @Path, @ReceivingModuleName, @ReceivingProgramName, @ReceivingProcedureName, @ReceivingType, @RequestStatus,"
                + "@RequestLevel, @SenderType, @SendingModule, @SendingProcedure, @SendingUserProfile, @SendingUser, @SendingProgramInstructionNumber,"
                + "@SendingProgramName, @key_message,@key_id)";
            ev.WriteEventToMyLog("query", query, EventLogEntryType.Warning, 1);// troppo lunga da scrivere

            cmd.Parameters.Add("@timewritten", SqlDbType.DateTime);
            cmd.Parameters.Add("@msgid", SqlDbType.NVarChar);
            cmd.Parameters.Add("@severity", SqlDbType.NVarChar);
            cmd.Parameters.Add("@servername", SqlDbType.NVarChar);
            cmd.Parameters.Add("@mtext", SqlDbType.NVarChar);
            cmd.Parameters.Add("@help", SqlDbType.NVarChar);
            cmd.Parameters.Add("@queuename", SqlDbType.NVarChar);
            cmd.Parameters.Add("@CurrentUser", SqlDbType.NVarChar);// mes.getCurrentUser(),
            cmd.Parameters.Add("@FromJobNumber", SqlDbType.NVarChar);// mes.getFromJobNumber(),
            cmd.Parameters.Add("@FromJobName", SqlDbType.NVarChar);// mes.getFromJobName(),
            cmd.Parameters.Add("@FromProgram", SqlDbType.NVarChar);// mes.getFromProgram(),
            cmd.Parameters.Add("@ReplyStatus", SqlDbType.NVarChar);// mes.getReplyStatus(),
            cmd.Parameters.Add("@DefaultReply", SqlDbType.NVarChar);// mes.getDefaultReply(),
            cmd.Parameters.Add("@LibraryName", SqlDbType.NVarChar);// mes.getLibraryName(),
            cmd.Parameters.Add("@Queue", SqlDbType.NVarChar);// mes.getQueue().toString(),
            cmd.Parameters.Add("@AlertOption", SqlDbType.NVarChar);// mes.getAlertOption(),
            cmd.Parameters.Add("@HashCode", SqlDbType.NVarChar);// mes.GetHashCode().ToString(),
            cmd.Parameters.Add("@Message", SqlDbType.NVarChar);// mes.getMessage(),
            cmd.Parameters.Add("@MessageHelpFormat", SqlDbType.NVarChar);// mes.getMessageHelpFormat(),
            cmd.Parameters.Add("@Path", SqlDbType.NVarChar);// mes.getPath(),
            cmd.Parameters.Add("@ReceivingModuleName", SqlDbType.NVarChar);// mes.getReceivingModuleName(),
            cmd.Parameters.Add("@ReceivingProgramName", SqlDbType.NVarChar);// mes.getReceivingProgramName(),
            cmd.Parameters.Add("@ReceivingProcedureName", SqlDbType.NVarChar);// mes.getReceivingProcedureName(),
            cmd.Parameters.Add("@ReceivingType", SqlDbType.NVarChar);// mes.getReceivingType(),
            cmd.Parameters.Add("@RequestStatus", SqlDbType.NVarChar);// mes.getRequestStatus(),
            cmd.Parameters.Add("@RequestLevel", SqlDbType.NVarChar);// mes.getRequestLevel().ToString(),
            cmd.Parameters.Add("@SenderType", SqlDbType.NVarChar);// mes.getSenderType(),
            cmd.Parameters.Add("@SendingModule", SqlDbType.NVarChar);// mes.getSendingModuleName(),
            cmd.Parameters.Add("@SendingProcedure", SqlDbType.NVarChar);// mes.getSendingProcedureName(),
            cmd.Parameters.Add("@SendingProgramInstructionNumber", SqlDbType.NVarChar);// mes.getSendingProgramInstructionNumber(),
            cmd.Parameters.Add("@SendingProgramName", SqlDbType.NVarChar);// mes.getSendingProgramName(),
            cmd.Parameters.Add("@SendingUserProfile", SqlDbType.NVarChar);// mes.getSendingUserProfile(),
            cmd.Parameters.Add("@SendingUser", SqlDbType.NVarChar);// mes.getUser()
            cmd.Parameters.Add("@key_message", SqlDbType.NVarChar);// mes.getUser()
            cmd.Parameters.Add("@key_id", SqlDbType.NVarChar);// mes.getUser()

            cmd.CommandText = query;
            cmd.Connection = cn;
            cn.Open();

            foreach (Message_iSeries m in ls_messages)
            {

                cmd.Parameters["@timewritten"].Value = m.Timewritten;
                cmd.Parameters["@msgid"].Value = m.msgid;
                cmd.Parameters["@severity"].Value = m.Severity;
                cmd.Parameters["@servername"].Value = m.ServerName;
                cmd.Parameters["@mtext"].Value = m.mText;
                cmd.Parameters["@help"].Value = m.Help;
                cmd.Parameters["@queuename"].Value = m.QueueName;
                cmd.Parameters["@CurrentUser"].Value = m.CurrentUser;// mes.getCurrentUser(),
                cmd.Parameters["@FromJobNumber"].Value = m.FromJobNumber;// mes.getFromJobNumber(),
                cmd.Parameters["@FromJobName"].Value = m.FromJobName;// mes.getFromJobName(),
                cmd.Parameters["@FromProgram"].Value = m.FromProgram;// mes.getFromProgram(),
                cmd.Parameters["@ReplyStatus"].Value = m.ReplyStatus;// mes.getReplyStatus(),
                cmd.Parameters["@DefaultReply"].Value = m.DefaultReply;// mes.getDefaultReply(),
                cmd.Parameters["@LibraryName"].Value = m.LibraryName;// mes.getLibraryName(),
                cmd.Parameters["@Queue"].Value = m.Queue;// mes.getQueue().toString(),
                cmd.Parameters["@AlertOption"].Value = m.AlertOption;// mes.getAlertOption(),
                cmd.Parameters["@HashCode"].Value = m.HashCode;// mes.GetHashCode().ToString(),
                cmd.Parameters["@Message"].Value = m.Message;// mes.getMessage(),
                cmd.Parameters["@MessageHelpFormat"].Value = m.MessageHelpFormat;// mes.getMessageHelpFormat(),
                cmd.Parameters["@Path"].Value = m.Path;// mes.getPath(),
                cmd.Parameters["@ReceivingModuleName"].Value = m.ReceivingModuleName;// mes.getReceivingModuleName(),
                cmd.Parameters["@ReceivingProgramName"].Value = m.ReceivingProgramName;// mes.getReceivingProgramName(),
                cmd.Parameters["@ReceivingProcedureName"].Value = m.ReceivingProcedureName;// mes.getReceivingProcedureName(),
                cmd.Parameters["@ReceivingType"].Value = m.ReceivingType;// mes.getReceivingType(),
                cmd.Parameters["@RequestStatus"].Value = m.RequestStatus;// mes.getRequestStatus(),
                cmd.Parameters["@RequestLevel"].Value = m.RequestLevel;// mes.getRequestLevel().ToString(),
                cmd.Parameters["@SenderType"].Value = m.SenderType;// mes.getSenderType(),
                cmd.Parameters["@SendingModule"].Value = m.SendingModule;// mes.getSendingModuleName(),
                cmd.Parameters["@SendingProcedure"].Value = m.SendingProcedure;// mes.getSendingProcedureName(),
                cmd.Parameters["@SendingProgramInstructionNumber"].Value = m.SendingProgramInstructionNumber;// mes.getSendingProgramInstructionNumber(),
                cmd.Parameters["@SendingProgramName"].Value = m.SendingProgramName;// mes.getSendingProgramName(),
                cmd.Parameters["@SendingUserProfile"].Value = m.SendingUserProfile;// mes.getSendingUserProfile(),
                cmd.Parameters["@SendingUser"].Value = m.SendingUser;// mes.getUser()
                cmd.Parameters["@key_message"].Value = m.Key_message;// mes.getUser()
                cmd.Parameters["@key_id"].Value = m.Key_id;// mes.getUser()

                cmd.ExecuteNonQuery();


            }

            cn.Close();
        }
        /// ///////////////////////


        // inserimento a blocchi
        private void insert_msg_bulk(List<Message_iSeries> ls_messages, String ServerName)
        {

            String connectionString = @"Server=.\sqlexpress;Integrated Security=true;";

            lock (thisLock)// uso loc per i thread multipli
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    SqlTransaction transaction = connection.BeginTransaction();

                    using (var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction))
                    {
                        bulkCopy.BatchSize = ls_messages.Count;
                        bulkCopy.DestinationTableName = "iseries_" + ServerName + ".dbo.MessageQueue";
                        try
                        {
                            bulkCopy.WriteToServer(ls_messages.AsDataTable());
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            connection.Close();
                            transaction.Dispose();
                            ev.WriteEventToMyLog("iSeriesMessQ - insert_msg_bulk", ex.Message, EventLogEntryType.Information, 999);// troppo lunga da scrivere
                        }
                    }

                    transaction.Commit();
                    transaction.Dispose();
                }


            /////////////////////////

        }


        // serializzazione che per ora non uso della lista messaggi
        private string Serializza_Message_queue(List<Message_iSeries> ls_msg, string xmlResult)
        {
            // SERIALIZZAZIONE DELLA CLASSE PER AVERE UN XML CON IL RISULTATO
            try
            {
                using (var stream = new MemoryStream())
                {
                    using (var writer = XmlWriter.Create(stream))
                    {
                        XmlSerializer serializer = new XmlSerializer(ls_msg.GetType());
                        serializer.Serialize(writer, ls_msg);
                        xmlResult = Encoding.UTF8.GetString(stream.ToArray());// per non sbagliare la codifica

                    }
                }
            }
            catch (Exception ex)
            {

                ev.WriteEventToMyLog("iSeriesMessQ", ex.Message + "\n" + ex.InnerException, EventLogEntryType.Error, 111);
            }
            ///////////////////////////////// FINE ////////////////////////////////////////////
            return xmlResult;
        }




        #endregion

        #region utenti connessi e dettagli attraverso la scansione dei job interattivi

        public int GetInteractiveResp(CancellationToken ct, String ServerName, String User, String Pwd)
        {

            AS400 server = null;
            if (server == null)
            {
                server = new AS400();
                server.setSystemName(ServerName);
                server.setUserId(User); //utente con diritti *ALLOBJ
                server.setPassword(Pwd);

            }

            while (true)// loop infinito
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                read_interactive(server);

                Thread.Sleep(10000);// DETERMINO IL REFRESH DEL CONTROLLO

                if (ct.IsCancellationRequested) { break; }
            }

            return 0;// performance interattive + lista job interattivi 


        }

        private void read_interactive(AS400 server)
        {
            /////////// VADO SUL ISERIES

            List<JobDetails> results = new List<JobDetails> { };


            /// gettype:
            /// " " - The job is not a valid job. 
            ///    "A" - The job is an autostart job. 
            ///    "B" - The job is a batch job. 
            ///    "I" - The job is an interactive job. 
            ///    "M" - The job is a subsystem monitor job. 
            ///    "R" - The job is a spooled reader job. 
            ///    "S" - The job is a system job. 
            ///    "W" - The job is a spooled writer job. 
            ///    "X" - The job is a SCPF system job.
            ///    "V" - SLIC task - check product licenses
            ///  NB  se voglio riusare la lista devo fare clearJobSelectionCriteria altrimenti mantiene i vecchi

            JobList list = new JobList(server);
            list.addJobSelectionCriteria(JobList.SELECTION_PRIMARY_JOB_STATUS_ACTIVE, java.lang.Boolean.TRUE);
            list.addJobSelectionCriteria(JobList.SELECTION_PRIMARY_JOB_STATUS_JOBQ, java.lang.Boolean.FALSE);
            list.addJobSelectionCriteria(JobList.SELECTION_PRIMARY_JOB_STATUS_OUTQ, java.lang.Boolean.FALSE);
            list.addJobSelectionCriteria(JobList.SELECTION_JOB_TYPE, Job.JOB_TYPE_INTERACTIVE);

            Int32 contJob = 0;// conteggio jobs

            decimal mediaIntRespTime = 0;
            decimal mediaInteractiveTrans = 0;
            decimal interactiveResp = 0;
            decimal interactiveTrans = 0;
            Int32 numTransIntTotali = 0;

            //recupero la data della partizione
            SystemStatus st = new SystemStatus();
            st.setSystem(server);
            SystemValue sv = new SystemValue(server, "QTIME");
            String timeserver = sv.getValue().ToString();
            SystemValue sx = new SystemValue(server, "QDATE");
            String dateserver = sx.getValue().ToString();

            DateTime DateiSeries = Convert.ToDateTime(dateserver + " " + timeserver);



            //trasformo il datetime iseries nel formato che serve a noi per il javascript
            String tstamp = DateiSeries.ToString("MM'/'dd'/'yyyy H':'mm':'ss");

            List<JobDetails_interactive> ls_int_job = new List<JobDetails_interactive> { };
            Enumeration items = list.getJobs();

            InteractiveRespTime intrp = null;

            if (items.hasMoreElements()) //se c sono job interattivi sulla partizione
            {
                Stopwatch sw = Stopwatch.StartNew();

                var signals = new List<ManualResetEvent>();// array contenente il segnale di fine thread

                //dettagli del job interattivo con la media dei tempi, lancio in thread per ogni job e poi carico il risultato in lista in un blocco locked per evitare sovrapposizioni tra i thread che aggiungono alla lista
                while (items.hasMoreElements())
                {
                    Job job = (Job)items.nextElement();// prelevo i valori dei job
                    contJob++;// ho trovato un job
                    var signal = new ManualResetEvent(false);// segnale di fine thread a falso

                    // creo il thread con il suo signal
                    var t = new Thread(() =>
                    {
                        JobDetails_interactive j = Get_Job_details_interactive(contJob, ref mediaIntRespTime, ref mediaInteractiveTrans, ref interactiveResp, ref interactiveTrans, ref numTransIntTotali, job);
                        lock (thisLock)
                        {
                            ls_int_job.Add(j);
                        }

                        signal.Set();
                    });
                    signals.Add(signal);// inserisco prima di lanciare il signal nell'array dei signals
                    t.Start();// lancio il thread
                }

                WaitAll(signals.ToArray());// attendo il reset dei thread
                sw.Stop();
                TimeSpan time = sw.Elapsed;
                // calcolo la media
                mediaInteractiveTrans = Math.Round(mediaInteractiveTrans / contJob, 3);
                mediaIntRespTime = Math.Round(mediaIntRespTime / contJob, 3);

                intrp = new InteractiveRespTime
                {
                    Timewritten = tstamp,
                    mediaIntRespTime = mediaIntRespTime,
                    numJobs = contJob,
                    NumTransIntTot = numTransIntTotali,
                    mediaInteractiveTrans = mediaInteractiveTrans,
                    timeElapsed = sw.ElapsedMilliseconds.ToString(),// metto i ms
                    jobDetail = ls_int_job.OrderBy(item => item.Name).ToList()
                };
            }
            else //non ci sono job interattivi
            {
                intrp = new InteractiveRespTime
                {
                    Timewritten = tstamp,
                    mediaIntRespTime = -1,
                    numJobs = 0,
                    mediaInteractiveTrans = -1,
                    NumTransIntTot = -1,
                    timeElapsed = "0",
                    jobDetail = null
                };
            }
            String int_result = String.Empty;
            foreach (JobDetails_interactive j in intrp.jobDetail)
            {
                int_result += j.User + " " + j.Name + " " + j.Command + "\n";
            }

            ev.WriteEventToMyLog("iSeriesINTERACTIVE", int_result, EventLogEntryType.Information, 112);
        }


        private JobDetails_interactive Get_Job_details_interactive(Int32 contJob, ref decimal mediaIntRespTime, ref decimal mediaInteractiveTrans, ref decimal interactiveResp, ref decimal interactiveTrans, ref Int32 numTransIntTotali, Job job)
        {
            try
            {
                String Command = String.Empty;
                String Name = String.Empty;
                String User = String.Empty;
                String Active_Status = String.Empty;
                String CpuTime = String.Empty;

                Int32 NumTransInt = 0;

                lock (thisLock)
                {
                    Active_Status = job.getValue(Job.ACTIVE_JOB_STATUS).ToString();// stato attuale tipo MSGW e altri

                    // calcolo la media REPONSE TIME


                    interactiveResp = Convert.ToDecimal(job.getValue(Job.ELAPSED_INTERACTIVE_RESPONSE_TIME).ToString()) / 100;
                    interactiveTrans = Convert.ToDecimal(job.getValue(Job.ELAPSED_INTERACTIVE_TRANSACTIONS).ToString()) / 100;



                    NumTransInt = Convert.ToInt32(job.getValue(Job.INTERACTIVE_TRANSACTIONS).ToString());
                    numTransIntTotali += NumTransInt;

                    //mediaIntRespTime = Math.Round((mediaIntRespTime + interactiveResp) / contJob, 3);// arrotondo
                    //mediaInteractiveTrans = Math.Round((mediaInteractiveTrans + interactiveTrans) / contJob, 3);// arrotondo

                    mediaIntRespTime = mediaIntRespTime + interactiveResp;//  
                    mediaInteractiveTrans = mediaInteractiveTrans + interactiveTrans;//  


                    Command = job.getFunctionName();
                    Name = job.getName();
                    User = job.getUser();
                    CpuTime = (Convert.ToDecimal(job.getValue(Job.CPU_TIME_USED).ToString()) / 100).ToString();// tempo CPU del JOB

                }
                JobDetails_interactive j = new JobDetails_interactive
                {
                    Name = Name,
                    User = User,
                    Status = "*ACTIVE",
                    ActiveStatus = Active_Status,
                    Command = Command,
                    intRespTime = interactiveResp.ToString(),
                    intTransTime = interactiveTrans.ToString(),
                    NumTransInt = NumTransInt.ToString(),
                    CpuTime = CpuTime

                };

                return j; // faccio la lista del job interattivi

            }
            catch (Exception e)
            {
                JobDetails_interactive j = new JobDetails_interactive
                {
                    Name = "ERROR",
                    User = e.Message,
                    Status = "*ACTIVE",
                    ActiveStatus = "",
                    Command = "",
                    intRespTime = "",
                    intTransTime = "",
                    NumTransInt = "",
                    CpuTime = ""

                };
                return j;

            }
        }



        public static void WaitAll(WaitHandle[] handles)
        {
            if (handles == null)
                throw new ArgumentNullException("handles");
            foreach (WaitHandle wh in handles)
            {
                wh.WaitOne();
            }
        }


        #endregion

        #region PING

        public int PING_SERVER(String ip, List<AS400Server> listaServer)
        {
            PingCls p = new PingCls();
            while (true)
            {
                if (p.isAvailable(ip))
                {

                    ev.WriteEventToMyLog("iSeries PING", "Server " + ip + " raggiungibile", EventLogEntryType.Information, 11);

                    AS400Server s = listaServer[listaServer.FindIndex(item => item.Ipaddress == ip)];
                    Boolean isStop = s.cTokenSource.IsCancellationRequested;


                    if (isStop)
                    {
 
    
                    }

                }
                else
                {
  

                }
                Thread.Sleep(10000);
            }

            return 0;

        }

        #endregion

    }

    // NOTA: le classi serializzabili devono essere fuori da ogni classe per essere pubbliche

    // CLASSI SERIALIZZABILI DEI RISULTATI

    [Serializable]// la definisco così perchè voglio serializzare per salvare su tabella le perf totali
    public class Performance_iSeries
    {
        public string ServerName { get; set; }
        public string Timewritten { get; set; }
        public float cpuValue { get; set; }
        public float aspValue { get; set; }
        public float PercentPermanentAddresses { get; set; }
        public float PercentTemporaryAddresses { get; set; }
        public float CurrentProcessingCapacity { get; set; }
        public int CurrentUnprotectedStorageUsed { get; set; }
        public float PercentCurrentInteractivePerformance { get; set; }
        public float PercentDBCapability { get; set; }
        public float PercentPermanent256MBSegmentsUsed { get; set; }
        public float PercentSharedProcessorPoolUsed { get; set; }
        public float PercentTemporary256MBSegmentsUsed { get; set; }
        public float PercentTemporary4GBSegmentsUsed { get; set; }
        public float PercentUncappedCPUCapacityUsed { get; set; }
        public float PercentProcessingUnitUsed { get; set; }
        public int TotalAuxiliaryStorage { get; set; }
        public long MainStorageSize { get; set; }
        public long MaximumJobsInSystem { get; set; }
        public int MaximumUnprotectedStorageUsed { get; set; }
        public int UsersCurrentSignedOn { get; set; }
        public int UsersSignedOffWithPrinterOutputWaitingToPrint { get; set; }
        public int UsersSuspendedByGroupJobs { get; set; }
        public int UsersSuspendedBySystemRequest { get; set; }
        public int UsersTemporarilySignedOff { get; set; }
        public int ActiveJobsInSystem { get; set; }
        public int ActiveThreadsInSystem { get; set; }
        public int BatchJobsEndedWithPrinterOutputWaitingToPrint { get; set; }
        public int BatchJobsEnding { get; set; }
        public int BatchJobsHeldOnJobQueue { get; set; }
        public int BatchJobsHeldWhileRunning { get; set; }
        public int BatchJobsOnAHeldJobQueue { get; set; }
        public int BatchJobsOnUnassignedJobQueue { get; set; }
        public int BatchJobsRunning { get; set; }
        public int BatchJobsWaitingForMessage { get; set; }
        public int NumberOfPartitions { get; set; }
        public int NumberOfProcessors { get; set; }
        public int PartitionIdentifier { get; set; }
        public int BatchJobsWaitingToRunOrAlreadyScheduled { get; set; }
        public int JobsInSystem { get; set; }
        public int PoolsNumber { get; set; }

    }

    [Serializable]// la definisco così perchè voglio serializzare per salvare su tabella le perf totali
    [XmlRoot("messages")]
    public class Message_iSeries
    {
        public string Key_id { get; set; }
        public byte[] Key_message { get; set; }
        public DateTime Timewritten { get; set; }
        public string msgid { get; set; }
        public string Severity { get; set; }
        public string mText { get; set; }
        public string Help { get; set; }
        public string ServerName { get; set; }
        public string QueueName { get; set; }
        public string CurrentUser { get; set; }
        public string FromJobNumber { get; set; }
        public string FromJobName { get; set; }
        public string FromProgram { get; set; }
        public string ReplyStatus { get; set; }
        public string DefaultReply { get; set; }
        public string LibraryName { get; set; }
        public string Queue { get; set; }
        public string AlertOption { get; set; }
        public string HashCode { get; set; }
        public string Message { get; set; }
        public string MessageHelpFormat { get; set; }
        public string Path { get; set; }
        public string ReceivingModuleName { get; set; }
        public string ReceivingProgramName { get; set; }
        public string ReceivingProcedureName { get; set; }
        public string ReceivingType { get; set; }
        public string RequestStatus { get; set; }
        public string RequestLevel { get; set; }
        public string SenderType { get; set; }
        public string SendingModule { get; set; }
        public string SendingProcedure { get; set; }
        public string SendingUserProfile { get; set; }
        public string SendingUser { get; set; }
        public string SendingProgramInstructionNumber { get; set; }
        public string SendingProgramName { get; set; }

    }

    [Serializable]
    public class JobDetails
    {
        public String ServerName { get; set; }
        public string ActiveStatus { get; set; }
        public string Status { get; set; }
        public string Number { get; set; }
        public string Name { get; set; }
        public string User { get; set; }
        public string RealUser { get; set; }
        public string Funzione { get; set; }
        public String JobLog { get; set; }
        public DateTime TimeOnStatus { get; set; }
    }
    ////////////

    // CLASSE CHE MEMORIZZA LO STATO PRECEDENTE PER NON INVIARE ALERT SU JOB IN MSGW RIPETITIVI
    // QUINDI ABBIAMO NOME SERVER E LA LISTA DEI JOBS CON LORO STATO
    [Serializable]
    public class Persistence
    {
        public String ServerName { get; set; }
        public List<JobDetails> jobs { get; set; }

    }

    public class indice_messaggi
    {
        public int last_index { get; set; }
        public DateTime last_date { get; set; }
    }
    /////////////


    // interfaccia per creare da una lista una tabella da buttare su db con la BULK

    public static class IEnumerableExtensions
    {
        public static DataTable AsDataTable<T>(this IEnumerable<T> data)
        {
            PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(typeof(T));
            var table = new DataTable();
            foreach (PropertyDescriptor prop in properties)
                table.Columns.Add(prop.Name, Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType);
            foreach (T item in data)
            {
                DataRow row = table.NewRow();
                foreach (PropertyDescriptor prop in properties)
                    row[prop.Name] = prop.GetValue(item) ?? DBNull.Value;
                table.Rows.Add(row);
            }
            return table;
        }
    }
    /////////////////////////////////////////////


    public class JobDetails_interactive
    {
        public string Status { get; set; }
        public string ActiveStatus { get; set; }
        public string Number { get; set; }
        public string Name { get; set; }
        public string User { get; set; }
        public string WrkName { get; set; }
        public string IpAddress { get; set; }
        public string Subsystem { get; set; }
        public string Command { get; set; }
        public string intRespTime { get; set; }
        public string intTransTime { get; set; }
        public string NumTransInt { get; set; }
        public string CpuTime { get; set; }
        public Boolean isinTable { get; set; }
    }

    public class InteractiveRespTime
    {
        public string Timewritten { get; set; }
        public decimal mediaIntRespTime { get; set; }
        public Int32 numJobs { get; set; }
        public decimal mediaInteractiveTrans { get; set; }
        public Int32 NumTransIntTot { get; set; }
        public List<JobDetails_interactive> jobDetail { get; set; }
        public String timeElapsed { get; set; }// tempo di elaborazione dei job che userò per settare il refresh

    }
    public class AS400Server
    {
        public string ServerName { get; set; }
        public string Ipaddress { get; set; }
        public string userid { get; set; }
        public string password { get; set; }
        public CancellationTokenSource cTokenSource { get; set; }// token di cancellazione

    }

    public class JOB
    {
        public String JobName;
        public String JobUser;

    }

    public class Queues
    {
        public string ServerName { get; set; }
        public string QueueName { get; set; }
        public string Library { get; set; }

    }

}
