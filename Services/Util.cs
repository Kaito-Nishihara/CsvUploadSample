using CsvHelper;
using System.Globalization;
using System.Text;

namespace CsvUploadSample.Services
{
    public static class Util
    {
        public static string GetFieldName(CsvReader csv, string defaultName = "-")
        {
            if (csv == null)
                throw new ArgumentNullException(nameof(csv), "CsvReader cannot be null.");

            try
            {
                // Ensure the context and header record are valid
                var headerRecord = csv.Context?.Reader?.HeaderRecord;
                if (headerRecord == null || csv.Context.Reader.CurrentIndex < 0 || csv.Context.Reader.CurrentIndex >= headerRecord.Length)
                    return defaultName;

                // Return the field name
                return headerRecord[csv.Context.Reader.CurrentIndex];
            }
            catch (Exception ex)
            {
                // Optionally log the exception if necessary
                Console.WriteLine($"Error retrieving field name: {ex.Message}");
                return defaultName;
            }
        }

        public static CsvReader CreateCsvReader(Stream csvStream, string encoding = "Shift_JIS") =>
new CsvReader(new StreamReader(csvStream, Encoding.GetEncoding(encoding)), CultureInfo.InvariantCulture);

        public static void SetEntityPropertyValues<TEntity>(TEntity entity, Dictionary<string, object> properties)
        {
            foreach (var property in properties)
            {
                var propertyInfo = entity.GetType().GetProperty(property.Key);
                if (propertyInfo != null && propertyInfo.CanWrite)
                {
                    propertyInfo.SetValue(entity, property.Value);
                }
            }
        }
    }
}
