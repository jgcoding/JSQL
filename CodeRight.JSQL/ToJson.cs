﻿using System;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using Microsoft.SqlServer.Server;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;


[Serializable]
[SqlUserDefinedAggregate(Format.UserDefined, //use clr serialization to serialize the intermediate result
    IsInvariantToNulls = true, //optimizer property
    IsInvariantToDuplicates = false, //optimizer property
    IsInvariantToOrder = false, //optimizer property
    Name = "JSqlSerializer",
    MaxByteSize = -1) //maximum size in bytes of persisted value
]
public struct JSqlSerializer : IBinarySerialize
{
    public static readonly StringComparison sc = StringComparison.InvariantCultureIgnoreCase;
    /// <summary>
    /// The variable that holds the intermediate result of the concatenation
    /// </summary>
    private StringBuilder json;
    private String objType;

    /// <summary>
    /// Initialize the internal data structures
    /// </summary>
    public void Init()
    {
        this.objType = "object";
        this.json = new StringBuilder();
    }

    /// <summary>
    /// Accumulate the next value, not if the value is null
    /// </summary>
    /// <param name="itemValue"></param>
    public void Accumulate(SqlString itemKey, SqlString itemValue)
    {
        if (String.IsNullOrEmpty(itemKey.Value))
        {
            // don't process empty key-value pairs
            if (String.IsNullOrEmpty(itemValue.Value))
            {
                return;    
            }
            // this is a JObject token
            if (itemValue.Value.StartsWith("{@JObject", sc))
            {
                this.objType = "object";
                this.json.AppendFormat("{0},", FormatJsonValue(itemValue.Value));
            }
            // this is a JArray token
            else if (itemValue.Value.StartsWith("{@JArray", sc))
            {
                this.objType = "array";
                this.json.AppendFormat("{0},", FormatJsonValue(itemValue.Value));
            }
            // otherwise, handle simple arrays (non-key/value pairs)
            else
            {   
                this.objType = "array";
                this.json.AppendFormat("{0},", itemValue.Value.StartsWith("\"") ? itemValue.Value : FormatJsonValue(itemValue.Value));
            }
        }
        else/*handle key/value pairs*/
        {
            this.json.AppendFormat("\"{0}\":{1},", itemKey.Value, itemValue.Value.StartsWith("\"") ? itemValue.Value : FormatJsonValue(itemValue.Value));
        }
    }

    public String FormatJsonValue(String itemValue)
    {
        /*arrays and objects*/
        if (itemValue.StartsWith("[") | itemValue.StartsWith("{"))
        {
            return itemValue;
        }
        /*boolean*/
        else if (String.Equals(itemValue, "true", sc) | String.Equals(itemValue, "false", sc))
        {
            return itemValue;
        }
        /*floats*/
        else if (Regex.IsMatch(itemValue, "^-{0,1}\\d*\\.[\\d]+$"))
        {
            return itemValue;
        }
        /*ints*/
        else if (Regex.IsMatch(itemValue, "^-{0,1}(?:[1-9]+[0-9]*|[0]{1})$"))
        {
            return itemValue;
        }
        /*empty quotes*/
        else if (String.Equals(itemValue, "\"\""))
        {
            return itemValue;
        }
        /*nulls*/
        else if ((String.Equals(itemValue, "null", sc)) || (itemValue == null))
        {
            return itemValue;
        }
        else
        {
            return String.Format("\"{0}\"", itemValue);
        }
    }
    /// <summary>
    /// Merge the partially computed aggregate with this aggregate.
    /// </summary>
    /// <param name="other"></param>
    public void Merge(JSqlSerializer Group)
    {
        this.json.Append(Group.json);
        this.objType = Group.objType;
    }

    /// <summary>
    /// Called at the end of aggregation, to return the results of the aggregation.
    /// </summary>
    /// <returns></returns>
    public SqlString Terminate()
    {
        String output = String.Empty;
        //delete the trailing comma, if any
        if (this.json != null && this.json.Length > 0)
        {
            this.json.Remove(json.Length - 1, 1);
            if (String.Equals(objType, "array"))
            {
                this.json.Insert(0, "[");
                this.json.Append("]");
            }
            else if (String.Equals(objType, "object"))
            {
                this.json.Insert(0, "{");
                this.json.Append("}");
            }
            output = this.json.ToString();            
        }        
        return new SqlString(output);
    }

    public void Read(BinaryReader r)
    {
        json = new StringBuilder(r.ReadString());
        objType = r.ReadString();
    }

    public void Write(BinaryWriter w)
    {
        w.Write(this.json.ToString());
        w.Write(this.objType);
    }

}
