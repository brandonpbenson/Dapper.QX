﻿using Dapper.QX.Attributes;
using Dapper.QX.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Dapper.QX
{
    public static class QueryUtil
    {
        public const string OrderByToken = "{orderBy}";
        public const string JoinToken = "{join}";
        public const string WhereToken = "{where}";
        public const string AndWhereToken = "{andWhere}";
        public const string OffsetToken = "{offset}";

        public static string ResolveSql(string sql, object parameters, out DynamicParameters dynamicParams)
        {
            if (sql is null)
            {
                throw new ArgumentNullException(nameof(sql));
            }

            if (parameters == null)
            {
                dynamicParams = null;
                return RegexHelper.RemovePlaceholders(sql);
            }

            string result = sql;

            var properties = GetParamProperties(parameters, sql, out QueryParameters paramInfo);            
            dynamicParams = GetDynamicParameters(properties, parameters);

            string queryTypeName = parameters.GetType().Name;

            result = ResolveOptionalCriteria(result, properties, parameters, paramInfo);
            result = ResolveOrderBy(result, parameters, queryTypeName);
            result = ResolveOptionalJoins(result, parameters);
            result = ResolveWhereClause(result, properties, parameters);
            result = ResolveOffset(result, parameters, queryTypeName);            
            result = RegexHelper.RemovePlaceholders(result);

            return result;  
        }

        private static string ResolveOffset(string sql, object parameters, string queryTypeName)
        {
            string result = sql;

            if (result.Contains(OffsetToken) && FindOffsetProperty(parameters, out PropertyInfo offsetProperty))
            {
                var offsetAttr = offsetProperty.GetCustomAttribute<OffsetAttribute>();
                int page = (int)offsetProperty.GetValue(parameters);
                result = result.Replace(OffsetToken, offsetAttr.GetOffsetFetchSyntax(page));
            }

            return result;
        }

        private static bool FindOffsetProperty(object queryObject, out PropertyInfo offsetProperty)
        {
            offsetProperty = queryObject.GetType().GetProperties().FirstOrDefault(pi => pi.HasAttribute<OffsetAttribute>() && pi.PropertyType.Equals(typeof(int?)));
            if (offsetProperty != null)
            {
                object value = offsetProperty.GetValue(queryObject);
                return (value != null);
            }

            return false;
        }

        private static string ResolveWhereClause(string sql, IEnumerable<PropertyInfo> properties, object parameters)
        {
            string result = sql;

            if (result.ContainsAnyOf(new string[] { WhereToken, AndWhereToken }, out string token))
            {

            }

            return result;
        }

        private static string ResolveOptionalJoins(string sql, object parameters)
        {
            var joinTerms = parameters.GetType().GetProperties()
               .Where(pi => pi.HasAttribute<JoinAttribute>() && pi.GetValue(parameters).Equals(true))
               .Select(pi => pi.GetAttribute<JoinAttribute>().Sql);

            return sql.Replace(JoinToken, string.Join("\r\n", joinTerms));
        }

        private static string ResolveOrderBy(string sql, object parameters, string typeName)
        {
            if (sql.Contains(OrderByToken))
            {
                var orderByProp = parameters.GetType().GetProperties()
                   .FirstOrDefault(pi => pi.HasAttribute<OrderByAttribute>() && HasValue(pi, parameters));

                if (orderByProp == null)
                {
                    throw new Exception($"Query {typeName} has an {{orderBy}} token, but no corresponding property with the [OrderBy] attribute.");
                }

                var value = orderByProp.GetValue(parameters);
                var sortOptions = orderByProp.GetCustomAttributes<OrderByAttribute>();
                var selectedSort = sortOptions.FirstOrDefault(a => a.Value.Equals(value));

                if (selectedSort == null) throw new Exception($"Query order by property {orderByProp.Name} had no matching case for value {value}");

                return sql.Replace(OrderByToken, selectedSort.Expression);
            }

            return sql;
        }

        private static string ResolveOptionalCriteria(string input, IEnumerable<PropertyInfo> properties, object parameters, QueryParameters paramInfo)
        {
            string result = input;
            foreach (var optional in paramInfo.Optional)
            {
                if (AllParametersSet(properties, parameters, optional.ParameterNames))
                {
                    result = result.Replace(optional.Token, optional.Content);
                }
                else
                {
                    // if the param object does not specify a property for this token, then remove the token from the SQL
                    result = result.Replace(optional.Token, string.Empty);
                }
            }
            return result;
        }

        private static bool AllParametersSet(IEnumerable<PropertyInfo> properties, object parameters, string[] parameterNames)
        {                        
            var paramPropertyMap = 
                (from name in parameterNames
                join pi in properties on name equals pi.Name
                select new
                {
                    Name = name.ToLower(),
                    PropertyInfo = pi
                }).ToDictionary(row => row.Name, row => row.PropertyInfo);

            return parameterNames.All(p => HasValue(paramPropertyMap[p.ToLower()], parameters));
        }

        private static bool HasValue(PropertyInfo propertyInfo, object @object, out object value)
        {
            value = propertyInfo.GetValue(@object);
            if (value != null)
            {
                if (value.Equals(string.Empty)) return false;
                return true;
            }
            return false;
        }

        private static bool HasValue(PropertyInfo propertyInfo, object @object)
        {
            return HasValue(propertyInfo, @object, out object value);
        }

        private static DynamicParameters GetDynamicParameters(IEnumerable<PropertyInfo> properties, object parameters)
        {
            var result = new DynamicParameters();
            foreach (var prop in properties)
            {
                if (HasValue(prop, parameters, out object value) && !prop.HasAttribute<PhraseAttribute>()) result.Add(prop.Name, value);
            }
            return result;
        }

        /// <summary>
        /// Returns the properties of a query object based on parameters defined in a
        /// SQL statement as well as properties with Where and Case attributes
        /// </summary>
        private static IEnumerable<PropertyInfo> GetParamProperties(object parameters, string sql, out QueryParameters paramInfo)
        {
            // this gets the param names within the query based on words with leading '@'
            paramInfo = RegexHelper.ParseParameters(sql, cleaned: true);
            
            var allParams = paramInfo.AllParamNames().Select(p => p.ToLower()).ToArray();            

            // these are the properties of the Query that are explicitly defined and may impact the WHERE clause
            var queryProps = parameters.GetType().GetProperties().Where(pi =>
                pi.HasAttribute<WhereAttribute>() ||
                pi.HasAttribute<CaseAttribute>() ||
                pi.HasAttribute<PhraseAttribute>() ||
                pi.HasAttribute<ParameterAttribute>() ||
                allParams.Contains(pi.Name.ToLower()));

            return queryProps;
        }
    }
}