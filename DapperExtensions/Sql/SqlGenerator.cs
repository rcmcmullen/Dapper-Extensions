﻿using DapperExtensions.Mapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DapperExtensions.Sql
{
    public interface ISqlGenerator
    {
        IDapperExtensionsConfiguration Configuration { get; }

        string Select(IClassMapper classMap, IPredicate predicate, IList<ISort> sort, IDictionary<string, object> parameters);
        string SelectPaged(IClassMapper classMap, IPredicate predicate, IList<ISort> sort, int page, int resultsPerPage, IDictionary<string, object> parameters);
        string SelectSet(IClassMapper classMap, IPredicate predicate, IList<ISort> sort, int firstResult, int maxResults, IDictionary<string, object> parameters);
        string Count(IClassMapper classMap, IPredicate predicate, IDictionary<string, object> parameters);

        string Insert(IClassMapper classMap);
        string BulkInsert(IEnumerable<IClassMapper> classMaps);
        string BulkInsert<T>(IClassMapper classMap, IEnumerable<T> entities) where T : class;
        string BulkUpdate(IClassMapper classMap, IEnumerable<IPredicate> predicates, IDictionary<string, object> parameters, bool ignoreAllKeyProperties);
        string Update(IClassMapper classMap, IPredicate predicate, IDictionary<string, object> parameters, bool ignoreAllKeyProperties);
        string Delete(IClassMapper classMap, IPredicate predicate, IDictionary<string, object> parameters);

        string IdentitySql(IClassMapper classMap);
        string GetTableName(IClassMapper map);
        string GetColumnName(IClassMapper map, IPropertyMap property, bool includeAlias);
        string GetColumnName(IClassMapper map, string propertyName, bool includeAlias);
        bool SupportsMultipleStatements();
    }

    public class SqlGeneratorImpl : ISqlGenerator
    {
        public SqlGeneratorImpl(IDapperExtensionsConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IDapperExtensionsConfiguration Configuration { get; private set; }

        public virtual string Select(IClassMapper classMap, IPredicate predicate, IList<ISort> sort, IDictionary<string, object> parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException("Parameters");
            }

            StringBuilder sql = new StringBuilder(string.Format("SELECT {0} FROM {1}",
                BuildSelectColumns(classMap),
                GetTableName(classMap)));
            if (predicate != null)
            {
                sql.Append(" WHERE ")
                    .Append(predicate.GetSql(this, parameters));
            }

            if (sort != null && sort.Any())
            {
                sql.Append(" ORDER BY ")
                    .Append(sort.Select(s => GetColumnName(classMap, s.PropertyName, false) + (s.Ascending ? " ASC" : " DESC")).AppendStrings());
            }

            return sql.ToString();
        }

        public virtual string SelectPaged(IClassMapper classMap, IPredicate predicate, IList<ISort> sort, int page, int resultsPerPage, IDictionary<string, object> parameters)
        {
            if (sort == null || !sort.Any())
            {
                throw new ArgumentNullException("Sort", "Sort cannot be null or empty.");
            }

            if (parameters == null)
            {
                throw new ArgumentNullException("Parameters");
            }

            StringBuilder innerSql = new StringBuilder(string.Format("SELECT {0} FROM {1}",
                BuildSelectColumns(classMap),
                GetTableName(classMap)));
            if (predicate != null)
            {
                innerSql.Append(" WHERE ")
                    .Append(predicate.GetSql(this, parameters));
            }

            string orderBy = sort.Select(s => GetColumnName(classMap, s.PropertyName, false) + (s.Ascending ? " ASC" : " DESC")).AppendStrings();
            innerSql.Append(" ORDER BY " + orderBy);

            string sql = Configuration.Dialect.GetPagingSql(innerSql.ToString(), page, resultsPerPage, parameters);
            return sql;
        }

        public virtual string SelectSet(IClassMapper classMap, IPredicate predicate, IList<ISort> sort, int firstResult, int maxResults, IDictionary<string, object> parameters)
        {
            if (sort == null || !sort.Any())
            {
                throw new ArgumentNullException("Sort", "Sort cannot be null or empty.");
            }

            if (parameters == null)
            {
                throw new ArgumentNullException("Parameters");
            }

            StringBuilder innerSql = new StringBuilder(string.Format("SELECT {0} FROM {1}",
                BuildSelectColumns(classMap),
                GetTableName(classMap)));
            if (predicate != null)
            {
                innerSql.Append(" WHERE ")
                    .Append(predicate.GetSql(this, parameters));
            }

            string orderBy = sort.Select(s => GetColumnName(classMap, s.PropertyName, false) + (s.Ascending ? " ASC" : " DESC")).AppendStrings();
            innerSql.Append(" ORDER BY " + orderBy);

            string sql = Configuration.Dialect.GetSetSql(innerSql.ToString(), firstResult, maxResults, parameters);
            return sql;
        }


        public virtual string Count(IClassMapper classMap, IPredicate predicate, IDictionary<string, object> parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException("Parameters");
            }

            StringBuilder sql = new StringBuilder(string.Format("SELECT COUNT(*) AS {0}Total{1} FROM {2}",
                                Configuration.Dialect.OpenQuote,
                                Configuration.Dialect.CloseQuote,
                                GetTableName(classMap)));
            if (predicate != null)
            {
                sql.Append(" WHERE ")
                    .Append(predicate.GetSql(this, parameters));
            }

            return sql.ToString();
        }

        public virtual string BulkInsert(IEnumerable<IClassMapper> classMaps)
        {
            if (classMaps == null || classMaps.Count().Equals(0))
            {
                throw new ArgumentException("ClassMaps");
            }

            var sql = new StringBuilder();

            var tableNames = classMaps.Distinct(new ClassMapComparer()).ToList();

            tableNames.ForEach(tn =>
            {
                var columns = tn.Properties.Where(p => !(p.Ignored || p.IsReadOnly || p.KeyType == KeyType.Identity || p.KeyType == KeyType.TriggerIdentity)).ToList();
                var columnNames = columns.Select(p => GetColumnName(tn, p, false));
                sql.AppendLine($"INSERT INTO {GetTableName(tn)} ({columnNames.AppendStrings()}) VALUES ");

                var classMapList = classMaps.ToList();
                for (int i = 0; i < classMapList.Count; i++)
                {
                    if (i > 0) sql.Append(",").AppendLine();
                    var parameters = columns.Select(p => Configuration.Dialect.ParameterPrefix + p.Name);
                    sql.Append($"({parameters.AppendStrings(suffix: i.ToString())})");

                    //var properties = classMap.Properties.Select(p => p.PropertyInfo).ToList();
                    //if (!properties.Count.Equals(0))
                    //{
                    //    if (sql.ToString().Trim().EndsWith("VALUES")) { sql.Append("("); }
                    //    else { sql.AppendLine(",").Append("("); }

                    //    properties.ForEach(p =>
                    //    {
                    //        if (properties.IndexOf(p) > 0) sql.Append(",");

                    //        if (p.PropertyType.Name.Equals("String")
                    //        || p.PropertyType.Name.Equals("DateTime")
                    //        || p.PropertyType.Name.Equals("Guid")
                    //        || (p.PropertyType.Name.Equals("Nullable`1") &&
                    //            !p.PropertyType.GenericTypeArguments.Count().Equals(0) &&
                    //            p.PropertyType.GenericTypeArguments[0].Name.Equals("DateTime"))
                    //        || (p.PropertyType.Name.Equals("Nullable`1") &&
                    //            !p.PropertyType.GenericTypeArguments.Count().Equals(0) &&
                    //            p.PropertyType.GenericTypeArguments[0].Name.Equals("Guid")))
                    //        {
                    //            sql.Append($"'{Convert.ToString(p.GetValue(e) as object).Replace("'", "''")}'");
                    //        }
                    //        else if (p.PropertyType.Name.Equals("Boolean"))
                    //        {
                    //            sql.Append($"{(Convert.ToBoolean(p.GetValue(e)) ? 1 : 0)}");
                    //        }
                    //        else
                    //        {
                    //            sql.Append($"{p.GetValue(e) ?? 0}");
                    //        }
                    //    });
                    //    sql.Append(")");
                    //}
                };
            });

            return sql.ToString();
        }

        public virtual string BulkInsert<T>(IClassMapper classMap, IEnumerable<T> entities) where T : class
        {
            var columns = classMap.Properties.Where(p => !(p.Ignored || p.IsReadOnly || p.KeyType == KeyType.Identity || p.KeyType == KeyType.TriggerIdentity)).ToList();
            var columnNames = columns.Select(p => GetColumnName(classMap, p, false));

            var sql = new StringBuilder();
            sql.AppendLine($"INSERT INTO {GetTableName(classMap)} ({columnNames.AppendStrings()}) VALUES ");

            foreach (var e in entities)
            {
                var properties = classMap.Properties.Select(p => p.PropertyInfo).ToList();
                if (!properties.Count.Equals(0))
                {
                    if (sql.ToString().Trim().EndsWith("VALUES")) { sql.Append("("); }
                    else { sql.AppendLine(",").Append("("); }

                    properties.ForEach(p =>
                    {
                        if (properties.IndexOf(p) > 0) sql.Append(",");

                        if (p.PropertyType.Name.Equals("String")
                        || p.PropertyType.Name.Equals("DateTime")
                        || p.PropertyType.Name.Equals("Guid")
                        || (p.PropertyType.Name.Equals("Nullable`1") &&
                            !p.PropertyType.GenericTypeArguments.Count().Equals(0) &&
                            p.PropertyType.GenericTypeArguments[0].Name.Equals("DateTime"))
                        || (p.PropertyType.Name.Equals("Nullable`1") &&
                            !p.PropertyType.GenericTypeArguments.Count().Equals(0) &&
                            p.PropertyType.GenericTypeArguments[0].Name.Equals("Guid")))
                        {
                            sql.Append($"'{Convert.ToString(p.GetValue(e) as object).Replace("'", "''")}'");
                        }
                        else if (p.PropertyType.Name.Equals("Boolean"))
                        {
                            sql.Append($"{(Convert.ToBoolean(p.GetValue(e)) ? 1 : 0)}");
                        }
                        else
                        {
                            sql.Append($"{p.GetValue(e) ?? 0}");
                        }
                    });
                    sql.Append(")");
                }
            }

            return sql.ToString();
        }

        public virtual string Insert(IClassMapper classMap)
        {
            var columns = classMap.Properties.Where(p => !(p.Ignored || p.IsReadOnly || p.KeyType == KeyType.Identity || p.KeyType == KeyType.TriggerIdentity));
            if (!columns.Any())
            {
                throw new ArgumentException("No columns were mapped.");
            }

            var columnNames = columns.Select(p => GetColumnName(classMap, p, false));
            var parameters = columns.Select(p => Configuration.Dialect.ParameterPrefix + p.Name);

            string sql = string.Format("INSERT INTO {0} ({1}) VALUES ({2})",
                                       GetTableName(classMap),
                                       columnNames.AppendStrings(),
                                       parameters.AppendStrings());

            var triggerIdentityColumn = classMap.Properties.Where(p => p.KeyType == KeyType.TriggerIdentity).ToList();

            if (triggerIdentityColumn.Count > 0)
            {
                if (triggerIdentityColumn.Count > 1)
                    throw new ArgumentException("TriggerIdentity generator cannot be used with multi-column keys");

                sql += string.Format(" RETURNING {0} INTO {1}IdOutParam", triggerIdentityColumn.Select(p => GetColumnName(classMap, p, false)).First(), Configuration.Dialect.ParameterPrefix);
            }

            return sql;
        }

        public virtual string BulkUpdate(IClassMapper classMap, IEnumerable<IPredicate> predicates, IDictionary<string, object> parameters, bool ignoreAllKeyProperties)
        {
            if (predicates.Count().Equals(0))
            {
                throw new ArgumentNullException("Predicates");
            }

            if (parameters == null)
            {
                throw new ArgumentNullException("Parameters");
            }

            var sql = new StringBuilder();

            var columns = ignoreAllKeyProperties
                ? classMap.Properties.Where(p => !(p.Ignored || p.IsReadOnly) && p.KeyType == KeyType.NotAKey)
                : classMap.Properties.Where(p => !(p.Ignored || p.IsReadOnly || p.KeyType == KeyType.Identity || p.KeyType == KeyType.Assigned));

            if (!columns.Any())
            {
                throw new ArgumentException("No columns were mapped.");
            }

            var predicateList = predicates.ToList();
            predicateList.ForEach(predicate =>
            {
                var setSql =
                    columns.Select(
                        p =>
                        $"{GetColumnName(classMap, p, false)} = {Configuration.Dialect.ParameterPrefix}{p.Name}_{predicateList.IndexOf(predicate)}");

                sql.AppendLine(string.Format("UPDATE {0} SET {1} WHERE {2};",
                    GetTableName(classMap),
                    setSql.AppendStrings(),
                    predicate.GetSql(this, parameters)));
            });

            return sql.ToString();
        }

        public virtual string Update(IClassMapper classMap, IPredicate predicate, IDictionary<string, object> parameters, bool ignoreAllKeyProperties)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException("Predicate");
            }

            if (parameters == null)
            {
                throw new ArgumentNullException("Parameters");
            }

            var columns = ignoreAllKeyProperties
                ? classMap.Properties.Where(p => !(p.Ignored || p.IsReadOnly) && p.KeyType == KeyType.NotAKey)
                : classMap.Properties.Where(p => !(p.Ignored || p.IsReadOnly || p.KeyType == KeyType.Identity || p.KeyType == KeyType.Assigned));

            if (!columns.Any())
            {
                throw new ArgumentException("No columns were mapped.");
            }

            var setSql =
                columns.Select(
                    p =>
                    string.Format(
                        "{0} = {1}{2}", GetColumnName(classMap, p, false), Configuration.Dialect.ParameterPrefix, p.Name));

            return string.Format("UPDATE {0} SET {1} WHERE {2}",
                GetTableName(classMap),
                setSql.AppendStrings(),
                predicate.GetSql(this, parameters));
        }

        public virtual string Delete(IClassMapper classMap, IPredicate predicate, IDictionary<string, object> parameters)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException("Predicate");
            }

            if (parameters == null)
            {
                throw new ArgumentNullException("Parameters");
            }

            StringBuilder sql = new StringBuilder(string.Format("DELETE FROM {0}", GetTableName(classMap)));
            sql.Append(" WHERE ").Append(predicate.GetSql(this, parameters));
            return sql.ToString();
        }

        public virtual string IdentitySql(IClassMapper classMap)
        {
            return Configuration.Dialect.GetIdentitySql(GetTableName(classMap));
        }

        public virtual string GetTableName(IClassMapper map)
        {
            return Configuration.Dialect.GetTableName(map.SchemaName, map.TableName, null);
        }

        public virtual string GetColumnName(IClassMapper map, IPropertyMap property, bool includeAlias)
        {
            string alias = null;
            if (property.ColumnName != property.Name && includeAlias)
            {
                alias = property.Name;
            }

            return Configuration.Dialect.GetColumnName(GetTableName(map), property.ColumnName, alias);
        }

        public virtual string GetColumnName(IClassMapper map, string propertyName, bool includeAlias)
        {
            IPropertyMap propertyMap = map.Properties.SingleOrDefault(p => p.Name.Equals(propertyName, StringComparison.InvariantCultureIgnoreCase));
            if (propertyMap == null)
            {
                throw new ArgumentException(string.Format("Could not find '{0}' in Mapping.", propertyName));
            }

            return GetColumnName(map, propertyMap, includeAlias);
        }

        public virtual bool SupportsMultipleStatements()
        {
            return Configuration.Dialect.SupportsMultipleStatements;
        }

        public virtual string BuildSelectColumns(IClassMapper classMap)
        {
            var columns = classMap.Properties
                .Where(p => !p.Ignored)
                .Select(p => GetColumnName(classMap, p, true));
            return columns.AppendStrings();
        }
    }

    class ClassMapComparer : IEqualityComparer<IClassMapper>
    {
        public bool Equals(IClassMapper x, IClassMapper y)
        {
            //Check whether the compared objects reference the same data.
            if (object.ReferenceEquals(x, y)) return true;

            //Check whether any of the compared objects is null.
            if (object.ReferenceEquals(x, null) || object.ReferenceEquals(y, null))
                return false;

            //Check whether the products' properties are equal.
            return x.SchemaName == y.SchemaName && x.TableName == y.TableName;
        }

        public int GetHashCode(IClassMapper map)
        {
            //Check whether the object is null
            if (object.ReferenceEquals(map, null)) return 0;

            //Get hash code for the Name field if it is not null.
            int hashMapTableName = string.IsNullOrEmpty(map.TableName) ? 0 : map.TableName.GetHashCode();

            //Get hash code for the Code field.
            int hashMapSchemaName = string.IsNullOrEmpty(map.SchemaName) ? 0 : map.SchemaName.GetHashCode();

            //Calculate the hash code for the product.
            return hashMapTableName ^ hashMapSchemaName;
        }
    }
}