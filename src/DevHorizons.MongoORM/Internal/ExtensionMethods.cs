namespace DevHorizons.MongoORM.Internal
{
    using System.ComponentModel;
    using System.Reflection;

    using MongoDB.Driver;

    using Settings;

    internal static class ExtensionMethods
    {
        /// <summary>
        ///    Gets the custom attribute.
        /// </summary>
        /// <param name="source">The source object.</param>
        /// <typeparam name="T">The type of the return attribute.</typeparam>
        /// <returns>The custom attribute.</returns>
        /// <Created>
        ///    <Author>Ahmad Gad (ahmad.gad@DevHorizons.com)</Author>
        ///    <DateTime>11/02/2020 10:33 AM</DateTime>
        /// </Created>
        internal static T GetCustomAttribute<T>(this object source)
        {
            return Attribute.GetCustomAttribute(source.GetType(), typeof(T), true).To<T>();
        }

        /// <summary>
        ///    Gets the custom attribute.
        /// </summary>
        /// <typeparam name="TSource">The type of the source.</typeparam>
        /// <typeparam name="TAttribute">The type of the attribute.</typeparam>
        /// <returns>The custom attribute.</returns>
        /// <Created>
        ///    <Author>Ahmad Gad (ahmad.gad@DevHorizons.com)</Author>
        ///    <DateTime>11/02/2020 10:33 AM</DateTime>
        /// </Created>
        internal static TAttribute GetCustomAttribute<TSource, TAttribute>()
        {
            return Attribute.GetCustomAttribute(typeof(TSource), typeof(TAttribute), true).To<TAttribute>();
        }

        internal static string GetDescriptionAttributeValue(this Enum source)
        {
            var memberInfo = source.GetType().GetMember(source.ToString()).FirstOrDefault();
            var descriptionAttribute = (DescriptionAttribute)memberInfo.GetCustomAttributes(typeof(DescriptionAttribute), false).FirstOrDefault();
            return descriptionAttribute?.Description;
        }


        internal static string GetLocaleValue(this MongoLocale mongoLocale)
        {
            var value = mongoLocale.GetDescriptionAttributeValue();
            return value ?? "en";
        }

        /// <summary>
        ///      Converts to generic data type based on the generic input data type.
        /// </summary>
        /// <typeparam name="T">The generic input data type.</typeparam>
        /// <param name="source">The source object to be converted.</param>
        /// <returns>Value based on generic data type from generic input data type.</returns>
        /// <Created>
        ///     <Author>Ahmad Gad (ahmad.gad@DevHorizons.com)</Author>
        ///     <DateTime>30/06/2012  06:22 PM</DateTime>
        /// </Created>
        internal static T To<T>(this object source)
        {
            return source.ChangeType<T>();
        }

        /// <summary>
        ///     Convers from a data type to another.
        /// </summary>
        /// <typeparam name="T">The type name.</typeparam>
        /// <param name="source">The source to be converted.</param>
        /// <returns>The converted object.</returns>
        /// <Created>
        ///    <Author>Ahmad Gad (ahmad.gad@DevHorizons.com)</Author>
        ///    <DateTime>30/06/2012  04:29 PM</DateTime>
        /// </Created>
        internal static T ChangeType<T>(this object source)
        {
            return source.ChangeType<T>(default);
        }

        /// <summary>
        ///     Convers from a data type to another.
        /// </summary>
        /// <typeparam name="T">The type name.</typeparam>
        /// <param name="source">The source to be converted.</param>
        /// <param name="defaultValue">The default return value in case of failure.</param>
        /// <returns>The converted object.</returns>
        /// <Created>
        ///    <Author>Ahmad Gad (ahmad.gad@DevHorizons.com)</Author>
        ///    <DateTime>30/06/2012  04:29 PM</DateTime>
        /// </Created>
        internal static T ChangeType<T>(this object source, T defaultValue)
        {
            if (source == null)
            {
                return defaultValue;
            }

            var type = typeof(T);
            return (T)source.ChangeType(type, defaultValue);
        }

        /// <summary>
        ///     Changes the type of specific object.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="type">The type.</param>
        /// <param name="defaultValue">The alternate value that should be returned in case of converted value.</param>
        /// <returns>The converted value of specific object.</returns>
        /// <Created>
        ///    <Author>Ahmad Gad (ahmad.gad@DevHorizons.com)</Author>
        ///    <DateTime>31/08/2013  01:23 AM</DateTime>
        /// </Created>
        internal static object ChangeType(this object source, Type type, object defaultValue)
        {
            try
            {
                if (source == null)
                {
                    return defaultValue;
                }

                var conversionType = Nullable.GetUnderlyingType(type) ?? type;

                if (conversionType.IsEnum)
                {
                    return Enum.Parse(conversionType, source.ToString());
                }

                if (source is IntPtr)
                {
                    source = source.ToString();
                }
                else if (source is byte[])
                {
                    var result = source.ConvertFromArrayOfBytes(conversionType);
                    if (result != null)
                    {
                        return result;
                    }
                }
                else if (source.GetType().BaseType == typeof(Array))
                {
                    if (conversionType.Name == "List`1")
                    {
                        source = source.ConvertFromArrayToList(conversionType);
                    }
                }
                else if (source is System.Collections.IList)
                {
                    if (conversionType.BaseType == typeof(Array))
                    {
                        source = source.ConvertFromListToArray();
                    }
                }
                else if (conversionType.Name == "List`1")
                {
                    if (!source.GetType().IsArray && source.GetType().Name != "List`1")
                    {
                        source = source.ConvertFromObjectToList(conversionType);
                    }
                }
                else if (conversionType.IsArray)
                {
                    if (!source.GetType().IsArray && source.GetType().Name != "List`1")
                    {
                        source = source.ConvertFromObjectToArray();
                    }
                }

#pragma warning disable CA1305 // Specify IFormatProvider
                return Convert.ChangeType(source, conversionType);
#pragma warning restore CA1305 // Specify IFormatProvider
            }
            catch
            {
#pragma warning disable CA1305 // Specify IFormatProvider
                return Convert.ChangeType(defaultValue, TypeCode.Object);
#pragma warning restore CA1305 // Specify IFormatProvider
            }
        }

        internal static bool IsNullOrEmpty(this System.Collections.ICollection collection)
        {
            if (collection is null)
            {
                return true;
            }

            if (collection.Count == 0)
            {
                return true;
            }

            return false;
        }

        internal static bool IsNullOrEmpty<T>(this ICollection<T> collection)
        {
            if (collection is null)
            {
                return true;
            }

            if (collection.Count == 0)
            {
                return true;
            }

            return false;
        }

        internal static bool IsNotNullOrEmpty(this System.Collections.ICollection collection)
        {
            return !collection.IsNullOrEmpty();
        }

        internal static bool IsNotNullOrEmpty<T>(this ICollection<T> collection)
        {
            return !collection.IsNullOrEmpty();
        }

        internal static Collation ToMongoCollation(this MongoCollationSettings mongoCollationSettings)
        {
            if (mongoCollationSettings is null)
            {
                return null;
            }

            var collation = new Collation(
                        mongoCollationSettings.Locale.GetLocaleValue(),
                        mongoCollationSettings.CaseLevel,
                        (CollationCaseFirst)mongoCollationSettings.CollationCaseFirst,
                        (CollationStrength)mongoCollationSettings.CollationStrength);

            return collation;
        }

        #region Private Methods
        /// <summary>
        ///     Converts from array of bytes.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="type">The destination type.</param>
        /// <returns>From Array of bytes.</returns>
        /// <Created>
        ///    <Author>Ahmad Gad (ahmad.gad@DevHorizons.com)</Author>
        ///    <DateTime>15/09/2013  11:38s AM</DateTime>
        /// </Created>
        private static object ConvertFromArrayOfBytes(this object source, Type type)
        {
            var sourceBytes = source as byte[];

            if (type == typeof(Guid))
            {
                return new Guid(sourceBytes);
            }
            else if (type == typeof(string))
            {
                return Convert.ToBase64String(sourceBytes);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        ///     Converts from array to list.
        /// </summary>
        /// <param name="source">The source array.</param>
        /// <param name="type">The type.</param>
        /// <returns>A list from an array.</returns>
        /// <Created>
        ///    <Author>Ahmad Gad (ahmad.gad@DevHorizons.com)</Author>
        ///    <DateTime>15/09/2013  11:38s AM</DateTime>
        /// </Created>
        private static object ConvertFromArrayToList(this object source, Type type)
        {
            var list = Activator.CreateInstance(type);
#pragma warning disable CA1304 // Specify CultureInfo
            list.GetType().InvokeMember("AddRange", BindingFlags.InvokeMethod, null, list, new object[] { source });
#pragma warning restore CA1304 // Specify CultureInfo
            source = list;
            return source;
        }

        /// <summary>
        ///     Converts from list to array.
        /// </summary>
        /// <param name="source">The source list.</param>
        /// <returns>An array from a list.</returns>
        /// <Created>
        ///    <Author>Ahmad Gad (ahmad.gad@DevHorizons.com)</Author>
        ///    <DateTime>15/09/2013  11:38s AM</DateTime>
        /// </Created>
        private static object ConvertFromListToArray(this object source)
        {
            var itemType = source.GetType().GetProperty("Item").PropertyType;
#pragma warning disable CA1305 // Specify IFormatProvider
            var count = Convert.ToInt32(source.GetType().GetProperty("Count").GetValue(source, null));
#pragma warning restore CA1305 // Specify IFormatProvider
            var items = source.GetType().GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(source);
            var array = Array.CreateInstance(itemType, count);
            Array.Copy(items as Array, array, count);
            source = array;
            return source;
        }

        /// <summary>
        ///     Converts from object to list.
        /// </summary>
        /// <param name="source">The source object which should not be an array or a list.</param>
        /// <param name="type">The destination type.</param>
        /// <returns>A list from an object.</returns>
        /// <Created>
        ///    <Author>Ahmad Gad (ahmad.gad@DevHorizons.com)</Author>
        ///    <DateTime>15/09/2013  11:38s AM</DateTime>
        /// </Created>
        private static object ConvertFromObjectToList(this object source, Type type)
        {
            var list = Activator.CreateInstance(type);
#pragma warning disable CA1304 // Specify CultureInfo
            list.GetType().InvokeMember("Add", BindingFlags.InvokeMethod, null, list, new object[] { source });
#pragma warning restore CA1304 // Specify CultureInfo
            source = list;
            return source;
        }

        /// <summary>
        ///     Converts from object to array.
        /// </summary>
        /// <param name="source">The source object which should not be an array or a list.</param>
        /// <returns>An array from an object.</returns>
        /// <Created>
        ///    <Author>Ahmad Gad (ahmad.gad@DevHorizons.com)</Author>
        ///    <DateTime>15/09/2013  11:38s AM</DateTime>
        /// </Created>
        private static object ConvertFromObjectToArray(this object source)
        {
            var array = Array.CreateInstance(source.GetType(), 1);
            array.SetValue(source, 0);
            source = array;
            return source;
        }
        #endregion Private Methods
    }
}
