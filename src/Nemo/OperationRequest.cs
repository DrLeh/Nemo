﻿using System;
using System.Configuration;
using System.Data;
using System.Data.Common;
using System.Linq;
using Nemo.Extensions;
using System.Collections.Generic;
using Nemo.Configuration;
using Nemo.Data;

namespace Nemo;

[Serializable]
public class OperationRequest
{
    private string _connectionString = null;
    private Param[] _parameters = null;
    private OperationReturnType _returnType = OperationReturnType.Guess;
    private OperationType _operationType = OperationType.Guess;
    
    [NonSerialized]
    private DbConnection _connection = null;
    [NonSerialized]
    private DbTransaction _transaction = null;
    [NonSerialized]
    private INemoConfiguration _configuration = null;

    public string Operation { get; set; }

    public string ConnectionName { get; set; }

    public DbConnection Connection
    {
        get
        {
            if (_connection == null && ConnectionName.NullIfEmpty() == null && _connectionString.NullIfEmpty() != null)
            {
                _connection = DbFactory.CreateConnection(_connectionString, _configuration);
            }
            return _connection;
        }
        set
        {
            _connection = value;
            if (_connection != null)
            {
                _connectionString = _connection.ConnectionString;
            }
        }
    }
    
    public DbTransaction Transaction 
    {
        get
        {
            return _transaction;
        }
        set
        {
            _transaction = value;
            if (_transaction?.Connection != null)
            {
                Connection = _transaction.Connection;
            }
        }
    }
    
    public bool InTransaction
    {
        get
        {
            return Transaction != null || System.Transactions.Transaction.Current != null;
        }
    }

    public IEnumerable<Param> Parameters
    {
        get
        {
            return _parameters ?? Enumerable.Empty<Param>();
        }
        set
        {
            if (value != null)
            {
                _parameters = value.ToArray();
            }
        }
    }

    public IList<Type> Types
    {
        get;
        set;
    }

    public OperationReturnType ReturnType
    {
        get
        {
            return _returnType;
        }
        set
        {
            _returnType = value;
        }
    }

    public OperationType OperationType
    {
        get
        {
            return _operationType;
        }
        set
        {
            _operationType = value;
        }
    }

    public bool CaptureException { get; set; }

    public string SchemaName { get; set; }

    public INemoConfiguration Configuration { get; set; }

    public bool IsValid()
    {
        return _transaction != null || _connection != null || !string.IsNullOrEmpty(_connectionString) || (Types != null && Types.Count > 0);
    }
}
