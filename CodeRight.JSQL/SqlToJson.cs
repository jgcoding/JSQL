﻿using System;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using Microsoft.SqlServer.Server;
using System.Text;
using System.Collections;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using System.Globalization;

using WebRight.Serialization;

public partial class UserDefinedFunctions
{
    //manage sql injection attacks
    private static String blockSqlInjection(String[] objName, String cols)
    {
        String err = String.Empty;
        if (objName.Count() < 3)
            err = "{\"InvalidInputError\":\"A fully qualified view, table, or function name must be supplied.(i.e. database.schema.table\"}";
        
        if (cols.Contains("DROP ") | cols.Contains("DELETE ") | cols.Contains("CREATE ") | cols.Contains("ALTER "))
            err = "{\"InvalidDDLError\":\"Objects may not be created, deleted, or destroyed.\"}";

        if (objName.Contains("master") | objName.Contains("model") | objName.Contains("msdb") | objName.Contains("tempdb"))
            err = "{\"InvalidResourceError\":\"System databases may not be referenced within this query.\"}";
        return err;
    }
    //manage sql injection attacks
    private static String blockSqlInjection(String query)
    {
        String err = String.Empty;
        if (query.Contains("drop ") | query.Contains("delete ") | query.Contains("create ") | query.Contains("alter ") | query.Contains("insert ") | query.Contains("update "))
            err = "{\"InvalidDDLError\":\"create, delete, update, insert, or drop not permitted with this function.\"}";

        if (query.Contains("model") | query.Contains("msdb") | query.Contains("tempdb"))
            err = "{\"InvalidResourceError\":\"System databases may not be referenced within this query.\"}";
        return err;
    }
    //construct the core query header
    private static StringBuilder buildQueryString(String objName, String criteria, String cols)
    {
        StringBuilder sql = new StringBuilder();
        sql.AppendFormat("SELECT {0} ", cols.StartsWith("@") ? cols.Substring(1) : String.IsNullOrEmpty(cols) ? "*" : cols);
        sql.AppendFormat("FROM {0} ", objName);
        if (!String.IsNullOrEmpty(criteria))
        {
            if (criteria.Substring(0, 1).Equals("@"))
            {
                if (criteria.Length > 5)
                    sql.AppendFormat("WHERE CONTAINS(*,'{0}') ", criteria.Substring(1, criteria.Length - 1));
            }
            else
                sql.AppendFormat("WHERE {0}", criteria);
        }
        return sql;     
    }

    //construct and manage sql server error messages
    private static String sqlErrorMessage(SqlException sqlex)
    {
        StringBuilder sqlerr = new StringBuilder();
        sqlerr.Append("{");
        sqlerr.AppendFormat("\"ErrorMessage\":{0},", sqlex.Message);
        sqlerr.AppendFormat("\"ErrorNumber\":{0},", sqlex.Number);
        sqlerr.AppendFormat("\"SeverityLevel\":\"{0}\",", sqlex.Class);
        sqlerr.AppendFormat("\"State\":{0},", sqlex.State);
        sqlerr.AppendFormat("\"Procedure\":\"{0}\",", sqlex.Procedure);
        sqlerr.AppendFormat("\"LineNumber\":{0},", sqlex.LineNumber);
        sqlerr.Append("}");
        return sqlerr.ToString();
    }
   
    public static void StringifiedRows(object row, out SqlGuid ItemID, out SqlString Json)
    {
        //this method receives the 'row' object returned from the JsonToTable function above and inserts
        //each object enumeration into the column(s) declaration below:
        StringifiedRow col = (StringifiedRow)row;        
        ItemID = (SqlGuid)(col.ItemID);
        Json = (SqlString)(col.Json);
    }
    public struct StringifiedRow
    {
        public Guid ItemID;
        public String Json;
        public void Load(Guid id, String json)
        {
            this.ItemID = id;
            this.Json = json;
        }
    }
    
    [SqlFunction(DataAccess = DataAccessKind.Read, SystemDataAccess = SystemDataAccessKind.Read, FillRowMethodName = "StringifiedRows",
      TableDefinition = "DocumentID uniqueidentifier, Document NVARCHAR(MAX)")]
    public static IEnumerable SqlStringifyRow2(SqlGuid docid, SqlString query)
    {
        ArrayList srows = new ArrayList();
        StringBuilder json = new StringBuilder();

        String injerr = blockSqlInjection(query.Value.ToLower());
        if (!String.IsNullOrEmpty(injerr))
        {
            StringifiedRow row = new StringifiedRow();
            row.Load(docid.Value, injerr);
            srows.Add(row);
            return srows;
        }
        using (SqlConnection cn = new SqlConnection("context connection = true"))
        {
            cn.Open();
            SqlCommand command = new SqlCommand(query.ToString(), cn);
            SqlDataReader dr = command.ExecuteReader();
            while (dr.Read())
            {
                json.Append("{");//begin object(row)

                for (Int32 i = 0; i < dr.FieldCount; i++)
                {
                    if (!dr.GetValue(i).Equals(DBNull.Value))
                    {
                        String kv = JsonFormat(dr, i);
                        if (!String.IsNullOrEmpty(kv))
                        {
                            json.AppendFormat("{0},", kv);
                        }
                    }
                }
                json.Remove(json.Length - 1, 1);//remove the trailing comma
                json.Append("}");//end object(row)

                StringifiedRow row = new StringifiedRow();
                row.Load(docid.Value, json.ToString());
                srows.Add(row);
            }
        }
        return srows;
    }

    [SqlFunction(DataAccess = DataAccessKind.Read, SystemDataAccess = SystemDataAccessKind.Read, FillRowMethodName = "StringifiedRows",
      TableDefinition = "ItemID uniqueidentifier, ItemValue NVARCHAR(MAX)")]
    public static IEnumerable SqlStringifyRow(String objName, String pKeyName, SqlGuid pKeyValue, String cols, String where, String misc)
    {
        String[] objrx = new String[] { "." };
        String[] obj = objName.Split(objrx, StringSplitOptions.RemoveEmptyEntries);

        ArrayList srows = new ArrayList();
        StringBuilder json = new StringBuilder();

        String injerr = blockSqlInjection(obj, cols);
        if (!String.IsNullOrEmpty(injerr))
        {
            StringifiedRow row = new StringifiedRow();
            row.Load(pKeyValue.Value, injerr);
            srows.Add(row);
            return srows;
        }
        StringBuilder sql = new StringBuilder();
        sql.AppendFormat("SELECT {0} ", cols.StartsWith("@") ? cols.Substring(1) : String.IsNullOrEmpty(cols) ? "*" : cols);
        sql.AppendFormat("FROM {0} ", objName);
        sql.AppendFormat("WHERE {0} = '{1}'", pKeyName, pKeyValue);
        if(!String.IsNullOrEmpty(where))
            sql.AppendFormat(" AND ({0})", where);
        if (!String.IsNullOrEmpty(misc))
            sql.AppendFormat(" {0} ", misc);
        using (SqlConnection cn = new SqlConnection("context connection = true"))
        {
            cn.Open();
            SqlCommand command = new SqlCommand(sql.ToString(), cn);
            SqlDataReader dr = command.ExecuteReader();
            while (dr.Read())
            {
                json.Append("{");//begin object(row)
                if (cols.StartsWith("@"))
                {
                    json.AppendFormat("{0},", dr.GetValue(0));
                }
                else
                {
                    for (Int32 i = 0; i < dr.FieldCount; i++)
                    {
                        if (!dr.GetValue(i).Equals(DBNull.Value))
                        {
                            String kv = JsonFormat(dr, i);
                            if (!String.IsNullOrEmpty(kv))
                            {
                                json.AppendFormat("{0},", kv);
                            }
                        }
                    }
                    json.Remove(json.Length - 1, 1);//remove the trailing comma
                    json.Append("}");//end object(row)
                }
                StringifiedRow row = new StringifiedRow();
                row.Load(pKeyValue.Value, json.ToString());
                srows.Add(row);
            }
        }
        return srows;
    }

    [SqlFunction(DataAccess = DataAccessKind.Read, SystemDataAccess = SystemDataAccessKind.Read)]
    public static SqlString ConcatFormatColumns(SqlString objName, SqlString cols, SqlString alias, SqlBoolean nullable)
    {
        StringBuilder sql = new StringBuilder();
        StringBuilder jformat = new StringBuilder();
        try
        {
            sql.AppendFormat("SELECT {0} FROM {1}", cols.Value, objName.Value);
            using (SqlConnection cn = new SqlConnection("context connection = true"))
            {
                cn.Open();
                SqlCommand command = new SqlCommand(sql.ToString(), cn);
                using (SqlDataReader dr = command.ExecuteReader(CommandBehavior.SchemaOnly))
                {
                    jformat.Append("SELECT '{'+");
                    for (Int32 i = 0; i < dr.FieldCount; i++)
                    {
                        jformat.AppendFormat("{0}", Utilities.FormatColumnForJson(dr.GetName(i).ToString(), dr.GetDataTypeName(i).ToLower(), String.IsNullOrEmpty(alias.Value) ? String.Empty : alias.Value, nullable.Value));                            
                    }
                    String token = jformat.ToString().Substring(jformat.Length - 1, 1);
                    while (token.Equals("+")|token.Equals(",")|token.Equals("'"))
                    {
                        jformat.Remove(jformat.Length - 1, 1);
                        token = jformat.ToString().Substring(jformat.Length - 1, 1);
                    }
                    jformat.Append("+'}' ");
                    jformat.AppendLine();
                    jformat.AppendFormat("FROM {0}{1}", objName.Value, String.IsNullOrEmpty(alias.Value) ? String.Empty : String.Format(" [{0}]", alias.Value));
                }
            }
        }
        catch (Exception)
        {            
            throw;
        }

        return jformat.ToString();
    }

    [SqlFunction()]
    public static String StringifySqlColumn(String key, String value, String dt)
    {
        return Utilities.StringifyColumn(key, value, dt);
    }

    [SqlFunction(DataAccess = DataAccessKind.Read, SystemDataAccess = SystemDataAccessKind.Read)]
    public static SqlString SqlStringifySet(String objName, String criteria, String cols)
    {
        StringBuilder json = new StringBuilder();
        try
        {
            String[] objrx = new String[] { "." };
            String[] obj = objName.Split(objrx, StringSplitOptions.RemoveEmptyEntries);

            String injerr = blockSqlInjection(obj, cols);
            if (!String.IsNullOrEmpty(injerr))
                return String.Format("{0}]}", injerr);

            StringBuilder sql = buildQueryString(objName, criteria, cols);
            json.Append("{");
            json.AppendFormat("\"{0}\":[", objName);

            using (SqlConnection cn = new SqlConnection("context connection = true"))
            {
                cn.Open();
                SqlCommand command = new SqlCommand(sql.ToString(), cn);
                SqlDataReader dr = command.ExecuteReader();
                if (dr.HasRows)
                {
                    while (dr.Read())
                    {
                        if (cols.StartsWith("@"))
                        {
                            json.AppendFormat("{0},", dr.GetValue(0));
                        }
                        else
                        {
                            json.Append("{");//begin object(row)
                            for (Int32 i = 0; i < dr.FieldCount; i++)
                            {
                                if (!dr.GetValue(i).Equals(DBNull.Value))
                                {
                                    String kv = JsonFormat(dr, i);
                                    if (!String.IsNullOrEmpty(kv))
                                    {
                                        json.AppendFormat("{0},", kv);
                                    }
                                }
                            }
                            json.Remove(json.Length - 1, 1);//remove the trailing comma
                            json.Append("},");//end object(row)
                        }
                    }
                    json.Remove(json.Length - 1, 1);//remove the trailing comma
                }
            }            
        }
        catch (SqlException sqlerr)
        {
            json.Append(sqlErrorMessage(sqlerr));
        }
        catch (Exception ex)
        {
            json.Append("{");
            json.AppendFormat("\"DotNetError\":\"{0}\"", ex.Message);
            json.Append("}");
        }
        json.Append("]}");
        return json.ToString();
    }
    

    //format the incoming column for json
    public static String JsonFormat(SqlDataReader dr, Int32 colno)
    {
        String result = String.Empty;
        String sval = String.Empty;
        switch (dr.GetDataTypeName(colno).ToLower())
        {  
            case "datetime":
            case "datetime2":
                sval = dr.GetValue(colno).ToString().Trim();
                if (sval.Length < 1)
                    result = String.Empty;
                else {
                    CultureInfo ci = CultureInfo.InvariantCulture;
                    DateTime rdate = DateTime.Parse(sval);
                    result = string.Format("\"{0}\":\"{1}\"", dr.GetName(colno), rdate.ToString("MM/dd/yyyy HH:mm:ss.FFF", ci.DateTimeFormat));
                }
                break;
            case "varchar":
            case "nvarchar":
            case "uniqueidentifier":
            case "smalldatetime":
            case "char":
            case "nchar": 
            case "date":
            case "text":
            case "ntext":            
            case "string":
                sval = dr.GetValue(colno).ToString().Trim();
                if (sval.Length < 1)
                    result = String.Empty;
                //is this a json object column?
                else if (sval.StartsWith("{"))
                {
                    if (sval.Substring(1, sval.Length - 1).Trim().Length < 1)//ignore it if it is empty
                        sval = String.Empty;
                    else
                        sval = String.Format("\"{0}\":{1}", dr.GetName(colno), dr.GetValue(colno));
                    result = sval;
                }
                //is this a json array column?
                else if (sval.StartsWith("["))
                {
                    if (sval.Substring(1, sval.Length - 1).Trim().Length < 1)//ignore it if it is empty
                        sval = String.Empty;
                    else
                        sval = String.Format("\"{0}\":{1}", dr.GetName(colno), dr.GetValue(colno));
                    result = sval;
                }
                else 
                    result = string.Format("\"{0}\":\"{1}\"", dr.GetName(colno), Utilities.JsonEscape(sval));
                break;
            case "int":
            case "smallint":
            case "tinyint":
            case "decimal":
            case "money":
            case "bigint":
            case "float":
            case "numeric":
            case "number":
            case "real":
            case "smallmoney":            
                result = string.Format("\"{0}\":{1}", dr.GetName(colno), dr.GetValue(colno));
                break;
            case "boolean":
            case "bit":
                result = string.Format("\"{0}\":{1}", dr.GetName(colno), dr.GetValue(colno).Equals(true) ? "true" : "false");
                break;
            default:
                result = String.Empty;
                break;
        }
        return result;
    }
};

