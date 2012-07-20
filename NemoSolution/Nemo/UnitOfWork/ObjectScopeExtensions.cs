﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Text;
using Nemo.Attributes;
using Nemo.Data;
using Nemo.Extensions;
using Nemo.Reflection;
using Nemo.Serialization;
using System.Diagnostics;

namespace Nemo.UnitOfWork
{
    public static class ObjectScopeExtensions
    {
        public static bool Commit<T>(this T businessObject)
            where T : class, IBusinessObject
        {
            var success = true;
            var context = ObjectScope.Current;

            if (context != null)
            {
                if (context.ChangeTracking == ChangeTrackingMode.Automatic)
                {
                    using (var connection = DbFactory.CreateConnection(null, typeof(T)))
                    {
                        connection.Open();
                        var changes = CompareObjects(businessObject, businessObject.Old());
                        var statement = GetCommitStatement<T>(changes, connection);
                        var response = ObjectFactory.Execute<T>(new OperationRequest { Operation = statement.Item1, OperationType = OperationType.Sql, Parameters = statement.Item2, Connection = connection, ReturnType = OperationReturnType.DataTable });
                        success = response.Value != null;
                        if (success)
                        {
                            SetGeneratedPropertyValues(statement.Item3, (DataTable)response.Value);
                        }
                    }
                }
                else if (context.ChangeTracking == ChangeTrackingMode.Debug)
                {
                    using (var connection = DbFactory.CreateConnection(null, typeof(T)))
                    {
                        connection.Open();
                        var changes = CompareObjects(businessObject, businessObject.Old());
                        var statement = GetCommitStatement<T>(changes, connection);
                        Debug.WriteLine(statement.Item1);
                    }
                }

                if (context.IsNested)
                {
                    success = context.UpdateOuterSnapshot(businessObject);
                }
                context.Snapshots.Remove(businessObject);
            }

            if (success && context.Transaction != null)
            {
                context.Transaction.Complete();
            }
            return success;
        }
        
        public static bool Rollback<T>(this T businessObject)
            where T : class, IBusinessObject
        {
            businessObject.ThrowIfNull("businessObject");

            var context = ObjectScope.Current;
            if (context == null) return false;

            var oldObject = businessObject.Old();
            ObjectFactory.Map(oldObject, businessObject, true);
            context.Snapshots.Remove(businessObject);
            return true;
        }

        public static T Old<T>(this T businessObject)
            where T : class, IBusinessObject
        {
            businessObject.ThrowIfNull("businessObject");

            var context = ObjectScope.Current;
            if (context == null) return default(T);

            object value;
            if (context.DeserializedSnapshots.TryGetValue(businessObject, out value))
            {
                return value as T;
            }

            byte[] serializedObject = context.Snapshots[businessObject];
            var result = SerializationExtensions.Deserialize<T>(serializedObject);
            context.DeserializedSnapshots[businessObject] = result;
            return result;
        }

        #region Cascade Methods

        public static void Cascade<T>(this T businessObject, string propertyName, object propertyValue)
            where T : class, IBusinessObject
        {
            Cascade((object)businessObject, propertyName, propertyValue);
        }

        private static void Cascade(object businessObject, string propertyName, object propertyValue)
        {
            if (businessObject == null) return;

            var objectType = businessObject.GetType();
            var interfaceType = Reflector.ExtractInterface(objectType);
            var propertyMap = Reflector.GetPropertyMap(interfaceType);

            foreach (var property in propertyMap.Values)
            {
                if (property.IsBusinessObjectList)
                {
                    var childCollection = (IEnumerable)((IBusinessObject)businessObject).Property(property.PropertyName);
                    if (childCollection != null)
                    {
                        foreach (var item in childCollection)
                        {
                            CascadeToChild(businessObject, item, propertyName, propertyValue);
                        }
                    }
                }
                else if (property.IsBusinessObject)
                {
                    var childObject = ((IBusinessObject)businessObject).Property(property.PropertyName);
                    CascadeToChild(businessObject, childObject, propertyName, propertyValue);
                }
            }
        }

        private static void CascadeToChild(object parentObject, object childObject, string propertyName, object propertyValue)
        {
            if (childObject != null)
            {
                var interfaceType = Reflector.ExtractInterface(childObject.GetType());
                var childProperty = interfaceType.GetProperty(propertyName);
                if (childProperty != null)
                {
                    ((IBusinessObject)childObject).Property(propertyName, propertyValue);
                }
                Cascade(childObject, propertyName, propertyValue);
            }
        }

        private static void SetGeneratedPropertyValues(List<IBusinessObject> businessObjects, DataTable generatedValues)
        {
            var map = generatedValues.AsEnumerable().ToDictionary(r => r.Field<int>("StatementId"), r => Tuple.Create(r.Field<object>("GeneratedId"), r.Field<string>("ParameterName"), r.Field<string>("PropertyName")));
            Tuple<object, string, string> value;

            for (int i = 0; i < businessObjects.Count; i++)
            {
                if (map.TryGetValue(i, out value))
                {
                    businessObjects[i].Property(value.Item3, value.Item1);
                    businessObjects[i].Cascade(value.Item3, value.Item1);
                }
            }
        }

        #endregion

        #region Change Detection Methods

        private static ChangeNode CompareObjects(IBusinessObject currentObject, IBusinessObject oldObject, ChangeNode parentNode = null)
        {
            if (currentObject == null && oldObject == null)
                throw new ArgumentNullException("currentObject and oldObject cannot be null at the same time");

            var rootNode = new ChangeNode();
            rootNode.Value = currentObject;
            if (parentNode != null)
            {
                rootNode.Parent = parentNode;
            }

            var context = ObjectScope.Current;
            if (context == null) return null;

            // Get properties and build a property map
            Type interfaceType = Reflector.ExtractInterface((currentObject ?? oldObject).GetType());

            var primaryKey = (currentObject ?? oldObject).GetPrimaryKey();

            var propertyMap = Reflector.GetPropertyMap(interfaceType);

            foreach (var property in propertyMap.Values)
            {
                object currentValue = null;
                if (currentObject != null && currentObject.PropertyExists(property.PropertyName))
                {
                    currentValue = currentObject.Property(property.PropertyName);
                }

                object oldValue = null;
                if (oldObject != null && oldObject.PropertyExists(property.PropertyName))
                {
                    oldValue = oldObject.Property(property.PropertyName);
                }

                if (currentValue == null && oldValue == null) continue;

                var changeNode = new ChangeNode();
                changeNode.ObjectState = ObjectState.Clean;

                Type objectType = property.PropertyType;
                var reflectedType = Reflector.GetReflectedType(objectType);
                changeNode.Type = objectType;
                changeNode.Property = property;

                if (!context.IsNew && rootNode.ObjectState != ObjectState.DirtyPrimaryKey && (reflectedType.IsSimpleType || reflectedType.IsSimpleList))
                {
                    if (reflectedType.IsSimpleList)
                    {
                        if (currentValue != null)
                        {
                            currentValue = ((IEnumerable)currentValue).Cast<object>().ToDelimitedString(",");
                        }

                        if (oldValue != null)
                        {
                            oldValue = ((IEnumerable)oldValue).Cast<object>().ToDelimitedString(",");
                        }
                    }

                    if (currentObject != null && oldObject != null && !object.Equals(currentValue, oldValue))
                    {
                        changeNode.Value = currentValue;
                        changeNode.ObjectState = ObjectState.Dirty;
                    }
                    else if (currentObject != null && oldObject == null)
                    {
                        changeNode.Value = currentValue;
                        changeNode.ObjectState = ObjectState.New;
                    }
                }
                /*else if (!context.IsNew && Reflector.IsXmlDocument(objectType))
                {
                    if (currentObject != null && oldObject != null && 
                        ((currentValue != null && oldValue != null && ((XmlDocument)currentValue).OuterXml.GetHashCode() != ((XmlDocument)oldValue).OuterXml.GetHashCode())
                        || (currentValue != null && oldValue == null) || (currentValue == null && oldValue != null))) 
                    {
                        changeNode.Value = currentValue;
                        changeNode.ObjectState = ObjectState.Dirty;
                    }
                }*/
                else if (reflectedType.IsBusinessObjectList)
                {
                    var changes = CompareLists((IList)currentValue, (IList)oldValue, changeNode, property.PropertyName);
                    if (changes.Count > 0)
                    {
                        changeNode.Value = currentValue ?? oldValue;
                    }
                    rootNode.ListProperties.Add(property.PropertyName);
                }
                else if (reflectedType.IsBusinessObject)
                {
                    var changes = CompareObjects((IBusinessObject)currentValue, (IBusinessObject)oldValue, parentNode);
                    if (changes.Count > 0)
                    {
                        changeNode.Value = currentValue ?? oldValue;
                        changes.Nodes.ForEach(n => changeNode.Nodes.Add(n));
                    }
                    rootNode.ObjectProperties.Add(property.PropertyName);
                }

                if (!changeNode.IsEmpty)
                {
                    changeNode.PropertyName = property.PropertyName;
                    if (changeNode.ObjectState == ObjectState.Clean)
                    {
                        if (context.IsNew)
                        {
                            changeNode.ObjectState = ObjectState.New;
                        }
                        else if (currentValue != null && oldValue != null)
                        {
                            changeNode.ObjectState = ObjectState.Dirty;
                        }
                        else if (currentValue != null)
                        {
                            changeNode.ObjectState = ObjectState.New;
                        }
                        else if (oldValue != null)
                        {
                            changeNode.ObjectState = ObjectState.Deleted;
                        }
                    }

                    // Primary key modifications should translate into insert and delete
                    if (changeNode.ObjectState == ObjectState.Dirty && primaryKey.ContainsKey(property.PropertyName))
                    {
                        rootNode.Nodes.RemoveAll(n => n.ObjectState == ObjectState.Dirty);

                        var insertNode = new ChangeNode();
                        insertNode.Parent = rootNode;
                        insertNode.Value = currentObject;
                        insertNode.ObjectState = ObjectState.New;

                        var deleteNode = new ChangeNode();
                        deleteNode.Parent = rootNode;
                        deleteNode.Value = oldObject;
                        deleteNode.ObjectState = ObjectState.Deleted;

                        rootNode.ObjectState = ObjectState.DirtyPrimaryKey;

                        rootNode.Nodes.Add(insertNode);
                        rootNode.Nodes.Add(deleteNode);
                    }
                    else
                    {
                        changeNode.Parent = rootNode;
                        rootNode.Nodes.Add(changeNode);
                    }
                }
            }

            // Root node can never be deleted!
            if (context.IsNew)
            {
                rootNode.ObjectState = ObjectState.New;
            }
            else if (!rootNode.IsRoot)
            {
                if (rootNode.ObjectState != ObjectState.Dirty)
                {
                    rootNode.ObjectState = ObjectState.Clean;
                }
            }
            else if (currentObject != null && oldObject == null)
            {
                rootNode.ObjectState = ObjectState.New;
            }

            return rootNode;
        }

        private static List<ChangeNode> CompareLists(IList currentList, IList oldList, ChangeNode parentNode, string propertyName)
        {
            var changeList = new List<ChangeNode>();

            if (currentList == null && oldList == null)
            {
                return changeList;
            }

            Dictionary<string, IBusinessObject> objectMapCurrent = new Dictionary<string, IBusinessObject>();
            Dictionary<string, IBusinessObject> objectMapOld = new Dictionary<string, IBusinessObject>();

            if (currentList != null)
            {
                for (int i = 0; i < currentList.Count; i++)
                {
                    objectMapCurrent.Add(((IBusinessObject)currentList[i]).ComputeHash(), (IBusinessObject)currentList[i]);
                }
            }

            if (oldList != null)
            {
                for (int i = 0; i < oldList.Count; i++)
                {
                    objectMapOld.Add(((IBusinessObject)oldList[i]).ComputeHash(), (IBusinessObject)oldList[i]);
                }
            }

            var modifications = objectMapCurrent.Where(k => objectMapOld.ContainsKey(k.Key));
            var additions = objectMapCurrent.Where(k => !objectMapOld.ContainsKey(k.Key));
            var deletions = objectMapOld.Where(k => !objectMapCurrent.ContainsKey(k.Key));

            foreach (var pair in modifications)
            {
                var changes = CompareObjects(pair.Value, objectMapOld[pair.Key], null);

                var changeNode = new ChangeNode();
                changeNode.PropertyName = propertyName;
                changeNode.Value = pair.Value;
                changeNode.Parent = parentNode;
                changeNode.Index = changeList.Count;

                if (changes.Count > 0)
                {
                    changeNode.ObjectState = ObjectState.Dirty;
                    changes.Parent = changeNode;
                    changeNode.Nodes.AddRange(changes.Nodes);
                }

                changeList.Add(changeNode);
            }

            foreach (var pair in additions)
            {
                var changes = CompareObjects(pair.Value, null, null);

                var changeNode = new ChangeNode();
                changeNode.ObjectState = ObjectState.New;
                changeNode.PropertyName = propertyName;
                changeNode.Value = pair.Value;
                changeNode.Parent = parentNode;
                changeNode.Index = changeList.Count;

                if (changes.Count > 0)
                {
                    changes.Parent = changeNode;
                    changeNode.Nodes.AddRange(changes.Nodes);
                }

                changeList.Add(changeNode);
            }

            foreach (var pair in deletions)
            {
                var changes = CompareObjects(null, pair.Value, null);

                var changeNode = new ChangeNode();
                changeNode.ObjectState = ObjectState.Deleted;
                changeNode.PropertyName = propertyName;
                changeNode.Value = pair.Value;
                changeNode.Parent = parentNode;
                changeNode.Index = changeList.Count;

                if (changes.Count > 0)
                {
                    changes.Parent = changeNode;
                    changeNode.Nodes.AddRange(changes.Nodes);
                }

                changeList.Add(changeNode);
            }

            if (changeList.Count > 0)
            {
                parentNode.Nodes.AddRange(changeList);
            }

            return changeList;
        }

        private static List<ChangeNode> GetChanges(ChangeNode rootNode, ObjectState objectState)
        {
            var result = new List<ChangeNode>();
            switch (objectState)
            {
                case ObjectState.New:
                    TraverseDepthFirst(rootNode, result, n => n.ObjectState == ObjectState.New);
                    break;
                case ObjectState.Deleted:
                    TraverseBreadthFirst(rootNode, result, n => n.ObjectState == ObjectState.Deleted);
                    break;
                case ObjectState.Dirty:
                    TraverseDepthFirst(rootNode, result, n => n.ObjectState == ObjectState.Dirty);
                    break;
            }
            return result;
        }

        private static void TraverseDepthFirst(ChangeNode rootNode, List<ChangeNode> accumulator, Predicate<ChangeNode> predicate)
        {
            if (accumulator == null)
            {
                accumulator = new List<ChangeNode>();
            }
            if ((rootNode.IsLeaf || rootNode.IsObject) && predicate != null && predicate(rootNode))
            {
                accumulator.Add(rootNode);
            }
            foreach (var childNode in rootNode.Nodes)
            {
                TraverseDepthFirst(childNode, accumulator, predicate);
            }
        }

        private static void TraverseBreadthFirst(ChangeNode rootNode, List<ChangeNode> accumulator, Predicate<ChangeNode> predicate)
        {
            if (accumulator == null)
            {
                accumulator = new List<ChangeNode>();
            }
            foreach (var childNode in rootNode.Nodes)
            {
                TraverseBreadthFirst(childNode, accumulator, predicate);
            }
            if ((rootNode.IsLeaf || rootNode.IsObject) && predicate != null && predicate(rootNode))
            {
                accumulator.Add(rootNode);
            }
        }

        #endregion

        #region Batch Update Methods

        private static Tuple<string, Param[], List<IBusinessObject>> GetCommitStatement<T>(ChangeNode rootNode, DbConnection connection)
            where T : class, IBusinessObject
        {
            var dialect = DialectFactory.GetProvider(connection);
            var businessObjects = new List<IBusinessObject>();
            var sql = new StringBuilder();
            var statementId = 0;

            // Inserts
            var newNodes = GetChanges(rootNode, ObjectState.New);
            var tableCreated = false;

            var autoGenParameterNames = new Dictionary<object, string>();
            var tempTableName = dialect.GetTemporaryTableName("ID");
            var allParameters = new List<Param>();

            foreach (var newNode in newNodes.Where(n => n.IsObject))
            {
                if (newNode.Value is IBusinessObject)
                {
                    businessObjects.Add((IBusinessObject)newNode.Value);
                }
                else
                {
                    continue;
                }

                var objectType = newNode.Value.GetType(); 
                var interfaceType = Reflector.ExtractInterface(objectType);
                var targetType = interfaceType ?? objectType;
                var propertyMap = Reflector.GetPropertyMap(targetType);
                var autoGenProperty = propertyMap.Where(p => p.Key.CanWrite && p.Value != null && p.Value.IsAutoGenerated).Select(p => p.Key).FirstOrDefault();
                var autoGenType = DbType.String;

                if (!tableCreated)
                {
                    if (autoGenProperty != null)
                    {
                        autoGenType = Reflector.ClrToDbType(autoGenProperty.PropertyType);
                    }
                    sql.AppendFormat(dialect.CreateTemporaryTable("ID", new Dictionary<string, DbType> { { "StatementId", DbType.Int32 }, { "GeneratedId", autoGenType }, { "ParameterName", DbType.AnsiString }, { "PropertyName", DbType.AnsiString } })).AppendLine();
                    tableCreated = true;
                }

                var parameters = ObjectExtensions.GetInsertParameters(newNode.Value, propertyMap, statementId);
                allParameters.AddRange(parameters);

                if (parameters.Length > 0)
                {
                    string commandName = SqlBuilder.GetInsertStatement(targetType, parameters, dialect);
                    var autoGenParameter = parameters.FirstOrDefault(p => p.IsAutoGenerated);

                    if (autoGenParameter != null)
                    {
                        sql.AppendFormat(dialect.DeclareVariable(autoGenParameter.Name, autoGenType)).AppendLine();
                        sql.AppendFormat(dialect.AssignVariable(autoGenParameter.Name, autoGenParameter.Type.GetDefault())).AppendLine();
                    }
                    sql.AppendLine(commandName);

                    if (autoGenParameter != null)
                    {
                        sql.AppendFormat(dialect.ComputeAutoIncrement(autoGenParameter.Name)).AppendLine();
                        sql.AppendFormat("INSERT INTO " + tempTableName + " ({4}StatementId{5}, {4}GeneratedId{5}, {4}ParameterName{5}, {4}PropertyName{5}) VALUES ({0}, {1}, '{2}', '{3}')", statementId, dialect.ParameterPrefix + autoGenParameter.Name, autoGenParameter.Name, autoGenProperty.Name, dialect.IdentifierEscapeStartCharacter, dialect.IdentifierEscapeEndCharacter).AppendLine();
                    }

                    statementId++;
                }
            }

            if (newNodes.Count > 0)
            {
                sql.AppendLine("SELECT * FROM " + tempTableName);
            }

            if (tableCreated && !dialect.SupportsTemporaryTables)
            {
                sql.AppendLine("DROP TABLE " + tempTableName);
            }

            // Updates
            var dirtyNodes = GetChanges(rootNode, ObjectState.Dirty);
            var dirtyNodeParents = dirtyNodes.Where(n => n.IsSimpleLeaf).Select(n => n.Parent).Distinct();

            foreach (var dirtyNode in dirtyNodeParents)
            {
                var objectType = dirtyNode.Value.GetType();
                var interfaceType = Reflector.ExtractInterface(objectType);
                var targetType = interfaceType ?? objectType;
                var propertyMap = Reflector.GetPropertyMap(targetType).ToDictionary(p => p.Key.Name, p => p);
                
                var parameters = new List<Param>();
                foreach (var change in dirtyNode.Nodes.Where(n => n.IsSimpleLeaf))
                {
                    KeyValuePair<PropertyInfo, ReflectedProperty> map;
                    if (propertyMap.TryGetValue(change.PropertyName, out map))
                    {
                        var property = map.Key;
                        string parameterName = change.PropertyName;
                        if (map.Value != null && !string.IsNullOrEmpty(map.Value.ParameterName))
                        {
                            parameterName = map.Value.ParameterName;
                        }

                        parameters.Add(new Param { Name = parameterName + "_" + statementId, Value = change.Value, Source = MapColumnAttribute.GetMappedColumnName(property) });
                    }
                }

                var primaryKey = new List<Param>();
                foreach (var primaryKeyMap in propertyMap.Values.Where(p => p.Value.IsPrimaryKey))
                {
                    object value = ((IBusinessObject)dirtyNode.Value).Property(primaryKeyMap.Key.Name);

                    string parameterName = primaryKeyMap.Key.Name;
                    if (primaryKeyMap.Value != null && !string.IsNullOrEmpty(primaryKeyMap.Value.ParameterName))
                    {
                        parameterName = primaryKeyMap.Value.ParameterName;
                    }
                    primaryKey.Add(new Param { Name = parameterName + "_" + statementId, Value = value, Source = MapColumnAttribute.GetMappedColumnName(primaryKeyMap.Key) });
                }

                var commandName = SqlBuilder.GetUpdateStatement(targetType, parameters, primaryKey, dialect);
                allParameters.AddRange(parameters);
                allParameters.AddRange(primaryKey);
                sql.Append(commandName).AppendLine();

                statementId++;
            }

            // Deletes
            var deletedNodes = GetChanges(rootNode, ObjectState.Deleted);
            foreach (var deletedNode in deletedNodes)
            {
                var objectType = deletedNode.Value.GetType();
                var interfaceType = Reflector.ExtractInterface(objectType);
                var targetType = interfaceType ?? objectType;
                var propertyMap = Reflector.GetPropertyMap(targetType);

                var parameters = ObjectExtensions.GetDeleteParameters(deletedNode.Value, propertyMap, statementId);
                var commandName = SqlBuilder.GetDeleteStatement(targetType, parameters, dialect);
                allParameters.AddRange(parameters);
                sql.Append(commandName).AppendLine();

                statementId++;
            }

            return Tuple.Create(sql.ToString(), allParameters.ToArray(), businessObjects);
        }

        #endregion
    }
}