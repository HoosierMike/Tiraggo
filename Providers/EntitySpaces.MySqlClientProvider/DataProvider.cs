/*  New BSD License
-------------------------------------------------------------------------------
Copyright (c) 2006-2012, EntitySpaces, LLC
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:
    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.
    * Redistributions in binary form must reproduce the above copyright
      notice, this list of conditions and the following disclaimer in the
      documentation and/or other materials provided with the distribution.
    * Neither the name of the EntitySpaces, LLC nor the
      names of its contributors may be used to endorse or promote products
      derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL EntitySpaces, LLC BE LIABLE FOR ANY
DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
-------------------------------------------------------------------------------
*/

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Threading;
using Tiraggo.DynamicQuery;
using Tiraggo.Interfaces;
using MySql.Data.MySqlClient;

namespace Tiraggo.MySqlClientProvider
{
    public class DataProvider : IDataProvider
    {
        public DataProvider()
        {
        }

        #region esTraceArguments

        private sealed class esTraceArguments : Tiraggo.Interfaces.ITraceArguments, IDisposable
        {
            static private long packetOrder = 0;

            private sealed class esTraceParameter : ITraceParameter
            {
                public string Name { get; set; }

                public string Direction { get; set; }

                public string ParamType { get; set; }

                public string BeforeValue { get; set; }

                public string AfterValue { get; set; }
            }

            public esTraceArguments()
            {
            }

            public esTraceArguments(tgDataRequest request, IDbCommand cmd, tgEntitySavePacket packet, string action, string callStack)
            {
                PacketOrder = Interlocked.Increment(ref esTraceArguments.packetOrder);

                this.command = cmd;

                TraceChannel = DataProvider.sTraceChannel;
                Syntax = "MYSQL";
                Request = request;
                ThreadId = Thread.CurrentThread.ManagedThreadId;
                Action = action;
                CallStack = callStack;
                SqlCommand = cmd;
                ApplicationName = System.IO.Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location);

                IDataParameterCollection parameters = cmd.Parameters;

                if (parameters.Count > 0)
                {
                    Parameters = new List<ITraceParameter>(parameters.Count);

                    for (int i = 0; i < parameters.Count; i++)
                    {
                        MySqlParameter param = parameters[i] as MySqlParameter;

                        esTraceParameter p = new esTraceParameter()
                        {
                            Name = param.ParameterName,
                            Direction = param.Direction.ToString(),
                            ParamType = param.MySqlDbType.ToString().ToUpper(),
                            BeforeValue = param.Value != null && param.Value != DBNull.Value ? Convert.ToString(param.Value) : "null"
                        };

                        try
                        {
                            // Let's make it look like we're using parameters for the profiler
                            if (param.Value == null || param.Value == DBNull.Value)
                            {
                                if (param.SourceVersion == DataRowVersion.Current || param.SourceVersion == DataRowVersion.Default)
                                {
                                    object o = packet.CurrentValues[param.SourceColumn];
                                    if (o != null && o != DBNull.Value)
                                    {
                                        p.BeforeValue = Convert.ToString(o);
                                    }
                                }
                                else if (param.SourceVersion == DataRowVersion.Original)
                                {
                                    object o = packet.OriginalValues[param.SourceColumn];
                                    if (o != null && o != DBNull.Value)
                                    {
                                        p.BeforeValue = Convert.ToString(o);
                                    }
                                }
                            }
                        }
                        catch { }

                        this.Parameters.Add(p);
                    }
                }

                stopwatch = Stopwatch.StartNew();
            }

            public esTraceArguments(tgDataRequest request, IDbCommand cmd, string action, string callStack)
            {
                PacketOrder = Interlocked.Increment(ref esTraceArguments.packetOrder);

                this.command = cmd;

                TraceChannel = DataProvider.sTraceChannel;
                Syntax = "MYSQL";
                Request = request;
                ThreadId = Thread.CurrentThread.ManagedThreadId;
                Action = action;
                CallStack = callStack;
                SqlCommand = cmd;
                ApplicationName = System.IO.Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location);

                IDataParameterCollection parameters = cmd.Parameters;

                if (parameters.Count > 0)
                {
                    Parameters = new List<ITraceParameter>(parameters.Count);

                    for (int i = 0; i < parameters.Count; i++)
                    {
                        MySqlParameter param = parameters[i] as MySqlParameter;

                        esTraceParameter p = new esTraceParameter()
                        {
                            Name = param.ParameterName,
                            Direction = param.Direction.ToString(),
                            ParamType = param.MySqlDbType.ToString().ToUpper(),
                            BeforeValue = param.Value != null && param.Value != DBNull.Value ? Convert.ToString(param.Value) : "null"
                        };

                        this.Parameters.Add(p);
                    }
                }

                stopwatch = Stopwatch.StartNew();
            }

            // Temporary variable
            private IDbCommand command;

            public long PacketOrder { get; set; }

            public string Syntax { get; set; }

            public tgDataRequest Request { get; set; }

            public int ThreadId { get; set; }

            public string Action { get; set; }

            public string CallStack { get; set; }

            public IDbCommand SqlCommand { get; set; }

            public string ApplicationName { get; set; }

            public string TraceChannel { get; set; }

            public long Duration { get; set; }

            public long Ticks { get; set; }

            public string Exception { get; set; }

            public List<ITraceParameter> Parameters { get; set; }

            private Stopwatch stopwatch;

            void IDisposable.Dispose()
            {
                stopwatch.Stop();
                Duration = stopwatch.ElapsedMilliseconds;
                Ticks = stopwatch.ElapsedTicks;

                // Gather Output Parameters
                if (this.Parameters != null && this.Parameters.Count > 0)
                {
                    IDataParameterCollection parameters = command.Parameters;

                    for (int i = 0; i < this.Parameters.Count; i++)
                    {
                        ITraceParameter esParam = this.Parameters[i];
                        IDbDataParameter param = parameters[esParam.Name] as IDbDataParameter;

                        if (param.Direction == ParameterDirection.InputOutput || param.Direction == ParameterDirection.Output)
                        {
                            esParam.AfterValue = param.Value != null ? Convert.ToString(param.Value) : "null";
                        }
                    }
                }

                DataProvider.sTraceHandler(this);
            }
        }

        #endregion esTraceArguments

        #region Profiling Logic

        /// <summary>
        /// The EventHandler used to decouple the profiling code from the core assemblies
        /// </summary>
        event TraceEventHandler IDataProvider.TraceHandler
        {
            add { DataProvider.sTraceHandler += value; }
            remove { DataProvider.sTraceHandler -= value; }
        }

        static private event TraceEventHandler sTraceHandler;

        /// <summary>
        /// Returns true if this Provider is current being profiled
        /// </summary>
        bool IDataProvider.IsTracing
        {
            get
            {
                return sTraceHandler != null ? true : false;
            }
        }

        /// <summary>
        /// Used to set the Channel this provider is to use during Profiling
        /// </summary>
        string IDataProvider.TraceChannel
        {
            get { return DataProvider.sTraceChannel; }
            set { DataProvider.sTraceChannel = value; }
        }

        static private string sTraceChannel = "Channel1";

        #endregion Profiling Logic

        /// <summary>
        /// This method acts as a delegate for tgTransactionScope
        /// </summary>
        /// <returns></returns>
        static private IDbConnection CreateIDbConnectionDelegate()
        {
            return new MySqlConnection();
        }

        static private void CleanupCommand(MySqlCommand cmd)
        {
            if (cmd != null && cmd.Connection != null)
            {
                if (cmd.Connection.State == ConnectionState.Open)
                {
                    cmd.Connection.Close();
                }
            }
        }

        #region IDataProvider Members

        tgDataResponse IDataProvider.esLoadDataTable(tgDataRequest request)
        {
            tgDataResponse response = new tgDataResponse();

            try
            {
                switch (request.QueryType)
                {
                    case tgQueryType.StoredProcedure:

                        response = LoadDataTableFromStoredProcedure(request);
                        break;

                    case tgQueryType.Text:

                        response = LoadDataTableFromText(request);
                        break;

                    case tgQueryType.DynamicQuery:

                        response = new tgDataResponse();
                        MySqlCommand cmd = QueryBuilder.PrepareCommand(request);
                        LoadDataTableFromDynamicQuery(request, response, cmd);
                        break;

                    case tgQueryType.DynamicQueryParseOnly:

                        response = new tgDataResponse();
                        MySqlCommand cmd1 = QueryBuilder.PrepareCommand(request);
                        response.LastQuery = cmd1.CommandText;
                        break;

                    case tgQueryType.ManyToMany:

                        response = LoadManyToMany(request);
                        break;

                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                response.Exception = ex;
            }

            return response;
        }

        tgDataResponse IDataProvider.esSaveDataTable(tgDataRequest request)
        {
            tgDataResponse response = new tgDataResponse();

            try
            {
                if (request.SqlAccessType == tgSqlAccessType.StoredProcedure)
                {
                    if (request.CollectionSavePacket != null)
                        SaveStoredProcCollection(request);
                    else
                        SaveStoredProcEntity(request);
                }
                else
                {
                    if (request.EntitySavePacket.CurrentValues == null)
                        SaveDynamicCollection(request);
                    else
                        SaveDynamicEntity(request);
                }
            }
            catch (MySqlException ex)
            {
                tgException es = Shared.CheckForConcurrencyException(ex);
                if (es != null)
                    response.Exception = es;
                else
                    response.Exception = ex;
            }
            catch (DBConcurrencyException dbex)
            {
                response.Exception = new tgConcurrencyException("Error in MySqlClientProvider.esSaveDataTable", dbex);
            }

            response.Table = request.Table;
            return response;
        }

        tgDataResponse IDataProvider.ExecuteNonQuery(tgDataRequest request)
        {
            tgDataResponse response = new tgDataResponse();
            MySqlCommand cmd = null;

            try
            {
                cmd = new MySqlCommand();
                if (request.CommandTimeout != null) cmd.CommandTimeout = request.CommandTimeout.Value;
                if (request.Parameters != null) Shared.AddParameters(cmd, request);

                switch (request.QueryType)
                {
                    case tgQueryType.TableDirect:
                        cmd.CommandType = CommandType.TableDirect;
                        cmd.CommandText = request.QueryText;
                        break;

                    case tgQueryType.StoredProcedure:
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.CommandText = request.QueryText;
                        break;

                    case tgQueryType.Text:
                        cmd.CommandType = CommandType.Text;
                        cmd.CommandText = request.QueryText;
                        break;
                }

                try
                {
                    tgTransactionScope.Enlist(cmd, request.ConnectionString, CreateIDbConnectionDelegate);

                    #region Profiling

                    if (sTraceHandler != null)
                    {
                        using (esTraceArguments esTrace = new esTraceArguments(request, cmd, "ExecuteNonQuery", System.Environment.StackTrace))
                        {
                            try
                            {
                                response.RowsEffected = cmd.ExecuteNonQuery();
                            }
                            catch (Exception ex)
                            {
                                esTrace.Exception = ex.Message;
                                throw;
                            }
                        }
                    }
                    else

                    #endregion Profiling

                    {
                        response.RowsEffected = cmd.ExecuteNonQuery();
                    }
                }
                finally
                {
                    tgTransactionScope.DeEnlist(cmd);
                }

                if (request.Parameters != null)
                {
                    Shared.GatherReturnParameters(cmd, request, response);
                }
            }
            catch (Exception ex)
            {
                CleanupCommand(cmd);
                response.Exception = ex;
            }

            return response;
        }

        tgDataResponse IDataProvider.ExecuteReader(tgDataRequest request)
        {
            tgDataResponse response = new tgDataResponse();
            MySqlCommand cmd = null;

            try
            {
                cmd = new MySqlCommand();
                if (request.CommandTimeout != null) cmd.CommandTimeout = request.CommandTimeout.Value;
                if (request.Parameters != null) Shared.AddParameters(cmd, request);

                switch (request.QueryType)
                {
                    case tgQueryType.TableDirect:
                        cmd.CommandType = CommandType.TableDirect;
                        cmd.CommandText = request.QueryText;
                        break;

                    case tgQueryType.StoredProcedure:
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.CommandText = request.QueryText;
                        break;

                    case tgQueryType.Text:
                        cmd.CommandType = CommandType.Text;
                        cmd.CommandText = request.QueryText;
                        break;

                    case tgQueryType.DynamicQuery:
                        cmd = QueryBuilder.PrepareCommand(request);
                        break;
                }

                cmd.Connection = new MySqlConnection(request.ConnectionString);
                cmd.Connection.Open();

                #region Profiling

                if (sTraceHandler != null)
                {
                    using (esTraceArguments esTrace = new esTraceArguments(request, cmd, "ExecuteReader", System.Environment.StackTrace))
                    {
                        try
                        {
                            response.DataReader = cmd.ExecuteReader(CommandBehavior.CloseConnection);
                        }
                        catch (Exception ex)
                        {
                            esTrace.Exception = ex.Message;
                            throw;
                        }
                    }
                }
                else

                #endregion Profiling

                {
                    response.DataReader = cmd.ExecuteReader(CommandBehavior.CloseConnection);
                }
            }
            catch (Exception ex)
            {
                CleanupCommand(cmd);
                response.Exception = ex;
            }

            return response;
        }

        tgDataResponse IDataProvider.ExecuteScalar(tgDataRequest request)
        {
            tgDataResponse response = new tgDataResponse();
            MySqlCommand cmd = null;

            try
            {
                cmd = new MySqlCommand();
                if (request.CommandTimeout != null) cmd.CommandTimeout = request.CommandTimeout.Value;
                if (request.Parameters != null) Shared.AddParameters(cmd, request);

                switch (request.QueryType)
                {
                    case tgQueryType.TableDirect:
                        cmd.CommandType = CommandType.TableDirect;
                        cmd.CommandText = request.QueryText;
                        break;

                    case tgQueryType.StoredProcedure:
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.CommandText = request.QueryText;
                        break;

                    case tgQueryType.Text:
                        cmd.CommandType = CommandType.Text;
                        cmd.CommandText = request.QueryText;
                        break;

                    case tgQueryType.DynamicQuery:
                        cmd = QueryBuilder.PrepareCommand(request);
                        break;
                }

                try
                {
                    tgTransactionScope.Enlist(cmd, request.ConnectionString, CreateIDbConnectionDelegate);

                    #region Profiling

                    if (sTraceHandler != null)
                    {
                        using (esTraceArguments esTrace = new esTraceArguments(request, cmd, "ExecuteScalar", System.Environment.StackTrace))
                        {
                            try
                            {
                                response.Scalar = cmd.ExecuteScalar();
                            }
                            catch (Exception ex)
                            {
                                esTrace.Exception = ex.Message;
                                throw;
                            }
                        }
                    }
                    else

                    #endregion Profiling

                    {
                        response.Scalar = cmd.ExecuteScalar();
                    }
                }
                finally
                {
                    tgTransactionScope.DeEnlist(cmd);
                }

                if (request.Parameters != null)
                {
                    Shared.GatherReturnParameters(cmd, request, response);
                }
            }
            catch (Exception ex)
            {
                CleanupCommand(cmd);
                response.Exception = ex;
            }

            return response;
        }

        tgDataResponse IDataProvider.FillDataSet(tgDataRequest request)
        {
            tgDataResponse response = new tgDataResponse();

            try
            {
                switch (request.QueryType)
                {
                    case tgQueryType.StoredProcedure:

                        response = LoadDataSetFromStoredProcedure(request);
                        break;

                    case tgQueryType.Text:

                        response = LoadDataSetFromText(request);
                        break;

                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                response.Exception = ex;
            }

            return response;
        }

        tgDataResponse IDataProvider.FillDataTable(tgDataRequest request)
        {
            tgDataResponse response = new tgDataResponse();

            try
            {
                switch (request.QueryType)
                {
                    case tgQueryType.StoredProcedure:

                        response = LoadDataTableFromStoredProcedure(request);
                        break;

                    case tgQueryType.Text:

                        response = LoadDataTableFromText(request);
                        break;

                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                response.Exception = ex;
            }

            return response;
        }

        #endregion IDataProvider Members

        static private tgDataResponse LoadDataSetFromStoredProcedure(tgDataRequest request)
        {
            tgDataResponse response = new tgDataResponse();
            MySqlCommand cmd = null;

            try
            {
                DataSet dataSet = new DataSet();

                cmd = new MySqlCommand();
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = request.QueryText;

                if (request.CommandTimeout != null) cmd.CommandTimeout = request.CommandTimeout.Value;
                if (request.Parameters != null) Shared.AddParameters(cmd, request);

                MySqlDataAdapter da = new MySqlDataAdapter();
                da.SelectCommand = cmd;

                try
                {
                    tgTransactionScope.Enlist(da.SelectCommand, request.ConnectionString, CreateIDbConnectionDelegate);

                    #region Profiling

                    if (sTraceHandler != null)
                    {
                        using (esTraceArguments esTrace = new esTraceArguments(request, cmd, "LoadFromStoredProcedure", System.Environment.StackTrace))
                        {
                            try
                            {
                                da.Fill(dataSet);
                            }
                            catch (Exception ex)
                            {
                                esTrace.Exception = ex.Message;
                                throw;
                            }
                        }
                    }
                    else

                    #endregion Profiling

                    {
                        da.Fill(dataSet);
                    }
                }
                finally
                {
                    tgTransactionScope.DeEnlist(da.SelectCommand);
                }

                response.DataSet = dataSet;

                if (request.Parameters != null)
                {
                    Shared.GatherReturnParameters(cmd, request, response);
                }
            }
            catch (Exception)
            {
                CleanupCommand(cmd);
                throw;
            }
            finally
            {
            }

            return response;
        }

        static private tgDataResponse LoadDataSetFromText(tgDataRequest request)
        {
            tgDataResponse response = new tgDataResponse();
            MySqlCommand cmd = null;

            try
            {
                DataSet dataSet = new DataSet();

                cmd = new MySqlCommand();
                cmd.CommandType = CommandType.Text;
                if (request.CommandTimeout != null) cmd.CommandTimeout = request.CommandTimeout.Value;
                if (request.Parameters != null) Shared.AddParameters(cmd, request);

                MySqlDataAdapter da = new MySqlDataAdapter();
                cmd.CommandText = request.QueryText;
                da.SelectCommand = cmd;

                try
                {
                    tgTransactionScope.Enlist(da.SelectCommand, request.ConnectionString, CreateIDbConnectionDelegate);

                    #region Profiling

                    if (sTraceHandler != null)
                    {
                        using (esTraceArguments esTrace = new esTraceArguments(request, cmd, "LoadDataSetFromText", System.Environment.StackTrace))
                        {
                            try
                            {
                                da.Fill(dataSet);
                            }
                            catch (Exception ex)
                            {
                                esTrace.Exception = ex.Message;
                                throw;
                            }
                        }
                    }
                    else

                    #endregion Profiling

                    {
                        da.Fill(dataSet);
                    }
                }
                finally
                {
                    tgTransactionScope.DeEnlist(da.SelectCommand);
                }

                response.DataSet = dataSet;

                if (request.Parameters != null)
                {
                    Shared.GatherReturnParameters(cmd, request, response);
                }
            }
            catch (Exception)
            {
                CleanupCommand(cmd);
                throw;
            }
            finally
            {
            }

            return response;
        }

        static private tgDataResponse LoadDataTableFromStoredProcedure(tgDataRequest request)
        {
            tgDataResponse response = new tgDataResponse();
            MySqlCommand cmd = null;

            try
            {
                DataTable dataTable = new DataTable(request.ProviderMetadata.Destination);

                cmd = new MySqlCommand();
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = request.QueryText;
                if (request.CommandTimeout != null) cmd.CommandTimeout = request.CommandTimeout.Value;
                if (request.Parameters != null) Shared.AddParameters(cmd, request);

                MySqlDataAdapter da = new MySqlDataAdapter();
                da.SelectCommand = cmd;

                try
                {
                    tgTransactionScope.Enlist(da.SelectCommand, request.ConnectionString, CreateIDbConnectionDelegate);

                    #region Profiling

                    if (sTraceHandler != null)
                    {
                        using (esTraceArguments esTrace = new esTraceArguments(request, cmd, "LoadFromStoredProcedure", System.Environment.StackTrace))
                        {
                            try
                            {
                                da.Fill(dataTable);
                            }
                            catch (Exception ex)
                            {
                                esTrace.Exception = ex.Message;
                                throw;
                            }
                        }
                    }
                    else

                    #endregion Profiling

                    {
                        da.Fill(dataTable);
                    }
                }
                finally
                {
                    tgTransactionScope.DeEnlist(da.SelectCommand);
                }

                response.Table = dataTable;

                if (request.Parameters != null)
                {
                    Shared.GatherReturnParameters(cmd, request, response);
                }
            }
            catch (Exception)
            {
                CleanupCommand(cmd);
                throw;
            }
            finally
            {
            }

            return response;
        }

        static private tgDataResponse LoadDataTableFromText(tgDataRequest request)
        {
            tgDataResponse response = new tgDataResponse();
            MySqlCommand cmd = null;

            try
            {
                DataTable dataTable = new DataTable(request.ProviderMetadata.Destination);

                cmd = new MySqlCommand();
                cmd.CommandType = CommandType.Text;
                if (request.CommandTimeout != null) cmd.CommandTimeout = request.CommandTimeout.Value;
                if (request.Parameters != null) Shared.AddParameters(cmd, request);

                MySqlDataAdapter da = new MySqlDataAdapter();
                cmd.CommandText = request.QueryText;
                da.SelectCommand = cmd;

                try
                {
                    tgTransactionScope.Enlist(da.SelectCommand, request.ConnectionString, CreateIDbConnectionDelegate);

                    #region Profiling

                    if (sTraceHandler != null)
                    {
                        using (esTraceArguments esTrace = new esTraceArguments(request, cmd, "LoadFromText", System.Environment.StackTrace))
                        {
                            try
                            {
                                da.Fill(dataTable);
                            }
                            catch (Exception ex)
                            {
                                esTrace.Exception = ex.Message;
                                throw;
                            }
                        }
                    }
                    else

                    #endregion Profiling

                    {
                        da.Fill(dataTable);
                    }
                }
                finally
                {
                    tgTransactionScope.DeEnlist(da.SelectCommand);
                }

                response.Table = dataTable;

                if (request.Parameters != null)
                {
                    Shared.GatherReturnParameters(cmd, request, response);
                }
            }
            catch (Exception)
            {
                CleanupCommand(cmd);
                throw;
            }
            finally
            {
            }

            return response;
        }

        static private tgDataResponse LoadManyToMany(tgDataRequest request)
        {
            tgDataResponse response = new tgDataResponse();
            MySqlCommand cmd = null;

            try
            {
                DataTable dataTable = new DataTable(request.ProviderMetadata.Destination);

                cmd = new MySqlCommand();
                cmd.CommandType = CommandType.Text;
                if (request.CommandTimeout != null) cmd.CommandTimeout = request.CommandTimeout.Value;

                string mmQuery = request.QueryText;

                string[] sections = mmQuery.Split('|');
                string[] tables = sections[0].Split(',');
                string[] columns = sections[1].Split(',');

                // We build the query, we don't use Delimiters to avoid tons of extra concatentation
                string sql = "SELECT * FROM `" + tables[0];
                sql += "` JOIN `" + tables[1] + "` ON `" + tables[0] + "`.`" + columns[0] + "` = `";
                sql += tables[1] + "`.`" + columns[1];
                sql += "` WHERE `" + tables[1] + "`.`" + sections[2] + "` = ?";

                if (request.Parameters != null)
                {
                    foreach (esParameter esParam in request.Parameters)
                    {
                        sql += esParam.Name;
                    }

                    Shared.AddParameters(cmd, request);
                }

                MySqlDataAdapter da = new MySqlDataAdapter();
                cmd.CommandText = sql;

                da.SelectCommand = cmd;

                try
                {
                    tgTransactionScope.Enlist(da.SelectCommand, request.ConnectionString, CreateIDbConnectionDelegate);

                    #region Profiling

                    if (sTraceHandler != null)
                    {
                        using (esTraceArguments esTrace = new esTraceArguments(request, cmd, "LoadManyToMany", System.Environment.StackTrace))
                        {
                            try
                            {
                                da.Fill(dataTable);
                            }
                            catch (Exception ex)
                            {
                                esTrace.Exception = ex.Message;
                                throw;
                            }
                        }
                    }
                    else

                    #endregion Profiling

                    {
                        da.Fill(dataTable);
                    }
                }
                finally
                {
                    tgTransactionScope.DeEnlist(da.SelectCommand);
                }

                response.Table = dataTable;
            }
            catch (Exception)
            {
                CleanupCommand(cmd);
                throw;
            }
            finally
            {
            }

            return response;
        }

        // This is used only to execute the Dynamic Query API
        static private void LoadDataTableFromDynamicQuery(tgDataRequest request, tgDataResponse response, MySqlCommand cmd)
        {
            try
            {
                response.LastQuery = cmd.CommandText;

                if (request.CommandTimeout != null) cmd.CommandTimeout = request.CommandTimeout.Value;

                DataTable dataTable = new DataTable(request.ProviderMetadata.Destination);

                MySqlDataAdapter da = new MySqlDataAdapter();
                da.SelectCommand = cmd;

                try
                {
                    tgTransactionScope.Enlist(da.SelectCommand, request.ConnectionString, CreateIDbConnectionDelegate);

                    #region Profiling

                    if (sTraceHandler != null)
                    {
                        using (esTraceArguments esTrace = new esTraceArguments(request, cmd, "LoadFromDynamicQuery", System.Environment.StackTrace))
                        {
                            try
                            {
                                da.Fill(dataTable);
                            }
                            catch (Exception ex)
                            {
                                esTrace.Exception = ex.Message;
                                throw;
                            }
                        }
                    }
                    else

                    #endregion Profiling

                    {
                        da.Fill(dataTable);
                    }
                }
                finally
                {
                    tgTransactionScope.DeEnlist(da.SelectCommand);
                }

                response.Table = dataTable;
            }
            catch (Exception)
            {
                CleanupCommand(cmd);
                throw;
            }
            finally
            {
            }
        }

        static private DataTable SaveStoredProcCollection(tgDataRequest request)
        {
            bool needToInsert = false;
            bool needToUpdate = false;
            bool needToDelete = false;

            Dictionary<DataRow, tgEntitySavePacket> rowMapping = null;

            if (request.ContinueUpdateOnError)
            {
                rowMapping = new Dictionary<DataRow, tgEntitySavePacket>();
            }

            //================================================
            // Create the DataTable ...
            //================================================
            DataTable dataTable = CreateDataTable(request);

            foreach (tgEntitySavePacket packet in request.CollectionSavePacket)
            {
                DataRow row = dataTable.NewRow();

                switch (request.EntitySavePacket.RowState)
                {
                    case tgDataRowState.Added:
                        SetModifiedValues(request, packet, row);
                        dataTable.Rows.Add(row);
                        if (request.ContinueUpdateOnError) rowMapping[row] = packet;
                        break;

                    case tgDataRowState.Modified:
                        SetOriginalValues(request, packet, row, false);
                        SetModifiedValues(request, packet, row);
                        dataTable.Rows.Add(row);
                        row.AcceptChanges();
                        row.SetModified();
                        if (request.ContinueUpdateOnError) rowMapping[row] = packet;
                        break;

                    case tgDataRowState.Deleted:
                        SetOriginalValues(request, packet, row, true);
                        dataTable.Rows.Add(row);
                        row.AcceptChanges();
                        row.Delete();
                        if (request.ContinueUpdateOnError) rowMapping[row] = packet;
                        break;
                }
            }

            if (Shared.HasUpdates(dataTable.Rows, out needToInsert, out needToUpdate, out needToDelete))
            {
                using (MySqlDataAdapter da = new MySqlDataAdapter())
                {
                    da.AcceptChangesDuringUpdate = false;

                    MySqlCommand cmd = null;

                    if (needToInsert) da.InsertCommand = cmd = Shared.BuildStoredProcInsertCommand(request);
                    if (needToUpdate) da.UpdateCommand = cmd = Shared.BuildStoredProcUpdateCommand(request);
                    if (needToDelete) da.DeleteCommand = cmd = Shared.BuildStoredProcDeleteCommand(request);

                    using (tgTransactionScope scope = new tgTransactionScope())
                    {
                        if (needToInsert) tgTransactionScope.Enlist(da.InsertCommand, request.ConnectionString, CreateIDbConnectionDelegate);
                        if (needToUpdate) tgTransactionScope.Enlist(da.UpdateCommand, request.ConnectionString, CreateIDbConnectionDelegate);
                        if (needToDelete) tgTransactionScope.Enlist(da.DeleteCommand, request.ConnectionString, CreateIDbConnectionDelegate);

                        try
                        {
                            #region Profiling

                            if (sTraceHandler != null)
                            {
                                using (esTraceArguments esTrace = new esTraceArguments(request, cmd, "SaveCollectionStoredProcedure", System.Environment.StackTrace))
                                {
                                    try
                                    {
                                        da.Update(dataTable);
                                    }
                                    catch (Exception ex)
                                    {
                                        esTrace.Exception = ex.Message;
                                        throw;
                                    }
                                }
                            }
                            else

                            #endregion Profiling

                            {
                                da.Update(dataTable);
                            }
                        }
                        finally
                        {
                            if (needToInsert) tgTransactionScope.DeEnlist(da.InsertCommand);
                            if (needToUpdate) tgTransactionScope.DeEnlist(da.UpdateCommand);
                            if (needToDelete) tgTransactionScope.DeEnlist(da.DeleteCommand);
                        }

                        scope.Complete();
                    }
                }

                if (request.ContinueUpdateOnError && dataTable.HasErrors)
                {
                    DataRow[] errors = dataTable.GetErrors();

                    foreach (DataRow rowWithError in errors)
                    {
                        request.FireOnError(rowMapping[rowWithError], rowWithError.RowError);
                    }
                }
            }

            return request.Table;
        }

        static private DataTable SaveStoredProcEntity(tgDataRequest request)
        {
            bool needToDelete = request.EntitySavePacket.RowState == tgDataRowState.Deleted;

            DataTable dataTable = CreateDataTable(request);

            using (MySqlDataAdapter da = new MySqlDataAdapter())
            {
                da.AcceptChangesDuringUpdate = false;

                DataRow row = dataTable.NewRow();
                dataTable.Rows.Add(row);

                MySqlCommand cmd = null;

                switch (request.EntitySavePacket.RowState)
                {
                    case tgDataRowState.Added:
                        cmd = da.InsertCommand = Shared.BuildStoredProcInsertCommand(request);
                        SetModifiedValues(request, request.EntitySavePacket, row);
                        break;

                    case tgDataRowState.Modified:
                        cmd = da.UpdateCommand = Shared.BuildStoredProcUpdateCommand(request);
                        SetOriginalValues(request, request.EntitySavePacket, row, false);
                        SetModifiedValues(request, request.EntitySavePacket, row);
                        row.AcceptChanges();
                        row.SetModified();
                        break;

                    case tgDataRowState.Deleted:
                        cmd = da.DeleteCommand = Shared.BuildStoredProcDeleteCommand(request);
                        SetOriginalValues(request, request.EntitySavePacket, row, true);
                        row.AcceptChanges();
                        row.Delete();
                        break;
                }

                DataRow[] singleRow = new DataRow[1];
                singleRow[0] = row;

                tgTransactionScope.Enlist(cmd, request.ConnectionString, CreateIDbConnectionDelegate);

                try
                {
                    #region Profiling

                    if (sTraceHandler != null)
                    {
                        using (esTraceArguments esTrace = new esTraceArguments(request, cmd, "SaveEntityStoredProcedure", System.Environment.StackTrace))
                        {
                            try
                            {
                                da.Update(singleRow);
                            }
                            catch (Exception ex)
                            {
                                esTrace.Exception = ex.Message;
                                throw;
                            }
                        }
                    }
                    else

                    #endregion Profiling

                    {
                        da.Update(singleRow);
                    }
                }
                finally
                {
                    tgTransactionScope.DeEnlist(cmd);
                }

                if (cmd.Parameters != null)
                {
                    foreach (MySqlParameter param in cmd.Parameters)
                    {
                        switch (param.Direction)
                        {
                            case ParameterDirection.Output:
                            case ParameterDirection.InputOutput:

                                request.EntitySavePacket.CurrentValues[param.SourceColumn] = param.Value;
                                break;
                        }
                    }
                }
            }

            return dataTable;
        }

        static private DataTable SaveDynamicCollection(tgDataRequest request)
        {
            tgEntitySavePacket pkt = request.CollectionSavePacket[0];

            if (pkt.RowState == tgDataRowState.Deleted)
            {
                //============================================================================
                // We do all our deletes at once, so if the first one is a delete they all are
                //============================================================================
                return SaveDynamicCollection_Deletes(request);
            }
            else
            {
                //============================================================================
                // We do all our Inserts and Updates at once
                //============================================================================
                return SaveDynamicCollection_InsertsUpdates(request);
            }
        }

        static private DataTable SaveDynamicCollection_InsertsUpdates(tgDataRequest request)
        {
            DataTable dataTable = CreateDataTable(request);

            using (tgTransactionScope scope = new tgTransactionScope())
            {
                using (MySqlDataAdapter da = new MySqlDataAdapter())
                {
                    da.AcceptChangesDuringUpdate = false;
                    da.ContinueUpdateOnError = request.ContinueUpdateOnError;

                    MySqlCommand cmd = null;

                    if (!request.IgnoreComputedColumns)
                    {
                        da.RowUpdated += new MySqlRowUpdatedEventHandler(OnRowUpdated);
                    }

                    foreach (tgEntitySavePacket packet in request.CollectionSavePacket)
                    {
                        if (packet.RowState != tgDataRowState.Added && packet.RowState != tgDataRowState.Modified) continue;

                        DataRow row = dataTable.NewRow();
                        dataTable.Rows.Add(row);

                        switch (packet.RowState)
                        {
                            case tgDataRowState.Added:
                                cmd = da.InsertCommand = Shared.BuildDynamicInsertCommand(request, packet.ModifiedColumns);
                                SetModifiedValues(request, packet, row);
                                break;

                            case tgDataRowState.Modified:
                                cmd = da.UpdateCommand = Shared.BuildDynamicUpdateCommand(request, packet.ModifiedColumns);
                                SetOriginalValues(request, packet, row, false);
                                SetModifiedValues(request, packet, row);
                                row.AcceptChanges();
                                row.SetModified();
                                break;
                        }

                        request.Properties["tgDataRequest"] = request;
                        request.Properties["esEntityData"] = packet;
                        dataTable.ExtendedProperties["props"] = request.Properties;

                        DataRow[] singleRow = new DataRow[1];
                        singleRow[0] = row;

                        try
                        {
                            tgTransactionScope.Enlist(cmd, request.ConnectionString, CreateIDbConnectionDelegate);

                            #region Profiling

                            if (sTraceHandler != null)
                            {
                                using (esTraceArguments esTrace = new esTraceArguments(request, cmd, packet, "SaveCollectionDynamic", System.Environment.StackTrace))
                                {
                                    try
                                    {
                                        da.Update(singleRow);
                                    }
                                    catch (Exception ex)
                                    {
                                        esTrace.Exception = ex.Message;
                                        throw;
                                    }
                                }
                            }
                            else

                            #endregion Profiling

                            {
                                da.Update(singleRow);
                            }

                            if (row.HasErrors)
                            {
                                request.FireOnError(packet, row.RowError);
                            }
                        }
                        finally
                        {
                            tgTransactionScope.DeEnlist(cmd);
                            dataTable.Rows.Clear();
                        }

                        if (!row.HasErrors && packet.RowState != tgDataRowState.Deleted && cmd.Parameters != null)
                        {
                            foreach (MySqlParameter param in cmd.Parameters)
                            {
                                switch (param.Direction)
                                {
                                    case ParameterDirection.Output:
                                    case ParameterDirection.InputOutput:

                                        packet.CurrentValues[param.SourceColumn] = param.Value;
                                        break;
                                }
                            }
                        }
                    }
                }

                scope.Complete();
            }

            return dataTable;
        }

        static private DataTable SaveDynamicCollection_Deletes(tgDataRequest request)
        {
            MySqlCommand cmd = null;

            DataTable dataTable = CreateDataTable(request);

            using (tgTransactionScope scope = new tgTransactionScope())
            {
                using (MySqlDataAdapter da = new MySqlDataAdapter())
                {
                    da.AcceptChangesDuringUpdate = false;
                    da.ContinueUpdateOnError = request.ContinueUpdateOnError;

                    try
                    {
                        cmd = da.DeleteCommand = Shared.BuildDynamicDeleteCommand(request, request.CollectionSavePacket[0].ModifiedColumns);
                        tgTransactionScope.Enlist(cmd, request.ConnectionString, CreateIDbConnectionDelegate);

                        DataRow[] singleRow = new DataRow[1];

                        // Delete each record
                        foreach (tgEntitySavePacket packet in request.CollectionSavePacket)
                        {
                            DataRow row = dataTable.NewRow();
                            dataTable.Rows.Add(row);

                            SetOriginalValues(request, packet, row, true);
                            row.AcceptChanges();
                            row.Delete();

                            singleRow[0] = row;

                            #region Profiling

                            if (sTraceHandler != null)
                            {
                                using (esTraceArguments esTrace = new esTraceArguments(request, cmd, packet, "SaveCollectionDynamic", System.Environment.StackTrace))
                                {
                                    try
                                    {
                                        da.Update(singleRow);
                                    }
                                    catch (Exception ex)
                                    {
                                        esTrace.Exception = ex.Message;
                                        throw;
                                    }
                                }
                            }
                            else

                            #endregion Profiling

                            {
                                da.Update(singleRow);
                            }

                            if (row.HasErrors)
                            {
                                request.FireOnError(packet, row.RowError);
                            }

                            dataTable.Rows.Clear(); // ADO.NET won't let us reuse the same DataRow
                        }
                    }
                    finally
                    {
                        tgTransactionScope.DeEnlist(cmd);
                    }
                }
                scope.Complete();
            }

            return request.Table;
        }

        static private DataTable SaveDynamicEntity(tgDataRequest request)
        {
            bool needToDelete = request.EntitySavePacket.RowState == tgDataRowState.Deleted;

            DataTable dataTable = CreateDataTable(request);

            using (MySqlDataAdapter da = new MySqlDataAdapter())
            {
                da.AcceptChangesDuringUpdate = false;

                DataRow row = dataTable.NewRow();
                dataTable.Rows.Add(row);

                MySqlCommand cmd = null;

                switch (request.EntitySavePacket.RowState)
                {
                    case tgDataRowState.Added:
                        cmd = da.InsertCommand = Shared.BuildDynamicInsertCommand(request, request.EntitySavePacket.ModifiedColumns);
                        SetModifiedValues(request, request.EntitySavePacket, row);
                        break;

                    case tgDataRowState.Modified:
                        cmd = da.UpdateCommand = Shared.BuildDynamicUpdateCommand(request, request.EntitySavePacket.ModifiedColumns);
                        SetOriginalValues(request, request.EntitySavePacket, row, false);
                        SetModifiedValues(request, request.EntitySavePacket, row);
                        row.AcceptChanges();
                        row.SetModified();
                        break;

                    case tgDataRowState.Deleted:
                        cmd = da.DeleteCommand = Shared.BuildDynamicDeleteCommand(request, null);
                        SetOriginalValues(request, request.EntitySavePacket, row, true);
                        row.AcceptChanges();
                        row.Delete();
                        break;
                }

                if (!needToDelete && request.Properties != null)
                {
                    request.Properties["tgDataRequest"] = request;
                    request.Properties["esEntityData"] = request.EntitySavePacket;
                    dataTable.ExtendedProperties["props"] = request.Properties;
                }

                DataRow[] singleRow = new DataRow[1];
                singleRow[0] = row;

                if (!request.IgnoreComputedColumns)
                {
                    da.RowUpdated += new MySqlRowUpdatedEventHandler(OnRowUpdated);
                }

                try
                {
                    tgTransactionScope.Enlist(cmd, request.ConnectionString, CreateIDbConnectionDelegate);

                    #region Profiling

                    if (sTraceHandler != null)
                    {
                        using (esTraceArguments esTrace = new esTraceArguments(request, cmd, request.EntitySavePacket, "SaveEntityDynamic", System.Environment.StackTrace))
                        {
                            try
                            {
                                da.Update(singleRow);
                            }
                            catch (Exception ex)
                            {
                                esTrace.Exception = ex.Message;
                                throw;
                            }
                        }
                    }
                    else

                    #endregion Profiling

                    {
                        da.Update(singleRow);
                    }
                }
                finally
                {
                    tgTransactionScope.DeEnlist(cmd);
                }

                if (request.EntitySavePacket.RowState != tgDataRowState.Deleted && cmd.Parameters != null)
                {
                    foreach (MySqlParameter param in cmd.Parameters)
                    {
                        switch (param.Direction)
                        {
                            case ParameterDirection.Output:
                            case ParameterDirection.InputOutput:

                                request.EntitySavePacket.CurrentValues[param.SourceColumn] = param.Value;
                                break;
                        }
                    }
                }
            }

            return dataTable;
        }

        static private DataTable CreateDataTable(tgDataRequest request)
        {
            DataTable dataTable = new DataTable();
            DataColumnCollection dataColumns = dataTable.Columns;
            tgColumnMetadataCollection cols = request.Columns;

            if (request.SelectedColumns == null)
            {
                tgColumnMetadata col;
                for (int i = 0; i < cols.Count; i++)
                {
                    col = cols[i];
                    dataColumns.Add(new DataColumn(col.Name, col.Type));
                }
            }
            else
            {
                foreach (string col in request.SelectedColumns.Keys)
                {
                    dataColumns.Add(new DataColumn(col, cols[col].Type));
                }
            }

            return dataTable;
        }

        private static void SetOriginalValues(tgDataRequest request, tgEntitySavePacket packet, DataRow row, bool primaryKeysAndConcurrencyOnly)
        {
            foreach (tgColumnMetadata col in request.Columns)
            {
                if (primaryKeysAndConcurrencyOnly &&
                    (!col.IsInPrimaryKey && !col.IsConcurrency && !col.IsEntitySpacesConcurrency)) continue;

                string columnName = col.Name;

                if (packet.OriginalValues.ContainsKey(columnName))
                {
                    row[columnName] = packet.OriginalValues[columnName];
                }
            }
        }

        private static void SetModifiedValues(tgDataRequest request, tgEntitySavePacket packet, DataRow row)
        {
            foreach (string column in packet.ModifiedColumns)
            {
                if (request.Columns.FindByColumnName(column) != null)
                {
                    row[column] = packet.CurrentValues[column];
                }
            }
        }

        protected static void OnRowUpdated(object sender, MySqlRowUpdatedEventArgs e)
        {
            try
            {
                PropertyCollection props = e.Row.Table.ExtendedProperties;
                if (props.ContainsKey("props"))
                {
                    props = (PropertyCollection)props["props"];
                }

                if (e.Status == UpdateStatus.Continue && (e.StatementType == StatementType.Insert || e.StatementType == StatementType.Update))
                {
                    tgDataRequest request = props["tgDataRequest"] as tgDataRequest;
                    tgEntitySavePacket packet = (tgEntitySavePacket)props["esEntityData"];
                    string source = props["Source"] as string;

                    if (e.StatementType == StatementType.Insert)
                    {
                        if (props.Contains("AutoInc"))
                        {
                            string autoInc = props["AutoInc"] as string;

                            MySqlCommand cmd = new MySqlCommand();
                            cmd.Connection = e.Command.Connection;
                            cmd.Transaction = e.Command.Transaction;
                            cmd.CommandText = "SELECT LAST_INSERT_ID();";

                            object o = null;

                            #region Profiling

                            if (sTraceHandler != null)
                            {
                                using (esTraceArguments esTrace = new esTraceArguments(request, cmd, "OnRowUpdated", System.Environment.StackTrace))
                                {
                                    try
                                    {
                                        o = cmd.ExecuteScalar();
                                    }
                                    catch (Exception ex)
                                    {
                                        esTrace.Exception = ex.Message;
                                        throw;
                                    }
                                }
                            }
                            else

                            #endregion Profiling

                            {
                                o = cmd.ExecuteScalar();
                            }

                            if (o != null)
                            {
                                e.Row[autoInc] = o;
                                e.Command.Parameters["?" + autoInc].Value = o;
                            }
                        }

                        if (props.Contains("EntitySpacesConcurrency"))
                        {
                            string esConcurrencyColumn = props["EntitySpacesConcurrency"] as string;
                            packet.CurrentValues[esConcurrencyColumn] = 1;
                        }
                    }

                    if (props.Contains("Timestamp"))
                    {
                        string column = props["Timestamp"] as string;

                        MySqlCommand cmd = new MySqlCommand();
                        cmd.Connection = e.Command.Connection;
                        cmd.Transaction = e.Command.Transaction;
                        cmd.CommandText = "SELECT LastTimestamp('" + source + "');";

                        object o = null;

                        #region Profiling

                        if (sTraceHandler != null)
                        {
                            using (esTraceArguments esTrace = new esTraceArguments(request, cmd, "OnRowUpdated", System.Environment.StackTrace))
                            {
                                try
                                {
                                    o = cmd.ExecuteScalar();
                                }
                                catch (Exception ex)
                                {
                                    esTrace.Exception = ex.Message;
                                    throw;
                                }
                            }
                        }
                        else

                        #endregion Profiling

                        {
                            o = cmd.ExecuteScalar();
                        }

                        if (o != null)
                        {
                            e.Command.Parameters["?" + column].Value = o;
                        }
                    }

                    //-------------------------------------------------------------------------------------------------
                    // Fetch any defaults, SQLite doesn't support output parameters so we gotta do this the hard way
                    //-------------------------------------------------------------------------------------------------
                    if (props.Contains("Defaults"))
                    {
                        // Build the Where parameter and parameters
                        MySqlCommand cmd = new MySqlCommand();
                        cmd.Connection = e.Command.Connection;
                        cmd.Transaction = e.Command.Transaction;

                        string select = (string)props["Defaults"];

                        string[] whereParameters = ((string)props["Where"]).Split(',');

                        string comma = String.Empty;
                        string where = String.Empty;
                        int i = 1;
                        foreach (string parameter in whereParameters)
                        {
                            MySqlParameter p = new MySqlParameter("?p" + i++.ToString(), e.Row[parameter]);
                            cmd.Parameters.Add(p);
                            where += comma + "`" + parameter + "` = " + p.ParameterName;
                            comma = " AND ";
                        }

                        // Okay, now we can execute the sql and get any values that have defaults that were
                        // null at the time of the insert and/or our timestamp
                        cmd.CommandText = "SELECT " + select + " FROM `" + request.ProviderMetadata.Source + "` WHERE " + where + ";";

                        MySqlDataReader rdr = null;

                        try
                        {
                            #region Profiling

                            if (sTraceHandler != null)
                            {
                                using (esTraceArguments esTrace = new esTraceArguments(request, cmd, "OnRowUpdated", System.Environment.StackTrace))
                                {
                                    try
                                    {
                                        rdr = cmd.ExecuteReader(CommandBehavior.SingleResult);
                                    }
                                    catch (Exception ex)
                                    {
                                        esTrace.Exception = ex.Message;
                                        throw;
                                    }
                                }
                            }
                            else

                            #endregion Profiling

                            {
                                rdr = cmd.ExecuteReader(CommandBehavior.SingleResult);
                            }

                            if (rdr.Read())
                            {
                                select = select.Replace("`", String.Empty).Replace("`", String.Empty);
                                string[] selectCols = select.Split(',');

                                for (int k = 0; k < selectCols.Length; k++)
                                {
                                    packet.CurrentValues[selectCols[k]] = rdr.GetValue(k);
                                }
                            }
                        }
                        finally
                        {
                            // Make sure we close the reader no matter what
                            if (rdr != null) rdr.Close();
                        }
                    }

                    if (e.StatementType == StatementType.Update)
                    {
                        string colName = props["EntitySpacesConcurrency"] as string;
                        object o = e.Row[colName];

                        MySqlParameter p = e.Command.Parameters["?" + colName];
                        object v = null;

                        switch (Type.GetTypeCode(o.GetType()))
                        {
                            case TypeCode.Int16: v = ((System.Int16)o) + 1; break;
                            case TypeCode.Int32: v = ((System.Int32)o) + 1; break;
                            case TypeCode.Int64: v = ((System.Int64)o) + 1; break;
                            case TypeCode.UInt16: v = ((System.UInt16)o) + 1; break;
                            case TypeCode.UInt32: v = ((System.UInt32)o) + 1; break;
                            case TypeCode.UInt64: v = ((System.UInt64)o) + 1; break;
                        }

                        p.Value = v;
                    }
                }
            }
            catch { }
        }
    }
}