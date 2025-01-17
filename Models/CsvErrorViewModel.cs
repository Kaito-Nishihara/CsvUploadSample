namespace CsvUploadSample.Models
{
    /// <summary>
    /// Csvアップロードに伴うエラー表示用ViewModel
    /// </summary>
    public class CsvErrorViewModel
    {
        public CsvErrorViewModel() { }
        public CsvErrorViewModel(int rowNumber, string error, string propertyName = "-")
        {
            RowNumber = rowNumber;
            Error = error;
            PropertyName = propertyName;
        }
        /// <summary>
        /// 行番号
        /// </summary>
        public int RowNumber { get; set; }

        /// <summary>
        /// エラー内容
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// プロパティ名
        /// </summary>
        public string PropertyName { get; set; }
    }
}
